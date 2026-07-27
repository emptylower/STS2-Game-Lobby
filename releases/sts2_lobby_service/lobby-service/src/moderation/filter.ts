import { findMatches, type MatchSpan, type TrieNode } from "./dfa.js";
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

export interface SensitiveWordFilter {
  contains(text: string): boolean;
  mask(text: string): MaskResult;
  readonly wordCount: number;
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
  ) {}

  contains(text: string): boolean {
    const normalized = normalizeForMatch(text);
    return findMatches(this.root, normalized.chars)
      .some((span) => spanRespectsAsciiBoundaries(normalized, span));
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
  contains: () => false,
  mask: (text: string) => ({ text, masked: false }),
};
