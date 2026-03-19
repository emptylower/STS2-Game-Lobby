import assert from "node:assert/strict";
import test from "node:test";
import { probeRegistryServer } from "./registry-probe.js";

test("probeRegistryServer marks healthy servers online with a usable quality grade", async () => {
  const fetchImpl: typeof fetch = async (input) => {
    const url = String(input);
    if (url.endsWith("/probe")) {
      return new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { "content-type": "application/json" },
      });
    }

    if (url.endsWith("/health")) {
      return new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { "content-type": "application/json" },
      });
    }

    return new Response(
      new ReadableStream({
        start(controller) {
          controller.enqueue(new Uint8Array(1024 * 1024));
          controller.close();
        },
      }),
      { status: 200 },
    );
  };

  const result = await probeRegistryServer({
    id: "server-1",
    displayName: "官方测试服",
    baseUrl: "http://127.0.0.1:18787",
    bandwidthProbeUrl: "http://127.0.0.1:18787/speed.bin",
  }, { fetchImpl });

  assert.equal(result.runtimeState, "online");
  assert.equal(result.healthOk, true);
  assert.equal(result.probeOk, true);
  assert.equal(typeof result.lastProbeRttMs, "number");
  assert.equal(typeof result.lastBandwidthMbps, "number");
  assert.notEqual(result.qualityGrade, "poor");
});

test("probeRegistryServer reports offline when probe fails", async () => {
  const fetchImpl: typeof fetch = async (input) => {
    const url = String(input);
    if (url.endsWith("/probe")) {
      throw new Error("connect ECONNREFUSED");
    }

    return new Response(JSON.stringify({ ok: false }), {
      status: 503,
      headers: { "content-type": "application/json" },
    });
  };

  const result = await probeRegistryServer({
    id: "server-2",
    displayName: "离线服",
    baseUrl: "http://127.0.0.1:28787",
  }, { fetchImpl });

  assert.equal(result.runtimeState, "offline");
  assert.equal(result.qualityGrade, "poor");
  assert.match(result.failureReason ?? "", /probe/);
});

test("probeRegistryServer preserves maintenance runtime state", async () => {
  const fetchImpl: typeof fetch = async () =>
    new Response(JSON.stringify({ ok: true }), {
      status: 200,
      headers: { "content-type": "application/json" },
    });

  const result = await probeRegistryServer({
    id: "server-3",
    displayName: "维护服",
    baseUrl: "http://127.0.0.1:38787",
    runtimeState: "maintenance",
  }, { fetchImpl });

  assert.equal(result.runtimeState, "maintenance");
  assert.equal(result.qualityGrade, "unknown");
});
