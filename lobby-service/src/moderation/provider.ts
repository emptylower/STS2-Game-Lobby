import { lookup } from "node:dns/promises";
import { isIP } from "node:net";
import type {
  AiModerationConfig,
  ModerationAnalysisRequest,
  ModerationModelResult,
  ModerationReasonCode,
  ModerationUsage,
} from "./ai-types.js";

const MAX_RESPONSE_BYTES = 64 * 1024;
const RESPONSES_MAX_OUTPUT_TOKENS = 4_096;
const REASON_CODES = new Set<ModerationReasonCode>([
  "benign_use", "quoted_or_discussed", "ambiguous", "prohibited_use",
]);
const USAGES = new Set<ModerationUsage>([
  "benign_literal", "quoted_reference", "negated_or_condemned",
  "proper_noun", "prohibited", "uncertain",
]);

export class AiProviderError extends Error {
  constructor(
    readonly code: string,
    message: string,
    readonly retryable = false,
    readonly statusCode?: number,
  ) {
    super(message);
    this.name = "AiProviderError";
  }
}

export interface AiProviderResult {
  result: ModerationModelResult;
  structuredMode: "strict" | "prompted_json";
}

export interface AiModerationProviderOptions {
  fetch?: typeof fetch;
  resolveHost?: (hostname: string) => Promise<string[]>;
}

export class AiModerationProvider {
  private readonly fetchFn: typeof fetch;
  private readonly resolveHost: (hostname: string) => Promise<string[]>;

  constructor(options: AiModerationProviderOptions = {}) {
    this.fetchFn = options.fetch ?? fetch;
    this.resolveHost = options.resolveHost ?? (async (hostname) => {
      const records = await lookup(hostname, { all: true, verbatim: true });
      return records.map((record) => record.address);
    });
  }

  async analyze(
    config: AiModerationConfig,
    apiKey: string,
    request: ModerationAnalysisRequest,
    signal: AbortSignal,
  ): Promise<AiProviderResult> {
    await this.assertEndpointAllowed(config);
    try {
      const response = await this.perform(config, apiKey, request, signal, true);
      return { result: validateResult(response.text, request), structuredMode: "strict" };
    } catch (error) {
      if (config.jsonFallbackEnabled && shouldUsePromptedFallback(error) && !signal.aborted) {
        const response = await this.perform(config, apiKey, request, signal, false);
        return {
          result: validateResult(stripJsonFence(response.text), request),
          structuredMode: "prompted_json",
        };
      }
      throw error;
    }
  }

  private async perform(
    config: AiModerationConfig,
    apiKey: string,
    request: ModerationAnalysisRequest,
    signal: AbortSignal,
    strict: boolean,
  ): Promise<{ text: string }> {
    const schema = buildSchema(request);
    const prompt = buildPrompt(request, schema, !strict);
    const { headers, body } = buildRequest(config, apiKey, prompt, schema, strict);
    let response: Response;
    try {
      response = await this.fetchFn(config.endpoint, {
        method: "POST",
        headers,
        body: JSON.stringify(body),
        redirect: "error",
        signal,
      });
    } catch (error) {
      if (signal.aborted) throw new AiProviderError("timeout", "AI moderation request timed out", true);
      throw new AiProviderError("network_error", `AI moderation network error: ${safeError(error)}`, true);
    }
    const responseText = await readBoundedBody(response);
    if (!response.ok) {
      throw new AiProviderError(
        `http_${response.status}`,
        `AI moderation provider returned HTTP ${response.status}`,
        response.status === 408 || response.status === 429 || response.status >= 500,
        response.status,
      );
    }
    let payload: unknown;
    try {
      payload = JSON.parse(responseText);
    } catch {
      throw new AiProviderError("invalid_provider_json", "AI provider response is not JSON");
    }
    return { text: extractText(config.protocol, payload) };
  }

