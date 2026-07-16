import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { createServer as createNetServer } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { WebSocket } from "ws";
import { createLobbyService } from "../app.js";
import { loadLobbyServiceConfig, type LobbyServiceConfig } from "../config.js";

type JsonFrame = Record<string, unknown> & { type: string };

interface CreatedRoom {
  roomId: string;
  roomSessionId: string;
  controlChannelId: string;
  hostToken: string;
  room: {
    maxPlayers: number;
    modVersion: string;
    relayState: string;
  };
  relayEndpoint: { host: string; port: number };
}

interface JoinedRoom {
  ticketId: string;
  roomSessionId: string;
  room: { modVersion: string; relayState: string };
  connectionPlan: {
    controlChannelId: string;
    relayAllowed: boolean;
    relayEndpoint: { host: string; port: number };
  };
}

interface ConnectedPeer {
  socket: WebSocket;
  joined: JoinedRoom;
}

interface LegacyFixture {
  modVersion: "0.4.0" | "0.2.2";
}

const fixtures: LegacyFixture[] = [
  { modVersion: "0.4.0" },
  { modVersion: "0.2.2" },
];

const phaseThreeVersions = {
  richContentVersion: 1,
  emojiSetVersion: 1,
  itemRefVersion: 1,
  combatRefVersion: 0,
} as const;

const disabledVersions = {
  richContentVersion: 0,
  emojiSetVersion: 0,
  itemRefVersion: 0,
  combatRefVersion: 0,
} as const;

function compatibilityConfig(tempDir: string): LobbyServiceConfig {
  const relayPortStart = 45_000 + (process.pid % 10_000);
  return loadLobbyServiceConfig({
    HOST: "127.0.0.1",
    PORT: "0",
    PEER_NETWORK_ENABLED: "false",
    PEER_SELF_ADDRESS: "",
    PEER_CF_DISCOVERY_BASE_URL: "",
    SERVER_ADMIN_STATE_FILE: join(tempDir, "server-admin.json"),
    PEER_STATE_DIR: join(tempDir, "peer"),
    ENFORCE_LOBBY_ACCESS_TOKEN: "false",
    ENFORCE_CREATE_ROOM_TOKEN: "false",
    SERVER_CHAT_ENABLED: "true",
    RELAY_BIND_HOST: "127.0.0.1",
    RELAY_PORT_START: String(relayPortStart),
    RELAY_PORT_END: String(relayPortStart + 8),
  });
}

function cleanupTempDir(tempDir: string): void {
  rmSync(tempDir, { recursive: true, force: true });
}

const NoPrimaryFailure = Symbol("no-primary-failure");
type CleanupStep = () => void | Promise<void>;

async function withCompatibilityCleanup<T>(
  body: () => Promise<T>,
  cleanupSteps: readonly CleanupStep[],
): Promise<T> {
  let primaryFailure: unknown | typeof NoPrimaryFailure = NoPrimaryFailure;
  try {
    return await body();
  } catch (error) {
    primaryFailure = error;
    throw error;
  } finally {
    const cleanupErrors: unknown[] = [];
    for (const cleanup of cleanupSteps) {
      try {
        await cleanup();
      } catch (error) {
        cleanupErrors.push(error);
      }
    }
    if (primaryFailure === NoPrimaryFailure && cleanupErrors.length > 0) {
      throw new AggregateError(cleanupErrors, "compatibility fixture cleanup failed");
    }
  }
}

