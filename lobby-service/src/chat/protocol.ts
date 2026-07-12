export type TextSegment = { kind: "text"; text: string };
export type ChatContent = { formatVersion: 1; segments: TextSegment[] };

export interface CanonicalChatMessage {
  messageId: string;
  senderId: string;
  senderName: string;
  content: ChatContent;
  plainTextFallback: string;
  sentAt: string;
}

export type ChatProtocolErrorCode =
  | "invalid_content"
  | "feature_disabled"
  | "invalid_message"
  | "chat_disabled"
  | "rate_limited"
  | "duplicate_message"
  | "protocol_mismatch"
  | "server_busy";

export class ChatProtocolError extends Error {
  constructor(
    readonly code: ChatProtocolErrorCode,
    message: string,
  ) {
    super(message);
    this.name = "ChatProtocolError";
  }
}

const MAX_SEGMENTS = 32;
const MAX_TEXT_SCALARS = 300;
const CONTENT_ALLOWED_KEYS = new Set(["formatVersion", "segments"]);
const TEXT_SEGMENT_ALLOWED_KEYS = new Set(["kind", "text"]);
const RICH_KINDS = new Set([
  "emoji",
  "item_ref",
  "power_state",
  "target_ref",
]);

/** Lowercase UUID strings are 36 ASCII bytes. */
export const WIRE_UUID_LENGTH = 36;
/** 16 random bytes encoded base64url are 22 ASCII bytes. */
export const WIRE_SENDER_ID_LENGTH = 22;
/** ISO-8601 millisecond timestamps like 2026-07-12T12:00:00.000Z are 24 ASCII bytes. */
export const WIRE_SENT_AT_LENGTH = 24;

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function assertAllowedKeys(
  value: Record<string, unknown>,
  allowed: Set<string>,
  label: string,
): void {
  for (const key of Object.keys(value)) {
    if (!allowed.has(key)) {
      throw new ChatProtocolError("invalid_content", `${label} has reserved or unknown field: ${key}`);
    }
  }
}

function countUnicodeScalars(text: string): number {
  return Array.from(text).length;
}

function isDisallowedControlChar(ch: string): boolean {
  const code = ch.codePointAt(0);
  if (code === undefined) {
    return true;
  }

  // Allow LF only among C0 controls.
  if (code === 0x0a) {
    return false;
  }

  // C0 controls + DEL
  if (code <= 0x1f || code === 0x7f) {
    return true;
  }

  // C1 controls
  if (code >= 0x80 && code <= 0x9f) {
    return true;
  }

  // Bidi overrides / isolates / embeddings
  // U+202A..U+202E, U+2066..U+2069
  if ((code >= 0x202a && code <= 0x202e) || (code >= 0x2066 && code <= 0x2069)) {
    return true;
  }

  // Invisible format characters commonly abused for spoofing / zero-width injection.
  // Cf category subset used by the design: ZWSP/ZWNJ/ZWJ/LRM/RLM/BOM/soft hyphen/etc.
  switch (code) {
    case 0x00ad: // soft hyphen
    case 0x061c: // Arabic letter mark
    case 0x180e: // Mongolian vowel separator (legacy Cf)
    case 0x200b: // ZWSP
    case 0x200c: // ZWNJ
    case 0x200d: // ZWJ
    case 0x200e: // LRM
    case 0x200f: // RLM
    case 0x2060: // word joiner
    case 0x2061:
    case 0x2062:
    case 0x2063:
    case 0x2064:
    case 0xfeff: // BOM / ZWNBSP
    case 0xfff9:
    case 0xfffa:
    case 0xfffb:
      return true;
    default:
      return false;
  }
}

function normalizeText(raw: string): string {
  // Design: validate UTF-8 (JS strings are already UTF-16 of scalar values),
  // then NFC, then CRLF/CR -> LF.
  const nfc = raw.normalize("NFC");
  return nfc.replace(/\r\n|\r/g, "\n");
}

function assertNoDisallowedChars(text: string): void {
  for (const ch of text) {
    if (isDisallowedControlChar(ch)) {
      const code = ch.codePointAt(0) ?? 0;
      throw new ChatProtocolError(
        "invalid_content",
        `text contains disallowed control or format character U+${code.toString(16).padStart(4, "0")}`,
      );
    }
  }
}