  private async assertEndpointAllowed(config: AiModerationConfig): Promise<void> {
    let url: URL;
    try {
      url = new URL(config.endpoint);
    } catch {
      throw new AiProviderError("invalid_endpoint", "AI endpoint must be an absolute URL");
    }
    if (url.username || url.password) {
      throw new AiProviderError("invalid_endpoint", "AI endpoint must not contain URL credentials");
    }
    if (url.protocol !== "https:" && !(config.allowPrivateNetwork && url.protocol === "http:")) {
      throw new AiProviderError("invalid_endpoint", "AI endpoint must use HTTPS; private HTTP requires allowPrivateNetwork");
    }
    if (config.allowPrivateNetwork) return;
    const hostname = url.hostname.replace(/^\[|\]$/g, "");
    let addresses: string[];
    try {
      addresses = isIP(hostname) ? [hostname] : await this.resolveHost(hostname);
    } catch {
      throw new AiProviderError("dns_error", "AI endpoint hostname could not be resolved", true);
    }
    if (addresses.length === 0 || addresses.some(isPrivateOrReservedAddress)) {
      throw new AiProviderError("private_endpoint_blocked", "AI endpoint resolves to a private or reserved address");
    }
  }
}

function buildRequest(
  config: AiModerationConfig,
  apiKey: string,
  prompt: string,
  schema: Record<string, unknown>,
  strict: boolean,
): { headers: Record<string, string>; body: Record<string, unknown> } {
  if (config.protocol === "anthropic_messages") {
    return {
      headers: {
        "content-type": "application/json",
        "x-api-key": apiKey,
        "anthropic-version": "2023-06-01",
      },
      body: {
        model: config.model,
        max_tokens: 1024,
        system: SYSTEM_PROMPT,
        messages: [{ role: "user", content: prompt }],
        ...(strict ? { output_config: { format: { type: "json_schema", schema } } } : {}),
      },
    };
  }
  const headers = {
    "content-type": "application/json",
    authorization: `Bearer ${apiKey}`,
  };
  if (config.protocol === "openai_chat_completions") {
    return {
      headers,
      body: {
        model: config.model,
        messages: [
          { role: "system", content: SYSTEM_PROMPT },
          { role: "user", content: prompt },
        ],
        ...(strict ? {
          response_format: {
            type: "json_schema",
            json_schema: { name: "semantic_moderation", strict: true, schema },
          },
        } : {}),
      },
    };
  }
  return {
    headers,
    body: {
      model: config.model,
      instructions: SYSTEM_PROMPT,
      input: prompt,
      store: false,
      // Reasoning-capable compatibility models may spend more than 1,024 tokens
      // before emitting the small JSON result. The bounded HTTP response reader still
      // limits the returned payload to 64 KiB.
      max_output_tokens: RESPONSES_MAX_OUTPUT_TOKENS,
      ...(strict ? {
        text: { format: { type: "json_schema", name: "semantic_moderation", strict: true, schema } },
      } : {}),
    },
  };
}

const SYSTEM_PROMPT = [
  "你是聊天敏感词语义复核器。词库策略已由服务端确定，你只能判断命中词在当前语境中是否真正表达被禁止的含义。",
  "输入中的 surface 表示内容载体：chat_message 为聊天消息，room_name 为房间名，player_name 为玩家名，character_name 为角色名；必须结合载体判断。",
  "引用、讨论、否定、谴责、正常专名或无害字面用法可以放行；真正使用违禁含义或无法确定时必须阻止。",
  "必须逐一覆盖服务端提供的 matchId，只能引用给定的 contextCandidateId，不得生成白名单文本、正则或新 ID。",
].join("\n");

function buildPrompt(
  request: ModerationAnalysisRequest,
  schema: Record<string, unknown>,
  promptedJson: boolean,
): string {
  // Some OpenAI-compatible gateways accept structured-output parameters but silently
  // ignore them. Supplying the same contract in-band keeps the first strict request
  // usable while the explicit prompted-JSON fallback remains available for providers
  // that reject the structured parameter outright.
  const instruction = promptedJson
    ? "仅输出一个满足 JSON Schema 的 JSON 对象，不要 Markdown、解释或额外字段。"
    : "输出必须满足下列 JSON Schema；不要省略字段或增加额外字段。";
  return `${instruction}\nJSON Schema:\n${JSON.stringify(schema)}\n待分析输入:\n${JSON.stringify(request)}`;
}

