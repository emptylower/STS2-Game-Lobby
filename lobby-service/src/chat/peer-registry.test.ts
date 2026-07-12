import assert from "node:assert/strict";
import test from "node:test";
import {
  ChatPeerRegistry,
  clampCloseReason,
  type ChatPeer,
  type ChatPeerRegistryOptions,
  type ChatSocket,
} from "./peer-registry.js";

const OPEN = 1;

function hasCode(code: string) {
  return (error: unknown) =>
    error instanceof Error &&
    "code" in error &&
    (error as { code: unknown }).code === code;
}

function makeClock(startMs = 1_000_000) {
  let nowMs = startMs;
  return {
    now: () => nowMs,
    setNow: (ms: number) => {
      nowMs = ms;
    },
    advance: (ms: number) => {
      nowMs += ms;
    },
  };
}

type FakeSocket = ChatSocket & {
  readonly sent: string[];
  readonly pings: number;
  readonly closes: Array<{ code: number; reason: string }>;
  readonly terminates: number;
  setBufferedAmount(value: number): void;
  setReadyState(value: number): void;
  completeNext(error?: Error): void;
  completeAll(error?: Error): void;
  pendingCount(): number;
};

function makeSocket(initialBuffered = 0): FakeSocket {
  let readyState = OPEN;
  let bufferedAmount = initialBuffered;
  const sent: string[] = [];
  const closes: Array<{ code: number; reason: string }> = [];
  let pings = 0;
  let terminates = 0;
  const pending: Array<(error?: Error) => void> = [];

  const socket: FakeSocket = {
    get readyState() {
      return readyState;
    },
    get bufferedAmount() {
      return bufferedAmount;
    },
    get sent() {
      return sent;
    },
    get pings() {
      return pings;
    },
    get closes() {
      return closes;
    },
    get terminates() {
      return terminates;
    },
    send(data: string, callback: (error?: Error) => void): void {
      sent.push(data);
      pending.push(callback);
    },
    ping(): void {
      pings += 1;
    },
    close(code: number, reason: string): void {
      closes.push({ code, reason });
      readyState = 2;
    },
    terminate(): void {
      terminates += 1;
      readyState = 3;
    },
    setBufferedAmount(value: number): void {
      bufferedAmount = value;
    },
    setReadyState(value: number): void {
      readyState = value;
    },
    completeNext(error?: Error): void {
      const callback = pending.shift();
      assert.ok(callback, "expected a pending send callback");
      callback(error);
    },
    completeAll(error?: Error): void {
      while (pending.length > 0) {
        const callback = pending.shift();
        callback?.(error);
      }
    },
    pendingCount(): number {
      return pending.length;
    },
  };

  return socket;
}

function peer(sessionId: string, clientIp: string, socket: ChatSocket = makeSocket()): ChatPeer {
  return { sessionId, clientIp, socket };
}

function registry(overrides: Partial<ChatPeerRegistryOptions> = {}) {
  const clock = makeClock();
  const timers = new Map<number, { fn: () => void; dueAt: number }>();
  let nextTimerId = 1;

  const setTimeoutFn = (fn: () => void, ms: number): number => {
    const id = nextTimerId;
    nextTimerId += 1;
    timers.set(id, { fn, dueAt: clock.now() + ms });
    return id;
  };

  const clearTimeoutFn = (id: number): void => {
    timers.delete(id);
  };

  const flushTimers = (): void => {
    let progressed = true;
    while (progressed) {
      progressed = false;
      for (const [id, timer] of [...timers.entries()]) {
        if (timer.dueAt <= clock.now()) {
          timers.delete(id);
          timer.fn();
          progressed = true;
        }
      }
    }
  };

  const peers = new ChatPeerRegistry({
    now: clock.now,
    setTimeout: setTimeoutFn as unknown as typeof setTimeout,
    clearTimeout: clearTimeoutFn as unknown as typeof clearTimeout,
    maxTotal: 500,
    maxPerIp: 10,
    slowClientBytes: 262_144,
    pingIntervalMs: 30_000,
    pongTimeoutMs: 45_000,
    slowClientTerminateMs: 2_000,
    ...overrides,
  });

  return { peers, clock, flushTimers, advance: (ms: number) => {
    clock.advance(ms);
    flushTimers();
  } };
}