export function canonicalizeServerContent(input: unknown): ChatContent {
  if (!isPlainObject(input)) {
    throw new ChatProtocolError("invalid_content", "content must be an object");
  }

  assertAllowedKeys(input, CONTENT_ALLOWED_KEYS, "content");

  if (input.formatVersion !== 1) {
    throw new ChatProtocolError("invalid_content", "formatVersion must be 1");
  }

  if (!Array.isArray(input.segments)) {
    throw new ChatProtocolError("invalid_content", "segments must be an array");
  }

  if (input.segments.length > MAX_SEGMENTS) {
    throw new ChatProtocolError("invalid_content", `segments must be at most ${MAX_SEGMENTS}`);
  }

  const normalizedTexts: string[] = [];

  for (const segment of input.segments) {
    if (!isPlainObject(segment)) {
      throw new ChatProtocolError("invalid_content", "segment must be an object");
    }

    const kind = segment.kind;
    if (typeof kind !== "string") {
      throw new ChatProtocolError("invalid_content", "segment.kind must be a string");
    }

    if (kind === "text") {
      assertAllowedKeys(segment, TEXT_SEGMENT_ALLOWED_KEYS, "text segment");
      if (typeof segment.text !== "string") {
        throw new ChatProtocolError("invalid_content", "text segment text must be a string");
      }

      const normalized = normalizeText(segment.text);
      assertNoDisallowedChars(normalized);
      if (normalized.length > 0) {
        normalizedTexts.push(normalized);
      }
      continue;
    }

    // Phase 1: any non-text kind is a future rich feature, not partially accepted.
    // Unknown kinds also map to feature_disabled so clients do not probe for partial support.
    if (RICH_KINDS.has(kind) || kind !== "text") {
      throw new ChatProtocolError("feature_disabled", `segment kind "${kind}" is not enabled in phase 1`);
    }
  }

  // Merge adjacent text segments (all are text in phase 1).
  let merged = normalizedTexts.join("");

  // Trim leading/trailing whitespace (spaces, tabs, newlines). Internal spaces/newlines kept.
  merged = merged.replace(/^[\s\u00a0]+|[\s\u00a0]+$/g, "");

  if (merged.length === 0) {
    throw new ChatProtocolError("invalid_content", "content must not be blank-only");
  }

  if (countUnicodeScalars(merged) > MAX_TEXT_SCALARS) {
    throw new ChatProtocolError(
      "invalid_content",
      `text must be at most ${MAX_TEXT_SCALARS} Unicode scalars`,
    );
  }

  return {
    formatVersion: 1,
    segments: [{ kind: "text", text: merged }],
  };
}

export function renderPlainTextFallback(content: ChatContent): string {
  return content.segments.map((segment) => segment.text).join("");
}

export function deterministicContentJson(content: ChatContent): string {
  // Fixed field order for dedupe comparisons.
  const body = {
    formatVersion: content.formatVersion,
    segments: content.segments.map((segment) => ({
      kind: segment.kind,
      text: segment.text,
    })),
  };
  return JSON.stringify(body);
}

export function utf8JsonBytes(value: unknown): number {
  return Buffer.byteLength(JSON.stringify(value), "utf8");
}

/**
 * Project the actual wire envelopes that must carry a canonical message and
 * reject when any of them would exceed maxBytes. Uses standardized ID widths:
 * - process/session/message/snapshot IDs: lowercase UUID (36 ASCII)
 * - senderId: 16 random bytes base64url (22 ASCII)
 * - clientMessageId: lowercase UUID (36 ASCII)
 * - sentAt: 24-byte ISO millisecond timestamp
 */
export function projectChatWireEnvelopes(message: CanonicalChatMessage): {
  ack: unknown;
  chatMessage: unknown;
  snapshotChunk: unknown;
} {
  // Preserve the provided message fields; callers/tests use the standardized widths.
  const ack = {
    type: "chat_ack",
    protocolVersion: 1,
    clientMessageId: "00000000-0000-0000-0000-000000000000",
    message,
  };
  const chatMessage = {
    type: "chat_message",
    protocolVersion: 1,
    message,
  };
  const snapshotChunk = {
    type: "chat_snapshot_chunk",
    protocolVersion: 1,
    snapshotId: "00000000-0000-0000-0000-000000000000",
    chunkIndex: 0,
    messages: [message],
  };
  return { ack, chatMessage, snapshotChunk };
}

export function measureChatWireBytes(message: CanonicalChatMessage): number {
  const { ack, chatMessage, snapshotChunk } = projectChatWireEnvelopes(message);
  return Math.max(utf8JsonBytes(ack), utf8JsonBytes(chatMessage), utf8JsonBytes(snapshotChunk));
}

export function assertWireBudget(value: unknown, maxBytes: number): void {
  // Accept either a CanonicalChatMessage (project all three envelopes) or a raw envelope value.
  if (isCanonicalChatMessage(value)) {
    const bytes = measureChatWireBytes(value);
    if (bytes > maxBytes) {
      throw new ChatProtocolError(
        "invalid_content",
        `wire envelope exceeds budget: ${bytes} > ${maxBytes}`,
      );
    }
    return;
  }

  const bytes = utf8JsonBytes(value);
  if (bytes > maxBytes) {
    throw new ChatProtocolError(
      "invalid_content",
      `wire envelope exceeds budget: ${bytes} > ${maxBytes}`,
    );
  }
}

function isCanonicalChatMessage(value: unknown): value is CanonicalChatMessage {
  if (!isPlainObject(value)) {
    return false;
  }
  return (
    typeof value.messageId === "string" &&
    typeof value.senderId === "string" &&
    typeof value.senderName === "string" &&
    typeof value.plainTextFallback === "string" &&
    typeof value.sentAt === "string" &&
    isPlainObject(value.content) &&
    value.content.formatVersion === 1 &&
    Array.isArray(value.content.segments)
  );
}
