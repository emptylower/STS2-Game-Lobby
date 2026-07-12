import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { connect as netConnect } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { setTimeout as sleep } from "node:timers/promises";
import { inspect } from "node:util";
import { WebSocket } from "ws";
import { createLobbyService } from "./app.js";
import { loadLobbyServiceConfig, type LobbyServiceConfig } from "./config.js";

function testConfig(overrides: Partial<LobbyServiceConfig> = {}): LobbyServiceConfig {
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-lobby-app-"));
  const base = loadLobbyServiceConfig({
    HOST: "127.0.0.1",
    PORT: "0",
    PEER_NETWORK_ENABLED: "false",
    PEER_SELF_ADDRESS: "",
    PEER_CF_DISCOVERY_BASE_URL: "",
    SERVER_ADMIN_STATE_FILE: join(tempDir, "server-admin.json"),
    PEER_STATE_DIR: join(tempDir, "peer"),
    ENFORCE_LOBBY_ACCESS_TOKEN: "false",
    ENFORCE_CREATE_ROOM_TOKEN: "false",
  });

  return {
    ...base,
    ...overrides,
    ...(overrides.lobbyAccessToken != null && overrides.chatAccessToken == null
      ? { chatAccessToken: overrides.lobbyAccessToken }
      : {}),
    peer: {
      ...base.peer,
      ...(overrides.peer ?? {}),
    },
    chat: {
      ...base.chat,
      ...(overrides.chat ?? {}),
    },
  };
}

function cleanupTempDir(config: LobbyServiceConfig) {
  try {
    rmSync(join(config.serverAdminStateFile, ".."), { recursive: true, force: true });
  } catch {
    // ignore cleanup failures
  }
}

test("factory does not listen until start and closes all resources", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  assert.equal(service.httpServer.listening, false);
  const address = await service.start();
  assert.ok(address.port > 0);
  await service.close();
  assert.equal(service.httpServer.listening, false);
  cleanupTempDir(config);
});

test("factory construction does not start relay cleanup interval before start", async () => {
  const config = testConfig({ port: 0 });
  const createdIntervals: NodeJS.Timeout[] = [];
  const realSetInterval = globalThis.setInterval;
  // Track intervals created during factory construction; factory must not start any.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).setInterval = ((...args: Parameters<typeof setInterval>) => {
    const timer = realSetInterval(...args);
    createdIntervals.push(timer);
    return timer;
  }) as typeof setInterval;

  let service: Awaited<ReturnType<typeof createLobbyService>> | undefined;
  try {
    service = await createLobbyService(config);
    assert.equal(
      createdIntervals.length,
      0,
      `expected no intervals before start(), got ${createdIntervals.length}`,
    );

    const address = await service.start();
    assert.ok(address.port > 0);
    assert.ok(
      createdIntervals.length > 0,
      "expected start() to create service intervals (cleanup / relay)",
    );
  } finally {
    (globalThis as typeof globalThis & { setInterval: typeof setInterval }).setInterval = realSetInterval;
    for (const timer of createdIntervals) {
      clearInterval(timer);
    }
    if (service) {
      await service.close();
    }
    cleanupTempDir(config);
  }
});

test("close terminates active control-channel sockets and finishes promptly", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const address = await service.start();

  const createResponse = await fetch(`http://127.0.0.1:${address.port}/rooms`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      roomName: "close-test",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.0.0",
      modVersion: "1.0.0",
      modList: [],
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 7777,
        localAddresses: ["127.0.0.1"],
      },
    }),
  });
  assert.equal(createResponse.status, 201);
  const created = (await createResponse.json()) as {
    roomId: string;
    hostToken: string;
    controlChannelId: string;
  };

  const wsUrl =
    `ws://127.0.0.1:${address.port}${config.wsPath}` +
    `?roomId=${encodeURIComponent(created.roomId)}` +
    `&controlChannelId=${encodeURIComponent(created.controlChannelId)}` +
    `&role=host` +
    `&token=${encodeURIComponent(created.hostToken)}`;

  const socket = new WebSocket(wsUrl);
  await new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("websocket connect timeout")), 2000);
    socket.once("open", () => {
      clearTimeout(timer);
      resolve();
    });
    socket.once("error", (error) => {
      clearTimeout(timer);
      reject(error);
    });
  });

  try {
    await Promise.race([
      service.close(),
      sleep(1500).then(() => {
        throw new Error("close() did not complete within 1500ms while control WS stayed open");
      }),
    ]);
    assert.equal(service.httpServer.listening, false);
  } finally {
    try {
      socket.terminate();
    } catch {
      // ignore
    }
    cleanupTempDir(config);
  }
});

