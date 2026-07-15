export const EMOJI_SET_1 = [
  "smile", "laugh", "heart", "thumbs-up", "thumbs-down", "sparkles",
  "flame", "zap", "shield", "swords", "target", "crown",
  "skull", "ghost", "eye", "message-circle", "check", "x",
] as const;

export type EmojiId = typeof EMOJI_SET_1[number];
export type TextSegment = { kind: "text"; text: string };
export type EmojiSegment = { kind: "emoji"; emojiId: EmojiId };
export type ItemRefSegment =
  | { kind: "item_ref"; itemType: "card"; modelId: string; upgradeLevel?: number }
  | { kind: "item_ref"; itemType: "relic" | "potion"; modelId: string };
export type PowerStateSegment = {
  kind: "power_state";
  modelId: string;
  amount: number;
  roomSessionId: string;
  ownerPlayerNetId?: string;
  applierPlayerNetId?: string;
};
export type TargetRefSegment = {
  kind: "target_ref";
  targetKind: "player" | "monster";
  targetKey: string;
  roomSessionId: string;
};
export type ChatSegment =
  | TextSegment
  | EmojiSegment
  | ItemRefSegment
  | PowerStateSegment
  | TargetRefSegment;
export type ChatContent = { formatVersion: 1; segments: ChatSegment[] };

export interface EnabledRichFeatures {
  richContentVersion: 0 | 1;
  emojiSetVersion: 0 | 1;
  itemRefVersion: 0 | 1;
  combatRefVersion: 0;
}

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
const MAX_ENTITIES = 12;
const MAX_TEXT_SCALARS = 300;
const CONTENT_ALLOWED_KEYS = new Set(["formatVersion", "segments"]);
const TEXT_SEGMENT_ALLOWED_KEYS = new Set(["kind", "text"]);
const EMOJI_SEGMENT_ALLOWED_KEYS = new Set(["kind", "emojiId"]);
const CARD_REF_SEGMENT_ALLOWED_KEYS = new Set(["kind", "itemType", "modelId", "upgradeLevel"]);
const STATIC_ITEM_REF_SEGMENT_ALLOWED_KEYS = new Set(["kind", "itemType", "modelId"]);
const EMOJI_SET_1_IDS = new Set<string>(EMOJI_SET_1);
const MODEL_ID_PATTERN = /^[A-Za-z0-9._-]{1,160}$/;
// config.ts permits snapshotLimit up to 1000. If every message needs its own
// chunk, valid indices span 0..999; reserve the widest supported index.
const MAX_CONFIGURED_SNAPSHOT_LIMIT = 1000;
const WORST_SNAPSHOT_CHUNK_INDEX = MAX_CONFIGURED_SNAPSHOT_LIMIT - 1;
const PHASE_ONE_FEATURES: EnabledRichFeatures = {
  richContentVersion: 0,
  emojiSetVersion: 0,
  itemRefVersion: 0,
  combatRefVersion: 0,
};

/** Lowercase UUID strings are 36 ASCII bytes. */
export const WIRE_UUID_LENGTH = 36;
/** 16 random bytes encoded base64url are 22 ASCII bytes. */
export const WIRE_SENDER_ID_LENGTH = 22;
/** ISO-8601 millisecond timestamps like 2026-07-12T12:00:00.000Z are 24 ASCII bytes. */
export const WIRE_SENT_AT_LENGTH = 24;

function isPlainObject(value: unknown): value is Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return false;
  }
  try {
    const prototype = Object.getPrototypeOf(value);
    return prototype === Object.prototype || prototype === null;
  } catch {
    return false;
  }
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

