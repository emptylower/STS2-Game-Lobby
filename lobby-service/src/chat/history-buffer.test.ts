import assert from "node:assert/strict";
import test from "node:test";
import {
  ChatHistoryBuffer,
  type ChatHistoryBufferOptions,
  type ChatSnapshotEnvelope,
} from "./history-buffer.js";
import {
  type CanonicalChatMessage,
  utf8JsonBytes,
} from "./protocol.js";

const SENDER_ID = "ABCDEFGHIJKLMNOPQRSTUV"; // 22 ASCII base64url
const SENT_AT = "2026-07-12T12:00:00.000Z"; // 24 ASCII

function message(index: number, overrides: Partial<CanonicalChatMessage> = {}): CanonicalChatMessage {
  const text = `msg-${index}`;
  return {
    messageId: `00000000-0000-4000-8000-${String(index).padStart(12, "0")}`,
    senderId: SENDER_ID,
    senderName: "Ironclad",
    content: { formatVersion: 1, segments: [{ kind: "text", text }] },
    plainTextFallback: text,
    sentAt: SENT_AT,
    ...overrides,
  };
}

function ids(from: number, to: number): string[] {
  const out: string[] = [];
  for (let index = from; index <= to; index += 1) {
    out.push(message(index).messageId);
  }
  return out;
}

function makeHistory(overrides: Partial<ChatHistoryBufferOptions> = {}) {
  let nowMs = 1_000_000;
  const options: ChatHistoryBufferOptions = {
    now: () => nowMs,
    instanceId: "11111111-1111-4111-8111-111111111111",
    historyLimit: 100,
    historyTtlMs: 86_400_000,
    snapshotLimit: 50,
    ...overrides,
  };
  const history = new ChatHistoryBuffer(options);
  return {
    history,
    get now() {
      return nowMs;
    },
    set now(value: number) {
      nowMs = value;
    },
    advance(ms: number) {
      nowMs += ms;
    },
  };
}

function isBegin(
  frame: ChatSnapshotEnvelope,
): frame is Extract<ChatSnapshotEnvelope, { type: "chat_snapshot_begin" }> {
  return frame.type === "chat_snapshot_begin";
}

function isChunk(
  frame: ChatSnapshotEnvelope,
): frame is Extract<ChatSnapshotEnvelope, { type: "chat_snapshot_chunk" }> {
  return frame.type === "chat_snapshot_chunk";
}

function isEnd(
  frame: ChatSnapshotEnvelope,
): frame is Extract<ChatSnapshotEnvelope, { type: "chat_snapshot_end" }> {
  return frame.type === "chat_snapshot_end";
}

test("keeps 100 for 24 hours and snapshots newest 50", () => {
  const ctx = makeHistory();
  const { history } = ctx;
  for (let index = 0; index < 101; index += 1) {
    history.append(message(index));
  }
  assert.deepEqual(
    history.snapshot().map((x) => x.messageId),
    ids(51, 100),
  );
  ctx.now += 86_400_001;
  assert.deepEqual(history.snapshot(), []);
});

test("final snapshot envelopes fit 8192 bytes", () => {
  const { history } = makeHistory();
  for (let index = 0; index < 50; index += 1) {
    history.append(message(index));
  }
  const frames = history.buildSnapshot("snapshot-1", 8192);
  assert.ok(frames.every((frame) => utf8JsonBytes(frame) <= 8192));
});

test("empty history still emits begin and end", () => {
  const { history } = makeHistory();
  const frames = history.buildSnapshot("snap-empty", 8192);
  assert.equal(frames.length, 2);
  assert.ok(isBegin(frames[0]!));
  assert.ok(isEnd(frames[1]!));
  assert.equal(frames[0]!.type, "chat_snapshot_begin");
  assert.equal(frames[0]!.snapshotId, "snap-empty");
  assert.equal(frames[0]!.protocolVersion, 1);
  assert.equal(frames[0]!.instanceId, "11111111-1111-4111-8111-111111111111");
  assert.equal(frames[0]!.historyEpoch, 0);
  assert.equal(frames[0]!.totalMessages, 0);
  assert.equal(frames[1]!.type, "chat_snapshot_end");
  assert.equal(frames[1]!.snapshotId, "snap-empty");
  assert.equal(frames[1]!.historyEpoch, 0);
  assert.equal(frames[1]!.protocolVersion, 1);
});

