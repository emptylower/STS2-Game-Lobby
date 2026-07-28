import type { SensitiveMatch } from "./filter.js";

export type AiModerationProtocol =
  | "openai_responses"
  | "openai_chat_completions"
  | "anthropic_messages";

export interface AiModerationConfig {
  enabled: boolean;
  protocol: AiModerationProtocol;
  endpoint: string;
  model: string;
  allowPrivateNetwork: boolean;
  jsonFallbackEnabled: boolean;
}

export interface ModerationContextCandidate {
  id: string;
  text: string;
  normalizedText: string;
  signature: string;
}

export interface ModerationModelMatch {
  matchId: string;
  termId: string;
  term: string;
  category: string;
  source: string;
  start: number;
  end: number;
  contextCandidateId: string;
}

export interface ModerationAnalysisRequest {
  surface: ModerationSurface;
  message: string;
  matches: ModerationModelMatch[];
  contexts: ModerationContextCandidate[];
}

export type ModerationSurface =
  | "chat_message"
  | "room_name"
  | "player_name"
  | "character_name";

export type ModerationReasonCode =
  | "benign_use"
  | "quoted_or_discussed"
  | "ambiguous"
  | "prohibited_use";

export type ModerationUsage =
  | "benign_literal"
  | "quoted_reference"
  | "negated_or_condemned"
  | "proper_noun"
  | "prohibited"
  | "uncertain";

export interface ModerationModelResult {
  schemaVersion: 1;
  decision: "allow" | "block";
  reasonCode: ModerationReasonCode;
  matches: Array<{
    matchId: string;
    verdict: "allow" | "block";
    usage: ModerationUsage;
    contextCandidateId: string;
  }>;
}

export interface PreparedModerationMatch {
  matchId: string;
  match: SensitiveMatch;
  context: ModerationContextCandidate;
}

export type AiReviewOutcome =
  | { kind: "allow"; result: ModerationModelResult; structuredMode: "strict" | "prompted_json" }
  | { kind: "block"; result: ModerationModelResult; structuredMode: "strict" | "prompted_json" }
  | { kind: "degraded"; code: string };

export const DEFAULT_AI_MODERATION_CONFIG: AiModerationConfig = {
  enabled: false,
  protocol: "openai_responses",
  endpoint: "",
  model: "",
  allowPrivateNetwork: false,
  jsonFallbackEnabled: false,
};
