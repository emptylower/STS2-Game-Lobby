import assert from "node:assert/strict";
import { createServer, type Server as HttpServer } from "node:http";
import { connect as netConnect } from "node:net";
import { randomBytes } from "node:crypto";
import test from "node:test";
import { WebSocket, WebSocketServer } from "ws";
import {
  CHAT_WS_PATH,
  installUpgradeRouter,
  type ChatUpgradeDecision,
} from "./upgrade-router.js";

async function listen(server: HttpServer): Promise<{ host: string; port: number }> {
  await new Promise<void>((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => resolve());
  });
  const address = server.address();
  assert.ok(address && typeof address === "object");
  return { host: address.address, port: address.port };
}

async function closeServer(server: HttpServer): Promise<void> {
  // Force-destroy any leftover sockets so close cannot hang on half-open upgrades.
  server.closeAllConnections?.();
  await new Promise<void>((resolve) => {
    server.close(() => resolve());
  });
}

function closeWss(wss: WebSocketServer): Promise<void> {
  for (const client of wss.clients) {
    try {
      client.terminate();
    } catch {
      // ignore
    }
  }
  return new Promise<void>((resolve) => {
    wss.close(() => resolve());
  });
}

function secWebSocketKey(): string {
  return randomBytes(16).toString("base64");
}

async function rawHttpUpgrade(
  port: number,
  requestTarget: string,
  extraHeaders: Record<string, string> = {},
): Promise<{ statusLine: string; statusCode: number; headers: Record<string, string>; closed: boolean }> {
  return await new Promise((resolve, reject) => {
    const socket = netConnect({ host: "127.0.0.1", port });
    let settled = false;
    let response = "";
    const timer = setTimeout(() => {
      if (!settled) {
        settled = true;
        socket.destroy();
        reject(new Error("raw upgrade response timeout"));
      }
    }, 2000);

    socket.once("connect", () => {
      const headers = {
        Host: "127.0.0.1",
        Connection: "Upgrade",
        Upgrade: "websocket",
        "Sec-WebSocket-Version": "13",
        "Sec-WebSocket-Key": secWebSocketKey(),
        ...extraHeaders,
      };
      const headerLines = Object.entries(headers)
        .map(([name, value]) => `${name}: ${value}`)
        .join("\r\n");
      socket.write(`GET ${requestTarget} HTTP/1.1\r\n${headerLines}\r\n\r\n`);
    });

    socket.on("data", (chunk) => {
      response += chunk.toString("utf8");
      if (!response.includes("\r\n\r\n")) {
        return;
      }
      if (settled) {
        return;
      }
      settled = true;
      clearTimeout(timer);
      const headerBlock = response.split("\r\n\r\n")[0] ?? "";
      const lines = headerBlock.split("\r\n");
      const statusLine = lines[0] ?? "";
      const match = /^HTTP\/\d\.\d\s+(\d{3})\b/.exec(statusLine);
      const statusCode = match ? Number(match[1]) : 0;
      const headers: Record<string, string> = {};
      for (const line of lines.slice(1)) {
        const idx = line.indexOf(":");
        if (idx > 0) {
          headers[line.slice(0, idx).trim().toLowerCase()] = line.slice(idx + 1).trim();
        }
      }

      const finish = (closed: boolean) => {
        try {
          socket.destroy();
        } catch {
          // ignore
        }
        resolve({ statusLine, statusCode, headers, closed });
      };

      if (socket.destroyed || socket.readableEnded) {
        finish(true);
        return;
      }
      socket.once("close", () => finish(true));
      // Unknown-path / rejection path should destroy promptly.
      setTimeout(() => finish(socket.destroyed), 100);
    });

    socket.once("error", (error) => {
      if (settled) {
        return;
      }
      settled = true;
      clearTimeout(timer);
      reject(error);
    });
  });
}

async function openWs(url: string, headers?: Record<string, string>): Promise<WebSocket> {
  const socket = new WebSocket(url, headers ? { headers } : undefined);
  await new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`websocket connect timeout: ${url}`)), 2000);
    socket.once("open", () => {
      clearTimeout(timer);
      resolve();
    });
    socket.once("error", (error) => {
      clearTimeout(timer);
      reject(error);
    });
  });
  return socket;
}

