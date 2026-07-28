import type { SensitiveMatch } from "./filter.js";
import type { ModerationContextCandidate, PreparedModerationMatch } from "./ai-types.js";

const SENTENCE_BOUNDARY = /[。！？!?；;\n]/u;

export function normalizeModerationContext(value: string): string {
  return value
    .normalize("NFKC")
    .toLowerCase()
    .replace(/[\p{White_Space}]+/gu, " ")
    .trim();
}

export function prepareModerationMatches(
  text: string,
  matches: readonly SensitiveMatch[],
  signContext: (normalized: string) => string,
): PreparedModerationMatch[] {
  return matches.map((match, index) => {
    const candidate = boundedSentence(text, match.start, match.end);
    const normalizedText = normalizeModerationContext(candidate);
    const context: ModerationContextCandidate = {
      id: `context_${index + 1}`,
      text: candidate,
      normalizedText,
      signature: signContext(normalizedText),
    };
    return {
      matchId: `match_${index + 1}`,
      match,
      context,
    };
  });
}

function boundedSentence(text: string, start: number, end: number): string {
  let from = start;
  while (from > 0) {
    const previous = text[from - 1];
    if (previous === undefined || SENTENCE_BOUNDARY.test(previous)) break;
    from -= 1;
  }
  let to = end;
  while (to < text.length) {
    const current = text[to];
    if (current === undefined) break;
    to += 1;
    if (SENTENCE_BOUNDARY.test(current)) break;
  }
  const raw = text.slice(from, to).trim();
  const scalars = Array.from(raw);
  if (scalars.length <= 160) return raw;

  const prefixLength = Array.from(text.slice(from, start)).length;
  const matchLength = Math.max(1, Array.from(text.slice(start, end)).length);
  const windowStart = Math.max(0, Math.min(prefixLength - 72, scalars.length - 160));
  const windowEnd = Math.min(scalars.length, Math.max(windowStart + 160, prefixLength + matchLength + 72));
  return scalars.slice(Math.max(0, windowEnd - 160), windowEnd).join("");
}
