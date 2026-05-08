import test from "node:test";
import assert from "node:assert/strict";
import { handleGetSeeds } from "./seeds.js";

class FakeKV {
  private store = new Map<string, string>();
  async get(key: string): Promise<string | null> {
    return this.store.get(key) ?? null;
  }
  set(key: string, value: string): void {
    this.store.set(key, value);
  }
}

test("GET /v1/seeds returns 200 with seeds payload", async () => {
  const kv = new FakeKV();
  kv.set("peers:seeds", JSON.stringify({
    version: 1,
    updated_at: "2026-05-08T00:00:00Z",
    seeds: [{ address: "https://a.example", note: "" }],
  }));
  const env = { DISCOVERY_KV: kv as unknown as KVNamespace };
  const res = await handleGetSeeds(env);
  assert.equal(res.status, 200);
  const body = await res.json() as { seeds: unknown[] };
  assert.equal(body.seeds.length, 1);
});

test("GET /v1/seeds returns 200 with empty seeds when KV missing", async () => {
  const kv = new FakeKV();
  const env = { DISCOVERY_KV: kv as unknown as KVNamespace };
  const res = await handleGetSeeds(env);
  assert.equal(res.status, 200);
  const body = await res.json() as { seeds: unknown[] };
  assert.equal(body.seeds.length, 0);
});
