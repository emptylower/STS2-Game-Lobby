import { findMatches, type TrieNode } from "./dfa.js";
import { normalizeForMatch } from "./normalize.js";

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
    return findMatches(this.root, normalizeForMatch(text).chars).length > 0;
  }

  mask(text: string): MaskResult {
    const nfc = text.normalize("NFC");
    const normalized = normalizeForMatch(nfc);
    const spans = findMatches(this.root, normalized.chars);
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
