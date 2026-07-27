import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { buildTrie, type TrieNode } from "./dfa.js";
import { normalizeForMatch } from "./normalize.js";

export interface LoadedLexicon {
  root: TrieNode;
  wordCount: number;
}

/**
 * 从目录加载全部 *.txt 词表并构建 Trie。
 * 词表词与输入文本过同一归一化管线；跳过空行与 # 注释行；跨文件去重。
 * 目录不可读时抛错，由调用方决定降级策略（fail-open）。
 */
export function loadLexicon(dir: string): LoadedLexicon {
  const words = new Map<string, string[]>();
  const fileNames = readdirSync(dir, { withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith(".txt"))
    .map((entry) => entry.name)
    .sort();
  for (const fileName of fileNames) {
    const content = readFileSync(join(dir, fileName), "utf8");
    for (const line of content.split(/\r?\n/)) {
      const trimmed = line.trim();
      if (trimmed === "" || trimmed.startsWith("#")) {
        continue;
      }
      const chars = normalizeForMatch(trimmed).chars;
      if (chars.length === 0) {
        continue;
      }
      const key = chars.join("");
      if (!words.has(key)) {
        words.set(key, chars);
      }
    }
  }
  return { root: buildTrie(words.values()), wordCount: words.size };
}
