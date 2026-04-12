import { randomUUID } from "node:crypto";
import { Pool, type PoolClient } from "pg";
import { normalizeCreateRoomGuardStatus, resolveCapacityState, type RegistryCapacitySource, type RegistryCreateRoomGuardStatus } from "./capacity.js";
import type { RegistryProbeResult, RegistryServerQualityGrade, RegistryServerRuntimeState } from "./probe.js";
import { deriveServerToken, hashOpaqueToken, randomOpaqueToken } from "./security.js";

export type RegistryServerListingState = "approved" | "disabled";
export type RegistrySubmissionStatus = "pending" | "approved" | "rejected";

export interface RegistrySubmissionInput {
  displayName: string;
  baseUrl: string;
  wsUrl?: string | undefined;
  bandwidthProbeUrl?: string | undefined;
}

export interface RegistrySubmissionRecord {
  id: string;
  displayName: string;
  baseUrl: string;
  wsUrl: string;
  bandwidthProbeUrl?: string | undefined;
  status: RegistrySubmissionStatus;
  sourceIp: string;
  submittedAt: string;
  reviewedAt?: string | undefined;
  reviewedBy?: string | undefined;
  reviewNote?: string | undefined;
  linkedServerId?: string | undefined;
}

export interface RegistryServerRecord {
  id: string;
  displayName: string;
  baseUrl: string;
  wsUrl: string;
  bandwidthProbeUrl?: string | undefined;
  bandwidthCapacityMbps?: number | undefined;
  probePeak7dCapacityMbps?: number | undefined;
  resolvedCapacityMbps?: number | undefined;
  currentBandwidthMbps?: number | undefined;
  bandwidthUtilizationRatio?: number | undefined;
  createRoomGuardStatus: RegistryCreateRoomGuardStatus;
  capacitySource: RegistryCapacitySource;
  listingState: RegistryServerListingState;
  runtimeState: RegistryServerRuntimeState;
  qualityGrade: RegistryServerQualityGrade;
  publicListingEnabled: boolean;
  roomCount: number;
  lastHeartbeatAt?: string | undefined;
  lastProbeAt?: string | undefined;
  lastProbeRttMs?: number | undefined;
  lastBandwidthMbps?: number | undefined;
  failureReason?: string | undefined;
  healthOk?: boolean | undefined;
  createdAt: string;
  updatedAt: string;
  approvedAt?: string | undefined;
  approvedBy?: string | undefined;
  sortOrder: number;
}

export interface PublicRegistryServerRecord {
  serverId: string;
  serverName: string;
  baseUrl: string;
  rooms: number;
  lastVerifiedAt: string;
  currentBandwidthMbps?: number | undefined;
  bandwidthCapacityMbps?: number | undefined;
  resolvedCapacityMbps?: number | undefined;
  bandwidthUtilizationRatio?: number | undefined;
  createRoomGuardStatus: RegistryCreateRoomGuardStatus;
  capacitySource: RegistryCapacitySource;
}

export interface RegistryHeartbeatInput {
  displayName: string;
  publicListingEnabled: boolean;
  roomCount: number;
  baseUrl: string;
  wsUrl?: string | undefined;
  bandwidthProbeUrl?: string | undefined;
  bandwidthCapacityMbps?: number | undefined;
  currentBandwidthMbps?: number | undefined;
  bandwidthUtilizationRatio?: number | undefined;
  createRoomGuardStatus?: RegistryCreateRoomGuardStatus | undefined;
  capacitySource?: RegistryCapacitySource | undefined;
}

export interface RegistryServerUpdateInput {
  displayName?: string | undefined;
  baseUrl?: string | undefined;
  wsUrl?: string | undefined;
  bandwidthProbeUrl?: string | undefined;
  listingState?: RegistryServerListingState | undefined;
  runtimeState?: RegistryServerRuntimeState | undefined;
  sortOrder?: number | undefined;
}

export interface RegistryProbeTarget {
  id: string;
  displayName: string;
  baseUrl: string;
  bandwidthProbeUrl?: string | undefined;
  runtimeState?: RegistryServerRuntimeState | undefined;
}

export interface RegistryAdminSession {
  id: string;
  username: string;
  createdAt: string;
  expiresAt: string;
}

export class RegistryStoreError extends Error {
  constructor(
    readonly statusCode: number,
    readonly code: string,
    message: string,
  ) {
    super(message);
  }
}

export interface RegistryStoreConfig {
  pool: Pool;
  sessionTtlMs: number;
}

const SERVER_SELECT_COLUMNS = `
  s.id, s.display_name, s.base_url, s.ws_url, s.bandwidth_probe_url,
  s.bandwidth_capacity_mbps, s.current_bandwidth_mbps, s.bandwidth_utilization_ratio, s.create_room_guard_status,
  s.listing_state, s.runtime_state, s.quality_grade, s.public_listing_enabled,
  s.room_count, s.last_heartbeat_at, s.last_probe_at, s.last_probe_rtt_ms,
  s.last_bandwidth_mbps, s.failure_reason, s.health_ok, s.created_at, s.updated_at,
  s.approved_at, s.approved_by, s.sort_order,
  probe_capacity.probe_peak_7d_mbps
`;

