import assert from "node:assert/strict";
import test from "node:test";
import { AiModerationProvider, AiProviderError } from "./provider.js";
import type { AiModerationConfig, ModerationAnalysisRequest } from "./ai-types.js";

const request: ModerationAnalysisRequest = {
  surface: "chat_message",
  message: "这是测试词",
  contexts: [{ id: "context_1", text: "这是测试词", normalizedText: "这是测试词", signature: "sig" }],
  matches: [{
    matchId: "match_1",
    termId: "term_1",
    term: "测试词",
    category: "misc",
    source: "misc.txt",
    start: 2,
    end: 5,
    contextCandidateId: "context_1",
  }],
};

const allowed = JSON.stringify({
  schemaVersion: 1,
  decision: "allow",
  reasonCode: "quoted_or_discussed",
  matches: [{
    matchId: "match_1",
    verdict: "allow",
    usage: "quoted_reference",
    contextCandidateId: "context_1",
  }],
});

function config(protocol: AiModerationConfig["protocol"]): AiModerationConfig {
  return {
    enabled: true,
    protocol,
    endpoint: "http://127.0.0.1/v1/test",
    model: "test-model",
    allowPrivateNetwork: true,
    jsonFallbackEnabled: false,
  };
}

test("provider emits strict structured output requests for all supported protocols", async () => {
  for (const protocol of ["openai_responses", "openai_chat_completions", "anthropic_messages"] as const) {
    let sentBody: Record<string, unknown> | undefined;
    let sentHeaders: Headers | undefined;
    const provider = new AiModerationProvider({
      fetch: (async (_url, init) => {
        sentBody = JSON.parse(String(init?.body)) as Record<string, unknown>;
        sentHeaders = new Headers(init?.headers);
        const payload = protocol === "openai_responses"
          ? { status: "completed", output: [{ content: [{ type: "output_text", text: allowed }] }] }
          : protocol === "openai_chat_completions"
            ? { choices: [{ finish_reason: "stop", message: { content: allowed } }] }
            : { stop_reason: "end_turn", content: [{ type: "text", text: allowed }] };
        return new Response(JSON.stringify(payload), { status: 200 });
      }) as typeof fetch,
    });
    const result = await provider.analyze(config(protocol), "secret-key", request, new AbortController().signal);
    assert.equal(result.result.decision, "allow");
    assert.equal(result.structuredMode, "strict");
    if (protocol === "openai_responses") {
      assert.equal(sentHeaders?.get("authorization"), "Bearer secret-key");
      assert.equal(((sentBody?.text as Record<string, unknown>).format as Record<string, unknown>).type, "json_schema");
      assert.equal(sentBody?.max_output_tokens, 4096);
      assert.match(String(sentBody?.input), /JSON Schema:/);
      assert.match(String(sentBody?.input), /schemaVersion/);
    } else if (protocol === "openai_chat_completions") {
      assert.equal(((sentBody?.response_format as Record<string, unknown>).json_schema as Record<string, unknown>).strict, true);
      const messages = sentBody?.messages as Array<Record<string, unknown>>;
      assert.match(String(messages[1]?.content), /JSON Schema:/);
      assert.match(String(messages[1]?.content), /schemaVersion/);
    } else {
      assert.equal(sentHeaders?.get("x-api-key"), "secret-key");
      assert.equal((((sentBody?.output_config as Record<string, unknown>).format as Record<string, unknown>).type), "json_schema");
      const messages = sentBody?.messages as Array<Record<string, unknown>>;
      assert.match(String(messages[0]?.content), /JSON Schema:/);
      assert.match(String(messages[0]?.content), /schemaVersion/);
    }
  }
});