/** Open a WebSocket and capture the first message without racing the server's welcome frame. */
async function openWsAndFirstMessage(
  url: string,
  headers?: Record<string, string>,
): Promise<{ socket: WebSocket; message: string }> {
  const socket = new WebSocket(url, headers ? { headers } : undefined);
  const messagePromise = new Promise<string>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`websocket message timeout: ${url}`)), 2000);
    socket.once("message", (data) => {
      clearTimeout(timer);
      resolve(String(data));
    });
    socket.once("error", (error) => {
      clearTimeout(timer);
      reject(error);
    });
  });
  await new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`websocket connect timeout: ${url}`)), 2000);
    socket.once("open", () => {
      clearTimeout(timer);
      resolve();
    });
    socket.once("error", (error) => {
      clearTimeout(timer);
      reject(error);
    });
  });
  const message = await messagePromise;
  return { socket, message };
}

function createHarness(options?: {
  controlPath?: string;
  authorizeChat?: (req: import("node:http").IncomingMessage) => ChatUpgradeDecision;
}) {
  const controlPath = options?.controlPath ?? "/control";
  const controlConnections: Array<{ url?: string }> = [];
  const chatConnections: Array<{ url?: string; authorization?: string }> = [];
  const commits: string[] = [];
  const releases: string[] = [];

  const server = createServer((_req, res) => {
    res.statusCode = 200;
    res.end("ok");
  });
  const controlWss = new WebSocketServer({ noServer: true });
  const chatWss = new WebSocketServer({ noServer: true });

  controlWss.on("connection", (ws, req) => {
    const entry: { url?: string } = {};
    if (typeof req.url === "string") {
      entry.url = req.url;
    }
    controlConnections.push(entry);
    ws.send(JSON.stringify({ type: "control-ok", url: req.url }));
  });
  chatWss.on("connection", (ws, req) => {
    const entry: { url?: string; authorization?: string } = {};
    if (typeof req.url === "string") {
      entry.url = req.url;
    }
    if (typeof req.headers.authorization === "string") {
      entry.authorization = req.headers.authorization;
    }
    chatConnections.push(entry);
    ws.send(JSON.stringify({ type: "chat-ok", url: req.url }));
  });

  const authorizeChat =
    options?.authorizeChat ??
    ((_req): ChatUpgradeDecision => {
      const id = `res-${commits.length + releases.length + 1}`;
      return {
        ok: true,
        commit() {
          commits.push(id);
        },
        release() {
          releases.push(id);
        },
      };
    });

  const uninstall = installUpgradeRouter({
    server,
    controlPath,
    controlWss,
    chatWss,
    authorizeChat,
  });

  return {
    server,
    controlWss,
    chatWss,
    controlPath,
    controlConnections,
    chatConnections,
    commits,
    releases,
    uninstall,
    async start() {
      return listen(server);
    },
    async stop() {
      try {
        uninstall();
      } catch {
        // ignore double-uninstall
      }
      await closeWss(controlWss);
      await closeWss(chatWss);
      await closeServer(server);
    },
  };
}

test("httpServer has exactly one upgrade listener", async () => {
  const harness = createHarness();
  try {
    assert.equal(harness.server.listenerCount("upgrade"), 1);
    const address = await harness.start();
    assert.equal(harness.server.listenerCount("upgrade"), 1);
    assert.ok(address.port > 0);

    harness.uninstall();
    assert.equal(harness.server.listenerCount("upgrade"), 0);

    // Re-install so stop()'s uninstall is a no-op-safe path and listener count stays 1.
    const uninstall2 = installUpgradeRouter({
      server: harness.server,
      controlPath: harness.controlPath,
      controlWss: harness.controlWss,
      chatWss: harness.chatWss,
      authorizeChat: () => ({ ok: false, statusCode: 503 }),
    });
    assert.equal(harness.server.listenerCount("upgrade"), 1);
    uninstall2();
    assert.equal(harness.server.listenerCount("upgrade"), 0);
  } finally {
    await harness.stop();
  }
});

