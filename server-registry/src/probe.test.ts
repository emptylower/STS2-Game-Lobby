import test from "node:test";
import assert from "node:assert/strict";
import { computeQualityGrade, probeRegistryServer } from "./probe.js";

test("computeQualityGrade prefers bandwidth thresholds", () => {
  assert.equal(computeQualityGrade("online", 40, 60), "excellent");
  assert.equal(computeQualityGrade("online", 40, 24), "good");
  assert.equal(computeQualityGrade("online", 40, 10), "fair");
  assert.equal(computeQualityGrade("online", 40, 4), "poor");
});

test("probeRegistryServer reports offline when probe fails", async () => {
  const result = await probeRegistryServer(
    {
      id: "server_a",
      displayName: "Server A",
      baseUrl: "http://127.0.0.1:8787",
    },
    {
      fetchImpl: async () => {
        throw new Error("boom");
      },
      timeoutMs: 500,
    },
  );

  assert.equal(result.runtimeState, "offline");
  assert.equal(result.qualityGrade, "poor");
  assert.match(result.failureReason ?? "", /probe_/);
});
