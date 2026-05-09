import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { PeerStore } from "./store.js";
import { announceToBootstrappedPeers } from "./auto-announce.js";
import type { AnnouncePayload } from "./auto-announce.js";

async function freshStore(seed: Array<{ address: string; publicKey: string; displayName?: string }>): Promise<{ store: PeerStore; cleanup: () => void }> {
  const dir = mkdtempSync(join(tmpdir(), "auto-announce-"));
  const store = new PeerStore(join(dir, "peers.json"));
  await store.load();
  const now = new Date().toISOString();
  for (const s of seed) {
    await store.upsert({
      address: s.address,
      publicKey: s.publicKey,
      ...(s.displayName ? { displayName: s.displayName } : {}),
      firstSeen: now,
      lastSeen: now,
      consecutiveProbeFailures: 0,
      status: "active",
      source: "seed",
    });
  }
  return { store, cleanup: () => rmSync(dir, { recursive: true, force: true }) };
}

test("announceToBootstrappedPeers posts self to every peer except self", async () => {
  const { store, cleanup } = await freshStore([
    { address: "https://peer-a", publicKey: "ka" },
    { address: "https://peer-b", publicKey: "kb" },
    { address: "https://self", publicKey: "kself" },
  ]);
  try {
    const calls: Array<{ address: string; body: AnnouncePayload }> = [];
    await announceToBootstrappedPeers({
      store,
      selfAddress: "https://self",
      selfPublicKey: "kself",
      selfDisplayName: "Self Lobby",
      postAnnounce: async (address, body) => { calls.push({ address, body }); },
    });
    const targets = calls.map((c) => c.address).sort();
    assert.deepEqual(targets, ["https://peer-a", "https://peer-b"]);
    for (const c of calls) {
      assert.equal(c.body.address, "https://self");
      assert.equal(c.body.publicKey, "kself");
      assert.equal(c.body.displayName, "Self Lobby");
    }
  } finally { cleanup(); }
});

test("announceToBootstrappedPeers continues on per-peer errors", async () => {
  const { store, cleanup } = await freshStore([
    { address: "https://flaky", publicKey: "kf" },
    { address: "https://good", publicKey: "kg" },
  ]);
  try {
    const successes: string[] = [];
    await announceToBootstrappedPeers({
      store,
      selfAddress: "https://self",
      selfPublicKey: "ks",
      postAnnounce: async (address) => {
        if (address === "https://flaky") throw new Error("network down");
        successes.push(address);
      },
    });
    assert.deepEqual(successes, ["https://good"]);
  } finally { cleanup(); }
});

test("announceToBootstrappedPeers is a no-op when store has only self", async () => {
  const { store, cleanup } = await freshStore([
    { address: "https://self", publicKey: "ks" },
  ]);
  try {
    let called = 0;
    await announceToBootstrappedPeers({
      store,
      selfAddress: "https://self",
      selfPublicKey: "ks",
      postAnnounce: async () => { called++; },
    });
    assert.equal(called, 0);
  } finally { cleanup(); }
});

test("announceToBootstrappedPeers omits displayName field when not provided", async () => {
  const { store, cleanup } = await freshStore([
    { address: "https://peer", publicKey: "kp" },
  ]);
  try {
    const calls: AnnouncePayload[] = [];
    await announceToBootstrappedPeers({
      store,
      selfAddress: "https://self",
      selfPublicKey: "ks",
      postAnnounce: async (_address, body) => { calls.push(body); },
    });
    assert.equal(calls.length, 1);
    assert.equal("displayName" in calls[0]!, false);
  } finally { cleanup(); }
});
