import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { connect as netConnect } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { setTimeout as sleep } from "node:timers/promises";
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

test("POST /chat/tickets logs omit submitted access and ticket values", async () => {
  const logs: string[] = [];
  const originalLog = console.log;
  console.log = (...args: unknown[]) => {
    logs.push(args.map((entry) => String(entry)).join(" "));
  };

  const accessToken = "super-secret-lobby-access-token-value";
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
    });
    assert.equal(response.status, 200);
    const body = (await response.json()) as { ticket: string };
    // Allow request logging to flush via response finish handlers.
    await sleep(20);
    const joined = logs.join("\n");
    assert.equal(joined.includes(accessToken), false, "logs must not include lobby access token");
    assert.equal(joined.includes(body.ticket), false, "logs must not include issued ticket");
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
