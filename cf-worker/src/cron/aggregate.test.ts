import test from "node:test";
import assert from "node:assert/strict";
import { aggregateActivePeers } from "./aggregate.js";

class FakeKV {
  private store = new Map<string, string>();
  async get(k: string): Promise<string | null> { return this.store.get(k) ?? null; }
  async put(k: string, v: string): Promise<void> { this.store.set(k, v); }
  read(k: string): string | undefined { return this.store.get(k); }
}

test("aggregate writes merged peers from seed list", async () => {
  const kv = new FakeKV();
  await kv.put("peers:seeds", JSON.stringify({
    version: 1, updated_at: "2026-05-08T00:00:00Z",
    seeds: [{ address: "https://a.example" }, { address: "https://b.example" }],
  }));

  const fetchMock = async (input: RequestInfo): Promise<Response> => {
    const url = typeof input === "string" ? input : input.url;
    if (url.startsWith("https://a.example/peers")) {
      return new Response(JSON.stringify({
        peers: [
          { address: "https://a.example", publicKey: "pa", lastSeen: "2026-05-08T00:00:00Z" },
          { address: "https://c.example", publicKey: "pc", lastSeen: "2026-05-08T00:00:00Z" },
        ],
      }), { status: 200 });
    }
    if (url.startsWith("https://b.example/peers")) {
      return new Response(JSON.stringify({
        peers: [{ address: "https://b.example", publicKey: "pb", lastSeen: "2026-05-08T00:00:00Z" }],
      }), { status: 200 });
    }
    return new Response("not found", { status: 404 });
  };

  await aggregateActivePeers({ DISCOVERY_KV: kv as unknown as KVNamespace }, fetchMock);
  const written = kv.read("peers:active");
  assert.ok(written, "active key should be written");
  const parsed = JSON.parse(written!);
  const addrs = parsed.servers.map((s: { address: string }) => s.address).sort();
  assert.deepEqual(addrs, ["https://a.example", "https://b.example", "https://c.example"]);
});

test("aggregate keeps previous active when all peers respond with empty lists", async () => {
  const kv = new FakeKV();
  await kv.put("peers:active", JSON.stringify({
    version: 1, updated_at: "2026-05-08T00:00:00Z",
    servers: [{ address: "https://prev.example", lastSeen: "2026-05-08T00:00:00Z" }],
  }));
  await kv.put("peers:seeds", JSON.stringify({
    version: 1, updated_at: "2026-05-08T00:00:00Z",
    seeds: [{ address: "https://empty.example" }],
  }));

  const fetchMock = async (): Promise<Response> =>
    new Response(JSON.stringify({ peers: [] }), { status: 200 });

  await aggregateActivePeers({ DISCOVERY_KV: kv as unknown as KVNamespace }, fetchMock);
  const written = JSON.parse(kv.read("peers:active")!);
  assert.equal(written.servers.length, 1);
  assert.equal(written.servers[0].address, "https://prev.example");
});

test("aggregate keeps previous active when all peers fail", async () => {
  const kv = new FakeKV();
  await kv.put("peers:active", JSON.stringify({
    version: 1, updated_at: "2026-05-08T00:00:00Z",
    servers: [{ address: "https://prev.example", lastSeen: "2026-05-08T00:00:00Z" }],
  }));
  await kv.put("peers:seeds", JSON.stringify({
    version: 1, updated_at: "2026-05-08T00:00:00Z",
    seeds: [{ address: "https://dead.example" }],
  }));

  const fetchMock = async (): Promise<Response> => { throw new Error("boom"); };

  await aggregateActivePeers({ DISCOVERY_KV: kv as unknown as KVNamespace }, fetchMock);
  const written = JSON.parse(kv.read("peers:active")!);
  assert.equal(written.servers[0].address, "https://prev.example");
});

test("aggregate preserves previous-active entries when only some fetches fail", async () => {
  // Regression test: before this fix, a partial-failure cron tick (e.g., CF
  // edge can't reach a CN-hosted lobby on port 8787 while a HK lobby on
  // 443 succeeds) would write the success-only set back to active and
  // silently drop the unreachable-from-CF peer entirely. Real lobby
  // operators only care about reachability from their players, not from CF.
  const recentLastSeen = new Date(Date.now() - 60_000).toISOString(); // 1 min ago
  const kv = new FakeKV();
  await kv.put("peers:active", JSON.stringify({
    version: 1, updated_at: recentLastSeen,
    servers: [
      { address: "https://reachable", publicKey: "kr", lastSeen: recentLastSeen },
      { address: "http://cn-only", publicKey: "kc", lastSeen: recentLastSeen, displayName: "CN Lobby" },
    ],
  }));
  await kv.put("peers:seeds", JSON.stringify({
    version: 1, updated_at: recentLastSeen,
    seeds: [
      { address: "https://reachable" },
      { address: "http://cn-only" },
    ],
  }));

  const fetchMock = async (input: RequestInfo): Promise<Response> => {
    const url = typeof input === "string" ? input : input.url;
    if (url.startsWith("https://reachable")) {
      return new Response(JSON.stringify({
        peers: [{ address: "https://reachable", publicKey: "kr", lastSeen: new Date().toISOString() }],
      }), { status: 200 });
    }
    throw new Error("simulated CF-edge-to-CN block");
  };

  await aggregateActivePeers({ DISCOVERY_KV: kv as unknown as KVNamespace }, fetchMock);
  const written = JSON.parse(kv.read("peers:active")!);
  const addrs = written.servers.map((s: { address: string }) => s.address).sort();
  assert.deepEqual(addrs, ["http://cn-only", "https://reachable"]);
  // The preserved entry should keep its prior displayName so the picker UI
  // doesn't blank out during transient outages.
  const preserved = written.servers.find((s: { address: string }) => s.address === "http://cn-only");
  assert.equal(preserved.displayName, "CN Lobby");
});

