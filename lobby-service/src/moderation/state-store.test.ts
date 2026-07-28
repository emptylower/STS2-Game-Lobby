import assert from "node:assert/strict";
import { randomBytes } from "node:crypto";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { AiModerationStateStore } from "./state-store.js";
import type { ModerationModelResult, PreparedModerationMatch } from "./ai-types.js";

function prepared(): PreparedModerationMatch[] {
  return [{
    matchId: "match_1",
    match: {
      termId: "term_1",
      normalizedTerm: "敏感词",
      displayTerm: "敏感词",
      category: "misc",
      source: "misc.txt",
      start: 2,
      end: 5,
    },
    context: {
      id: "context_1",
      text: "讨论敏感词",
      normalizedText: "讨论敏感词",
      signature: "context-signature",
    },
  }];
}

const result: ModerationModelResult = {
  schemaVersion: 1,
  decision: "allow",
  reasonCode: "quoted_or_discussed",
  matches: [{
    matchId: "match_1",
    verdict: "allow",
    usage: "quoted_reference",
    contextCandidateId: "context_1",
  }],
};

test("state encrypts API keys and evidence, then supports approve and revoke", () => {
  const dir = mkdtempSync(join(tmpdir(), "sts2-ai-state-"));
  const path = join(dir, "state.json");
  const key = randomBytes(32);
  const store = new AiModerationStateStore(path, key, () => Date.parse("2026-07-28T00:00:00Z"));
  store.updateConfig({
    enabled: true,
    protocol: "openai_responses",
    endpoint: "https://api.example/v1/responses",
    model: "model",
    allowPrivateNetwork: false,
    jsonFallbackEnabled: false,
  }, "replace", "plaintext-api-key");
  assert.equal(store.getApiKey(), "plaintext-api-key");
  assert.equal(readFileSync(path, "utf8").includes("plaintext-api-key"), false);

  store.recordAllowed("这是对敏感词的讨论", prepared(), result, "openai_responses", "model", "strict");
  const listed = store.listReviews("pending", undefined, 20);
  assert.equal(listed.items.length, 1);
  const id = listed.items[0]!.id;
  assert.deepEqual(store.getReview(id)?.evidence, {
    message: "这是对敏感词的讨论",
    context: "讨论敏感词",
  });
  assert.equal(store.getReview(id)?.aiVerdict, "allow");
  assert.equal(store.getReview(id)?.aiUsage, "quoted_reference");
  assert.equal(store.getReview(id)?.aiContextCandidateId, "context_1");
  assert.equal(store.getReview(id)?.structuredMode, "strict");
  assert.equal(readFileSync(path, "utf8").includes("这是对敏感词的讨论"), false);

  const rule = store.approveReview(id, "context", "人工确认");
  assert.equal(rule?.scope, "context");
  assert.equal(store.getReview(id)?.evidence, null);
  assert.equal(store.isPermanentlyAllowed(prepared()[0]!.match, "context-signature"), true);
  assert.equal(store.revokeAllowRule(rule!.id), true);
  assert.equal(store.isPermanentlyAllowed(prepared()[0]!.match, "context-signature"), false);
  assert.equal(store.getReview(id)?.status, "pending");
});

test("state reports decrypt errors when the master key changes", () => {
  const dir = mkdtempSync(join(tmpdir(), "sts2-ai-state-key-"));
  const path = join(dir, "state.json");
  const store = new AiModerationStateStore(path, randomBytes(32));
  store.updateConfig(store.getConfig(), "replace", "secret");
  const reopened = new AiModerationStateStore(path, randomBytes(32));
  assert.equal(reopened.getConfigView().credentialStatus, "decrypt_error");
  assert.throws(() => reopened.getApiKey());
});