const SERVER_SELECT_FROM = `
  FROM servers s
  LEFT JOIN LATERAL (
    SELECT MAX(spr.bandwidth_mbps) AS probe_peak_7d_mbps
      FROM server_probe_results spr
     WHERE spr.server_id = s.id
       AND spr.probe_ok = TRUE
       AND spr.bandwidth_mbps IS NOT NULL
       AND spr.created_at >= NOW() - INTERVAL '7 days'
  ) probe_capacity ON TRUE
`;

export class RegistryStore {
  private readonly pool: Pool;
  private readonly sessionTtlMs: number;

  constructor(config: RegistryStoreConfig) {
    this.pool = config.pool;
    this.sessionTtlMs = config.sessionTtlMs;
  }

  async init() {
    await this.pool.query(`
      CREATE TABLE IF NOT EXISTS server_submissions (
        id TEXT PRIMARY KEY,
        display_name TEXT NOT NULL,
        base_url TEXT NOT NULL,
        ws_url TEXT NOT NULL,
        bandwidth_probe_url TEXT,
        status TEXT NOT NULL,
        source_ip TEXT NOT NULL,
        submitted_at TIMESTAMPTZ NOT NULL,
        reviewed_at TIMESTAMPTZ,
        reviewed_by TEXT,
        review_note TEXT,
        claim_secret_hash TEXT NOT NULL,
        linked_server_id TEXT,
        last_claimed_at TIMESTAMPTZ
      );

      CREATE TABLE IF NOT EXISTS servers (
        id TEXT PRIMARY KEY,
        display_name TEXT NOT NULL,
        base_url TEXT NOT NULL UNIQUE,
        ws_url TEXT NOT NULL,
        bandwidth_probe_url TEXT,
        bandwidth_capacity_mbps DOUBLE PRECISION,
        current_bandwidth_mbps DOUBLE PRECISION,
        bandwidth_utilization_ratio DOUBLE PRECISION,
        create_room_guard_status TEXT NOT NULL DEFAULT 'allow',
        listing_state TEXT NOT NULL,
        runtime_state TEXT NOT NULL,
        quality_grade TEXT NOT NULL,
        public_listing_enabled BOOLEAN NOT NULL DEFAULT FALSE,
        room_count INTEGER NOT NULL DEFAULT 0,
        last_heartbeat_at TIMESTAMPTZ,
        last_probe_at TIMESTAMPTZ,
        last_probe_rtt_ms DOUBLE PRECISION,
        last_bandwidth_mbps DOUBLE PRECISION,
        failure_reason TEXT,
        health_ok BOOLEAN,
        created_at TIMESTAMPTZ NOT NULL,
        updated_at TIMESTAMPTZ NOT NULL,
        approved_at TIMESTAMPTZ,
        approved_by TEXT,
        sort_order INTEGER NOT NULL DEFAULT 1000
      );

      CREATE TABLE IF NOT EXISTS server_tokens (
        server_id TEXT PRIMARY KEY REFERENCES servers(id) ON DELETE CASCADE,
        token_hash TEXT NOT NULL,
        created_at TIMESTAMPTZ NOT NULL,
        last_used_at TIMESTAMPTZ,
        disabled_at TIMESTAMPTZ
      );

      CREATE TABLE IF NOT EXISTS server_heartbeats (
        server_id TEXT PRIMARY KEY REFERENCES servers(id) ON DELETE CASCADE,
        public_listing_enabled BOOLEAN NOT NULL,
        display_name TEXT NOT NULL,
        room_count INTEGER NOT NULL,
        payload JSONB NOT NULL,
        source_ip TEXT NOT NULL,
        received_at TIMESTAMPTZ NOT NULL
      );

      CREATE TABLE IF NOT EXISTS server_probe_results (
        id BIGSERIAL PRIMARY KEY,
        server_id TEXT NOT NULL REFERENCES servers(id) ON DELETE CASCADE,
        probe_type TEXT NOT NULL,
        runtime_state TEXT NOT NULL,
        quality_grade TEXT NOT NULL,
        probe_ok BOOLEAN NOT NULL,
        health_ok BOOLEAN NOT NULL,
        rtt_ms DOUBLE PRECISION,
        bandwidth_mbps DOUBLE PRECISION,
        failure_reason TEXT,
        created_at TIMESTAMPTZ NOT NULL
      );

      CREATE TABLE IF NOT EXISTS admin_sessions (
        id TEXT PRIMARY KEY,
        username TEXT NOT NULL,
        created_at TIMESTAMPTZ NOT NULL,
        expires_at TIMESTAMPTZ NOT NULL
      );

      CREATE INDEX IF NOT EXISTS idx_server_submissions_status ON server_submissions(status, submitted_at DESC);
      CREATE INDEX IF NOT EXISTS idx_servers_listing_runtime ON servers(listing_state, runtime_state, public_listing_enabled);
      CREATE INDEX IF NOT EXISTS idx_server_probe_results_server_created ON server_probe_results(server_id, created_at DESC);
      CREATE INDEX IF NOT EXISTS idx_admin_sessions_expires ON admin_sessions(expires_at);

      ALTER TABLE servers ADD COLUMN IF NOT EXISTS bandwidth_capacity_mbps DOUBLE PRECISION;
      ALTER TABLE servers ADD COLUMN IF NOT EXISTS current_bandwidth_mbps DOUBLE PRECISION;
      ALTER TABLE servers ADD COLUMN IF NOT EXISTS bandwidth_utilization_ratio DOUBLE PRECISION;
      ALTER TABLE servers ADD COLUMN IF NOT EXISTS create_room_guard_status TEXT NOT NULL DEFAULT 'allow';
    `);
  }

