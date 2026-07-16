export type RateLimitErrorCode = "server_busy";

export class RateLimitError extends Error {
  constructor(
    readonly code: RateLimitErrorCode,
    message: string,
  ) {
    super(message);
    this.name = "RateLimitError";
  }
}

export type RateLimitResult =
  | { allowed: true }
  | { allowed: false; retryAfterMs: number };

export type SlidingWindowPurpose = "ticket" | "ip_message";

export interface SlidingWindowLimiterOptions {
  now?: () => number;
  maxRequests?: number;
  windowMs?: number;
  maxKeys?: number;
  purpose?: SlidingWindowPurpose;
}

export interface TokenBucketLimiterOptions {
  now?: () => number;
  burst?: number;
  refillMs?: number;
  maxKeys?: number;
}

const DEFAULT_TICKET_MAX = 20;
const DEFAULT_IP_MESSAGE_MAX = 30;
const DEFAULT_WINDOW_MS = 60_000;
const DEFAULT_CONNECTION_BURST = 5;
const DEFAULT_CONNECTION_REFILL_MS = 2_000;
const DEFAULT_MAX_KEYS = 10_000;

interface SlidingWindowEntry {
  hits: number[];
  lastSeenAt: number;
}

interface TokenBucketEntry {
  tokens: number;
  lastRefillAt: number;
}

function positiveIntegerRetryAfter(ms: number): number {
  if (!Number.isFinite(ms) || ms <= 0) {
    return 1;
  }
  return Math.max(1, Math.ceil(ms));
}

export class SlidingWindowLimiter {
  private readonly now: () => number;
  private readonly maxRequests: number;
  private readonly windowMs: number;
  private readonly maxKeys: number;
  private readonly entries = new Map<string, SlidingWindowEntry>();

  constructor(options: SlidingWindowLimiterOptions = {}) {
    this.now = options.now ?? (() => Date.now());
    this.windowMs = options.windowMs ?? DEFAULT_WINDOW_MS;
    this.maxKeys = options.maxKeys ?? DEFAULT_MAX_KEYS;

    if (options.maxRequests != null) {
      this.maxRequests = options.maxRequests;
    } else if (options.purpose === "ip_message") {
      this.maxRequests = DEFAULT_IP_MESSAGE_MAX;
    } else {
      // Default purpose "ticket" and unspecified both match ticket 20/min.
      this.maxRequests = DEFAULT_TICKET_MAX;
    }
  }

  consume(key: string): RateLimitResult {
    const now = this.now();
    this.cleanup(now);

    let entry = this.entries.get(key);
    if (!entry) {
      if (this.entries.size >= this.maxKeys) {
        throw new RateLimitError("server_busy", "rate limiter key capacity exceeded");
      }
      entry = { hits: [], lastSeenAt: now };
      this.entries.set(key, entry);
    }

    const recent = entry.hits.filter((timestamp) => now - timestamp < this.windowMs);
    entry.hits = recent;
    entry.lastSeenAt = now;

    if (recent.length >= this.maxRequests) {
      const oldest = recent[0] ?? now;
      const retryAfterMs = positiveIntegerRetryAfter(this.windowMs - (now - oldest));
      return { allowed: false, retryAfterMs };
    }

    recent.push(now);
    return { allowed: true };
  }

  cleanup(now = this.now()): void {
    for (const [key, entry] of this.entries) {
      const recent = entry.hits.filter((timestamp) => now - timestamp < this.windowMs);
      if (recent.length === 0 && now - entry.lastSeenAt >= this.windowMs) {
        this.entries.delete(key);
        continue;
      }
      entry.hits = recent;
    }
  }
}

export class TokenBucketLimiter {
  private readonly now: () => number;
  private readonly burst: number;
  private readonly refillMs: number;
  private readonly maxKeys: number;
  private readonly entries = new Map<string, TokenBucketEntry>();

  constructor(options: TokenBucketLimiterOptions = {}) {
    this.now = options.now ?? (() => Date.now());
    this.burst = options.burst ?? DEFAULT_CONNECTION_BURST;
    this.refillMs = options.refillMs ?? DEFAULT_CONNECTION_REFILL_MS;
    this.maxKeys = options.maxKeys ?? DEFAULT_MAX_KEYS;
  }

  consume(key: string): RateLimitResult {
    const now = this.now();
    let entry = this.entries.get(key);

    if (!entry) {
      if (this.entries.size >= this.maxKeys) {
        throw new RateLimitError("server_busy", "rate limiter key capacity exceeded");
      }
      entry = { tokens: this.burst, lastRefillAt: now };
      this.entries.set(key, entry);
    } else {
      this.refill(entry, now);
    }

    if (entry.tokens < 1) {
      const elapsed = now - entry.lastRefillAt;
      const remaining = this.refillMs - (elapsed % this.refillMs);
      // When elapsed is exact multiple and tokens still 0, next token needs full interval.
      const retryAfterMs = positiveIntegerRetryAfter(
        elapsed > 0 && elapsed % this.refillMs === 0 ? this.refillMs : remaining,
      );
      return { allowed: false, retryAfterMs };
    }

    entry.tokens -= 1;
    return { allowed: true };
  }

  remove(key: string): void {
    this.entries.delete(key);
  }

  private refill(entry: TokenBucketEntry, now: number): void {
    if (now <= entry.lastRefillAt) {
      return;
    }
    const elapsed = now - entry.lastRefillAt;
    const gained = Math.floor(elapsed / this.refillMs);
    if (gained <= 0) {
      return;
    }
    entry.tokens = Math.min(this.burst, entry.tokens + gained);
    entry.lastRefillAt += gained * this.refillMs;
  }
}