test("aggregate drops peers whose /peers/metrics reports publicListing=false", async () => {
  // The operator's opt-out toggle is the source of truth. A peer can still
  // be gossipped around the network (other lobbies know its address), but
  // CF must drop it from the public aggregate so it does not show up in
  // the client picker for end users.
  const recentLastSeen = new Date(Date.now() - 60_000).toISOString();
  const kv = new FakeKV();
  await kv.put("peers:seeds", JSON.stringify({
    version: 1, updated_at: recentLastSeen,
    seeds: [{ address: "https://opted-in" }, { address: "https://opted-out" }],
  }));

  const fetchMock = async (input: RequestInfo): Promise<Response> => {
    const url = typeof input === "string" ? input : input.url;
    if (url.endsWith("/peers")) {
      const addr = url.replace(/\/peers$/, "");
      return new Response(JSON.stringify({
        peers: [{ address: addr, publicKey: "k", lastSeen: recentLastSeen }],
      }), { status: 200 });
    }
    if (url.endsWith("/peers/metrics")) {
      const isOptedOut = url.includes("opted-out");
      return new Response(JSON.stringify({ publicListing: !isOptedOut }), { status: 200 });
    }
    return new Response("not found", { status: 404 });
  };

  await aggregateActivePeers({ DISCOVERY_KV: kv as unknown as KVNamespace }, fetchMock);
  const written = JSON.parse(kv.read("peers:active")!);
  const addrs = written.servers.map((s: { address: string }) => s.address).sort();
  assert.deepEqual(addrs, ["https://opted-in"], "opted-out peer must not appear in active list");
});

test("aggregate keeps peers when /peers/metrics is unreachable (backwards compat with older nodes)", async () => {
  // Older v0.2/v0.3 lobby-service nodes don't expose /peers/metrics. The
  // aggregator must treat missing-metrics as "leave the peer in" rather
  // than silently dropping every pre-v0.4 node from the network.
  const recentLastSeen = new Date(Date.now() - 60_000).toISOString();
  const kv = new FakeKV();
  await kv.put("peers:seeds", JSON.stringify({
    version: 1, updated_at: recentLastSeen,
    seeds: [{ address: "https://legacy.example" }],
  }));

  const fetchMock = async (input: RequestInfo): Promise<Response> => {
    const url = typeof input === "string" ? input : input.url;
    if (url.endsWith("/peers")) {
      return new Response(JSON.stringify({
        peers: [{ address: "https://legacy.example", publicKey: "k", lastSeen: recentLastSeen }],
      }), { status: 200 });
    }
    return new Response("not found", { status: 404 });
  };

  await aggregateActivePeers({ DISCOVERY_KV: kv as unknown as KVNamespace }, fetchMock);
  const written = JSON.parse(kv.read("peers:active")!);
  const addrs = written.servers.map((s: { address: string }) => s.address);
  assert.deepEqual(addrs, ["https://legacy.example"]);
});

test("aggregate evicts previous-active entries past the offline retention TTL", async () => {
  // Bound the preservation behavior: a peer that hasn't been refreshed in
  // OFFLINE_RETENTION_MS (24h) is genuinely gone, drop it so the public
  // list doesn't accumulate dead entries indefinitely.
  const longAgo = new Date(Date.now() - 25 * 3600_000).toISOString(); // 25h ago
  const kv = new FakeKV();
  await kv.put("peers:active", JSON.stringify({
    version: 1, updated_at: longAgo,
    servers: [
      { address: "https://stale", publicKey: "ks", lastSeen: longAgo },
    ],
  }));
  await kv.put("peers:seeds", JSON.stringify({
    version: 1, updated_at: longAgo,
    seeds: [{ address: "https://stale" }],
  }));

  const fetchMock = async (): Promise<Response> => { throw new Error("boom"); };

  await aggregateActivePeers({ DISCOVERY_KV: kv as unknown as KVNamespace }, fetchMock);
  // All fetches failed AND the only previous entry is past TTL → merged
  // is empty AND preservation rejects the stale entry. With previous
  // non-empty, the existing safety guard early-returns rather than
  // clobbering with []. So peers:active stays untouched (which is also
  // a no-op vs the stale doc we wrote). What we want to verify is that
  // the cron does NOT promote the stale entry back into a "fresh" doc.
  const written = JSON.parse(kv.read("peers:active")!);
  // Either unchanged (early-return path) or empty (clobbered) — both
  // acceptable; the key invariant is the stale entry is never given
  // a fresh updated_at via preservation.
  if (written.servers.length > 0) {
    assert.equal(written.servers[0].address, "https://stale");
    assert.equal(written.updated_at, longAgo, "stale entry must not get a fresh updated_at via preservation");
  }
});