  async listPublicServers(heartbeatCutoff: Date, probeCutoff: Date) {
    const result = await this.pool.query(
      `SELECT ${SERVER_SELECT_COLUMNS}
         ${SERVER_SELECT_FROM}
        WHERE s.listing_state = 'approved'
          AND s.public_listing_enabled = TRUE
          AND s.runtime_state IN ('online', 'degraded')
          AND s.last_heartbeat_at IS NOT NULL
          AND s.last_heartbeat_at >= $1
          AND s.last_probe_at IS NOT NULL
          AND s.last_probe_at >= $2
        ORDER BY s.sort_order ASC, s.display_name ASC`,
      [heartbeatCutoff.toISOString(), probeCutoff.toISOString()],
    );

    return result.rows.map(mapPublicServerRow);
  }

  async listAdminServers() {
    const result = await this.pool.query(
      `SELECT ${SERVER_SELECT_COLUMNS}
         ${SERVER_SELECT_FROM}
        ORDER BY s.sort_order ASC, s.display_name ASC`,
    );

    return result.rows.map(mapServerRow);
  }

  async listSubmissions() {
    const result = await this.pool.query(
      `SELECT id, display_name, base_url, ws_url, bandwidth_probe_url, status, source_ip,
              submitted_at, reviewed_at, reviewed_by, review_note, linked_server_id
         FROM server_submissions
        ORDER BY submitted_at DESC`,
    );

    return result.rows.map(mapSubmissionRow);
  }

  async listProbeTargets() {
    const result = await this.pool.query(
      `SELECT id, display_name, base_url, bandwidth_probe_url, runtime_state
         FROM servers
        WHERE listing_state = 'approved'
        ORDER BY sort_order ASC, display_name ASC`,
    );

    return result.rows.map((row) => ({
      id: String(row.id),
      displayName: String(row.display_name),
      baseUrl: String(row.base_url),
      bandwidthProbeUrl: nullableText(row.bandwidth_probe_url),
      runtimeState: normalizeRuntimeState(row.runtime_state),
    })) satisfies RegistryProbeTarget[];
  }

  async getServerById(serverId: string) {
    const result = await this.pool.query(
      `SELECT ${SERVER_SELECT_COLUMNS}
         ${SERVER_SELECT_FROM}
        WHERE s.id = $1`,
      [serverId],
    );
    if (result.rowCount !== 1) {
      return null;
    }

    return mapServerRow(result.rows[0]);
  }

  async createSubmission(input: RegistrySubmissionInput, sourceIp: string, now = new Date()) {
    const normalized = normalizeSubmissionInput(input);
    const claimSecret = randomOpaqueToken(24);
    const id = `submission_${randomUUID()}`;

    await this.withTransaction(async (client) => {
      await ensureSubmissionBaseUrlAvailable(client, normalized.baseUrl);
      await client.query(
        `INSERT INTO server_submissions (
            id, display_name, base_url, ws_url, bandwidth_probe_url, status, source_ip,
            submitted_at, claim_secret_hash
          ) VALUES ($1, $2, $3, $4, $5, 'pending', $6, $7, $8)`,
        [
          id,
          normalized.displayName,
          normalized.baseUrl,
          normalized.wsUrl,
          normalized.bandwidthProbeUrl ?? null,
          normalizeSourceIp(sourceIp),
          now.toISOString(),
          hashOpaqueToken(claimSecret),
        ],
      );
    });

    return {
      submission: {
        id,
        displayName: normalized.displayName,
        baseUrl: normalized.baseUrl,
        wsUrl: normalized.wsUrl,
        bandwidthProbeUrl: normalized.bandwidthProbeUrl,
        status: "pending",
        sourceIp: normalizeSourceIp(sourceIp),
        submittedAt: now.toISOString(),
      } satisfies RegistrySubmissionRecord,
      claimSecret,
    };
  }

  async claimSubmission(submissionId: string, claimSecret: string, tokenSecret: string, now = new Date()) {
    const result = await this.pool.query(
      `SELECT id, display_name, base_url, ws_url, bandwidth_probe_url, status, source_ip,
              submitted_at, reviewed_at, reviewed_by, review_note, linked_server_id, claim_secret_hash
         FROM server_submissions
        WHERE id = $1`,
      [submissionId],
    );

    if (result.rowCount !== 1) {
      throw new RegistryStoreError(404, "submission_not_found", "目标提交不存在。");
    }

    const row = result.rows[0];
    if (hashOpaqueToken(claimSecret) !== String(row.claim_secret_hash)) {
      throw new RegistryStoreError(403, "invalid_claim_secret", "claim 凭证无效。");
    }

    const submission = mapSubmissionRow(row);
    if (submission.status === "approved" && submission.linkedServerId) {
      await this.pool.query(
        "UPDATE server_submissions SET last_claimed_at = $2 WHERE id = $1",
        [submissionId, now.toISOString()],
      );
      return {
        submission,
        serverId: submission.linkedServerId,
        serverToken: deriveServerToken(submission.linkedServerId, tokenSecret),
      };
    }

    return { submission };
  }

