import assert from "node:assert/strict";
import test from "node:test";
import { CreateRoomBandwidthGuard } from "./bandwidth-guard.js";
import { RollingBandwidthMeter } from "./rolling-bandwidth.js";

test("rolling bandwidth meter uses a fixed 30 second window", () => {
  const meter = new RollingBandwidthMeter(30_000);
  meter.recordBytes(3_750_000, 1_000);
  const snapshot = meter.getSnapshot(1_000);
  assert.equal(snapshot.currentBandwidthMbps, 1);
  assert.equal(snapshot.totalBytesInWindow, 3_750_000);

  const expired = meter.getSnapshot(31_500);
  assert.equal(expired.currentBandwidthMbps, 0);
  assert.equal(expired.totalBytesInWindow, 0);
});

test("bandwidth guard prefers manual capacity over probe fallback", () => {
  const guard = new CreateRoomBandwidthGuard();
  const snapshot = guard.getSnapshot({
    createRoomGuardApplies: true,
    currentBandwidthMbps: 18,
    bandwidthCapacityMbps: 20,
    probePeak7dCapacityMbps: 60,
  });

  assert.equal(snapshot.capacitySource, "manual");
  assert.equal(snapshot.resolvedCapacityMbps, 20);
  assert.equal(snapshot.bandwidthUtilizationRatio, 0.9);
  assert.equal(snapshot.createRoomGuardStatus, "block");
});

test("bandwidth guard falls back to recent probe peak when manual capacity is missing", () => {
  const guard = new CreateRoomBandwidthGuard();
  const snapshot = guard.getSnapshot({
    createRoomGuardApplies: true,
    currentBandwidthMbps: 27,
    probePeak7dCapacityMbps: 30,
  });

  assert.equal(snapshot.capacitySource, "probe_peak_7d");
  assert.equal(snapshot.resolvedCapacityMbps, 30);
  assert.equal(snapshot.createRoomGuardStatus, "block");
});

test("bandwidth guard uses hysteresis between block and allow", () => {
  const guard = new CreateRoomBandwidthGuard();
  const blocked = guard.getSnapshot({
    createRoomGuardApplies: true,
    currentBandwidthMbps: 45,
    bandwidthCapacityMbps: 50,
  });
  assert.equal(blocked.createRoomGuardStatus, "block");

  const stillBlocked = guard.getSnapshot({
    createRoomGuardApplies: true,
    currentBandwidthMbps: 43,
    bandwidthCapacityMbps: 50,
  });
  assert.equal(stillBlocked.createRoomGuardStatus, "block");

  const released = guard.getSnapshot({
    createRoomGuardApplies: true,
    currentBandwidthMbps: 41,
    bandwidthCapacityMbps: 50,
  });
  assert.equal(released.createRoomGuardStatus, "allow");
});

test("bandwidth guard returns unknown when capacity is missing", () => {
  const guard = new CreateRoomBandwidthGuard();
  const snapshot = guard.getSnapshot({
    createRoomGuardApplies: true,
    currentBandwidthMbps: 5,
  });

  assert.equal(snapshot.capacitySource, "unknown");
  assert.equal(snapshot.createRoomGuardStatus, "unknown");
  assert.equal(snapshot.resolvedCapacityMbps, undefined);
});
