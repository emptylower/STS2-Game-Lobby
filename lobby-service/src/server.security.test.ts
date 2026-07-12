import test from "node:test";
import assert from "node:assert/strict";
import {
  cleanupCreateJoinRateLimitBuckets,
  consumeCreateJoinRateLimit,
  getCreateRoomToken,
  getLobbyAccessToken,
  type CreateJoinRateLimitBucket,
} from "./client-ip.js";

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
    headers,
    body: options.body,
  };
}

test("security helpers run without importing the listening server", () => {
  assert.equal(typeof getLobbyAccessToken, "function");
  assert.equal(typeof consumeCreateJoinRateLimit, "function");
});

test("token extraction reads header and bearer token but ignores query-only token input", () => {
  const headerReq = makeRequest({
    headers: {
      "x-lobby-access-token": "header-token",
    },
  });
  assert.equal(getLobbyAccessToken(headerReq), "header-token");

  const bearerReq = makeRequest({
    headers: {
      authorization: "Bearer bearer-token",
    },
  });
  assert.equal(getLobbyAccessToken(bearerReq), "bearer-token");

  const createReq = makeRequest({
    headers: {
      "x-create-room-token": "create-token",
    },
  });
  assert.equal(getCreateRoomToken(createReq), "create-token");

  const queryOnlyReq = {
    ...makeRequest({}),
    query: {
      createRoomToken: "query-token",
      lobbyAccessToken: "query-read-token",
    },
  } as any;
  assert.equal(getLobbyAccessToken(queryOnlyReq), undefined);
  assert.equal(getCreateRoomToken(queryOnlyReq), undefined);
});

test("consumeCreateJoinRateLimit blocks bursts and cleanup evicts expired buckets", () => {
  const hits = new Map<string, CreateJoinRateLimitBucket>();
  const ip = "10.0.0.8";
  const scope = "join_room";
  const bucketKey = `${scope}:${ip}`;
  const originalNow = Date.now;

  try {
    let now = 1_000_000;
    Date.now = () => now;

    for (let index = 0; index < 30; index++) {
      assert.equal(consumeCreateJoinRateLimit(hits, scope, ip, 60_000, 30), false);
      now += 1;
    }

    assert.equal(consumeCreateJoinRateLimit(hits, scope, ip, 60_000, 30), true);

    const bucket = hits.get(bucketKey);
    assert.ok(bucket);
    assert.equal(bucket.hits.length, 30);

    now += 61_000;
    cleanupCreateJoinRateLimitBuckets(hits, now, 60_000);
    assert.equal(hits.has(bucketKey), false);
  } finally {
    Date.now = originalNow;
  }
});