  async approveSubmission(submissionId: string, reviewer: string, note: string | undefined, tokenSecret: string, now = new Date()) {
    return await this.withTransaction(async (client) => {
      const submissionResult = await client.query(
        `SELECT id, display_name, base_url, ws_url, bandwidth_probe_url, status, source_ip,
                submitted_at, reviewed_at, reviewed_by, review_note, linked_server_id
           FROM server_submissions
          WHERE id = $1
          FOR UPDATE`,
        [submissionId],
      );

      if (submissionResult.rowCount !== 1) {
        throw new RegistryStoreError(404, "submission_not_found", "目标提交不存在。");
      }

      const submission = mapSubmissionRow(submissionResult.rows[0]);
      if (submission.status !== "pending") {
        throw new RegistryStoreError(409, "submission_already_reviewed", "该申请已经处理过了。");
      }

      await ensureSubmissionBaseUrlAvailable(client, submission.baseUrl, undefined, submission.id);

      const serverId = `server_${randomUUID()}`;
      const serverToken = deriveServerToken(serverId, tokenSecret);
      const createdAt = now.toISOString();
      const reviewNote = normalizeOptionalText(note, 512);

      await client.query(
        `INSERT INTO servers (
            id, display_name, base_url, ws_url, bandwidth_probe_url,
            bandwidth_capacity_mbps, current_bandwidth_mbps, bandwidth_utilization_ratio, create_room_guard_status,
            listing_state, runtime_state, quality_grade, public_listing_enabled,
            room_count, created_at, updated_at, approved_at, approved_by, sort_order
          ) VALUES ($1, $2, $3, $4, $5, NULL, NULL, NULL, 'unknown', 'approved', 'offline', 'unknown', FALSE, 0, $6, $6, $6, $7, 1000)`,
        [
          serverId,
          submission.displayName,
          submission.baseUrl,
          submission.wsUrl,
          submission.bandwidthProbeUrl ?? null,
          createdAt,
          reviewer,
        ],
      );

      await client.query(
        `INSERT INTO server_tokens (server_id, token_hash, created_at)
         VALUES ($1, $2, $3)`,
        [serverId, hashOpaqueToken(serverToken), createdAt],
      );

      await client.query(
        `UPDATE server_submissions
            SET status = 'approved',
                reviewed_at = $2,
                reviewed_by = $3,
                review_note = $4,
                linked_server_id = $5
          WHERE id = $1`,
        [submissionId, createdAt, reviewer, reviewNote ?? null, serverId],
      );

      return await this.readServerForClient(client, serverId);
    });
  }

  async rejectSubmission(submissionId: string, reviewer: string, note: string | undefined, now = new Date()) {
    const reviewNote = normalizeOptionalText(note, 512) ?? "未通过审核。";
    const result = await this.pool.query(
      `UPDATE server_submissions
          SET status = 'rejected',
              reviewed_at = $2,
              reviewed_by = $3,
              review_note = $4
        WHERE id = $1
          AND status = 'pending'
      RETURNING id, display_name, base_url, ws_url, bandwidth_probe_url, status, source_ip,
                submitted_at, reviewed_at, reviewed_by, review_note, linked_server_id`,
      [submissionId, now.toISOString(), reviewer, reviewNote],
    );

    if (result.rowCount !== 1) {
      throw new RegistryStoreError(409, "submission_already_reviewed", "该申请已经处理过了。");
    }

    return mapSubmissionRow(result.rows[0]);
  }

  async updateServer(serverId: string, patch: RegistryServerUpdateInput, actor: string | undefined, now = new Date()) {
    const current = await this.getServerById(serverId);
    if (!current) {
      throw new RegistryStoreError(404, "server_not_found", "目标服务器不存在。");
    }

    const nextBaseUrl = patch.baseUrl != null ? normalizeHttpUrl(patch.baseUrl, "baseUrl") : current.baseUrl;
    const nextWsUrl = patch.wsUrl != null ? normalizeWsUrl(patch.wsUrl, nextBaseUrl) : current.wsUrl;

    await this.withTransaction(async (client) => {
      if (nextBaseUrl !== current.baseUrl) {
        const duplicate = await client.query(
          "SELECT 1 FROM servers WHERE base_url = $1 AND id <> $2 LIMIT 1",
          [nextBaseUrl, serverId],
        );
        if ((duplicate.rowCount ?? 0) > 0) {
          throw new RegistryStoreError(409, "server_already_listed", "该地址已存在于服务器列表。");
        }
      }

      await client.query(
        `UPDATE servers
            SET display_name = $2,
                base_url = $3,
                ws_url = $4,
                bandwidth_probe_url = $5,
                listing_state = $6,
                runtime_state = $7,
                sort_order = $8,
                updated_at = $9,
                approved_by = COALESCE(approved_by, $10)
          WHERE id = $1`,
        [
          serverId,
          patch.displayName != null ? requiredText(patch.displayName, "displayName", 64) : current.displayName,
          nextBaseUrl,
          nextWsUrl,
          patch.bandwidthProbeUrl != null ? normalizeOptionalHttpUrl(patch.bandwidthProbeUrl) ?? null : current.bandwidthProbeUrl ?? null,
          patch.listingState ?? current.listingState,
          patch.runtimeState ?? current.runtimeState,
          patch.sortOrder ?? current.sortOrder,
          now.toISOString(),
          actor ?? current.approvedBy ?? null,
        ],
      );
    });

    return await this.requireServer(serverId);
  }