test("post-listen start failure tears down and allows a later successful start", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-lobby-partial-start-"));
  const badPeerStatePath = join(tempDir, "peer-state-as-file");
  // Force identity/bootstrap path to throw after HTTP listen succeeds.
  writeFileSync(badPeerStatePath, "not-a-directory");

  const failingConfig = testConfig({
    port: 0,
    peer: {
      enabled: true,
      selfAddress: "http://127.0.0.1:9",
      stateDir: badPeerStatePath,
      cfDiscoveryBaseUrl: "",
      displayNameOverride: "",
    },
  });

  const failingService = await createLobbyService(failingConfig);
  await assert.rejects(() => failingService.start(), /./);
  assert.equal(
    failingService.httpServer.listening,
    false,
    "failed start must not leave HTTP server listening",
  );

  // Same instance must not report a sticky successful start while half-initialized.
  await assert.rejects(() => failingService.start(), /./);
  assert.equal(failingService.httpServer.listening, false);

  await Promise.race([
    failingService.close(),
    sleep(1500).then(() => {
      throw new Error("close() after failed start hung");
    }),
  ]);

  const recoveryConfig = testConfig({ port: 0 });
  const recoveryService = await createLobbyService(recoveryConfig);
  try {
    const address = await recoveryService.start();
    assert.ok(address.port > 0);
    const probe = await fetch(`http://127.0.0.1:${address.port}/probe`);
    assert.equal(probe.status, 200);
    await recoveryService.close();
    assert.equal(recoveryService.httpServer.listening, false);
  } finally {
    cleanupTempDir(failingConfig);
    cleanupTempDir(recoveryConfig);
    try {
      rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  }
});

test("close finishes promptly while an HTTP keep-alive connection is open", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const address = await service.start();

  // Hold a real keep-alive TCP connection from outside Node's Agent lifecycle so
  // server.close() would otherwise wait until the idle timeout.
  const socket = netConnect({ host: "127.0.0.1", port: address.port });
  await new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("keep-alive connect timeout")), 2000);
    socket.once("error", (error) => {
      clearTimeout(timer);
      reject(error);
    });
    socket.once("connect", () => {
      socket.write(
        "GET /probe HTTP/1.1\r\n" +
          "Host: 127.0.0.1\r\n" +
          "Connection: keep-alive\r\n" +
          "\r\n",
      );
    });
    let buffer = "";
    socket.on("data", (chunk) => {
      buffer += chunk.toString("utf8");
      if (buffer.includes("\r\n\r\n")) {
        clearTimeout(timer);
        resolve();
      }
    });
  });

  assert.equal(socket.destroyed, false, "expected keep-alive socket to remain open");

  try {
    await Promise.race([
      service.close(),
      sleep(1500).then(() => {
        throw new Error("close() did not complete within 1500ms with keep-alive HTTP open");
      }),
    ]);
    assert.equal(service.httpServer.listening, false);
  } finally {
    try {
      socket.destroy();
    } catch {
      // ignore
    }
    cleanupTempDir(config);
  }
});

const PHASE1_PROBE_CAPABILITIES = {
  serverChatVersion: 1,
  roomChatProtocolVersion: 0,
  richContentVersion: 0,
  emojiSetVersion: 0,
  itemRefVersion: 0,
  combatRefVersion: 0,
  maxMessageChars: 300,
  maxSegments: 32,
  maxEntities: 0,
  historyLimit: 50,
} as const;

