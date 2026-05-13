// lobby-service/src/peer/handlers/metrics.ts
//
// Read-only live snapshot used by the in-game server picker. Replaces the
// legacy aggregated `/servers/` directory call to the central mother registry
// with a per-node fan-out: each lobby exposes its own current state, and the
// client merges them in the picker UI.
import type { Express, Request, Response } from "express";
import type { NodeIdentity } from "../identity.js";
import type { MetricsResponse } from "../types.js";

export interface MetricsSnapshot {
  rooms: number;
  currentBandwidthMbps: number;
  bandwidthCapacityMbps: number | null | undefined;
  resolvedCapacityMbps: number | null | undefined;
  bandwidthUtilizationRatio: number | undefined;
  capacitySource: string;
  createRoomGuardApplies: boolean;
  createRoomGuardStatus: "allow" | "block" | "unknown";
}

interface Deps {
  identity: NodeIdentity;
  address: string;
  getDisplayName?: () => string;
  getPublicListing: () => boolean;
  getSnapshot: () => MetricsSnapshot;
}

export function mountMetrics(app: Express, deps: Deps): void {
  app.get("/peers/metrics", (_req: Request, res: Response) => {
    const snapshot = deps.getSnapshot();
    const body: MetricsResponse = {
      address: deps.address,
      publicKey: deps.identity.publicKey,
      serverTime: new Date().toISOString(),
      publicListing: deps.getPublicListing(),
      rooms: snapshot.rooms,
      currentBandwidthMbps: snapshot.currentBandwidthMbps,
      bandwidthCapacityMbps: snapshot.bandwidthCapacityMbps ?? null,
      resolvedCapacityMbps: snapshot.resolvedCapacityMbps ?? null,
      capacitySource: snapshot.capacitySource,
      createRoomGuardApplies: snapshot.createRoomGuardApplies,
      createRoomGuardStatus: snapshot.createRoomGuardStatus,
    };
    if (typeof snapshot.bandwidthUtilizationRatio === "number") {
      body.bandwidthUtilizationRatio = snapshot.bandwidthUtilizationRatio;
    }
    const displayName = deps.getDisplayName?.().trim();
    if (displayName) {
      body.displayName = displayName;
    }
    res.status(200).json(body);
  });
}