test("processed or expired evidence is removed while aggregate observations persist", () => {
  const dir = mkdtempSync(join(tmpdir(), "sts2-ai-state-expiry-"));
  const path = join(dir, "state.json");
  let now = Date.parse("2026-07-28T00:00:00Z");
  const store = new AiModerationStateStore(path, randomBytes(32), () => now);
  store.recordAllowed("message one", prepared(), result, "openai_responses", "model", "strict");
  store.recordAllowed("message two", prepared(), result, "openai_responses", "model", "prompted_json");
  assert.equal(store.listReviews("pending", undefined, 10).items[0]?.observations, 2);
  assert.equal(store.listReviews("pending", undefined, 10).items[0]?.structuredMode, "prompted_json");
  now += 31 * 24 * 60 * 60_000;
  const review = store.listReviews("pending", undefined, 10).items[0];
  assert.equal(review?.evidenceAvailable, false);
  assert.equal(review?.observations, 2);
});

test("rejecting a review creates a persistent block rule that can be listed and revoked", () => {
  const dir = mkdtempSync(join(tmpdir(), "sts2-ai-state-block-"));
  const path = join(dir, "state.json");
  const key = randomBytes(32);
  const store = new AiModerationStateStore(path, key, () => Date.parse("2026-07-28T01:00:00Z"));
  store.recordAllowed("这是对敏感词的讨论", prepared(), result, "openai_responses", "model", "strict");
  const id = store.listReviews("pending", undefined, 10).items[0]!.id;

  const rejected = store.rejectReview(id, "context", "AI 误放行");
  assert.equal(rejected?.review.status, "rejected");
  assert.equal(rejected?.rule.scope, "context");
  assert.equal(rejected?.rule.contextSignature, "context-signature");
  assert.equal(store.getReview(id)?.evidence, null);
  assert.equal(store.isPermanentlyBlocked(prepared()[0]!.match, "context-signature"), true);
  assert.equal(store.isPermanentlyBlocked(prepared()[0]!.match, "different-context"), false);
  assert.equal(store.stats().blockRules, 1);
  assert.equal(store.listBlocklist(undefined, 10, "context").items[0]?.id, rejected?.rule.id);
  assert.equal(JSON.parse(readFileSync(path, "utf8")).version, 2);

  const reopened = new AiModerationStateStore(path, key);
  assert.equal(reopened.isPermanentlyBlocked(prepared()[0]!.match, "context-signature"), true);
  assert.equal(reopened.revokeBlockRule(rejected!.rule.id), true);
  assert.equal(reopened.isPermanentlyBlocked(prepared()[0]!.match, "context-signature"), false);
  assert.equal(reopened.getReview(id)?.status, "pending");
  assert.equal(reopened.getReview(id)?.evidence, null);
  reopened.recordAllowed("撤销后再次观察敏感词", prepared(), result, "openai_responses", "model", "strict");
  assert.equal(reopened.getReview(id)?.evidence?.message, "撤销后再次观察敏感词");
});

test("version 1 state migrates without losing configuration or allow rules", () => {
  const dir = mkdtempSync(join(tmpdir(), "sts2-ai-state-v1-"));
  const path = join(dir, "state.json");
  writeFileSync(path, JSON.stringify({
    version: 1,
    config: {
      enabled: false,
      protocol: "openai_chat_completions",
      endpoint: "https://api.example/v1/chat/completions",
      model: "model",
      allowPrivateNetwork: false,
      jsonFallbackEnabled: true,
    },
    reviews: [],
    allowlist: [{
      id: "allow_existing",
      scope: "term",
      termId: "term_1",
      normalizedTerm: "敏感词",
      reviewId: "review_existing",
      createdAt: "2026-07-28T00:00:00.000Z",
    }],
  }), "utf8");

  const store = new AiModerationStateStore(path, randomBytes(32));
  assert.equal(store.getConfig().endpoint, "https://api.example/v1/chat/completions");
  assert.equal(store.listAllowlist(undefined, 10).items[0]?.id, "allow_existing");
  assert.equal(store.stats().blockRules, 0);
  store.updateConfig(store.getConfig(), "keep");
  assert.equal(JSON.parse(readFileSync(path, "utf8")).version, 2);
});
