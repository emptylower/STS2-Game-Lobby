import type { CanonicalChatMessage, ChatProtocolErrorCode } from "./protocol.js";

export type ChatAckEnvelope = {
  type: "chat_ack";
  protocolVersion: 1;
  clientMessageId: string;
  message: CanonicalChatMessage;
};

export type ChatErrorEnvelope = {
  type: "chat_error";
  protocolVersion: 1;
  clientMessageId: string;
  code: ChatProtocolErrorCode;
  message: string;
  retryAfterMs?: number;
};

export type ChatDedupeResult = ChatAckEnvelope | ChatErrorEnvelope;

export type DedupeLookup =
  | { kind: "miss" }
  | { kind: "replay"; result: ChatDedupeResult }
  | { kind: "conflict"; code: "duplicate_message" };

export type ChatDedupeErrorCode = "server_busy";

export class ChatDedupeError extends Error {
  constructor(
    readonly code: ChatDedupeErrorCode,
    message: string,
  ) {
    super(message);
    this.name = "ChatDedupeError";
  }
}

export interface ChatDedupeCacheOptions {
  now?: () => number;
  maxEntriesPerSession?: number;
  sessionTtlMs?: number;
  maxSessions?: number;
}

interface DedupeEntry {
  canonicalJson: string;
  result: ChatDedupeResult;
}

interface SessionBucket {
  entries: Map<string, DedupeEntry>;
  lastSeenAt: number;
}

const DEFAULT_MAX_ENTRIES_PER_SESSION = 256;
const DEFAULT_SESSION_TTL_MS = 10 * 60_000;
const DEFAULT_MAX_SESSIONS = 10_000;

export class ChatDedupeCache {
  private readonly now: () => number;
  private readonly maxEntriesPerSession: number;
  private readonly sessionTtlMs: number;
  private readonly maxSessions: number;
  private readonly sessions = new Map<string, SessionBucket>();

  constructor(options: ChatDedupeCacheOptions = {}) {
    this.now = options.now ?? (() => Date.now());
    this.maxEntriesPerSession = options.maxEntriesPerSession ?? DEFAULT_MAX_ENTRIES_PER_SESSION;
    this.sessionTtlMs = options.sessionTtlMs ?? DEFAULT_SESSION_TTL_MS;
    this.maxSessions = options.maxSessions ?? DEFAULT_MAX_SESSIONS;
  }

  lookup(sessionId: string, clientMessageId: string, canonicalJson: string): DedupeLookup {
    const now = this.now();
    this.cleanup(now);

    const session = this.sessions.get(sessionId);
    if (!session) {
      return { kind: "miss" };
    }

    session.lastSeenAt = now;
    // Touch session order for TTL bookkeeping (Map is insertion-ordered).
    this.sessions.delete(sessionId);
    this.sessions.set(sessionId, session);

    const entry = session.entries.get(clientMessageId);
    if (!entry) {
      return { kind: "miss" };
    }

    if (entry.canonicalJson === canonicalJson) {
      return { kind: "replay", result: entry.result };
    }

    return { kind: "conflict", code: "duplicate_message" };
  }

  store(
    sessionId: string,
    clientMessageId: string,
    canonicalJson: string,
    result: ChatDedupeResult,
  ): void {
    const now = this.now();
    this.cleanup(now);

    let session = this.sessions.get(sessionId);
    if (!session) {
      if (this.sessions.size >= this.maxSessions) {
        throw new ChatDedupeError("server_busy", "dedupe session capacity exceeded");
      }
      session = {
        entries: new Map(),
        lastSeenAt: now,
      };
      this.sessions.set(sessionId, session);
    } else {
      session.lastSeenAt = now;
      this.sessions.delete(sessionId);
      this.sessions.set(sessionId, session);
    }

    // Re-store updates insertion order so re-sent IDs count as newest.
    if (session.entries.has(clientMessageId)) {
      session.entries.delete(clientMessageId);
    }
    session.entries.set(clientMessageId, {
      canonicalJson,
      result,
    });

    while (session.entries.size > this.maxEntriesPerSession) {
      const oldestKey = session.entries.keys().next().value;
      if (oldestKey === undefined) {
        break;
      }
      session.entries.delete(oldestKey);
    }
  }

  cleanup(now = this.now()): void {
    for (const [sessionId, session] of this.sessions) {
      if (now - session.lastSeenAt >= this.sessionTtlMs) {
        this.sessions.delete(sessionId);
      }
    }
  }
}
