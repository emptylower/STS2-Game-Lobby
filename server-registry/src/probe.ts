export type RegistryServerRuntimeState = "online" | "degraded" | "offline" | "maintenance";
export type RegistryServerQualityGrade = "excellent" | "good" | "fair" | "poor" | "unknown";

export interface RegistryProbeTarget {
  id: string;
  displayName: string;
  baseUrl: string;
  bandwidthProbeUrl?: string | undefined;
  runtimeState?: RegistryServerRuntimeState | undefined;
}

export interface RegistryProbeResult {
  runtimeState: RegistryServerRuntimeState;
  qualityGrade: RegistryServerQualityGrade;
  healthOk: boolean;
  probeOk: boolean;
  lastProbeRttMs?: number | undefined;
  lastBandwidthMbps?: number | undefined;
  failureReason?: string | undefined;
}

export interface RegistryProbeOptions {
  fetchImpl?: typeof fetch;
  timeoutMs?: number;
  bandwidthSampleBytes?: number;
}

export async function runLightProbe(
  target: RegistryProbeTarget,
  options: RegistryProbeOptions = {},
): Promise<RegistryProbeResult> {
  return probeRegistryServer(target, { ...options, bandwidthSampleBytes: 0 });
}

export async function probeRegistryServer(
  target: RegistryProbeTarget,
  options: RegistryProbeOptions = {},
): Promise<RegistryProbeResult> {
  const fetchImpl = options.fetchImpl ?? fetch;
  const timeoutMs = Math.max(500, options.timeoutMs ?? 5_000);
  const bandwidthSampleBytes = Math.max(0, options.bandwidthSampleBytes ?? 8 * 1024 * 1024);

  let probeOk = false;
  let healthOk = false;
  let lastProbeRttMs: number | undefined;
  let lastBandwidthMbps: number | undefined;
  let failureReason: string | undefined;

  try {
    lastProbeRttMs = await measureJsonProbe(fetchImpl, buildUrl(target.baseUrl, "probe"), timeoutMs);
    probeOk = true;
  } catch (error) {
    failureReason = describeProbeError(error, "probe");
  }

  try {
    const health = await readHealth(fetchImpl, buildUrl(target.baseUrl, "health"), timeoutMs);
    healthOk = health.ok === true;
  } catch (error) {
    if (!failureReason) {
      failureReason = describeProbeError(error, "health");
    }
  }

  if (target.bandwidthProbeUrl && bandwidthSampleBytes > 0) {
    try {
      lastBandwidthMbps = await measureBandwidthMbps(fetchImpl, target.bandwidthProbeUrl, timeoutMs, bandwidthSampleBytes);
    } catch (error) {
      if (!failureReason) {
        failureReason = describeProbeError(error, "bandwidth");
      }
    }
  }

  const runtimeState =
    target.runtimeState === "maintenance"
      ? "maintenance"
      : !probeOk
        ? "offline"
        : healthOk
          ? "online"
          : "degraded";

  return {
    runtimeState,
    qualityGrade: computeQualityGrade(runtimeState, lastProbeRttMs, lastBandwidthMbps),
    healthOk,
    probeOk,
    lastProbeRttMs,
    lastBandwidthMbps,
    failureReason,
  };
}

export function computeQualityGrade(
  runtimeState: RegistryServerRuntimeState,
  rttMs?: number,
  bandwidthMbps?: number,
): RegistryServerQualityGrade {
  if (runtimeState === "maintenance") {
    return "unknown";
  }

  if (runtimeState === "offline") {
    return "poor";
  }

  if (typeof bandwidthMbps === "number") {
    if (bandwidthMbps >= 50) {
      return "excellent";
    }

    if (bandwidthMbps >= 20) {
      return "good";
    }

    if (bandwidthMbps >= 8) {
      return "fair";
    }

    return "poor";
  }

  if (typeof rttMs !== "number") {
    return runtimeState === "online" ? "good" : "fair";
  }

  if (rttMs <= 120) {
    return "good";
  }

  if (rttMs <= 260) {
    return "fair";
  }

  return "poor";
}

async function measureJsonProbe(fetchImpl: typeof fetch, url: string, timeoutMs: number) {
  const startedAt = performance.now();
  const response = await fetchWithTimeout(fetchImpl, url, timeoutMs);
  if (!response.ok) {
    throw new Error(`probe_http_${response.status}`);
  }

  await response.arrayBuffer();
  return performance.now() - startedAt;
}

async function readHealth(fetchImpl: typeof fetch, url: string, timeoutMs: number) {
  const response = await fetchWithTimeout(fetchImpl, url, timeoutMs);
  if (!response.ok) {
    return { ok: false };
  }

  return await response.json() as { ok?: boolean };
}

async function measureBandwidthMbps(fetchImpl: typeof fetch, url: string, timeoutMs: number, sampleBytes: number) {
  const response = await fetchWithTimeout(fetchImpl, url, timeoutMs);
  if (!response.ok || !response.body) {
    throw new Error(`bandwidth_http_${response.status}`);
  }

  const startedAt = performance.now();
  const reader = response.body.getReader();
  let consumedBytes = 0;

  try {
    while (consumedBytes < sampleBytes) {
      const { done, value } = await reader.read();
      if (done) {
        break;
      }

      consumedBytes += value.byteLength;
    }
  } finally {
    try {
      await reader.cancel();
    } catch {
      // Ignore partial reader cancellation failures.
    }
  }

  const elapsedSeconds = Math.max((performance.now() - startedAt) / 1000, 0.001);
  return (consumedBytes * 8) / elapsedSeconds / 1_000_000;
}

async function fetchWithTimeout(fetchImpl: typeof fetch, url: string, timeoutMs: number) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetchImpl(url, {
      signal: controller.signal,
      redirect: "follow",
      headers: {
        "user-agent": "sts2-server-registry-probe/0.1",
      },
    });
  } finally {
    clearTimeout(timer);
  }
}

function buildUrl(baseUrl: string, path: string) {
  const normalizedBaseUrl = baseUrl.endsWith("/") ? baseUrl : `${baseUrl}/`;
  return new URL(path, normalizedBaseUrl).toString();
}

function describeProbeError(error: unknown, phase: string) {
  if (error instanceof Error) {
    if (error.name === "AbortError") {
      return `${phase}_timeout`;
    }

    return `${phase}_${error.message}`;
  }

  return `${phase}_unknown_error`;
}
