import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import type { WebSocket } from "ws";
import { WebSocket as RealWebSocket } from "ws";
import { createLobbyService } from "../app.js";
import { loadLobbyServiceConfig } from "../config.js";
import { ServerChatGateway } from "./gateway.js";
import type { ChatFeatureVersions } from "./feature-resolver.js";
import { ChatPeerRegistry, type ChatSocket } from "./peer-registry.js";
import type { ReservedChatTicket } from "./ticket-store.js";

class FakeSocket extends EventEmitter implements ChatSocket {
  readyState = 1;
  bufferedAmount = 0;
  readonly sent: string[] = [];
  readonly closes: Array<{ code: number; reason: string }> = [];
  terminateCalls = 0;
  private readonly callbacks: Array<(error?: Error) => void> = [];

  send(data: string, callback: (error?: Error) => void): void {
    this.sent.push(data);
    this.callbacks.push(callback);
  }

  ping(): void {}

  close(code: number, reason: string): void {
    this.closes.push({ code, reason });
    this.readyState = 3;
  }

  terminate(): void {
    this.terminateCalls += 1;
    this.readyState = 3;
  }

  flushOne(): void {
    this.callbacks.shift()?.();
  }

  flushAll(): void {
    while (this.callbacks.length > 0) {
      this.flushOne();
    }
  }
}

class ThrowingSnapshotRegistry extends ChatPeerRegistry {
  failSnapshot = true;

  override enqueueSnapshot(sessionId: string, frames: readonly object[]): Promise<void> {
    if (this.failSnapshot) {
      throw new Error("injected snapshot failure");
    }
    return super.enqueueSnapshot(sessionId, frames);
  }
}

const ticket: ReservedChatTicket = {
  id: "reservation-1",
  protocolVersion: 1,
  playerNetId: "steam:42",
  playerName: "Alice",
  clientIp: "203.0.113.10",
  expiresAt: "2026-07-13T00:01:00.000Z",
};

const richFeatures: ChatFeatureVersions = {
  richContentVersion: 1,
  emojiSetVersion: 1,
  itemRefVersion: 1,
  combatRefVersion: 0,
};

function frames(socket: FakeSocket): Array<Record<string, unknown>> {
  return socket.sent.map((payload) => JSON.parse(payload) as Record<string, unknown>);
}

async function settle(...sockets: FakeSocket[]): Promise<void> {
  for (let index = 0; index < 4; index += 1) {
    for (const socket of sockets) {
      socket.flushAll();
    }
    await new Promise<void>((resolve) => setImmediate(resolve));
  }
}

function chatSend(clientMessageId: string, text: string): string {
  return JSON.stringify({
    type: "chat_send",
    protocolVersion: 1,
    channel: "server",
    clientMessageId,
    content: { formatVersion: 1, segments: [{ kind: "text", text }] },
  });
}

function chatSendContent(clientMessageId: string, content: unknown): string {
  return JSON.stringify({
    type: "chat_send",
    protocolVersion: 1,
    channel: "server",
    clientMessageId,
    content,
  });
}

test("accept sends ready before snapshot and rejects input until the snapshot barrier", async () => {
  const socket = new FakeSocket();
  const gateway = new ServerChatGateway({
    peerRegistry: new ChatPeerRegistry(),
    chatEnabled: true,
    maxPayloadBytes: 8192,
  });

  gateway.accept(socket as unknown as WebSocket, ticket);
  assert.equal(frames(socket)[0]?.type, "chat_ready");

  socket.emit("message", JSON.stringify({
    type: "chat_send",
    protocolVersion: 1,
    clientMessageId: "11111111-1111-4111-8111-111111111111",
    content: { formatVersion: 1, segments: [{ kind: "text", text: "too early" }] },
  }), false);
  socket.flushAll();
  await new Promise<void>((resolve) => setImmediate(resolve));

  const all = frames(socket);
  assert.deepEqual(all.map((frame) => frame.type), [
    "chat_ready",
    "chat_snapshot_begin",
    "chat_snapshot_end",
    "chat_error",
  ]);
  assert.equal(all[3]?.code, "protocol_mismatch");

  const ready = all[0]!;
  assert.match(String(ready.sessionId), /^[0-9a-f-]{36}$/);
  assert.match(String(ready.senderId), /^[A-Za-z0-9_-]{22}$/);
  assert.match(String(ready.instanceId), /^[0-9a-f-]{36}$/);
  assert.equal(ready.historyEpoch, 0);
  assert.equal(ready.chatEnabled, true);
  assert.equal(ready.channel, "server");
  assert.equal(ready.serverChatVersion, 1);
  assert.deepEqual(ready.enabledFeatures, {
    richContentVersion: 1,
    emojiSetVersion: 1,
    itemRefVersion: 1,
    combatRefVersion: 0,
  });
  assert.equal(ready.combatRefVersion, undefined);
});

