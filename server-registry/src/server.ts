import express, { type NextFunction, type Request, type Response } from "express";
import { createServer } from "node:http";
import { Pool } from "pg";
import { renderAdminPage } from "./admin-ui.js";
import { computeQualityGrade, probeRegistryServer, runLightProbe, type RegistryProbeTarget } from "./probe.js";
import { deriveServerToken, signSession, verifyPassword, verifySession } from "./security.js";
import { RegistryStore, RegistryStoreError, type RegistryServerUpdateInput, type RegistrySubmissionInput } from "./store.js";

const env = {
  host: process.env.HOST ?? "0.0.0.0",
  port: Number.parseInt(process.env.PORT ?? "18787", 10),
  databaseUrl: requiredEnv(process.env.DATABASE_URL, "DATABASE_URL"),
  publicBaseUrl: process.env.PUBLIC_BASE_URL ?? buildPublicBaseUrl(),
  adminUsername: process.env.ADMIN_USERNAME ?? "admin",
  adminPasswordHash: optionalEnv(process.env.ADMIN_PASSWORD_HASH),
  adminSessionSecret: optionalEnv(process.env.ADMIN_SESSION_SECRET),
  serverTokenSecret: optionalEnv(process.env.SERVER_TOKEN_SECRET),
  adminSessionTtlMs: Number.parseInt(process.env.ADMIN_SESSION_TTL_HOURS ?? "168", 10) * 60 * 60 * 1000,
  lightProbeIntervalMs: Number.parseInt(process.env.LIGHT_PROBE_INTERVAL_SECONDS ?? "180", 10) * 1000,
  bandwidthProbeIntervalMs: Number.parseInt(process.env.BANDWIDTH_PROBE_INTERVAL_SECONDS ?? "1800", 10) * 1000,
  probeTimeoutMs: Number.parseInt(process.env.PROBE_TIMEOUT_MS ?? "5000", 10),
  bandwidthSampleBytes: Number.parseInt(process.env.BANDWIDTH_SAMPLE_BYTES ?? String(8 * 1024 * 1024), 10),
  publicHeartbeatStaleMs: Number.parseInt(process.env.PUBLIC_HEARTBEAT_STALE_SECONDS ?? "600", 10) * 1000,
  publicProbeStaleMs: Number.parseInt(process.env.PUBLIC_PROBE_STALE_SECONDS ?? "600", 10) * 1000,
};

const adminSessionSecret = env.adminSessionSecret;
const serverTokenSecret = env.serverTokenSecret;

if (!env.adminPasswordHash || !adminSessionSecret || !serverTokenSecret) {
  throw new Error("ADMIN_PASSWORD_HASH, ADMIN_SESSION_SECRET, and SERVER_TOKEN_SECRET must be configured.");
}

const pool = new Pool({
  connectionString: env.databaseUrl,
});
const store = new RegistryStore({
  pool,
  sessionTtlMs: env.adminSessionTtlMs,
});

const app = express();
app.use(express.json({ limit: "64kb" }));
app.use((req, res, next) => {
  const startedAt = Date.now();
  res.on("finish", () => {
    const durationMs = Date.now() - startedAt;
    console.log(`[registry] ${req.method} ${req.originalUrl} ip=${requestIp(req)} status=${res.statusCode} durationMs=${durationMs}`);
  });
  next();
});

app.get("/health", async (_req, res, next) => {
  try {
    const servers = await store.listAdminServers();
    const submissions = await store.listSubmissions();
    res.json({
      ok: true,
      servers: servers.length,
      pendingSubmissions: submissions.filter((entry) => entry.status === "pending").length,
    });
  } catch (error) {
    next(error);
  }
});

app.get("/servers/", async (_req, res, next) => {
  try {
    const now = new Date();
    const servers = await store.listPublicServers(
      new Date(now.getTime() - env.publicHeartbeatStaleMs),
      new Date(now.getTime() - env.publicProbeStaleMs),
    );
    res.json({
      ok: true,
      generatedAt: now.toISOString(),
      servers,
    });
  } catch (error) {
    next(error);
  }
});

app.post("/api/submissions", async (req, res, next) => {
  try {
    const body = req.body as Partial<RegistrySubmissionInput> | undefined;
    const created = await store.createSubmission({
      displayName: requiredString(body?.displayName, "displayName"),
      baseUrl: requiredString(body?.baseUrl, "baseUrl"),
      wsUrl: optionalString(body?.wsUrl),
      bandwidthProbeUrl: optionalString(body?.bandwidthProbeUrl),
    }, requestIp(req));
    res.status(201).json({
      ok: true,
      status: "pending",
      submissionId: created.submission.id,
      submissionClaimSecret: created.claimSecret,
      submittedAt: created.submission.submittedAt,
    });
  } catch (error) {
    next(error);
  }
});

