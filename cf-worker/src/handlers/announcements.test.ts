import test from "node:test";
import assert from "node:assert/strict";
import { handleGetAnnouncements } from "./announcements.js";

class FakeKV {
  private store = new Map<string, string>();
  async get(k: string): Promise<string | null> { return this.store.get(k) ?? null; }
  set(k: string, v: string): void { this.store.set(k, v); }
}

test("GET /v1/announcements returns items", async () => {
  const kv = new FakeKV();
  kv.set("announcements", JSON.stringify({
    version: 1,
    updated_at: "2026-05-08T00:00:00Z",
    items: [{ id: "1", title: "t", body: "b", publishedAt: "2026-05-08T00:00:00Z" }],
  }));
  const env = { DISCOVERY_KV: kv as unknown as KVNamespace };
  const res = await handleGetAnnouncements(env);
  assert.equal(res.status, 200);
  const body = await res.json() as { items: unknown[] };
  assert.equal(body.items.length, 1);
});