test("chunk indexes are continuous from zero", () => {
  const { history } = makeHistory({ snapshotLimit: 10, historyLimit: 10 });
  for (let index = 0; index < 10; index += 1) {
    // Large-ish payloads force multiple chunks under a tight budget.
    history.append(
      message(index, {
        plainTextFallback: "x".repeat(200),
        content: { formatVersion: 1, segments: [{ kind: "text", text: "x".repeat(200) }] },
      }),
    );
  }
  const frames = history.buildSnapshot("snap-chunks", 900);
  assert.ok(isBegin(frames[0]!));
  assert.ok(isEnd(frames[frames.length - 1]!));
  const chunks = frames.filter(isChunk);
  assert.ok(chunks.length >= 2);
  for (let i = 0; i < chunks.length; i += 1) {
    assert.equal(chunks[i]!.chunkIndex, i);
    assert.equal(chunks[i]!.snapshotId, "snap-chunks");
    assert.equal(chunks[i]!.protocolVersion, 1);
    assert.ok(chunks[i]!.messages.length >= 1);
  }
  const totalInChunks = chunks.reduce((sum, chunk) => sum + chunk.messages.length, 0);
  assert.equal(totalInChunks, frames[0]!.totalMessages);
  assert.equal(frames[0]!.totalMessages, 10);
});

test("single-message overflow still emits a solo chunk", () => {
  const { history } = makeHistory({ snapshotLimit: 5, historyLimit: 5 });
  const huge = message(0, {
    plainTextFallback: "H".repeat(4000),
    content: { formatVersion: 1, segments: [{ kind: "text", text: "H".repeat(4000) }] },
  });
  history.append(huge);
  const frames = history.buildSnapshot("snap-overflow", 512);
  const chunks = frames.filter(isChunk);
  assert.equal(chunks.length, 1);
  assert.equal(chunks[0]!.chunkIndex, 0);
  assert.equal(chunks[0]!.messages.length, 1);
  assert.equal(chunks[0]!.messages[0]!.messageId, huge.messageId);
  assert.ok(utf8JsonBytes(chunks[0]!) > 512);
  assert.ok(isBegin(frames[0]!));
  assert.equal(frames[0]!.totalMessages, 1);
  assert.ok(isEnd(frames[frames.length - 1]!));
});

test("append and snapshot cleanup expired messages", () => {
  const ctx = makeHistory({ historyLimit: 10, snapshotLimit: 10, historyTtlMs: 1000 });
  const { history } = ctx;
  history.append(message(1));
  history.append(message(2));
  assert.equal(history.snapshot().length, 2);

  ctx.advance(1001);
  // Read path cleanup
  assert.deepEqual(history.snapshot(), []);

  history.append(message(3));
  assert.deepEqual(
    history.snapshot().map((m) => m.messageId),
    [message(3).messageId],
  );

  ctx.advance(1001);
  // Write path cleanup: append after TTL drops old entries.
  history.append(message(4));
  assert.deepEqual(
    history.snapshot().map((m) => m.messageId),
    [message(4).messageId],
  );
});

test("explicit clear increments epoch and empties history", () => {
  const { history } = makeHistory();
  assert.equal(history.historyEpoch, 0);
  history.append(message(1));
  history.append(message(2));
  assert.equal(history.snapshot().length, 2);

  const nextEpoch = history.clear();
  assert.equal(nextEpoch, 1);
  assert.equal(history.historyEpoch, 1);
  assert.deepEqual(history.snapshot(), []);

  const frames = history.buildSnapshot("after-clear", 8192);
  assert.ok(isBegin(frames[0]!));
  assert.equal(frames[0]!.historyEpoch, 1);
  assert.equal(frames[0]!.totalMessages, 0);
  assert.ok(isEnd(frames[1]!));
  assert.equal(frames[1]!.historyEpoch, 1);

  history.append(message(3));
  assert.equal(history.clear(), 2);
  assert.equal(history.historyEpoch, 2);
});

