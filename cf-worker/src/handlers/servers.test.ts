import test from "node:test";
import assert from "node:assert/strict";
import { handleGetServers } from "./servers.js";

class FakeKV {
  private store = new Map<string, string>();
  async get(key: string): Promise<string | null> {
    return this.store.get(key) ?? null;
  }
  set(key: string, value: string): void {
    this.store.set(key, value);
  }
}

test("GET /v1/servers returns active list from kv", async () => {
  const kv = new FakeKV();
  kv.set("peers:active", JSON.stringify({
    version: 1,
    updated_at: "2026-05-08T00:00:00Z",
    servers: [{ address: "https://a.example", lastSeen: "2026-05-08T00:00:00Z" }],
  }));
  const env = { DISCOVERY_KV: kv as unknown as KVNamespace };
  const res = await handleGetServers(env);
  assert.equal(res.status, 200);
  const body = await res.json() as { servers: unknown[] };
  assert.equal(body.servers.length, 1);
});

test("GET /v1/servers returns empty list if KV missing", async () => {
  const kv = new FakeKV();
  const env = { DISCOVERY_KV: kv as unknown as KVNamespace };
  const res = await handleGetServers(env);
  assert.equal(res.status, 200);
  const body = await res.json() as { servers: unknown[] };
  assert.equal(body.servers.length, 0);
});
