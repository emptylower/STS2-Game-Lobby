// lobby-service/src/peer/handlers/metrics.test.ts
import test from "node:test";
import assert from "node:assert/strict";
import express from "express";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { loadOrCreateIdentity } from "../identity.js";
import { mountMetrics } from "./metrics.js";

test("/peers/metrics returns live snapshot for the picker", async () => {
  const dir = mkdtempSync(join(tmpdir(), "metrics-"));
  try {
    const identity = await loadOrCreateIdentity(dir);
    const app = express();
    mountMetrics(app, {
      identity,
      address: "https://self.example",
      getDisplayName: () => "测试服",
      getPublicListing: () => true,
      getSnapshot: () => ({
        rooms: 3,
        currentBandwidthMbps: 12.34,
        bandwidthCapacityMbps: 100,
        resolvedCapacityMbps: 100,
        bandwidthUtilizationRatio: 0.12,
        capacitySource: "manual",
        createRoomGuardApplies: true,
        createRoomGuardStatus: "allow",
      }),
    });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/metrics`);
    server.close();
    assert.equal(res.status, 200);
    const body = await res.json() as {
      rooms: number;
      currentBandwidthMbps: number;
      publicListing: boolean;
      displayName: string;
      createRoomGuardStatus: string;
    };
    assert.equal(body.rooms, 3);
    assert.equal(body.currentBandwidthMbps, 12.34);
    assert.equal(body.publicListing, true);
    assert.equal(body.displayName, "测试服");
    assert.equal(body.createRoomGuardStatus, "allow");
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("/peers/metrics surfaces publicListing=false so CF aggregator can drop the node", async () => {
  const dir = mkdtempSync(join(tmpdir(), "metrics-"));
  try {
    const identity = await loadOrCreateIdentity(dir);
    const app = express();
    mountMetrics(app, {
      identity,
      address: "https://self.example",
      getPublicListing: () => false,
      getSnapshot: () => ({
        rooms: 0,
        currentBandwidthMbps: 0,
        bandwidthCapacityMbps: null,
        resolvedCapacityMbps: null,
        bandwidthUtilizationRatio: undefined,
        capacitySource: "unknown",
        createRoomGuardApplies: false,
        createRoomGuardStatus: "unknown",
      }),
    });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/metrics`);
    server.close();
    const body = await res.json() as { publicListing: boolean };
    assert.equal(body.publicListing, false);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("/peers/metrics omits bandwidthUtilizationRatio when undefined", async () => {
  // Lobby has no traffic → utilization can't be computed. The field should
  // be absent rather than serialized as null so clients can render
  // "未计算" cleanly without ambiguity.
  const dir = mkdtempSync(join(tmpdir(), "metrics-"));
  try {
    const identity = await loadOrCreateIdentity(dir);
    const app = express();
    mountMetrics(app, {
      identity,
      address: "https://self.example",
      getPublicListing: () => true,
      getSnapshot: () => ({
        rooms: 0,
        currentBandwidthMbps: 0,
        bandwidthCapacityMbps: null,
        resolvedCapacityMbps: null,
        bandwidthUtilizationRatio: undefined,
        capacitySource: "unknown",
        createRoomGuardApplies: false,
        createRoomGuardStatus: "unknown",
      }),
    });
    const server = app.listen(0);
    const port = (server.address() as { port: number }).port;
    const res = await fetch(`http://127.0.0.1:${port}/peers/metrics`);
    server.close();
    const body = await res.json() as Record<string, unknown>;
    assert.equal(Object.prototype.hasOwnProperty.call(body, "bandwidthUtilizationRatio"), false);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