app.post("/api/submissions/:id/claim", async (req, res, next) => {
  try {
    const body = req.body as { claimSecret?: string } | undefined;
    const claimed = await store.claimSubmission(
      req.params.id,
      requiredString(body?.claimSecret, "claimSecret"),
      serverTokenSecret,
    );
    res.json({
      ok: true,
      status: claimed.submission.status,
      reviewNote: claimed.submission.reviewNote,
      serverId: claimed.serverId,
      serverToken: claimed.serverToken,
    });
  } catch (error) {
    next(error);
  }
});

app.post("/api/servers/heartbeat", async (req, res, next) => {
  try {
    const body = req.body as Record<string, unknown> | undefined;
    const server = await store.recordHeartbeat(
      requiredString(body?.serverId, "serverId"),
      requiredString(body?.serverToken, "serverToken"),
      {
        displayName: requiredString(body?.displayName, "displayName"),
        publicListingEnabled: Boolean(body?.publicListingEnabled),
        roomCount: positiveInt(body?.roomCount, "roomCount", 0, 999_999),
        baseUrl: requiredString(body?.baseUrl, "baseUrl"),
        wsUrl: optionalString(body?.wsUrl),
        bandwidthProbeUrl: optionalString(body?.bandwidthProbeUrl),
        bandwidthCapacityMbps: optionalNumber(body?.bandwidthCapacityMbps, "bandwidthCapacityMbps"),
        currentBandwidthMbps: optionalNonNegativeNumber(body?.currentBandwidthMbps, "currentBandwidthMbps"),
        bandwidthUtilizationRatio: optionalNonNegativeNumber(body?.bandwidthUtilizationRatio, "bandwidthUtilizationRatio"),
        createRoomGuardStatus: optionalString(body?.createRoomGuardStatus) as "allow" | "block" | "unknown" | undefined,
        capacitySource: optionalString(body?.capacitySource) as "manual" | "probe_peak_7d" | "unknown" | undefined,
      },
      requestIp(req),
    );
    res.json({
      ok: true,
      serverId: server.id,
      runtimeState: server.runtimeState,
      qualityGrade: server.qualityGrade,
      publicListingEnabled: server.publicListingEnabled,
      probePeak7dCapacityMbps: server.probePeak7dCapacityMbps,
      resolvedCapacityMbps: server.resolvedCapacityMbps,
      capacitySource: server.capacitySource,
    });
  } catch (error) {
    next(error);
  }
});

app.get("/admin", (_req, res) => {
  res.type("html").send(renderAdminPage());
});

app.post("/admin/login", async (req, res, next) => {
  try {
    const body = req.body as { username?: string; password?: string } | undefined;
    const username = requiredString(body?.username, "username");
    const password = requiredString(body?.password, "password");
    if (username !== env.adminUsername || !verifyPassword(password, env.adminPasswordHash)) {
      throw new RegistryStoreError(401, "invalid_admin_credentials", "用户名或密码不正确。");
    }

    const session = await store.createAdminSession(username);
    setAdminCookie(res, session.id);
    res.json(session);
  } catch (error) {
    next(error);
  }
});

app.post("/admin/logout", async (req, res, next) => {
  try {
    const session = await requireAdminSession(req);
    await store.deleteAdminSession(session.id);
    clearAdminCookie(res);
    res.status(204).send();
  } catch (error) {
    next(error);
  }
});

app.get("/admin/session", async (req, res, next) => {
  try {
    const session = await requireAdminSession(req);
    res.json(session);
  } catch (error) {
    next(error);
  }
});

app.get("/admin/submissions", async (req, res, next) => {
  try {
    await requireAdminSession(req);
    res.json(await store.listSubmissions());
  } catch (error) {
    next(error);
  }
});

app.post("/admin/submissions/:id/approve", async (req, res, next) => {
  try {
    const session = await requireAdminSession(req);
    const body = req.body as { note?: string } | undefined;
    const server = await store.approveSubmission(req.params.id, session.username, optionalString(body?.note), serverTokenSecret);
    await runManualServerProbe(server.id, true);
    res.json(await store.getServerById(server.id));
  } catch (error) {
    next(error);
  }
});

