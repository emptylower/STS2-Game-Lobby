import assert from "node:assert/strict";
import test from "node:test";
import {
  ChatDedupeCache,
  ChatDedupeError,
  type ChatAckEnvelope,
  type ChatDedupeCacheOptions,
  type ChatErrorEnvelope,
} from "./dedupe-cache.js";
import type { CanonicalChatMessage } from "./protocol.js";

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

function sampleMessage(text: string): CanonicalChatMessage {
  return {
    messageId: "11111111-1111-1111-1111-111111111111",
    senderId: "aaaaaaaaaaaaaaaaaaaaaa",
    senderName: "Ironclad",
    content: {
      formatVersion: 1,
      segments: [{ kind: "text", text }],
    },
    plainTextFallback: text,
    sentAt: "2026-07-12T12:00:00.000Z",
  };
}

function ack(clientMessageId: string, text: string): ChatAckEnvelope {
  return {
    type: "chat_ack",
    protocolVersion: 1,
    clientMessageId,
    message: sampleMessage(text),
  };
}

function err(clientMessageId: string, code: ChatErrorEnvelope["code"] = "rate_limited"): ChatErrorEnvelope {
  const envelope: ChatErrorEnvelope = {
    type: "chat_error",
    protocolVersion: 1,
    clientMessageId,
    code,
    message: "denied",
  };
  if (code === "rate_limited") {
    envelope.retryAfterMs = 1_000;
  }
  return envelope;
}

function makeCache(overrides: Partial<ChatDedupeCacheOptions> = {}) {
  const clock = makeClock();
  const cache = new ChatDedupeCache({
    now: clock.now,
    maxEntriesPerSession: 256,
    sessionTtlMs: 10 * 60_000,
    maxSessions: 1_000,
    ...overrides,
  });
  return { cache, ...clock };
}

test("same sessionId + clientMessageId + canonicalJson replays stored ACK", () => {
  const { cache } = makeCache();
  const sessionId = "session-a";
  const clientMessageId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
  const canonicalJson = '{"formatVersion":1,"segments":[{"kind":"text","text":"hello"}]}';
  const result = ack(clientMessageId, "hello");

  assert.deepEqual(cache.lookup(sessionId, clientMessageId, canonicalJson), { kind: "miss" });
  cache.store(sessionId, clientMessageId, canonicalJson, result);

  const second = cache.lookup(sessionId, clientMessageId, canonicalJson);
  assert.equal(second.kind, "replay");
  if (second.kind !== "replay") {
    assert.fail("expected replay");
  }
  assert.deepEqual(second.result, result);
});

test("same sessionId + clientMessageId + canonicalJson replays stored error", () => {
  const { cache } = makeCache();
  const sessionId = "session-b";
  const clientMessageId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
  const canonicalJson = '{"formatVersion":1,"segments":[{"kind":"text","text":"nope"}]}';
  const result = err(clientMessageId, "invalid_content");

  cache.store(sessionId, clientMessageId, canonicalJson, result);
  const replayed = cache.lookup(sessionId, clientMessageId, canonicalJson);
  assert.equal(replayed.kind, "replay");
  if (replayed.kind !== "replay") {
    assert.fail("expected replay");
  }
  assert.deepEqual(replayed.result, result);
});

test("same clientMessageId with different canonical content yields duplicate_message", () => {
  const { cache } = makeCache();
  const sessionId = "session-c";
  const clientMessageId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
  const firstJson = '{"formatVersion":1,"segments":[{"kind":"text","text":"one"}]}';
  const secondJson = '{"formatVersion":1,"segments":[{"kind":"text","text":"two"}]}';

  cache.store(sessionId, clientMessageId, firstJson, ack(clientMessageId, "one"));

  const conflict = cache.lookup(sessionId, clientMessageId, secondJson);
  assert.deepEqual(conflict, { kind: "conflict", code: "duplicate_message" });
});

test("oldest result evicts after 256 entries per session", () => {
  const { cache } = makeCache({ maxEntriesPerSession: 256 });
  const sessionId = "session-d";
  const firstId = "00000000-0000-0000-0000-000000000000";
  const firstJson = '{"formatVersion":1,"segments":[{"kind":"text","text":"first"}]}';

  cache.store(sessionId, firstId, firstJson, ack(firstId, "first"));
  assert.equal(cache.lookup(sessionId, firstId, firstJson).kind, "replay");

  for (let i = 1; i <= 256; i += 1) {
    const id = `00000000-0000-0000-0000-${String(i).padStart(12, "0")}`;
    const json = `{"formatVersion":1,"segments":[{"kind":"text","text":"m${i}"}]}`;
    cache.store(sessionId, id, json, ack(id, `m${i}`));
  }

  // First entry was oldest and should be gone after 256 newer inserts.
  assert.deepEqual(cache.lookup(sessionId, firstId, firstJson), { kind: "miss" });

  // Newest remains.
  const newestId = "00000000-0000-0000-0000-000000000256";
  const newestJson = '{"formatVersion":1,"segments":[{"kind":"text","text":"m256"}]}';
  assert.equal(cache.lookup(sessionId, newestId, newestJson).kind, "replay");
});

