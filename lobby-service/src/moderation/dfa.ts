/**
 * 敏感词 Trie 匹配器。
 *
 * 从归一化字符序列的每个起点逐字符扫描，复杂度 O(文本长 × 最长词长)。
 * 聊天上限 300 字符、名称 80 字符、词库数万条——无需 Aho-Corasick 失效指针。
 */
export interface TrieNode {
  children: Map<string, TrieNode>;
  terminal: boolean;
}

/** 命中区间，归一化字符下标 [startChar, endChar)。 */
export interface MatchSpan {
  startChar: number;
  endChar: number;
}

export function createTrieNode(): TrieNode {
  return { children: new Map(), terminal: false };
}

export function buildTrie(words: Iterable<string[]>): TrieNode {
  const root = createTrieNode();
  for (const chars of words) {
    if (chars.length === 0) {
      continue;
    }
    let node = root;
    for (const ch of chars) {
      let next = node.children.get(ch);
      if (next === undefined) {
        next = createTrieNode();
        node.children.set(ch, next);
      }
      node = next;
    }
    node.terminal = true;
  }
  return root;
}

export function findMatches(root: TrieNode, chars: string[]): MatchSpan[] {
  const spans: MatchSpan[] = [];
  for (let start = 0; start < chars.length; start += 1) {
    let node = root;
    let index = start;
    while (index < chars.length) {
      const ch = chars[index];
      if (ch === undefined) {
        break;
      }
      const next = node.children.get(ch);
      if (next === undefined) {
        break;
      }
      node = next;
      index += 1;
      if (node.terminal) {
        spans.push({ startChar: start, endChar: index });
      }
    }
  }
  return spans;
}
