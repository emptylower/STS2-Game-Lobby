import test from "node:test";
import assert from "node:assert/strict";
import express from "express";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { loadOrCreateIdentity } from "../identity.js";
import { mountHealth } from "./health.js";
import { PeerStore } from "../store.js";
import { mountAnnounce } from "./announce.js";

test("/peers/announce accepts new peer after liveness probe succeeds", async () => {
  const dir = mkdtempSync(join(tmpdir(), "announce-"));
  try {
    const identity = await loadOrCreateIdentity(join(dir, "remote"));
    const remote = express();
    mountHealth(remote, { identity, address: "" });
    const remoteServer = remote.listen(0);
    const remotePort = (remoteServer.address() as { port: number }).port;

    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    const local = express();
    local.use(express.json());
    mountAnnounce(local, { store });
    const localServer = local.listen(0);
    const localPort = (localServer.address() as { port: number }).port;

    const res = await fetch(`http://127.0.0.1:${localPort}/peers/announce`, {
      method: "POST", headers: { "content-type": "application/json" },
      body: JSON.stringify({ address: `http://127.0.0.1:${remotePort}`, publicKey: identity.publicKey }),
    });
    remoteServer.close();
    localServer.close();
    assert.equal(res.status, 202);
    assert.equal(store.list().length, 1);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("/peers/announce rejects 400 if address missing", async () => {
  const dir = mkdtempSync(join(tmpdir(), "announce-"));
  try {
    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    const app = express();
    app.use(express.json());
    mountAnnounce(app, { store });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/announce`, {
      method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({}),
    });
    server.close();
    assert.equal(res.status, 400);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