function waitForFrame(
  socket: WebSocket,
  predicate: (frame: JsonFrame) => boolean,
  timeoutMs = 2_000,
): Promise<JsonFrame> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      cleanup();
      reject(new Error("control websocket frame timeout"));
    }, timeoutMs);
    const onMessage = (data: WebSocket.RawData) => {
      const frame = JSON.parse(data.toString()) as JsonFrame;
      if (!predicate(frame)) return;
      cleanup();
      resolve(frame);
    };
    const onClose = (code: number) => {
      cleanup();
      reject(new Error(`control websocket closed before frame (${code})`));
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

function waitForClose(
  socket: WebSocket,
  timeoutMs = 2_000,
): Promise<{ code: number; reason: string }> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      cleanup();
      reject(new Error("control websocket close timeout"));
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

function openControlWebSocket(
  url: string,
): Promise<{ socket: WebSocket; connected: JsonFrame }> {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url);
    let opened = false;
    let connected: JsonFrame | undefined;
    let settled = false;
    let timer: NodeJS.Timeout | undefined;

    const cleanup = () => {
      if (timer) clearTimeout(timer);
      socket.off("open", onOpen);
      socket.off("message", onMessage);
      socket.off("error", onError);
      socket.off("close", onClose);
    };
    const fail = (error: Error) => {
      if (settled) return;
      settled = true;
      cleanup();
      // `terminate()` while connecting may emit one final error asynchronously.
      socket.once("error", () => {});
      try {
        socket.terminate();
      } catch {
        // The socket may already be terminal.
      }
      reject(error);
    };
    const resolveIfReady = () => {
      if (settled || !opened || !connected) return;
      settled = true;
      cleanup();
      resolve({ socket, connected });
    };
    const onOpen = () => {
      opened = true;
      resolveIfReady();
    };
    const onMessage = (data: WebSocket.RawData) => {
      try {
        const frame = JSON.parse(data.toString()) as JsonFrame;
        if (frame.type !== "connected") return;
        connected = frame;
        resolveIfReady();
      } catch (error) {
        fail(error instanceof Error ? error : new Error("invalid control websocket frame"));
      }
    };
    const onError = (error: Error) => fail(error);
    const onClose = (code: number) =>
      fail(new Error(`control websocket closed before connected (${code})`));

    socket.on("open", onOpen);
    socket.on("message", onMessage);
    socket.on("error", onError);
    socket.on("close", onClose);
    timer = setTimeout(() => fail(new Error("control websocket connect timeout")), 2_000);
  });
}

function openServerChatWebSocket(
  url: string,
  ticket: string,
): Promise<{ socket: WebSocket; ready: JsonFrame }> {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url, {
      headers: { authorization: `Bearer ${ticket}` },
    });
    let opened = false;
    let ready: JsonFrame | undefined;
    let settled = false;
    const timer = setTimeout(() => fail(new Error("server chat websocket timeout")), 2_000);
    const cleanup = () => {
      clearTimeout(timer);
      socket.off("open", onOpen);
      socket.off("message", onMessage);
      socket.off("error", onError);
      socket.off("close", onClose);
    };
    const fail = (error: Error) => {
      if (settled) return;
      settled = true;
      cleanup();
      socket.once("error", () => {});
      socket.terminate();
      reject(error);
    };
    const resolveIfReady = () => {
      if (settled || !opened || !ready) return;
      settled = true;
      cleanup();
      resolve({ socket, ready });
    };
    const onOpen = () => {
      opened = true;
      resolveIfReady();
    };
    const onMessage = (data: WebSocket.RawData) => {
      const frame = JSON.parse(data.toString()) as JsonFrame;
      if (frame.type !== "chat_ready") return;
      ready = frame;
      resolveIfReady();
    };
    const onError = (error: Error) => fail(error);
    const onClose = (code: number) => fail(new Error(`server chat closed before ready (${code})`));
    socket.on("open", onOpen);
    socket.on("message", onMessage);
    socket.on("error", onError);
    socket.on("close", onClose);
  });
}

async function reserveThenCloseLocalPort(): Promise<number> {
  const server = createNetServer();
  await new Promise<void>((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", resolve);
  });
  const address = server.address();
  assert.ok(address && typeof address === "object");
  await new Promise<void>((resolve, reject) => {
    server.close((error) => error ? reject(error) : resolve());
  });
  return address.port;
}

function rejectedWebSocketStatus(url: string): Promise<number> {
  const socket = new WebSocket(url);
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      socket.terminate();
      reject(new Error("websocket rejection timeout"));
    }, 2_000);
    socket.once("open", () => {
      clearTimeout(timer);
      socket.terminate();
      reject(new Error("websocket unexpectedly upgraded"));
    });
    socket.once("unexpected-response", (_request, response) => {
      clearTimeout(timer);
      const status = response.statusCode ?? 0;
      response.resume();
      resolve(status);
    });
    socket.once("error", () => {
      // `unexpected-response` is authoritative for an HTTP upgrade rejection.
    });
  });
}

async function joinRoom(
  baseUrl: string,
  roomId: string,
  playerName: string,
  playerNetId: string,
): Promise<JoinedRoom> {
  const response = await fetch(`${baseUrl}/rooms/${encodeURIComponent(roomId)}/join`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      playerName,
      version: "1.0.0",
      modVersion: "0.4.0",
      modList: ["sts2_lan_connect"],
      playerNetId,
    }),
  });
  assert.equal(response.status, 200, `join failed for ${playerName}`);
  return (await response.json()) as JoinedRoom;
}

