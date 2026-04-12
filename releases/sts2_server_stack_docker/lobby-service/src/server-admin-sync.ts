import { randomUUID } from "node:crypto";
import type { CreateRoomGuardSnapshot } from "./bandwidth-guard.js";
import type { ServerAdminStateStore } from "./server-admin-state.js";

export interface ServerRegistrySyncEnv {
  registryBaseUrl: string;
  timeoutMs: number;
  publicBaseUrl: string;
  publicWsUrl: string;
  bandwidthProbeUrl: string;
}

export interface ServerRegistrySyncService {
  runNow(): Promise<void>;
}

export interface ServerRegistrySyncOptions {
  env: ServerRegistrySyncEnv;
  stateStore: ServerAdminStateStore;
  getRoomCount: () => number;
  getGuardSnapshot: () => CreateRoomGuardSnapshot;
}

export function createServerRegistrySyncService(options: ServerRegistrySyncOptions): ServerRegistrySyncService {
  let inFlight = false;

  return {
    async runNow() {
      if (inFlight) {
        return;
      }

      inFlight = true;
      try {
        await runSyncCycle(options);
      } finally {
        inFlight = false;
      }
    },
  };
}

async function runSyncCycle(options: ServerRegistrySyncOptions) {
  const { env, stateStore, getRoomCount, getGuardSnapshot } = options;
  const current = stateStore.getState();
  const now = new Date().toISOString();
  const guardSnapshot = getGuardSnapshot();

  if (!env.registryBaseUrl) {
    stateStore.patch({
      lastSyncAt: now,
      lastSyncStatus: "registry_disabled",
      lastSyncError: "SERVER_REGISTRY_BASE_URL 未配置。",
    });
    return;
  }

  try {
    if (current.publicListingEnabled) {
      try {
        assertPublicRegistryEndpointsReachable(env);
      } catch (error) {
        stateStore.patch({
          lastSyncAt: now,
          lastSyncStatus: "public_endpoint_invalid",
          lastSyncError: error instanceof Error ? error.message : "invalid_public_endpoint",
        });
        return;
      }
    }

    if (!current.publicListingEnabled) {
      if (current.serverId && current.serverToken) {
        let response: {
          probePeak7dCapacityMbps?: number;
          resolvedCapacityMbps?: number;
          capacitySource?: string;
        };
        try {
          response = await sendHeartbeat(options, {
            displayName: resolveDisplayName(current.displayName, env.publicBaseUrl),
            publicListingEnabled: false,
            roomCount: getRoomCount(),
            baseUrl: env.publicBaseUrl,
            wsUrl: env.publicWsUrl,
            bandwidthProbeUrl: env.bandwidthProbeUrl,
            bandwidthCapacityMbps: current.bandwidthCapacityMbps,
            currentBandwidthMbps: guardSnapshot.currentBandwidthMbps,
            bandwidthUtilizationRatio: guardSnapshot.bandwidthUtilizationRatio,
            createRoomGuardStatus: guardSnapshot.createRoomGuardStatus,
            capacitySource: guardSnapshot.capacitySource,
          });
        } catch (error) {
          stateStore.patch({
            lastSyncAt: now,
            lastSyncStatus: "listing_disable_failed",
            lastSyncError: error instanceof Error ? error.message : "listing_disable_failed",
          });
          return;
        }
        stateStore.patch({
          lastSyncAt: now,
          lastSyncStatus: "listing_disabled",
          lastSyncError: "",
          probePeak7dCapacityMbps: response.probePeak7dCapacityMbps ?? null,
          resolvedCapacityMbps: response.resolvedCapacityMbps ?? null,
          capacitySource: response.capacitySource ?? "unknown",
        });
      }
      else
      {
        stateStore.patch({
          lastSyncAt: now,
          lastSyncStatus: "idle",
          lastSyncError: "",
        });
      }
      return;
    }

    if (!current.serverId || !current.serverToken) {
      if (!current.submissionId || !current.submissionClaimSecret) {
        let created: { submissionId: string; submissionClaimSecret: string };
        try {
          created = await createSubmission(options, {
            displayName: resolveDisplayName(current.displayName, env.publicBaseUrl),
            baseUrl: env.publicBaseUrl,
            wsUrl: env.publicWsUrl,
            bandwidthProbeUrl: env.bandwidthProbeUrl,
          });
        } catch (error) {
          stateStore.patch({
            lastSyncAt: now,
            lastSyncStatus: "submission_failed",
            lastSyncError: error instanceof Error ? error.message : "submission_failed",
          });
          return;
        }
        stateStore.patch({
          submissionId: created.submissionId,
          submissionClaimSecret: created.submissionClaimSecret,
          lastSyncAt: now,
          lastSyncStatus: "submission_created",
          lastSyncError: "",
          lastReviewNote: "",
        });
        return;
      }

      let claimed: { status: string; reviewNote?: string; serverId?: string; serverToken?: string };
      try {
        claimed = await claimSubmission(options, current.submissionId, current.submissionClaimSecret);
      } catch (error) {
        stateStore.patch({
          lastSyncAt: now,
          lastSyncStatus: "claim_failed",
          lastSyncError: error instanceof Error ? error.message : "claim_failed",
        });
        return;
      }

      if (claimed.status === "approved" && claimed.serverId && claimed.serverToken) {
        stateStore.patch({
          serverId: claimed.serverId,
          serverToken: claimed.serverToken,
          lastSyncAt: now,
          lastSyncStatus: "approved",
          lastSyncError: "",
          lastReviewNote: claimed.reviewNote ?? "",
        });

        try {
          const response = await sendHeartbeat(options, {
            displayName: resolveDisplayName(current.displayName, env.publicBaseUrl),
            publicListingEnabled: true,
            roomCount: getRoomCount(),
            baseUrl: env.publicBaseUrl,
            wsUrl: env.publicWsUrl,
            bandwidthProbeUrl: env.bandwidthProbeUrl,
            bandwidthCapacityMbps: current.bandwidthCapacityMbps,
            currentBandwidthMbps: guardSnapshot.currentBandwidthMbps,
            bandwidthUtilizationRatio: guardSnapshot.bandwidthUtilizationRatio,
            createRoomGuardStatus: guardSnapshot.createRoomGuardStatus,
            capacitySource: guardSnapshot.capacitySource,
          });
          stateStore.patch({
            lastSyncAt: now,
            lastSyncStatus: "heartbeat_ok",
            lastSyncError: "",
            probePeak7dCapacityMbps: response.probePeak7dCapacityMbps ?? null,
            resolvedCapacityMbps: response.resolvedCapacityMbps ?? null,
            capacitySource: response.capacitySource ?? "unknown",
          });
        } catch (error) {
          stateStore.patch({
            lastSyncAt: now,
            lastSyncStatus: "heartbeat_failed",
            lastSyncError: error instanceof Error ? error.message : "heartbeat_failed",
          });
        }
      } else if (claimed.status === "rejected") {
        stateStore.patch({
          lastSyncAt: now,
          lastSyncStatus: "rejected",
          lastSyncError: "",
          lastReviewNote: claimed.reviewNote ?? "申请未通过。",
        });
      } else {
        stateStore.patch({
          lastSyncAt: now,
          lastSyncStatus: "pending_review",
          lastSyncError: "",
        });
      }
      return;
    }

    let response: {
      resolvedCapacityMbps?: number;
      probePeak7dCapacityMbps?: number;
      capacitySource?: string;
    };
    try {
      response = await sendHeartbeat(options, {
        displayName: resolveDisplayName(current.displayName, env.publicBaseUrl),
        publicListingEnabled: true,
        roomCount: getRoomCount(),
        baseUrl: env.publicBaseUrl,
        wsUrl: env.publicWsUrl,
        bandwidthProbeUrl: env.bandwidthProbeUrl,
        bandwidthCapacityMbps: current.bandwidthCapacityMbps,
        currentBandwidthMbps: guardSnapshot.currentBandwidthMbps,
        bandwidthUtilizationRatio: guardSnapshot.bandwidthUtilizationRatio,
        createRoomGuardStatus: guardSnapshot.createRoomGuardStatus,
        capacitySource: guardSnapshot.capacitySource,
      });
    } catch (error) {
      stateStore.patch({
        lastSyncAt: now,
        lastSyncStatus: "heartbeat_failed",
        lastSyncError: error instanceof Error ? error.message : "heartbeat_failed",
      });
      return;
    }
    stateStore.patch({
      lastSyncAt: now,
      lastSyncStatus: "heartbeat_ok",
      lastSyncError: "",
      probePeak7dCapacityMbps: response.probePeak7dCapacityMbps ?? null,
      resolvedCapacityMbps: response.resolvedCapacityMbps ?? null,
      capacitySource: response.capacitySource ?? "unknown",
    });
  } catch (error) {
    stateStore.patch({
      lastSyncAt: now,
      lastSyncStatus: "sync_failed",
      lastSyncError: error instanceof Error ? error.message : "unknown_sync_error",
    });
  }
}