test("GET /probe returns exact phase-1 chat capabilities", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const probe = await fetch(`http://127.0.0.1:${address.port}/probe`);
    assert.equal(probe.status, 200);
    assert.deepEqual(await probe.json(), {
      ok: true,
      capabilities: PHASE1_PROBE_CAPABILITIES,
    });

    const health = await fetch(`http://127.0.0.1:${address.port}/health`);
    assert.equal(health.status, 200);
    const healthBody = (await health.json()) as Record<string, unknown>;
    assert.equal(healthBody.ok, true);
    assert.equal("capabilities" in healthBody, false);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("GET /probe keeps phase-1 historyLimit fixed when snapshot config changes", async () => {
  const base = testConfig({ port: 0 });
  const config: LobbyServiceConfig = {
    ...base,
    chat: { ...base.chat, snapshotLimit: 7 },
  };
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const probe = await fetch(`http://127.0.0.1:${address.port}/probe`);
    assert.equal(probe.status, 200);
    const body = (await probe.json()) as { capabilities: { historyLimit: number } };
    assert.equal(body.capabilities.historyLimit, 50);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("GET /peers/health does not supply negotiation capabilities", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-lobby-probe-peers-"));
  const config = testConfig({
    port: 0,
    peer: {
      enabled: true,
      selfAddress: "http://127.0.0.1:9",
      stateDir: join(tempDir, "peer"),
      cfDiscoveryBaseUrl: "",
      displayNameOverride: "",
    },
  });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await fetch(
      `http://127.0.0.1:${address.port}/peers/health?challenge=probe-capability-check`,
    );
    assert.equal(response.status, 200);
    const body = (await response.json()) as Record<string, unknown>;
    assert.equal("capabilities" in body, false);
    assert.equal(typeof body.publicKey, "string");
  } finally {
    await service.close();
    cleanupTempDir(config);
    try {
      rmSync(tempDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  }
});

function ticketBody(overrides: Record<string, unknown> = {}) {
  return {
    protocolVersion: 1,
    playerNetId: "net-player-1",
    playerName: "Ironclad",
    ...overrides,
  };
}

async function postChatTicket(
  port: number,
  options: {
    headers?: Record<string, string>;
    body?: unknown;
  } = {},
) {
  return fetch(`http://127.0.0.1:${port}/chat/tickets`, {
    method: "POST",
    headers: {
      "content-type": "application/json",
      ...(options.headers ?? {}),
    },
    body: JSON.stringify(options.body ?? ticketBody()),
  });
}

type ChatFrame = Record<string, unknown> & { type: string };
const bufferedChatFrames = new WeakMap<WebSocket, ChatFrame[]>();

function waitForChatFrame(
  socket: WebSocket,
  predicate: (frame: ChatFrame) => boolean,
  timeoutMs = 2_000,
): Promise<ChatFrame> {
  const buffered = bufferedChatFrames.get(socket) ?? [];
  const bufferedIndex = buffered.findIndex(predicate);
  if (bufferedIndex >= 0) {
    return Promise.resolve(buffered.splice(bufferedIndex, 1)[0]!);
  }
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      cleanup();
      reject(new Error("chat websocket frame timeout"));
    }, timeoutMs);
    const onMessage = (data: WebSocket.RawData) => {
      let frame: ChatFrame;
      try {
        frame = JSON.parse(data.toString()) as ChatFrame;
      } catch {
        return;
      }
      if (predicate(frame)) {
        const queuedIndex = buffered.indexOf(frame);
        if (queuedIndex >= 0) {
          buffered.splice(queuedIndex, 1);
        }
        cleanup();
        resolve(frame);
      }
    };
    const onClose = (code: number) => {
      cleanup();
      reject(new Error(`chat websocket closed before frame (${code})`));
    };
    const cleanup = () => {
      clearTimeout(timer);
      socket.off("message", onMessage);
      socket.off("close", onClose);
    };
    socket.on("message", onMessage);
    socket.once("close", onClose);
  });
}

function waitForChatClose(
  socket: WebSocket,
  timeoutMs = 2_000,
): Promise<{ code: number; reason: string }> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      cleanup();
      reject(new Error("chat websocket close timeout"));
    }, timeoutMs);
    const onClose = (code: number, reason: Buffer) => {
      cleanup();
      resolve({ code, reason: reason.toString("utf8") });
    };
    const cleanup = () => {
      clearTimeout(timer);
      socket.off("close", onClose);
    };
    socket.once("close", onClose);
  });
}

function openChatWebSocket(
  url: string,
  ticket: string,
  headers: Record<string, string> = {},
): Promise<WebSocket> {
  const socket = new WebSocket(url, {
    headers: { authorization: `Bearer ${ticket}`, ...headers },
  });
  const buffered: ChatFrame[] = [];
  bufferedChatFrames.set(socket, buffered);
  socket.on("message", (data) => {
    try {
      buffered.push(JSON.parse(data.toString()) as ChatFrame);
    } catch {
      // Individual waiters ignore non-JSON frames as well.
    }
  });
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      socket.terminate();
      reject(new Error("chat websocket connect timeout"));
    }, 2_000);
    socket.once("open", () => {
      clearTimeout(timer);
      resolve(socket);
    });
    socket.once("unexpected-response", (_request, response) => {
      clearTimeout(timer);
      response.resume();
      reject(new Error(`chat websocket upgrade rejected (${response.statusCode})`));
    });
    socket.once("error", (error) => {
      clearTimeout(timer);
      reject(error);
    });
  });
}