function parseSent(socket: FakeSocket): object[] {
  return socket.sent.map((raw) => JSON.parse(raw) as object);
}

test("enforces total and per-IP caps", () => {
  const { peers } = registry({ maxTotal: 2, maxPerIp: 1 });
  peers.add(peer("a", "203.0.113.1"));
  assert.throws(() => peers.add(peer("b", "203.0.113.1")), hasCode("too_many_connections"));
  peers.add(peer("c", "203.0.113.2"));
  assert.throws(() => peers.add(peer("d", "203.0.113.3")), hasCode("server_busy"));
});

test("assertCapacity mirrors add limits without registering", () => {
  const { peers } = registry({ maxTotal: 1, maxPerIp: 1 });
  peers.assertCapacity("203.0.113.9");
  peers.add(peer("a", "203.0.113.9"));
  assert.throws(() => peers.assertCapacity("203.0.113.9"), hasCode("too_many_connections"));
  assert.throws(() => peers.assertCapacity("198.51.100.1"), hasCode("server_busy"));
});

test("remove releases total and per-IP counters", () => {
  const { peers } = registry({ maxTotal: 1, maxPerIp: 1 });
  peers.add(peer("a", "203.0.113.1"));
  assert.throws(() => peers.add(peer("b", "203.0.113.1")), hasCode("too_many_connections"));
  peers.remove("a");
  peers.add(peer("b", "203.0.113.1"));
  assert.equal(peers.size, 1);
});

test("serializes one send in flight per peer", () => {
  const { peers } = registry();
  const socket = makeSocket();
  peers.add(peer("a", "203.0.113.1", socket));

  peers.send("a", { type: "one" });
  peers.send("a", { type: "two" });

  assert.equal(socket.sent.length, 1);
  assert.deepEqual(JSON.parse(socket.sent[0]!), { type: "one" });
  assert.equal(socket.pendingCount(), 1);

  socket.completeNext();
  assert.equal(socket.sent.length, 2);
  assert.deepEqual(JSON.parse(socket.sent[1]!), { type: "two" });

  socket.completeNext();
  assert.equal(socket.pendingCount(), 0);
});

test("incrementals wait until snapshot end", async () => {
  const { peers } = registry();
  const socket = makeSocket();
  peers.add(peer("a", "203.0.113.1", socket));

  const snapshotDone = peers.enqueueSnapshot("a", [
    { type: "chat_snapshot_begin" },
    { type: "chat_snapshot_chunk" },
    { type: "chat_snapshot_end" },
  ]);

  peers.send("a", { type: "chat_message", id: "inc-1" });
  peers.broadcast({ type: "chat_message", id: "inc-2" });

  assert.equal(socket.sent.length, 1);
  assert.deepEqual(JSON.parse(socket.sent[0]!), { type: "chat_snapshot_begin" });

  socket.completeNext();
  assert.equal(socket.sent.length, 2);
  assert.deepEqual(JSON.parse(socket.sent[1]!), { type: "chat_snapshot_chunk" });

  socket.completeNext();
  assert.equal(socket.sent.length, 3);
  assert.deepEqual(JSON.parse(socket.sent[2]!), { type: "chat_snapshot_end" });

  socket.completeNext();
  await snapshotDone;

  assert.equal(socket.sent.length, 5);
  assert.deepEqual(parseSent(socket).slice(3), [
    { type: "chat_message", id: "inc-1" },
    { type: "chat_message", id: "inc-2" },
  ]);

  socket.completeAll();
});

test("broadcasts include sender", () => {
  const { peers } = registry();
  const socketA = makeSocket();
  const socketB = makeSocket();
  peers.add(peer("a", "203.0.113.1", socketA));
  peers.add(peer("b", "203.0.113.2", socketB));

  peers.broadcast({ type: "chat_message", text: "hi" });

  assert.equal(socketA.sent.length, 1);
  assert.equal(socketB.sent.length, 1);
  assert.deepEqual(JSON.parse(socketA.sent[0]!), { type: "chat_message", text: "hi" });
  assert.deepEqual(JSON.parse(socketB.sent[0]!), { type: "chat_message", text: "hi" });
});

