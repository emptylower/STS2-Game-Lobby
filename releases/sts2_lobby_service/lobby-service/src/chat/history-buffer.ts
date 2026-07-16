import { randomUUID } from "node:crypto";
import {
  type CanonicalChatMessage,
  utf8JsonBytes,
} from "./protocol.js";

export type ChatSnapshotBegin = {
  type: "chat_snapshot_begin";
  protocolVersion: 1;
  snapshotId: string;
  instanceId: string;
  historyEpoch: number;
  totalMessages: number;
};

export type ChatSnapshotChunk = {
  type: "chat_snapshot_chunk";
  protocolVersion: 1;
  snapshotId: string;
  chunkIndex: number;
  messages: CanonicalChatMessage[];
};

export type ChatSnapshotEnd = {
  type: "chat_snapshot_end";
  protocolVersion: 1;
  snapshotId: string;
  historyEpoch: number;
};

export type ChatSnapshotEnvelope = ChatSnapshotBegin | ChatSnapshotChunk | ChatSnapshotEnd;

export interface ChatHistoryBufferOptions {
  now?: () => number;
  instanceId?: string;
  historyLimit?: number;
  historyTtlMs?: number;
  snapshotLimit?: number;
}

interface StoredMessage {
  message: CanonicalChatMessage;
  storedAtMs: number;
}

const DEFAULT_HISTORY_LIMIT = 100;
const DEFAULT_HISTORY_TTL_MS = 86_400_000;
const DEFAULT_SNAPSHOT_LIMIT = 50;

export class ChatHistoryBuffer {
  readonly instanceId: string;

  private readonly now: () => number;
  private readonly historyLimit: number;
  private readonly historyTtlMs: number;
  private readonly snapshotLimit: number;
  private readonly entries: StoredMessage[] = [];
  private epoch = 0;

  constructor(options: ChatHistoryBufferOptions = {}) {
    this.now = options.now ?? (() => Date.now());
    this.instanceId = options.instanceId ?? randomUUID();
    this.historyLimit = options.historyLimit ?? DEFAULT_HISTORY_LIMIT;
    this.historyTtlMs = options.historyTtlMs ?? DEFAULT_HISTORY_TTL_MS;
    this.snapshotLimit = options.snapshotLimit ?? DEFAULT_SNAPSHOT_LIMIT;
  }

  get historyEpoch(): number {
    return this.epoch;
  }

  get retainedCount(): number {
    this.cleanup();
    return this.entries.length;
  }

  append(message: CanonicalChatMessage): void {
    this.cleanup();
    this.entries.push({
      message,
      storedAtMs: this.now(),
    });
    while (this.entries.length > this.historyLimit) {
      this.entries.shift();
    }
  }

  snapshot(): CanonicalChatMessage[] {
    this.cleanup();
    if (this.snapshotLimit <= 0 || this.entries.length === 0) {
      return [];
    }
    const start = Math.max(0, this.entries.length - this.snapshotLimit);
    return this.entries.slice(start).map((entry) => entry.message);
  }

  buildSnapshot(snapshotId: string, maxBytes: number): ChatSnapshotEnvelope[] {
    const messages = this.snapshot();
    const begin: ChatSnapshotBegin = {
      type: "chat_snapshot_begin",
      protocolVersion: 1,
      snapshotId,
      instanceId: this.instanceId,
      historyEpoch: this.epoch,
      totalMessages: messages.length,
    };
    const end: ChatSnapshotEnd = {
      type: "chat_snapshot_end",
      protocolVersion: 1,
      snapshotId,
      historyEpoch: this.epoch,
    };

    if (messages.length === 0) {
      return [begin, end];
    }

    const chunks = packMessagesIntoChunks(messages, snapshotId, maxBytes);
    return [begin, ...chunks, end];
  }

  clear(): number {
    this.entries.length = 0;
    this.epoch += 1;
    return this.epoch;
  }

  cleanup(): void {
    const cutoff = this.now() - this.historyTtlMs;
    while (this.entries.length > 0) {
      const oldest = this.entries[0];
      if (oldest === undefined || oldest.storedAtMs >= cutoff) {
        break;
      }
      this.entries.shift();
    }
  }
}

/**
 * Greedy pack: after each candidate append, measure the actual chunk envelope.
 * If it exceeds maxBytes and the current chunk already has messages, flush and
 * start a new chunk. A single message that alone exceeds maxBytes is still
 * emitted as a solo chunk so clients can receive it (wire budget is enforced
 * earlier at accept time for normal messages).
 */
function packMessagesIntoChunks(
  messages: CanonicalChatMessage[],
  snapshotId: string,
  maxBytes: number,
): ChatSnapshotChunk[] {
  const chunks: ChatSnapshotChunk[] = [];
  let current: CanonicalChatMessage[] = [];
  let chunkIndex = 0;

  const makeChunk = (chunkMessages: CanonicalChatMessage[], index: number): ChatSnapshotChunk => ({
    type: "chat_snapshot_chunk",
    protocolVersion: 1,
    snapshotId,
    chunkIndex: index,
    messages: chunkMessages,
  });

  for (const message of messages) {
    const candidate = [...current, message];
    const envelope = makeChunk(candidate, chunkIndex);
    const bytes = utf8JsonBytes(envelope);

    if (bytes <= maxBytes || current.length === 0) {
      // Fits, or this is the first message of a chunk (solo overflow allowed).
      current = candidate;
      // If a solo message already overflows, flush immediately so subsequent
      // messages start a fresh chunk.
      if (bytes > maxBytes && current.length === 1) {
        chunks.push(makeChunk(current, chunkIndex));
        chunkIndex += 1;
        current = [];
      }
      continue;
    }

    // Current chunk is full; flush it and start a new one with this message.
    chunks.push(makeChunk(current, chunkIndex));
    chunkIndex += 1;
    current = [message];

    // Solo overflow on the new chunk: flush immediately.
    const solo = makeChunk(current, chunkIndex);
    if (utf8JsonBytes(solo) > maxBytes) {
      chunks.push(solo);
      chunkIndex += 1;
      current = [];
    }
  }

  if (current.length > 0) {
    chunks.push(makeChunk(current, chunkIndex));
  }

  return chunks;
}