async function openJoinedPeer(
  wsBase: string,
  wsPath: string,
  created: CreatedRoom,
  joined: JoinedRoom,
): Promise<ConnectedPeer> {
  const query =
    `roomId=${encodeURIComponent(created.roomId)}` +
    `&controlChannelId=${encodeURIComponent(created.controlChannelId)}` +
    `&role=client&ticketId=${encodeURIComponent(joined.ticketId)}`;
  const opened = await openControlWebSocket(`${wsBase}${wsPath}?${query}`);
  return { socket: opened.socket, joined };
}

test("control websocket connection failure rejects once without an unhandled promise", async () => {
  const port = await reserveThenCloseLocalPort();
  const unhandled: unknown[] = [];
  const onUnhandled = (reason: unknown) => unhandled.push(reason);
  process.on("unhandledRejection", onUnhandled);
  try {
    await assert.rejects(
      openControlWebSocket(`ws://127.0.0.1:${port}/control`),
      /ECONNREFUSED|connect/i,
    );
    await new Promise<void>((resolve) => setImmediate(resolve));
    assert.deepEqual(unhandled, []);
  } finally {
    process.off("unhandledRejection", onUnhandled);
  }
});

test("compatibility cleanup reports cleanup failures without replacing a primary failure", async (t) => {
  await t.test("successful body rejects with every cleanup failure", async () => {
    const first = new Error("first cleanup failed");
    const second = new Error("second cleanup failed");
    const visited: string[] = [];

    await assert.rejects(
      withCompatibilityCleanup(
        async () => {
          visited.push("body");
        },
        [
          () => {
            visited.push("first");
            throw first;
          },
          async () => {
            visited.push("second");
            throw second;
          },
        ],
      ),
      (error) => {
        assert.ok(error instanceof AggregateError);
        assert.deepEqual(error.errors, [first, second]);
        return true;
      },
    );
    assert.deepEqual(visited, ["body", "first", "second"]);
  });

  await t.test("failed body keeps its primary error after every cleanup runs", async () => {
    const primary = new Error("body failed");
    const visited: string[] = [];

    await assert.rejects(
      withCompatibilityCleanup(
        async () => {
          throw primary;
        },
        [
          () => {
            visited.push("first");
            throw new Error("cleanup failed");
          },
          () => {
            visited.push("second");
          },
        ],
      ),
      (error) => error === primary,
    );
    assert.deepEqual(visited, ["first", "second"]);
  });
});

