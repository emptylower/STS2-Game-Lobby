import { randomUUID } from "node:crypto";
import type { SensitiveMatch } from "./filter.js";
import type { ModerationService } from "./moderation-service.js";
import { moderationHmac } from "./crypto.js";
import { prepareModerationMatches } from "./context.js";
import { ModerationCacheStore } from "./cache-store.js";
import {
  AiModerationStateStore,
  type ModerationRuleScope,
  type ReviewRejectionResult,
} from "./state-store.js";
import { AiModerationProvider, AiProviderError } from "./provider.js";
import type {
  AiModerationConfig,
  AiReviewOutcome,
  ModerationAnalysisRequest,
  ModerationSurface,
  PreparedModerationMatch,
} from "./ai-types.js";

export type ModerationPlan =
  | { kind: "allow"; source: "clean" | "exact_cache" | "context_cache" | "permanent" }
  | { kind: "block"; source: "permanent" }
  | { kind: "mask"; code: string }
  | {
      kind: "review";
      reviewId: string;
      timeoutMs: number;
      text: string;
      surface: ModerationSurface;
      prepared: PreparedModerationMatch[];
      exactKey: string;
      contextKeys: string[];
    };

interface RecentAiError {
  at: string;
  code: string;
  statusCode?: number;
}

export interface AiModerationRuntimeMetrics {
  started: number;
  allowed: number;
  blocked: number;
  degraded: number;
  exactCacheHits: number;
  contextCacheHits: number;
  permanentAllowHits: number;
  permanentBlockHits: number;
  active: number;
  queued: number;
  averageLatencyMs: number;
  circuitState: "closed" | "open";
  circuitOpenUntil: string | null;
  recentErrors: RecentAiError[];
}

interface QueuedPermit {
  resolve(): void;
}

export interface AiModerationServiceOptions {
  moderation: ModerationService;
  stateStore: AiModerationStateStore;
  cacheStore: ModerationCacheStore;
  hmacKey: Buffer | null;
  provider?: AiModerationProvider;
  now?: () => number;
  timeoutMs?: number;
  maxConcurrency?: number;
  maxQueue?: number;
  maxReviewsPerIpMinute?: number;
  log?: (message: string) => void;
}

export class AiSemanticModerationService {
  readonly stateStore: AiModerationStateStore;
  readonly cacheStore: ModerationCacheStore;
  private readonly moderation: ModerationService;
  private readonly hmacKey: Buffer | null;
  private readonly provider: AiModerationProvider;
  private readonly now: () => number;
  private readonly timeoutMs: number;
  private readonly maxConcurrency: number;
  private readonly maxQueue: number;
  private readonly maxReviewsPerIpMinute: number;
  private readonly log: (message: string) => void;
  private readonly queue: QueuedPermit[] = [];
  private readonly activeControllers = new Set<AbortController>();
  private readonly reviewTasks = new Set<Promise<AiReviewOutcome>>();
  private readonly ipReviews = new Map<string, number[]>();
  private active = 0;
  private closed = false;
  private consecutiveFailures = 0;
  private circuitOpenUntil = 0;
  private started = 0;
  private allowed = 0;
  private blocked = 0;
  private degraded = 0;
  private exactCacheHits = 0;
  private contextCacheHits = 0;
  private permanentAllowHits = 0;
  private permanentBlockHits = 0;
  private latencyTotalMs = 0;
  private latencySamples = 0;
  private readonly recentErrors: RecentAiError[] = [];

  constructor(options: AiModerationServiceOptions) {
    this.moderation = options.moderation;
    this.stateStore = options.stateStore;
    this.cacheStore = options.cacheStore;
    this.hmacKey = options.hmacKey;
    this.provider = options.provider ?? new AiModerationProvider();
    this.now = options.now ?? Date.now;
    this.timeoutMs = options.timeoutMs ?? 5_000;
    this.maxConcurrency = options.maxConcurrency ?? 4;
    this.maxQueue = options.maxQueue ?? 64;
    this.maxReviewsPerIpMinute = options.maxReviewsPerIpMinute ?? 10;
    this.log = options.log ?? ((message) => console.error(message));
  }

