import assert from "node:assert/strict";
import test from "node:test";
import {
  RateLimitError,
  SlidingWindowLimiter,
  TokenBucketLimiter,
  type SlidingWindowLimiterOptions,
  type TokenBucketLimiterOptions,
} from "./rate-limiter.js";

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

function makeSliding(overrides: Partial<SlidingWindowLimiterOptions> = {}) {
  const clock = makeClock();
  const limiter = new SlidingWindowLimiter({
    now: clock.now,
    maxRequests: 20,
    windowMs: 60_000,
    maxKeys: 1_000,
    ...overrides,
  });
  return { limiter, ...clock };
}

function makeBucket(overrides: Partial<TokenBucketLimiterOptions> = {}) {
  const clock = makeClock();
  const limiter = new TokenBucketLimiter({
    now: clock.now,
    burst: 5,
    refillMs: 2_000,
    maxKeys: 1_000,
    ...overrides,
  });
  return { limiter, ...clock };
}

test("ticket requests allow 20 per minute per IP then block with integer retryAfterMs", () => {
  const { limiter, advance, now } = makeSliding({ maxRequests: 20, windowMs: 60_000 });
  const ip = "203.0.113.10";

  // Stagger hits so only the oldest leaves the window at a time.
  for (let i = 0; i < 20; i += 1) {
    assert.deepEqual(limiter.consume(ip), { allowed: true });
    if (i < 19) {
      advance(1_000);
    }
  }
  // Hits at t0, t0+1s, ..., t0+19s. now = t0+19s.

  const blocked = limiter.consume(ip);
  assert.equal(blocked.allowed, false);
  if (blocked.allowed) {
    assert.fail("expected rate limit");
  }
  assert.equal(Number.isInteger(blocked.retryAfterMs), true);
  // Oldest age is 19_000ms; remaining window is 41_000ms.
  assert.equal(blocked.retryAfterMs, 41_000);

  // One ms short of the oldest expiring: still blocked.
  advance(blocked.retryAfterMs - 1);
  const stillBlocked = limiter.consume(ip);
  assert.equal(stillBlocked.allowed, false);
  if (!stillBlocked.allowed) {
    assert.equal(Number.isInteger(stillBlocked.retryAfterMs), true);
    assert.equal(stillBlocked.retryAfterMs, 1);
  }

  // Oldest drops out; exactly one slot opens (19 remaining + 1 new = 20).
  advance(1);
  assert.deepEqual(limiter.consume(ip), { allowed: true });
  assert.equal(limiter.consume(ip).allowed, false);

  assert.ok(now() > 1_000_000);
});

test("ip messages allow 30 per minute per IP with independent keys", () => {
  const { limiter, advance } = makeSliding({ maxRequests: 30, windowMs: 60_000 });
  const ipA = "198.51.100.1";
  const ipB = "198.51.100.2";

  for (let i = 0; i < 30; i += 1) {
    assert.deepEqual(limiter.consume(ipA), { allowed: true });
  }
  assert.equal(limiter.consume(ipA).allowed, false);

  // Different IP is independent.
  for (let i = 0; i < 30; i += 1) {
    assert.deepEqual(limiter.consume(ipB), { allowed: true });
  }
  assert.equal(limiter.consume(ipB).allowed, false);

  advance(60_000);
  assert.deepEqual(limiter.consume(ipA), { allowed: true });
  assert.deepEqual(limiter.consume(ipB), { allowed: true });
});

test("connection burst is 5 with one token refill every 2 seconds", () => {
  const { limiter, advance } = makeBucket({ burst: 5, refillMs: 2_000 });
  const connectionId = "conn-1";

  for (let i = 0; i < 5; i += 1) {
    assert.deepEqual(limiter.consume(connectionId), { allowed: true });
  }

  const blocked = limiter.consume(connectionId);
  assert.equal(blocked.allowed, false);
  if (blocked.allowed) {
    assert.fail("expected rate limit");
  }
  assert.equal(Number.isInteger(blocked.retryAfterMs), true);
  assert.equal(blocked.retryAfterMs, 2_000);

  // Partial refill: 1999ms is not enough for a token.
  advance(1_999);
  assert.equal(limiter.consume(connectionId).allowed, false);

  // Exactly 2000ms refills one token.
  advance(1);
  assert.deepEqual(limiter.consume(connectionId), { allowed: true });
  assert.equal(limiter.consume(connectionId).allowed, false);

  // Idle long enough refills up to burst, not beyond.
  advance(2_000 * 10);
  for (let i = 0; i < 5; i += 1) {
    assert.deepEqual(limiter.consume(connectionId), { allowed: true });
  }
  assert.equal(limiter.consume(connectionId).allowed, false);
});

