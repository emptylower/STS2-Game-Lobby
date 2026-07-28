import assert from "node:assert/strict";
import { createServer } from "node:http";
import { mkdirSync, mkdtempSync, readFileSync, rmSync } from "node:fs";
import type { AddressInfo } from "node:net";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { createLobbyService } from "../app.js";
import { loadLobbyServiceConfig } from "../config.js";
import { hashServerAdminPassword } from "../server-admin-auth.js";
import { AiModerationStateStore } from "./state-store.js";
import type { ModerationModelResult, PreparedModerationMatch } from "./ai-types.js";

test("AI moderation admin API enforces auth and CSRF while never returning plaintext keys", async () => {
  const dir = mkdtempSync(join(tmpdir(), "sts2-ai-admin-"));
  const lexicon = join(dir, "lexicon");
  mkdirSync(lexicon);
  const password = "ai-admin-password";
  const credentialKeyHex = "11".repeat(32);
  const aiStateFile = join(dir, "ai-state.json");
  const seededState = new AiModerationStateStore(aiStateFile, Buffer.from(credentialKeyHex, "hex"));
  const prepared: PreparedModerationMatch[] = [{
    matchId: "match_1",
    match: {
      termId: "term_test",
      normalizedTerm: "敏感词",
      displayTerm: "敏感词",
      category: "test",
      source: "test.txt",
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
  const allowed: ModerationModelResult = {
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
  seededState.recordAllowed(
    "这是对敏感词的讨论",
    prepared,
    allowed,
    "openai_responses",
    "seed-model",
    "strict",
  );
  const preparedForRejection: PreparedModerationMatch[] = [{
    ...prepared[0]!,
    matchId: "match_2",
    context: {
      id: "context_2",
      text: "违规使用敏感词",
      normalizedText: "违规使用敏感词",
      signature: "blocked-context-signature",
    },
  }];
  seededState.recordAllowed(
    "违规使用敏感词",
    preparedForRejection,
    {
      ...allowed,
      matches: [{
        matchId: "match_2",
        verdict: "allow",
        usage: "quoted_reference",
        contextCandidateId: "context_2",
      }],
    },
    "openai_responses",
    "seed-model",
    "strict",
  );
  const seededReviews = seededState.listReviews("pending", undefined, 10).items;
  const reviewId = seededReviews.find((item) => item.contextSignature === "context-signature")!.id;
  const rejectedReviewId = seededReviews.find((item) => item.contextSignature === "blocked-context-signature")!.id;

  const modelServer = createServer((_request, response) => {
    response.setHeader("content-type", "application/json");
    response.end(JSON.stringify({
      status: "completed",
      output: [{ content: [{ type: "output_text", text: JSON.stringify(allowed) }] }],
    }));
  });
  await new Promise<void>((resolve) => modelServer.listen(0, "127.0.0.1", resolve));
  const modelAddress = modelServer.address() as AddressInfo;
  const config = loadLobbyServiceConfig({
    HOST: "127.0.0.1",
    PORT: "0",
    PEER_NETWORK_ENABLED: "false",
    PEER_SELF_ADDRESS: "",
    PEER_CF_DISCOVERY_BASE_URL: "",
    PEER_STATE_DIR: join(dir, "peer"),
    SENSITIVE_LEXICON_DIR: lexicon,
    SERVER_ADMIN_STATE_FILE: join(dir, "server-admin.json"),
    SERVER_ADMIN_PASSWORD_HASH: hashServerAdminPassword(password),
    SERVER_ADMIN_SESSION_SECRET: "ai-admin-session-secret",
    AI_MODERATION_CREDENTIAL_KEY: credentialKeyHex,
    AI_MODERATION_STATE_FILE: aiStateFile,
    AI_MODERATION_CACHE_FILE: join(dir, "ai-cache.json"),
  });
  const service = await createLobbyService(config);
  const address = await service.start();
  const base = `http://127.0.0.1:${address.port}`;
  try {
    assert.equal((await fetch(`${base}/server-admin/moderation/ai`)).status, 401);
    const login = await fetch(`${base}/server-admin/login`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ username: "admin", password }),
    });
    assert.equal(login.status, 200);
    const session = await login.json() as { csrfToken: string };
    const cookie = login.headers.get("set-cookie")?.split(";", 1)[0];
    assert.ok(cookie);

    const patchBody = {
      enabled: true,
      protocol: "openai_responses",
      endpoint: "https://api.example/v1/responses",
      model: "semantic-model",
      allowPrivateNetwork: false,
      jsonFallbackEnabled: true,
      apiKeyAction: "replace",
      apiKey: "plain-secret-api-key",
    };
    const missingCsrf = await fetch(`${base}/server-admin/moderation/ai`, {
      method: "PATCH",
      headers: { cookie, "content-type": "application/json" },
      body: JSON.stringify(patchBody),
    });
    assert.equal(missingCsrf.status, 403);

    const insecureEndpoint = await fetch(`${base}/server-admin/moderation/ai`, {
      method: "PATCH",
      headers: { cookie, "content-type": "application/json", "x-csrf-token": session.csrfToken },
      body: JSON.stringify({ endpoint: "http://127.0.0.1/v1/responses", allowPrivateNetwork: false }),
    });
    assert.equal(insecureEndpoint.status, 400);

    const updated = await fetch(`${base}/server-admin/moderation/ai`, {
      method: "PATCH",
      headers: { cookie, "content-type": "application/json", "x-csrf-token": session.csrfToken },
      body: JSON.stringify(patchBody),
    });
    assert.equal(updated.status, 200);
    const updatedText = await updated.text();
    assert.equal(updatedText.includes("plain-secret-api-key"), false);
    const updatedBody = JSON.parse(updatedText) as {
      config: { apiKeyConfigured: boolean; endpoint: string };
    };
    assert.equal(updatedBody.config.apiKeyConfigured, true);
    assert.equal(updatedBody.config.endpoint, patchBody.endpoint);
    assert.equal(readFileSync(config.aiModeration.stateFile, "utf8").includes("plain-secret-api-key"), false);

    const tested = await fetch(`${base}/server-admin/moderation/ai/test`, {
      method: "POST",
      headers: { cookie, "content-type": "application/json", "x-csrf-token": session.csrfToken },
      body: JSON.stringify({
        protocol: "openai_responses",
        endpoint: `http://127.0.0.1:${modelAddress.port}/v1/responses`,
        model: "temporary-test-model",
        allowPrivateNetwork: true,
        jsonFallbackEnabled: false,
        apiKey: "temporary-test-api-key",
      }),
    });
    assert.equal(tested.status, 200);
    const testedBody = await tested.json() as {
      ok: boolean;
      decision: string;
      structuredMode: string;
      latencyMs: number;
    };
    assert.equal(testedBody.ok, true);
    assert.equal(testedBody.decision, "allow");
    assert.equal(testedBody.structuredMode, "strict");
    assert.equal(Number.isFinite(testedBody.latencyMs), true);

    const reviews = await fetch(`${base}/server-admin/moderation/reviews?status=pending&limit=10`, {
      headers: { cookie },
    });
    assert.equal(reviews.status, 200);
    const reviewsBody = await reviews.json() as {
      items: Array<{ id: string; aiUsage: string; evidenceAvailable: boolean }>;
      nextCursor: string | null;
    };
    const listedReview = reviewsBody.items.find((item) => item.id === reviewId);
    assert.ok(listedReview);
    assert.equal(listedReview.aiUsage, "quoted_reference");
    assert.equal(listedReview.evidenceAvailable, true);

    const detail = await fetch(`${base}/server-admin/moderation/reviews/${reviewId}`, {
      headers: { cookie },
    });
    assert.equal(detail.status, 200);
    const detailBody = await detail.json() as { evidence: { message: string } | null };
    assert.equal(detailBody.evidence?.message, "这是对敏感词的讨论");

    const approved = await fetch(`${base}/server-admin/moderation/reviews/${reviewId}/approve`, {
      method: "POST",
      headers: { cookie, "content-type": "application/json", "x-csrf-token": session.csrfToken },
      body: JSON.stringify({ scope: "context", note: "集成测试批准" }),
    });
    assert.equal(approved.status, 200);
    const approvedBody = await approved.json() as { rule: { id: string; scope: string } };
    assert.equal(approvedBody.rule.scope, "context");

    const allowlist = await fetch(`${base}/server-admin/moderation/allowlist?scope=context`, {
      headers: { cookie },
    });
    assert.equal(allowlist.status, 200);
    const allowlistBody = await allowlist.json() as { items: Array<{ id: string }> };
    assert.equal(allowlistBody.items[0]?.id, approvedBody.rule.id);

    const revoked = await fetch(`${base}/server-admin/moderation/allowlist/${approvedBody.rule.id}`, {
      method: "DELETE",
      headers: { cookie, "x-csrf-token": session.csrfToken },
    });
    assert.equal(revoked.status, 200);

    const rejected = await fetch(`${base}/server-admin/moderation/reviews/${rejectedReviewId}/reject`, {
      method: "POST",
      headers: { cookie, "content-type": "application/json", "x-csrf-token": session.csrfToken },
      body: JSON.stringify({ scope: "context", note: "集成测试拒绝" }),
    });
    assert.equal(rejected.status, 200);
    const rejectedBody = await rejected.json() as {
      review: { status: string };
      rule: { id: string; scope: string; surface: string };
    };
    assert.equal(rejectedBody.review.status, "rejected");
    assert.equal(rejectedBody.rule.scope, "context");
    assert.equal(rejectedBody.rule.surface, "chat_message");

    const blocklist = await fetch(`${base}/server-admin/moderation/blocklist?scope=context`, {
      headers: { cookie },
    });
    assert.equal(blocklist.status, 200);
    const blocklistBody = await blocklist.json() as { items: Array<{ id: string }> };
    assert.equal(blocklistBody.items[0]?.id, rejectedBody.rule.id);

    const revokedBlock = await fetch(`${base}/server-admin/moderation/blocklist/${rejectedBody.rule.id}`, {
      method: "DELETE",
      headers: { cookie, "x-csrf-token": session.csrfToken },
    });
    assert.equal(revokedBlock.status, 200);
    const persistedText = readFileSync(config.aiModeration.stateFile, "utf8");
    assert.equal(persistedText.includes("temporary-test-api-key"), false);
    assert.equal(persistedText.includes("temporary-test-model"), false);

    const revoke = await fetch(`${base}/server-admin/moderation/allowlist/missing`, {
      method: "DELETE",
      headers: { cookie, "x-csrf-token": session.csrfToken },
    });
    assert.equal(revoke.status, 404);
    const revokeBlock = await fetch(`${base}/server-admin/moderation/blocklist/missing`, {
      method: "DELETE",
      headers: { cookie, "x-csrf-token": session.csrfToken },
    });
    assert.equal(revokeBlock.status, 404);
  } finally {
    await service.close();
    modelServer.closeAllConnections();
    await new Promise<void>((resolve, reject) => modelServer.close((error) => error ? reject(error) : resolve()));
    rmSync(dir, { recursive: true, force: true });
  }
});