  prepare(text: string, surface: ModerationSurface = "chat_message"): ModerationPlan {
    const matches = this.moderation.findSensitiveMatches(text);
    if (matches.length === 0) return { kind: "allow", source: "clean" };
    if (matches.some((match) => this.stateStore.isTermPermanentlyBlocked(match))) {
      this.permanentBlockHits += 1;
      return { kind: "block", source: "permanent" };
    }
    if (matches.every((match) => this.stateStore.isTermPermanentlyAllowed(match))) {
      this.permanentAllowHits += 1;
      return { kind: "allow", source: "permanent" };
    }
    if (!this.hmacKey) return { kind: "mask", code: "missing_master_key" };

    const prepared = prepareModerationMatches(
      text,
      matches,
      (context) => moderationHmac(this.hmacKey!, "context-signature:v1", context),
    );
    if (prepared.some((item) =>
      this.stateStore.isPermanentlyBlocked(item.match, item.context.signature, surface))) {
      this.permanentBlockHits += 1;
      return { kind: "block", source: "permanent" };
    }
    const permanentCoverage = prepared.map((item) =>
      this.stateStore.isPermanentlyAllowed(item.match, item.context.signature, surface));
    if (permanentCoverage.every(Boolean)) {
      this.permanentAllowHits += 1;
      return { kind: "allow", source: "permanent" };
    }
    const exactKey = moderationHmac(
      this.hmacKey,
      "exact-message:v2",
      `${surface}\0${text.normalize("NFC")}`,
    );
    if (this.cacheStore.hasExact(exactKey)) {
      this.exactCacheHits += 1;
      return { kind: "allow", source: "exact_cache" };
    }
    const contextKeys = prepared.map((item) => moderationHmac(
      this.hmacKey!,
      "context-cache:v2",
      `${surface}\0${item.match.normalizedTerm}\0${item.context.signature}`,
    ));
    const contextCoverage = prepared.map((item, index) =>
      permanentCoverage[index] || this.cacheStore.hasContext(contextKeys[index]!));
    if (contextCoverage.every(Boolean)) {
      this.contextCacheHits += 1;
      return { kind: "allow", source: "context_cache" };
    }
    const uncoveredPrepared = prepared.filter((_item, index) => !contextCoverage[index]);
    const uncoveredContextKeys = contextKeys.filter((_key, index) => !contextCoverage[index]);

    const config = this.stateStore.getConfig();
    if (!config.enabled) return { kind: "mask", code: "ai_disabled" };
    if (!config.endpoint || !config.model) return { kind: "mask", code: "ai_unconfigured" };
    try {
      if (!this.stateStore.getApiKey()) return { kind: "mask", code: "missing_api_key" };
    } catch {
      return { kind: "mask", code: "credential_error" };
    }
    if (this.circuitOpenUntil > this.now()) return { kind: "mask", code: "circuit_open" };
    if (this.active >= this.maxConcurrency && this.queue.length >= this.maxQueue) {
      return { kind: "mask", code: "review_queue_full" };
    }
    return {
      kind: "review",
      reviewId: `review_request_${randomUUID()}`,
      timeoutMs: this.timeoutMs,
      text,
      surface,
      prepared: uncoveredPrepared,
      exactKey,
      contextKeys: uncoveredContextKeys,
    };
  }

  areNameMatchesPermanentlyAllowed(_text: string, matches: readonly SensitiveMatch[]): boolean {
    return matches.every((match) =>
      !this.stateStore.isTermPermanentlyBlocked(match)
      && this.stateStore.isTermPermanentlyAllowed(match));
  }

  rejectReviewCandidate(
    id: string,
    scope: ModerationRuleScope,
    note?: string,
  ): ReviewRejectionResult | null {
    const detail = this.stateStore.getReview(id);
    const result = this.stateStore.rejectReview(id, scope, note);
    if (!result || !this.hmacKey) return result;
    const contextKey = moderationHmac(
      this.hmacKey,
      "context-cache:v2",
      `${result.rule.surface}\0${result.rule.normalizedTerm}\0${detail?.contextSignature ?? result.rule.contextSignature ?? ""}`,
    );
    this.cacheStore.disallowContext(contextKey);
    if (detail?.evidence?.message) {
      this.cacheStore.disallowExact(moderationHmac(
        this.hmacKey,
        "exact-message:v2",
        `${result.rule.surface}\0${detail.evidence.message.normalize("NFC")}`,
      ));
    }
    return result;
  }

