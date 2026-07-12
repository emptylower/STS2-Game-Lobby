import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
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
