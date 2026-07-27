/**
 * 敏感词匹配归一化器。
 *
 * 输入文本与词库词都经过同一管线，保证两端口径一致：
 * NFC → 全角转半角 → 英文小写折叠 → 剔除空白/标点/符号/零宽字符 → 连续重复字符压缩。
 *
 * 每个保留下来的归一化字符记录其在原文中的 UTF-16 code unit 区间，
 * 供打码时把命中区间映射回原文位置。
 */
export interface NormalizedText {
  /** 归一化后的字符序列（每个元素一个 Unicode scalar）。 */
  chars: string[];
  /** chars[i] 在原文中的起始下标（UTF-16 code unit）。 */
  originalStart: number[];
  /** chars[i] 在原文中的结束下标（不含；被压缩的重复字符会扩展该区间）。 */
  originalEnd: number[];
}

const STRIP_PATTERN = /[\p{White_Space}\p{P}\p{S}\p{Cf}\p{M}]/u;

export function normalizeForMatch(raw: string): NormalizedText {
  const nfc = raw.normalize("NFC");
  const chars: string[] = [];
  const originalStart: number[] = [];
  const originalEnd: number[] = [];
  let index = 0;
  let previousKept: string | undefined;
  while (index < nfc.length) {
    const codePoint = nfc.codePointAt(index);
    if (codePoint === undefined) {
      break;
    }
    const ch = String.fromCodePoint(codePoint);
    const mapped = mapChar(ch, codePoint);
    if (mapped !== undefined) {
      if (mapped === previousKept) {
        // 连续重复字符压缩：扩展上一个保留字符覆盖的原文区间。
        originalEnd[originalEnd.length - 1] = index + ch.length;
      } else {
        chars.push(mapped);
        originalStart.push(index);
        originalEnd.push(index + ch.length);
        previousKept = mapped;
      }
    }
    index += ch.length;
  }
  return { chars, originalStart, originalEnd };
}

function mapChar(ch: string, codePoint: number): string | undefined {
  if (STRIP_PATTERN.test(ch)) {
    return undefined;
  }
  let mapped = ch;
  if (codePoint >= 0xff01 && codePoint <= 0xff5e) {
    mapped = String.fromCodePoint(codePoint - 0xfee0);
  }
  const lower = mapped.toLowerCase();
  const lowerChars = Array.from(lower);
  const first = lowerChars[0];
  return lowerChars.length === 1 && first !== undefined ? first : mapped;
}