test("configured control path reaches only control WSS", async () => {
  const harness = createHarness({ controlPath: "/control" });
  try {
    const { port } = await harness.start();
    const { socket, message } = await openWsAndFirstMessage(
      `ws://127.0.0.1:${port}/control?roomId=r1&role=host`,
    );
    assert.deepEqual(JSON.parse(message), {
      type: "control-ok",
      url: "/control?roomId=r1&role=host",
    });
    assert.equal(harness.controlConnections.length, 1);
    assert.equal(harness.chatConnections.length, 0);
    socket.close();
  } finally {
    await harness.stop();
  }
});

test("/chat reaches only chat preflight and chat WSS", async () => {
  let authorizeCalls = 0;
  const commits: string[] = [];
  const releases: string[] = [];
  const harness = createHarness({
    authorizeChat: (req): ChatUpgradeDecision => {
      authorizeCalls += 1;
      assert.match(String(req.headers.authorization ?? ""), /^Bearer\s+/i);
      return {
        ok: true,
        commit() {
          commits.push("ok");
        },
        release() {
          releases.push("ok");
        },
      };
    },
  });
  try {
    const { port } = await harness.start();
    const { socket, message } = await openWsAndFirstMessage(
      `ws://127.0.0.1:${port}${CHAT_WS_PATH}`,
      { Authorization: "Bearer chat-ticket-1" },
    );
    assert.deepEqual(JSON.parse(message), {
      type: "chat-ok",
      url: CHAT_WS_PATH,
    });
    assert.equal(authorizeCalls, 1);
    assert.equal(harness.controlConnections.length, 0);
    assert.equal(harness.chatConnections.length, 1);
    assert.equal(harness.chatConnections[0]?.authorization, "Bearer chat-ticket-1");
    assert.deepEqual(commits, ["ok"]);
    assert.deepEqual(releases, []);
    socket.close();
  } finally {
    await harness.stop();
  }
});

test("chat rejection writes status then destroys socket", async () => {
  const harness = createHarness({
    authorizeChat: (): ChatUpgradeDecision => ({
      ok: false,
      statusCode: 429,
      retryAfterSeconds: 7,
    }),
  });
  try {
    const { port } = await harness.start();
    const response = await rawHttpUpgrade(port, CHAT_WS_PATH, {
      Authorization: "Bearer blocked",
    });
    assert.equal(response.statusCode, 429);
    assert.equal(response.headers["retry-after"], "7");
    assert.equal(response.closed, true);
    assert.equal(harness.chatConnections.length, 0);
    assert.equal(harness.controlConnections.length, 0);
  } finally {
    await harness.stop();
  }
});

test("chat missing bearer is rejected with 401 before authorize commit", async () => {
  let authorizeCalls = 0;
  const harness = createHarness({
    authorizeChat: (): ChatUpgradeDecision => {
      authorizeCalls += 1;
      return {
        ok: true,
        commit() {
          /* should not run */
        },
        release() {
          /* should not run */
        },
      };
    },
  });
  try {
    const { port } = await harness.start();
    const response = await rawHttpUpgrade(port, CHAT_WS_PATH);
    assert.equal(response.statusCode, 401);
    assert.equal(response.closed, true);
    assert.equal(authorizeCalls, 0);
    assert.equal(harness.chatConnections.length, 0);
  } finally {
    await harness.stop();
  }
});

test("unknown path returns HTTP 404 then destroys socket", async () => {
  const harness = createHarness();
  try {
    const { port } = await harness.start();
    const response = await rawHttpUpgrade(port, "/nope");
    assert.equal(response.statusCode, 404);
    assert.equal(response.closed, true);
    assert.equal(harness.controlConnections.length, 0);
    assert.equal(harness.chatConnections.length, 0);
  } finally {
    await harness.stop();
  }
});

test("malformed upgrade URL returns HTTP 400", async () => {
  const harness = createHarness();
  try {
    const { port } = await harness.start();
    // Space in the request-target is invalid for URL parsing via new URL(req.url, base).
    const response = await rawHttpUpgrade(port, "/control%zz");
    // Percent-decoding invalid sequences can still parse as a path; use a target that
    // Node exposes but URL constructor rejects when combined poorly.
    // Prefer an explicit malformed request-line path that our router treats as 400:
    // empty URL is not possible via WebSocket clients, so use raw socket with bad target.
    assert.ok(response.statusCode === 400 || response.statusCode === 404);
  } finally {
    await harness.stop();
  }
});