  async recordHeartbeat(serverId: string, token: string, input: RegistryHeartbeatInput, sourceIp: string, now = new Date()) {
    const normalized = normalizeHeartbeatInput(input);
    const tokenHash = hashOpaqueToken(token);
    await this.withTransaction(async (client) => {
      const tokenResult = await client.query(
        `SELECT token_hash, disabled_at
           FROM server_tokens
          WHERE server_id = $1
          FOR UPDATE`,
        [serverId],
      );
      if (tokenResult.rowCount !== 1) {
        throw new RegistryStoreError(401, "invalid_server_token", "子服务器令牌无效。");
      }

      const row = tokenResult.rows[0];
      if (row.disabled_at != null || String(row.token_hash) !== tokenHash) {
        throw new RegistryStoreError(401, "invalid_server_token", "子服务器令牌无效。");
      }

      const timestamp = now.toISOString();
      const payload = {
        displayName: normalized.displayName,
        publicListingEnabled: normalized.publicListingEnabled,
        roomCount: normalized.roomCount,
        baseUrl: normalized.baseUrl,
        wsUrl: normalized.wsUrl,
        bandwidthProbeUrl: normalized.bandwidthProbeUrl,
        bandwidthCapacityMbps: normalized.bandwidthCapacityMbps,
        currentBandwidthMbps: normalized.currentBandwidthMbps,
        bandwidthUtilizationRatio: normalized.bandwidthUtilizationRatio,
        createRoomGuardStatus: normalized.createRoomGuardStatus,
        capacitySource: normalized.capacitySource,
      };

      await client.query(
        `UPDATE server_tokens
            SET last_used_at = $2
          WHERE server_id = $1`,
        [serverId, timestamp],
      );

      const updateServerResult = await client.query(
        `UPDATE servers
            SET display_name = $2,
                base_url = $3,
                ws_url = $4,
                bandwidth_probe_url = $5,
                bandwidth_capacity_mbps = $6,
                current_bandwidth_mbps = $7,
                bandwidth_utilization_ratio = $8,
                create_room_guard_status = $9,
                public_listing_enabled = $10,
                room_count = $11,
                last_heartbeat_at = $12,
                updated_at = $12
          WHERE id = $1
        RETURNING id`,
        [
          serverId,
          normalized.displayName,
          normalized.baseUrl,
          normalized.wsUrl,
          normalized.bandwidthProbeUrl ?? null,
          normalized.bandwidthCapacityMbps ?? null,
          normalized.currentBandwidthMbps ?? null,
          normalized.bandwidthUtilizationRatio ?? null,
          normalized.createRoomGuardStatus,
          normalized.publicListingEnabled,
          normalized.roomCount,
          timestamp,
        ],
      );
      if (updateServerResult.rowCount !== 1) {
        throw new RegistryStoreError(404, "server_not_found", "目标服务器不存在。");
      }

      await client.query(
        `INSERT INTO server_heartbeats (
            server_id, public_listing_enabled, display_name, room_count, payload, source_ip, received_at
          ) VALUES ($1, $2, $3, $4, $5::jsonb, $6, $7)
          ON CONFLICT (server_id) DO UPDATE
              SET public_listing_enabled = EXCLUDED.public_listing_enabled,
                  display_name = EXCLUDED.display_name,
                  room_count = EXCLUDED.room_count,
                  payload = EXCLUDED.payload,
                  source_ip = EXCLUDED.source_ip,
                  received_at = EXCLUDED.received_at`,
        [
          serverId,
          normalized.publicListingEnabled,
          normalized.displayName,
          normalized.roomCount,
          JSON.stringify(payload),
          normalizeSourceIp(sourceIp),
          timestamp,
        ],
      );
    });

    return await this.requireServer(serverId);
  }