function assertPublicRegistryEndpointsReachable(env: ServerRegistrySyncEnv) {
  assertReachablePublicUrl("SERVER_REGISTRY_PUBLIC_BASE_URL", env.publicBaseUrl, ["http:", "https:"]);
  assertReachablePublicUrl("SERVER_REGISTRY_PUBLIC_WS_URL", env.publicWsUrl, ["ws:", "wss:"]);
  assertReachablePublicUrl("SERVER_REGISTRY_BANDWIDTH_PROBE_URL", env.bandwidthProbeUrl, ["http:", "https:"]);
}

function assertReachablePublicUrl(name: string, rawUrl: string, allowedProtocols: string[]) {
  let parsed: URL;
  try {
    parsed = new URL(rawUrl);
  } catch {
    throw new Error(`${name} 不是合法 URL：${rawUrl}`);
  }

  if (!allowedProtocols.includes(parsed.protocol)) {
    throw new Error(`${name} 必须使用 ${allowedProtocols.join(" / ")}：${rawUrl}`);
  }

  const hostname = parsed.hostname.trim().toLowerCase();
  if (hostname === "" || hostname === "localhost" || hostname === "127.0.0.1" || hostname === "0.0.0.0" || hostname === "::1") {
    throw new Error(
      `${name} 当前指向 ${parsed.hostname || "<empty>"}，母面板无法从公网访问这台子服。请改成公网 IP 或域名，或配置 RELAY_PUBLIC_HOST / SERVER_REGISTRY_PUBLIC_*。`,
    );
  }
}

