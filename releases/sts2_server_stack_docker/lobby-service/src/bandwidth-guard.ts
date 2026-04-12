export type CreateRoomGuardStatus = "allow" | "block" | "unknown";
export type CapacitySource = "manual" | "probe_peak_7d" | "unknown";

export interface CreateRoomGuardInput {
  currentBandwidthMbps?: number | null;
  bandwidthCapacityMbps?: number | null;
  probePeak7dCapacityMbps?: number | null;
  createRoomGuardApplies: boolean;
}

export interface CreateRoomGuardSnapshot {
  createRoomGuardApplies: boolean;
  createRoomGuardStatus: CreateRoomGuardStatus;
  currentBandwidthMbps: number;
  bandwidthCapacityMbps?: number | undefined;
  probePeak7dCapacityMbps?: number | undefined;
  resolvedCapacityMbps?: number | undefined;
  bandwidthUtilizationRatio?: number | undefined;
  capacitySource: CapacitySource;
  createRoomThresholdRatio: number;
  createRoomReleaseThresholdRatio: number;
}

export interface CreateRoomBandwidthGuardConfig {
  createRoomThresholdRatio?: number;
  createRoomReleaseThresholdRatio?: number;
}

export class CreateRoomBandwidthGuard {
  private readonly createRoomThresholdRatio: number;
  private readonly createRoomReleaseThresholdRatio: number;
  private blocked = false;

  constructor(config: CreateRoomBandwidthGuardConfig = {}) {
    this.createRoomThresholdRatio = normalizeThreshold(config.createRoomThresholdRatio, 0.9);
    this.createRoomReleaseThresholdRatio = normalizeThreshold(config.createRoomReleaseThresholdRatio, 0.85);
  }

  getSnapshot(input: CreateRoomGuardInput): CreateRoomGuardSnapshot {
    const currentBandwidthMbps = roundNullablePositive(input.currentBandwidthMbps) ?? 0;
    const bandwidthCapacityMbps = roundNullablePositive(input.bandwidthCapacityMbps);
    const probePeak7dCapacityMbps = roundNullablePositive(input.probePeak7dCapacityMbps);
    const resolvedCapacityMbps = bandwidthCapacityMbps ?? probePeak7dCapacityMbps;
    const capacitySource: CapacitySource = bandwidthCapacityMbps != null
      ? "manual"
      : probePeak7dCapacityMbps != null
        ? "probe_peak_7d"
        : "unknown";

    if (!input.createRoomGuardApplies) {
      this.blocked = false;
      return {
        createRoomGuardApplies: false,
        createRoomGuardStatus: "allow",
        currentBandwidthMbps,
        bandwidthCapacityMbps,
        probePeak7dCapacityMbps,
        resolvedCapacityMbps,
        bandwidthUtilizationRatio: resolvedCapacityMbps == null ? undefined : roundRatio(currentBandwidthMbps / resolvedCapacityMbps),
        capacitySource,
        createRoomThresholdRatio: this.createRoomThresholdRatio,
        createRoomReleaseThresholdRatio: this.createRoomReleaseThresholdRatio,
      };
    }

    if (resolvedCapacityMbps == null) {
      this.blocked = false;
      return {
        createRoomGuardApplies: true,
        createRoomGuardStatus: "unknown",
        currentBandwidthMbps,
        bandwidthCapacityMbps,
        probePeak7dCapacityMbps,
        resolvedCapacityMbps: undefined,
        bandwidthUtilizationRatio: undefined,
        capacitySource,
        createRoomThresholdRatio: this.createRoomThresholdRatio,
        createRoomReleaseThresholdRatio: this.createRoomReleaseThresholdRatio,
      };
    }

    const bandwidthUtilizationRatio = roundRatio(currentBandwidthMbps / resolvedCapacityMbps);
    if (this.blocked) {
      if (bandwidthUtilizationRatio < this.createRoomReleaseThresholdRatio) {
        this.blocked = false;
      }
    } else if (bandwidthUtilizationRatio >= this.createRoomThresholdRatio) {
      this.blocked = true;
    }

    return {
      createRoomGuardApplies: true,
      createRoomGuardStatus: this.blocked ? "block" : "allow",
      currentBandwidthMbps,
      bandwidthCapacityMbps,
      probePeak7dCapacityMbps,
      resolvedCapacityMbps,
      bandwidthUtilizationRatio,
      capacitySource,
      createRoomThresholdRatio: this.createRoomThresholdRatio,
      createRoomReleaseThresholdRatio: this.createRoomReleaseThresholdRatio,
    };
  }
}

function normalizeThreshold(value: number | undefined, fallback: number) {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0 || value > 1) {
    return fallback;
  }

  return value;
}

function roundNullablePositive(value: number | null | undefined) {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) {
    return undefined;
  }

  return Math.round(value * 100) / 100;
}

function roundRatio(value: number) {
  return Math.round(value * 10_000) / 10_000;
}