test("provider falls back to prompted JSON only when explicitly enabled", async () => {
  let calls = 0;
  const provider = new AiModerationProvider({
    fetch: (async () => {
      calls += 1;
      if (calls === 1) return new Response("unsupported", { status: 400 });
      return new Response(JSON.stringify({
        choices: [{ finish_reason: "stop", message: { content: `\`\`\`json\n${allowed}\n\`\`\`` } }],
      }), { status: 200 });
    }) as typeof fetch,
  });
  const fallbackConfig = { ...config("openai_chat_completions"), jsonFallbackEnabled: true };
  const result = await provider.analyze(fallbackConfig, "key", request, new AbortController().signal);
  assert.equal(calls, 2);
  assert.equal(result.structuredMode, "prompted_json");
});

test("provider rejects private endpoints unless explicitly allowed", async () => {
  const provider = new AiModerationProvider({
    resolveHost: async () => ["127.0.0.1"],
    fetch: (async () => { throw new Error("must not fetch"); }) as typeof fetch,
  });
  await assert.rejects(
    () => provider.analyze({
      ...config("openai_responses"),
      endpoint: "https://model.example/v1/responses",
      allowPrivateNetwork: false,
    }, "key", request, new AbortController().signal),
    (error: unknown) => error instanceof AiProviderError && error.code === "private_endpoint_blocked",
  );
});

test("provider validates exact match coverage and aggregate decisions", async () => {
  const provider = new AiModerationProvider({
    fetch: (async () => new Response(JSON.stringify({
      status: "completed",
      output: [{ content: [{ type: "output_text", text: JSON.stringify({
        schemaVersion: 1,
        decision: "allow",
        reasonCode: "benign_use",
        matches: [],
      }) }] }],
    }), { status: 200 })) as typeof fetch,
  });
  await assert.rejects(
    () => provider.analyze(config("openai_responses"), "key", request, new AbortController().signal),
    (error: unknown) => error instanceof AiProviderError && error.code === "invalid_model_schema",
  );
});

test("provider rejects reason and usage values that contradict an allow verdict", async () => {
  for (const invalid of [
    {
      schemaVersion: 1,
      decision: "allow",
      reasonCode: "prohibited_use",
      matches: [{
        matchId: "match_1",
        verdict: "allow",
        usage: "quoted_reference",
        contextCandidateId: "context_1",
      }],
    },
    {
      schemaVersion: 1,
      decision: "allow",
      reasonCode: "benign_use",
      matches: [{
        matchId: "match_1",
        verdict: "allow",
        usage: "prohibited",
        contextCandidateId: "context_1",
      }],
    },
  ]) {
    const provider = new AiModerationProvider({
      fetch: (async () => new Response(JSON.stringify({
        status: "completed",
        output: [{ content: [{ type: "output_text", text: JSON.stringify(invalid) }] }],
      }), { status: 200 })) as typeof fetch,
    });
    await assert.rejects(
      () => provider.analyze(config("openai_responses"), "key", request, new AbortController().signal),
      (error: unknown) => error instanceof AiProviderError && error.code === "invalid_model_decision",
    );
  }
});

test("provider rejects refusals, truncated output, and responses larger than 64 KiB", async () => {
  const cases: Array<{ payload: unknown; code: string }> = [
    {
      payload: { status: "completed", output: [{ content: [{ type: "refusal", refusal: "no" }] }] },
      code: "model_refusal",
    },
    {
      payload: { status: "incomplete", output: [] },
      code: "incomplete_response",
    },
  ];
  for (const item of cases) {
    const provider = new AiModerationProvider({
      fetch: (async () => new Response(JSON.stringify(item.payload), { status: 200 })) as typeof fetch,
    });
    await assert.rejects(
      () => provider.analyze(config("openai_responses"), "key", request, new AbortController().signal),
      (error: unknown) => error instanceof AiProviderError && error.code === item.code,
    );
  }

  const oversized = new AiModerationProvider({
    fetch: (async () => new Response("x".repeat(65 * 1024), { status: 200 })) as typeof fetch,
  });
  await assert.rejects(
    () => oversized.analyze(config("openai_responses"), "key", request, new AbortController().signal),
    (error: unknown) => error instanceof AiProviderError && error.code === "response_too_large",
  );
});