function assertOwnRequiredKeys(
  value: Record<string, unknown>,
  required: readonly string[],
  label: string,
): void {
  for (const key of required) {
    if (!Object.hasOwn(value, key)) {
      throw new ChatProtocolError("invalid_content", `${label} is missing required field: ${key}`);
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

  // Deprecated format controls U+206A..U+206F (symmetric swapping / digit shapes).
  if (code >= 0x206a && code <= 0x206f) {
    return true;
  }

  // Variation selectors (VS1–VS16, VS17–VS256) used for spoofing / invisible styling.
  if ((code >= 0xfe00 && code <= 0xfe0f) || (code >= 0xe0100 && code <= 0xe01ef)) {
    return true;
  }

  // Language tags / tag characters U+E0000..U+E007F (format-ish, invisible).
  if (code >= 0xe0000 && code <= 0xe007f) {
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
  assertWellFormedUnicode(raw);
  const nfc = raw.normalize("NFC");
  return nfc.replace(/\r\n|\r/g, "\n");
}

function assertWellFormedUnicode(text: string): void {
  for (let index = 0; index < text.length; index += 1) {
    const code = text.charCodeAt(index);
    if (code >= 0xd800 && code <= 0xdbff) {
      const next = text.charCodeAt(index + 1);
      if (!(next >= 0xdc00 && next <= 0xdfff)) {
        throw new ChatProtocolError("invalid_content", "text contains an unpaired surrogate");
      }
      index += 1;
    } else if (code >= 0xdc00 && code <= 0xdfff) {
      throw new ChatProtocolError("invalid_content", "text contains an unpaired surrogate");
    }
  }
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

export function canonicalizeChatContent(
  input: unknown,
  features: EnabledRichFeatures,
): ChatContent {
  return canonicalizeContent(input, features, false);
}

function canonicalizeContent(
  input: unknown,
  features: EnabledRichFeatures,
  legacyRichKindPrecedence: boolean,
): ChatContent {
  if (!isPlainObject(input)) {
    throw new ChatProtocolError("invalid_content", "content must be an object");
  }

  assertOwnRequiredKeys(input, ["formatVersion", "segments"], "content");
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

  const canonicalSegments: ChatSegment[] = [];
  let entityCount = 0;
  let requiresEmoji = false;
  let requiresItemRef = false;

  for (const segment of input.segments) {
    if (!isPlainObject(segment)) {
      throw new ChatProtocolError("invalid_content", "segment must be an object");
    }

    assertOwnRequiredKeys(segment, ["kind"], "segment");
    const kind = segment.kind;
    if (typeof kind !== "string") {
      throw new ChatProtocolError("invalid_content", "segment.kind must be a string");
    }

    if (kind === "text") {
      assertOwnRequiredKeys(segment, ["text"], "text segment");
      assertAllowedKeys(segment, TEXT_SEGMENT_ALLOWED_KEYS, "text segment");
      if (typeof segment.text !== "string") {
        throw new ChatProtocolError("invalid_content", "text segment text must be a string");
      }

      const normalized = normalizeText(segment.text);
      assertNoDisallowedChars(normalized);
      if (normalized.length > 0) {
        const previous = canonicalSegments.at(-1);
        if (previous?.kind === "text") {
          previous.text = `${previous.text}${normalized}`.normalize("NFC");
        } else {
          canonicalSegments.push({ kind: "text", text: normalized });
        }
      }
      continue;
    }

    if (kind === "emoji") {
      if (legacyRichKindPrecedence && features.richContentVersion !== 1) {
        throw new ChatProtocolError("feature_disabled", "emoji segments are not enabled");
      }
      assertOwnRequiredKeys(segment, ["emojiId"], "emoji segment");
      assertAllowedKeys(segment, EMOJI_SEGMENT_ALLOWED_KEYS, "emoji segment");
      if (typeof segment.emojiId !== "string" || !EMOJI_SET_1_IDS.has(segment.emojiId)) {
        throw new ChatProtocolError("invalid_content", "emojiId must be from Emoji Set 1");
      }
      requiresEmoji = true;
      entityCount += 1;
      canonicalSegments.push({ kind: "emoji", emojiId: segment.emojiId as EmojiId });
      continue;
    }

    if (kind === "item_ref") {
      if (legacyRichKindPrecedence && features.richContentVersion !== 1) {
        throw new ChatProtocolError("feature_disabled", "item_ref segments are not enabled");
      }
      assertOwnRequiredKeys(segment, ["itemType", "modelId"], "item_ref segment");
      const itemType = segment.itemType;
      if (itemType === "card") {
        assertAllowedKeys(segment, CARD_REF_SEGMENT_ALLOWED_KEYS, "card item_ref segment");
      } else if (itemType === "relic" || itemType === "potion") {
        assertAllowedKeys(segment, STATIC_ITEM_REF_SEGMENT_ALLOWED_KEYS, `${itemType} item_ref segment`);
      } else {
        assertAllowedKeys(segment, CARD_REF_SEGMENT_ALLOWED_KEYS, "item_ref segment");
        throw new ChatProtocolError("invalid_content", "itemType must be card, relic, or potion");
      }

      if (typeof segment.modelId !== "string" || !MODEL_ID_PATTERN.test(segment.modelId)) {
        throw new ChatProtocolError(
          "invalid_content",
          "modelId must be 1 to 160 ASCII letters, digits, dots, underscores, or hyphens",
        );
      }

      if (itemType === "card" && Object.hasOwn(segment, "upgradeLevel")) {
        if (
          typeof segment.upgradeLevel !== "number" ||
          !Number.isInteger(segment.upgradeLevel) ||
          segment.upgradeLevel < 0 ||
          segment.upgradeLevel > 9
        ) {
          throw new ChatProtocolError("invalid_content", "card upgradeLevel must be an integer from 0 to 9");
        }
      }

      requiresItemRef = true;
      entityCount += 1;
      if (itemType === "card") {
        if (Object.hasOwn(segment, "upgradeLevel")) {
          canonicalSegments.push({
            kind: "item_ref",
            itemType,
            modelId: segment.modelId,
            upgradeLevel: segment.upgradeLevel as number,
          });
        } else {
          canonicalSegments.push({ kind: "item_ref", itemType, modelId: segment.modelId });
        }
      } else {
        canonicalSegments.push({ kind: "item_ref", itemType, modelId: segment.modelId });
      }
      continue;
    }

    if (kind === "power_state" || kind === "target_ref") {
      throw new ChatProtocolError(
        legacyRichKindPrecedence ? "feature_disabled" : "invalid_content",
        `segment kind "${kind}" is not valid for server chat`,
      );
    }
    throw new ChatProtocolError("invalid_content", `segment kind "${kind}" is not valid`);
  }

  if (entityCount > MAX_ENTITIES) {
    throw new ChatProtocolError("invalid_content", `content must contain at most ${MAX_ENTITIES} entities`);
  }

  const first = canonicalSegments[0];
  if (first?.kind === "text") {
    first.text = first.text.replace(/^[\s\u00a0]+/u, "");
    if (first.text.length === 0) {
      canonicalSegments.shift();
    }
  }
  const last = canonicalSegments.at(-1);
  if (last?.kind === "text") {
    last.text = last.text.replace(/[\s\u00a0]+$/u, "");
    if (last.text.length === 0) {
      canonicalSegments.pop();
    }
  }

  if (canonicalSegments.length === 0) {
    throw new ChatProtocolError("invalid_content", "content must not be blank-only");
  }

  const textScalars = canonicalSegments.reduce(
    (total, segment) => total + (segment.kind === "text" ? countUnicodeScalars(segment.text) : 0),
    0,
  );
  if (textScalars > MAX_TEXT_SCALARS) {
    throw new ChatProtocolError(
      "invalid_content",
      `text must be at most ${MAX_TEXT_SCALARS} Unicode scalars`,
    );
  }

  if (requiresEmoji && (features.richContentVersion !== 1 || features.emojiSetVersion !== 1)) {
    throw new ChatProtocolError("feature_disabled", "emoji segments are not enabled");
  }
  if (requiresItemRef && (features.richContentVersion !== 1 || features.itemRefVersion !== 1)) {
    throw new ChatProtocolError("feature_disabled", "item_ref segments are not enabled");
  }

  return {
    formatVersion: 1,
    segments: canonicalSegments,
  };
}

export function canonicalizeServerContent(input: unknown): ChatContent {
  return canonicalizeContent(input, PHASE_ONE_FEATURES, true);
}

export function renderPlainTextFallback(content: ChatContent): string {
  return content.segments.map((segment) => {
    if (segment.kind === "text") return segment.text;
    if (segment.kind === "emoji") return "[Emoji]";
    if (segment.kind === "item_ref") {
      return segment.itemType === "card" ? "[Card]"
        : segment.itemType === "relic" ? "[Relic]" : "[Potion]";
    }
    if (segment.kind === "power_state") return "[Power]";
    return segment.targetKind === "player" ? "[Player]" : "[Monster]";
  }).join("");
}

export function deterministicContentJson(content: ChatContent): string {
  // Fixed field order for dedupe comparisons.
  const body = {
    formatVersion: content.formatVersion,
    segments: content.segments.map((segment) => {
      if (segment.kind === "text") {
        return { kind: "text", text: segment.text };
      }
      if (segment.kind === "emoji") {
        return { kind: "emoji", emojiId: segment.emojiId };
      }
      if (segment.kind === "power_state") {
        return {
          kind: "power_state",
          modelId: segment.modelId,
          amount: segment.amount,
          roomSessionId: segment.roomSessionId,
          ...(segment.ownerPlayerNetId === undefined
            ? {}
            : { ownerPlayerNetId: segment.ownerPlayerNetId }),
          ...(segment.applierPlayerNetId === undefined
            ? {}
            : { applierPlayerNetId: segment.applierPlayerNetId }),
        };
      }
      if (segment.kind === "target_ref") {
        return {
          kind: "target_ref",
          targetKind: segment.targetKind,
          targetKey: segment.targetKey,
          roomSessionId: segment.roomSessionId,
        };
      }
      if (segment.itemType === "card" && segment.upgradeLevel !== undefined) {
        return {
          kind: "item_ref",
          itemType: "card",
          modelId: segment.modelId,
          upgradeLevel: segment.upgradeLevel,
        };
      }
      return {
        kind: "item_ref",
        itemType: segment.itemType,
        modelId: segment.modelId,
      };
    }),
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
    chunkIndex: WORST_SNAPSHOT_CHUNK_INDEX,
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
