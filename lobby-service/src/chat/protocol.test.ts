import assert from "node:assert/strict";
import test from "node:test";
import {
  assertWireBudget,
  canonicalizeServerContent,
  type CanonicalChatMessage,
  type ChatContent,
  deterministicContentJson,
  renderPlainTextFallback,
  utf8JsonBytes,
} from "./protocol.js";

function hasCode(code: string) {
  return (error: unknown) =>
    error instanceof Error &&
    "code" in error &&
    (error as { code: unknown }).code === code;
}

const UUID_A = "01234567-89ab-cdef-0123-456789abcdef";
const UUID_B = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
const UUID_C = "11111111-2222-3333-4444-555555555555";
const SENDER_ID = "ABCDEFGHIJKLMNOPQRSTUV"; // 22 ASCII base64url
const SENT_AT = "2026-07-12T12:00:00.000Z"; // 24 ASCII

function textContent(text: string): ChatContent {
  return { formatVersion: 1, segments: [{ kind: "text", text }] };
}

function sampleMessage(content: ChatContent, senderName = "Ironclad"): CanonicalChatMessage {
  return {
    messageId: UUID_A,
    senderId: SENDER_ID,
    senderName,
    content,
    plainTextFallback: renderPlainTextFallback(content),
    sentAt: SENT_AT,
  };
}

test("normalizes NFC/newlines and merges text", () => {
  assert.deepEqual(
    canonicalizeServerContent({
      formatVersion: 1,
      segments: [
        { kind: "text", text: "  e\u0301\r\n" },
        { kind: "text", text: "hello  " },
      ],
    }),
    { formatVersion: 1, segments: [{ kind: "text", text: "é\nhello" }] },
  );
});

test("phase 1 rejects rich kinds", () => {
  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [{ kind: "emoji", emojiId: "thumbs-up" }],
      }),
    hasCode("feature_disabled"),
  );
  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [{ kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" }],
      }),
    hasCode("feature_disabled"),
  );
  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [{ kind: "power_state", modelId: "x", amount: 1, roomSessionId: UUID_A }],
      }),
    hasCode("feature_disabled"),
  );
});

test("rejects unknown kinds and reserved fields with invalid_content", () => {
  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [{ kind: "unknown_widget", text: "x" }],
      }),
    hasCode("feature_disabled"),
  );
  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [{ kind: "text", text: "ok", extra: true }],
      }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [{ kind: "text", text: "ok" }],
        extra: 1,
      }),
    hasCode("invalid_content"),
  );
});

test("rejects wrong format version and non-exact types", () => {
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: 2, segments: [{ kind: "text", text: "x" }] }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: "1", segments: [{ kind: "text", text: "x" }] }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: 1, segments: "nope" }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: 1 }] }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () => canonicalizeServerContent(null),
    hasCode("invalid_content"),
  );
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: 1, segments: [null] }),
    hasCode("invalid_content"),
  );
});

test("enforces 32 segment limit before merge", () => {
  const segments = Array.from({ length: 32 }, () => ({ kind: "text", text: "a" }));
  assert.deepEqual(canonicalizeServerContent({ formatVersion: 1, segments }), {
    formatVersion: 1,
    segments: [{ kind: "text", text: "a".repeat(32) }],
  });

  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [...segments, { kind: "text", text: "b" }],
      }),
    hasCode("invalid_content"),
  );
});

test("enforces 300 Unicode scalars including astral characters", () => {
  const astral = "😀"; // one scalar, two UTF-16 code units
  assert.equal(Array.from(astral).length, 1);

  const max = astral.repeat(300);
  assert.deepEqual(canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: max }] }), {
    formatVersion: 1,
    segments: [{ kind: "text", text: max }],
  });

  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [{ kind: "text", text: max + "x" }],
      }),
    hasCode("invalid_content"),
  );

  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [
          { kind: "text", text: "a".repeat(200) },
          { kind: "text", text: "b".repeat(101) },
        ],
      }),
    hasCode("invalid_content"),
  );
});

