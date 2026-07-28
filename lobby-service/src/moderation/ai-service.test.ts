import assert from "node:assert/strict";
import { randomBytes } from "node:crypto";
import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { AiSemanticModerationService } from "./ai-service.js";
import { ModerationCacheStore } from "./cache-store.js";
import { deriveModerationHmacKey } from "./crypto.js";
import { ModerationService } from "./moderation-service.js";
import { AiModerationProvider } from "./provider.js";
import { AiModerationStateStore } from "./state-store.js";

function createHarness(responseText: string | null) {
  const dir = mkdtempSync(join(tmpdir(), "sts2-ai-service-"));
  const lexiconDir = join(dir, "lexicon");
  mkdirSync(lexiconDir);
  writeFileSync(join(lexiconDir, "misc.txt"), "敏感词\n另一个词\n", "utf8");
  const key = randomBytes(32);
  const state = new AiModerationStateStore(join(dir, "state.json"), key);
  state.updateConfig({
    enabled: true,
    protocol: "openai_responses",
    endpoint: "http://127.0.0.1/v1/responses",
    model: "test-model",
    allowPrivateNetwork: true,
    jsonFallbackEnabled: false,
  }, "replace", "test-api-key");
  let fetchCalls = 0;
  const provider = new AiModerationProvider({
    fetch: (async () => {
      fetchCalls += 1;
      if (responseText === null) throw new Error("provider offline");
      return new Response(JSON.stringify({
        status: "completed",
        output: [{ content: [{ type: "output_text", text: responseText }] }],
      }), { status: 200 });
    }) as typeof fetch,
  });
  const moderation = ModerationService.create({ lexiconDir, enabled: true });
  const cache = new ModerationCacheStore(join(dir, "cache.json"));
  const service = new AiSemanticModerationService({
    moderation,
    stateStore: state,
    cacheStore: cache,
    hmacKey: deriveModerationHmacKey(key),
    provider,
    timeoutMs: 1_000,
  });
  return { dir, service, state, moderation, getFetchCalls: () => fetchCalls };
}

function output(decision: "allow" | "block") {
  return JSON.stringify({
    schemaVersion: 1,
    decision,
    reasonCode: decision === "allow" ? "quoted_or_discussed" : "prohibited_use",
    matches: [{
      matchId: "match_1",
      verdict: decision,
      usage: decision === "allow" ? "quoted_reference" : "prohibited",
      contextCandidateId: "context_1",
    }],
  });
}

test("AI allow immediately populates exact cache and creates one review candidate", async () => {
  const harness = createHarness(output("allow"));
  const plan = harness.service.prepare("这里讨论敏感词");
  assert.equal(plan.kind, "review");
  if (plan.kind !== "review") return;
  const outcome = await harness.service.review(plan, "198.51.100.1");
  assert.equal(outcome.kind, "allow");
  assert.equal(harness.getFetchCalls(), 1);
  assert.deepEqual(harness.service.prepare("这里讨论敏感词"), {
    kind: "allow",
    source: "exact_cache",
  });
  assert.equal(harness.state.listReviews("pending", undefined, 10).items.length, 1);
});

test("AI block does not populate cache and remains reviewable on the next send", async () => {
  const harness = createHarness(output("block"));
  const plan = harness.service.prepare("这里使用敏感词");
  assert.equal(plan.kind, "review");
  if (plan.kind !== "review") return;
  assert.equal((await harness.service.review(plan, "198.51.100.2")).kind, "block");
  assert.equal(harness.service.prepare("这里使用敏感词").kind, "review");
  assert.equal(harness.state.listReviews(undefined, undefined, 10).items.length, 0);
});

test("provider failures degrade to masking and surface sanitized runtime errors", async () => {
  const harness = createHarness(null);
  const base = harness.service;
  // The default logger is exercised by the same error path; runtime status must not contain message content.
  const plan = base.prepare("用户发送敏感词");
  assert.equal(plan.kind, "review");
  if (plan.kind !== "review") return;
  const outcome = await base.review(plan, "198.51.100.3");
  assert.equal(outcome.kind, "degraded");
  assert.equal(harness.getFetchCalls(), 2);
  assert.equal(base.getMetrics().degraded, 1);
  assert.equal(JSON.stringify(base.getMetrics()).includes("用户发送"), false);
});

test("human term approval bypasses AI for chat and names until revoked", async () => {
  const harness = createHarness(output("allow"));
  const plan = harness.service.prepare("讨论敏感词");
  assert.equal(plan.kind, "review");
  if (plan.kind !== "review") return;
  await harness.service.review(plan, "198.51.100.4");
  const review = harness.state.listReviews("pending", undefined, 10).items[0]!;
  const rule = harness.state.approveReview(review.id, "term");
  const name = "敏感词玩家";
  const matches = harness.moderation.findSensitiveMatches(name);
  assert.equal(harness.service.areNameMatchesPermanentlyAllowed(name, matches), true);
  harness.state.revokeAllowRule(rule!.id);
  assert.equal(harness.service.areNameMatchesPermanentlyAllowed(name, matches), false);
});

