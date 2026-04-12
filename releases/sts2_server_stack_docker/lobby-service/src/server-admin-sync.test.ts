import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { ServerAdminStateStore } from "./server-admin-state.js";
import { createServerRegistrySyncService } from "./server-admin-sync.js";

function createTempStatePath() {
  const directory = mkdtempSync(join(tmpdir(), "sts2-server-admin-sync-"));
  return {
    directory,
    path: join(directory, "server-admin.json"),
  };
}

function createGuardSnapshot() {
  return {
    createRoomGuardApplies: false,
    createRoomGuardStatus: "allow" as const,
    currentBandwidthMbps: 0,
    capacitySource: "unknown" as const,
    createRoomThresholdRatio: 0.9,
    createRoomReleaseThresholdRatio: 0.85,
  };
}

test("registry sync fails fast when public listing points at loopback endpoints", async () => {
  const temp = createTempStatePath();
  const stateStore = new ServerAdminStateStore(temp.path);
  stateStore.updateSettings({
    displayName: "Test Server",
    publicListingEnabled: true,
  });

  const originalFetch = globalThis.fetch;
  let fetchCalled = false;
  globalThis.fetch = async () => {
    fetchCalled = true;
    throw new Error("fetch should not be called for loopback public URLs");
  };

  try {
    const syncService = createServerRegistrySyncService({
      env: {
        registryBaseUrl: "http://registry.example.com",
        timeoutMs: 1000,
        publicBaseUrl: "http://127.0.0.1:8787",
        publicWsUrl: "ws://127.0.0.1:8787/control",
        bandwidthProbeUrl: "http://127.0.0.1:8787/registry/bandwidth-probe.bin",
      },
      stateStore,
      getRoomCount: () => 0,
      getGuardSnapshot: () => createGuardSnapshot(),
    });

    await syncService.runNow();

    const settings = stateStore.getSettingsView();
    assert.equal(fetchCalled, false);
    assert.equal(settings.lastSyncStatus, "public_endpoint_invalid");
    assert.match(settings.lastSyncError, /SERVER_REGISTRY_PUBLIC_BASE_URL/);
  } finally {
    globalThis.fetch = originalFetch;
    rmSync(temp.directory, { recursive: true, force: true });
  }
});

test("registry sync marks submission as created after parent registry accepts the request", async () => {
  const temp = createTempStatePath();
  const stateStore = new ServerAdminStateStore(temp.path);
  stateStore.updateSettings({
    displayName: "Test Server",
    publicListingEnabled: true,
  });

  const originalFetch = globalThis.fetch;
  globalThis.fetch = async () => new Response(
    JSON.stringify({
      submissionId: "sub-123",
      submissionClaimSecret: "claim-123",
    }),
    {
      status: 200,
      headers: { "content-type": "application/json" },
    },
  );

  try {
    const syncService = createServerRegistrySyncService({
      env: {
        registryBaseUrl: "http://registry.example.com",
        timeoutMs: 1000,
        publicBaseUrl: "http://public.example.com:8787",
        publicWsUrl: "ws://public.example.com:8787/control",
        bandwidthProbeUrl: "http://public.example.com:8787/registry/bandwidth-probe.bin",
      },
      stateStore,
      getRoomCount: () => 0,
      getGuardSnapshot: () => createGuardSnapshot(),
    });

    await syncService.runNow();

    const settings = stateStore.getSettingsView();
    assert.equal(settings.lastSyncStatus, "submission_created");
    assert.equal(settings.submissionId, "sub-123");
  } finally {
    globalThis.fetch = originalFetch;
    rmSync(temp.directory, { recursive: true, force: true });
  }
});