async function rejectedChatWebSocketStatus(
  url: string,
  headers: Record<string, string> = {},
): Promise<number> {
  const socket = new WebSocket(url, { headers });
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      socket.terminate();
      reject(new Error("chat websocket rejection timeout"));
    }, 2_000);
    socket.once("open", () => {
      clearTimeout(timer);
      socket.terminate();
      reject(new Error("chat websocket unexpectedly upgraded"));
    });
    socket.once("unexpected-response", (_request, response) => {
      clearTimeout(timer);
      const status = response.statusCode ?? 0;
      response.resume();
      resolve(status);
    });
    socket.once("error", () => {
      // `unexpected-response` is the authoritative result for HTTP rejections.
    });
  });
}

function attemptChatWebSocketUpgrade(
  url: string,
  ticket: string,
): Promise<{ status: 101 | number; socket?: WebSocket }> {
  const socket = new WebSocket(url, {
    headers: { authorization: `Bearer ${ticket}` },
  });
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      socket.terminate();
      reject(new Error("chat websocket upgrade attempt timeout"));
    }, 2_000);
    socket.once("open", () => {
      clearTimeout(timer);
      resolve({ status: 101, socket });
    });
    socket.once("unexpected-response", (_request, response) => {
      clearTimeout(timer);
      const status = response.statusCode ?? 0;
      response.resume();
      resolve({ status });
    });
    socket.once("error", (error) => {
      clearTimeout(timer);
      reject(error);
    });
  });
}

test("chat websocket redeems a ticket once and delivers ready, snapshot, ack, and self-broadcast", async () => {
  const base = testConfig({ port: 0 });
  const config = { ...base, chat: { ...base.chat, enabled: true } };
  const service = await createLobbyService(config);
  const address = await service.start();
  let socket: WebSocket | undefined;

  try {
    const response = await postChatTicket(address.port);
    assert.equal(response.status, 200);
    const issued = (await response.json()) as { ticket: string; webSocketUrl: string };

    socket = await openChatWebSocket(issued.webSocketUrl, issued.ticket);
    const readyPromise = waitForChatFrame(socket, (frame) => frame.type === "chat_ready");
    const snapshotBeginPromise = waitForChatFrame(
      socket,
      (frame) => frame.type === "chat_snapshot_begin",
    );
    const snapshotEndPromise = waitForChatFrame(
      socket,
      (frame) => frame.type === "chat_snapshot_end",
    );
    const [ready, snapshotBegin] = await Promise.all([
      readyPromise,
      snapshotBeginPromise,
      snapshotEndPromise,
    ]);
    assert.equal(ready.channel, "server");
    assert.equal(snapshotBegin.totalMessages, 0);

    const clientMessageId = "15151515-1515-4515-8515-151515151515";
    const ackPromise = waitForChatFrame(
      socket,
      (frame) => frame.type === "chat_ack" && frame.clientMessageId === clientMessageId,
    );
    const broadcastPromise = waitForChatFrame(socket, (frame) => frame.type === "chat_message");
    socket.send(JSON.stringify({
      type: "chat_send",
      protocolVersion: 1,
      channel: "server",
      clientMessageId,
      content: { formatVersion: 1, segments: [{ kind: "text", text: "real socket" }] },
    }));
    const [ack, broadcast] = await Promise.all([ackPromise, broadcastPromise]);
    assert.deepEqual(ack.message, broadcast.message);

    assert.equal(
      await rejectedChatWebSocketStatus(issued.webSocketUrl, {
        authorization: `Bearer ${issued.ticket}`,
      }),
      401,
    );
  } finally {
    socket?.terminate();
    await service.close();
    cleanupTempDir(config);
  }
});