  review(plan: Extract<ModerationPlan, { kind: "review" }>, clientIp: string): Promise<AiReviewOutcome> {
    const task = this.runReview(plan, clientIp);
    this.reviewTasks.add(task);
    void task.finally(() => this.reviewTasks.delete(task)).catch(() => undefined);
    return task;
  }

  private async runReview(
    plan: Extract<ModerationPlan, { kind: "review" }>,
    clientIp: string,
  ): Promise<AiReviewOutcome> {
    if (this.closed) return this.degrade("service_closing");
    if (!this.consumeIpReview(clientIp)) return this.degrade("review_rate_limited");
    if (this.circuitOpenUntil > this.now()) return this.degrade("circuit_open");
    if (this.active >= this.maxConcurrency && this.queue.length >= this.maxQueue) {
      return this.degrade("review_queue_full");
    }

    this.started += 1;
    const startedAt = this.now();
    if (this.active >= this.maxConcurrency) await this.waitForPermit();
    if (this.closed) return this.degrade("service_closing");
    this.active += 1;
    const controller = new AbortController();
    this.activeControllers.add(controller);
    const timer = setTimeout(() => controller.abort(), Math.max(1, this.timeoutMs - (this.now() - startedAt)));
    timer.unref();
    try {
      const config = this.stateStore.getConfig();
      const apiKey = this.stateStore.getApiKey();
      if (!config.enabled || !apiKey) return this.degrade("ai_unconfigured");
      const request = toAnalysisRequest(plan);
      const providerResult = await this.callWithRetry(config, apiKey, request, controller.signal, startedAt);
      this.consecutiveFailures = 0;
      this.circuitOpenUntil = 0;
      this.latencyTotalMs += Math.max(0, this.now() - startedAt);
      this.latencySamples += 1;
      if (providerResult.result.decision === "allow") {
        this.allowed += 1;
        this.cacheStore.allowExact(plan.exactKey);
        for (const key of plan.contextKeys) this.cacheStore.allowContext(key);
        this.stateStore.recordAllowed(
          plan.text,
          plan.prepared,
          providerResult.result,
          config.protocol,
          config.model,
          providerResult.structuredMode,
          plan.surface,
        );
        return { kind: "allow", ...providerResult };
      }
      this.blocked += 1;
      return { kind: "block", ...providerResult };
    } catch (error) {
      return this.handleProviderFailure(error);
    } finally {
      clearTimeout(timer);
      this.activeControllers.delete(controller);
      this.active -= 1;
      this.releasePermit();
    }
  }

