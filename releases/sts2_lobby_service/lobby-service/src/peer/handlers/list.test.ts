import test from "node:test";
import assert from "node:assert/strict";
import express from "express";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { PeerStore } from "../store.js";
import { mountList } from "./list.js";

test("/peers returns active peers only by default", async () => {
  const dir = mkdtempSync(join(tmpdir(), "list-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    await store.upsert({
      address: "https://a", publicKey: "pa", firstSeen: "2026-05-08T00:00:00Z",
      lastSeen: "2026-05-08T00:00:00Z", consecutiveProbeFailures: 0, status: "active", source: "seed",
    });
    await store.upsert({
      address: "https://b", publicKey: "pb", firstSeen: "2026-05-08T00:00:00Z",
      lastSeen: "2026-05-08T00:00:00Z", consecutiveProbeFailures: 5, status: "offline", source: "seed",
    });

    const app = express();
    mountList(app, { store });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers`);
    server.close();
    assert.equal(res.status, 200);
    const body = await res.json() as { peers: Array<{ address: string }> };
    assert.equal(body.peers.length, 1);
    assert.equal(body.peers[0]!.address, "https://a");
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
