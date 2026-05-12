import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { PeerStore } from "./store.js";
import { GossipScheduler } from "./gossip.js";
import type { ProbeResult } from "./prober.js";

test("pull cycle merges peers from sampled known peers", async () => {
  const dir = mkdtempSync(join(tmpdir(), "gossip-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    await store.upsert({
      address: "https://a", publicKey: "pa",
      firstSeen: "2026-05-08T00:00:00Z", lastSeen: "2026-05-08T00:00:00Z",
      consecutiveProbeFailures: 0, status: "active", source: "seed",
    });

    const fakeFetchPeers = async (addr: string) => {
      if (addr === "https://a") return [{ address: "https://b", publicKey: "pb", lastSeen: "2026-05-08T00:00:00Z" }];
      return [];
    };
    const fakeProbe = async (_addr: string, _expectedKey?: string): Promise<ProbeResult> => ({ ok: true, publicKey: "pb" });

    const sched = new GossipScheduler({
      store, selfAddress: "https://self",
      fetchPeers: fakeFetchPeers, probeAndVerify: fakeProbe,
      seedAddresses: [],
    });
    await sched.runPullCycleOnce();

    const list = store.list();
    assert.ok(list.find((p) => p.address === "https://b"));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("ttl marks offline peers older than 24h, deletes those older than 7d", async () => {
  const dir = mkdtempSync(join(tmpdir(), "gossip-ttl-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    const now = new Date("2026-06-01T00:00:00Z");
    const day3Ago = new Date(now.getTime() - 3 * 86400_000);
    const day9Ago = new Date(now.getTime() - 9 * 86400_000);
    await store.upsert({
      address: "https://stale-3d", publicKey: "p1",
      firstSeen: day3Ago.toISOString(), lastSeen: day3Ago.toISOString(),
      consecutiveProbeFailures: 0, status: "active", source: "seed",
    });
    await store.upsert({
      address: "https://stale-9d", publicKey: "p2",
      firstSeen: day9Ago.toISOString(), lastSeen: day9Ago.toISOString(),
      consecutiveProbeFailures: 0, status: "active", source: "seed",
    });

    const sched = new GossipScheduler({
      store, selfAddress: "https://self",
      fetchPeers: async () => [], probeAndVerify: async () => ({ ok: false, reason: "" }),
      seedAddresses: [],
    });
    sched.runTtlCycleOnce(now);

    const after = store.list();
    assert.equal(after.find((p) => p.address === "https://stale-3d")?.status, "offline");
    assert.ok(!after.find((p) => p.address === "https://stale-9d"));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
