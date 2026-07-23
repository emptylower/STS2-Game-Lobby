import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { connect as netConnect } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { setTimeout as sleep } from "node:timers/promises";
import { inspect } from "node:util";
import { WebSocket } from "ws";
import { createLobbyService } from "./app.js";
import { ChatPeerRegistry } from "./chat/peer-registry.js";
import { loadLobbyServiceConfig, type LobbyServiceConfig } from "./config.js";
import { hashServerAdminPassword } from "./server-admin-auth.js";

class FailOnceStateBroadcastRegistry extends ChatPeerRegistry {
  failNextStateBroadcast = true;
  readonly attemptedTypes: string[] = [];

  override broadcast(frame: object): void {
    const type = String((frame as { type?: unknown }).type);
    this.attemptedTypes.push(type);
    if (type === "chat_state" && this.failNextStateBroadcast) {
      this.failNextStateBroadcast = false;
      throw new Error("injected state broadcast failure");
    }
    super.broadcast(frame);
  }
}

function createServerAdminMutationBarrier() {
  let armed: {
    kind: "settings" | "clear-history";
    entered(): void;
    release: Promise<void>;
  } | undefined;
  return {
    beforeMutation: async (kind: "settings" | "clear-history") => {
      if (armed?.kind !== kind) return;
      const current = armed;
      armed = undefined;
      current.entered();
      await current.release;
    },
    arm(kind: "settings" | "clear-history") {
      assert.equal(armed, undefined, "only one admin mutation barrier may be armed");
      let entered!: () => void;
      let release!: () => void;
      const enteredPromise = new Promise<void>((resolve) => { entered = resolve; });
      const releasePromise = new Promise<void>((resolve) => { release = resolve; });
      armed = { kind, entered, release: releasePromise };
      return { entered: enteredPromise, release };
    },
  };
}

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
  let socket: WebSocket | undefined;

  try {
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

    const controlSocket = new WebSocket(wsUrl);
    socket = controlSocket;
    const connectedFrame = new Promise<Record<string, unknown>>((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error("connected frame timeout")), 2000);
      controlSocket.once("message", (data) => {
        clearTimeout(timer);
        resolve(JSON.parse(data.toString()) as Record<string, unknown>);
      });
    });
    const opened = new Promise<void>((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error("websocket connect timeout")), 2000);
      controlSocket.once("open", () => {
        clearTimeout(timer);
        resolve();
      });
      controlSocket.once("error", (error) => {
        clearTimeout(timer);
        reject(error);
      });
    });
    const [, connected] = await Promise.all([opened, connectedFrame]);
    assert.deepEqual(connected, {
      type: "connected",
      roomId: created.roomId,
      controlChannelId: created.controlChannelId,
      role: "host",
    });

    await Promise.race([
      service.close(),
      sleep(1500).then(() => {
        throw new Error("close() did not complete within 1500ms while control WS stayed open");
      }),
    ]);
    assert.equal(service.httpServer.listening, false);
  } finally {
    try {
      socket?.terminate();
    } catch {
      // ignore
    }
    await service.close();
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

const CURRENT_PROBE_CAPABILITIES = {
  serverChatVersion: 1,
  roomChatProtocolVersion: 1,
  richContentVersion: 1,
  emojiSetVersion: 1,
  itemRefVersion: 1,
  combatRefVersion: 1,
  maxMessageChars: 300,
  maxSegments: 32,
  maxEntities: 12,
  historyLimit: 50,
  modSyncProtocolVersion: 1,
  modSyncEnabled: true,
  modSyncMinimumClientVersion: "0.5.1",
} as const;

test("GET /probe returns exact current chat capabilities", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const address = await service.start();

  try {
    const probe = await fetch(`http://127.0.0.1:${address.port}/probe`);
    assert.equal(probe.status, 200);
    assert.deepEqual(await probe.json(), {
      ok: true,
      capabilities: CURRENT_PROBE_CAPABILITIES,
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

test("GET /probe keeps phase-3 historyLimit fixed when snapshot config changes", async () => {
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

interface ChatFrameWaiter {
  predicate(frame: ChatFrame): boolean;
  resolve(frame: ChatFrame): void;
  reject(error: Error): void;
  timer: NodeJS.Timeout;
}

interface ChatSocketState {
  frames: ChatFrame[];
  waiters: ChatFrameWaiter[];
  closedError?: Error;
}

const chatSocketStates = new WeakMap<WebSocket, ChatSocketState>();

function chatSocketState(socket: WebSocket): ChatSocketState {
  const existing = chatSocketStates.get(socket);
  if (existing) {
    return existing;
  }

  const state: ChatSocketState = { frames: [], waiters: [] };
  const onMessage = (data: WebSocket.RawData) => {
    let frame: ChatFrame;
    try {
      frame = JSON.parse(data.toString()) as ChatFrame;
    } catch {
      return;
    }
    const waiterIndex = state.waiters.findIndex((waiter) => waiter.predicate(frame));
    if (waiterIndex < 0) {
      state.frames.push(frame);
      return;
    }
    const waiter = state.waiters.splice(waiterIndex, 1)[0]!;
    clearTimeout(waiter.timer);
    waiter.resolve(frame);
  };
  const onClose = (code: number) => {
    state.closedError = new Error(`chat websocket closed before frame (${code})`);
    const waiters = state.waiters.splice(0);
    for (const waiter of waiters) {
      clearTimeout(waiter.timer);
      waiter.reject(state.closedError);
    }
    socket.off("message", onMessage);
    socket.off("close", onClose);
  };
  socket.on("message", onMessage);
  socket.once("close", onClose);
  chatSocketStates.set(socket, state);
  return state;
}

function waitForChatFrame(
  socket: WebSocket,
  predicate: (frame: ChatFrame) => boolean,
  timeoutMs = 2_000,
): Promise<ChatFrame> {
  const state = chatSocketState(socket);
  const bufferedIndex = state.frames.findIndex(predicate);
  if (bufferedIndex >= 0) {
    return Promise.resolve(state.frames.splice(bufferedIndex, 1)[0]!);
  }
  if (state.closedError) {
    return Promise.reject(state.closedError);
  }
  return new Promise((resolve, reject) => {
    const waiter: ChatFrameWaiter = {
      predicate,
      resolve,
      reject,
      timer: setTimeout(() => {
        const waiterIndex = state.waiters.indexOf(waiter);
        if (waiterIndex >= 0) {
          state.waiters.splice(waiterIndex, 1);
        }
        reject(new Error("chat websocket frame timeout"));
      }, timeoutMs),
    };
    state.waiters.push(waiter);
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

function waitForServerPings(
  socket: WebSocket,
  targetCount: number,
  timeoutMs: number,
): Promise<void> {
  return new Promise((resolve, reject) => {
    let pingCount = 0;
    let settled = false;
    const timer = setTimeout(
      () => settleReject(new Error("chat websocket server ping timeout")),
      timeoutMs,
    );
    const cleanup = () => {
      clearTimeout(timer);
      socket.off("ping", onPing);
      socket.off("close", onClose);
    };
    const settleResolve = () => {
      if (settled) return;
      settled = true;
      cleanup();
      resolve();
    };
    const settleReject = (error: Error) => {
      if (settled) return;
      settled = true;
      cleanup();
      reject(error);
    };
    const onPing = () => {
      pingCount += 1;
      if (pingCount >= targetCount) {
        settleResolve();
      }
    };
    const onClose = (code: number) => {
      settleReject(new Error(`chat websocket closed before heartbeat (${code})`));
    };
    socket.on("ping", onPing);
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
  chatSocketState(socket);
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

function openControlWebSocket(url: string): Promise<WebSocket> {
  const socket = new WebSocket(url);
  chatSocketState(socket);
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      socket.terminate();
      reject(new Error("control websocket connect timeout"));
    }, 2_000);
    socket.once("open", () => {
      clearTimeout(timer);
      resolve(socket);
    });
    socket.once("error", (error) => {
      clearTimeout(timer);
      reject(error);
    });
  });
}

test("control host and client room_chat_ready uses Phase 1 generation for rich and legacy routing", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const sockets: WebSocket[] = [];
  try {
    const address = await service.start();
    const createResponse = await fetch(`http://127.0.0.1:${address.port}/rooms`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        roomName: "rich-control",
        hostPlayerName: "Host",
        gameMode: "standard",
        version: "1.0.0",
        modVersion: "1.0.0",
        modList: [],
        maxPlayers: 4,
        hostConnectionInfo: { enetPort: 7777, localAddresses: ["127.0.0.1"] },
      }),
    });
    assert.equal(createResponse.status, 201);
    const created = await createResponse.json() as {
      roomId: string;
      roomSessionId: string;
      controlChannelId: string;
      hostToken: string;
      room: object;
    };
    const join = async (playerName: string, playerNetId: string) => {
      const response = await fetch(`http://127.0.0.1:${address.port}/rooms/${created.roomId}/join`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          playerName,
          playerNetId,
          version: "1.0.0",
          modVersion: "1.0.0",
          modList: [],
        }),
      });
      assert.equal(response.status, 200);
      return response.json() as Promise<{ ticketId: string; roomSessionId: string; room: object }>;
    };
    const richJoin = await join("Rich", "net:rich");
    const oldJoin = await join("Old", "net:old");
    assert.equal(richJoin.roomSessionId, created.roomSessionId);
    assert.equal(Object.hasOwn(richJoin.room, "roomSessionId"), false);
    const baseUrl = `ws://127.0.0.1:${address.port}${config.wsPath}`;
    const hostUrl = `${baseUrl}?roomId=${created.roomId}&controlChannelId=${created.controlChannelId}`
      + `&role=host&token=${created.hostToken}`;
    const host = await openControlWebSocket(hostUrl);
    sockets.push(host);
    const mutatingHost = await openControlWebSocket(
      `${baseUrl}?roomId=${created.roomId}&controlChannelId=${created.controlChannelId}`
      + `&role=host&token=${created.hostToken}`,
    );
    sockets.push(mutatingHost);
    const legacyHost = await openControlWebSocket(
      `${baseUrl}?roomId=${created.roomId}&controlChannelId=${created.controlChannelId}`
      + `&role=host&token=${created.hostToken}`,
    );
    sockets.push(legacyHost);
    const rich = await openControlWebSocket(
      `${baseUrl}?roomId=${created.roomId}&controlChannelId=${created.controlChannelId}`
      + `&role=client&ticketId=${richJoin.ticketId}`,
    );
    sockets.push(rich);
    const old = await openControlWebSocket(
      `${baseUrl}?roomId=${created.roomId}&controlChannelId=${created.controlChannelId}`
      + `&role=client&ticketId=${oldJoin.ticketId}`,
    );
    sockets.push(old);
    await Promise.all([
      waitForChatFrame(host, (frame) => frame.type === "connected"),
      waitForChatFrame(mutatingHost, (frame) => frame.type === "connected"),
      waitForChatFrame(legacyHost, (frame) => frame.type === "connected"),
      waitForChatFrame(rich, (frame) => frame.type === "connected"),
      waitForChatFrame(old, (frame) => frame.type === "connected"),
    ]);

    const earlyId = "31313131-3131-4131-8131-313131313131";
    host.send(JSON.stringify({
      type: "room_chat_v2",
      protocolVersion: 1,
      clientMessageId: earlyId,
      roomId: created.roomId,
      roomSessionId: created.roomSessionId,
      content: { formatVersion: 1, segments: [{ kind: "text", text: "early" }] },
    }));
    const early = await waitForChatFrame(host, (frame) => frame.type === "room_chat_error");
    assert.equal(early.code, "protocol_mismatch");

    const versions = {
      richContentVersion: 1,
      emojiSetVersion: 1,
      itemRefVersion: 1,
      combatRefVersion: 1,
    };
    host.send(JSON.stringify({
      type: "host_hello",
      roomId: created.roomId,
      controlChannelId: created.controlChannelId,
      role: "host",
      playerName: " Host ",
      playerNetId: " net:host ",
      roomChatVersions: versions,
    }));
    rich.send(JSON.stringify({
      type: "client_hello",
      roomId: created.roomId,
      controlChannelId: created.controlChannelId,
      role: "client",
      playerName: "Rich",
      playerNetId: "net:rich",
      roomChatVersions: versions,
    }));
    mutatingHost.send(JSON.stringify({
      type: "host_hello",
      roomId: created.roomId,
      controlChannelId: created.controlChannelId,
      role: "host",
      playerName: "Mutation Probe",
      playerNetId: "net:mutation-probe",
      roomChatVersions: versions,
    }));
    legacyHost.send(JSON.stringify({
      type: "host_hello",
      roomId: created.roomId,
      controlChannelId: created.controlChannelId,
      role: "host",
      playerName: "Legacy Host",
    }));
    legacyHost.send(JSON.stringify({ type: "ping" }));
    old.send(JSON.stringify({
      type: "client_hello",
      roomId: created.roomId,
      controlChannelId: created.controlChannelId,
      role: "client",
      ticketId: oldJoin.ticketId,
      playerName: "Old",
      playerNetId: "net:old",
    }));
    old.send(JSON.stringify({ type: "ping" }));
    const [hostReady, richReady, reconnectReady] = await Promise.all([
      waitForChatFrame(host, (frame) => frame.type === "room_chat_ready"),
      waitForChatFrame(rich, (frame) => frame.type === "room_chat_ready"),
      waitForChatFrame(mutatingHost, (frame) => frame.type === "room_chat_ready"),
      waitForChatFrame(legacyHost, (frame) => frame.type === "pong"),
      waitForChatFrame(old, (frame) => frame.type === "pong"),
    ]);
    assert.equal(hostReady.roomSessionId, created.roomSessionId);
    assert.equal(reconnectReady.roomSessionId, created.roomSessionId);
    assert.deepEqual(hostReady.enabledFeatures, {
      richContentVersion: 1,
      emojiSetVersion: 1,
      itemRefVersion: 1,
      combatRefVersion: 1,
    });
    assert.deepEqual(richReady.enabledFeatures, hostReady.enabledFeatures);
    const assertNoRoomDeliveryAfterPing = async (
      candidates: WebSocket[],
      label: string,
    ) => {
      const deliveryCount = (socket: WebSocket) => chatSocketState(socket).frames.filter(
        (frame) => frame.type === "room_chat_message" || frame.type === "room_chat",
      ).length;
      const before = candidates.map(deliveryCount);
      const barriers = candidates.map((socket) =>
        waitForChatFrame(socket, (frame) => frame.type === "pong"));
      for (const socket of candidates) {
        socket.send(JSON.stringify({ type: "ping" }));
      }
      await Promise.all(barriers);
      assert.deepEqual(candidates.map(deliveryCount), before, label);
    };

    const mutationError = waitForChatFrame(
      mutatingHost,
      (frame) => frame.type === "room_chat_error" && frame.code === "protocol_mismatch",
    );
    const mutationClose = waitForChatClose(mutatingHost);
    mutatingHost.send(JSON.stringify({
      type: "host_hello",
      roomId: created.roomId,
      controlChannelId: created.controlChannelId,
      role: "host",
      playerName: "Mallory",
      playerNetId: "net:mallory",
      roomChatVersions: versions,
    }));
    assert.equal((await mutationError).clientMessageId, "");
    assert.deepEqual(await mutationClose, { code: 1002, reason: "protocol_mismatch" });

    const replayReady = waitForChatFrame(host, (frame) => frame.type === "room_chat_ready");
    host.send(JSON.stringify({
      type: "host_hello",
      roomId: created.roomId,
      controlChannelId: created.controlChannelId,
      role: "host",
      playerName: "Host",
      playerNetId: "net:host",
      roomChatVersions: { ...versions },
    }));
    assert.deepEqual(await replayReady, hostReady);

    const reservedMetadata: Array<[string, unknown]> = [
      ["message", {}],
      ["messageId", "sender-message"],
      ["senderId", "net:mallory"],
      ["senderName", "Mallory"],
      ["sentAt", "2026-07-15T00:00:00.000Z"],
      ["plainTextFallback", "sender fallback"],
    ];
    for (const [index, [field, value]] of reservedMetadata.entries()) {
      const metadataId = `30303030-3030-4030-8030-${String(index).padStart(12, "0")}`;
      const metadataError = waitForChatFrame(
        host,
        (frame) => frame.type === "room_chat_error" && frame.clientMessageId === metadataId,
      );
      host.send(JSON.stringify({
        type: "room_chat_v2",
        protocolVersion: 1,
        clientMessageId: metadataId,
        roomId: created.roomId,
        roomSessionId: created.roomSessionId,
        content: { formatVersion: 1, segments: [{ kind: "text", text: "spoof" }] },
        [field]: value,
      }));
      const error = await metadataError;
      assert.equal(error.code, "invalid_message", field);
      assert.deepEqual(Object.keys(error).sort(), [
        "clientMessageId", "code", "message", "protocolVersion", "type",
      ], field);
    }
    await assertNoRoomDeliveryAfterPing(
      [host, rich, legacyHost, old],
      "reserved metadata must not broadcast",
    );

    const sendId = "32323232-3232-4232-8232-323232323232";
    const sendContent = {
      formatVersion: 1,
      segments: [
        { kind: "text", text: "look " },
        { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" },
        { kind: "emoji", emojiId: "heart" },
      ],
    };
    const hostDeliveryOrder: string[] = [];
    const trackHostDelivery = (data: WebSocket.RawData) => {
      const frame = JSON.parse(data.toString()) as ChatFrame;
      if (
        (frame.type === "room_chat_ack" && frame.clientMessageId === sendId)
        || frame.type === "room_chat_message"
      ) {
        hostDeliveryOrder.push(frame.type);
      }
    };
    host.on("message", trackHostDelivery);
    host.send(JSON.stringify({
      type: "room_chat_v2",
      protocolVersion: 1,
      clientMessageId: sendId,
      roomId: created.roomId,
      roomSessionId: created.roomSessionId,
      content: sendContent,
    }));
    const [ack, selfBroadcast, richBroadcast, legacy, legacyHostFallback] = await Promise.all([
      waitForChatFrame(host, (frame) => frame.type === "room_chat_ack"),
      waitForChatFrame(host, (frame) => frame.type === "room_chat_message"),
      waitForChatFrame(rich, (frame) => frame.type === "room_chat_message"),
      waitForChatFrame(old, (frame) => frame.type === "room_chat"),
      waitForChatFrame(legacyHost, (frame) => frame.type === "room_chat"),
    ]);
    const message = ack.message as Record<string, unknown>;
    host.off("message", trackHostDelivery);
    assert.deepEqual(hostDeliveryOrder, ["room_chat_ack", "room_chat_message"]);
    assert.deepEqual(selfBroadcast.message, message);
    assert.deepEqual(richBroadcast.message, message);
    assert.equal(message.senderName, "Host");
    assert.equal(message.senderId, "net:host");
    assert.equal(legacy.messageText, "look [Card][Emoji]");
    assert.equal(legacy.playerName, "Host");
    assert.equal("modelId" in legacy, false);
    assert.equal(JSON.stringify(legacy).includes("MegaCrit.Strike"), false);
    assert.deepEqual(legacyHostFallback, legacy);
    for (const socket of [legacyHost, old]) {
      assert.equal(
        chatSocketState(socket).frames.some(
          (frame) => frame.type === "room_chat_ready" || frame.type === "room_chat_error",
        ),
        false,
      );
    }

    const canonicalReplayAck = waitForChatFrame(
      host,
      (frame) => frame.type === "room_chat_ack" && frame.clientMessageId === sendId,
    );
    host.send(JSON.stringify({
      type: "room_chat_v2",
      protocolVersion: 1,
      clientMessageId: sendId,
      roomId: created.roomId,
      roomSessionId: created.roomSessionId,
      content: {
        segments: [
          { text: "look", kind: "text" },
          { kind: "text", text: " " },
          { modelId: "MegaCrit.Strike", itemType: "card", kind: "item_ref" },
          { emojiId: "heart", kind: "emoji" },
        ],
        formatVersion: 1,
      },
    }));
    assert.deepEqual(await canonicalReplayAck, ack);
    await assertNoRoomDeliveryAfterPing(
      [host, rich, legacyHost, old],
      "canonical replay must not rebroadcast",
    );

    const conflictPromise = waitForChatFrame(
      host,
      (frame) => frame.type === "room_chat_error" && frame.clientMessageId === sendId,
    );
    host.send(JSON.stringify({
      type: "room_chat_v2",
      protocolVersion: 1,
      clientMessageId: sendId,
      roomId: created.roomId,
      roomSessionId: created.roomSessionId,
      content: { formatVersion: 1, segments: [{ kind: "text", text: "changed" }] },
    }));
    const conflict = await conflictPromise;
    assert.equal(conflict.code, "duplicate_message");
    assert.deepEqual(Object.keys(conflict).sort(), [
      "clientMessageId", "code", "message", "protocolVersion", "type",
    ]);
    await assertNoRoomDeliveryAfterPing(
      [host, rich, legacyHost, old],
      "duplicate conflict must not broadcast",
    );

    const kickedFrame = waitForChatFrame(old, (frame) => frame.type === "kicked");
    const kickedClose = waitForChatClose(old);
    host.send(JSON.stringify({
      type: "kick_player",
      targetPlayerNetId: "net:old",
      targetPlayerName: "Old",
    }));
    assert.deepEqual(await kickedFrame, {
      type: "kicked",
      roomId: created.roomId,
      reason: "host_kick",
      message: "你已被房主移出房间。",
    });
    assert.deepEqual(await kickedClose, { code: 4001, reason: "kicked" });

    const hostClose = waitForChatClose(host);
    host.close(1000, "reconnect_test");
    await hostClose;
    const reconnectedHost = await openControlWebSocket(hostUrl);
    sockets.push(reconnectedHost);
    await waitForChatFrame(reconnectedHost, (frame) => frame.type === "connected");
    const reconnectDeliveryCount = () => chatSocketState(reconnectedHost).frames.filter(
      (frame) => frame.type === "room_chat_ack" || frame.type === "room_chat_message",
    ).length;
    const deliveriesBeforeReady = reconnectDeliveryCount();
    reconnectedHost.send(JSON.stringify({
      type: "host_hello",
      roomId: created.roomId,
      controlChannelId: created.controlChannelId,
      role: "host",
      playerName: "Host",
      playerNetId: "net:host",
      roomChatVersions: versions,
    }));
    await waitForChatFrame(reconnectedHost, (frame) => frame.type === "room_chat_ready");
    const reconnectBarrier = waitForChatFrame(
      reconnectedHost,
      (frame) => frame.type === "pong",
    );
    reconnectedHost.send(JSON.stringify({ type: "ping" }));
    await reconnectBarrier;
    assert.equal(reconnectDeliveryCount(), deliveriesBeforeReady);

    const reconnectAckPromise = waitForChatFrame(
      reconnectedHost,
      (frame) => frame.type === "room_chat_ack" && frame.clientMessageId === sendId,
    );
    const reconnectSelfPromise = waitForChatFrame(
      reconnectedHost,
      (frame) => frame.type === "room_chat_message",
    );
    const reconnectRichPromise = waitForChatFrame(
      rich,
      (frame) => frame.type === "room_chat_message",
    );
    reconnectedHost.send(JSON.stringify({
      type: "room_chat_v2",
      protocolVersion: 1,
      clientMessageId: sendId,
      roomId: created.roomId,
      roomSessionId: created.roomSessionId,
      content: sendContent,
    }));
    const [reconnectAck, reconnectSelf, reconnectRich] = await Promise.all([
      reconnectAckPromise,
      reconnectSelfPromise,
      reconnectRichPromise,
    ]);
    assert.deepEqual(reconnectSelf.message, reconnectAck.message);
    assert.deepEqual(reconnectRich.message, reconnectAck.message);
    assert.notEqual(
      (reconnectAck.message as Record<string, unknown>).messageId,
      message.messageId,
    );
  } finally {
    for (const socket of sockets) socket.terminate();
    await service.close();
    cleanupTempDir(config);
  }
});

test("legacy room chat burst forwards only the shared connection budget without sender errors", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const sockets: WebSocket[] = [];
  try {
    const address = await service.start();
    const createResponse = await fetch(`http://127.0.0.1:${address.port}/rooms`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        roomName: "legacy-rate-limit",
        hostPlayerName: "Host",
        gameMode: "standard",
        version: "1.0.0",
        modVersion: "1.0.0",
        modList: [],
        maxPlayers: 4,
        hostConnectionInfo: { enetPort: 7777, localAddresses: ["127.0.0.1"] },
      }),
    });
    assert.equal(createResponse.status, 201);
    const created = await createResponse.json() as {
      roomId: string;
      controlChannelId: string;
      hostToken: string;
    };
    const joinResponse = await fetch(`http://127.0.0.1:${address.port}/rooms/${created.roomId}/join`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        playerName: "Recipient",
        playerNetId: "net:recipient",
        version: "1.0.0",
        modVersion: "1.0.0",
        modList: [],
      }),
    });
    assert.equal(joinResponse.status, 200);
    const joined = await joinResponse.json() as { ticketId: string };
    const baseUrl = `ws://127.0.0.1:${address.port}${config.wsPath}`;
    const sender = await openControlWebSocket(
      `${baseUrl}?roomId=${created.roomId}&controlChannelId=${created.controlChannelId}`
      + `&role=host&token=${created.hostToken}`,
    );
    const recipient = await openControlWebSocket(
      `${baseUrl}?roomId=${created.roomId}&controlChannelId=${created.controlChannelId}`
      + `&role=client&ticketId=${joined.ticketId}`,
    );
    sockets.push(sender, recipient);
    await Promise.all([
      waitForChatFrame(sender, (frame) => frame.type === "connected"),
      waitForChatFrame(recipient, (frame) => frame.type === "connected"),
    ]);
    chatSocketState(sender).frames.length = 0;
    chatSocketState(recipient).frames.length = 0;

    for (let index = 0; index < 7; index += 1) {
      sender.send(JSON.stringify({
        type: "room_chat",
        messageText: `legacy-${index}`,
      }));
    }
    await waitForChatFrame(
      recipient,
      (frame) => frame.type === "room_chat" && frame.messageText === "legacy-4",
    );
    await sleep(50);

    const recipientLegacyFrames = chatSocketState(recipient).frames.filter(
      (frame) => frame.type === "room_chat",
    );
    assert.deepEqual(
      recipientLegacyFrames.map((frame) => frame.messageText),
      ["legacy-0", "legacy-1", "legacy-2", "legacy-3"],
    );
    assert.equal(
      chatSocketState(sender).frames.some((frame) => frame.type === "room_chat_error"),
      false,
    );
  } finally {
    for (const socket of sockets) socket.terminate();
    await service.close();
    cleanupTempDir(config);
  }
});

