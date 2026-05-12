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

test("/peers/announce bypasses per-IP rate limit when known publicKey re-announces", async () => {
  const dir = mkdtempSync(join(tmpdir(), "announce-"));
  try {
    const identity = await loadOrCreateIdentity(join(dir, "remote"));
    const remote = express();
    mountHealth(remote, { identity, address: "" });
    const remoteServer = remote.listen(0);
    const remotePort = (remoteServer.address() as { port: number }).port;
    const remoteAddress = `http://127.0.0.1:${remotePort}`;

    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    const local = express();
    local.use(express.json());
    mountAnnounce(local, { store });
    const localServer = local.listen(0);
    const localPort = (localServer.address() as { port: number }).port;

    // Twelve announces from the same source IP with the same (address, publicKey)
    // — well past the 5/hour limit. All must succeed because the (address,
    // publicKey) pair is already in the store after the first probe verifies it.
    const statuses: number[] = [];
    for (let i = 0; i < 12; i++) {
      const res = await fetch(`http://127.0.0.1:${localPort}/peers/announce`, {
        method: "POST", headers: { "content-type": "application/json" },
        body: JSON.stringify({ address: remoteAddress, publicKey: identity.publicKey }),
      });
      statuses.push(res.status);
    }
    remoteServer.close();
    localServer.close();

    for (const s of statuses) assert.equal(s, 202, `expected 202 for every known re-announce, got ${statuses.join(",")}`);
    assert.equal(store.list().length, 1);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("/peers/announce still rate-limits when same IP announces different new publicKeys", async () => {
  const dir = mkdtempSync(join(tmpdir(), "announce-"));
  try {
    // Six distinct identities, all reachable from the same loopback IP.
    const identities = await Promise.all(
      Array.from({ length: 6 }, (_, i) => loadOrCreateIdentity(join(dir, `remote-${i}`))),
    );
    const remoteServers = identities.map((identity) => {
      const remote = express();
      mountHealth(remote, { identity, address: "" });
      return remote.listen(0);
    });
    const remoteAddrs = remoteServers.map(
      (s) => `http://127.0.0.1:${(s.address() as { port: number }).port}`,
    );

    const store = new PeerStore(join(dir, "peers.json"));
    await store.load();
    const local = express();
    local.use(express.json());
    mountAnnounce(local, { store });
    const localServer = local.listen(0);
    const localPort = (localServer.address() as { port: number }).port;

    const statuses: number[] = [];
    for (let i = 0; i < 6; i++) {
      const res = await fetch(`http://127.0.0.1:${localPort}/peers/announce`, {
        method: "POST", headers: { "content-type": "application/json" },
        body: JSON.stringify({ address: remoteAddrs[i], publicKey: identities[i]!.publicKey }),
      });
      statuses.push(res.status);
    }
    for (const s of remoteServers) s.close();
    localServer.close();

    // First five new announces accepted, sixth (still a new publicKey from the
    // same IP) trips the per-hour cap.
    assert.deepEqual(statuses.slice(0, 5), [202, 202, 202, 202, 202]);
    assert.equal(statuses[5], 429);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