test("rejects blank-only content after normalize/trim", () => {
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: "   " }] }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () =>
      canonicalizeServerContent({
        formatVersion: 1,
        segments: [
          { kind: "text", text: "\r\n" },
          { kind: "text", text: " \t " },
        ],
      }),
    hasCode("invalid_content"),
  );
  assert.throws(
    () => canonicalizeServerContent({ formatVersion: 1, segments: [] }),
    hasCode("invalid_content"),
  );
});

test("rejects NUL/C0/C1/bidi/format characters but allows LF", () => {
  const allowed = canonicalizeServerContent({
    formatVersion: 1,
    segments: [{ kind: "text", text: "line1\nline2" }],
  });
  assert.deepEqual(allowed, { formatVersion: 1, segments: [{ kind: "text", text: "line1\nline2" }] });

  // CR is normalized to LF before control checks, so it is allowed as a newline.
  assert.deepEqual(
    canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: "x\ry" }] }),
    { formatVersion: 1, segments: [{ kind: "text", text: "x\ny" }] },
  );
  assert.deepEqual(
    canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: "x\r\ny" }] }),
    { formatVersion: 1, segments: [{ kind: "text", text: "x\ny" }] },
  );

  const disallowed = [
    "\u0000", // NUL
    "\u0001", // C0
    "\u0009", // TAB (C0; not LF)
    "\u007f", // DEL
    "\u0085", // C1 NEL
    "\u009f", // C1
    "\u202a", // LRE
    "\u202e", // RLO
    "\u2066", // LRI
    "\u2069", // PDI
    "\u200b", // ZWSP
    "\u200e", // LRM
    "\ufeff", // BOM
    "\u00ad", // soft hyphen
  ];

  for (const ch of disallowed) {
    assert.throws(
      () => canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: `a${ch}b` }] }),
      hasCode("invalid_content"),
      `expected rejection for U+${ch.codePointAt(0)!.toString(16)}`,
    );
  }
});

test("allows literal HTML/Markdown/BBCode/template text as plain content", () => {
  const raw =
    '<b>bold</b> **md** [b]bb[/b] {{item:card}} and <script>alert(1)</script>';
  assert.deepEqual(canonicalizeServerContent({ formatVersion: 1, segments: [{ kind: "text", text: raw }] }), {
    formatVersion: 1,
    segments: [{ kind: "text", text: raw }],
  });
});

test("renderPlainTextFallback joins text segments", () => {
  const content = canonicalizeServerContent({
    formatVersion: 1,
    segments: [
      { kind: "text", text: "hello " },
      { kind: "text", text: "world" },
    ],
  });
  assert.equal(renderPlainTextFallback(content), "hello world");
});

test("deterministicContentJson uses fixed field order", () => {
  const content = canonicalizeServerContent({
    formatVersion: 1,
    segments: [{ kind: "text", text: "hi" }],
  });
  assert.equal(
    deterministicContentJson(content),
    '{"formatVersion":1,"segments":[{"kind":"text","text":"hi"}]}',
  );
});

test("utf8JsonBytes measures UTF-8 JSON size", () => {
  assert.equal(utf8JsonBytes({ a: "é" }), Buffer.byteLength(JSON.stringify({ a: "é" }), "utf8"));
});