test("control websocket closes frames over the configured payload before routing", async () => {
  const config = testConfig({ port: 0 });
  assert.equal(config.chat.maxPayloadBytes, 8_192);
  const service = await createLobbyService(config);
  let socket: WebSocket | undefined;
  try {
    const address = await service.start();
    const response = await fetch(`http://127.0.0.1:${address.port}/rooms`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        roomName: "control-payload-bound",
        hostPlayerName: "Host",
        gameMode: "standard",
        version: "1.0.0",
        modVersion: "1.0.0",
        modList: [],
        maxPlayers: 4,
        hostConnectionInfo: { enetPort: 7777, localAddresses: ["127.0.0.1"] },
      }),
    });
    assert.equal(response.status, 201);
    const created = await response.json() as {
      roomId: string;
      controlChannelId: string;
      hostToken: string;
    };
    socket = await openControlWebSocket(
      `ws://127.0.0.1:${address.port}${config.wsPath}`
      + `?roomId=${created.roomId}&controlChannelId=${created.controlChannelId}`
      + `&role=host&token=${created.hostToken}`,
    );
    await waitForChatFrame(socket, (frame) => frame.type === "connected");
    const oversized = JSON.stringify({
      type: "ping",
      padding: "x".repeat(config.chat.maxPayloadBytes),
    });
    assert.ok(Buffer.byteLength(oversized, "utf8") > config.chat.maxPayloadBytes);
    const closed = waitForChatClose(socket);
    socket.send(oversized);
    assert.deepEqual(await closed, { code: 1009, reason: "" });
    assert.equal(
      chatSocketState(socket).frames.some(
        (frame) => frame.type === "pong" || frame.type === "room_chat_error",
      ),
      false,
    );
  } finally {
    socket?.terminate();
    await service.close();
    cleanupTempDir(config);
  }
});

