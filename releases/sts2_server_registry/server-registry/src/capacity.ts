export type RegistryCreateRoomGuardStatus = "allow" | "block" | "unknown";
export type RegistryCapacitySource = "manual" | "probe_peak_7d" | "unknown";

export interface RegistryResolvedCapacityState {
  bandwidthCapacityMbps?: number | undefined;
  probePeak7dCapacityMbps?: number | undefined;
  resolvedCapacityMbps?: number | undefined;
  capacitySource: RegistryCapacitySource;
}

export function resolveCapacityState(
  bandwidthCapacityMbps?: number | null,
  probePeak7dCapacityMbps?: number | null,
): RegistryResolvedCapacityState {
  const normalizedManual = roundNullablePositive(bandwidthCapacityMbps);
  const normalizedProbePeak = roundNullablePositive(probePeak7dCapacityMbps);

  if (normalizedManual != null) {
    return {
      bandwidthCapacityMbps: normalizedManual,
      probePeak7dCapacityMbps: normalizedProbePeak,
      resolvedCapacityMbps: normalizedManual,
      capacitySource: "manual",
    };
  }

  if (normalizedProbePeak != null) {
    return {
      bandwidthCapacityMbps: undefined,
      probePeak7dCapacityMbps: normalizedProbePeak,
      resolvedCapacityMbps: normalizedProbePeak,
      capacitySource: "probe_peak_7d",
    };
  }

  return {
    bandwidthCapacityMbps: undefined,
    probePeak7dCapacityMbps: undefined,
    resolvedCapacityMbps: undefined,
    capacitySource: "unknown",
  };
}

export function normalizeCreateRoomGuardStatus(value: unknown): RegistryCreateRoomGuardStatus {
  return value === "block" || value === "unknown" ? value : "allow";
}

function roundNullablePositive(value: number | null | undefined) {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) {
    return undefined;
  }

  return Math.round(value * 100) / 100;
}
