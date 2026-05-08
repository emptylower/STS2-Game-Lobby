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