test("chat websocket redeems a ticket once and delivers ready, snapshot, ack, and self-broadcast", async () => {
  const base = testConfig({ port: 0 });
  const config = {
    ...base,
    chat: { ...base.chat, features: { ...base.chat.features, serverChatEnabled: true } },
  };
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
    const payload = JSON.stringify({
      type: "chat_send",
      protocolVersion: 1,
      channel: "server",
      clientMessageId,
      content: { formatVersion: 1, segments: [{ kind: "text", text: "real socket" }] },
    });
    socket.send(payload);
    const [ack, broadcast] = await Promise.all([ackPromise, broadcastPromise]);
    assert.deepEqual(ack.message, broadcast.message);

    let replayResolved = false;
    const replayPromise = waitForChatFrame(
      socket,
      (frame) => frame.type === "chat_ack" && frame.clientMessageId === clientMessageId,
    ).then((frame) => {
      replayResolved = true;
      return frame;
    });
    await new Promise<void>((resolve) => setImmediate(resolve));
    assert.equal(replayResolved, false, "a consumed frame must not satisfy a later waiter");
    socket.send(payload);
    assert.deepEqual(await replayPromise, ack);

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
  const config = {
    ...base,
    chat: { ...base.chat, features: { ...base.chat.features, serverChatEnabled: true } },
  };
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

test("chat websocket receives a server heartbeat and remains usable after automatic pong", async () => {
  const base = testConfig({ port: 0 });
  const config = {
    ...base,
    chat: { ...base.chat, features: { ...base.chat.features, serverChatEnabled: true } },
  };
  const chatPeerRegistry = new ChatPeerRegistry({
    pingIntervalMs: 100,
    pongTimeoutMs: 800,
  });
  const service = await createLobbyService(config, {
    createChatPeerRegistry: () => chatPeerRegistry,
    chatGatewayOptions: { heartbeatTickMs: 50 },
  });
  const address = await service.start();
  let socket: WebSocket | undefined;

  try {
    const response = await postChatTicket(address.port);
    assert.equal(response.status, 200);
    const issued = (await response.json()) as { ticket: string; webSocketUrl: string };
    socket = await openChatWebSocket(issued.webSocketUrl, issued.ticket);
    await waitForChatFrame(socket, (frame) => frame.type === "chat_snapshot_end");
    // Ten 100ms heartbeat periods exceed the 800ms no-pong timeout.
    await waitForServerPings(socket, 10, 8_000);
    assert.equal(socket.listenerCount("ping"), 0, "heartbeat waiter must remove its listener");

    const clientMessageId = "45454545-4545-4545-8545-454545454545";
    const ackPromise = waitForChatFrame(
      socket,
      (frame) => frame.type === "chat_ack" && frame.clientMessageId === clientMessageId,
    );
    socket.send(JSON.stringify({
      type: "chat_send",
      protocolVersion: 1,
      channel: "server",
      clientMessageId,
      content: { formatVersion: 1, segments: [{ kind: "text", text: "after pong" }] },
    }));
    assert.equal((await ackPromise).type, "chat_ack");
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

test("server admin chat governance persists before ordered websocket state and clears history", async () => {
  const password = "governance-test-password";
  const base = testConfig({
    port: 0,
    serverAdminPasswordHash: hashServerAdminPassword(password),
    serverAdminSessionSecret: "governance-session-secret",
  });
  const config: LobbyServiceConfig = {
    ...base,
    chat: {
      ...base.chat,
      features: { ...base.chat.features, serverChatEnabled: true },
    },
  };
  const mutationBarrier = createServerAdminMutationBarrier();
  const service = await createLobbyService(config, {
    beforeServerAdminMutation: mutationBarrier.beforeMutation,
  });
  const address = await service.start();
  let socket: WebSocket | undefined;
  try {
    for (const [method, path] of [
      ["GET", "/server-admin/settings"],
      ["PATCH", "/server-admin/settings"],
      ["POST", "/server-admin/chat/clear-history"],
    ] as const) {
      const unauthorized = await fetch(`http://127.0.0.1:${address.port}${path}`, {
        method,
        headers: { "content-type": "application/json" },
        ...(method === "PATCH" ? { body: JSON.stringify({ chatFeatures: {} }) } : {}),
      });
      assert.equal(unauthorized.status, 401);
    }

    const login = await fetch(`http://127.0.0.1:${address.port}/server-admin/login`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ username: "admin", password }),
    });
    assert.equal(login.status, 200);
    const loginBody = await login.json() as { csrfToken: string };
    const cookie = login.headers.get("set-cookie")?.split(";", 1)[0];
    assert.ok(cookie);
    const authHeaders = { cookie, "x-csrf-token": loginBody.csrfToken };

    const initial = await fetch(`http://127.0.0.1:${address.port}/server-admin/settings`, {
      headers: authHeaders,
    });
    assert.equal(initial.status, 200);
    const initialSettings = await initial.json() as Record<string, unknown>;
    assert.equal((initialSettings.chatFeatures as Record<string, unknown>).serverChatEnabled, true);
    assert.deepEqual(initialSettings.serverFeatures, {
      richContentVersion: 1,
      emojiSetVersion: 1,
      itemRefVersion: 1,
      combatRefVersion: 0,
    });

    const ticketResponse = await postChatTicket(address.port);
    const issued = await ticketResponse.json() as { ticket: string; webSocketUrl: string };
    socket = await openChatWebSocket(issued.webSocketUrl, issued.ticket);
    await Promise.all([
      waitForChatFrame(socket, (frame) => frame.type === "chat_ready"),
      waitForChatFrame(socket, (frame) => frame.type === "chat_snapshot_end"),
    ]);
    const messageId = "61616161-6161-4616-8616-616161616161";
    const messageAck = waitForChatFrame(
      socket,
      (frame) => frame.type === "chat_ack" && frame.clientMessageId === messageId,
    );
    const messageBroadcast = waitForChatFrame(socket, (frame) => frame.type === "chat_message");
    socket.send(JSON.stringify({
      type: "chat_send",
      protocolVersion: 1,
      channel: "server",
      clientMessageId: messageId,
      content: { formatVersion: 1, segments: [{ kind: "text", text: "clear me" }] },
    }));
    await Promise.all([messageAck, messageBroadcast]);
    const beforeClearResponse = await fetch(
      `http://127.0.0.1:${address.port}/server-admin/settings`,
      { headers: authHeaders },
    );
    const beforeClear = await beforeClearResponse.json() as {
      metrics: { serverRetainedHistoryCount: number; historyEpoch: number };
    };
    assert.equal(beforeClear.metrics.serverRetainedHistoryCount, 1);
    assert.equal(beforeClear.metrics.historyEpoch, 0);

    let persistedServerEnabledAtState: unknown;
    let stateReadError: unknown;
    const stateFrame = waitForChatFrame(socket, (frame) => {
      if (frame.type !== "chat_state") return false;
      try {
        const observed = JSON.parse(
          readFileSync(config.serverAdminStateFile, "utf8"),
        ) as Record<string, unknown>;
        persistedServerEnabledAtState = (
          observed.chatFeatures as Record<string, unknown>
        ).serverChatEnabled;
      } catch (error) {
        stateReadError = error;
      }
      return true;
    });
    const patchResponse = await fetch(`http://127.0.0.1:${address.port}/server-admin/settings`, {
      method: "PATCH",
      headers: { ...authHeaders, "content-type": "application/json" },
      body: JSON.stringify({
        chatFeatures: {
          serverChatEnabled: false,
          richContentEnabled: false,
          emojiEnabled: true,
          itemRefsEnabled: true,
          roomChatV2Enabled: false,
          roomCombatRefsEnabled: true,
        },
      }),
    });
    assert.equal(patchResponse.status, 200);
    const patched = await patchResponse.json() as Record<string, unknown>;
    const persisted = JSON.parse(readFileSync(config.serverAdminStateFile, "utf8")) as Record<string, unknown>;
    assert.equal((persisted.chatFeatures as Record<string, unknown>).serverChatEnabled, false);
    assert.deepEqual(patched.serverFeatures, {
      richContentVersion: 0,
      emojiSetVersion: 0,
      itemRefVersion: 0,
      combatRefVersion: 0,
    });
    assert.equal((patched.chatFeatures as Record<string, unknown>).emojiEnabled, true);
    assert.equal((await stateFrame).chatEnabled, false);
    assert.equal(stateReadError, undefined);
    assert.equal(persistedServerEnabledAtState, false);

    const clearedFrameA = waitForChatFrame(socket, (frame) => frame.type === "chat_history_cleared");
    const clearedFrameB = waitForChatFrame(socket, (frame) => frame.type === "chat_history_cleared");
    const [clearA, clearB] = await Promise.all([
      fetch(`http://127.0.0.1:${address.port}/server-admin/chat/clear-history`, {
        method: "POST",
        headers: authHeaders,
      }),
      fetch(`http://127.0.0.1:${address.port}/server-admin/chat/clear-history`, {
        method: "POST",
        headers: authHeaders,
      }),
    ]);
    const clearBodies = await Promise.all([
      clearA.json() as Promise<{
        historyEpoch: number;
        metrics: { serverRetainedHistoryCount: number; historyEpoch: number };
      }>,
      clearB.json() as Promise<{
        historyEpoch: number;
        metrics: { serverRetainedHistoryCount: number; historyEpoch: number };
      }>,
    ]);
    const epochs = clearBodies.map((body) => body.historyEpoch);
    assert.deepEqual(epochs.sort((left, right) => left - right), [1, 2]);
    assert.equal(clearBodies.every((body) => body.metrics.serverRetainedHistoryCount === 0), true);
    const clearedEpochs = (await Promise.all([clearedFrameA, clearedFrameB]))
      .map((frame) => frame.historyEpoch as number)
      .sort((left, right) => left - right);
    assert.deepEqual(clearedEpochs, [1, 2]);

    const metricsResponse = await fetch(`http://127.0.0.1:${address.port}/server-admin/settings`, {
      headers: authHeaders,
    });
    const metricsText = await metricsResponse.text();
    const metricsSettings = JSON.parse(metricsText) as { metrics: Record<string, unknown> };
    assert.equal(metricsSettings.metrics.serverRetainedHistoryCount, 0);
    assert.equal(metricsSettings.metrics.historyEpoch, 2);
    assert.deepEqual(Object.keys(metricsSettings.metrics).sort(), [
      "historyEpoch",
      "roomAcceptedMessages",
      "roomConnectionCount",
      "roomRejectedMessages",
      "serverAcceptedMessages",
      "serverConnectionCount",
      "serverRejectedMessages",
      "serverRetainedHistoryCount",
    ]);
    for (const forbidden of [
      "messages", "content", "modelId", "ticket", "token", "sessionId", "senderId", "127.0.0.1",
    ]) {
      assert.equal(metricsText.includes(forbidden), false, forbidden);
    }

    assert.equal(Object.hasOwn(persisted, "metrics"), false);

    const concurrentStateA = waitForChatFrame(socket, (frame) => frame.type === "chat_state");
    const concurrentStateB = waitForChatFrame(socket, (frame) => frame.type === "chat_state");
    const concurrentPatchBarrier = mutationBarrier.arm("settings");
    const concurrentPatchA = fetch(`http://127.0.0.1:${address.port}/server-admin/settings`, {
      method: "PATCH",
      headers: { ...authHeaders, "content-type": "application/json" },
      body: JSON.stringify({
        chatFeatures: { serverChatEnabled: true, richContentEnabled: true },
      }),
    });
    await concurrentPatchBarrier.entered;
    const concurrentPatchB = fetch(`http://127.0.0.1:${address.port}/server-admin/settings`, {
      method: "PATCH",
      headers: { ...authHeaders, "content-type": "application/json" },
      body: JSON.stringify({
        chatFeatures: { serverChatEnabled: false, richContentEnabled: false },
      }),
    });
    concurrentPatchBarrier.release();
    const [concurrentResponseA, concurrentResponseB] = await Promise.all([
      concurrentPatchA,
      concurrentPatchB,
    ]);
    assert.deepEqual([concurrentResponseA.status, concurrentResponseB.status], [200, 200]);
    const [concurrentBodyA, concurrentBodyB] = await Promise.all([
      concurrentResponseA.json() as Promise<{ chatFeatures: { serverChatEnabled: boolean } }>,
      concurrentResponseB.json() as Promise<{ chatFeatures: { serverChatEnabled: boolean } }>,
    ]);
    assert.equal(concurrentBodyA.chatFeatures.serverChatEnabled, true);
    assert.equal(concurrentBodyB.chatFeatures.serverChatEnabled, false);
    assert.deepEqual(
      (await Promise.all([concurrentStateA, concurrentStateB])).map((frame) => frame.chatEnabled),
      [true, false],
    );
    const persistedAfterConcurrent = JSON.parse(
      readFileSync(config.serverAdminStateFile, "utf8"),
    ) as { chatFeatures: { serverChatEnabled: boolean } };
    assert.equal(persistedAfterConcurrent.chatFeatures.serverChatEnabled, false);

    const enabledFrame = waitForChatFrame(socket, (frame) => frame.type === "chat_state");
    const enableResponse = await fetch(
      `http://127.0.0.1:${address.port}/server-admin/settings`,
      {
        method: "PATCH",
        headers: { ...authHeaders, "content-type": "application/json" },
        body: JSON.stringify({
          chatFeatures: { serverChatEnabled: true, richContentEnabled: true },
        }),
      },
    );
    assert.equal(enableResponse.status, 200);
    assert.equal((await enabledFrame).chatEnabled, true);
    const orderedMessageId = "62626262-6262-4626-8626-626262626262";
    const orderedAck = waitForChatFrame(
      socket,
      (frame) => frame.type === "chat_ack" && frame.clientMessageId === orderedMessageId,
    );
    const orderedBroadcast = waitForChatFrame(socket, (frame) => frame.type === "chat_message");
    socket.send(JSON.stringify({
      type: "chat_send",
      protocolVersion: 1,
      channel: "server",
      clientMessageId: orderedMessageId,
      content: { formatVersion: 1, segments: [{ kind: "text", text: "ordered clear" }] },
    }));
    await Promise.all([orderedAck, orderedBroadcast]);

    const mutationFrameA = waitForChatFrame(
      socket,
      (frame) => frame.type === "chat_state" || frame.type === "chat_history_cleared",
    );
    const mutationFrameB = waitForChatFrame(
      socket,
      (frame) => frame.type === "chat_state" || frame.type === "chat_history_cleared",
    );
    const patchClearBarrier = mutationBarrier.arm("settings");
    const disablePatch = fetch(`http://127.0.0.1:${address.port}/server-admin/settings`, {
      method: "PATCH",
      headers: { ...authHeaders, "content-type": "application/json" },
      body: JSON.stringify({ chatFeatures: { serverChatEnabled: false } }),
    });
    await patchClearBarrier.entered;
    const orderedClear = fetch(
      `http://127.0.0.1:${address.port}/server-admin/chat/clear-history`,
      { method: "POST", headers: authHeaders },
    );
    patchClearBarrier.release();
    const [disableResponse, orderedClearResponse] = await Promise.all([disablePatch, orderedClear]);
    assert.deepEqual([disableResponse.status, orderedClearResponse.status], [200, 200]);
    assert.deepEqual(
      (await Promise.all([mutationFrameA, mutationFrameB])).map((frame) => frame.type),
      ["chat_state", "chat_history_cleared"],
    );
    const orderedClearBody = await orderedClearResponse.json() as {
      historyEpoch: number;
      metrics: { serverRetainedHistoryCount: number };
    };
    assert.equal(orderedClearBody.historyEpoch, 3);
    assert.equal(orderedClearBody.metrics.serverRetainedHistoryCount, 0);

    const diskBeforeFailure = readFileSync(config.serverAdminStateFile, "utf8");
    mkdirSync(`${config.serverAdminStateFile}.tmp`);
    const failedPatch = await fetch(`http://127.0.0.1:${address.port}/server-admin/settings`, {
      method: "PATCH",
      headers: { ...authHeaders, "content-type": "application/json" },
      body: JSON.stringify({ chatFeatures: { serverChatEnabled: true } }),
    });
    assert.equal(failedPatch.status, 500);
    assert.equal(readFileSync(config.serverAdminStateFile, "utf8"), diskBeforeFailure);
    await assert.rejects(
      waitForChatFrame(socket, (frame) => frame.type === "chat_state", 100),
      /frame timeout/,
    );
    const afterFailedPatch = await fetch(
      `http://127.0.0.1:${address.port}/server-admin/settings`,
      { headers: authHeaders },
    );
    const afterFailedSettings = await afterFailedPatch.json() as Record<string, unknown>;
    assert.equal(
      (afterFailedSettings.chatFeatures as Record<string, unknown>).serverChatEnabled,
      false,
    );

    rmSync(`${config.serverAdminStateFile}.tmp`, { recursive: true, force: true });
    const recoveredState = waitForChatFrame(socket, (frame) => frame.type === "chat_state");
    const recoveredPatch = await fetch(
      `http://127.0.0.1:${address.port}/server-admin/settings`,
      {
        method: "PATCH",
        headers: { ...authHeaders, "content-type": "application/json" },
        body: JSON.stringify({ chatFeatures: { serverChatEnabled: true } }),
      },
    );
    assert.equal(recoveredPatch.status, 200);
    assert.equal((await recoveredState).chatEnabled, true);
    const recoveredClearFrame = waitForChatFrame(
      socket,
      (frame) => frame.type === "chat_history_cleared",
    );
    const recoveredClear = await fetch(
      `http://127.0.0.1:${address.port}/server-admin/chat/clear-history`,
      { method: "POST", headers: authHeaders },
    );
    assert.equal(recoveredClear.status, 200);
    assert.equal((await recoveredClearFrame).historyEpoch, 4);
  } finally {
    socket?.terminate();
    await service.close();
    cleanupTempDir(config);
  }
});

test("server admin mutation queue recovers after a gateway broadcast failure", async () => {
  const password = "broadcast-recovery-password";
  const config = testConfig({
    port: 0,
    serverAdminPasswordHash: hashServerAdminPassword(password),
    serverAdminSessionSecret: "broadcast-recovery-secret",
  });
  const registry = new FailOnceStateBroadcastRegistry();
  const service = await createLobbyService(config, {
    createChatPeerRegistry: () => registry,
  });
  const address = await service.start();
  try {
    const login = await fetch(`http://127.0.0.1:${address.port}/server-admin/login`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ username: "admin", password }),
    });
    const loginBody = await login.json() as { csrfToken: string };
    const cookie = login.headers.get("set-cookie")?.split(";", 1)[0];
    assert.ok(cookie);
    const headers = {
      cookie,
      "content-type": "application/json",
      "x-csrf-token": loginBody.csrfToken,
    };

    const failed = await fetch(`http://127.0.0.1:${address.port}/server-admin/settings`, {
      method: "PATCH",
      headers,
      body: JSON.stringify({ chatFeatures: { serverChatEnabled: true } }),
    });
    assert.equal(failed.status, 500);
    const persistedAfterFailure = JSON.parse(
      readFileSync(config.serverAdminStateFile, "utf8"),
    ) as { chatFeatures: { serverChatEnabled: boolean } };
    assert.equal(persistedAfterFailure.chatFeatures.serverChatEnabled, true);

    const recovered = await fetch(`http://127.0.0.1:${address.port}/server-admin/settings`, {
      method: "PATCH",
      headers,
      body: JSON.stringify({ chatFeatures: { serverChatEnabled: false } }),
    });
    assert.equal(recovered.status, 200);
    const recoveredBody = await recovered.json() as {
      chatFeatures: { serverChatEnabled: boolean };
    };
    assert.equal(recoveredBody.chatFeatures.serverChatEnabled, false);
    const clear = await fetch(
      `http://127.0.0.1:${address.port}/server-admin/chat/clear-history`,
      { method: "POST", headers: { cookie, "x-csrf-token": loginBody.csrfToken } },
    );
    assert.equal(clear.status, 200);
    assert.deepEqual(registry.attemptedTypes, [
      "chat_state",
      "chat_state",
      "chat_history_cleared",
    ]);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("server admin CSRF protects every mutation without leaking tokens", async () => {
  const password = "csrf-integration-password";
  const config = testConfig({
    port: 0,
    serverAdminPasswordHash: hashServerAdminPassword(password),
    serverAdminSessionSecret: "csrf-integration-secret",
  });
  const logs: string[] = [];
  const originalLog = console.log;
  console.log = (...args: unknown[]) => logs.push(args.map(String).join(" "));
  const service = await createLobbyService(config);
  const address = await service.start();
  const baseUrl = `http://127.0.0.1:${address.port}`;
  try {
    const html = await fetch(`${baseUrl}/server-admin`);
    assert.equal(html.headers.get("cache-control"), "no-store");

    async function login() {
      const response = await fetch(`${baseUrl}/server-admin/login`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ username: "admin", password }),
      });
      assert.equal(response.status, 200);
      assert.equal(response.headers.get("cache-control"), "no-store");
      const body = await response.json() as { csrfToken: string };
      const cookie = response.headers.get("set-cookie")?.split(";", 1)[0];
      assert.ok(cookie);
      assert.match(body.csrfToken, /^[A-Za-z0-9_-]{43}$/);
      assert.equal(response.headers.get("set-cookie")?.includes(body.csrfToken), false);
      return { cookie, csrfToken: body.csrfToken };
    }

    async function mutation(
      path: "/server-admin/settings"
        | "/server-admin/chat/clear-history"
        | "/server-admin/update/check"
        | "/server-admin/update/install"
        | "/server-admin/logout",
      cookie?: string,
      csrfToken?: string,
    ) {
      const headers: Record<string, string> = { "content-type": "application/json" };
      if (cookie) headers.cookie = cookie;
      if (csrfToken) headers["x-csrf-token"] = csrfToken;
      const response = await fetch(`${baseUrl}${path}`, {
        method: path === "/server-admin/settings" ? "PATCH" : "POST",
        headers,
        ...(path === "/server-admin/settings"
          ? { body: JSON.stringify({ chatFeatures: { serverChatEnabled: true } }) }
          : {}),
      });
      assert.equal(response.headers.get("cache-control"), "no-store");
      const text = await response.text();
      return {
        response,
        text,
        body: text ? JSON.parse(text) as Record<string, unknown> : {},
      };
    }

    const sessionA = await login();
    const sessionB = await login();
    const firstTab = await fetch(`${baseUrl}/server-admin/session`, {
      headers: { cookie: sessionA.cookie },
    });
    const secondTab = await fetch(`${baseUrl}/server-admin/session`, {
      headers: { cookie: sessionA.cookie },
    });
    assert.equal(firstTab.headers.get("cache-control"), "no-store");
    const firstTabBody = await firstTab.json() as { csrfToken: string };
    const secondTabBody = await secondTab.json() as { csrfToken: string };
    assert.equal(firstTabBody.csrfToken, sessionA.csrfToken);
    assert.equal(secondTabBody.csrfToken, sessionA.csrfToken);

    for (const path of [
      "/server-admin/settings",
      "/server-admin/chat/clear-history",
      "/server-admin/update/check",
      "/server-admin/update/install",
      "/server-admin/logout",
    ] as const) {
      const withoutSession = await mutation(path, undefined, sessionA.csrfToken);
      assert.equal(withoutSession.response.status, 401);
      assert.equal(withoutSession.body.code, "server_admin_auth_required");

      for (const csrfToken of [undefined, "wrong-token", sessionB.csrfToken]) {
        const rejected = await mutation(path, sessionA.cookie, csrfToken);
        assert.equal(rejected.response.status, 403);
        assert.equal(rejected.body.code, "server_admin_csrf_invalid");
      }
    }

    const unchanged = await fetch(`${baseUrl}/server-admin/settings`, {
      headers: { cookie: sessionA.cookie },
    });
    const unchangedText = await unchanged.text();
    const unchangedBody = JSON.parse(unchangedText) as {
      chatFeatures: { serverChatEnabled: boolean };
      metrics: { historyEpoch: number };
    };
    assert.equal(unchanged.status, 200);
    assert.equal(unchangedBody.chatFeatures.serverChatEnabled, false);
    assert.equal(unchangedBody.metrics.historyEpoch, 0);

    const validPatch = await mutation(
      "/server-admin/settings",
      sessionA.cookie,
      firstTabBody.csrfToken,
    );
    assert.equal(validPatch.response.status, 200);
    const validClear = await mutation(
      "/server-admin/chat/clear-history",
      sessionA.cookie,
      secondTabBody.csrfToken,
    );
    assert.equal(validClear.response.status, 200);
    assert.equal(validClear.body.historyEpoch, 1);

    for (const secret of [sessionA.csrfToken, sessionB.csrfToken]) {
      assert.equal(validPatch.text.includes(secret), false);
      assert.equal(validClear.text.includes(secret), false);
      assert.equal(unchangedText.includes(secret), false);
    }
    const persistedText = readFileSync(config.serverAdminStateFile, "utf8");
    assert.equal(persistedText.includes(sessionA.csrfToken), false);
    assert.equal(persistedText.includes(sessionB.csrfToken), false);

    const logout = await mutation("/server-admin/logout", sessionA.cookie, sessionA.csrfToken);
    assert.equal(logout.response.status, 204);
    const afterLogout = await mutation(
      "/server-admin/settings",
      sessionA.cookie,
      sessionA.csrfToken,
    );
    assert.equal(afterLogout.response.status, 401);
    assert.equal(afterLogout.body.code, "server_admin_auth_required");

    const replacement = await login();
    const staleToken = await mutation(
      "/server-admin/settings",
      replacement.cookie,
      sessionA.csrfToken,
    );
    assert.equal(staleToken.response.status, 403);
    assert.equal(staleToken.body.code, "server_admin_csrf_invalid");

    const logText = logs.join("\n");
    assert.equal(logText.includes(sessionA.csrfToken), false);
    assert.equal(logText.includes(sessionB.csrfToken), false);
    assert.equal(logText.includes(replacement.csrfToken), false);
  } finally {
    console.log = originalLog;
    await service.close();
    cleanupTempDir(config);
  }
});