test("chat websocket rejects wrong IP, missing bearer, query tickets, and unknown paths", async () => {
  const base = testConfig({ port: 0 });
  const config = {
    ...base,
    chat: { ...base.chat, trustedProxyCidrs: ["127.0.0.0/8"] },
  };
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await postChatTicket(address.port, {
      headers: { "x-forwarded-for": "198.51.100.10" },
    });
    assert.equal(response.status, 200);
    const issued = (await response.json()) as { ticket: string; webSocketUrl: string };

    assert.equal(
      await rejectedChatWebSocketStatus(issued.webSocketUrl, {
        authorization: `Bearer ${issued.ticket}`,
        "x-forwarded-for": "198.51.100.11",
      }),
      401,
    );
    assert.equal(await rejectedChatWebSocketStatus(issued.webSocketUrl), 401);
    assert.equal(
      await rejectedChatWebSocketStatus(
        `${issued.webSocketUrl}?ticket=${encodeURIComponent(issued.ticket)}`,
      ),
      401,
    );
    assert.equal(
      await rejectedChatWebSocketStatus(
        `ws://127.0.0.1:${address.port}/not-chat`,
        { authorization: `Bearer ${issued.ticket}` },
      ),
      404,
    );
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("chat websocket allows exactly one concurrent redemption of a ticket", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const address = await service.start();
  const opened: WebSocket[] = [];

  try {
    const response = await postChatTicket(address.port);
    assert.equal(response.status, 200);
    const issued = (await response.json()) as { ticket: string; webSocketUrl: string };
    const results = await Promise.all([
      attemptChatWebSocketUpgrade(issued.webSocketUrl, issued.ticket),
      attemptChatWebSocketUpgrade(issued.webSocketUrl, issued.ticket),
    ]);
    opened.push(...results.flatMap((result) => result.socket ? [result.socket] : []));
    assert.deepEqual(results.map((result) => result.status).sort(), [101, 401]);
  } finally {
    for (const socket of opened) {
      socket.terminate();
    }
    await service.close();
    cleanupTempDir(config);
  }
});

test("chat websocket assembles fragmented text into one JSON message", async () => {
  const base = testConfig({ port: 0 });
  const config = { ...base, chat: { ...base.chat, enabled: true } };
  const service = await createLobbyService(config);
  const address = await service.start();
  let socket: WebSocket | undefined;

  try {
    const response = await postChatTicket(address.port);
    assert.equal(response.status, 200);
    const issued = (await response.json()) as { ticket: string; webSocketUrl: string };
    socket = await openChatWebSocket(issued.webSocketUrl, issued.ticket);
    await waitForChatFrame(socket, (frame) => frame.type === "chat_snapshot_end");

    const clientMessageId = "25252525-2525-4525-8525-252525252525";
    const payload = JSON.stringify({
      type: "chat_send",
      protocolVersion: 1,
      channel: "server",
      clientMessageId,
      content: { formatVersion: 1, segments: [{ kind: "text", text: "fragmented" }] },
    });
    const splitAt = Math.floor(payload.length / 2);
    const ackPromise = waitForChatFrame(
      socket,
      (frame) => frame.type === "chat_ack" && frame.clientMessageId === clientMessageId,
    );
    socket.send(payload.slice(0, splitAt), { fin: false });
    socket.send(payload.slice(splitAt), { fin: true });
    const ack = await ackPromise;
    assert.equal((ack.message as { plainTextFallback: string }).plainTextFallback, "fragmented");
  } finally {
    socket?.terminate();
    await service.close();
    cleanupTempDir(config);
  }
});

test("chat websocket closes binary messages with 1003", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const address = await service.start();
  let socket: WebSocket | undefined;

  try {
    const response = await postChatTicket(address.port);
    assert.equal(response.status, 200);
    const issued = (await response.json()) as { ticket: string; webSocketUrl: string };
    socket = await openChatWebSocket(issued.webSocketUrl, issued.ticket);
    await waitForChatFrame(socket, (frame) => frame.type === "chat_snapshot_end");
    const closePromise = waitForChatClose(socket);
    socket.send(Buffer.from([0x01, 0x02, 0x03]), { binary: true });
    assert.equal((await closePromise).code, 1003);
  } finally {
    socket?.terminate();
    await service.close();
    cleanupTempDir(config);
  }
});