test("valid text is acknowledged and broadcast with authoritative identity, then replayed once", async () => {
  const gateway = new ServerChatGateway({ chatEnabled: true, maxPayloadBytes: 8192 });
  const alice = new FakeSocket();
  const bob = new FakeSocket();
  gateway.accept(alice as unknown as WebSocket, ticket);
  gateway.accept(bob as unknown as WebSocket, { ...ticket, id: "reservation-2", playerName: "Bob" });
  await settle(alice, bob);
  const aliceStart = alice.sent.length;
  const bobStart = bob.sent.length;

  const id = "22222222-2222-4222-8222-222222222222";
  alice.emit("message", chatSend(id, "  hello\r\nworld  "), false);
  await settle(alice, bob);

  const aliceNew = frames(alice).slice(aliceStart);
  const bobNew = frames(bob).slice(bobStart);
  assert.deepEqual(aliceNew.map((frame) => frame.type), ["chat_ack", "chat_message"]);
  assert.deepEqual(bobNew.map((frame) => frame.type), ["chat_message"]);
  const message = aliceNew[0]?.message as Record<string, unknown>;
  assert.match(String(message.messageId), /^[0-9a-f-]{36}$/);
  assert.equal(message.senderId, frames(alice)[0]?.senderId);
  assert.equal(message.senderName, "Alice");
  assert.equal(message.plainTextFallback, "hello\nworld");
  assert.match(String(message.sentAt), /^2026-|^20\d\d-/);
  assert.equal(aliceNew[0]?.clientMessageId, id);
  assert.ok(alice.sent.slice(aliceStart).every((payload) => Buffer.byteLength(payload) <= 8192));

  const beforeReplayAlice = alice.sent.length;
  const beforeReplayBob = bob.sent.length;
  alice.emit("message", chatSend(id, "hello\nworld"), false);
  await settle(alice, bob);
  assert.deepEqual(frames(alice).slice(beforeReplayAlice).map((frame) => frame.type), ["chat_ack"]);
  assert.equal(alice.sent.length - beforeReplayAlice, 1);
  assert.equal(bob.sent.length, beforeReplayBob);

  alice.emit("message", chatSend(id, "changed"), false);
  await settle(alice, bob);
  const conflict = frames(alice).at(-1)!;
  assert.equal(conflict.type, "chat_error");
  assert.equal(conflict.code, "duplicate_message");
});