test("slow client stops enqueueing, discards ordinary queue, closes, then terminates", () => {
  const { peers, advance } = registry({ slowClientBytes: 262_144, slowClientTerminateMs: 2_000 });
  const socket = makeSocket();
  peers.add(peer("a", "203.0.113.1", socket));

  peers.send("a", { type: "queued-1" });
  peers.send("a", { type: "queued-2" });
  assert.equal(socket.sent.length, 1);

  socket.setBufferedAmount(262_145);
  peers.send("a", { type: "should-not-enqueue" });

  assert.equal(socket.closes.length, 1);
  assert.equal(socket.closes[0]!.code, 1001);
  assert.equal(socket.terminates, 0);
  // Ordinary pending frames discarded; only the first in-flight payload remains as already sent.
  assert.equal(socket.sent.length, 1);
  assert.deepEqual(JSON.parse(socket.sent[0]!), { type: "queued-1" });

  peers.send("a", { type: "still-blocked" });
  assert.equal(socket.sent.length, 1);

  advance(1_999);
  assert.equal(socket.terminates, 0);
  advance(1);
  assert.equal(socket.terminates, 1);
});

test("heartbeat pings every 30s and terminates after 45s without pong", () => {
  const { peers, advance } = registry({
    pingIntervalMs: 30_000,
    pongTimeoutMs: 45_000,
  });
  const socket = makeSocket();
  peers.add(peer("a", "203.0.113.1", socket));

  peers.heartbeat();
  assert.equal(socket.pings, 0);

  advance(29_999);
  peers.heartbeat();
  assert.equal(socket.pings, 0);

  advance(1);
  peers.heartbeat();
  assert.equal(socket.pings, 1);

  advance(14_999);
  peers.heartbeat();
  assert.equal(socket.terminates, 0);

  advance(1);
  peers.heartbeat();
  assert.equal(socket.terminates, 1);
});

test("markPong resets the no-pong termination clock", () => {
  const { peers, advance } = registry({
    pingIntervalMs: 30_000,
    pongTimeoutMs: 45_000,
  });
  const socket = makeSocket();
  peers.add(peer("a", "203.0.113.1", socket));

  advance(30_000);
  peers.heartbeat();
  assert.equal(socket.pings, 1);

  advance(44_000);
  peers.markPong("a");
  peers.heartbeat();
  assert.equal(socket.terminates, 0);

  advance(45_000);
  peers.heartbeat();
  assert.equal(socket.terminates, 1);
});

test("clampCloseReason keeps at most 123 UTF-8 bytes without splitting a scalar", () => {
  const ascii = "a".repeat(200);
  assert.equal(Buffer.byteLength(clampCloseReason(ascii), "utf8"), 123);
  assert.equal(clampCloseReason(ascii).length, 123);

  // U+1F600 is 4 UTF-8 bytes; 30 emoji = 120 bytes, next would exceed 123.
  const emoji = "😀".repeat(40);
  const clamped = clampCloseReason(emoji);
  assert.equal(Buffer.byteLength(clamped, "utf8"), 120);
  assert.equal([...clamped].length, 30);
  assert.equal(clamped.includes("\uFFFD"), false);

  // Mix: 122 ASCII + emoji would split if truncated by bytes alone.
  const mixed = `${"x".repeat(122)}😀yyy`;
  const mixedClamped = clampCloseReason(mixed);
  assert.equal(mixedClamped, "x".repeat(122));
  assert.equal(Buffer.byteLength(mixedClamped, "utf8"), 122);
});

test("slow-client close reason is clamped", () => {
  const { peers } = registry({ slowClientBytes: 1 });
  const socket = makeSocket(2);
  peers.add(peer("a", "203.0.113.1", socket));
  peers.send("a", { type: "x" });
  assert.equal(socket.closes.length, 1);
  assert.ok(Buffer.byteLength(socket.closes[0]!.reason, "utf8") <= 123);
});
