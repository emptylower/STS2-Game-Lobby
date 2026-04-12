import assert from "node:assert/strict";
import test from "node:test";
import { normalizeCreateRoomGuardStatus, resolveCapacityState } from "./capacity.js";

test("resolveCapacityState prefers manual capacity", () => {
  const resolved = resolveCapacityState(50, 72.5);
  assert.equal(resolved.capacitySource, "manual");
  assert.equal(resolved.bandwidthCapacityMbps, 50);
  assert.equal(resolved.probePeak7dCapacityMbps, 72.5);
  assert.equal(resolved.resolvedCapacityMbps, 50);
});

test("resolveCapacityState falls back to recent 7 day probe peak", () => {
  const resolved = resolveCapacityState(undefined, 48.88);
  assert.equal(resolved.capacitySource, "probe_peak_7d");
  assert.equal(resolved.resolvedCapacityMbps, 48.88);
});

test("resolveCapacityState returns unknown when no capacity is available", () => {
  const resolved = resolveCapacityState(undefined, undefined);
  assert.equal(resolved.capacitySource, "unknown");
  assert.equal(resolved.resolvedCapacityMbps, undefined);
});

test("normalizeCreateRoomGuardStatus accepts known states", () => {
  assert.equal(normalizeCreateRoomGuardStatus("block"), "block");
  assert.equal(normalizeCreateRoomGuardStatus("unknown"), "unknown");
  assert.equal(normalizeCreateRoomGuardStatus("other"), "allow");
});
