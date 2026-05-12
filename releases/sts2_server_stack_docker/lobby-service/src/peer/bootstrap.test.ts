import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { PeerStore } from "./store.js";
import { bootstrapPeers } from "./bootstrap.js";
import type { ProbeResult } from "./prober.js";

test("bootstrap adds seeds that pass probe", async () => {
  const dir = mkdtempSync(join(tmpdir(), "boot-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    const probes: Record<string, ProbeResult> = {
      "https://good": { ok: true, publicKey: "pg" },
      "https://bad": { ok: false, reason: "timeout" },
    };
    await bootstrapPeers({
      store,
      selfAddress: "https://self",
      seeds: [{ address: "https://good" }, { address: "https://bad" }],
      probeAndVerify: async (addr) => probes[addr] ?? { ok: false, reason: "unknown" },
    });
    const list = store.list();
    assert.equal(list.length, 1);
    assert.equal(list[0]!.address, "https://good");
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("bootstrap skips self", async () => {
  const dir = mkdtempSync(join(tmpdir(), "boot-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    await bootstrapPeers({
      store,
      selfAddress: "https://self",
      seeds: [{ address: "https://self" }],
      probeAndVerify: async () => ({ ok: true, publicKey: "ps" }),
    });
    assert.equal(store.list().length, 0);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