app.post("/admin/submissions/:id/reject", async (req, res, next) => {
  try {
    const session = await requireAdminSession(req);
    const body = req.body as { note?: string } | undefined;
    res.json(await store.rejectSubmission(req.params.id, session.username, optionalString(body?.note)));
  } catch (error) {
    next(error);
  }
});

app.get("/admin/servers", async (req, res, next) => {
  try {
    await requireAdminSession(req);
    res.json(await store.listAdminServers());
  } catch (error) {
    next(error);
  }
});

app.patch("/admin/servers/:id", async (req, res, next) => {
  try {
    const session = await requireAdminSession(req);
    const body = req.body as RegistryServerUpdateInput | undefined;
    res.json(await store.updateServer(req.params.id, {
      displayName: optionalString(body?.displayName),
      baseUrl: optionalString(body?.baseUrl),
      wsUrl: optionalString(body?.wsUrl),
      bandwidthProbeUrl: optionalString(body?.bandwidthProbeUrl),
      listingState: parseListingState(body?.listingState),
      runtimeState: parseRuntimeState(body?.runtimeState),
      sortOrder: typeof body?.sortOrder === "number" ? body.sortOrder : undefined,
    }, session.username));
  } catch (error) {
    next(error);
  }
});

app.post("/admin/servers/:id/probe", async (req, res, next) => {
  try {
    await requireAdminSession(req);
    res.json(await runManualServerProbe(req.params.id, true));
  } catch (error) {
    next(error);
  }
});

app.use((error: unknown, _req: Request, res: Response, _next: NextFunction) => {
  if (error instanceof RegistryStoreError) {
    res.status(error.statusCode).json({
      code: error.code,
      message: error.message,
    });
    return;
  }

  if (error instanceof InputError) {
    res.status(400).json({
      code: "invalid_request",
      message: error.message,
    });
    return;
  }

  console.error("[registry] unhandled error", error);
  res.status(500).json({
    code: "internal_error",
    message: "服务器内部错误。",
  });
});

const server = createServer(app);

async function bootstrap() {
  await store.init();

  const sessionCleanupInterval = setInterval(() => {
    void store.cleanupExpiredSessions();
  }, 60_000);
  const lightProbeInterval = setInterval(() => {
    void runScheduledProbeSweep(false);
  }, env.lightProbeIntervalMs);
  const bandwidthProbeInterval = setInterval(() => {
    void runScheduledProbeSweep(true);
  }, env.bandwidthProbeIntervalMs);

  server.listen(env.port, env.host, () => {
    console.log(`[registry] listening on ${env.publicBaseUrl}`);
    console.log(`[registry] admin console ready at ${env.publicBaseUrl}/admin`);
    void runScheduledProbeSweep(false);
  });

  const shutdown = async () => {
    clearInterval(sessionCleanupInterval);
    clearInterval(lightProbeInterval);
    clearInterval(bandwidthProbeInterval);
    await pool.end();
    process.exit(0);
  };

  process.on("SIGINT", () => void shutdown());
  process.on("SIGTERM", () => void shutdown());
}

async function runScheduledProbeSweep(includeBandwidth: boolean) {
  const targets = await store.listProbeTargets();
  for (const target of targets) {
    try {
      const result = includeBandwidth
        ? await probeRegistryServer(target, {
            timeoutMs: env.probeTimeoutMs,
            bandwidthSampleBytes: env.bandwidthSampleBytes,
          })
        : await runLightProbe(target, {
            timeoutMs: env.probeTimeoutMs,
          });
      await store.recordProbeResult(target.id, result, includeBandwidth ? "bandwidth" : "light");
      console.log(`[registry] probe serverId=${target.id} state=${result.runtimeState} quality=${result.qualityGrade} rttMs=${result.lastProbeRttMs ?? "<none>"} bandwidthMbps=${result.lastBandwidthMbps ?? "<none>"}`);
    } catch (error) {
      console.warn(`[registry] probe failed serverId=${target.id} error=${error instanceof Error ? error.message : "unknown"}`);
    }
  }
}