  async testConfiguration(config: AiModerationConfig, apiKey: string): Promise<{
    ok: true;
    decision: "allow" | "block";
    structuredMode: "strict" | "prompted_json";
    latencyMs: number;
  }> {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), this.timeoutMs);
    timer.unref();
    const startedAt = this.now();
    try {
      const request: ModerationAnalysisRequest = {
        surface: "chat_message",
        message: "这是对测试词的语言学讨论。",
        contexts: [{ id: "context_1", text: "这是对测试词的语言学讨论。", normalizedText: "这是对测试词的语言学讨论。", signature: "test" }],
        matches: [{
          matchId: "match_1",
          termId: "term_test",
          term: "测试词",
          category: "test",
          source: "test",
          start: 3,
          end: 6,
          contextCandidateId: "context_1",
        }],
      };
      const result = await this.provider.analyze(config, apiKey, request, controller.signal);
      this.consecutiveFailures = 0;
      this.circuitOpenUntil = 0;
      return {
        ok: true,
        decision: result.result.decision,
        structuredMode: result.structuredMode,
        latencyMs: Math.max(0, this.now() - startedAt),
      };
    } finally {
      clearTimeout(timer);
    }
  }

  getStatus() {
    const cache = this.cacheStore.stats();
    const config = this.stateStore.getConfigView();
    return {
      config,
      health: {
        ready: config.credentialStatus === "ready"
          && config.enabled
          && config.endpoint.length > 0
          && config.model.length > 0,
        ...this.getMetrics(),
      },
      cache,
      reviews: this.stateStore.stats(),
    };
  }

  getMetrics(): AiModerationRuntimeMetrics {
    const now = this.now();
    return {
      started: this.started,
      allowed: this.allowed,
      blocked: this.blocked,
      degraded: this.degraded,
      exactCacheHits: this.exactCacheHits,
      contextCacheHits: this.contextCacheHits,
      permanentAllowHits: this.permanentAllowHits,
      permanentBlockHits: this.permanentBlockHits,
      active: this.active,
      queued: this.queue.length,
      averageLatencyMs: this.latencySamples === 0 ? 0 : Math.round(this.latencyTotalMs / this.latencySamples),
      circuitState: this.circuitOpenUntil > now ? "open" : "closed",
      circuitOpenUntil: this.circuitOpenUntil > now ? new Date(this.circuitOpenUntil).toISOString() : null,
      recentErrors: this.recentErrors.map((error) => ({ ...error })),
    };
  }

  async close(): Promise<void> {
    this.closed = true;
    for (const controller of this.activeControllers) controller.abort();
    for (const permit of this.queue.splice(0)) permit.resolve();
    await Promise.allSettled([...this.reviewTasks]);
    this.cacheStore.flush();
    this.stateStore.flush();
  }

  private async callWithRetry(
    config: AiModerationConfig,
    apiKey: string,
    request: ModerationAnalysisRequest,
    signal: AbortSignal,
    startedAt: number,
  ) {
    try {
      return await this.provider.analyze(config, apiKey, request, signal);
    } catch (error) {
      if (
        error instanceof AiProviderError
        && error.retryable
        && !signal.aborted
        && this.now() - startedAt < this.timeoutMs - 100
      ) {
        return this.provider.analyze(config, apiKey, request, signal);
      }
      throw error;
    }
  }

  private handleProviderFailure(error: unknown): AiReviewOutcome {
    const providerError = error instanceof AiProviderError
      ? error
      : new AiProviderError("provider_error", "AI provider failed");
    this.consecutiveFailures += 1;
    if (this.consecutiveFailures >= 5) this.circuitOpenUntil = this.now() + 30_000;
    const entry: RecentAiError = {
      at: new Date(this.now()).toISOString(),
      code: providerError.code,
      ...(providerError.statusCode === undefined ? {} : { statusCode: providerError.statusCode }),
    };
    this.recentErrors.unshift(entry);
    this.recentErrors.splice(20);
    this.log(
      `[moderation-ai] event=provider_error code=${providerError.code}`
      + `${providerError.statusCode === undefined ? "" : ` status=${providerError.statusCode}`}`,
    );
    return this.degrade(providerError.code);
  }

  private degrade(code: string): AiReviewOutcome {
    this.degraded += 1;
    return { kind: "degraded", code };
  }

  private consumeIpReview(clientIp: string): boolean {
    const now = this.now();
    const cutoff = now - 60_000;
    const hits = (this.ipReviews.get(clientIp) ?? []).filter((timestamp) => timestamp > cutoff);
    if (hits.length >= this.maxReviewsPerIpMinute) {
      this.ipReviews.set(clientIp, hits);
      return false;
    }
    hits.push(now);
    this.ipReviews.set(clientIp, hits);
    if (this.ipReviews.size > 10_000) {
      for (const [ip, timestamps] of this.ipReviews) {
        if (timestamps.every((timestamp) => timestamp <= cutoff)) this.ipReviews.delete(ip);
      }
    }
    return true;
  }

  private waitForPermit(): Promise<void> {
    return new Promise((resolve) => this.queue.push({ resolve }));
  }

  private releasePermit(): void {
    this.queue.shift()?.resolve();
  }
}

function toAnalysisRequest(plan: Extract<ModerationPlan, { kind: "review" }>): ModerationAnalysisRequest {
  return {
    surface: plan.surface,
    message: plan.text,
    contexts: plan.prepared.map((item) => item.context),
    matches: plan.prepared.map((item) => ({
      matchId: item.matchId,
      termId: item.match.termId,
      term: item.match.displayTerm,
      category: item.match.category,
      source: item.match.source,
      start: item.match.start,
      end: item.match.end,
      contextCandidateId: item.context.id,
    })),
  };
}
