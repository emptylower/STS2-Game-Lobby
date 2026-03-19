import { mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { randomUUID } from "node:crypto";
import type { RegistryProbeResult, RegistryServerQualityGrade, RegistryServerRuntimeState } from "./registry-probe.js";

export type RegistryServerSourceType = "official" | "community";
export type RegistryServerListingState = "approved" | "disabled";
export type RegistrySubmissionStatus = "pending" | "approved" | "rejected";

export interface RegistryServerEntry {
  id: string;
  sourceType: RegistryServerSourceType;
  displayName: string;
  regionLabel: string;
  baseUrl: string;
  wsUrl: string;
  bandwidthProbeUrl?: string | undefined;
  listingState: RegistryServerListingState;
  runtimeState: RegistryServerRuntimeState;
  qualityGrade: RegistryServerQualityGrade;
  lastProbeAt?: string | undefined;
  lastProbeRttMs?: number | undefined;
  lastBandwidthMbps?: number | undefined;
  failureReason?: string | undefined;
  healthOk?: boolean | undefined;
  operatorName?: string | undefined;
  contact?: string | undefined;
  notes?: string | undefined;
  createdAt: string;
  updatedAt: string;
  approvedAt?: string | undefined;
  approvedBy?: string | undefined;
  sortOrder: number;
}

export interface RegistrySubmission {
  id: string;
  displayName: string;
  regionLabel: string;
  baseUrl: string;
  wsUrl: string;
  bandwidthProbeUrl?: string | undefined;
  operatorName: string;
  contact: string;
  notes: string;
  status: RegistrySubmissionStatus;
  sourceIp: string;
  submittedAt: string;
  reviewedAt?: string | undefined;
  reviewedBy?: string | undefined;
  reviewNote?: string | undefined;
  linkedServerId?: string | undefined;
}

export interface RegistryAuditEntry {
  id: string;
  actor: string;
  action: string;
  targetType: "server" | "submission" | "session";
  targetId: string;
  createdAt: string;
  detail?: string | undefined;
}

export interface RegistryAdminSession {
  id: string;
  username: string;
  createdAt: string;
  expiresAt: string;
}

export interface RegistrySubmissionInput {
  displayName: string;
  regionLabel: string;
  baseUrl: string;
  wsUrl?: string | undefined;
  bandwidthProbeUrl?: string | undefined;
  operatorName: string;
  contact: string;
  notes?: string | undefined;
}

export interface RegistryServerUpdateInput {
  displayName?: string | undefined;
  regionLabel?: string | undefined;
  baseUrl?: string | undefined;
  wsUrl?: string | undefined;
  bandwidthProbeUrl?: string | undefined;
  operatorName?: string | undefined;
  contact?: string | undefined;
  notes?: string | undefined;
  listingState?: RegistryServerListingState | undefined;
  runtimeState?: RegistryServerRuntimeState | undefined;
  sortOrder?: number | undefined;
}

export interface OfficialRegistryServerConfig {
  id: string;
  displayName: string;
  regionLabel: string;
  baseUrl: string;
  wsUrl: string;
  bandwidthProbeUrl?: string | undefined;
}

export interface RegistryStoreConfig {
  dataFilePath: string;
  officialServer: OfficialRegistryServerConfig;
  sessionTtlMs: number;
}

interface RegistryStoreData {
  servers: RegistryServerEntry[];
  submissions: RegistrySubmission[];
  sessions: RegistryAdminSession[];
  auditLog: RegistryAuditEntry[];
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

export class RegistryStore {
  private readonly dataFilePath: string;
  private readonly sessionTtlMs: number;
  private readonly officialServer: OfficialRegistryServerConfig;
  private data: RegistryStoreData;

  constructor(config: RegistryStoreConfig) {
    this.dataFilePath = resolve(config.dataFilePath);
    this.sessionTtlMs = config.sessionTtlMs;
    this.officialServer = normalizeOfficialServer(config.officialServer);
    this.data = loadData(this.dataFilePath);
    this.syncOfficialServer();
  }

  listPublicServers() {
    return clone(this.data.servers
      .filter((server) => server.listingState === "approved")
      .sort(compareServerEntries));
  }

  listAdminServers() {
    return clone([...this.data.servers].sort(compareServerEntries));
  }

  listSubmissions() {
    return clone(
      [...this.data.submissions].sort((left, right) => right.submittedAt.localeCompare(left.submittedAt)),
    );
  }

  createSubmission(input: RegistrySubmissionInput, sourceIp: string, now = new Date()) {
    const normalized = normalizeSubmissionInput(input);
    ensureUniqueBaseUrl(this.data, normalized.baseUrl);
    const submission: RegistrySubmission = {
      id: `submission_${randomUUID()}`,
      displayName: normalized.displayName,
      regionLabel: normalized.regionLabel,
      baseUrl: normalized.baseUrl,
      wsUrl: normalized.wsUrl,
      bandwidthProbeUrl: normalized.bandwidthProbeUrl,
      operatorName: normalized.operatorName,
      contact: normalized.contact,
      notes: normalized.notes,
      status: "pending",
      sourceIp: normalizeText(sourceIp, 128) || "<unknown>",
      submittedAt: now.toISOString(),
    };
    this.data.submissions.unshift(submission);
    this.addAudit("public", "submission_created", "submission", submission.id, submission.baseUrl, now);
    this.save();
    return clone(submission);
  }

  approveSubmission(submissionId: string, reviewer: string, reviewNote?: string, now = new Date()) {
    const submission = this.requireSubmission(submissionId);
    if (submission.status !== "pending") {
      throw new RegistryStoreError(409, "submission_already_reviewed", "该提交通道已完成审核。");
    }

    ensureUniqueBaseUrl(this.data, submission.baseUrl, undefined, submission.id);
    const server: RegistryServerEntry = {
      id: `server_${randomUUID()}`,
      sourceType: "community",
      displayName: submission.displayName,
      regionLabel: submission.regionLabel,
      baseUrl: submission.baseUrl,
      wsUrl: submission.wsUrl,
      bandwidthProbeUrl: submission.bandwidthProbeUrl,
      listingState: "approved",
      runtimeState: "offline",
      qualityGrade: "unknown",
      operatorName: submission.operatorName,
      contact: submission.contact,
      notes: submission.notes,
      createdAt: now.toISOString(),
      updatedAt: now.toISOString(),
      approvedAt: now.toISOString(),
      approvedBy: reviewer,
      sortOrder: 1000 + this.data.servers.filter((serverEntry) => serverEntry.sourceType === "community").length,
    };
    submission.status = "approved";
    submission.reviewedAt = now.toISOString();
    submission.reviewedBy = reviewer;
    submission.reviewNote = normalizeOptionalText(reviewNote, 512);
    submission.linkedServerId = server.id;
    this.data.servers.push(server);
    this.addAudit(reviewer, "submission_approved", "submission", submission.id, server.id, now);
    this.addAudit(reviewer, "server_created_from_submission", "server", server.id, server.baseUrl, now);
    this.save();
    return clone(server);
  }

  rejectSubmission(submissionId: string, reviewer: string, reviewNote?: string, now = new Date()) {
    const submission = this.requireSubmission(submissionId);
    if (submission.status !== "pending") {
      throw new RegistryStoreError(409, "submission_already_reviewed", "该提交通道已完成审核。");
    }

    submission.status = "rejected";
    submission.reviewedAt = now.toISOString();
    submission.reviewedBy = reviewer;
    submission.reviewNote = normalizeOptionalText(reviewNote, 512) ?? "未通过审核。";
    this.addAudit(reviewer, "submission_rejected", "submission", submission.id, submission.reviewNote, now);
    this.save();
    return clone(submission);
  }

  updateServer(serverId: string, patch: RegistryServerUpdateInput, reviewer: string, now = new Date()) {
    const server = this.requireServer(serverId);
    const nextBaseUrl = patch.baseUrl != null ? normalizeHttpUrl(patch.baseUrl, "baseUrl") : server.baseUrl;
    const nextWsUrl = patch.wsUrl != null ? normalizeWsUrl(patch.wsUrl, nextBaseUrl) : server.wsUrl;
    ensureUniqueBaseUrl(this.data, nextBaseUrl, server.id);

    server.displayName = patch.displayName != null ? requiredText(patch.displayName, "displayName", 64) : server.displayName;
    server.regionLabel = patch.regionLabel != null ? requiredText(patch.regionLabel, "regionLabel", 64) : server.regionLabel;
    server.baseUrl = nextBaseUrl;
    server.wsUrl = nextWsUrl;
    server.bandwidthProbeUrl =
      patch.bandwidthProbeUrl != null ? normalizeOptionalHttpUrl(patch.bandwidthProbeUrl) : server.bandwidthProbeUrl;
    server.operatorName =
      patch.operatorName != null ? requiredText(patch.operatorName, "operatorName", 64) : server.operatorName;
    server.contact = patch.contact != null ? requiredText(patch.contact, "contact", 128) : server.contact;
    server.notes = patch.notes != null ? normalizeOptionalText(patch.notes, 512) : server.notes;
    server.listingState = patch.listingState ?? server.listingState;
    server.runtimeState = patch.runtimeState ?? server.runtimeState;
    server.sortOrder = patch.sortOrder ?? server.sortOrder;
    server.updatedAt = now.toISOString();
    this.addAudit(reviewer, "server_updated", "server", server.id, server.baseUrl, now);
    this.save();
    return clone(server);
  }

  recordProbeResult(serverId: string, result: RegistryProbeResult, now = new Date()) {
    const server = this.requireServer(serverId);
    server.lastProbeAt = now.toISOString();
    server.lastProbeRttMs = roundMaybe(result.lastProbeRttMs);
    server.lastBandwidthMbps = roundMaybe(result.lastBandwidthMbps);
    server.healthOk = result.healthOk;
    server.failureReason = result.failureReason;
    server.qualityGrade = result.qualityGrade;
    if (server.runtimeState !== "maintenance") {
      server.runtimeState = result.runtimeState;
    }
    server.updatedAt = now.toISOString();
    this.save();
    return clone(server);
  }

  createSession(username: string, now = new Date()) {
    this.cleanupExpiredSessions(now);
    const session: RegistryAdminSession = {
      id: `session_${randomUUID()}`,
      username,
      createdAt: now.toISOString(),
      expiresAt: new Date(now.getTime() + this.sessionTtlMs).toISOString(),
    };
    this.data.sessions.unshift(session);
    this.addAudit(username, "session_created", "session", session.id, undefined, now);
    this.save();
    return clone(session);
  }

  getSession(sessionId: string, now = new Date()) {
    const didCleanup = this.cleanupExpiredSessions(now);
    const session = this.data.sessions.find((entry) => entry.id === sessionId) ?? null;
    if (didCleanup) {
      this.save();
    }
    return clone(session);
  }

  deleteSession(sessionId: string, actor = "system", now = new Date()) {
    const initialLength = this.data.sessions.length;
    this.data.sessions = this.data.sessions.filter((session) => session.id !== sessionId);
    if (this.data.sessions.length !== initialLength) {
      this.addAudit(actor, "session_deleted", "session", sessionId, undefined, now);
      this.save();
      return true;
    }

    return false;
  }

  cleanupExpiredSessions(now = new Date()) {
    const cutoff = now.toISOString();
    const initialLength = this.data.sessions.length;
    this.data.sessions = this.data.sessions.filter((session) => session.expiresAt > cutoff);
    const didChange = this.data.sessions.length !== initialLength;
    if (didChange) {
      this.save();
    }

    return didChange;
  }

  private requireServer(serverId: string) {
    const server = this.data.servers.find((entry) => entry.id === serverId);
    if (!server) {
      throw new RegistryStoreError(404, "server_not_found", "目标服务器不存在。");
    }

    return server;
  }

  private requireSubmission(submissionId: string) {
    const submission = this.data.submissions.find((entry) => entry.id === submissionId);
    if (!submission) {
      throw new RegistryStoreError(404, "submission_not_found", "目标提交不存在。");
    }

    return submission;
  }

  private syncOfficialServer() {
    const now = new Date().toISOString();
    const existing = this.data.servers.find((entry) => entry.id === this.officialServer.id);
    if (existing) {
      existing.sourceType = "official";
      existing.displayName = this.officialServer.displayName;
      existing.regionLabel = this.officialServer.regionLabel;
      existing.baseUrl = this.officialServer.baseUrl;
      existing.wsUrl = this.officialServer.wsUrl;
      existing.bandwidthProbeUrl = this.officialServer.bandwidthProbeUrl;
      existing.listingState = "approved";
      existing.updatedAt = now;
      existing.sortOrder = -100;
    } else {
      this.data.servers.push({
        id: this.officialServer.id,
        sourceType: "official",
        displayName: this.officialServer.displayName,
        regionLabel: this.officialServer.regionLabel,
        baseUrl: this.officialServer.baseUrl,
        wsUrl: this.officialServer.wsUrl,
        bandwidthProbeUrl: this.officialServer.bandwidthProbeUrl,
        listingState: "approved",
        runtimeState: "offline",
        qualityGrade: "unknown",
        createdAt: now,
        updatedAt: now,
        approvedAt: now,
        approvedBy: "system",
        sortOrder: -100,
      });
    }

    this.save();
  }

  private addAudit(
    actor: string,
    action: string,
    targetType: "server" | "submission" | "session",
    targetId: string,
    detail?: string,
    now = new Date(),
  ) {
    this.data.auditLog.unshift({
      id: `audit_${randomUUID()}`,
      actor,
      action,
      targetType,
      targetId,
      createdAt: now.toISOString(),
      detail: normalizeOptionalText(detail, 512),
    });
    if (this.data.auditLog.length > 256) {
      this.data.auditLog.length = 256;
    }
  }

  private save() {
    const directory = dirname(this.dataFilePath);
    mkdirSync(directory, { recursive: true });
    const tempFilePath = `${this.dataFilePath}.tmp`;
    writeFileSync(tempFilePath, JSON.stringify(this.data, null, 2), "utf8");
    renameSync(tempFilePath, this.dataFilePath);
  }
}

function loadData(dataFilePath: string): RegistryStoreData {
  try {
    const raw = readFileSync(dataFilePath, "utf8");
    const parsed = JSON.parse(raw) as Partial<RegistryStoreData>;
    return {
      servers: Array.isArray(parsed.servers) ? parsed.servers.map(normalizePersistedServer) : [],
      submissions: Array.isArray(parsed.submissions) ? parsed.submissions.map(normalizePersistedSubmission) : [],
      sessions: Array.isArray(parsed.sessions) ? parsed.sessions.map(normalizePersistedSession) : [],
      auditLog: Array.isArray(parsed.auditLog) ? parsed.auditLog.map(normalizePersistedAudit) : [],
    };
  } catch {
    return {
      servers: [],
      submissions: [],
      sessions: [],
      auditLog: [],
    };
  }
}

function normalizeOfficialServer(server: OfficialRegistryServerConfig): OfficialRegistryServerConfig {
  return {
    id: requiredText(server.id, "officialServer.id", 64),
    displayName: requiredText(server.displayName, "officialServer.displayName", 64),
    regionLabel: requiredText(server.regionLabel, "officialServer.regionLabel", 64),
    baseUrl: normalizeHttpUrl(server.baseUrl, "officialServer.baseUrl"),
    wsUrl: normalizeWsUrl(server.wsUrl, server.baseUrl),
    bandwidthProbeUrl: normalizeOptionalHttpUrl(server.bandwidthProbeUrl),
  };
}

function normalizeSubmissionInput(input: RegistrySubmissionInput) {
  const baseUrl = normalizeHttpUrl(input.baseUrl, "baseUrl");
  return {
    displayName: requiredText(input.displayName, "displayName", 64),
    regionLabel: requiredText(input.regionLabel, "regionLabel", 64),
    baseUrl,
    wsUrl: normalizeWsUrl(input.wsUrl, baseUrl),
    bandwidthProbeUrl: normalizeOptionalHttpUrl(input.bandwidthProbeUrl),
    operatorName: requiredText(input.operatorName, "operatorName", 64),
    contact: requiredText(input.contact, "contact", 128),
    notes: normalizeOptionalText(input.notes, 512) ?? "",
  };
}

function ensureUniqueBaseUrl(
  data: RegistryStoreData,
  baseUrl: string,
  excludeServerId?: string,
  excludeSubmissionId?: string,
) {
  if (data.servers.some((server) => server.baseUrl === baseUrl && server.id !== excludeServerId)) {
    throw new RegistryStoreError(409, "server_already_listed", "该地址已在服务器目录中。");
  }

  if (data.submissions.some((submission) =>
    submission.baseUrl === baseUrl
    && submission.status === "pending"
    && submission.id !== excludeSubmissionId))
  {
    throw new RegistryStoreError(409, "submission_already_pending", "该地址已经在待审核队列中。");
  }
}

function normalizePersistedServer(candidate: unknown): RegistryServerEntry {
  const data = asRecord(candidate);
  return {
    id: requiredText(String(data.id ?? ""), "server.id", 128),
    sourceType: data.sourceType === "official" ? "official" : "community",
    displayName: requiredText(String(data.displayName ?? ""), "server.displayName", 64),
    regionLabel: requiredText(String(data.regionLabel ?? ""), "server.regionLabel", 64),
    baseUrl: normalizeHttpUrl(String(data.baseUrl ?? ""), "server.baseUrl"),
    wsUrl: normalizeWsUrl(typeof data.wsUrl === "string" ? data.wsUrl : undefined, String(data.baseUrl ?? "")),
    bandwidthProbeUrl: normalizeOptionalHttpUrl(typeof data.bandwidthProbeUrl === "string" ? data.bandwidthProbeUrl : undefined),
    listingState: data.listingState === "disabled" ? "disabled" : "approved",
    runtimeState: normalizeRuntimeState(data.runtimeState),
    qualityGrade: normalizeQualityGrade(data.qualityGrade),
    lastProbeAt: normalizeOptionalText(String(data.lastProbeAt ?? ""), 64),
    lastProbeRttMs: typeof data.lastProbeRttMs === "number" ? data.lastProbeRttMs : undefined,
    lastBandwidthMbps: typeof data.lastBandwidthMbps === "number" ? data.lastBandwidthMbps : undefined,
    failureReason: normalizeOptionalText(String(data.failureReason ?? ""), 256),
    healthOk: typeof data.healthOk === "boolean" ? data.healthOk : undefined,
    operatorName: normalizeOptionalText(String(data.operatorName ?? ""), 64),
    contact: normalizeOptionalText(String(data.contact ?? ""), 128),
    notes: normalizeOptionalText(String(data.notes ?? ""), 512),
    createdAt: normalizeTimestamp(String(data.createdAt ?? new Date().toISOString())),
    updatedAt: normalizeTimestamp(String(data.updatedAt ?? new Date().toISOString())),
    approvedAt: normalizeOptionalTimestamp(typeof data.approvedAt === "string" ? data.approvedAt : undefined),
    approvedBy: normalizeOptionalText(String(data.approvedBy ?? ""), 64),
    sortOrder: typeof data.sortOrder === "number" ? data.sortOrder : 0,
  };
}

function normalizePersistedSubmission(candidate: unknown): RegistrySubmission {
  const data = asRecord(candidate);
  const baseUrl = normalizeHttpUrl(String(data.baseUrl ?? ""), "submission.baseUrl");
  return {
    id: requiredText(String(data.id ?? ""), "submission.id", 128),
    displayName: requiredText(String(data.displayName ?? ""), "submission.displayName", 64),
    regionLabel: requiredText(String(data.regionLabel ?? ""), "submission.regionLabel", 64),
    baseUrl,
    wsUrl: normalizeWsUrl(typeof data.wsUrl === "string" ? data.wsUrl : undefined, baseUrl),
    bandwidthProbeUrl: normalizeOptionalHttpUrl(typeof data.bandwidthProbeUrl === "string" ? data.bandwidthProbeUrl : undefined),
    operatorName: requiredText(String(data.operatorName ?? ""), "submission.operatorName", 64),
    contact: requiredText(String(data.contact ?? ""), "submission.contact", 128),
    notes: normalizeOptionalText(String(data.notes ?? ""), 512) ?? "",
    status: normalizeSubmissionStatus(data.status),
    sourceIp: normalizeOptionalText(String(data.sourceIp ?? ""), 128) ?? "<unknown>",
    submittedAt: normalizeTimestamp(String(data.submittedAt ?? new Date().toISOString())),
    reviewedAt: normalizeOptionalTimestamp(typeof data.reviewedAt === "string" ? data.reviewedAt : undefined),
    reviewedBy: normalizeOptionalText(String(data.reviewedBy ?? ""), 64),
    reviewNote: normalizeOptionalText(String(data.reviewNote ?? ""), 512),
    linkedServerId: normalizeOptionalText(String(data.linkedServerId ?? ""), 128),
  };
}

function normalizePersistedSession(candidate: unknown): RegistryAdminSession {
  const data = asRecord(candidate);
  return {
    id: requiredText(String(data.id ?? ""), "session.id", 128),
    username: requiredText(String(data.username ?? ""), "session.username", 64),
    createdAt: normalizeTimestamp(String(data.createdAt ?? new Date().toISOString())),
    expiresAt: normalizeTimestamp(String(data.expiresAt ?? new Date().toISOString())),
  };
}

function normalizePersistedAudit(candidate: unknown): RegistryAuditEntry {
  const data = asRecord(candidate);
  return {
    id: requiredText(String(data.id ?? ""), "audit.id", 128),
    actor: requiredText(String(data.actor ?? ""), "audit.actor", 64),
    action: requiredText(String(data.action ?? ""), "audit.action", 64),
    targetType: data.targetType === "server" || data.targetType === "session" ? data.targetType : "submission",
    targetId: requiredText(String(data.targetId ?? ""), "audit.targetId", 128),
    createdAt: normalizeTimestamp(String(data.createdAt ?? new Date().toISOString())),
    detail: normalizeOptionalText(String(data.detail ?? ""), 512),
  };
}

function compareServerEntries(left: RegistryServerEntry, right: RegistryServerEntry) {
  const sourceDiff = getSourceRank(left.sourceType) - getSourceRank(right.sourceType);
  if (sourceDiff !== 0) {
    return sourceDiff;
  }

  const sortDiff = left.sortOrder - right.sortOrder;
  if (sortDiff !== 0) {
    return sortDiff;
  }

  return left.displayName.localeCompare(right.displayName, "zh-Hans-CN");
}

function getSourceRank(sourceType: RegistryServerSourceType) {
  return sourceType === "official" ? 0 : 1;
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

function normalizeSubmissionStatus(value: unknown): RegistrySubmissionStatus {
  if (value === "approved" || value === "rejected") {
    return value;
  }

  return "pending";
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

function normalizeOptionalHttpUrl(value: string | undefined) {
  if (!value || value.trim() === "") {
    return undefined;
  }

  return normalizeHttpUrl(value, "bandwidthProbeUrl");
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

function normalizeText(value: string, maxLength: number) {
  return value.trim().slice(0, maxLength);
}

function normalizeOptionalText(value: string | undefined, maxLength: number) {
  if (!value) {
    return undefined;
  }

  const normalized = value.trim();
  return normalized ? normalized.slice(0, maxLength) : undefined;
}

function normalizeTimestamp(value: string) {
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? new Date().toISOString() : parsed.toISOString();
}

function normalizeOptionalTimestamp(value: string | undefined) {
  return value ? normalizeTimestamp(value) : undefined;
}

function asRecord(value: unknown) {
  return value && typeof value === "object" ? value as Record<string, unknown> : {};
}

function roundMaybe(value: number | undefined) {
  return typeof value === "number" && Number.isFinite(value) ? Math.round(value * 100) / 100 : undefined;
}

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}
