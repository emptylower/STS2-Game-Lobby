import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { ModerationCacheStore } from "./cache-store.js";

test("cache persists HMAC keys, enforces LRU capacity, and expires entries", () => {
  const dir = mkdtempSync(join(tmpdir(), "sts2-ai-cache-"));
  const path = join(dir, "cache.json");
  let now = 1_000;
  const options = {
    now: () => now,
    exactTtlMs: 100,
    contextTtlMs: 50,
    maxExactEntries: 2,
    maxContextEntries: 2,
  };
  const cache = new ModerationCacheStore(path, options);
  cache.allowExact("hash-a");
  now += 1;
  cache.allowExact("hash-b");
  assert.equal(cache.hasExact("hash-a"), true);
  now += 1;
  cache.allowExact("hash-c");
  assert.equal(cache.hasExact("hash-b"), false);
  assert.equal(cache.hasExact("hash-a"), true);
  assert.equal(new ModerationCacheStore(path, options).hasExact("hash-c"), true);

  cache.allowContext("context-a");
  cache.disallowContext("context-a");
  assert.equal(cache.hasContext("context-a"), false);
  cache.allowExact("hash-remove");
  cache.disallowExact("hash-remove");
  assert.equal(cache.hasExact("hash-remove"), false);
  const reopenedAfterRemoval = new ModerationCacheStore(path, options);
  assert.equal(reopenedAfterRemoval.hasExact("hash-remove"), false);
  assert.equal(reopenedAfterRemoval.hasContext("context-a"), false);
  cache.allowContext("context-a");
  now += 51;
  assert.equal(cache.hasContext("context-a"), false);
});