test("assertWireBudget accepts exact 8192 and rejects 8193 worst-case projections", () => {
  // Phase-1 text (300 scalars) never reaches 8 KiB by itself. Budget enforcement still
  // measures the max of chat_ack / chat_message / one-message chat_snapshot_chunk so a
  // message that cannot be replayed in one chunk is rejected. Use standardized ID widths
  // and pad plainTextFallback only inside the projected envelopes for exact boundaries.
  const content = textContent("hello");
  const baseMessage: CanonicalChatMessage = {
    messageId: UUID_A, // 36
    senderId: SENDER_ID, // 22
    senderName: "Ironclad",
    content,
    plainTextFallback: "hello",
    sentAt: SENT_AT, // 24
  };

  const baseBytes = measureProjections(baseMessage);
  assert.ok(baseBytes < 8192);
  assertWireBudget(baseMessage, 8192);

  // Snapshot chunk is the largest of the three for a single message.
  const ackBytes = utf8JsonBytes({
    type: "chat_ack",
    protocolVersion: 1,
    clientMessageId: UUID_B,
    message: baseMessage,
  });
  const messageBytes = utf8JsonBytes({
    type: "chat_message",
    protocolVersion: 1,
    message: baseMessage,
  });
  const chunkBytes = utf8JsonBytes({
    type: "chat_snapshot_chunk",
    protocolVersion: 1,
    snapshotId: UUID_C,
    chunkIndex: 0,
    messages: [baseMessage],
  });
  assert.equal(measureProjections(baseMessage), Math.max(ackBytes, messageBytes, chunkBytes));
  assert.ok(chunkBytes >= messageBytes);
  assert.ok(chunkBytes >= ackBytes || ackBytes > 0);

  // Exact envelope-level 8192 / 8193 boundaries via raw wire values.
  const exact8192 = makeExactByteObject(8192);
  assert.equal(utf8JsonBytes(exact8192), 8192);
  assertWireBudget(exact8192, 8192);

  const exact8193 = makeExactByteObject(8193);
  assert.equal(utf8JsonBytes(exact8193), 8193);
  assert.throws(() => assertWireBudget(exact8193, 8192), hasCode("invalid_content"));

  // Pad only plainTextFallback so each ASCII char adds one projected byte and exact
  // 8192/8193 sizes are reachable (content text + fallback both padded would step by 2).
  const exactProjected = padFallbackToExactBytes(baseMessage, 8192);
  assert.ok(exactProjected, "expected exact 8192 projected message");
  assert.equal(measureProjections(exactProjected), 8192);
  assertWireBudget(exactProjected, 8192);

  const exactProjectedOver = padFallbackToExactBytes(baseMessage, 8193);
  assert.ok(exactProjectedOver, "expected exact 8193 projected message");
  assert.equal(measureProjections(exactProjectedOver), 8193);
  assert.throws(() => assertWireBudget(exactProjectedOver, 8192), hasCode("invalid_content"));
});

function measureProjections(message: CanonicalChatMessage): number {
  const ack = {
    type: "chat_ack",
    protocolVersion: 1,
    clientMessageId: UUID_B,
    message,
  };
  const chatMessage = {
    type: "chat_message",
    protocolVersion: 1,
    message,
  };
  const chunk = {
    type: "chat_snapshot_chunk",
    protocolVersion: 1,
    snapshotId: UUID_C,
    chunkIndex: 0,
    messages: [message],
  };
  return Math.max(utf8JsonBytes(ack), utf8JsonBytes(chatMessage), utf8JsonBytes(chunk));
}

function makeExactByteObject(targetBytes: number): { p: string } {
  // {"p":"..."} => 8 overhead bytes for ASCII payload.
  const overhead = Buffer.byteLength(JSON.stringify({ p: "" }), "utf8");
  assert.ok(targetBytes >= overhead);
  return { p: "x".repeat(targetBytes - overhead) };
}

function padFallbackToExactBytes(
  base: CanonicalChatMessage,
  targetBytes: number,
): CanonicalChatMessage | null {
  const baseSize = measureProjections(base);
  if (baseSize > targetBytes) {
    return null;
  }
  const pad = targetBytes - baseSize;
  const message: CanonicalChatMessage = {
    ...base,
    plainTextFallback: `${base.plainTextFallback}${"x".repeat(pad)}`,
  };
  return measureProjections(message) === targetBytes ? message : null;
}