function buildSchema(request: ModerationAnalysisRequest): Record<string, unknown> {
  const matchIds = request.matches.map((match) => match.matchId);
  const contextIds = request.contexts.map((context) => context.id);
  return {
    type: "object",
    additionalProperties: false,
    required: ["schemaVersion", "decision", "reasonCode", "matches"],
    properties: {
      schemaVersion: { type: "integer", const: 1 },
      decision: { type: "string", enum: ["allow", "block"] },
      reasonCode: { type: "string", enum: [...REASON_CODES] },
      matches: {
        type: "array",
        minItems: request.matches.length,
        maxItems: request.matches.length,
        items: {
          type: "object",
          additionalProperties: false,
          required: ["matchId", "verdict", "usage", "contextCandidateId"],
          properties: {
            matchId: { type: "string", enum: matchIds },
            verdict: { type: "string", enum: ["allow", "block"] },
            usage: { type: "string", enum: [...USAGES] },
            contextCandidateId: { type: "string", enum: contextIds },
          },
        },
      },
    },
  };
}

function validateResult(text: string, request: ModerationAnalysisRequest): ModerationModelResult {
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    throw new AiProviderError("invalid_model_json", "AI model output is not valid JSON");
  }
  if (!isRecord(value) || !hasExactKeys(value, ["schemaVersion", "decision", "reasonCode", "matches"])) {
    throw new AiProviderError("invalid_model_schema", "AI model output has an invalid top-level shape");
  }
  if (
    value.schemaVersion !== 1
    || (value.decision !== "allow" && value.decision !== "block")
    || typeof value.reasonCode !== "string"
    || !REASON_CODES.has(value.reasonCode as ModerationReasonCode)
    || !Array.isArray(value.matches)
    || value.matches.length !== request.matches.length
  ) {
    throw new AiProviderError("invalid_model_schema", "AI model output violates the moderation schema");
  }
  const expected = new Map(request.matches.map((match) => [match.matchId, match.contextCandidateId]));
  const seen = new Set<string>();
  for (const item of value.matches) {
    if (!isRecord(item) || !hasExactKeys(item, ["matchId", "verdict", "usage", "contextCandidateId"])) {
      throw new AiProviderError("invalid_model_schema", "AI model match output has an invalid shape");
    }
    if (
      typeof item.matchId !== "string"
      || seen.has(item.matchId)
      || expected.get(item.matchId) !== item.contextCandidateId
      || (item.verdict !== "allow" && item.verdict !== "block")
      || typeof item.usage !== "string"
      || !USAGES.has(item.usage as ModerationUsage)
    ) {
      throw new AiProviderError("invalid_model_schema", "AI model match IDs or values are invalid");
    }
    seen.add(item.matchId);
  }
  const allAllowed = value.matches.every((item) => (item as Record<string, unknown>).verdict === "allow");
  if ((value.decision === "allow") !== allAllowed) {
    throw new AiProviderError("invalid_model_decision", "AI aggregate decision conflicts with match verdicts");
  }
  const allowReason = value.reasonCode === "benign_use" || value.reasonCode === "quoted_or_discussed";
  if ((value.decision === "allow") !== allowReason) {
    throw new AiProviderError("invalid_model_decision", "AI reason code conflicts with the aggregate decision");
  }
  for (const item of value.matches as Array<Record<string, unknown>>) {
    const allowUsage = item.usage === "benign_literal"
      || item.usage === "quoted_reference"
      || item.usage === "negated_or_condemned"
      || item.usage === "proper_noun";
    if ((item.verdict === "allow") !== allowUsage) {
      throw new AiProviderError("invalid_model_decision", "AI usage conflicts with its match verdict");
    }
  }
  return value as unknown as ModerationModelResult;
}