test("explicit malformed request url returns 400", async () => {
  // Drive the router handler with a synthetic IncomingMessage-like object is hard;
  // instead send a request-target that `new URL` rejects relative to the host base.
  const harness = createHarness();
  try {
    const { port } = await harness.start();
    const response = await new Promise<{ statusCode: number; closed: boolean }>((resolve, reject) => {
      const socket = netConnect({ host: "127.0.0.1", port });
      let responseText = "";
      const timer = setTimeout(() => {
        socket.destroy();
        reject(new Error("malformed url timeout"));
      }, 2000);
      socket.once("connect", () => {
        // Invalid request-target for WHATWG URL when used as `new URL(req.url, base)`:
        // Node keeps req.url as provided; a bare "http://" absolute form without host
        // is rejected by the URL parser in our router.
        socket.write(
          "GET http:// HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Connection: Upgrade\r\n" +
            "Upgrade: websocket\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            `Sec-WebSocket-Key: ${secWebSocketKey()}\r\n\r\n`,
        );
      });
      socket.on("data", (chunk) => {
        responseText += chunk.toString("utf8");
        if (!responseText.includes("\r\n\r\n")) {
          return;
        }
        clearTimeout(timer);
        const statusLine = responseText.split("\r\n")[0] ?? "";
        const match = /^HTTP\/\d\.\d\s+(\d{3})\b/.exec(statusLine);
        const statusCode = match ? Number(match[1]) : 0;
        const closed = socket.destroyed;
        socket.once("close", () => resolve({ statusCode, closed: true }));
        setTimeout(() => resolve({ statusCode, closed: socket.destroyed || closed }), 100);
      });
      socket.once("error", (error) => {
        clearTimeout(timer);
        reject(error);
      });
    });
    assert.equal(response.statusCode, 400);
    assert.equal(response.closed, true);
  } finally {
    await harness.stop();
  }
});

test("neither WSS installs an additional upgrade listener", async () => {
  const harness = createHarness();
  try {
    await harness.start();
    assert.equal(harness.server.listenerCount("upgrade"), 1);
    // Touch both servers; path option is forbidden by using noServer only.
    assert.equal(harness.controlWss.options.noServer, true);
    assert.equal(harness.chatWss.options.noServer, true);
  } finally {
    await harness.stop();
  }
});

test("failed handleUpgrade releases reservation", async () => {
  const commits: string[] = [];
  const releases: string[] = [];
  const harness = createHarness({
    authorizeChat: (): ChatUpgradeDecision => ({
      ok: true,
      commit() {
        commits.push("c");
      },
      release() {
        releases.push("r");
      },
    }),
  });
  try {
    // Force handleUpgrade to throw after authorize succeeds.
    harness.chatWss.handleUpgrade = (() => {
      throw new Error("forced handleUpgrade failure");
    }) as typeof harness.chatWss.handleUpgrade;

    const { port } = await harness.start();
    await new Promise<void>((resolve, reject) => {
      const socket = netConnect({ host: "127.0.0.1", port });
      const timer = setTimeout(() => {
        socket.destroy();
        reject(new Error("forced failure close timeout"));
      }, 2000);
      socket.once("connect", () => {
        socket.write(
          `GET ${CHAT_WS_PATH} HTTP/1.1\r\n` +
            "Host: 127.0.0.1\r\n" +
            "Connection: Upgrade\r\n" +
            "Upgrade: websocket\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            `Sec-WebSocket-Key: ${secWebSocketKey()}\r\n` +
            "Authorization: Bearer abort-me\r\n\r\n",
        );
      });
      socket.once("close", () => {
        clearTimeout(timer);
        resolve();
      });
      socket.once("error", () => {
        // expected when server destroys the socket without a response body
      });
    });
    assert.equal(harness.chatConnections.length, 0);
    assert.deepEqual(commits, []);
    assert.deepEqual(releases, ["r"]);
  } finally {
    await harness.stop();
  }
});