test("real room gateway routes rich and exact legacy fallback per recipient and rejects combat", async () => {
  let service: Awaited<ReturnType<typeof createLobbyService>> | undefined;
  const sockets: WebSocket[] = [];
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-lobby-rich-compat-"));

  await withCompatibilityCleanup(
    async () => {
      const config = compatibilityConfig(tempDir);
      service = await createLobbyService(config);
      const address = await service.start();
      const httpBase = `http://127.0.0.1:${address.port}`;
      const wsBase = `ws://127.0.0.1:${address.port}`;
      const serverTicketResponse = await fetch(`${httpBase}/chat/tickets`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          protocolVersion: 1,
          playerNetId: "net:server-channel",
          playerName: "ServerChannel",
        }),
      });
      assert.equal(serverTicketResponse.status, 200);
      const issuedServerTicket = await serverTicketResponse.json() as {
        ticket: string;
        webSocketUrl: string;
      };
      const openedServerChat = await openServerChatWebSocket(
        issuedServerTicket.webSocketUrl,
        issuedServerTicket.ticket,
      );
      const serverChat = openedServerChat.socket;
      sockets.push(serverChat);
      assert.equal(openedServerChat.ready.channel, "server");
      const serverFrames: JsonFrame[] = [];
      serverChat.on("message", (data) => {
        serverFrames.push(JSON.parse(data.toString()) as JsonFrame);
      });
      const createResponse = await fetch(`${httpBase}/rooms`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          roomName: "phase-three-compat",
          hostPlayerName: "Host",
          gameMode: "standard",
          version: "1.0.0",
          modVersion: "0.4.0",
          modList: ["sts2_lan_connect"],
          maxPlayers: 5,
          hostConnectionInfo: {
            enetPort: 7777,
            localAddresses: ["127.0.0.1"],
          },
        }),
      });
      assert.equal(createResponse.status, 201);
      const created = (await createResponse.json()) as CreatedRoom;

      const richJoined = await joinRoom(httpBase, created.roomId, "Rich", "net:rich");
      const legacyJoined = await joinRoom(httpBase, created.roomId, "Legacy", "net:legacy");
      const disabledJoined = await joinRoom(httpBase, created.roomId, "Disabled", "net:disabled");
      const oldSenderJoined = await joinRoom(httpBase, created.roomId, "OldSender", "net:old");

      const hostQuery =
        `roomId=${encodeURIComponent(created.roomId)}` +
        `&controlChannelId=${encodeURIComponent(created.controlChannelId)}` +
        `&role=host&token=${encodeURIComponent(created.hostToken)}`;
      const openedHost = await openControlWebSocket(`${wsBase}${config.wsPath}?${hostQuery}`);
      const host = openedHost.socket;
      sockets.push(host);
      const openedMutatingHost = await openControlWebSocket(`${wsBase}${config.wsPath}?${hostQuery}`);
      const mutatingHost = openedMutatingHost.socket;
      sockets.push(mutatingHost);
      const rich = await openJoinedPeer(wsBase, config.wsPath, created, richJoined);
      const legacy = await openJoinedPeer(wsBase, config.wsPath, created, legacyJoined);
      const disabled = await openJoinedPeer(wsBase, config.wsPath, created, disabledJoined);
      const oldSender = await openJoinedPeer(wsBase, config.wsPath, created, oldSenderJoined);
      sockets.push(rich.socket, legacy.socket, disabled.socket, oldSender.socket);

      const hostReadyPromise = waitForFrame(host, (frame) => frame.type === "room_chat_ready");
      host.send(JSON.stringify({
        type: "host_hello",
        roomId: created.roomId,
        controlChannelId: created.controlChannelId,
        role: "host",
        playerName: "Host",
        playerNetId: "net:host",
        roomChatVersions: phaseThreeVersions,
      }));
      const hostReady = await hostReadyPromise;
      assert.deepEqual(hostReady.enabledFeatures, phaseThreeVersions);

      const mutatingReadyPromise = waitForFrame(
        mutatingHost,
        (frame) => frame.type === "room_chat_ready",
      );
      mutatingHost.send(JSON.stringify({
        type: "host_hello",
        roomId: created.roomId,
        controlChannelId: created.controlChannelId,
        role: "host",
        playerName: "Mutation Probe",
        playerNetId: "net:mutation-probe",
        roomChatVersions: phaseThreeVersions,
      }));
      await mutatingReadyPromise;

      const richReadyPromise = waitForFrame(rich.socket, (frame) => frame.type === "room_chat_ready");
      rich.socket.send(JSON.stringify({
        type: "client_hello",
        roomId: created.roomId,
        controlChannelId: created.controlChannelId,
        role: "client",
        playerName: "Rich",
        playerNetId: "net:rich",
        roomChatVersions: phaseThreeVersions,
      }));
      assert.deepEqual((await richReadyPromise).enabledFeatures, phaseThreeVersions);

      legacy.socket.send(JSON.stringify({
        type: "client_hello",
        roomId: created.roomId,
        controlChannelId: created.controlChannelId,
        role: "client",
        ticketId: legacy.joined.ticketId,
        playerName: "Legacy",
        playerNetId: "net:legacy",
      }));

      const disabledReadyPromise = waitForFrame(
        disabled.socket,
        (frame) => frame.type === "room_chat_ready",
      );
      disabled.socket.send(JSON.stringify({
        type: "client_hello",
        roomId: created.roomId,
        controlChannelId: created.controlChannelId,
        role: "client",
        playerName: "Disabled",
        playerNetId: "net:disabled",
        roomChatVersions: disabledVersions,
      }));
      assert.deepEqual((await disabledReadyPromise).enabledFeatures, disabledVersions);

      oldSender.socket.send(JSON.stringify({
        type: "client_hello",
        roomId: created.roomId,
        controlChannelId: created.controlChannelId,
        role: "client",
        ticketId: oldSender.joined.ticketId,
        playerName: "OldSender",
        playerNetId: "net:old",
      }));

      const content = {
        formatVersion: 1,
        segments: [
          { kind: "text", text: "look " },
          { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike", upgradeLevel: 1 },
          { kind: "emoji", emojiId: "heart" },
        ],
      };
      const clientMessageId = "31313131-3131-4131-8131-313131313131";
      const richBroadcasts: JsonFrame[] = [];
      rich.socket.on("message", (data) => {
        const frame = JSON.parse(data.toString()) as JsonFrame;
        if (frame.type === "room_chat_message") richBroadcasts.push(frame);
      });
      const ackPromise = waitForFrame(
        host,
        (frame) => frame.type === "room_chat_ack" && frame.clientMessageId === clientMessageId,
      );
      const selfPromise = waitForFrame(
        host,
        (frame) => frame.type === "room_chat_message" &&
          (frame.message as Record<string, unknown> | undefined)?.plainTextFallback === "look [Card][Emoji]",
      );
      const richPromise = waitForFrame(
        rich.socket,
        (frame) => frame.type === "room_chat_message",
      );
      const legacyPromise = waitForFrame(
        legacy.socket,
        (frame) => frame.type === "room_chat" && frame.messageText === "look [Card][Emoji]",
      );
      const disabledPromise = waitForFrame(
        disabled.socket,
        (frame) => frame.type === "room_chat" && frame.messageText === "look [Card][Emoji]",
      );
      host.send(JSON.stringify({
        type: "room_chat_v2",
        protocolVersion: 1,
        clientMessageId,
        roomId: created.roomId,
        roomSessionId: created.roomSessionId,
        content,
      }));

      const ack = await ackPromise;
      const self = await selfPromise;
      const richFrame = await richPromise;
      const legacyFrame = await legacyPromise;
      const disabledFrame = await disabledPromise;
      assert.deepEqual(ack.message, self.message);
      const richMessage = richFrame.message as Record<string, unknown>;
      assert.deepEqual(richMessage.content, content);
      assert.equal(richMessage.plainTextFallback, "look [Card][Emoji]");
      assert.equal(richBroadcasts.length, 1);
      for (const fallback of [legacyFrame, disabledFrame]) {
        assert.equal(fallback.messageText, "look [Card][Emoji]");
        assert.equal("content" in fallback, false);
        assert.equal("modelId" in fallback, false);
        assert.equal(JSON.stringify(fallback).includes("MegaCrit.Strike"), false);
        assert.equal(String(fallback.messageText).includes("Host"), false);
      }
      assert.equal(
        serverFrames.some((frame) => frame.type.startsWith("room_")),
        false,
        "room messages must never enter the server channel socket",
      );

      const roomSockets = [host, rich.socket, legacy.socket, disabled.socket, oldSender.socket];
      const crossChannelFrames: JsonFrame[] = [];
      const crossChannelListeners = roomSockets.map((socket) => {
        const listener = (data: WebSocket.RawData) => {
          const frame = JSON.parse(data.toString()) as JsonFrame;
          if (frame.type.startsWith("chat_")) crossChannelFrames.push(frame);
        };
        socket.on("message", listener);
        return listener;
      });
      const serverClientMessageId = "51515151-5151-4151-8151-515151515151";
      const serverAckPromise = waitForFrame(
        serverChat,
        (frame) => frame.type === "chat_ack" && frame.clientMessageId === serverClientMessageId,
      );
      const serverBroadcastPromise = waitForFrame(
        serverChat,
        (frame) => frame.type === "chat_message",
      );
      serverChat.send(JSON.stringify({
        type: "chat_send",
        protocolVersion: 1,
        channel: "server",
        clientMessageId: serverClientMessageId,
        content: {
          formatVersion: 1,
          segments: [{ kind: "text", text: "server channel stays independent" }],
        },
      }));
      const [serverAck, serverBroadcast] = await Promise.all([
        serverAckPromise,
        serverBroadcastPromise,
      ]);
      assert.deepEqual(serverAck.message, serverBroadcast.message);
      await new Promise<void>((resolve) => setImmediate(resolve));
      assert.deepEqual(crossChannelFrames, []);
      roomSockets.forEach((socket, index) => {
        socket.off("message", crossChannelListeners[index]!);
      });

      const duplicateAckPromise = waitForFrame(
        host,
        (frame) => frame.type === "room_chat_ack" && frame.clientMessageId === clientMessageId,
      );
      host.send(JSON.stringify({
        type: "room_chat_v2",
        protocolVersion: 1,
        clientMessageId,
        roomId: created.roomId,
        roomSessionId: created.roomSessionId,
        content,
      }));
      assert.deepEqual(await duplicateAckPromise, ack);
      await new Promise<void>((resolve) => setImmediate(resolve));
      assert.equal(richBroadcasts.length, 1, "same-connection dedupe must not rebroadcast");

      const identityErrorPromise = waitForFrame(
        mutatingHost,
        (frame) => frame.type === "room_chat_error" && frame.code === "protocol_mismatch",
      );
      const identityClosePromise = waitForClose(mutatingHost);
      mutatingHost.send(JSON.stringify({
        type: "host_hello",
        roomId: created.roomId,
        controlChannelId: created.controlChannelId,
        role: "host",
        playerName: "Mallory",
        playerNetId: "net:mallory",
        roomChatVersions: phaseThreeVersions,
      }));
      assert.equal((await identityErrorPromise).clientMessageId, "");
      assert.deepEqual(await identityClosePromise, { code: 1002, reason: "protocol_mismatch" });

      const afterLockId = "32323232-3232-4232-8232-323232323232";
      const afterLockAckPromise = waitForFrame(
        host,
        (frame) => frame.type === "room_chat_ack" && frame.clientMessageId === afterLockId,
      );
      host.send(JSON.stringify({
        type: "room_chat_v2",
        protocolVersion: 1,
        clientMessageId: afterLockId,
        roomId: created.roomId,
        roomSessionId: created.roomSessionId,
        content: { formatVersion: 1, segments: [{ kind: "text", text: "identity stays locked" }] },
      }));
      const afterLockMessage = (await afterLockAckPromise).message as Record<string, unknown>;
      assert.equal(afterLockMessage.senderName, "Host");
      assert.equal(afterLockMessage.senderId, "net:host");

      const oldMessageId = "old-sender-text";
      const oldToNewPromise = waitForFrame(
        rich.socket,
        (frame) => frame.type === "room_chat" && frame.messageId === oldMessageId,
      );
      oldSender.socket.send(JSON.stringify({
        type: "room_chat",
        roomId: created.roomId,
        controlChannelId: created.controlChannelId,
        role: "client",
        ticketId: oldSender.joined.ticketId,
        playerName: "OldSender",
        playerNetId: "net:old",
        messageId: oldMessageId,
        messageText: "legacy sender text",
        sentAtUnixMs: 1_783_857_600_000,
      }));
      assert.deepEqual(await oldToNewPromise, {
        type: "room_chat",
        roomId: created.roomId,
        controlChannelId: created.controlChannelId,
        role: "client",
        ticketId: oldSender.joined.ticketId,
        playerName: "OldSender",
        playerNetId: "net:old",
        messageId: oldMessageId,
        messageText: "legacy sender text",
        sentAtUnixMs: 1_783_857_600_000,
      });

      for (const [index, segment] of [
        { kind: "power_state", modelId: "MegaCrit.Strength", amount: 1, roomSessionId: created.roomSessionId },
        { kind: "target_ref", targetKind: "player", targetKey: "net:rich", roomSessionId: created.roomSessionId },
      ].entries()) {
        const rejectedId = `41414141-4141-4141-8141-41414141414${index}`;
        const rejectedPromise = waitForFrame(
          host,
          (frame) => frame.type === "room_chat_error" && frame.clientMessageId === rejectedId,
        );
        host.send(JSON.stringify({
          type: "room_chat_v2",
          protocolVersion: 1,
          clientMessageId: rejectedId,
          roomId: created.roomId,
          roomSessionId: created.roomSessionId,
          content: { formatVersion: 1, segments: [segment] },
        }));
        const rejected = await rejectedPromise;
        assert.equal(rejected.code, "feature_disabled");
      }
    },
    [
      () => {
        for (const socket of sockets) socket.terminate();
      },
      async () => {
        await service?.close();
      },
      () => cleanupTempDir(tempDir),
    ],
  );
});