test("TTL cleanup does not increment epoch", () => {
  const ctx = makeHistory({ historyTtlMs: 5000, historyLimit: 10, snapshotLimit: 10 });
  const { history } = ctx;
  history.append(message(1));
  assert.equal(history.historyEpoch, 0);
  ctx.advance(5001);
  history.cleanup();
  assert.equal(history.historyEpoch, 0);
  assert.deepEqual(history.snapshot(), []);
  // Another path: snapshot-driven cleanup also leaves epoch alone.
  history.append(message(2));
  ctx.advance(5001);
  history.snapshot();
  assert.equal(history.historyEpoch, 0);
});

test("instanceId is process-local and stable on the buffer", () => {
  const a = new ChatHistoryBuffer({
    now: () => 0,
    instanceId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
  });
  const b = new ChatHistoryBuffer({
    now: () => 0,
    instanceId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
  });
  assert.equal(a.instanceId, "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
  assert.equal(b.instanceId, "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
  assert.notEqual(a.instanceId, b.instanceId);

  // Default construction still yields a non-empty process-local id.
  const c = new ChatHistoryBuffer({ now: () => 0 });
  const d = new ChatHistoryBuffer({ now: () => 0 });
  assert.match(c.instanceId, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
  assert.match(d.instanceId, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
  // Distinct instances get distinct ids when not injected.
  assert.notEqual(c.instanceId, d.instanceId);

  a.append(message(0));
  const frames = a.buildSnapshot("id-check", 8192);
  assert.ok(isBegin(frames[0]!));
  assert.equal(frames[0]!.instanceId, a.instanceId);
});

test("snapshot respects snapshotLimit independently of retained history", () => {
  const { history } = makeHistory({ historyLimit: 20, snapshotLimit: 5 });
  for (let index = 0; index < 15; index += 1) {
    history.append(message(index));
  }
  assert.deepEqual(
    history.snapshot().map((m) => m.messageId),
    ids(10, 14),
  );
  // Full retained window is larger than snapshotLimit; only newest 5 are snapshot.
  // Append more until historyLimit trims.
  for (let index = 15; index < 25; index += 1) {
    history.append(message(index));
  }
  // Retained: newest 20 => 5..24; snapshot newest 5 => 20..24
  assert.deepEqual(
    history.snapshot().map((m) => m.messageId),
    ids(20, 24),
  );
  assert.equal(history.retainedCount, 20);
});

test("retainedCount applies TTL cleanup and reports historyLimit rather than snapshotLimit", () => {
  const ctx = makeHistory({ historyLimit: 4, snapshotLimit: 1, historyTtlMs: 1000 });
  for (let index = 0; index < 6; index += 1) {
    ctx.history.append(message(index));
  }
  assert.equal(ctx.history.retainedCount, 4);
  assert.equal(ctx.history.snapshot().length, 1);

  ctx.advance(1001);
  assert.equal(ctx.history.retainedCount, 0);
});

test("buildSnapshot messages are newest-first-within-limit chronological order", () => {
  const { history } = makeHistory({ historyLimit: 100, snapshotLimit: 3 });
  for (let index = 0; index < 5; index += 1) {
    history.append(message(index));
  }
  const snap = history.snapshot();
  assert.deepEqual(
    snap.map((m) => m.messageId),
    ids(2, 4),
  );
  const frames = history.buildSnapshot("order", 8192);
  const chunks = frames.filter(isChunk);
  const flattened = chunks.flatMap((chunk) => chunk.messages.map((m) => m.messageId));
  assert.deepEqual(flattened, ids(2, 4));
});