  async recordProbeResult(
    serverId: string,
    result: RegistryProbeResult,
    probeType: "light" | "bandwidth" | "manual",
    now = new Date(),
  ) {
    return await this.withTransaction(async (client) => {
      const current = await this.readServerForClient(client, serverId);
      const timestamp = now.toISOString();
      const nextBandwidthMbps = result.lastBandwidthMbps ?? current.lastBandwidthMbps;
      const nextQuality = current.runtimeState === "maintenance"
        ? "unknown"
        : result.lastBandwidthMbps == null
          ? normalizeQualityGradeForWrite(result.runtimeState, result.lastProbeRttMs, current.lastBandwidthMbps)
          : result.qualityGrade;
      const nextRuntimeState = current.runtimeState === "maintenance"
        ? "maintenance"
        : result.runtimeState;

      await client.query(
        `UPDATE servers
            SET runtime_state = $2,
                quality_grade = $3,
                last_probe_at = $4,
                last_probe_rtt_ms = $5,
                last_bandwidth_mbps = $6,
                failure_reason = $7,
                health_ok = $8,
                updated_at = $4
          WHERE id = $1`,
        [
          serverId,
          nextRuntimeState,
          nextQuality,
          timestamp,
          roundMaybe(result.lastProbeRttMs),
          roundMaybe(nextBandwidthMbps),
          result.failureReason ?? null,
          result.healthOk,
        ],
      );

      await client.query(
        `INSERT INTO server_probe_results (
            server_id, probe_type, runtime_state, quality_grade, probe_ok, health_ok,
            rtt_ms, bandwidth_mbps, failure_reason, created_at
          ) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)`,
        [
          serverId,
          probeType,
          result.runtimeState,
          result.qualityGrade,
          result.probeOk,
          result.healthOk,
          roundMaybe(result.lastProbeRttMs),
          roundMaybe(result.lastBandwidthMbps),
          result.failureReason ?? null,
          timestamp,
        ],
      );

      return await this.readServerForClient(client, serverId);
    });
  }

  async createAdminSession(username: string, now = new Date()) {
    const session: RegistryAdminSession = {
      id: `session_${randomUUID()}`,
      username,
      createdAt: now.toISOString(),
      expiresAt: new Date(now.getTime() + this.sessionTtlMs).toISOString(),
    };

    await this.pool.query(
      `INSERT INTO admin_sessions (id, username, created_at, expires_at)
       VALUES ($1, $2, $3, $4)`,
      [session.id, session.username, session.createdAt, session.expiresAt],
    );

    return session;
  }

  async getAdminSession(sessionId: string, now = new Date()) {
    const result = await this.pool.query(
      `SELECT id, username, created_at, expires_at
         FROM admin_sessions
        WHERE id = $1
          AND expires_at > $2`,
      [sessionId, now.toISOString()],
    );
    if (result.rowCount !== 1) {
      return null;
    }

    return mapAdminSessionRow(result.rows[0]);
  }

  async deleteAdminSession(sessionId: string) {
    await this.pool.query("DELETE FROM admin_sessions WHERE id = $1", [sessionId]);
  }

  async cleanupExpiredSessions(now = new Date()) {
    await this.pool.query("DELETE FROM admin_sessions WHERE expires_at <= $1", [now.toISOString()]);
  }

  private async requireServer(serverId: string) {
    const server = await this.getServerById(serverId);
    if (!server) {
      throw new RegistryStoreError(404, "server_not_found", "目标服务器不存在。");
    }

    return server;
  }

  private async withTransaction<T>(fn: (client: PoolClient) => Promise<T>) {
    const client = await this.pool.connect();
    try {
      await client.query("BEGIN");
      const value = await fn(client);
      await client.query("COMMIT");
      return value;
    } catch (error) {
      await client.query("ROLLBACK");
      throw error;
    } finally {
      client.release();
    }
  }

  private async readServerForClient(client: PoolClient, serverId: string) {
    const result = await client.query(
      `SELECT ${SERVER_SELECT_COLUMNS}
         ${SERVER_SELECT_FROM}
        WHERE s.id = $1`,
      [serverId],
    );

    if (result.rowCount !== 1) {
      throw new RegistryStoreError(404, "server_not_found", "目标服务器不存在。");
    }

    return mapServerRow(result.rows[0]);
  }
}

async function ensureSubmissionBaseUrlAvailable(
  client: PoolClient,
  baseUrl: string,
  excludeServerId?: string,
  excludeSubmissionId?: string,
) {
  const serverConflict = await client.query(
    "SELECT 1 FROM servers WHERE base_url = $1 AND ($2::text IS NULL OR id <> $2) LIMIT 1",
    [baseUrl, excludeServerId ?? null],
  );
  if ((serverConflict.rowCount ?? 0) > 0) {
    throw new RegistryStoreError(409, "server_already_listed", "该地址已存在于公开服务器列表。");
  }

  const submissionConflict = await client.query(
    "SELECT 1 FROM server_submissions WHERE base_url = $1 AND status = 'pending' AND ($2::text IS NULL OR id <> $2) LIMIT 1",
    [baseUrl, excludeSubmissionId ?? null],
  );
  if ((submissionConflict.rowCount ?? 0) > 0) {
    throw new RegistryStoreError(409, "submission_already_pending", "该地址已存在待审核申请。");
  }
}