test("rich server content is canonicalized into ACK history and self-broadcast", async () => {
  const gateway = new ServerChatGateway({
    chatEnabled: true,
    compiledFeatures: richFeatures,
    configuredFeatures: richFeatures,
  });
  const alice = new FakeSocket();
  gateway.accept(alice as unknown as WebSocket, ticket);
  await settle(alice);
  const start = alice.sent.length;
  const id = "23232323-2323-4323-8323-232323232323";
  alice.emit("message", chatSendContent(id, {
    formatVersion: 1,
    segments: [
      { kind: "text", text: "  look " },
      { kind: "emoji", emojiId: "heart" },
      { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike", upgradeLevel: 1 },
    ],
  }), false);
  await settle(alice);

  const sent = frames(alice).slice(start);
  assert.deepEqual(sent.map((frame) => frame.type), ["chat_ack", "chat_message"]);
  assert.deepEqual(sent[0]?.message, sent[1]?.message);
  const message = sent[0]?.message as Record<string, unknown>;
  assert.deepEqual(message.content, {
    formatVersion: 1,
    segments: [
      { kind: "text", text: "look " },
      { kind: "emoji", emojiId: "heart" },
      { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike", upgradeLevel: 1 },
    ],
  });
  assert.equal(message.plainTextFallback, "look [Emoji][Card]");

  const newcomer = new FakeSocket();
  gateway.accept(newcomer as unknown as WebSocket, { ...ticket, id: "rich-history" });
  await settle(newcomer);
  const snapshotMessage = frames(newcomer)
    .filter((frame) => frame.type === "chat_snapshot_chunk")
    .flatMap((frame) => frame.messages as Array<Record<string, unknown>>)
    .find((candidate) => candidate.messageId === message.messageId);
  assert.deepEqual(snapshotMessage, message);
  await gateway.close();
});

test("server rich feature gates and combat rejection use exact error codes", async () => {
  const gateway = new ServerChatGateway({
    chatEnabled: true,
    compiledFeatures: richFeatures,
    configuredFeatures: { ...richFeatures, emojiSetVersion: 0 },
  });
  const socket = new FakeSocket();
  gateway.accept(socket as unknown as WebSocket, ticket);
  await settle(socket);

  socket.emit("message", chatSendContent(
    "24242424-2424-4424-8424-242424242424",
    { formatVersion: 1, segments: [{ kind: "emoji", emojiId: "heart" }] },
  ), false);
  await settle(socket);
  assert.equal(frames(socket).at(-1)?.code, "feature_disabled");

  socket.emit("message", chatSendContent(
    "25252525-2525-4525-8525-252525252525",
    {
      formatVersion: 1,
      segments: [{ kind: "power_state", modelId: "MegaCrit.Strength", amount: 1, roomSessionId: "s" }],
    },
  ), false);
  await settle(socket);
  assert.equal(frames(socket).at(-1)?.code, "invalid_content");
  await gateway.close();
});

test("mixed canonical content dedupes without losing rich segments", async () => {
  const gateway = new ServerChatGateway({
    chatEnabled: true,
    compiledFeatures: richFeatures,
    configuredFeatures: richFeatures,
  });
  const socket = new FakeSocket();
  gateway.accept(socket as unknown as WebSocket, ticket);
  await settle(socket);
  const id = "26262626-2626-4626-8626-262626262626";
  const first = {
    segments: [
      { text: "  look", kind: "text" },
      { kind: "text", text: " " },
      { emojiId: "heart", kind: "emoji" },
      { modelId: "MegaCrit.Strike", itemType: "card", kind: "item_ref" },
    ],
    formatVersion: 1,
  };
  const equivalent = {
    formatVersion: 1,
    segments: [
      { kind: "text", text: "look " },
      { kind: "emoji", emojiId: "heart" },
      { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" },
    ],
  };

  socket.emit("message", chatSendContent(id, first), false);
  await settle(socket);
  const firstAck = frames(socket).filter((frame) => frame.type === "chat_ack").at(-1)!;
  const broadcasts = frames(socket).filter((frame) => frame.type === "chat_message").length;
  socket.emit("message", chatSendContent(id, equivalent), false);
  await settle(socket);
  assert.deepEqual(frames(socket).at(-1), firstAck);
  assert.equal(frames(socket).filter((frame) => frame.type === "chat_message").length, broadcasts);

  socket.emit("message", chatSendContent(id, {
    ...equivalent,
    segments: [
      ...equivalent.segments.slice(0, 2),
      { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Defend" },
    ],
  }), false);
  await settle(socket);
  assert.equal(frames(socket).at(-1)?.code, "duplicate_message");
  await gateway.close();
});

test("disabled chat, rich content, and connection rate limits reject without broadcasting", async () => {
  const gateway = new ServerChatGateway({
    chatEnabled: false,
    maxPayloadBytes: 8192,
    connectionBurst: 1,
    connectionRefillMs: 60_000,
    configuredFeatures: { ...richFeatures, emojiSetVersion: 0 },
  });
  const socket = new FakeSocket();
  gateway.accept(socket as unknown as WebSocket, ticket);
  await settle(socket);

  socket.emit("message", chatSend("33333333-3333-4333-8333-333333333333", "disabled"), false);
  await settle(socket);
  assert.equal(frames(socket).at(-1)?.code, "chat_disabled");

  gateway.setState({ chatEnabled: true });
  socket.emit("message", JSON.stringify({
    type: "chat_send",
    protocolVersion: 1,
    channel: "server",
    clientMessageId: "44444444-4444-4444-8444-444444444444",
    content: { formatVersion: 1, segments: [{ kind: "emoji", emojiId: "heart" }] },
  }), false);
  await settle(socket);
  assert.equal(frames(socket).at(-1)?.code, "feature_disabled");

  socket.emit("message", chatSend("55555555-5555-4555-8555-555555555555", "first"), false);
  await settle(socket);
  assert.equal(frames(socket).at(-2)?.type, "chat_ack");
  socket.emit("message", chatSend("66666666-6666-4666-8666-666666666666", "second"), false);
  await settle(socket);
  const limited = frames(socket).at(-1)!;
  assert.equal(limited.code, "rate_limited");
  assert.ok(Number(limited.retryAfterMs) > 0);
});

test("requires chat_send channel to be exactly server", async () => {
  for (const channel of [undefined, "room", "SERVER"]) {
    const socket = new FakeSocket();
    const gateway = new ServerChatGateway({ chatEnabled: true });
    gateway.accept(socket as unknown as WebSocket, ticket);
    await settle(socket);
    const payload: Record<string, unknown> = {
      type: "chat_send",
      protocolVersion: 1,
      clientMessageId: "99999999-9999-4999-8999-999999999999",
      content: { formatVersion: 1, segments: [{ kind: "text", text: "channel" }] },
    };
    if (channel !== undefined) {
      payload.channel = channel;
    }

    socket.emit("message", JSON.stringify(payload), false);
    await settle(socket);
    assert.equal(frames(socket).at(-1)?.code, "protocol_mismatch");
    assert.equal(socket.closes.at(-1)?.code, 1002);
    await gateway.close();
  }
});

test("replays a cached chat_disabled error after state changes and conflicts on changed content", async () => {
  const gateway = new ServerChatGateway({ chatEnabled: false });
  const socket = new FakeSocket();
  gateway.accept(socket as unknown as WebSocket, ticket);
  await settle(socket);
  const id = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";

  socket.emit("message", chatSend(id, "disabled result"), false);
  await settle(socket);
  const original = frames(socket).at(-1)!;
  assert.equal(original.code, "chat_disabled");

  gateway.setState({ chatEnabled: true });
  await settle(socket);
  socket.emit("message", chatSend(id, "disabled result"), false);
  await settle(socket);
  assert.deepEqual(frames(socket).at(-1), original);

  socket.emit("message", chatSend(id, "changed result"), false);
  await settle(socket);
  assert.equal(frames(socket).at(-1)?.code, "duplicate_message");

  const newcomer = new FakeSocket();
  gateway.accept(newcomer as unknown as WebSocket, { ...ticket, id: "disabled-newcomer" });
  await settle(newcomer);
  assert.equal(frames(newcomer).find((frame) => frame.type === "chat_snapshot_begin")?.totalMessages, 0);
});

test("replays invalid and feature-disabled errors using a stable content fingerprint", async () => {
  const cases = [
    {
      id: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      code: "invalid_content",
      content: { segments: [{ text: "   ", kind: "text" }], formatVersion: 1 },
      reordered: { formatVersion: 1, segments: [{ kind: "text", text: "   " }] },
      changed: { formatVersion: 1, segments: [{ kind: "text", text: "different" }] },
    },
    {
      id: "cccccccc-cccc-4ccc-8ccc-cccccccccccc",
      code: "feature_disabled",
      content: { segments: [{ emojiId: "heart", kind: "emoji" }], formatVersion: 1 },
      reordered: { formatVersion: 1, segments: [{ kind: "emoji", emojiId: "heart" }] },
      changed: { formatVersion: 1, segments: [{ kind: "emoji", emojiId: "check" }] },
    },
  ] as const;

  for (const entry of cases) {
    const gateway = new ServerChatGateway({
      chatEnabled: true,
      ...(entry.code === "feature_disabled"
        ? { configuredFeatures: { ...richFeatures, emojiSetVersion: 0 as const } }
        : {}),
    });
    const socket = new FakeSocket();
    gateway.accept(socket as unknown as WebSocket, ticket);
    await settle(socket);
    socket.emit("message", chatSendContent(entry.id, entry.content), false);
    await settle(socket);
    const original = frames(socket).at(-1)!;
    assert.equal(original.code, entry.code);

    socket.emit("message", chatSendContent(entry.id, entry.reordered), false);
    await settle(socket);
    assert.deepEqual(frames(socket).at(-1), original);

    socket.emit("message", chatSendContent(entry.id, entry.changed), false);
    await settle(socket);
    assert.equal(frames(socket).at(-1)?.code, "duplicate_message");
    await gateway.close();
  }
});

test("fingerprints deeply nested invalid content within bounded stack and CPU", async () => {
  const gateway = new ServerChatGateway({ chatEnabled: true, maxPayloadBytes: 65_536 });
  const socket = new FakeSocket();
  gateway.accept(socket as unknown as WebSocket, ticket);
  await settle(socket);
  const nested = `${"[".repeat(5_000)}0${"]".repeat(5_000)}`;
  const payload = `{"type":"chat_send","protocolVersion":1,"channel":"server",`
    + `"clientMessageId":"abababab-abab-4bab-8bab-abababababab","content":${nested}}`;
  assert.ok(Buffer.byteLength(payload, "utf8") < 65_536);

  socket.emit("message", payload, false);
  await settle(socket);
  const original = frames(socket).at(-1)!;
  assert.equal(original.code, "invalid_content");
  socket.emit("message", payload, false);
  await settle(socket);
  assert.deepEqual(frames(socket).at(-1), original);
});

test("replays cached rate limits after refill without appending another history message", async () => {
  let now = 0;
  const gateway = new ServerChatGateway({
    chatEnabled: true,
    now: () => now,
    connectionBurst: 1,
    connectionRefillMs: 1_000,
  });
  const socket = new FakeSocket();
  gateway.accept(socket as unknown as WebSocket, ticket);
  await settle(socket);
  socket.emit("message", chatSend("dddddddd-dddd-4ddd-8ddd-dddddddddddd", "accepted"), false);
  await settle(socket);

  const limitedId = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee";
  socket.emit("message", chatSend(limitedId, "limited"), false);
  await settle(socket);
  const original = frames(socket).at(-1)!;
  assert.equal(original.code, "rate_limited");

  now = 2_000;
  socket.emit("message", chatSend(limitedId, "limited"), false);
  await settle(socket);
  assert.deepEqual(frames(socket).at(-1), original);
  socket.emit("message", chatSend(limitedId, "changed"), false);
  await settle(socket);
  assert.equal(frames(socket).at(-1)?.code, "duplicate_message");

  const newcomer = new FakeSocket();
  gateway.accept(newcomer as unknown as WebSocket, { ...ticket, id: "rate-newcomer" });
  await settle(newcomer);
  assert.equal(frames(newcomer).find((frame) => frame.type === "chat_snapshot_begin")?.totalMessages, 1);
});

test("bounds every outbound chat_error and never reflects attacker-controlled field names", async () => {
  const gateway = new ServerChatGateway({ chatEnabled: true, maxPayloadBytes: 1024 });
  const socket = new FakeSocket();
  gateway.accept(socket as unknown as WebSocket, ticket);
  await settle(socket);
  const attackerKey = `attack_${"x".repeat(790)}`;
  const payload = chatSendContent("ffffffff-ffff-4fff-8fff-ffffffffffff", {
    formatVersion: 1,
    segments: [{ kind: "text", text: "safe" }],
    [attackerKey]: true,
  });
  assert.ok(Buffer.byteLength(payload, "utf8") <= 1024);

  socket.emit("message", payload, false);
  await settle(socket);
  const errorPayload = socket.sent.at(-1)!;
  const error = JSON.parse(errorPayload) as Record<string, unknown>;
  assert.equal(error.code, "invalid_content");
  assert.ok(Buffer.byteLength(errorPayload, "utf8") <= 1024);
  assert.equal(errorPayload.includes("attack_"), false);
});

test("closes unsupported websocket payloads with the required close codes", async () => {
  const binary = new FakeSocket();
  const invalidUtf8 = new FakeSocket();
  const oversized = new FakeSocket();
  const gateway = new ServerChatGateway({ chatEnabled: true, maxPayloadBytes: 1024 });
  gateway.accept(binary as unknown as WebSocket, ticket);
  gateway.accept(invalidUtf8 as unknown as WebSocket, { ...ticket, id: "reservation-2" });
  gateway.accept(oversized as unknown as WebSocket, { ...ticket, id: "reservation-3" });
  await settle(binary, invalidUtf8, oversized);

  binary.emit("message", Buffer.from("binary"), true);
  invalidUtf8.emit("message", Buffer.from([0xc3, 0x28]), false);
  oversized.emit("message", Buffer.alloc(1025, 0x20), false);
  await settle(binary, invalidUtf8, oversized);

  assert.equal(binary.closes.at(-1)?.code, 1003);
  assert.equal(invalidUtf8.closes.at(-1)?.code, 1007);
  assert.equal(oversized.closes.at(-1)?.code, 1009);
  for (const socket of [binary, invalidUtf8, oversized]) {
    assert.ok(Buffer.byteLength(socket.closes.at(-1)?.reason ?? "") <= 123);
  }
});

test("sends a protocol error before closing malformed and mismatched messages", async () => {
  for (const payload of ["{", JSON.stringify({ type: "chat_send", protocolVersion: 2 })]) {
    const gateway = new ServerChatGateway({ chatEnabled: true });
    const socket = new FakeSocket();
    gateway.accept(socket as unknown as WebSocket, ticket);
    await settle(socket);
    const start = socket.sent.length;

    socket.emit("message", payload, false);
    await settle(socket);

    assert.equal(frames(socket).slice(start)[0]?.code, "protocol_mismatch");
    assert.equal(socket.closes.at(-1)?.code, 1002);
  }
});

test("a stalled protocol-error peer does not block later global events for other peers", async () => {
  const gateway = new ServerChatGateway({ chatEnabled: true, protocolErrorCloseGraceMs: 10 });
  const stalled = new FakeSocket();
  const healthy = new FakeSocket();
  gateway.accept(stalled as unknown as WebSocket, ticket);
  gateway.accept(healthy as unknown as WebSocket, { ...ticket, id: "healthy", playerName: "Bob" });
  await settle(stalled, healthy);
  const healthyStart = healthy.sent.length;

  stalled.emit("message", "{", false);
  await new Promise<void>((resolve) => setImmediate(resolve));
  assert.equal(frames(stalled).at(-1)?.code, "protocol_mismatch");

  healthy.emit("message", chatSend("12121212-1212-4212-8212-121212121212", "still live"), false);
  gateway.setState({ chatEnabled: false });
  gateway.clearHistory(new Date("2026-07-13T07:00:00.000Z"));
  await settle(healthy);
  assert.deepEqual(frames(healthy).slice(healthyStart).map((frame) => frame.type), [
    "chat_ack",
    "chat_message",
    "chat_state",
    "chat_history_cleared",
  ]);

  await new Promise<void>((resolve) => setTimeout(resolve, 100));
  assert.equal(stalled.closes.at(-1)?.code, 1002);
  assert.equal(stalled.terminateCalls, 1);
  await gateway.close();
});

test("ignores a valid send queued behind the same peer's protocol error", async () => {
  const gateway = new ServerChatGateway({ chatEnabled: true, protocolErrorCloseGraceMs: 1_000 });
  const attacker = new FakeSocket();
  const observer = new FakeSocket();
  gateway.accept(attacker as unknown as WebSocket, ticket);
  gateway.accept(observer as unknown as WebSocket, { ...ticket, id: "observer", playerName: "Bob" });
  await settle(attacker, observer);
  const observerStart = observer.sent.length;

  attacker.emit("message", "{", false);
  attacker.emit("message", chatSend("13131313-1313-4313-8313-131313131313", "must drop"), false);
  await settle(observer);

  assert.equal(frames(attacker).filter((frame) => frame.type === "chat_ack").length, 0);
  assert.equal(frames(observer).slice(observerStart).some((frame) => frame.type === "chat_message"), false);
  const newcomer = new FakeSocket();
  gateway.accept(newcomer as unknown as WebSocket, { ...ticket, id: "post-error-newcomer" });
  await settle(newcomer);
  assert.equal(frames(newcomer).find((frame) => frame.type === "chat_snapshot_begin")?.totalMessages, 0);
  await gateway.close();
});

test("repeated malformed messages schedule only one protocol error delivery and close", async () => {
  const gateway = new ServerChatGateway({ chatEnabled: true, protocolErrorCloseGraceMs: 1_000 });
  const socket = new FakeSocket();
  gateway.accept(socket as unknown as WebSocket, ticket);
  await settle(socket);

  socket.emit("message", "{", false);
  socket.emit("message", "{", false);
  await new Promise<void>((resolve) => setImmediate(resolve));
  socket.flushAll();
  await new Promise<void>((resolve) => setImmediate(resolve));
  socket.flushAll();
  await new Promise<void>((resolve) => setImmediate(resolve));

  assert.equal(frames(socket).filter((frame) => frame.code === "protocol_mismatch").length, 1);
  assert.equal(socket.closes.filter((entry) => entry.code === 1002).length, 1);
  await gateway.close();
});

test("accept rolls back peer capacity and closes the socket when setup throws", async () => {
  const registry = new ThrowingSnapshotRegistry({ maxPerIp: 1, maxTotal: 1 });
  const gateway = new ServerChatGateway({ peerRegistry: registry, chatEnabled: true });
  const failed = new FakeSocket();
  assert.throws(
    () => gateway.accept(failed as unknown as WebSocket, ticket),
    /injected snapshot failure/,
  );
  assert.equal(failed.terminateCalls, 1);
  assert.equal(registry.size, 0);

  registry.failSnapshot = false;
  const replacement = new FakeSocket();
  assert.doesNotThrow(() => gateway.accept(
    replacement as unknown as WebSocket,
    { ...ticket, id: "replacement" },
  ));
  await settle(replacement);
  assert.equal(frames(replacement)[0]?.type, "chat_ready");
  await gateway.close();
});

test("state changes and history clears remain ordered after an accepting peer snapshot", async () => {
  const socket = new FakeSocket();
  const gateway = new ServerChatGateway({ chatEnabled: true });
  gateway.accept(socket as unknown as WebSocket, ticket);
  gateway.setState({ chatEnabled: false });
  gateway.clearHistory(new Date("2026-07-13T04:05:06.000Z"));
  await settle(socket);

  const all = frames(socket);
  assert.deepEqual(all.map((frame) => frame.type), [
    "chat_ready",
    "chat_snapshot_begin",
    "chat_snapshot_end",
    "chat_state",
    "chat_history_cleared",
  ]);
  assert.equal(all[3]?.chatEnabled, false);
  assert.deepEqual(all[3]?.enabledFeatures, {
    richContentVersion: 0,
    emojiSetVersion: 0,
    itemRefVersion: 0,
    combatRefVersion: 0,
  });
  assert.equal(all[4]?.historyEpoch, 1);
  assert.equal(all[4]?.changedAt, "2026-07-13T04:05:06.000Z");
});

test("chat_state includes current history epoch and fake-clock event time", async () => {
  let now = Date.parse("2026-07-13T06:00:00.000Z");
  const socket = new FakeSocket();
  const gateway = new ServerChatGateway({ chatEnabled: true, now: () => now });
  gateway.accept(socket as unknown as WebSocket, ticket);
  await settle(socket);
  gateway.clearHistory(new Date("2026-07-13T06:01:00.000Z"));
  await settle(socket);

  now = Date.parse("2026-07-13T06:02:03.456Z");
  gateway.setState({ chatEnabled: false });
  await settle(socket);
  const state = frames(socket).at(-1)!;
  assert.equal(state.type, "chat_state");
  assert.equal(state.historyEpoch, 1);
  assert.equal(state.changedAt, "2026-07-13T06:02:03.456Z");
  assert.deepEqual(state.enabledFeatures, {
    richContentVersion: 0,
    emojiSetVersion: 0,
    itemRefVersion: 0,
    combatRefVersion: 0,
  });
});

test("clearHistory defaults changedAt from the injected clock", async () => {
  const now = Date.parse("2026-07-13T08:09:10.123Z");
  const socket = new FakeSocket();
  const gateway = new ServerChatGateway({ chatEnabled: true, now: () => now });
  gateway.accept(socket as unknown as WebSocket, ticket);
  await settle(socket);

  gateway.clearHistory();
  await settle(socket);
  const cleared = frames(socket).at(-1)!;
  assert.equal(cleared.type, "chat_history_cleared");
  assert.equal(cleared.changedAt, "2026-07-13T08:09:10.123Z");
});

test("heartbeatTickMs requires a finite positive integer", () => {
  for (const heartbeatTickMs of [0, -1, Number.NaN]) {
    assert.throws(
      () => new ServerChatGateway({ heartbeatTickMs }),
      /heartbeatTickMs must be a finite positive integer/,
    );
  }
  assert.doesNotThrow(() => new ServerChatGateway());
  assert.doesNotThrow(() => new ServerChatGateway({ heartbeatTickMs: 5 }));
});

test("heartbeat lives exactly while at least one successfully added peer exists", async () => {
  let intervalStarts = 0;
  let intervalClears = 0;
  const activeTimers = new Set<NodeJS.Timeout>();
  const setIntervalHook = ((_callback: () => void, _ms: number) => {
    intervalStarts += 1;
    const timer = { unref: () => timer } as unknown as NodeJS.Timeout;
    activeTimers.add(timer);
    return timer;
  }) as unknown as typeof setInterval;
  const clearIntervalHook = ((timer: NodeJS.Timeout) => {
    intervalClears += 1;
    activeTimers.delete(timer);
  }) as unknown as typeof clearInterval;

  const gateway = new ServerChatGateway({
    chatEnabled: true,
    setInterval: setIntervalHook,
    clearInterval: clearIntervalHook,
  });
  assert.equal(intervalStarts, 0);
  const first = new FakeSocket();
  const second = new FakeSocket();
  gateway.accept(first as unknown as WebSocket, ticket);
  assert.equal(intervalStarts, 1);
  gateway.accept(second as unknown as WebSocket, { ...ticket, id: "heartbeat-second" });
  assert.equal(intervalStarts, 1);
  await settle(first, second);

  first.emit("close", 1000, Buffer.alloc(0));
  assert.equal(intervalClears, 0);
  second.emit("close", 1000, Buffer.alloc(0));
  assert.equal(intervalClears, 1);
  assert.equal(activeTimers.size, 0);
  await gateway.close();
  await gateway.close();
  assert.equal(intervalClears, 1);

  const addFailureGateway = new ServerChatGateway({
    peerRegistry: new ChatPeerRegistry({ maxTotal: 0 }),
    setInterval: setIntervalHook,
    clearInterval: clearIntervalHook,
  });
  assert.throws(() => addFailureGateway.accept(new FakeSocket() as unknown as WebSocket, ticket));
  assert.equal(intervalStarts, 1, "failed peer add must not start heartbeat");

  const snapshotRegistry = new ThrowingSnapshotRegistry();
  const snapshotFailureGateway = new ServerChatGateway({
    peerRegistry: snapshotRegistry,
    setInterval: setIntervalHook,
    clearInterval: clearIntervalHook,
  });
  assert.throws(() => snapshotFailureGateway.accept(
    new FakeSocket() as unknown as WebSocket,
    { ...ticket, id: "heartbeat-failure" },
  ));
  assert.equal(intervalStarts, 2);
  assert.equal(intervalClears, 2);
  assert.equal(activeTimers.size, 0);
});

test("serializes sends before later state and history mutations", async () => {
  const socket = new FakeSocket();
  const gateway = new ServerChatGateway({ chatEnabled: true });
  gateway.accept(socket as unknown as WebSocket, ticket);
  await settle(socket);
  const start = socket.sent.length;

  socket.emit("message", chatSend("77777777-7777-4777-8777-777777777777", "ordered"), false);
  gateway.setState({ chatEnabled: false });
  gateway.clearHistory(new Date("2026-07-13T05:00:00.000Z"));
  await settle(socket);

  assert.deepEqual(frames(socket).slice(start).map((frame) => frame.type), [
    "chat_ack",
    "chat_message",
    "chat_state",
    "chat_history_cleared",
  ]);

  const newcomer = new FakeSocket();
  gateway.accept(newcomer as unknown as WebSocket, { ...ticket, id: "reservation-new" });
  await settle(newcomer);
  assert.equal(frames(newcomer).find((frame) => frame.type === "chat_snapshot_begin")?.totalMessages, 0);
});

test("audit logs contain metadata and hashes but never chat text or private identity", async () => {
  const logs: string[] = [];
  const originalLog = console.log;
  console.log = (...values: unknown[]) => logs.push(values.map(String).join(" "));
  try {
    const socket = new FakeSocket();
    const gateway = new ServerChatGateway({ chatEnabled: true });
    gateway.accept(socket as unknown as WebSocket, {
      ...ticket,
      id: "private-ticket-marker",
      playerNetId: "private-model-id-marker",
    });
    await settle(socket);
    socket.emit(
      "message",
      chatSend("88888888-8888-4888-8888-888888888888", "private-chat-body-marker"),
      false,
    );
    await settle(socket);
    await gateway.close();
  } finally {
    console.log = originalLog;
  }

  const joined = logs.join("\n");
  assert.match(joined, /event=message_accepted/);
  assert.match(joined, /contentHash=[0-9a-f]{64}/);
  assert.match(joined, /bytes=\d+/);
  assert.match(joined, /durationMs=\d+/);
  assert.equal(joined.includes("private-chat-body-marker"), false);
  assert.equal(joined.includes("private-ticket-marker"), false);
  assert.equal(joined.includes("private-model-id-marker"), false);
});

test("close releases a protocol-error barrier even when socket send stalls", async () => {
  const socket = new FakeSocket();
  const gateway = new ServerChatGateway({ chatEnabled: true });
  gateway.accept(socket as unknown as WebSocket, ticket);
  await settle(socket);
  socket.emit("message", "{", false);
  await new Promise<void>((resolve) => setImmediate(resolve));

  await Promise.race([
    gateway.close(),
    new Promise<never>((_, reject) => {
      setTimeout(() => reject(new Error("gateway close timeout")), 100);
    }),
  ]);
});

test("lobby service commits a chat ticket into a ready gateway connection", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-chat-gateway-"));
  const config = loadLobbyServiceConfig({
    HOST: "127.0.0.1",
    PORT: "0",
    PEER_NETWORK_ENABLED: "false",
    PEER_SELF_ADDRESS: "",
    PEER_CF_DISCOVERY_BASE_URL: "",
    PEER_STATE_DIR: join(tempDir, "peer"),
    SERVER_ADMIN_STATE_FILE: join(tempDir, "admin.json"),
    ENFORCE_LOBBY_ACCESS_TOKEN: "false",
    ENFORCE_CREATE_ROOM_TOKEN: "false",
    SERVER_CHAT_ENABLED: "true",
  });
  const service = await createLobbyService(config);
  const address = await service.start();
  let socket: RealWebSocket | undefined;
  try {
    const response = await fetch(`http://127.0.0.1:${address.port}/chat/tickets`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        protocolVersion: 1,
        playerNetId: "steam:integration",
        playerName: "Watcher",
      }),
    });
    assert.equal(response.status, 200);
    const issued = await response.json() as { ticket: string; webSocketUrl: string };

    socket = new RealWebSocket(issued.webSocketUrl, {
      headers: { authorization: `Bearer ${issued.ticket}` },
    });
    const received = await new Promise<Array<Record<string, unknown>>>((resolve, reject) => {
      const frames: Array<Record<string, unknown>> = [];
      const timer = setTimeout(() => reject(new Error("chat gateway frames timeout")), 1000);
      socket!.on("message", (data) => {
        frames.push(JSON.parse(String(data)) as Record<string, unknown>);
        if (frames.length === 3) {
          clearTimeout(timer);
          resolve(frames);
        }
      });
      socket!.once("error", reject);
    });
    assert.deepEqual(received.map((frame) => frame.type), [
      "chat_ready",
      "chat_snapshot_begin",
      "chat_snapshot_end",
    ]);
    assert.equal(received[0]?.chatEnabled, true);
    assert.equal(received[0]?.senderId != null, true);
  } finally {
    socket?.terminate();
    await service.close();
    rmSync(tempDir, { recursive: true, force: true });
  }
});
