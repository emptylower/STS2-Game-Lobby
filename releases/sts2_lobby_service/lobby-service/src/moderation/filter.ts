import { findMatches, type MatchSpan, type TrieNode } from "./dfa.js";
import type { LexiconEntry } from "./lexicon-loader.js";
import { normalizeForMatch, type NormalizedText } from "./normalize.js";

const ASCII_ALNUM_PATTERN = /^[a-z0-9]$/;

function isAsciiAlnum(ch: string | undefined): boolean {
  return ch !== undefined && ASCII_ALNUM_PATTERN.test(ch);
}

/**
 * ASCII 词边界规则：命中词首/尾若是 ASCII 字母数字，则对应外侧相邻字符
 * 不得也是 ASCII 字母数字且与词在原文中直接相邻（无被剔除字符的间隙）。
 * 防 "av" 误伤 "have"；同时 "av movie"、"a v" 这类靠空白隔开的写法
 * 因间隙构成边界而依然命中。
 */
function spanRespectsAsciiBoundaries(normalized: NormalizedText, span: MatchSpan): boolean {
  const { chars, originalStart, originalEnd } = normalized;
  if (isAsciiAlnum(chars[span.startChar])) {
    const prevIndex = span.startChar - 1;
    if (
      isAsciiAlnum(chars[prevIndex])
      && originalEnd[prevIndex] === originalStart[span.startChar]
    ) {
      return false;
    }
  }
  const lastIndex = span.endChar - 1;
  if (isAsciiAlnum(chars[lastIndex])) {
    if (
      isAsciiAlnum(chars[span.endChar])
      && originalEnd[lastIndex] === originalStart[span.endChar]
    ) {
      return false;
    }
  }
  return true;
}

export interface MaskResult {
  text: string;
  masked: boolean;
}

export interface SensitiveMatch extends LexiconEntry {
  start: number;
  end: number;
}

export interface SensitiveWordFilter {
  contains(text: string): boolean;
  find(text: string): SensitiveMatch[];
  mask(text: string): MaskResult;
  readonly wordCount: number;
  readonly fingerprint: string;
}

/**
 * 基于词库 Trie 的敏感词过滤器。
 *
 * mask 为等量替换（每个 UTF-16 code unit 替换为一个 *）。下标契约：
 * normalizeForMatch 返回的 originalStart/originalEnd 是相对 NFC 归一化串的
 * 下标，因此 mask 内部先把输入转为 NFC 再套用 span——对已是 NFC 的输入
 * （聊天文本在 canonicalize 阶段已 NFC）输出与原串等长；非 NFC 输入则
 * 返回其 NFC 形式的打码结果，保证不错位。
 */
export class LexiconSensitiveWordFilter implements SensitiveWordFilter {
  constructor(
    private readonly root: TrieNode,
    readonly wordCount: number,
    private readonly entries: ReadonlyMap<string, LexiconEntry> = new Map(),
    readonly fingerprint = "",
  ) {}

  contains(text: string): boolean {
    return this.find(text).length > 0;
  }

  find(text: string): SensitiveMatch[] {
    const nfc = text.normalize("NFC");
    const normalized = normalizeForMatch(nfc);
    const candidates = findMatches(this.root, normalized.chars)
      .filter((span) => spanRespectsAsciiBoundaries(normalized, span))
      .sort((left, right) => left.startChar - right.startChar
        || (right.endChar - right.startChar) - (left.endChar - left.startChar));
    const selected: MatchSpan[] = [];
    let coveredUntil = -1;
    for (const span of candidates) {
      if (span.startChar < coveredUntil) continue;
      selected.push(span);
      coveredUntil = span.endChar;
    }
    return selected.flatMap((span) => {
      const start = normalized.originalStart[span.startChar];
      const end = normalized.originalEnd[span.endChar - 1];
      if (start === undefined || end === undefined) return [];
      const normalizedTerm = normalized.chars.slice(span.startChar, span.endChar).join("");
      const entry = this.entries.get(normalizedTerm) ?? {
        termId: `term_${normalizedTerm}`,
        normalizedTerm,
        displayTerm: nfc.slice(start, end),
        category: "unknown",
        source: "unknown",
      };
      return [{ ...entry, start, end }];
    });
  }

  mask(text: string): MaskResult {
    const nfc = text.normalize("NFC");
    const normalized = normalizeForMatch(nfc);
    const spans = findMatches(this.root, normalized.chars)
      .filter((span) => spanRespectsAsciiBoundaries(normalized, span));
    if (spans.length === 0) {
      return { text, masked: false };
    }
    const cut = new Uint8Array(nfc.length);
    for (const span of spans) {
      const from = normalized.originalStart[span.startChar];
      const lastEnd = normalized.originalEnd[span.endChar - 1];
      if (from === undefined || lastEnd === undefined) {
        continue;
      }
      for (let index = from; index < lastEnd; index += 1) {
        cut[index] = 1;
      }
    }
    let masked = "";
    for (let index = 0; index < nfc.length; index += 1) {
      masked += cut[index] === 1 ? "*" : nfc[index];
    }
    return { text: masked, masked: true };
  }
}

/** 开关关闭或词库不可用时的 no-op 实现。 */
export const nullSensitiveWordFilter: SensitiveWordFilter = {
  wordCount: 0,
  fingerprint: "",
  contains: () => false,
  find: () => [],
  mask: (text: string) => ({ text, masked: false }),
};