async function createSubmission(options: ServerRegistrySyncOptions, payload: {
  displayName: string;
  baseUrl: string;
  wsUrl: string;
  bandwidthProbeUrl: string;
}) {
  return await sendJsonRequest<{ submissionId: string; submissionClaimSecret: string }>(
    options.env.registryBaseUrl,
    "api/submissions",
    {
      method: "POST",
      body: payload,
    },
    options.env.timeoutMs,
  );
}

async function claimSubmission(options: ServerRegistrySyncOptions, submissionId: string, claimSecret: string) {
  return await sendJsonRequest<{ status: string; reviewNote?: string; serverId?: string; serverToken?: string }>(
    options.env.registryBaseUrl,
    `api/submissions/${encodeURIComponent(submissionId)}/claim`,
    {
      method: "POST",
      body: { claimSecret },
    },
    options.env.timeoutMs,
  );
}

async function sendHeartbeat(options: ServerRegistrySyncOptions, payload: {
  displayName: string;
  publicListingEnabled: boolean;
  roomCount: number;
  baseUrl: string;
  wsUrl: string;
  bandwidthProbeUrl: string;
  bandwidthCapacityMbps: number | null;
  currentBandwidthMbps: number;
  bandwidthUtilizationRatio?: number | undefined;
  createRoomGuardStatus: string;
  capacitySource: string;
}) {
  const state = options.stateStore.getState();
  return await sendJsonRequest<{
    resolvedCapacityMbps?: number;
    probePeak7dCapacityMbps?: number;
    capacitySource?: string;
  }>(
    options.env.registryBaseUrl,
    "api/servers/heartbeat",
    {
      method: "POST",
      body: {
        serverId: state.serverId,
        serverToken: state.serverToken,
        ...payload,
      },
    },
    options.env.timeoutMs,
  );
}

async function sendJsonRequest<T>(
  baseUrl: string,
  path: string,
  options: { method: string; body?: unknown },
  timeoutMs: number,
): Promise<T> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const requestInit: RequestInit = {
      method: options.method,
      signal: controller.signal,
      headers: {
        "content-type": "application/json",
        "x-sts2-sync-id": randomUUID(),
      },
      body: options.body == null ? null : JSON.stringify(options.body),
    };
    const response = await fetch(new URL(path, normalizeBaseUrl(baseUrl) + "/"), requestInit);
    const text = await response.text();
    const payload = text ? JSON.parse(text) as Record<string, unknown> : {};
    if (!response.ok) {
      throw new Error(typeof payload.message === "string" ? payload.message : `registry_http_${response.status}`);
    }

    return payload as T;
  } finally {
    clearTimeout(timer);
  }
}

function normalizeBaseUrl(value: string) {
  return value.trim().replace(/\/+$/, "");
}

function resolveDisplayName(displayName: string, publicBaseUrl: string) {
  const normalized = displayName.trim();
  if (normalized) {
    return normalized;
  }

  try {
    const url = new URL(publicBaseUrl);
    return `社区服务器 ${url.host}`;
  } catch {
    return "社区服务器";
  }
}
