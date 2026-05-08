// lobby-service/src/peer/store.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { PeerStore } from "./store.js";

function tmpDir(): string { return mkdtempSync(join(tmpdir(), "peer-store-")); }

test("upsert + list + persistence roundtrip", async () => {
  const dir = tmpDir();
  try {
    const s1 = new PeerStore(join(dir, "peers.json"));
    await s1.load();
    await s1.upsert({
      address: "https://a.example",
      publicKey: "pa",
      firstSeen: "2026-05-08T00:00:00Z",
      lastSeen: "2026-05-08T00:00:00Z",
      consecutiveProbeFailures: 0,
      status: "active",
      source: "seed",
    });
    await s1.flush();

    const s2 = new PeerStore(join(dir, "peers.json"));
    await s2.load();
    const list = s2.list();
    assert.equal(list.length, 1);
    assert.equal(list[0]!.address, "https://a.example");
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("recordProbeFailure marks offline at threshold and TTL deletes", async () => {
  const dir = tmpDir();
  try {
    const s = new PeerStore(join(dir, "peers.json"));
    await s.load();
    await s.upsert({
      address: "https://a.example",
      publicKey: "pa",
      firstSeen: "2026-05-08T00:00:00Z",
      lastSeen: "2026-05-08T00:00:00Z",
      consecutiveProbeFailures: 0,
      status: "active",
      source: "seed",
    });
    s.recordProbeFailure("https://a.example");
    s.recordProbeFailure("https://a.example");
    s.recordProbeFailure("https://a.example");
    assert.equal(s.list()[0]!.status, "offline");

    const cutoff8d = new Date(Date.now() - 8 * 86400_000);
    s.list()[0]!.lastSeen = cutoff8d.toISOString();
    s.runTtlCleanup(new Date());
    assert.equal(s.list().length, 0);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
