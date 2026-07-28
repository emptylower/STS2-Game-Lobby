import { randomUUID } from "node:crypto";
import { mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import {
  decryptModerationValue,
  encryptModerationValue,
  type EncryptedValue,
} from "./crypto.js";
import {
  DEFAULT_AI_MODERATION_CONFIG,
  type AiModerationConfig,
  type ModerationModelResult,
  type ModerationSurface,
  type ModerationUsage,
  type PreparedModerationMatch,
} from "./ai-types.js";
import type { SensitiveMatch } from "./filter.js";

export type ReviewStatus = "pending" | "approved" | "rejected";
export type ModerationRuleScope = "term" | "context";
export type AllowlistScope = ModerationRuleScope;

interface ReviewEvidence {
  message: string;
  context: string;
}

export interface ModerationReviewRecord {
  id: string;
  aggregateKey: string;
  status: ReviewStatus;
  surface: ModerationSurface;
  termId: string;
  normalizedTerm: string;
  displayTerm: string;
  category: string;
  source: string;
  contextSignature: string;
  evidence?: EncryptedValue;
  evidenceExpiresAt?: number;
  observations: number;
  firstObservedAt: string;
  lastObservedAt: string;
  provider: string;
  model: string;
  reasonCode: string;
  aiDecision: "allow";
  aiVerdict: "allow";
  aiUsage: ModerationUsage;
  aiContextCandidateId: string;
  structuredMode: "strict" | "prompted_json";
  note?: string;
}

export interface PermanentAllowRule {
  id: string;
  scope: ModerationRuleScope;
  surface: ModerationSurface;
  termId: string;
  normalizedTerm: string;
  contextSignature?: string;
  reviewId: string;
  createdAt: string;
  note?: string;
}

export interface PermanentBlockRule {
  id: string;
  scope: ModerationRuleScope;
  surface: ModerationSurface;
  termId: string;
  normalizedTerm: string;
  contextSignature?: string;
  reviewId: string;
  createdAt: string;
  note?: string;
}

interface PersistedModerationState {
  version: 2;
  config: AiModerationConfig;
  encryptedApiKey?: EncryptedValue;
  reviews: ModerationReviewRecord[];
  allowlist: PermanentAllowRule[];
  blocklist: PermanentBlockRule[];
}

interface PersistedModerationStateV1 extends Omit<PersistedModerationState, "version" | "blocklist"> {
  version: 1;
}

export interface ReviewRejectionResult {
  review: ReviewListItem;
  rule: PermanentBlockRule;
}

export interface ReviewListItem extends Omit<ModerationReviewRecord, "aggregateKey" | "evidence"> {
  evidenceAvailable: boolean;
}

export interface ReviewDetail extends ReviewListItem {
  evidence: ReviewEvidence | null;
}

export class AiModerationStateStore {
  private readonly path: string;
  private readonly masterKey: Buffer | null;
  private readonly now: () => number;
  private state: PersistedModerationState;

  constructor(path: string, masterKey: Buffer | null, now: () => number = Date.now) {
    this.path = resolve(path);
    this.masterKey = masterKey;
    this.now = now;
    this.state = this.load();
    if (this.cleanupExpiredEvidence()) this.save();
  }

  getConfig(): AiModerationConfig {
    return { ...this.state.config };
  }

  getConfigView(): AiModerationConfig & {
    apiKeyConfigured: boolean;
    credentialKeyConfigured: boolean;
    credentialStatus: "ready" | "missing_master_key" | "missing_api_key" | "decrypt_error";
  } {
    let credentialStatus: "ready" | "missing_master_key" | "missing_api_key" | "decrypt_error";
    if (!this.masterKey) credentialStatus = "missing_master_key";
    else if (!this.state.encryptedApiKey) credentialStatus = "missing_api_key";
    else {
      try {
        this.getApiKey();
        credentialStatus = "ready";
      } catch {
        credentialStatus = "decrypt_error";
      }
    }
    return {
      ...this.getConfig(),
      apiKeyConfigured: this.state.encryptedApiKey !== undefined,
      credentialKeyConfigured: this.masterKey !== null,
      credentialStatus,
    };
  }

  getApiKey(): string | null {
    if (!this.state.encryptedApiKey) return null;
    if (!this.masterKey) throw new Error("AI moderation credential key is not configured");
    return decryptModerationValue(this.masterKey, this.state.encryptedApiKey, "ai-api-key:v1");
  }

  updateConfig(
    config: AiModerationConfig,
    apiKeyAction: "keep" | "replace" | "clear",
    apiKey?: string,
  ): void {
    const staged: PersistedModerationState = {
      ...this.state,
      config: { ...config },
      reviews: this.state.reviews.map(cloneReview),
      allowlist: this.state.allowlist.map(cloneRule),
      blocklist: this.state.blocklist.map(cloneBlockRule),
    };
    if (apiKeyAction === "clear") {
      delete staged.encryptedApiKey;
    } else if (apiKeyAction === "replace") {
      if (!this.masterKey) throw new Error("AI_MODERATION_CREDENTIAL_KEY is required to save an API key");
      if (!apiKey?.trim()) throw new Error("apiKey is required when apiKeyAction is replace");
      staged.encryptedApiKey = encryptModerationValue(this.masterKey, apiKey.trim(), "ai-api-key:v1");
    }
    this.save(staged);
    this.state = staged;
  }

  isPermanentlyAllowed(
    match: SensitiveMatch,
    contextSignature: string,
    surface: ModerationSurface = "chat_message",
  ): boolean {
    return this.state.allowlist.some((rule) =>
      rule.normalizedTerm === match.normalizedTerm
      && (rule.scope === "term" || (
        rule.surface === surface && rule.contextSignature === contextSignature
      )));
  }

  isTermPermanentlyAllowed(match: SensitiveMatch): boolean {
    return this.state.allowlist.some((rule) =>
      rule.scope === "term" && rule.normalizedTerm === match.normalizedTerm);
  }

  isPermanentlyBlocked(
    match: SensitiveMatch,
    contextSignature: string,
    surface: ModerationSurface = "chat_message",
  ): boolean {
    return this.state.blocklist.some((rule) =>
      rule.normalizedTerm === match.normalizedTerm
      && (rule.scope === "term" || (
        rule.surface === surface && rule.contextSignature === contextSignature
      )));
  }

  isTermPermanentlyBlocked(match: SensitiveMatch): boolean {
    return this.state.blocklist.some((rule) =>
      rule.scope === "term" && rule.normalizedTerm === match.normalizedTerm);
  }

  recordAllowed(
    message: string,
    prepared: readonly PreparedModerationMatch[],
    result: ModerationModelResult,
    provider: string,
    model: string,
    structuredMode: "strict" | "prompted_json",
    surface: ModerationSurface = "chat_message",
  ): void {
    if (!this.masterKey) return;
    const now = this.now();
    const nowIso = new Date(now).toISOString();
    for (const item of prepared) {
      const modelMatch = result.matches.find((candidate) => candidate.matchId === item.matchId);
      if (!modelMatch || modelMatch.verdict !== "allow") continue;
      const aggregateKey = `${surface}:${item.match.termId}:${item.context.signature}`;
      let review = this.state.reviews.find((candidate) => candidate.aggregateKey === aggregateKey);
      if (!review) {
        const id = `review_${randomUUID()}`;
        const evidence = encryptModerationValue(
          this.masterKey,
          JSON.stringify({ message, context: item.context.text } satisfies ReviewEvidence),
          `review-evidence:${id}:v1`,
        );
        review = {
          id,
          aggregateKey,
          status: "pending",
          surface,
          termId: item.match.termId,
          normalizedTerm: item.match.normalizedTerm,
          displayTerm: item.match.displayTerm,
          category: item.match.category,
          source: item.match.source,
          contextSignature: item.context.signature,
          evidence,
          evidenceExpiresAt: now + 30 * 24 * 60 * 60_000,
          observations: 0,
          firstObservedAt: nowIso,
          lastObservedAt: nowIso,
          provider,
          model,
          reasonCode: result.reasonCode,
          aiDecision: "allow",
          aiVerdict: "allow",
          aiUsage: modelMatch.usage,
          aiContextCandidateId: modelMatch.contextCandidateId,
          structuredMode,
        };
        this.state.reviews.push(review);
      } else if (!review.evidence) {
        review.evidence = encryptModerationValue(
          this.masterKey,
          JSON.stringify({ message, context: item.context.text } satisfies ReviewEvidence),
          `review-evidence:${review.id}:v1`,
        );
        review.evidenceExpiresAt = now + 30 * 24 * 60 * 60_000;
      }
      review.observations += 1;
      review.lastObservedAt = nowIso;
      review.provider = provider;
      review.model = model;
      review.reasonCode = result.reasonCode;
      review.aiDecision = "allow";
      review.aiVerdict = "allow";
      review.aiUsage = modelMatch.usage;
      review.aiContextCandidateId = modelMatch.contextCandidateId;
      review.structuredMode = structuredMode;
    }
    this.save();
  }

  listReviews(status: ReviewStatus | undefined, cursor: string | undefined, limit: number) {
    if (this.cleanupExpiredEvidence()) this.save();
    const records = this.state.reviews
      .filter((review) => status === undefined || review.status === status)
      .sort((left, right) => right.lastObservedAt.localeCompare(left.lastObservedAt));
    return paginate(records.map((review) => this.toListItem(review)), cursor, limit);
  }

  getReview(id: string): ReviewDetail | null {
    if (this.cleanupExpiredEvidence()) this.save();
    const review = this.state.reviews.find((candidate) => candidate.id === id);
    if (!review) return null;
    let evidence: ReviewEvidence | null = null;
    if (review.evidence && this.masterKey) {
      try {
        evidence = JSON.parse(decryptModerationValue(
          this.masterKey,
          review.evidence,
          `review-evidence:${review.id}:v1`,
        )) as ReviewEvidence;
      } catch {
        evidence = null;
      }
    }
    return { ...this.toListItem(review), evidence };
  }

  approveReview(id: string, scope: AllowlistScope, note?: string): PermanentAllowRule | null {
    const review = this.state.reviews.find((candidate) => candidate.id === id);
    if (!review) return null;
    const existing = this.state.allowlist.find((rule) =>
      rule.scope === scope
      && rule.normalizedTerm === review.normalizedTerm
      && (scope === "term" || (
        rule.surface === review.surface
        && rule.contextSignature === review.contextSignature
      )));
    const rule = existing ?? {
      id: `allow_${randomUUID()}`,
      scope,
      surface: review.surface,
      termId: review.termId,
      normalizedTerm: review.normalizedTerm,
      ...(scope === "context" ? { contextSignature: review.contextSignature } : {}),
      reviewId: review.id,
      createdAt: new Date(this.now()).toISOString(),
      ...(note?.trim() ? { note: note.trim().slice(0, 500) } : {}),
    } satisfies PermanentAllowRule;
    if (!existing) this.state.allowlist.push(rule);
    review.status = "approved";
    if (note?.trim()) review.note = note.trim().slice(0, 500);
    delete review.evidence;
    delete review.evidenceExpiresAt;
    this.save();
    return cloneRule(rule);
  }

  rejectReview(
    id: string,
    scope: ModerationRuleScope,
    note?: string,
  ): ReviewRejectionResult | null {
    const review = this.state.reviews.find((candidate) => candidate.id === id);
    if (!review) return null;
    const existing = this.state.blocklist.find((rule) =>
      rule.scope === scope
      && rule.normalizedTerm === review.normalizedTerm
      && (scope === "term" || (
        rule.surface === review.surface
        && rule.contextSignature === review.contextSignature
      )));
    const rule = existing ?? {
      id: `block_${randomUUID()}`,
      scope,
      surface: review.surface,
      termId: review.termId,
      normalizedTerm: review.normalizedTerm,
      ...(scope === "context" ? { contextSignature: review.contextSignature } : {}),
      reviewId: review.id,
      createdAt: new Date(this.now()).toISOString(),
      ...(note?.trim() ? { note: note.trim().slice(0, 500) } : {}),
    } satisfies PermanentBlockRule;
    if (!existing) this.state.blocklist.push(rule);
    review.status = "rejected";
    if (note?.trim()) review.note = note.trim().slice(0, 500);
    delete review.evidence;
    delete review.evidenceExpiresAt;
    this.save();
    return { review: this.toListItem(review), rule: cloneBlockRule(rule) };
  }

  listAllowlist(cursor: string | undefined, limit: number, scope?: AllowlistScope) {
    const rules = this.state.allowlist
      .filter((rule) => scope === undefined || rule.scope === scope)
      .sort((left, right) => right.createdAt.localeCompare(left.createdAt));
    return paginate(rules.map(cloneRule), cursor, limit);
  }

  revokeAllowRule(id: string): boolean {
    const index = this.state.allowlist.findIndex((rule) => rule.id === id);
    if (index < 0) return false;
    const [removed] = this.state.allowlist.splice(index, 1);
    if (removed) this.reopenReviewWhenUnruled(removed.reviewId);
    this.save();
    return true;
  }

  listBlocklist(cursor: string | undefined, limit: number, scope?: ModerationRuleScope) {
    const rules = this.state.blocklist
      .filter((rule) => scope === undefined || rule.scope === scope)
      .sort((left, right) => right.createdAt.localeCompare(left.createdAt));
    return paginate(rules.map(cloneBlockRule), cursor, limit);
  }

  revokeBlockRule(id: string): boolean {
    const index = this.state.blocklist.findIndex((rule) => rule.id === id);
    if (index < 0) return false;
    const [removed] = this.state.blocklist.splice(index, 1);
    if (removed) this.reopenReviewWhenUnruled(removed.reviewId);
    this.save();
    return true;
  }

  stats(): {
    pendingReviews: number;
    approvedReviews: number;
    rejectedReviews: number;
    allowRules: number;
    blockRules: number;
  } {
    return {
      pendingReviews: this.state.reviews.filter((review) => review.status === "pending").length,
      approvedReviews: this.state.reviews.filter((review) => review.status === "approved").length,
      rejectedReviews: this.state.reviews.filter((review) => review.status === "rejected").length,
      allowRules: this.state.allowlist.length,
      blockRules: this.state.blocklist.length,
    };
  }

  flush(): void {
    // All state mutations are persisted synchronously before becoming visible.
  }

  private toListItem(review: ModerationReviewRecord): ReviewListItem {
    const { aggregateKey: _aggregateKey, evidence, ...rest } = review;
    return { ...rest, evidenceAvailable: evidence !== undefined };
  }

  private cleanupExpiredEvidence(): boolean {
    const now = this.now();
    let changed = false;
    for (const review of this.state.reviews) {
      if (review.evidenceExpiresAt !== undefined && review.evidenceExpiresAt <= now) {
        delete review.evidence;
        delete review.evidenceExpiresAt;
        changed = true;
      }
    }
    return changed;
  }

  private reopenReviewWhenUnruled(reviewId: string): void {
    const stillRuled = this.state.allowlist.some((rule) => rule.reviewId === reviewId)
      || this.state.blocklist.some((rule) => rule.reviewId === reviewId);
    if (stillRuled) return;
    const review = this.state.reviews.find((candidate) => candidate.id === reviewId);
    if (!review) return;
    review.status = "pending";
    delete review.note;
  }

  private save(state = this.state): void {
    mkdirSync(dirname(this.path), { recursive: true });
    const temp = `${this.path}.tmp`;
    writeFileSync(temp, JSON.stringify(state, null, 2), { encoding: "utf8", mode: 0o600 });
    renameSync(temp, this.path);
  }

  private load(): PersistedModerationState {
    try {
      const parsed = JSON.parse(readFileSync(this.path, "utf8")) as
        Partial<PersistedModerationState | PersistedModerationStateV1>;
      if (parsed.version !== 1 && parsed.version !== 2) throw new Error("unsupported state version");
      return {
        version: 2,
        config: normalizeConfig(parsed.config),
        ...(isEncryptedValue(parsed.encryptedApiKey) ? { encryptedApiKey: parsed.encryptedApiKey } : {}),
        reviews: Array.isArray(parsed.reviews) ? parsed.reviews.filter(isReview).map(normalizeReview) : [],
        allowlist: Array.isArray(parsed.allowlist) ? parsed.allowlist.filter(isRule).map(normalizeRule) : [],
        blocklist: parsed.version === 2 && Array.isArray(parsed.blocklist)
          ? parsed.blocklist.filter(isBlockRule).map(normalizeBlockRule)
          : [],
      };
    } catch {
      return {
        version: 2,
        config: { ...DEFAULT_AI_MODERATION_CONFIG },
        reviews: [],
        allowlist: [],
        blocklist: [],
      };
    }
  }
}

function normalizeConfig(value: unknown): AiModerationConfig {
  if (!value || typeof value !== "object") return { ...DEFAULT_AI_MODERATION_CONFIG };
  const candidate = value as Partial<AiModerationConfig>;
  const protocol = candidate.protocol === "openai_chat_completions" || candidate.protocol === "anthropic_messages"
    ? candidate.protocol
    : "openai_responses";
  return {
    enabled: candidate.enabled === true,
    protocol,
    endpoint: typeof candidate.endpoint === "string" ? candidate.endpoint : "",
    model: typeof candidate.model === "string" ? candidate.model : "",
    allowPrivateNetwork: candidate.allowPrivateNetwork === true,
    jsonFallbackEnabled: candidate.jsonFallbackEnabled === true,
  };
}

function paginate<T extends { id: string }>(records: T[], cursor: string | undefined, limit: number) {
  const start = cursor ? Math.max(0, records.findIndex((record) => record.id === cursor) + 1) : 0;
  const items = records.slice(start, start + limit);
  return {
    items,
    nextCursor: start + limit < records.length ? items.at(-1)?.id ?? null : null,
  };
}

function isEncryptedValue(value: unknown): value is EncryptedValue {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Partial<EncryptedValue>;
  return candidate.algorithm === "aes-256-gcm"
    && typeof candidate.iv === "string"
    && typeof candidate.ciphertext === "string"
    && typeof candidate.tag === "string";
}

function isReview(value: unknown): value is ModerationReviewRecord {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Partial<ModerationReviewRecord>;
  return typeof candidate.id === "string"
    && typeof candidate.aggregateKey === "string"
    && (candidate.status === "pending" || candidate.status === "approved" || candidate.status === "rejected")
    && typeof candidate.normalizedTerm === "string"
    && typeof candidate.contextSignature === "string";
}

function isRule(value: unknown): value is PermanentAllowRule {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Partial<PermanentAllowRule>;
  return typeof candidate.id === "string"
    && (candidate.scope === "term" || candidate.scope === "context")
    && typeof candidate.normalizedTerm === "string"
    && typeof candidate.reviewId === "string";
}

function isBlockRule(value: unknown): value is PermanentBlockRule {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Partial<PermanentBlockRule>;
  return typeof candidate.id === "string"
    && (candidate.scope === "term" || candidate.scope === "context")
    && typeof candidate.normalizedTerm === "string"
    && typeof candidate.reviewId === "string";
}

function cloneReview(value: ModerationReviewRecord): ModerationReviewRecord {
  return JSON.parse(JSON.stringify(value)) as ModerationReviewRecord;
}

function normalizeReview(value: ModerationReviewRecord): ModerationReviewRecord {
  const candidate = value as ModerationReviewRecord & Partial<Pick<
    ModerationReviewRecord,
    "aiDecision" | "aiVerdict" | "aiUsage" | "aiContextCandidateId" | "structuredMode"
  >>;
  return cloneReview({
    ...candidate,
    surface: normalizeSurface(candidate.surface),
    aiDecision: "allow",
    aiVerdict: "allow",
    aiUsage: candidate.aiUsage ?? "uncertain",
    aiContextCandidateId: candidate.aiContextCandidateId ?? "",
    structuredMode: candidate.structuredMode ?? "strict",
  });
}

function cloneRule(value: PermanentAllowRule): PermanentAllowRule {
  return { ...value };
}

function normalizeRule(value: PermanentAllowRule): PermanentAllowRule {
  return { ...value, surface: normalizeSurface(value.surface) };
}

function cloneBlockRule(value: PermanentBlockRule): PermanentBlockRule {
  return { ...value };
}

function normalizeBlockRule(value: PermanentBlockRule): PermanentBlockRule {
  return { ...value, surface: normalizeSurface(value.surface) };
}

function normalizeSurface(value: unknown): ModerationSurface {
  if (
    value === "room_name"
    || value === "player_name"
    || value === "character_name"
  ) return value;
  return "chat_message";
}
