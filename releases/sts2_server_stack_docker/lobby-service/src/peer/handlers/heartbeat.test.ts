import test from "node:test";
import assert from "node:assert/strict";
import express from "express";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { PeerStore } from "../store.js";
import { mountHeartbeat } from "./heartbeat.js";

test("heartbeat updates lastSeen for known peer", async () => {
  const dir = mkdtempSync(join(tmpdir(), "hb-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    await store.upsert({
      address: "https://a", publicKey: "pa",
      firstSeen: "2025-01-01T00:00:00Z", lastSeen: "2025-01-01T00:00:00Z",
      consecutiveProbeFailures: 0, status: "active", source: "seed",
    });
    const app = express();
    app.use(express.json());
    mountHeartbeat(app, { store });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/heartbeat`, {
      method: "POST", headers: { "content-type": "application/json" },
      body: JSON.stringify({ address: "https://a", publicKey: "pa" }),
    });
    server.close();
    assert.equal(res.status, 200);
    const updated = store.get("https://a");
    assert.notEqual(updated?.lastSeen, "2025-01-01T00:00:00Z");
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("heartbeat 404 for unknown peer", async () => {
  const dir = mkdtempSync(join(tmpdir(), "hb-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    const app = express();
    app.use(express.json());
    mountHeartbeat(app, { store });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/heartbeat`, {
      method: "POST", headers: { "content-type": "application/json" },
      body: JSON.stringify({ address: "https://nope", publicKey: "p" }),
    });
    server.close();
    assert.equal(res.status, 404);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