test("new server preserves 0.4.0 and 0.2.2 legacy lobby/control compatibility", async (t) => {
  for (const fixture of fixtures) {
    await t.test(`mod ${fixture.modVersion}`, async () => {
      let service: Awaited<ReturnType<typeof createLobbyService>> | undefined;
      const sockets: WebSocket[] = [];
      const tempDir = mkdtempSync(join(tmpdir(), "sts2-lobby-compat-"));

      await withCompatibilityCleanup(
        async () => {
          const config = compatibilityConfig(tempDir);
          service = await createLobbyService(config);
          const address = await service.start();
          const createBody: Record<string, unknown> = {
            roomName: `compat-${fixture.modVersion}`,
            hostPlayerName: "Host",
            gameMode: "standard",
            version: "1.0.0",
            modVersion: fixture.modVersion,
            modList: ["sts2_lan_connect"],
            maxPlayers: 4,
            hostConnectionInfo: {
              enetPort: 7777,
              localAddresses: ["127.0.0.1"],
            },
          };

          const createResponse = await fetch(`http://127.0.0.1:${address.port}/rooms`, {
            method: "POST",
            headers: { "content-type": "application/json" },
            body: JSON.stringify(createBody),
          });
          assert.equal(createResponse.status, 201, `create status drifted for ${fixture.modVersion}`);
          const created = (await createResponse.json()) as CreatedRoom;
          assert.equal(created.room.modVersion, fixture.modVersion);
          assert.equal(created.room.maxPlayers, 4);
          assert.equal(created.room.relayState, "planned");
          assert.equal(typeof created.roomSessionId, "string");
          assert.ok(created.roomSessionId.length > 0);
          assert.ok(created.relayEndpoint.port > 0, "create must retain Relay-adjacent allocation");

          const joinResponse = await fetch(
            `http://127.0.0.1:${address.port}/rooms/${encodeURIComponent(created.roomId)}/join`,
            {
              method: "POST",
              headers: { "content-type": "application/json" },
              body: JSON.stringify({
                playerName: "Silent",
                version: "1.0.0",
                modVersion: fixture.modVersion,
                modList: ["sts2_lan_connect"],
                playerNetId: "222",
              }),
            },
          );
          assert.equal(joinResponse.status, 200, `join status drifted for ${fixture.modVersion}`);
          const joined = (await joinResponse.json()) as JoinedRoom;
          assert.equal(joined.roomSessionId, created.roomSessionId, "roomSessionId must be additive and stable");
          assert.equal(joined.room.modVersion, fixture.modVersion);
          assert.equal(joined.room.relayState, "planned");
          assert.equal(joined.connectionPlan.controlChannelId, created.controlChannelId);
          assert.equal(joined.connectionPlan.relayAllowed, true);
          assert.deepEqual(joined.connectionPlan.relayEndpoint, created.relayEndpoint);

          const queryBase =
            `roomId=${encodeURIComponent(created.roomId)}` +
            `&controlChannelId=${encodeURIComponent(created.controlChannelId)}`;
          const openedHost = await openControlWebSocket(
            `ws://127.0.0.1:${address.port}${config.wsPath}?${queryBase}` +
              `&role=host&token=${encodeURIComponent(created.hostToken)}`,
          );
          const host = openedHost.socket;
          sockets.push(host);
          const openedClient = await openControlWebSocket(
            `ws://127.0.0.1:${address.port}${config.wsPath}?${queryBase}` +
              `&role=client&ticketId=${encodeURIComponent(joined.ticketId)}`,
          );
          const client = openedClient.socket;
          sockets.push(client);

          assert.equal(openedHost.connected.role, "host");
          assert.equal(openedClient.connected.role, "client");

          const hostHello =
            `{"type":"host_hello","roomId":"${created.roomId}",` +
            `"controlChannelId":"${created.controlChannelId}","role":"host","playerName":"Host"}`;
          const clientHello =
            `{"type":"client_hello","roomId":"${created.roomId}",` +
            `"controlChannelId":"${created.controlChannelId}","role":"client",` +
            `"ticketId":"${joined.ticketId}","playerName":"Silent","playerNetId":"222"}`;
          host.send(hostHello);
          client.send(clientHello);

          const expectedRoomChat = {
            type: "room_chat",
            roomId: created.roomId,
            controlChannelId: created.controlChannelId,
            role: "client",
            ticketId: joined.ticketId,
            playerName: "Silent",
            playerNetId: "222",
            messageId: "legacy-message-1",
            messageText: "legacy text",
            sentAtUnixMs: 1_783_857_600_000,
          };
          const relayedPromise = waitForFrame(
            host,
            (frame) => frame.type === "room_chat" && frame.messageId === "legacy-message-1",
          );
          const roomChat =
            `{"type":"room_chat","roomId":"${created.roomId}",` +
            `"controlChannelId":"${created.controlChannelId}","role":"client",` +
            `"ticketId":"${joined.ticketId}","playerName":"Silent","playerNetId":"222",` +
            `"messageId":"legacy-message-1","messageText":"legacy text",` +
            `"sentAtUnixMs":1783857600000}`;
          client.send(roomChat);
          assert.deepEqual(await relayedPromise, expectedRoomChat, "legacy room_chat fields must remain exact");

          assert.equal(
            await rejectedWebSocketStatus(
              `ws://127.0.0.1:${address.port}/chat?${queryBase}` +
                `&role=host&token=${encodeURIComponent(created.hostToken)}`,
            ),
            401,
            "/chat must not accept /control query credentials",
          );
        },
        [
          () => {
            const errors: unknown[] = [];
            for (const socket of sockets) {
              try {
                socket.terminate();
              } catch (error) {
                errors.push(error);
              }
            }
            if (errors.length > 0) {
              throw new AggregateError(errors, "compatibility sockets failed to terminate");
            }
          },
          async () => {
            await service?.close();
          },
          () => {
            cleanupTempDir(tempDir);
          },
        ],
      );
    });
  }
});