test("malformed server admin JSON is no-store and cannot mutate authenticated state", async () => {
  const password = "malformed-admin-password";
  const config = testConfig({
    port: 0,
    serverAdminPasswordHash: hashServerAdminPassword(password),
    serverAdminSessionSecret: "malformed-admin-secret",
  });
  const service = await createLobbyService(config);
  const address = await service.start();
  const baseUrl = `http://127.0.0.1:${address.port}`;
  try {
    async function malformed(
      path: string,
      method: "PATCH" | "POST",
      headers: Record<string, string> = {},
    ) {
      const response = await fetch(`${baseUrl}${path}`, {
        method,
        headers: { "content-type": "application/json", ...headers },
        body: "{",
      });
      assert.equal(response.status, 400, path);
      assert.equal(response.headers.get("cache-control"), "no-store", path);
      assert.deepEqual(await response.json(), {
        code: "invalid_request",
        message: "请求体必须是有效的 JSON。",
      });
      return response;
    }

    await malformed("/server-admin/login", "POST");
    await malformed("/server-admin/settings", "PATCH");
    await malformed("/server-admin/chat/clear-history", "POST");
    await malformed("/server-admin/logout", "POST");

    const login = await fetch(`${baseUrl}/server-admin/login`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ username: "admin", password }),
    });
    assert.equal(login.status, 200);
    const loginBody = await login.json() as { csrfToken: string };
    const cookie = login.headers.get("set-cookie")?.split(";", 1)[0];
    assert.ok(cookie);
    const authHeaders = { cookie, "x-csrf-token": loginBody.csrfToken };

    const before = await fetch(`${baseUrl}/server-admin/settings`, {
      headers: { cookie },
    });
    const beforeBody = await before.json() as {
      chatFeatures: Record<string, boolean>;
      metrics: { historyEpoch: number };
    };

    await malformed("/server-admin/settings", "PATCH", authHeaders);
    await malformed("/server-admin/chat/clear-history", "POST", authHeaders);
    const malformedLogout = await malformed("/server-admin/logout", "POST", authHeaders);
    assert.equal(malformedLogout.headers.get("set-cookie"), null);

    const session = await fetch(`${baseUrl}/server-admin/session`, {
      headers: { cookie },
    });
    assert.equal(session.status, 200, "malformed logout must not delete the session");
    const after = await fetch(`${baseUrl}/server-admin/settings`, {
      headers: { cookie },
    });
    const afterBody = await after.json() as {
      chatFeatures: Record<string, boolean>;
      metrics: { historyEpoch: number };
    };
    assert.deepEqual(afterBody.chatFeatures, beforeBody.chatFeatures);
    assert.equal(afterBody.metrics.historyEpoch, beforeBody.metrics.historyEpoch);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("server admin cookie trusts forwarded HTTPS only from configured proxies", async () => {
  const password = "secure-cookie-password";
  const base = testConfig({
    port: 0,
    serverAdminPasswordHash: hashServerAdminPassword(password),
    serverAdminSessionSecret: "secure-cookie-secret",
  });

  async function loginCookie(config: LobbyServiceConfig) {
    const service = await createLobbyService(config);
    const address = await service.start();
    try {
      const response = await fetch(`http://127.0.0.1:${address.port}/server-admin/login`, {
        method: "POST",
        headers: { "content-type": "application/json", "x-forwarded-proto": "https" },
        body: JSON.stringify({ username: "admin", password }),
      });
      assert.equal(response.status, 200);
      return response.headers.get("set-cookie") ?? "";
    } finally {
      await service.close();
      cleanupTempDir(config);
    }
  }

  const untrusted = await loginCookie(base);
  assert.match(untrusted, /HttpOnly; SameSite=Lax/);
  assert.equal(/; Secure(?:;|$)/.test(untrusted), false);

  const trustedBase = testConfig({
    port: 0,
    serverAdminPasswordHash: hashServerAdminPassword(password),
    serverAdminSessionSecret: "secure-cookie-secret",
  });
  const trusted = await loginCookie({
    ...trustedBase,
    chat: { ...trustedBase.chat, trustedProxyCidrs: ["127.0.0.0/8"] },
  });
  assert.match(trusted, /; Secure(?:;|$)/);
});