test("human context approval never bypasses the existing name rejection", async () => {
  const harness = createHarness(output("allow"));
  const message = "敏感词";
  const plan = harness.service.prepare(message);
  assert.equal(plan.kind, "review");
  if (plan.kind !== "review") return;
  await harness.service.review(plan, "198.51.100.7");
  const review = harness.state.listReviews("pending", undefined, 10).items[0]!;
  harness.state.approveReview(review.id, "context");
  assert.equal(
    harness.service.areNameMatchesPermanentlyAllowed(
      message,
      harness.moderation.findSensitiveMatches(message),
    ),
    false,
  );
});

test("human term rules remain active when the credential master key is unavailable", async () => {
  const harness = createHarness(output("allow"));
  const plan = harness.service.prepare("讨论敏感词");
  assert.equal(plan.kind, "review");
  if (plan.kind !== "review") return;
  await harness.service.review(plan, "198.51.100.5");
  const review = harness.state.listReviews("pending", undefined, 10).items[0]!;
  harness.state.approveReview(review.id, "term");

  const stateWithoutKey = new AiModerationStateStore(join(harness.dir, "state.json"), null);
  const serviceWithoutKey = new AiSemanticModerationService({
    moderation: harness.moderation,
    stateStore: stateWithoutKey,
    cacheStore: new ModerationCacheStore(join(harness.dir, "cache-no-key.json")),
    hmacKey: null,
  });
  assert.deepEqual(serviceWithoutKey.prepare("再次讨论敏感词"), {
    kind: "allow",
    source: "permanent",
  });
  const name = "敏感词玩家";
  assert.equal(
    serviceWithoutKey.areNameMatchesPermanentlyAllowed(
      name,
      harness.moderation.findSensitiveMatches(name),
    ),
    true,
  );
});

test("AI only receives matches not already covered by permanent rules", async () => {
  const harness = createHarness(output("allow"));
  const firstPlan = harness.service.prepare("讨论敏感词");
  assert.equal(firstPlan.kind, "review");
  if (firstPlan.kind !== "review") return;
  await harness.service.review(firstPlan, "198.51.100.6");
  const review = harness.state.listReviews("pending", undefined, 10).items[0]!;
  harness.state.approveReview(review.id, "term");

  const mixedPlan = harness.service.prepare("敏感词和另一个词都在这里");
  assert.equal(mixedPlan.kind, "review");
  if (mixedPlan.kind !== "review") return;
  assert.equal(mixedPlan.prepared.length, 1);
  assert.equal(mixedPlan.prepared[0]?.match.normalizedTerm, "另一个词");
  assert.equal(mixedPlan.contextKeys.length, 1);
});

test("human rejection invalidates the safe cache and permanently blocks until the rule is revoked", async () => {
  const harness = createHarness(output("allow"));
  const message = "这里讨论敏感词";
  const plan = harness.service.prepare(message);
  assert.equal(plan.kind, "review");
  if (plan.kind !== "review") return;
  await harness.service.review(plan, "198.51.100.8");
  assert.equal(harness.service.prepare(message).kind, "allow");

  const review = harness.state.listReviews("pending", undefined, 10).items[0]!;
  const rejected = harness.service.rejectReviewCandidate(review.id, "context", "人工确认违规");
  assert.ok(rejected);
  assert.deepEqual(harness.service.prepare(message), { kind: "block", source: "permanent" });
  assert.equal(harness.service.getMetrics().permanentBlockHits, 1);

  assert.equal(harness.state.revokeBlockRule(rejected!.rule.id), true);
  assert.equal(harness.service.prepare(message).kind, "review");
});

test("exact and context safe caches are isolated by moderation surface", async () => {
  const harness = createHarness(output("allow"));
  const message = "敏感词";
  const roomPlan = harness.service.prepare(message, "room_name");
  assert.equal(roomPlan.kind, "review");
  if (roomPlan.kind !== "review") return;
  await harness.service.review(roomPlan, "198.51.100.9");
  assert.deepEqual(harness.service.prepare(message, "room_name"), {
    kind: "allow",
    source: "exact_cache",
  });
  assert.equal(harness.service.prepare(message, "player_name").kind, "review");
  assert.equal(harness.service.prepare(message, "chat_message").kind, "review");
});

test("term blacklist overrides allow rules and applies to names", async () => {
  const harness = createHarness(output("allow"));
  const plan = harness.service.prepare("敏感词");
  assert.equal(plan.kind, "review");
  if (plan.kind !== "review") return;
  await harness.service.review(plan, "198.51.100.10");
  const review = harness.state.listReviews("pending", undefined, 10).items[0]!;
  harness.state.approveReview(review.id, "term");
  harness.service.rejectReviewCandidate(review.id, "term", "最终判定违规");

  assert.deepEqual(harness.service.prepare("敏感词"), { kind: "block", source: "permanent" });
  const name = "敏感词玩家";
  assert.equal(
    harness.service.areNameMatchesPermanentlyAllowed(
      name,
      harness.moderation.findSensitiveMatches(name),
    ),
    false,
  );
});
