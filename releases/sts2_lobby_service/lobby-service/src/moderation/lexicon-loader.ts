import { createHash } from "node:crypto";
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { buildTrie, type TrieNode } from "./dfa.js";
import { normalizeForMatch } from "./normalize.js";

export interface LoadedLexicon {
  root: TrieNode;
  wordCount: number;
  entries: ReadonlyMap<string, LexiconEntry>;
  fingerprint: string;
}

export interface LexiconEntry {
  termId: string;
  normalizedTerm: string;
  displayTerm: string;
  category: string;
  source: string;
}

/**
 * 从目录加载全部 *.txt 词表并构建 Trie。
 * 词表词与输入文本过同一归一化管线；跳过空行与 # 注释行；跨文件去重。
 * 目录不可读时抛错，由调用方决定降级策略（fail-open）。
 */
export function loadLexicon(dir: string): LoadedLexicon {
  const words = new Map<string, string[]>();
  const entries = new Map<string, LexiconEntry>();
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
      // 丢弃归一化后不足 2 字符的词：上游词表混入了大量单字符条目
      // （如 "1"、"日"、"妈"、"法"），单字符子串匹配在大厅场景几乎
      // 必然误报正常文本，全部按垃圾词处理。
      if (chars.length < 2) {
        continue;
      }
      const key = chars.join("");
      if (!words.has(key)) {
        words.set(key, chars);
        entries.set(key, {
          termId: `term_${createHash("sha256").update(key).digest("hex").slice(0, 24)}`,
          normalizedTerm: key,
          displayTerm: trimmed,
          category: fileName.slice(0, -4),
          source: fileName,
        });
      }
    }
  }
  const fingerprint = createHash("sha256")
    .update([...words.keys()].sort().join("\n"))
    .digest("hex");
  return { root: buildTrie(words.values()), wordCount: words.size, entries, fingerprint };
}