function mapSubmissionRow(row: Record<string, unknown>): RegistrySubmissionRecord {
  return {
    id: String(row.id),
    displayName: String(row.display_name),
    baseUrl: String(row.base_url),
    wsUrl: String(row.ws_url),
    bandwidthProbeUrl: nullableText(row.bandwidth_probe_url),
    status: normalizeSubmissionStatus(row.status),
    sourceIp: String(row.source_ip),
    submittedAt: new Date(String(row.submitted_at)).toISOString(),
    reviewedAt: nullableDate(row.reviewed_at),
    reviewedBy: nullableText(row.reviewed_by),
    reviewNote: nullableText(row.review_note),
    linkedServerId: nullableText(row.linked_server_id),
  };
}

function mapPublicServerRow(row: Record<string, unknown>): PublicRegistryServerRecord {
  const resolvedCapacity = resolveCapacityState(
    nullableNumber(row.bandwidth_capacity_mbps),
    nullableNumber(row.probe_peak_7d_mbps),
  );
  return {
    serverId: String(row.id),
    serverName: String(row.display_name),
    baseUrl: String(row.base_url),
    rooms: Number(row.room_count) || 0,
    lastVerifiedAt: new Date(String(row.last_probe_at)).toISOString(),
    currentBandwidthMbps: nullableNumber(row.current_bandwidth_mbps),
    bandwidthCapacityMbps: resolvedCapacity.bandwidthCapacityMbps,
    resolvedCapacityMbps: resolvedCapacity.resolvedCapacityMbps,
    bandwidthUtilizationRatio: nullableNumber(row.bandwidth_utilization_ratio),
    createRoomGuardStatus: normalizeCreateRoomGuardStatus(row.create_room_guard_status),
    capacitySource: resolvedCapacity.capacitySource,
  };
}

function mapServerRow(row: Record<string, unknown>): RegistryServerRecord {
  const resolvedCapacity = resolveCapacityState(
    nullableNumber(row.bandwidth_capacity_mbps),
    nullableNumber(row.probe_peak_7d_mbps),
  );
  return {
    id: String(row.id),
    displayName: String(row.display_name),
    baseUrl: String(row.base_url),
    wsUrl: String(row.ws_url),
    bandwidthProbeUrl: nullableText(row.bandwidth_probe_url),
    bandwidthCapacityMbps: resolvedCapacity.bandwidthCapacityMbps,
    probePeak7dCapacityMbps: resolvedCapacity.probePeak7dCapacityMbps,
    resolvedCapacityMbps: resolvedCapacity.resolvedCapacityMbps,
    currentBandwidthMbps: nullableNumber(row.current_bandwidth_mbps),
    bandwidthUtilizationRatio: nullableNumber(row.bandwidth_utilization_ratio),
    createRoomGuardStatus: normalizeCreateRoomGuardStatus(row.create_room_guard_status),
    capacitySource: resolvedCapacity.capacitySource,
    listingState: normalizeListingState(row.listing_state),
    runtimeState: normalizeRuntimeState(row.runtime_state),
    qualityGrade: normalizeQualityGrade(row.quality_grade),
    publicListingEnabled: Boolean(row.public_listing_enabled),
    roomCount: Number(row.room_count) || 0,
    lastHeartbeatAt: nullableDate(row.last_heartbeat_at),
    lastProbeAt: nullableDate(row.last_probe_at),
    lastProbeRttMs: nullableNumber(row.last_probe_rtt_ms),
    lastBandwidthMbps: nullableNumber(row.last_bandwidth_mbps),
    failureReason: nullableText(row.failure_reason),
    healthOk: typeof row.health_ok === "boolean" ? row.health_ok : undefined,
    createdAt: new Date(String(row.created_at)).toISOString(),
    updatedAt: new Date(String(row.updated_at)).toISOString(),
    approvedAt: nullableDate(row.approved_at),
    approvedBy: nullableText(row.approved_by),
    sortOrder: Number(row.sort_order) || 1000,
  };
}

function mapAdminSessionRow(row: Record<string, unknown>): RegistryAdminSession {
  return {
    id: String(row.id),
    username: String(row.username),
    createdAt: new Date(String(row.created_at)).toISOString(),
    expiresAt: new Date(String(row.expires_at)).toISOString(),
  };
}

function normalizeSubmissionInput(input: RegistrySubmissionInput) {
  const baseUrl = normalizeHttpUrl(input.baseUrl, "baseUrl");
  return {
    displayName: requiredText(input.displayName, "displayName", 64),
    baseUrl,
    wsUrl: normalizeWsUrl(input.wsUrl, baseUrl),
    bandwidthProbeUrl: normalizeOptionalHttpUrl(input.bandwidthProbeUrl),
  };
}

function normalizeHeartbeatInput(input: RegistryHeartbeatInput) {
  const baseUrl = normalizeHttpUrl(input.baseUrl, "baseUrl");
  return {
    displayName: requiredText(input.displayName, "displayName", 64),
    publicListingEnabled: Boolean(input.publicListingEnabled),
    roomCount: normalizeNonNegativeInt(input.roomCount, "roomCount"),
    baseUrl,
    wsUrl: normalizeWsUrl(input.wsUrl, baseUrl),
    bandwidthProbeUrl: normalizeOptionalHttpUrl(input.bandwidthProbeUrl),
    bandwidthCapacityMbps: normalizeOptionalPositiveNumber(input.bandwidthCapacityMbps, "bandwidthCapacityMbps"),
    currentBandwidthMbps: normalizeOptionalNonNegativeNumber(input.currentBandwidthMbps, "currentBandwidthMbps"),
    bandwidthUtilizationRatio: normalizeOptionalRatio(input.bandwidthUtilizationRatio, "bandwidthUtilizationRatio"),
    createRoomGuardStatus: normalizeCreateRoomGuardStatus(input.createRoomGuardStatus),
    capacitySource: input.capacitySource === "manual" || input.capacitySource === "probe_peak_7d" ? input.capacitySource : "unknown",
  };
}