test("retryAfterMs is always a positive integer when denied by rate", () => {
  const sliding = makeSliding({ maxRequests: 1, windowMs: 10_000 });
  assert.deepEqual(sliding.limiter.consume("k"), { allowed: true });
  const sDenied = sliding.limiter.consume("k");
  assert.equal(sDenied.allowed, false);
  if (!sDenied.allowed) {
    assert.equal(Number.isInteger(sDenied.retryAfterMs), true);
    assert.ok(sDenied.retryAfterMs >= 1);
  }

  const bucket = makeBucket({ burst: 1, refillMs: 2_500 });
  assert.deepEqual(bucket.limiter.consume("c"), { allowed: true });
  const bDenied = bucket.limiter.consume("c");
  assert.equal(bDenied.allowed, false);
  if (!bDenied.allowed) {
    assert.equal(Number.isInteger(bDenied.retryAfterMs), true);
    assert.ok(bDenied.retryAfterMs >= 1);
  }
});

test("sliding window cleanup removes inactive keys", () => {
  const { limiter, advance } = makeSliding({ maxRequests: 5, windowMs: 60_000, maxKeys: 2 });

  assert.deepEqual(limiter.consume("a"), { allowed: true });
  assert.deepEqual(limiter.consume("b"), { allowed: true });

  // Capacity full for new keys while both are active.
  assert.throws(() => limiter.consume("c"), hasCode("server_busy"));

  advance(60_000);
  limiter.cleanup();

  // After window expiry + cleanup, a new key can enter.
  assert.deepEqual(limiter.consume("c"), { allowed: true });
});

test("token bucket remove frees capacity for a new key", () => {
  const { limiter } = makeBucket({ burst: 2, refillMs: 2_000, maxKeys: 1 });

  assert.deepEqual(limiter.consume("conn-a"), { allowed: true });
  assert.throws(() => limiter.consume("conn-b"), hasCode("server_busy"));

  limiter.remove("conn-a");
  assert.deepEqual(limiter.consume("conn-b"), { allowed: true });
});

test("bounded map capacity refuses unseen keys with server_busy without growing", () => {
  const sliding = makeSliding({ maxRequests: 3, windowMs: 60_000, maxKeys: 2 });
  assert.deepEqual(sliding.limiter.consume("ip-1"), { allowed: true });
  assert.deepEqual(sliding.limiter.consume("ip-2"), { allowed: true });
  assert.throws(() => sliding.limiter.consume("ip-3"), (error: unknown) => {
    assert.ok(error instanceof RateLimitError);
    assert.equal(error.code, "server_busy");
    return true;
  });
  // Existing keys remain usable.
  assert.deepEqual(sliding.limiter.consume("ip-1"), { allowed: true });
  assert.deepEqual(sliding.limiter.consume("ip-2"), { allowed: true });

  const bucket = makeBucket({ burst: 3, refillMs: 2_000, maxKeys: 1 });
  assert.deepEqual(bucket.limiter.consume("only"), { allowed: true });
  assert.throws(() => bucket.limiter.consume("other"), hasCode("server_busy"));
  assert.deepEqual(bucket.limiter.consume("only"), { allowed: true });
});

test("defaults match chat config ticket 20/min, messages 30/min, burst 5, refill 2000ms", () => {
  const clock = makeClock();
  const ticketLimiter = new SlidingWindowLimiter({ now: clock.now, purpose: "ticket" });
  const messageLimiter = new SlidingWindowLimiter({ now: clock.now, purpose: "ip_message" });
  const connectionLimiter = new TokenBucketLimiter({ now: clock.now });

  for (let i = 0; i < 20; i += 1) {
    assert.equal(ticketLimiter.consume("ip").allowed, true);
  }
  assert.equal(ticketLimiter.consume("ip").allowed, false);

  for (let i = 0; i < 30; i += 1) {
    assert.equal(messageLimiter.consume("ip").allowed, true);
  }
  assert.equal(messageLimiter.consume("ip").allowed, false);

  for (let i = 0; i < 5; i += 1) {
    assert.equal(connectionLimiter.consume("c").allowed, true);
  }
  const denied = connectionLimiter.consume("c");
  assert.equal(denied.allowed, false);
  if (!denied.allowed) {
    assert.equal(denied.retryAfterMs, 2_000);
  }
});