test("session expires after 10 minutes with injected clock", () => {
  const { cache, advance } = makeCache({ sessionTtlMs: 10 * 60_000 });
  const sessionId = "session-e";
  const clientMessageId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
  const canonicalJson = '{"formatVersion":1,"segments":[{"kind":"text","text":"ttl"}]}';

  cache.store(sessionId, clientMessageId, canonicalJson, ack(clientMessageId, "ttl"));
  assert.equal(cache.lookup(sessionId, clientMessageId, canonicalJson).kind, "replay");

  // Just under TTL: still present.
  advance(10 * 60_000 - 1);
  assert.equal(cache.lookup(sessionId, clientMessageId, canonicalJson).kind, "replay");

  // Past TTL after last activity: session gone.
  advance(10 * 60_000);
  assert.deepEqual(cache.lookup(sessionId, clientMessageId, canonicalJson), { kind: "miss" });
});

test("cleanup removes expired sessions", () => {
  const { cache, advance } = makeCache({ sessionTtlMs: 10 * 60_000, maxSessions: 1 });
  const sessionId = "session-f";
  const clientMessageId = "ffffffff-ffff-ffff-ffff-ffffffffffff";
  const canonicalJson = '{"formatVersion":1,"segments":[{"kind":"text","text":"clean"}]}';

  cache.store(sessionId, clientMessageId, canonicalJson, ack(clientMessageId, "clean"));
  advance(10 * 60_000);
  cache.cleanup();

  // Capacity free after cleanup of expired session.
  cache.store("session-g", clientMessageId, canonicalJson, ack(clientMessageId, "clean"));
  assert.equal(cache.lookup("session-g", clientMessageId, canonicalJson).kind, "replay");
});

test("bounded session capacity yields server_busy without growing", () => {
  const { cache } = makeCache({ maxSessions: 2 });
  const id = "11111111-1111-1111-1111-111111111111";
  const json = '{"formatVersion":1,"segments":[{"kind":"text","text":"x"}]}';

  cache.store("s1", id, json, ack(id, "x"));
  cache.store("s2", id, json, ack(id, "x"));

  assert.throws(
    () => cache.store("s3", id, json, ack(id, "x")),
    (error: unknown) => {
      assert.ok(error instanceof ChatDedupeError);
      assert.equal(error.code, "server_busy");
      return true;
    },
  );

  // Existing sessions remain usable.
  assert.equal(cache.lookup("s1", id, json).kind, "replay");
  assert.equal(cache.lookup("s2", id, json).kind, "replay");
  // Another attempt still busy.
  assert.throws(() => cache.store("s3", id, json, ack(id, "x")), hasCode("server_busy"));
});

test("defaults are 256 entries per session and 10 minute TTL", () => {
  const clock = makeClock();
  const cache = new ChatDedupeCache({ now: clock.now });
  const sessionId = "defaults";
  const firstId = "00000000-0000-0000-0000-000000000000";
  const firstJson = '{"formatVersion":1,"segments":[{"kind":"text","text":"d0"}]}';

  cache.store(sessionId, firstId, firstJson, ack(firstId, "d0"));
  for (let i = 1; i <= 256; i += 1) {
    const id = `00000000-0000-0000-0000-${String(i).padStart(12, "0")}`;
    const json = `{"formatVersion":1,"segments":[{"kind":"text","text":"d${i}"}]}`;
    cache.store(sessionId, id, json, ack(id, `d${i}`));
  }
  assert.deepEqual(cache.lookup(sessionId, firstId, firstJson), { kind: "miss" });

  const keepId = "00000000-0000-0000-0000-000000000001";
  const keepJson = '{"formatVersion":1,"segments":[{"kind":"text","text":"d1"}]}';
  assert.equal(cache.lookup(sessionId, keepId, keepJson).kind, "replay");

  clock.advance(10 * 60_000);
  assert.deepEqual(cache.lookup(sessionId, keepId, keepJson), { kind: "miss" });
});
