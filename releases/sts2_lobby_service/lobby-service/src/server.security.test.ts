import test from "node:test";
import assert from "node:assert/strict";
import { __testHooks } from "./server.js";

function makeRequest(options: {
  remoteAddress?: string;
  headers?: Record<string, string | undefined>;
  body?: unknown;
}) {
  const headers = Object.fromEntries(
    Object.entries(options.headers ?? {}).map(([key, value]) => [key.toLowerCase(), value]),
  );

  return {
    socket: {
      remoteAddress: options.remoteAddress ?? "127.0.0.1",
    },
    header(name: string) {
      const value = headers[name.toLowerCase()];
      return typeof value === "string" ? value : undefined;
    },
    body: options.body,
  } as any;
}

test("normalizeRemoteIp collapses IPv4-mapped IPv6 addresses", () => {
  assert.equal(__testHooks.normalizeRemoteIp("::ffff:127.0.0.1"), "127.0.0.1");
  assert.equal(__testHooks.normalizeRemoteIp(" ::1 "), "::1");
  assert.equal(__testHooks.normalizeRemoteIp(""), "");
});

test("ipMatchesCandidate supports IPv4, IPv6, and IPv4-mapped IPv6 loopback forms", () => {
  assert.equal(__testHooks.ipMatchesCandidate("127.0.0.1", "127.0.0.1"), true);
  assert.equal(__testHooks.ipMatchesCandidate("::1", "::1"), true);
  assert.equal(__testHooks.ipMatchesCandidate("::ffff:127.0.0.1", "127.0.0.1"), true);
  assert.equal(__testHooks.ipMatchesCandidate("127.0.0.42", "127.0.0.0/24"), true);
  assert.equal(__testHooks.ipMatchesCandidate("2001:db8::10", "2001:db8::/64"), true);
  assert.equal(__testHooks.ipMatchesCandidate("2001:db9::10", "2001:db8::/64"), false);
});

test("token extraction reads header and bearer token but ignores query-only token input", () => {
  const headerReq = makeRequest({
    headers: {
      "x-lobby-access-token": "header-token",
    },
  });
  assert.equal(__testHooks.getLobbyAccessToken(headerReq), "header-token");

  const bearerReq = makeRequest({
    headers: {
      authorization: "Bearer bearer-token",
    },
  });
  assert.equal(__testHooks.getLobbyAccessToken(bearerReq), "bearer-token");

  const createReq = makeRequest({
    headers: {
      "x-create-room-token": "create-token",
    },
  });
  assert.equal(__testHooks.getCreateRoomToken(createReq), "create-token");

  const queryOnlyReq = {
    ...makeRequest({}),
    query: {
      createRoomToken: "query-token",
      lobbyAccessToken: "query-read-token",
    },
  } as any;
  assert.equal(__testHooks.getLobbyAccessToken(queryOnlyReq), undefined);
  assert.equal(__testHooks.getCreateRoomToken(queryOnlyReq), undefined);
});

test("assertCreateJoinRateLimit blocks bursts and cleanupRateLimitBuckets evicts expired buckets", () => {
  const req = makeRequest({ remoteAddress: "10.0.0.8" });
  const bucketKey = "join_room:10.0.0.8";
  const originalNow = Date.now;

  try {
    __testHooks.createJoinRateLimitHits.clear();

    let now = 1_000_000;
    Date.now = () => now;

    for (let index = 0; index < 30; index++) {
      __testHooks.assertCreateJoinRateLimit(req, "join_room");
      now += 1;
    }

    assert.throws(
      () => __testHooks.assertCreateJoinRateLimit(req, "join_room"),
      (error: unknown) => (error as { statusCode?: number; code?: string }).statusCode === 429
        && (error as { statusCode?: number; code?: string }).code === "rate_limited",
    );

    const bucket = __testHooks.createJoinRateLimitHits.get(bucketKey);
    assert.ok(bucket);
    assert.equal(bucket.hits.length, 30);

    now += 61_000;
    __testHooks.cleanupRateLimitBuckets(now, 60_000);
    assert.equal(__testHooks.createJoinRateLimitHits.has(bucketKey), false);
  } finally {
    Date.now = originalNow;
    __testHooks.createJoinRateLimitHits.clear();
  }
});

test("server test hooks can close runtime resources after import", async () => {
  await new Promise<void>((resolve) => {
    __testHooks.closeRuntimeResources(resolve);
  });
});
