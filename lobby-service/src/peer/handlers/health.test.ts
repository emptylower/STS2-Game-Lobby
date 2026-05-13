// lobby-service/src/peer/handlers/health.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import express from "express";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { loadOrCreateIdentity, verifySignature } from "../identity.js";
import { mountHealth } from "./health.js";

test("/peers/health returns signed challenge", async () => {
  const dir = mkdtempSync(join(tmpdir(), "health-"));
  try {
    const identity = await loadOrCreateIdentity(dir);
    const app = express();
    mountHealth(app, { identity, address: "https://self.example" });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/health?challenge=hi`);
    server.close();
    assert.equal(res.status, 200);
    const body = await res.json() as { challenge: string; signature: string; publicKey: string };
    assert.equal(body.challenge, "hi");
    assert.ok(verifySignature(body.publicKey, body.challenge, body.signature));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("/peers/health 400 if challenge missing", async () => {
  const dir = mkdtempSync(join(tmpdir(), "health-"));
  try {
    const identity = await loadOrCreateIdentity(dir);
    const app = express();
    mountHealth(app, { identity, address: "https://self.example" });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/health`);
    server.close();
    assert.equal(res.status, 400);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("/peers/health echoes publicListing=true when opt-in resolver returns true", async () => {
  const dir = mkdtempSync(join(tmpdir(), "health-"));
  try {
    const identity = await loadOrCreateIdentity(dir);
    const app = express();
    mountHealth(app, {
      identity,
      address: "https://self.example",
      getPublicListing: () => true,
    });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/health?challenge=x`);
    server.close();
    const body = await res.json() as { publicListing: boolean };
    assert.equal(body.publicListing, true);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("/peers/health echoes publicListing=false when operator opted out", async () => {
  // The node still answers (so direct-IP joins work), but the field tells
  // the CF aggregator to drop it during the next aggregation pass. This is
  // the source-of-truth for the "私有节点" admin toggle.
  const dir = mkdtempSync(join(tmpdir(), "health-"));
  try {
    const identity = await loadOrCreateIdentity(dir);
    const app = express();
    mountHealth(app, {
      identity,
      address: "https://self.example",
      getPublicListing: () => false,
    });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/health?challenge=x`);
    server.close();
    assert.equal(res.status, 200);
    const body = await res.json() as { publicListing: boolean };
    assert.equal(body.publicListing, false);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