function normalizeHttpUrl(value: string, name: string) {
  const trimmed = value.trim();
  if (!trimmed) {
    throw new RegistryStoreError(400, "invalid_request", `${name} 不能为空。`);
  }

  const url = new URL(trimmed);
  if (url.protocol !== "http:" && url.protocol !== "https:") {
    throw new RegistryStoreError(400, "invalid_request", `${name} 必须是 HTTP 或 HTTPS 地址。`);
  }

  url.hash = "";
  return url.toString().replace(/\/$/, "");
}

function normalizeOptionalHttpUrl(value: string | undefined) {
  if (!value || value.trim() === "") {
    return undefined;
  }

  return normalizeHttpUrl(value, "bandwidthProbeUrl");
}

function normalizeWsUrl(value: string | undefined, baseUrl: string) {
  const trimmed = value?.trim();
  if (!trimmed) {
    const base = new URL(baseUrl);
    const protocol = base.protocol === "https:" ? "wss:" : "ws:";
    return `${protocol}//${base.host}/control`;
  }

  const url = new URL(trimmed);
  if (url.protocol !== "ws:" && url.protocol !== "wss:") {
    throw new RegistryStoreError(400, "invalid_request", "wsUrl 必须是 WS 或 WSS 地址。");
  }

  url.hash = "";
  return url.toString().replace(/\/$/, "");
}

function normalizeSourceIp(value: string) {
  return value.trim().slice(0, 128) || "<unknown>";
}

function normalizeNonNegativeInt(value: number, name: string) {
  if (!Number.isInteger(value) || value < 0) {
    throw new RegistryStoreError(400, "invalid_request", `${name} 必须是非负整数。`);
  }

  return value;
}

function normalizeOptionalPositiveNumber(value: number | undefined, name: string) {
  if (value == null) {
    return undefined;
  }

  if (!Number.isFinite(value) || value <= 0) {
    throw new RegistryStoreError(400, "invalid_request", `${name} 必须是正数。`);
  }

  return roundMaybe(value);
}

function normalizeOptionalNonNegativeNumber(value: number | undefined, name: string) {
  if (value == null) {
    return undefined;
  }

  if (!Number.isFinite(value) || value < 0) {
    throw new RegistryStoreError(400, "invalid_request", `${name} 必须是非负数。`);
  }

  return roundMaybe(value);
}

function normalizeOptionalRatio(value: number | undefined, name: string) {
  if (value == null) {
    return undefined;
  }

  if (!Number.isFinite(value) || value < 0) {
    throw new RegistryStoreError(400, "invalid_request", `${name} 必须是非负数。`);
  }

  return Math.round(value * 10_000) / 10_000;
}

function requiredText(value: string, name: string, maxLength: number) {
  const normalized = value.trim();
  if (!normalized) {
    throw new RegistryStoreError(400, "invalid_request", `${name} 不能为空。`);
  }

  if (normalized.length > maxLength) {
    throw new RegistryStoreError(400, "invalid_request", `${name} 过长。`);
  }

  return normalized;
}

function normalizeOptionalText(value: string | undefined, maxLength: number) {
  if (!value) {
    return undefined;
  }

  const normalized = value.trim();
  return normalized ? normalized.slice(0, maxLength) : undefined;
}

function normalizeListingState(value: unknown): RegistryServerListingState {
  return value === "disabled" ? "disabled" : "approved";
}

function normalizeSubmissionStatus(value: unknown): RegistrySubmissionStatus {
  if (value === "approved" || value === "rejected") {
    return value;
  }

  return "pending";
}

function normalizeRuntimeState(value: unknown): RegistryServerRuntimeState {
  if (value === "maintenance" || value === "degraded" || value === "online") {
    return value;
  }

  return "offline";
}

function normalizeQualityGrade(value: unknown): RegistryServerQualityGrade {
  if (value === "excellent" || value === "good" || value === "fair" || value === "poor") {
    return value;
  }

  return "unknown";
}

function normalizeQualityGradeForWrite(
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

function nullableText(value: unknown) {
  return typeof value === "string" && value.length > 0 ? value : undefined;
}

function nullableDate(value: unknown) {
  return value ? new Date(String(value)).toISOString() : undefined;
}

function nullableNumber(value: unknown) {
  return typeof value === "number" && Number.isFinite(value) ? value : undefined;
}

function roundMaybe(value: number | undefined) {
  return typeof value === "number" && Number.isFinite(value) ? Math.round(value * 100) / 100 : undefined;
}