test("passworded continue-game rooms remain coherent across delete and join races", async () => {
  let service: Awaited<ReturnType<typeof createLobbyService>> | undefined;
  const sockets: WebSocket[] = [];
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-lobby-continue-compat-"));

  await withCompatibilityCleanup(
    async () => {
      const config = compatibilityConfig(tempDir);
      service = await createLobbyService(config);
      const address = await service.start();
      const baseUrl = `http://127.0.0.1:${address.port}`;
      const createResponse = await fetch(`${baseUrl}/rooms`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          roomName: "continue-compat",
          password: "secret",
          hostPlayerName: "Host",
          gameMode: "standard",
          version: "1.0.0",
          modVersion: "0.4.0",
          modList: ["sts2_lan_connect"],
          maxPlayers: 4,
          hostConnectionInfo: { enetPort: 7777, localAddresses: ["127.0.0.1"] },
          savedRun: {
            saveKey: "save-compat",
            slots: [
              { netId: "1", characterId: "IRONCLAD", isHost: true },
              { netId: "222", characterId: "SILENT", isHost: false },
              { netId: "333", characterId: "DEFECT", isHost: false },
            ],
            connectedPlayerNetIds: ["1"],
          },
        }),
      });
      const createResult = await createResponse.json() as CreatedRoom & { code?: string; message?: string };
      assert.equal(createResponse.status, 201, JSON.stringify(createResult));
      const created = createResult;
      assert.ok(created.relayEndpoint.port > 0, "continue room must retain Relay allocation");

      const joinBody = (password: string, desiredSavePlayerNetId: string, playerName: string) => ({
        playerName,
        playerNetId: `live:${playerName}`,
        password,
        version: "1.0.0",
        modVersion: "0.4.0",
        modList: ["sts2_lan_connect"],
        desiredSavePlayerNetId,
      });
      const wrongPassword = await fetch(`${baseUrl}/rooms/${created.roomId}/join`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(joinBody("wrong", "222", "Wrong")),
      });
      assert.equal(wrongPassword.status, 401);
      assert.equal((await wrongPassword.json() as { code?: string }).code, "invalid_password");

      const heartbeat = await fetch(`${baseUrl}/rooms/${created.roomId}/heartbeat`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          hostToken: created.hostToken,
          currentPlayers: 1,
          status: "starting",
          connectedPlayerNetIds: ["1"],
        }),
      });
      assert.equal(heartbeat.status, 200);

      const continueResponse = await fetch(`${baseUrl}/rooms/${created.roomId}/join`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(joinBody("secret", "222", "Continue")),
      });
      assert.equal(continueResponse.status, 200);
      const continued = await continueResponse.json() as JoinedRoom & {
        room: JoinedRoom["room"] & { status: string; savedRun?: { saveKey: string } };
      };
      assert.equal(continued.room.status, "open");
      assert.equal(continued.room.savedRun?.saveKey, "save-compat");
      assert.equal(continued.roomSessionId, created.roomSessionId);
      assert.deepEqual(continued.connectionPlan.relayEndpoint, created.relayEndpoint);

      const query =
        `roomId=${encodeURIComponent(created.roomId)}`
        + `&controlChannelId=${encodeURIComponent(created.controlChannelId)}`
        + `&role=client&ticketId=${encodeURIComponent(continued.ticketId)}`;
      const opened = await openControlWebSocket(
        `ws://127.0.0.1:${address.port}${config.wsPath}?${query}`,
      );
      sockets.push(opened.socket);
      const deletedClose = waitForClose(opened.socket);

      const [deleteResponse, racingJoin] = await Promise.all([
        fetch(`${baseUrl}/rooms/${created.roomId}`, {
          method: "DELETE",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ hostToken: created.hostToken }),
        }),
        fetch(`${baseUrl}/rooms/${created.roomId}/join`, {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify(joinBody("secret", "333", "Racer")),
        }),
      ]);
      assert.equal(deleteResponse.status, 204);
      assert.equal([200, 404].includes(racingJoin.status), true, "join may linearize before or after delete");
      assert.deepEqual(await deletedClose, { code: 4000, reason: "room_deleted" });

      const afterDelete = await fetch(`${baseUrl}/rooms/${created.roomId}/join`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(joinBody("secret", "333", "AfterDelete")),
      });
      assert.equal(afterDelete.status, 404);
    },
    [
      () => {
        for (const socket of sockets) socket.terminate();
      },
      async () => {
        await service?.close();
      },
      () => cleanupTempDir(tempDir),
    ],
  );
});