test("chat websocket closes text payloads over 8 KiB with 1009", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const address = await service.start();
  let socket: WebSocket | undefined;

  try {
    const response = await postChatTicket(address.port);
    assert.equal(response.status, 200);
    const issued = (await response.json()) as { ticket: string; webSocketUrl: string };
    socket = await openChatWebSocket(issued.webSocketUrl, issued.ticket);
    await waitForChatFrame(socket, (frame) => frame.type === "chat_snapshot_end");
    const payload = JSON.stringify({
      type: "chat_send",
      protocolVersion: 1,
      channel: "server",
      clientMessageId: "35353535-3535-4535-8535-353535353535",
      content: { formatVersion: 1, segments: [{ kind: "text", text: "x".repeat(8_192) }] },
    });
    assert.ok(Buffer.byteLength(payload, "utf8") > 8_192);
    const closePromise = waitForChatClose(socket);
    socket.send(payload);
    assert.equal((await closePromise).code, 1009);
  } finally {
    socket?.terminate();
    await service.close();
    cleanupTempDir(config);
  }
});

test("chat websocket completes a real heartbeat ping-pong without production timers", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const address = await service.start();
  let socket: WebSocket | undefined;

  try {
    const response = await postChatTicket(address.port);
    assert.equal(response.status, 200);
    const issued = (await response.json()) as { ticket: string; webSocketUrl: string };
    socket = await openChatWebSocket(issued.webSocketUrl, issued.ticket);
    await waitForChatFrame(socket, (frame) => frame.type === "chat_snapshot_end");
    const pong = new Promise<Buffer>((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error("chat websocket pong timeout")), 2_000);
      socket!.once("pong", (data) => {
        clearTimeout(timer);
        resolve(data);
      });
    });
    socket.ping("heartbeat-smoke");
    assert.equal((await pong).toString("utf8"), "heartbeat-smoke");
  } finally {
    socket?.terminate();
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets issues ticket with lobby header when access enforced", async () => {
  const config = testConfig({
    port: 0,
    enforceLobbyAccessToken: true,
    lobbyAccessToken: "lobby-secret",
    createRoomToken: "create-secret",
  });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await postChatTicket(address.port, {
      headers: { "x-lobby-access-token": "lobby-secret" },
    });
    assert.equal(response.status, 200);
    const body = (await response.json()) as {
      ticket: string;
      expiresAt: string;
      webSocketUrl: string;
      protocolVersion: number;
    };
    assert.equal(typeof body.ticket, "string");
    assert.ok(body.ticket.length > 16);
    assert.equal(typeof body.expiresAt, "string");
    assert.ok(!Number.isNaN(Date.parse(body.expiresAt)));
    assert.equal(body.webSocketUrl, `ws://127.0.0.1:${address.port}/chat`);
    assert.equal(body.protocolVersion, 1);
    assert.equal(new URL(body.webSocketUrl).search, "", "webSocketUrl must not embed credentials");
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets accepts equivalent Bearer lobby access token", async () => {
  const config = testConfig({
    port: 0,
    enforceLobbyAccessToken: true,
    lobbyAccessToken: "lobby-bearer-secret",
    createRoomToken: "create-secret",
  });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await postChatTicket(address.port, {
      headers: { authorization: "Bearer lobby-bearer-secret" },
    });
    assert.equal(response.status, 200);
    const body = (await response.json()) as { ticket: string; protocolVersion: number };
    assert.equal(typeof body.ticket, "string");
    assert.equal(body.protocolVersion, 1);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets requires a complete Bearer authorization scheme", async () => {
  const config = testConfig({
    port: 0,
    enforceLobbyAccessToken: true,
    lobbyAccessToken: "strict-bearer-secret",
    createRoomToken: "create-secret",
  });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const raw = await postChatTicket(address.port, {
      headers: { authorization: "strict-bearer-secret" },
    });
    assert.equal(raw.status, 401);

    const malformed = await postChatTicket(address.port, {
      headers: { authorization: "Bearer strict-bearer-secret trailing" },
    });
    assert.equal(malformed.status, 401);

    const valid = await postChatTicket(address.port, {
      headers: { authorization: "Bearer strict-bearer-secret" },
    });
    assert.equal(valid.status, 200);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets rejects Basic authorization that equals the configured token", async () => {
  const config = testConfig({
    port: 0,
    enforceLobbyAccessToken: true,
    lobbyAccessToken: "Basic collision-secret",
    createRoomToken: "create-secret",
  });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await postChatTicket(address.port, {
      headers: { authorization: "Basic collision-secret" },
    });
    assert.equal(response.status, 401);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets ignores forwarded proto from an untrusted direct caller", async () => {
  const config = testConfig({ port: 0, enforceLobbyAccessToken: false });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await postChatTicket(address.port, {
      headers: { "x-forwarded-proto": "https" },
    });
    assert.equal(response.status, 200);
    const body = (await response.json()) as { webSocketUrl: string };
    assert.equal(body.webSocketUrl, `ws://127.0.0.1:${address.port}/chat`);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets honors forwarded proto from a trusted chat proxy", async () => {
  const base = testConfig({ port: 0, enforceLobbyAccessToken: false });
  const config: LobbyServiceConfig = {
    ...base,
    chat: { ...base.chat, trustedProxyCidrs: ["127.0.0.0/8"] },
  };
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await postChatTicket(address.port, {
      headers: { "x-forwarded-proto": "https" },
    });
    assert.equal(response.status, 200);
    const body = (await response.json()) as { webSocketUrl: string };
    assert.equal(body.webSocketUrl, `wss://127.0.0.1:${address.port}/chat`);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets rejects missing or wrong lobby token with 401", async () => {
  const config = testConfig({
    port: 0,
    enforceLobbyAccessToken: true,
    lobbyAccessToken: "lobby-secret",
    createRoomToken: "create-secret",
  });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const missing = await postChatTicket(address.port);
    assert.equal(missing.status, 401);

    const wrong = await postChatTicket(address.port, {
      headers: { "x-lobby-access-token": "not-the-lobby-secret" },
    });
    assert.equal(wrong.status, 401);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets rejects create-room-token-only requests with 401", async () => {
  const config = testConfig({
    port: 0,
    enforceLobbyAccessToken: true,
    lobbyAccessToken: "lobby-secret",
    createRoomToken: "create-secret",
  });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await postChatTicket(address.port, {
      headers: { "x-create-room-token": "create-secret" },
    });
    assert.equal(response.status, 401);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets never accepts the legacy CREATE_ROOM_TOKEN fallback", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-lobby-chat-access-"));
  const config = loadLobbyServiceConfig({
    HOST: "127.0.0.1",
    PORT: "0",
    PEER_NETWORK_ENABLED: "false",
    SERVER_ADMIN_STATE_FILE: join(tempDir, "server-admin.json"),
    PEER_STATE_DIR: join(tempDir, "peer"),
    ENFORCE_LOBBY_ACCESS_TOKEN: "true",
    ENFORCE_CREATE_ROOM_TOKEN: "true",
    CREATE_ROOM_TOKEN: "create-only-secret",
  });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await postChatTicket(address.port, {
      headers: { authorization: "Bearer create-only-secret" },
    });
    assert.equal(response.status, 503);
    assert.equal(
      (await response.json() as { code: string }).code,
      "lobby_access_token_not_configured",
    );
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets rejects invalid protocol, name, and net id with 400", async () => {
  const config = testConfig({
    port: 0,
    enforceLobbyAccessToken: false,
  });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const badProtocol = await postChatTicket(address.port, {
      body: ticketBody({ protocolVersion: 2 }),
    });
    assert.equal(badProtocol.status, 400);

    const badName = await postChatTicket(address.port, {
      body: ticketBody({ playerName: "bad\nname" }),
    });
    assert.equal(badName.status, 400);

    const badNetId = await postChatTicket(address.port, {
      body: ticketBody({ playerNetId: "net\u0000id" }),
    });
    assert.equal(badNetId.status, 400);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets rejects unexpected body fields", async () => {
  const config = testConfig({ port: 0, enforceLobbyAccessToken: false });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await postChatTicket(address.port, {
      body: ticketBody({ unexpected: true }),
    });
    assert.equal(response.status, 400);
    assert.equal((await response.json() as { code: string }).code, "invalid_request");
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets rate limits with 429 and Retry-After", async () => {
  const base = testConfig({ port: 0, enforceLobbyAccessToken: false });
  const config: LobbyServiceConfig = {
    ...base,
    chat: {
      ...base.chat,
      ticketRequestsPerMinute: 2,
    },
  };
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    assert.equal((await postChatTicket(address.port)).status, 200);
    assert.equal((await postChatTicket(address.port)).status, 200);
    const limited = await postChatTicket(address.port);
    assert.equal(limited.status, 429);
    const retryAfter = limited.headers.get("retry-after");
    assert.ok(retryAfter, "expected Retry-After header");
    assert.ok(Number.parseInt(retryAfter, 10) >= 1);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets returns 503 when pending ticket capacity is exhausted", async () => {
  const base = testConfig({ port: 0, enforceLobbyAccessToken: false });
  const config: LobbyServiceConfig = {
    ...base,
    chat: {
      ...base.chat,
      maxPendingTickets: 1,
      ticketRequestsPerMinute: 100,
    },
  };
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    assert.equal((await postChatTicket(address.port)).status, 200);
    const busy = await postChatTicket(address.port, {
      body: ticketBody({ playerNetId: "net-player-2" }),
    });
    assert.equal(busy.status, 503);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets logs omit submitted access, ticket, identity, and IP values", async () => {
  const logs: string[] = [];
  const originalLog = console.log;
  console.log = (...args: unknown[]) => {
    logs.push(args.map((entry) => String(entry)).join(" "));
  };

  const accessToken = "super-secret-lobby-access-token-value";
  const playerNetId = "private-player-net-id-marker";
  const playerName = "private-player-name-marker";
  const config = testConfig({
    port: 0,
    enforceLobbyAccessToken: true,
    lobbyAccessToken: accessToken,
    createRoomToken: "create-secret",
  });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await postChatTicket(address.port, {
      headers: { "x-lobby-access-token": accessToken },
      body: ticketBody({ playerNetId, playerName }),
    });
    assert.equal(response.status, 200);
    const body = (await response.json()) as { ticket: string };
    // Allow request logging to flush via response finish handlers.
    await sleep(20);
    const joined = logs.join("\n");
    assert.equal(joined.includes(accessToken), false, "logs must not include lobby access token");
    assert.equal(joined.includes(body.ticket), false, "logs must not include issued ticket");
    assert.equal(joined.includes(playerNetId), false, "logs must not include player net id");
    assert.equal(joined.includes(playerName), false, "logs must not include player name");
    assert.equal(joined.includes("127.0.0.1"), false, "chat ticket logs must not include client IP");
  } finally {
    console.log = originalLog;
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets rejects query credentials without logging them", async () => {
  const logs: string[] = [];
  const originalLog = console.log;
  console.log = (...args: unknown[]) => {
    logs.push(args.map((entry) => String(entry)).join(" "));
  };

  const marker = "query-leak-marker";
  const config = testConfig({ port: 0, enforceLobbyAccessToken: false });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await fetch(
      `http://127.0.0.1:${address.port}/chat/tickets?access_token=${marker}`,
      {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(ticketBody()),
      },
    );
    assert.equal(response.status, 400);
    await sleep(20);
    assert.equal(logs.join("\n").includes(marker), false, "logs must omit query credentials");
  } finally {
    console.log = originalLog;
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets rejects malformed JSON without logging its body", async () => {
  const logs: string[] = [];
  const originalLog = console.log;
  const originalError = console.error;
  console.log = (...args: unknown[]) => logs.push(inspect(args));
  console.error = (...args: unknown[]) => logs.push(inspect(args));

  const marker = "malformed-private-marker";
  const config = testConfig({ port: 0, enforceLobbyAccessToken: false });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await fetch(`http://127.0.0.1:${address.port}/chat/tickets`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: `{"playerName":"${marker}",`,
    });
    assert.equal(response.status, 400);
    assert.equal((await response.json() as { code: string }).code, "invalid_request");
    assert.equal(logs.join("\n").includes(marker), false, "logs must omit malformed JSON bodies");
  } finally {
    console.log = originalLog;
    console.error = originalError;
    await service.close();
    cleanupTempDir(config);
  }
});

test("POST /chat/tickets/ omits client IP from logs for the equivalent trailing-slash route", async () => {
  const logs: string[] = [];
  const originalLog = console.log;
  console.log = (...args: unknown[]) => logs.push(args.map((entry) => String(entry)).join(" "));

  const config = testConfig({ port: 0, enforceLobbyAccessToken: false });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const response = await fetch(`http://127.0.0.1:${address.port}/chat/tickets/`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(ticketBody()),
    });
    assert.equal(response.status, 200);
    await sleep(20);
    assert.equal(
      logs.join("\n").includes("127.0.0.1"),
      false,
      "equivalent chat ticket routes must omit client IP",
    );
  } finally {
    console.log = originalLog;
    await service.close();
    cleanupTempDir(config);
  }
});