async function runManualServerProbe(serverId: string, includeBandwidth: boolean) {
  const target = await store.getServerById(serverId);
  if (!target) {
    throw new RegistryStoreError(404, "server_not_found", "目标服务器不存在。");
  }

  const probeTarget: RegistryProbeTarget = {
    id: target.id,
    displayName: target.displayName,
    baseUrl: target.baseUrl,
    bandwidthProbeUrl: target.bandwidthProbeUrl,
    runtimeState: target.runtimeState,
  };
  const result = includeBandwidth
    ? await probeRegistryServer(probeTarget, {
        timeoutMs: env.probeTimeoutMs,
        bandwidthSampleBytes: env.bandwidthSampleBytes,
      })
    : await runLightProbe(probeTarget, {
        timeoutMs: env.probeTimeoutMs,
      });
  return await store.recordProbeResult(serverId, {
    ...result,
    qualityGrade: includeBandwidth
      ? result.qualityGrade
      : computeQualityGrade(result.runtimeState, result.lastProbeRttMs, target.lastBandwidthMbps),
    lastBandwidthMbps: includeBandwidth ? result.lastBandwidthMbps : target.lastBandwidthMbps,
  }, "manual");
}

async function requireAdminSession(req: Request) {
  const cookieToken = parseCookies(req.headers.cookie)["sts2_registry_admin_session"];
  const sessionId = verifySession(cookieToken, adminSessionSecret!);
  if (!sessionId) {
    throw new RegistryStoreError(401, "admin_auth_required", "请先登录后台。");
  }

  const session = await store.getAdminSession(sessionId);
  if (!session) {
    throw new RegistryStoreError(401, "admin_auth_required", "请先登录后台。");
  }

  return session;
}

function setAdminCookie(res: Response, sessionId: string) {
  const token = signSession(sessionId, adminSessionSecret!);
  res.setHeader("Set-Cookie", `sts2_registry_admin_session=${token}; Path=/; HttpOnly; SameSite=Lax; Max-Age=${Math.floor(env.adminSessionTtlMs / 1000)}`);
}

function clearAdminCookie(res: Response) {
  res.setHeader("Set-Cookie", "sts2_registry_admin_session=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0");
}

function parseCookies(header: string | undefined) {
  const cookies: Record<string, string> = {};
  if (!header) {
    return cookies;
  }

  for (const segment of header.split(";")) {
    const separatorIndex = segment.indexOf("=");
    if (separatorIndex <= 0) {
      continue;
    }

    const key = segment.slice(0, separatorIndex).trim();
    const value = segment.slice(separatorIndex + 1).trim();
    if (key) {
      cookies[key] = value;
    }
  }

  return cookies;
}

function buildPublicBaseUrl() {
  return `http://127.0.0.1:${process.env.PORT ?? "18787"}`;
}

function requestIp(req: Request) {
  const forwarded = req.headers["x-forwarded-for"];
  if (typeof forwarded === "string" && forwarded.trim()) {
    return forwarded.split(",")[0]!.trim();
  }

  return req.socket.remoteAddress ?? "";
}

function requiredEnv(value: string | undefined, name: string) {
  const normalized = value?.trim();
  if (!normalized) {
    throw new Error(`${name} must be configured.`);
  }

  return normalized;
}

function optionalEnv(value: string | undefined) {
  const normalized = value?.trim();
  return normalized ? normalized : undefined;
}

function requiredString(value: unknown, name: string) {
  if (typeof value !== "string" || value.trim() === "") {
    throw new InputError(`${name} 不能为空。`);
  }

  return value.trim();
}

function optionalString(value: unknown) {
  if (typeof value !== "string") {
    return undefined;
  }

  const normalized = value.trim();
  return normalized ? normalized : undefined;
}

function positiveInt(value: unknown, name: string, min: number, max: number) {
  if (typeof value !== "number" || !Number.isInteger(value) || value < min || value > max) {
    throw new InputError(`${name} 必须是 ${min}-${max} 之间的整数。`);
  }

  return value;
}

function optionalNumber(value: unknown, name: string) {
  if (value == null) {
    return undefined;
  }

  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) {
    throw new InputError(`${name} 必须是正数。`);
  }

  return value;
}

function optionalNonNegativeNumber(value: unknown, name: string) {
  if (value == null) {
    return undefined;
  }

  if (typeof value !== "number" || !Number.isFinite(value) || value < 0) {
    throw new InputError(`${name} 必须是非负数。`);
  }

  return value;
}

function parseListingState(value: unknown) {
  return value === "disabled" ? "disabled" : value === "approved" ? "approved" : undefined;
}

function parseRuntimeState(value: unknown) {
  if (value === "online" || value === "degraded" || value === "offline" || value === "maintenance") {
    return value;
  }

  return undefined;
}

class InputError extends Error {}

bootstrap().catch((error) => {
  console.error("[registry] failed to bootstrap", error);
  process.exit(1);
});