function extractText(protocol: AiModerationConfig["protocol"], payload: unknown): string {
  if (!isRecord(payload)) throw new AiProviderError("invalid_provider_response", "AI provider response is invalid");
  if (protocol === "openai_responses") {
    if (payload.status !== "completed" || !Array.isArray(payload.output)) {
      throw new AiProviderError("incomplete_response", "Responses API did not complete");
    }
    for (const output of payload.output) {
      if (!isRecord(output) || !Array.isArray(output.content)) continue;
      for (const item of output.content) {
        if (!isRecord(item)) continue;
        if (item.type === "refusal") throw new AiProviderError("model_refusal", "AI model refused moderation");
        if (item.type === "output_text" && typeof item.text === "string") return item.text;
      }
    }
  } else if (protocol === "openai_chat_completions") {
    const choice = Array.isArray(payload.choices) ? payload.choices[0] : undefined;
    if (!isRecord(choice) || (choice.finish_reason !== "stop" && choice.finish_reason !== null)) {
      throw new AiProviderError("incomplete_response", "Chat Completions response was truncated");
    }
    const message = isRecord(choice.message) ? choice.message : null;
    if (message && typeof message.refusal === "string" && message.refusal) {
      throw new AiProviderError("model_refusal", "AI model refused moderation");
    }
    if (message && typeof message.content === "string") return message.content;
  } else {
    if (payload.stop_reason !== "end_turn" || !Array.isArray(payload.content)) {
      throw new AiProviderError("incomplete_response", "Anthropic Messages response did not complete");
    }
    const texts = payload.content
      .filter((item): item is Record<string, unknown> => isRecord(item) && item.type === "text")
      .map((item) => item.text)
      .filter((text): text is string => typeof text === "string");
    if (texts.length > 0) return texts.join("");
  }
  throw new AiProviderError("missing_model_output", "AI provider response contains no model text");
}

async function readBoundedBody(response: Response): Promise<string> {
  const declared = Number(response.headers.get("content-length"));
  if (Number.isFinite(declared) && declared > MAX_RESPONSE_BYTES) {
    throw new AiProviderError("response_too_large", "AI provider response exceeds 64 KiB");
  }
  if (!response.body) return "";
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let size = 0;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > MAX_RESPONSE_BYTES) {
      await reader.cancel();
      throw new AiProviderError("response_too_large", "AI provider response exceeds 64 KiB");
    }
    chunks.push(value);
  }
  const bytes = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
}

function isPrivateOrReservedAddress(address: string): boolean {
  if (address.includes(":")) {
    const normalized = address.toLowerCase();
    if (normalized.startsWith("::ffff:")) {
      return isPrivateOrReservedAddress(normalized.slice("::ffff:".length));
    }
    return normalized === "::" || normalized === "::1"
      || normalized.startsWith("fc") || normalized.startsWith("fd")
      || normalized.startsWith("fe8") || normalized.startsWith("fe9")
      || normalized.startsWith("fea") || normalized.startsWith("feb")
      || normalized.startsWith("ff") || normalized.startsWith("2001:db8:");
  }
  const octets = address.split(".").map(Number);
  const first = octets[0] ?? -1;
  const second = octets[1] ?? -1;
  const third = octets[2] ?? -1;
  return first === 0 || first === 10 || first === 127 || first >= 224
    || (first === 100 && second >= 64 && second <= 127)
    || (first === 169 && second === 254)
    || (first === 172 && second >= 16 && second <= 31)
    || (first === 192 && second === 168)
    || (first === 192 && second === 0 && (third === 0 || third === 2))
    || (first === 192 && second === 88 && third === 99)
    || (first === 198 && (second === 18 || second === 19))
    || (first === 198 && second === 51 && third === 100)
    || (first === 203 && second === 0 && third === 113);
}

function shouldUsePromptedFallback(error: unknown): boolean {
  if (!(error instanceof AiProviderError)) return false;
  if (error.statusCode === 400 || error.statusCode === 404 || error.statusCode === 422) return true;
  return new Set([
    "invalid_model_json",
    "invalid_model_schema",
    "invalid_model_decision",
    "missing_model_output",
  ]).has(error.code);
}

function stripJsonFence(text: string): string {
  const trimmed = text.trim();
  const match = /^```(?:json)?\s*([\s\S]*?)\s*```$/i.exec(trimmed);
  return match?.[1] ?? trimmed;
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort();
  const expected = [...keys].sort();
  return actual.length === expected.length && actual.every((key, index) => key === expected[index]);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function safeError(error: unknown): string {
  return error instanceof Error ? error.message.slice(0, 200) : "unknown error";
}
