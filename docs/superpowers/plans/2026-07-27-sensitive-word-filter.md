# 敏感词过滤（lobby-service 0.5.3）实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 lobby-service 中接入 konsheng/Sensitive-lexicon 词库，聊天消息命中敏感词时用 `*` 打码，用户名/房间名等名称类字段命中时拒绝（400 + 「包含敏感词内容，请修改后重试」），管理面板提供开关与状态展示。

**Architecture:** 新增 `src/moderation/` 纯函数模块（归一化器 + Trie 匹配器 + 可热切换的 ModerationService），词库 vendor 进 `lexicon/` 随包分发。聊天打码挂在 `chat/protocol.ts` 的 canonicalize 收口点（可选 `maskText` 参数），名称拒绝挂在 `app.ts` 现有 `boundedString` 验证链之后（抛 `InputError`）。开关状态持久化在 `ServerAdminStateStore`，面板 PATCH 即时生效。

**Tech Stack:** Node 20 + TypeScript strict ESM（`node:` 前缀、本地导入带 `.js` 后缀）、express 5、ws、node:test。**不引入任何新 npm 依赖。**

**Spec:** `docs/superpowers/specs/2026-07-27-sensitive-word-filter-design.md`

**通用约定：**
- 所有命令在 `lobby-service/` 目录下执行，除非另有说明。
- 单测运行方式：`npm run build` 后 `node --test dist/<对应路径>.test.js`；全量：`npm test`（自动 rm -rf dist && build && 跑全部）。
- 类型检查：`npm run check`。
- 每个 Task 结束按指示提交，提交信息用英文 conventional commits。

---

### Task 1: Vendor 词库快照（lexicon/）

**Files:**
- Create: `lexicon/politics.txt`、`lexicon/porn.txt`、`lexicon/violence.txt`、`lexicon/ads.txt`、`lexicon/misc.txt`、`lexicon/SOURCES.md`

- [ ] **Step 1: 拉取上游词库并记录 commit**

```bash
rm -rf /tmp/sensitive-lexicon && git clone --depth 1 https://github.com/konsheng/Sensitive-lexicon.git /tmp/sensitive-lexicon
git -C /tmp/sensitive-lexicon rev-parse HEAD
ls /tmp/sensitive-lexicon/Vocabulary/
```

预期：打印一个 40 位 commit hash（记录下来，Step 3 用）；`Vocabulary/` 下应有 `政治类型.txt`、`反动词库.txt`、`色情类型.txt`、`色情词库.txt`、`暴恐词库.txt`、`涉枪涉爆.txt`、`广告类型.txt`、`非法网址.txt`、`GFW补充词库.txt`、`补充词库.txt`、`其他词库.txt`、`网易前端过滤敏感词库.txt`、`零时-Tencent.txt` 等文件。若文件名与预期不符，按实际文件名调整下步映射并在 SOURCES.md 注明。

- [ ] **Step 2: 按类别合并去重生成 5 个词表文件**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby/lobby-service
mkdir -p lexicon
SL=/tmp/sensitive-lexicon/Vocabulary
cat "$SL/政治类型.txt" "$SL/反动词库.txt"        | sed 's/\r$//' | awk 'NF' | sort -u > lexicon/politics.txt
cat "$SL/色情类型.txt" "$SL/色情词库.txt"        | sed 's/\r$//' | awk 'NF' | sort -u > lexicon/porn.txt
cat "$SL/暴恐词库.txt" "$SL/涉枪涉爆.txt"        | sed 's/\r$//' | awk 'NF' | sort -u > lexicon/violence.txt
cat "$SL/广告类型.txt" "$SL/非法网址.txt"        | sed 's/\r$//' | awk 'NF' | sort -u > lexicon/ads.txt
cat "$SL/GFW补充词库.txt" "$SL/补充词库.txt" "$SL/其他词库.txt" "$SL/网易前端过滤敏感词库.txt" "$SL/零时-Tencent.txt" | sed 's/\r$//' | awk 'NF' | sort -u > lexicon/misc.txt
wc -l lexicon/*.txt
```

预期：5 个文件均非空（每个至少数百行）。明确排除：`COVID-19词库.txt`、`民生词库.txt`、`贪腐词库.txt`、`新思想启蒙.txt`（与游戏大厅场景相关度低、误报高）。

- [ ] **Step 3: 写 SOURCES.md**

创建 `lexicon/SOURCES.md`（把 `<COMMIT_HASH>` 替换为 Step 1 记录的 hash）：

```markdown
# 敏感词库来源说明

本目录词表快照自 https://github.com/konsheng/Sensitive-lexicon （MIT License）。

- 上游 commit: `<COMMIT_HASH>`
- 快照日期: 2026-07-27

## 类别映射

| 文件 | 上游来源（Vocabulary/） |
|---|---|
| politics.txt | 政治类型.txt + 反动词库.txt |
| porn.txt | 色情类型.txt + 色情词库.txt |
| violence.txt | 暴恐词库.txt + 涉枪涉爆.txt |
| ads.txt | 广告类型.txt + 非法网址.txt |
| misc.txt | GFW补充词库.txt + 补充词库.txt + 其他词库.txt + 网易前端过滤敏感词库.txt + 零时-Tencent.txt |

## 明确排除

COVID-19词库.txt、民生词库.txt、贪腐词库.txt、新思想启蒙.txt —— 与游戏大厅场景相关度低、误报率高。

## 处理方式

合并后按行去重（sort -u），每行一个词。运行时加载器会跳过空行与 `#` 开头的注释行，并对每个词做与输入文本一致的归一化（NFC、全半角、大小写折叠、符号剔除、重复压缩）。
```

- [ ] **Step 4: 提交**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git add lobby-service/lexicon
git commit -m "feat(lobby): vendor sensitive-word lexicon snapshot"
```

---

### Task 2: 归一化器（normalize.ts）

**Files:**
- Create: `src/moderation/normalize.ts`
- Test: `src/moderation/normalize.test.ts`

- [ ] **Step 1: 写失败的测试**

创建 `src/moderation/normalize.test.ts`：

```ts
import assert from "node:assert/strict";
import test from "node:test";
import { normalizeForMatch } from "./normalize.js";

test("plain CJK text passes through unchanged", () => {
  const result = normalizeForMatch("敏感词");
  assert.deepEqual(result.chars, ["敏", "感", "词"]);
  assert.deepEqual(result.originalStart, [0, 1, 2]);
  assert.deepEqual(result.originalEnd, [1, 2, 3]);
});

test("whitespace and punctuation are stripped but span mapping covers them", () => {
  const result = normalizeForMatch("敏 感");
  assert.deepEqual(result.chars, ["敏", "感"]);
  assert.deepEqual(result.originalStart, [0, 2]);
  assert.deepEqual(result.originalEnd, [1, 3]);
});

test("fullwidth ASCII folds to halfwidth and lowercases", () => {
  const result = normalizeForMatch("ＡＢｃ");
  assert.deepEqual(result.chars, ["a", "b", "c"]);
});

test("ASCII case folds", () => {
  assert.deepEqual(normalizeForMatch("MgD").chars, ["m", "g", "d"]);
});

test("symbols and emoji are stripped for matching", () => {
  const result = normalizeForMatch("a🔥b");
  assert.deepEqual(result.chars, ["a", "b"]);
  assert.deepEqual(result.originalStart, [0, 3]);
});

test("consecutive repeated characters collapse and extend the original span", () => {
  const result = normalizeForMatch("笨笨笨蛋");
  assert.deepEqual(result.chars, ["笨", "蛋"]);
  assert.deepEqual(result.originalEnd, [3, 4]);
});

test("repeats separated by stripped chars still collapse", () => {
  const result = normalizeForMatch("a.a");
  assert.deepEqual(result.chars, ["a"]);
  assert.deepEqual(result.originalStart, [0]);
  assert.deepEqual(result.originalEnd, [3]);
});

test("zero-width characters are stripped", () => {
  assert.deepEqual(normalizeForMatch("敏\u200b感").chars, ["敏", "感"]);
});

test("surrogate pairs advance original indices by UTF-16 width", () => {
  const result = normalizeForMatch("🔥甲");
  assert.deepEqual(result.chars, ["甲"]);
  assert.deepEqual(result.originalStart, [2]);
  assert.deepEqual(result.originalEnd, [3]);
});

test("empty and symbol-only input normalize to nothing", () => {
  assert.deepEqual(normalizeForMatch("").chars, []);
  assert.deepEqual(normalizeForMatch(" 。。 ").chars, []);
});
```

- [ ] **Step 2: 运行测试确认失败**

```bash
npm run build && node --test dist/moderation/normalize.test.js
```

预期：FAIL，报 `Cannot find module .../normalize.js`（编译期 tsc 也会报找不到模块，属预期）。

- [ ] **Step 3: 实现 normalize.ts**

创建 `src/moderation/normalize.ts`：

```ts
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
```

- [ ] **Step 4: 运行测试确认通过**

```bash
npm run build && node --test dist/moderation/normalize.test.js
```

预期：PASS（10 个测试）。

- [ ] **Step 5: 提交**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git add lobby-service/src/moderation/normalize.ts lobby-service/src/moderation/normalize.test.ts
git commit -m "feat(lobby): add moderation text normalizer with original-index mapping"
```

---

### Task 3: Trie 匹配器（dfa.ts）

**Files:**
- Create: `src/moderation/dfa.ts`
- Test: `src/moderation/dfa.test.ts`

- [ ] **Step 1: 写失败的测试**

创建 `src/moderation/dfa.test.ts`：

```ts
import assert from "node:assert/strict";
import test from "node:test";
import { buildTrie, findMatches } from "./dfa.js";

test("finds a single match with exact span", () => {
  const root = buildTrie([["敏", "感"], ["词"]]);
  const spans = findMatches(root, ["这", "是", "敏", "感", "词", "啊"]);
  assert.deepEqual(spans, [
    { startChar: 2, endChar: 4 },
    { startChar: 4, endChar: 5 },
  ]);
});

test("reports overlapping matches from different start positions", () => {
  const root = buildTrie([["a", "b"], ["b", "c"]]);
  const spans = findMatches(root, ["a", "b", "c"]);
  assert.deepEqual(spans, [
    { startChar: 0, endChar: 2 },
    { startChar: 1, endChar: 3 },
  ]);
});

test("empty word list matches nothing", () => {
  const root = buildTrie([]);
  assert.deepEqual(findMatches(root, ["敏", "感"]), []);
});

test("no match returns empty array", () => {
  const root = buildTrie([["敏", "感"]]);
  assert.deepEqual(findMatches(root, ["正", "常", "内", "容"]), []);
});

test("empty words are ignored during build", () => {
  const root = buildTrie([[], ["x"]]);
  assert.deepEqual(findMatches(root, ["y"]), []);
  assert.deepEqual(findMatches(root, ["x"]), [{ startChar: 0, endChar: 1 }]);
});

test("longer word at same start produces both spans", () => {
  const root = buildTrie([["敏"], ["敏", "感", "词"]]);
  const spans = findMatches(root, ["敏", "感", "词"]);
  assert.deepEqual(spans, [
    { startChar: 0, endChar: 1 },
    { startChar: 0, endChar: 3 },
  ]);
});
```

- [ ] **Step 2: 运行测试确认失败**

```bash
npm run build && node --test dist/moderation/dfa.test.js
```

预期：FAIL（找不到 `./dfa.js`）。

- [ ] **Step 3: 实现 dfa.ts**

创建 `src/moderation/dfa.ts`：

```ts
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
```

- [ ] **Step 4: 运行测试确认通过**

```bash
npm run build && node --test dist/moderation/dfa.test.js
```

预期：PASS（6 个测试）。

- [ ] **Step 5: 提交**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git add lobby-service/src/moderation/dfa.ts lobby-service/src/moderation/dfa.test.ts
git commit -m "feat(lobby): add trie matcher for moderation"
```

---

### Task 4: 过滤器与词库加载器（filter.ts + lexicon-loader.ts）

**Files:**
- Create: `src/moderation/filter.ts`、`src/moderation/lexicon-loader.ts`
- Test: `src/moderation/filter.test.ts`、`src/moderation/lexicon-loader.test.ts`

- [ ] **Step 1: 写失败的测试（filter）**

创建 `src/moderation/filter.test.ts`：

```ts
import assert from "node:assert/strict";
import test from "node:test";
import { buildTrie } from "./dfa.js";
import { LexiconSensitiveWordFilter, nullSensitiveWordFilter } from "./filter.js";
import { normalizeForMatch } from "./normalize.js";

function filterWith(words: string[]): LexiconSensitiveWordFilter {
  return new LexiconSensitiveWordFilter(
    buildTrie(words.map((word) => normalizeForMatch(word).chars)),
    words.length,
  );
}

test("mask replaces the sensitive span with equal-length asterisks", () => {
  const filter = filterWith(["敏感词"]);
  assert.deepEqual(filter.mask("这是敏感词啊"), { text: "这是***啊", masked: true });
});

test("mask covers stripped characters inside the matched span", () => {
  const filter = filterWith(["敏感"]);
  assert.deepEqual(filter.mask("敏 感 话题"), { text: "*** 话题", masked: true });
});

test("matching is case and width insensitive on both sides", () => {
  const filter = filterWith(["ABC"]);
  assert.deepEqual(filter.mask("a b c"), { text: "*****", masked: true });
  assert.equal(filter.contains("ＡＢＣ"), true);
});

test("repeated-character evasion still matches", () => {
  const filter = filterWith(["笨蛋"]);
  assert.equal(filter.contains("笨笨笨蛋"), true);
});

test("clean text is untouched", () => {
  const filter = filterWith(["敏感词"]);
  assert.deepEqual(filter.mask("正常聊天内容"), { text: "正常聊天内容", masked: false });
  assert.equal(filter.contains("正常聊天内容"), false);
});

test("wordCount is exposed", () => {
  assert.equal(filterWith(["a", "b", "c"]).wordCount, 3);
});

test("nullSensitiveWordFilter is a no-op", () => {
  assert.equal(nullSensitiveWordFilter.wordCount, 0);
  assert.equal(nullSensitiveWordFilter.contains("任何词"), false);
  assert.deepEqual(nullSensitiveWordFilter.mask("任何词"), { text: "任何词", masked: false });
});

test("mask never changes string length", () => {
  const filter = filterWith(["敏感词"]);
  const input = "前敏感词后";
  const result = filter.mask(input);
  assert.equal(result.text.length, input.length);
});
```

- [ ] **Step 2: 写失败的测试（lexicon-loader）**

创建 `src/moderation/lexicon-loader.test.ts`：

```ts
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { loadLexicon } from "./lexicon-loader.js";

function lexiconDir(files: Record<string, string>): string {
  const dir = mkdtempSync(join(tmpdir(), "sts2-lexicon-test-"));
  for (const [name, content] of Object.entries(files)) {
    writeFileSync(join(dir, name), content, "utf8");
  }
  return dir;
}

test("loads words from all txt files with dedupe across files", () => {
  const dir = lexiconDir({
    "a.txt": "敏感词\n笨蛋\n",
    "b.txt": "敏感词\n白痴\n",
  });
  const lexicon = loadLexicon(dir);
  assert.equal(lexicon.wordCount, 3);
});

test("skips blank lines and # comments", () => {
  const dir = lexiconDir({ "a.txt": "# 注释\n\n敏感词\n  \n" });
  assert.equal(loadLexicon(dir).wordCount, 1);
});

test("ignores non-txt files", () => {
  const dir = lexiconDir({ "a.txt": "敏感词\n", "notes.md": "不该被加载的词\n" });
  assert.equal(loadLexicon(dir).wordCount, 1);
});

test("words are normalized with the same pipeline as input text", () => {
  const dir = lexiconDir({ "a.txt": "ＡＢＣ\n" });
  const lexicon = loadLexicon(dir);
  assert.equal(lexicon.wordCount, 1);
  // 归一化后词表词应为小写半角。
  const chars = Array.from(lexicon.root.children.keys());
  assert.deepEqual(chars, ["a"]);
});

test("missing directory throws (caller decides fail-open policy)", () => {
  assert.throws(() => loadLexicon(join(tmpdir(), "sts2-lexicon-does-not-exist")));
});

test("empty directory loads zero words", () => {
  const dir = mkdtempSync(join(tmpdir(), "sts2-lexicon-empty-"));
  mkdirSync(dir, { recursive: true });
  assert.equal(loadLexicon(dir).wordCount, 0);
});
```

- [ ] **Step 3: 运行测试确认失败**

```bash
npm run build && node --test dist/moderation/filter.test.js dist/moderation/lexicon-loader.test.js
```

预期：FAIL（找不到 `./filter.js`、`./lexicon-loader.js`）。

- [ ] **Step 4: 实现 filter.ts**

创建 `src/moderation/filter.ts`：

```ts
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
 * mask 为等量替换（每个 UTF-16 code unit 替换为一个 *），不改变字符串长度，
 * 因此不影响聊天 wire 预算与去重逻辑。
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
    const normalized = normalizeForMatch(text);
    const spans = findMatches(this.root, normalized.chars);
    if (spans.length === 0) {
      return { text, masked: false };
    }
    const cut = new Uint8Array(text.length);
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
    for (let index = 0; index < text.length; index += 1) {
      masked += cut[index] === 1 ? "*" : text[index];
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
```

- [ ] **Step 5: 实现 lexicon-loader.ts**

创建 `src/moderation/lexicon-loader.ts`：

```ts
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
```

- [ ] **Step 6: 运行测试确认通过**

```bash
npm run build && node --test dist/moderation/filter.test.js dist/moderation/lexicon-loader.test.js
```

预期：PASS（8 + 6 个测试）。

- [ ] **Step 7: 提交**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git add lobby-service/src/moderation/filter.ts lobby-service/src/moderation/filter.test.ts lobby-service/src/moderation/lexicon-loader.ts lobby-service/src/moderation/lexicon-loader.test.ts
git commit -m "feat(lobby): add sensitive word filter and lexicon loader"
```

---

### Task 5: ModerationService（可热切换 + 统计 + fail-open）

**Files:**
- Create: `src/moderation/moderation-service.ts`
- Test: `src/moderation/moderation-service.test.ts`

- [ ] **Step 1: 写失败的测试**

创建 `src/moderation/moderation-service.test.ts`：

```ts
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { ModerationService } from "./moderation-service.js";

function lexiconDir(words: string[] = ["白痴", "敏感词"]): string {
  const dir = mkdtempSync(join(tmpdir(), "sts2-moderation-test-"));
  writeFileSync(join(dir, "words.txt"), words.join("\n"), "utf8");
  return dir;
}

test("enabled service masks chat text and counts each masked call", () => {
  const service = ModerationService.create({ lexiconDir: lexiconDir(), enabled: true });
  assert.equal(service.enabled, true);
  assert.equal(service.wordCount, 2);
  assert.deepEqual(service.maskChatText("你是白痴"), { text: "你是**", masked: true });
  assert.deepEqual(service.maskChatText("正常内容"), { text: "正常内容", masked: false });
  assert.equal(service.stats().maskedMessages, 1);
});

test("containsSensitiveName and recordNameRejection drive rejection stats", () => {
  const service = ModerationService.create({ lexiconDir: lexiconDir(), enabled: true });
  assert.equal(service.containsSensitiveName("白痴玩家"), true);
  assert.equal(service.containsSensitiveName("正常名字"), false);
  service.recordNameRejection();
  service.recordNameRejection();
  assert.equal(service.stats().rejectedNames, 2);
});

test("setEnabled(false) swaps to the no-op filter and back", () => {
  const service = ModerationService.create({ lexiconDir: lexiconDir(), enabled: true });
  service.setEnabled(false);
  assert.equal(service.enabled, false);
  assert.equal(service.containsSensitiveName("白痴"), false);
  assert.deepEqual(service.maskChatText("白痴"), { text: "白痴", masked: false });
  service.setEnabled(true);
  assert.equal(service.containsSensitiveName("白痴"), true);
});

test("created disabled when enabled=false", () => {
  const service = ModerationService.create({ lexiconDir: lexiconDir(), enabled: false });
  assert.equal(service.enabled, false);
  assert.equal(service.containsSensitiveName("白痴"), false);
  // 词库仍在，可随时开启。
  service.setEnabled(true);
  assert.equal(service.containsSensitiveName("白痴"), true);
});

test("missing lexicon dir fails open with loadError", () => {
  const logs: string[] = [];
  const service = ModerationService.create({
    lexiconDir: join(tmpdir(), "sts2-moderation-missing-dir"),
    enabled: true,
    log: (message) => logs.push(message),
  });
  assert.equal(service.enabled, false);
  assert.equal(service.wordCount, 0);
  assert.equal(service.stats().loadError, "词库加载失败");
  assert.equal(service.containsSensitiveName("白痴"), false);
  assert.equal(logs.length, 1);
});

test("empty lexicon fails open with loadError", () => {
  const dir = mkdtempSync(join(tmpdir(), "sts2-moderation-empty-"));
  const service = ModerationService.create({ lexiconDir: dir, enabled: true });
  assert.equal(service.enabled, false);
  assert.equal(service.stats().loadError, "词库为空");
});

test("stats snapshot shape", () => {
  const service = ModerationService.create({ lexiconDir: lexiconDir(), enabled: true });
  assert.deepEqual(service.stats(), {
    enabled: true,
    wordCount: 2,
    maskedMessages: 0,
    rejectedNames: 0,
    loadError: null,
  });
});
```

- [ ] **Step 2: 运行测试确认失败**

```bash
npm run build && node --test dist/moderation/moderation-service.test.js
```

预期：FAIL（找不到模块）。

- [ ] **Step 3: 实现 moderation-service.ts**

创建 `src/moderation/moderation-service.ts`：

```ts
import { LexiconSensitiveWordFilter, nullSensitiveWordFilter, type MaskResult, type SensitiveWordFilter } from "./filter.js";
import { loadLexicon } from "./lexicon-loader.js";

export interface ModerationStats {
  enabled: boolean;
  wordCount: number;
  maskedMessages: number;
  rejectedNames: number;
  loadError: string | null;
}

export interface ModerationServiceOptions {
  lexiconDir: string;
  enabled: boolean;
  log?: (message: string) => void;
}

/**
 * 敏感词过滤的运行时持有者：
 * - 启动时加载词库（失败 fail-open，记录 loadError，不阻断服务启动）；
 * - setEnabled 热切换 开/关（关 = NullFilter），挂接点持有稳定引用无感知；
 * - 统计打码/拒绝次数（自进程启动起累计，重启清零）。
 *
 * 注意：maskedMessages 按 maskChatText 调用计数。聊天一条消息的相邻 text
 * segment 会被 canonicalize 合并，通常一条消息只触发一次打码检查；
 * 含 emoji/引用分割的多段消息命中多段时会计多次，属可接受的近似。
 */
export class ModerationService {
  private active: SensitiveWordFilter;
  private maskedMessages = 0;
  private rejectedNames = 0;

  private constructor(
    private readonly lexiconFilter: SensitiveWordFilter,
    private readonly lexiconLoadError: string | null,
    enabled: boolean,
  ) {
    this.active = enabled ? lexiconFilter : nullSensitiveWordFilter;
  }

  static create(options: ModerationServiceOptions): ModerationService {
    try {
      const lexicon = loadLexicon(options.lexiconDir);
      if (lexicon.wordCount === 0) {
        options.log?.(`[moderation] lexicon at ${options.lexiconDir} is empty; sensitive filter unavailable`);
        return new ModerationService(nullSensitiveWordFilter, "词库为空", options.enabled);
      }
      const filter = new LexiconSensitiveWordFilter(lexicon.root, lexicon.wordCount);
      options.log?.(`[moderation] loaded ${lexicon.wordCount} sensitive words from ${options.lexiconDir}`);
      return new ModerationService(filter, null, options.enabled);
    } catch (error) {
      options.log?.(`[moderation] failed to load lexicon from ${options.lexiconDir}: ${String(error)}`);
      return new ModerationService(nullSensitiveWordFilter, "词库加载失败", options.enabled);
    }
  }

  get enabled(): boolean {
    return this.active !== nullSensitiveWordFilter;
  }

  get wordCount(): number {
    return this.lexiconFilter.wordCount;
  }

  setEnabled(enabled: boolean): void {
    this.active = enabled ? this.lexiconFilter : nullSensitiveWordFilter;
  }

  maskChatText(text: string): MaskResult {
    const result = this.active.mask(text);
    if (result.masked) {
      this.maskedMessages += 1;
    }
    return result;
  }

  containsSensitiveName(text: string): boolean {
    return this.active.contains(text);
  }

  recordNameRejection(): void {
    this.rejectedNames += 1;
  }

  stats(): ModerationStats {
    return {
      enabled: this.enabled,
      wordCount: this.wordCount,
      maskedMessages: this.maskedMessages,
      rejectedNames: this.rejectedNames,
      loadError: this.lexiconLoadError,
    };
  }
}
```

- [ ] **Step 4: 运行测试确认通过**

```bash
npm run build && node --test dist/moderation/moderation-service.test.js
```

预期：PASS（7 个测试）。

- [ ] **Step 5: 提交**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git add lobby-service/src/moderation/moderation-service.ts lobby-service/src/moderation/moderation-service.test.ts
git commit -m "feat(lobby): add hot-swappable moderation service with stats"
```

---

### Task 6: 配置项（config.ts + .env.example）

**Files:**
- Modify: `src/config.ts`（interface + loadLobbyServiceConfig + import）
- Modify: `src/config.test.ts`
- Modify: `.env.example`

- [ ] **Step 1: 写失败的测试**

在 `src/config.test.ts` 末尾追加（文件顶部已有 `loadLobbyServiceConfig` 的 import；若没有则照现有 import 补 `LobbyServiceConfigError`）：

```ts
test("sensitive filter config defaults", () => {
  const config = loadLobbyServiceConfig({});
  assert.equal(config.sensitiveFilterEnabled, true);
  assert.ok(
    config.sensitiveLexiconDir.replaceAll("\\", "/").endsWith("lexicon/"),
    `expected default lexicon dir to end with lexicon/, got ${config.sensitiveLexiconDir}`,
  );
});

test("sensitive filter config honors env overrides", () => {
  const config = loadLobbyServiceConfig({
    SENSITIVE_FILTER_ENABLED: "false",
    SENSITIVE_LEXICON_DIR: "/tmp/custom-lexicon",
  });
  assert.equal(config.sensitiveFilterEnabled, false);
  assert.equal(config.sensitiveLexiconDir, "/tmp/custom-lexicon");
});

test("sensitive filter config rejects invalid boolean", () => {
  assert.throws(
    () => loadLobbyServiceConfig({ SENSITIVE_FILTER_ENABLED: "maybe" }),
    (error: unknown) => error instanceof LobbyServiceConfigError && error.code === "invalid_boolean",
  );
});
```

- [ ] **Step 2: 运行测试确认失败**

```bash
npm run build && node --test dist/config.test.js
```

预期：FAIL（`config.sensitiveFilterEnabled` 为 undefined / tsc 报属性不存在）。

- [ ] **Step 3: 实现 config.ts 改动**

`src/config.ts` 顶部 import 区加：

```ts
import { fileURLToPath } from "node:url";
```

`LobbyServiceConfig` interface 中（`chat: ChatConfig;` 之前）加：

```ts
  sensitiveFilterEnabled: boolean;
  sensitiveLexiconDir: string;
```

`loadLobbyServiceConfig` 返回对象中（`chat: loadChatConfig(source),` 之前）加：

```ts
    sensitiveFilterEnabled: parseBoolean(source, "SENSITIVE_FILTER_ENABLED", true),
    sensitiveLexiconDir: source.SENSITIVE_LEXICON_DIR ?? defaultLexiconDir(),
```

文件末尾加：

```ts
/**
 * 默认词库目录：包根下的 lexicon/（dist/config.js 的上一级即包根）。
 * 与 readLobbyServiceVersion 读取 ../package.json 同款相对定位。
 */
function defaultLexiconDir(): string {
  return fileURLToPath(new URL("../lexicon/", import.meta.url));
}
```

注意：`parseBoolean` 是 `config.ts` 内已存在的 helper，直接复用。

- [ ] **Step 4: 更新 .env.example**

在 `.env.example` 的聊天/治理相关段落附近追加（保持文件中英文注释风格）：

```dotenv
# 敏感词过滤（首次启动的种子值；之后以管理面板设置为准）
#SENSITIVE_FILTER_ENABLED=true
# 词库目录覆盖（默认包根 lexicon/）
#SENSITIVE_LEXICON_DIR=
```

- [ ] **Step 5: 运行测试确认通过**

```bash
npm run build && node --test dist/config.test.js && npm run check
```

预期：PASS。

- [ ] **Step 6: 提交**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git add lobby-service/src/config.ts lobby-service/src/config.test.ts lobby-service/.env.example
git commit -m "feat(lobby): add sensitive filter env config"
```

---

### Task 7: 聊天协议打码钩子（protocol.ts）

**Files:**
- Modify: `src/chat/protocol.ts:259-272`（canonicalizeChatContent/canonicalizeRoomContent 签名）、`:274-279`（canonicalizeContent 签名）、`:524-527`（return 前插入打码）
- Test: `src/chat/protocol.test.ts`

- [ ] **Step 1: 写失败的测试**

在 `src/chat/protocol.test.ts` 末尾追加（沿用文件现有的 import，若 `canonicalizeRoomContent` 未导入则补充）：

```ts
test("canonicalizeChatContent applies maskText to text segments only", () => {
  const content = canonicalizeChatContent(
    {
      formatVersion: 1,
      segments: [
        { kind: "text", text: "你是白痴" },
        { kind: "emoji", emojiId: "smile" },
        { kind: "text", text: "吗" },
      ],
    },
    { richContentVersion: 1, emojiSetVersion: 1, itemRefVersion: 1, combatRefVersion: 0 },
    (text) => ({ text: text.replaceAll("白痴", "**"), masked: text.includes("白痴") }),
  );
  assert.deepEqual(content.segments, [
    { kind: "text", text: "你是**" },
    { kind: "emoji", emojiId: "smile" },
    { kind: "text", text: "吗" },
  ]);
});

test("canonicalizeChatContent without maskText leaves text untouched", () => {
  const content = canonicalizeChatContent(
    { formatVersion: 1, segments: [{ kind: "text", text: "白痴" }] },
    { richContentVersion: 1, emojiSetVersion: 1, itemRefVersion: 1, combatRefVersion: 0 },
  );
  assert.deepEqual(content.segments, [{ kind: "text", text: "白痴" }]);
});

test("maskText runs after structural validation so invalid content still rejects", () => {
  assert.throws(
    () => canonicalizeChatContent(
      { formatVersion: 2, segments: [{ kind: "text", text: "hi" }] },
      { richContentVersion: 1, emojiSetVersion: 1, itemRefVersion: 1, combatRefVersion: 0 },
      (text) => ({ text, masked: false }),
    ),
    (error: unknown) => error instanceof ChatProtocolError && error.code === "invalid_content",
  );
});

test("canonicalizeRoomContent applies maskText with room context", () => {
  const content = canonicalizeRoomContent(
    { formatVersion: 1, segments: [{ kind: "text", text: "你是白痴" }] },
    { richContentVersion: 1, emojiSetVersion: 1, itemRefVersion: 1, combatRefVersion: 1 },
    {
      envelopeRoomSessionId: "session-1",
      activeRoomSessionId: "session-1",
      peerPlayerNetIds: new Set<string>(),
    },
    (text) => ({ text: text.replaceAll("白痴", "**"), masked: text.includes("白痴") }),
  );
  assert.deepEqual(content.segments, [{ kind: "text", text: "你是**" }]);
});
```

- [ ] **Step 2: 运行测试确认失败**

```bash
npm run build && node --test dist/chat/protocol.test.js
```

预期：FAIL（tsc 报 canonicalize 调用参数过多 / maskText 未生效）。

- [ ] **Step 3: 实现 protocol.ts 改动**

`src/chat/protocol.ts` 中：

① `canonicalizeChatContent` 之前新增类型并改签名（:259-272）：

```ts
/** 打码函数：输入 text segment 原文，返回打码结果。由 ModerationService 注入。 */
export type ChatTextMaskFn = (text: string) => { text: string; masked: boolean };

export function canonicalizeChatContent(
  input: unknown,
  features: EnabledRichFeatures,
  maskText?: ChatTextMaskFn,
): ChatContent {
  return canonicalizeContent(input, features, false, undefined, maskText);
}

export function canonicalizeRoomContent(
  input: unknown,
  features: EnabledRichFeatures,
  context: RoomContentContext,
  maskText?: ChatTextMaskFn,
): ChatContent {
  return canonicalizeContent(input, features, false, context, maskText);
}
```

② `canonicalizeContent` 签名（:274-279）加参数：

```ts
function canonicalizeContent(
  input: unknown,
  features: EnabledRichFeatures,
  legacyRichKindPrecedence: boolean,
  roomContext?: RoomContentContext,
  maskText?: ChatTextMaskFn,
): ChatContent {
```

③ 在 `return { formatVersion: 1, segments: canonicalSegments };`（:524-527）**之前**插入：

```ts
  // 结构校验与预算检查全部通过后才打码。打码为等量替换，不改变长度，
  // 因此前面的 300 字符预算与后续 wire 预算依然成立。
  if (maskText !== undefined) {
    for (const segment of canonicalSegments) {
      if (segment.kind === "text") {
        segment.text = maskText(segment.text).text;
      }
    }
  }
```

- [ ] **Step 4: 运行测试确认通过**

```bash
npm run build && node --test dist/chat/protocol.test.js && npm run check
```

预期：PASS（含既有协议测试回归）。

- [ ] **Step 5: 提交**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git add lobby-service/src/chat/protocol.ts lobby-service/src/chat/protocol.test.ts
git commit -m "feat(lobby): add optional mask hook to chat content canonicalization"
```

---

### Task 8: 管理状态持久化（server-admin-state.ts）

**Files:**
- Modify: `src/server-admin-state.ts`
- Test: `src/server-admin-state.test.ts`

- [ ] **Step 1: 写失败的测试**

先看 `src/server-admin-state.test.ts` 现有测试如何构造 store（复制其 defaults 参数写法），然后在文件末尾追加（`chatFeaturesDefault` 的完整对象照抄现有测试中的写法）：

```ts
test("sensitiveFilterEnabled defaults to true and persists across reload", () => {
  // 照抄本文件现有测试的 stateFile/store 构造方式，仅断言新字段。
  const store = new ServerAdminStateStore(stateFilePath, {
    chatFeaturesDefault: defaultChatFeatures, // 使用本文件已有的 defaults 常量/写法
  });
  assert.equal(store.getSettingsView().sensitiveFilterEnabled, true);
  store.updateSettings({ sensitiveFilterEnabled: false });
  assert.equal(store.getSettingsView().sensitiveFilterEnabled, false);
  const reloaded = new ServerAdminStateStore(stateFilePath, {
    chatFeaturesDefault: defaultChatFeatures,
  });
  assert.equal(reloaded.getSettingsView().sensitiveFilterEnabled, false);
});

test("sensitiveFilterEnabled falls back to env seed default when persisted value absent", () => {
  const store = new ServerAdminStateStore(freshStateFilePath, {
    chatFeaturesDefault: defaultChatFeatures,
    sensitiveFilterEnabledDefault: false,
  });
  assert.equal(store.getSettingsView().sensitiveFilterEnabled, false);
});

test("updateSettings without sensitiveFilterEnabled keeps current value", () => {
  const store = new ServerAdminStateStore(stateFilePath, {
    chatFeaturesDefault: defaultChatFeatures,
    sensitiveFilterEnabledDefault: false,
  });
  store.updateSettings({ displayName: "新名字" });
  assert.equal(store.getSettingsView().sensitiveFilterEnabled, false);
});
```

> 注：`stateFilePath`/`defaultChatFeatures` 等名称以该测试文件里已有的 helper/常量为准；若文件已有创建临时 state 文件的 helper（如 mkdtempSync），直接复用。每条测试用独立的临时文件路径，避免互相污染。

- [ ] **Step 2: 运行测试确认失败**

```bash
npm run build && node --test dist/server-admin-state.test.js
```

预期：FAIL（属性不存在）。

- [ ] **Step 3: 实现 server-admin-state.ts 改动**

逐处修改 `src/server-admin-state.ts`：

① `ServerAdminState` 与 `ServerAdminSettingsView` 两个 interface 各加一行（放在 `chatFeatures` 之前）：

```ts
  sensitiveFilterEnabled: boolean;
```

② `ServerAdminStateDefaults` 加：

```ts
  sensitiveFilterEnabledDefault?: boolean;
```

③ 构造函数改为传入新默认值：

```ts
  constructor(stateFilePath: string, defaults: ServerAdminStateDefaults) {
    this.stateFilePath = resolve(stateFilePath);
    this.state = loadState(
      this.stateFilePath,
      defaults.publicListingEnabledDefault ?? true,
      defaults.modSyncEnabledDefault ?? true,
      defaults.chatFeaturesDefault,
      defaults.sensitiveFilterEnabledDefault ?? true,
    );
  }
```

④ `getSettingsView()` 返回对象加：

```ts
      sensitiveFilterEnabled: this.state.sensitiveFilterEnabled,
```

⑤ `updateSettings` 的参数类型加 `sensitiveFilterEnabled?: boolean;`，方法体内加：

```ts
    if (next.sensitiveFilterEnabled !== undefined) {
      staged.sensitiveFilterEnabled = next.sensitiveFilterEnabled;
    }
```

⑥ `loadState` 签名加第 5 参 `sensitiveFilterEnabledDefault: boolean`；catch 分支的默认对象加：

```ts
      sensitiveFilterEnabled: sensitiveFilterEnabledDefault,
```

⑦ `normalizeState` 签名同样加第 5 参，返回对象加：

```ts
    sensitiveFilterEnabled: booleanOrDefault(data.sensitiveFilterEnabled, sensitiveFilterEnabledDefault),
```

⑧ `loadState` 内对 `normalizeState` 的调用把新参数透传。

（`patch()` 方法通过 `...patch` 展开已自动支持该字段，无需改动。）

- [ ] **Step 4: 运行测试确认通过**

```bash
npm run build && node --test dist/server-admin-state.test.js && npm run check
```

预期：PASS。注意此时 `app.ts` 构造 `ServerAdminStateStore` 处尚未传新默认值——由于是可选项，编译不报错。

- [ ] **Step 5: 提交**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git add lobby-service/src/server-admin-state.ts lobby-service/src/server-admin-state.test.ts
git commit -m "feat(lobby): persist sensitive filter toggle in server admin state"
```

---

### Task 9: 服务端接线与 HTTP 名称拒绝（app.ts + 两个网关注入）

**Files:**
- Modify: `src/app.ts`（InputError details、moderation 构造、网关注入、4 类路由拒绝、settings PATCH/响应）
- Modify: `src/chat/gateway.ts`（options + canonicalize 调用点 :431）
- Modify: `src/chat/room-gateway.ts`（options + canonicalize 调用点 :488；hello 拒绝在 Task 10）
- Test: `src/app.integration.test.ts`

- [ ] **Step 1: 写失败的集成测试**

`src/app.integration.test.ts` 顶部 fs import 中确认有 `writeFileSync`（没有则补进现有的 `node:fs` import）。文件末尾追加：

```ts
function writeTestLexicon(words: string[] = ["白痴", "敏感词"]): string {
  const dir = mkdtempSync(join(tmpdir(), "sts2-lexicon-it-"));
  writeFileSync(join(dir, "words.txt"), words.join("\n"), "utf8");
  return dir;
}

test("create room rejects sensitive roomName with 400 and reason detail", async () => {
  const config = testConfig({ port: 0, sensitiveLexiconDir: writeTestLexicon() });
  const service = await createLobbyService(config);
  const address = await service.start();
  try {
    const response = await fetch(`http://127.0.0.1:${address.port}/rooms`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        roomName: "敏感词专用房",
        hostPlayerName: "Host",
        gameMode: "standard",
        version: "1.0.0",
        modVersion: "1.0.0",
        modList: [],
        maxPlayers: 4,
        hostConnectionInfo: { enetPort: 7777, localAddresses: ["127.0.0.1"] },
      }),
    });
    assert.equal(response.status, 400);
    const body = await response.json() as { code: string; message: string; details?: unknown };
    assert.equal(body.code, "invalid_request");
    assert.equal(body.message, "包含敏感词内容，请修改后重试");
    assert.deepEqual(body.details, { reason: "sensitive_content" });
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("create room rejects sensitive hostPlayerName", async () => {
  const config = testConfig({ port: 0, sensitiveLexiconDir: writeTestLexicon() });
  const service = await createLobbyService(config);
  const address = await service.start();
  try {
    const response = await fetch(`http://127.0.0.1:${address.port}/rooms`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        roomName: "正常房间",
        hostPlayerName: "白痴房主",
        gameMode: "standard",
        version: "1.0.0",
        modVersion: "1.0.0",
        modList: [],
        maxPlayers: 4,
        hostConnectionInfo: { enetPort: 7777, localAddresses: ["127.0.0.1"] },
      }),
    });
    assert.equal(response.status, 400);
    const body = await response.json() as { message: string };
    assert.equal(body.message, "包含敏感词内容，请修改后重试");
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("chat ticket rejects sensitive playerName", async () => {
  const config = testConfig({ port: 0, sensitiveLexiconDir: writeTestLexicon() });
  const service = await createLobbyService(config);
  const address = await service.start();
  try {
    const response = await postChatTicket(address.port, {
      body: ticketBody({ playerName: "白痴玩家" }),
    });
    assert.equal(response.status, 400);
    const body = await response.json() as { code: string; message: string };
    assert.equal(body.code, "invalid_request");
    assert.equal(body.message, "包含敏感词内容，请修改后重试");
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});

test("server chat masks sensitive text before ack and broadcast", async () => {
  const base = testConfig({ port: 0, sensitiveLexiconDir: writeTestLexicon() });
  const config = {
    ...base,
    chat: { ...base.chat, features: { ...base.chat.features, serverChatEnabled: true } },
  };
  const service = await createLobbyService(config);
  const address = await service.start();
  let socket: WebSocket | undefined;
  try {
    const ticketResponse = await postChatTicket(address.port);
    assert.equal(ticketResponse.status, 200);
    const issued = await ticketResponse.json() as { ticket: string; webSocketUrl: string };
    socket = await openChatWebSocket(issued.webSocketUrl, issued.ticket);
    await waitForChatFrame(socket, (frame) => frame.type === "chat_ready");

    const clientMessageId = "25252525-2525-4525-8525-252525252525";
    const ackPromise = waitForChatFrame(
      socket,
      (frame) => frame.type === "chat_ack" && frame.clientMessageId === clientMessageId,
    );
    const broadcastPromise = waitForChatFrame(socket, (frame) => frame.type === "chat_message");
    socket.send(JSON.stringify({
      type: "chat_send",
      protocolVersion: 1,
      channel: "server",
      clientMessageId,
      content: { formatVersion: 1, segments: [{ kind: "text", text: "你是白痴吗" }] },
    }));
    const [ack, broadcast] = await Promise.all([ackPromise, broadcastPromise]);
    const ackMessage = ack.message as { content: { segments: unknown[] } };
    const broadcastMessage = broadcast.message as { content: { segments: unknown[] } };
    assert.deepEqual(ackMessage.content.segments, [{ kind: "text", text: "你是**吗" }]);
    assert.deepEqual(broadcastMessage.content.segments, [{ kind: "text", text: "你是**吗" }]);
  } finally {
    socket?.terminate();
    await service.close();
    cleanupTempDir(config);
  }
});

test("clean names and clean chat work unchanged when filter enabled", async () => {
  const config = testConfig({ port: 0, sensitiveLexiconDir: writeTestLexicon() });
  const service = await createLobbyService(config);
  const address = await service.start();
  try {
    const response = await fetch(`http://127.0.0.1:${address.port}/rooms`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        roomName: "正常房间",
        hostPlayerName: "正常玩家",
        gameMode: "standard",
        version: "1.0.0",
        modVersion: "1.0.0",
        modList: [],
        maxPlayers: 4,
        hostConnectionInfo: { enetPort: 7777, localAddresses: ["127.0.0.1"] },
      }),
    });
    assert.equal(response.status, 201);
  } finally {
    await service.close();
    cleanupTempDir(config);
  }
});
```

- [ ] **Step 2: 运行测试确认失败**

```bash
npm run build && node --test dist/app.integration.test.js --test-name-pattern="sensitive"
```

预期：名称含 "sensitive" 的 4 个新测试 FAIL（还没有过滤行为）。随后单独跑 `node --test dist/app.integration.test.js --test-name-pattern="clean names"`，第 5 个（clean）此时应已 PASS。

- [ ] **Step 3: 实现 app.ts — InputError details**

`src/app.ts` 中 `class InputError extends Error {}`（:111 附近）改为：

```ts
class InputError extends Error {
  readonly details?: Record<string, unknown>;

  constructor(message: string, details?: Record<string, unknown>) {
    super(message);
    this.details = details;
  }
}
```

错误中间件的 `if (error instanceof InputError)` 分支（:910 附近）改为：

```ts
    if (error instanceof InputError) {
      res.status(400).json({
        code: "invalid_request",
        message: error.message,
        ...(error.details === undefined ? {} : { details: error.details }),
      });
      return;
    }
```

- [ ] **Step 4: 实现 app.ts — 构造 ModerationService 并注入网关**

① import 区加：

```ts
import { ModerationService } from "./moderation/moderation-service.js";
```

② `const serverAdminStateStore = new ServerAdminStateStore(env.serverAdminStateFile, {`（:231）的 defaults 对象加一行：

```ts
    sensitiveFilterEnabledDefault: env.sensitiveFilterEnabled,
```

③ 紧随其后（serviceUpdateManager 构造之前）插入：

```ts
  const moderation = ModerationService.create({
    lexiconDir: env.sensitiveLexiconDir,
    enabled: serverAdminStateStore.getState().sensitiveFilterEnabled,
    log: (message) => console.error(message),
  });
```

④ `new ServerChatGateway({`（:260）的 options 里加一行（放在 `...dependencies.chatGatewayOptions` 之前）：

```ts
    moderation,
```

⑤ `new RoomChatGateway({`（:282）的 options 里加一行：

```ts
    moderation,
```

- [ ] **Step 5: 实现 gateway.ts — 接受并使用 moderation**

`src/chat/gateway.ts`：

① import 区加：

```ts
import type { ModerationService } from "../moderation/moderation-service.js";
```

② `ServerChatGatewayOptions`（:23）加可选字段：

```ts
  moderation?: ModerationService;
```

③ 类内加私有字段并在 constructor 中赋值（照其他 option 的写法）：

```ts
  private readonly moderation: ModerationService | null;
```
```ts
    this.moderation = options.moderation ?? null;
```

④ canonicalize 调用点（:431）改为：

```ts
      const enabledFeatures = this.resolveFeatures(true);
      const moderation = this.moderation;
      content = canonicalizeChatContent(envelope.content, {
        richContentVersion: enabledFeatures.richContentVersion,
        emojiSetVersion: enabledFeatures.emojiSetVersion,
        itemRefVersion: enabledFeatures.itemRefVersion,
        combatRefVersion: 0,
      }, moderation === null ? undefined : (text) => moderation.maskChatText(text));
```

- [ ] **Step 6: 实现 room-gateway.ts — 接受并使用 moderation（仅聊天打码）**

`src/chat/room-gateway.ts`：

① import 区加：

```ts
import type { ModerationService } from "../moderation/moderation-service.js";
```

② `RoomChatGatewayOptions`（:36）加：

```ts
  moderation?: ModerationService;
```

③ 类内加 `private readonly moderation: ModerationService | null;` 并在 constructor 中 `this.moderation = options.moderation ?? null;`

④ canonicalize 调用点（:488）改为：

```ts
      const senderFeatures = this.resolveFeatures(peer, peer, true);
      const moderation = this.moderation;
      content = canonicalizeRoomContent(envelope.content, {
        richContentVersion: senderFeatures.richContentVersion,
        emojiSetVersion: senderFeatures.emojiSetVersion,
        itemRefVersion: senderFeatures.itemRefVersion,
        combatRefVersion: senderFeatures.combatRefVersion,
      }, {
        envelopeRoomSessionId: envelope.roomSessionId,
        activeRoomSessionId: receiveContext.roomSessionId,
        peerPlayerNetIds: receiveContext.peerPlayerNetIds,
      }, moderation === null ? undefined : (text) => moderation.maskChatText(text));
```

- [ ] **Step 7: 实现 app.ts — 名称拒绝 helper 与 4 类路由挂点**

① 在 `createLobbyService` 内部（moderation 构造之后的任意 helper 区）加：

```ts
  function assertNameAllowedByModeration(value: string): void {
    if (moderation.containsSensitiveName(value)) {
      moderation.recordNameRejection();
      throw new InputError("包含敏感词内容，请修改后重试", { reason: "sensitive_content" });
    }
  }
```

② 建房路由：`const room = store.createRoom(roomInput, requestIp(req));`（:560 附近）**之前**插入：

```ts
      assertNameAllowedByModeration(roomInput.roomName);
      assertNameAllowedByModeration(roomInput.hostPlayerName);
      for (const slot of roomInput.savedRun?.slots ?? []) {
        if (slot.characterName !== undefined) {
          assertNameAllowedByModeration(slot.characterName);
        }
        if (slot.playerName !== undefined) {
          assertNameAllowedByModeration(slot.playerName);
        }
      }
```

③ 加入路由（:588）：`const response = store.joinRoom(req.params.id, {` **之前**插入：

```ts
      if (typeof body?.playerName === "string") {
        assertNameAllowedByModeration(body.playerName);
      }
```

④ 聊天票据路由（:380）：在 `if (typeof body.playerNetId !== "string" || typeof body.playerName !== "string")` 检查**之后**插入：

```ts
    assertNameAllowedByModeration(body.playerName);
```

⑤ MOD 预检路由（:618）：在 allowedFields 白名单检查**之后**插入：

```ts
      if (typeof body.playerName === "string") {
        assertNameAllowedByModeration(body.playerName);
      }
```

- [ ] **Step 8: 运行集成测试确认通过**

```bash
npm run build && node --test dist/app.integration.test.js && npm run check
```

预期：全部 PASS（含既有集成测试回归）。

- [ ] **Step 9: 提交**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git add lobby-service/src/app.ts lobby-service/src/chat/gateway.ts lobby-service/src/chat/room-gateway.ts lobby-service/src/app.integration.test.ts
git commit -m "feat(lobby): wire moderation into chat gateways and reject sensitive names"
```

---

### Task 10: 房间 WS hello 昵称拒绝（room-gateway.ts）

**Files:**
- Modify: `src/chat/room-gateway.ts`（handleHello :344 附近、handleLegacyHello :416 附近）
- Test: `src/chat/room-gateway.test.ts`

- [ ] **Step 1: 写失败的测试**

`src/chat/room-gateway.test.ts` 顶部 import 区加（文件已有 node:test / fs 相关 import 则合并）：

```ts
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { ModerationService } from "../moderation/moderation-service.js";
```

文件末尾追加：

```ts
function testModeration(words: string[] = ["白痴"]): ModerationService {
  const dir = mkdtempSync(join(tmpdir(), "sts2-room-moderation-"));
  writeFileSync(join(dir, "words.txt"), words.join("\n"), "utf8");
  return ModerationService.create({ lexiconDir: dir, enabled: true });
}

test("hello with sensitive player name is rejected with invalid_message", () => {
  const gateway = createGateway({ moderation: testModeration() });
  const client = peer("client-connection");
  gateway.registerPeer(client.registration);

  assert.equal(
    gateway.handleControlEnvelope("client-connection", hello("白痴玩家", "net:alice")),
    true,
  );
  const error = client.frames.at(-1);
  assert.equal(error?.type, "room_chat_error");
  assert.equal(error?.code, "invalid_message");
  assert.equal(gateway.getLockedIdentity("client-connection"), undefined);
});

test("legacy hello with sensitive player name is rejected", () => {
  const gateway = createGateway({ moderation: testModeration() });
  const client = peer("client-connection");
  gateway.registerPeer(client.registration);

  const legacyHello = {
    type: "client_hello",
    roomId: "room-1",
    controlChannelId: "control-1",
    role: "client",
    playerName: "白痴玩家",
    playerNetId: "net:alice",
  };
  assert.equal(gateway.handleControlEnvelope("client-connection", legacyHello), true);
  const error = client.frames.at(-1);
  assert.equal(error?.type, "room_chat_error");
  assert.equal(error?.code, "invalid_message");
  assert.equal(gateway.getLockedIdentity("client-connection"), undefined);
});

test("clean hello still locks identity when moderation is enabled", () => {
  const gateway = createGateway({ moderation: testModeration() });
  const client = peer("client-connection");
  gateway.registerPeer(client.registration);

  assert.equal(
    gateway.handleControlEnvelope("client-connection", hello("Alice", "net:alice")),
    true,
  );
  assert.deepEqual(gateway.getLockedIdentity("client-connection"), {
    playerName: "Alice",
    playerNetId: "net:alice",
  });
});

test("room chat message masks sensitive text before broadcast", () => {
  const gateway = createGateway({ moderation: testModeration() });
  const client = peer("client-connection");
  const host = peer("host-connection", "host");
  gateway.registerPeer(client.registration);
  gateway.registerPeer(host.registration);
  gateway.handleControlEnvelope("client-connection", hello("Alice", "net:alice"));
  gateway.handleControlEnvelope("host-connection", hostHello("Host", "net:host"));
  client.frames.length = 0;
  host.frames.length = 0;

  assert.equal(gateway.handleControlEnvelope(
    "client-connection",
    roomSend("33333333-3333-4333-8333-333333333333", textContent("你是白痴")),
  ), true);

  const ack = client.frames.find((frame) => frame.type === "room_chat_ack");
  const delivered = host.frames.find((frame) => frame.type === "room_chat_message");
  assert.ok(ack, "expected a room_chat_ack frame");
  assert.ok(delivered, "expected a room_chat_message frame");
  const ackMessage = ack.message as { content: { segments: unknown[] } };
  const deliveredMessage = delivered.message as { content: { segments: unknown[] } };
  assert.deepEqual(ackMessage.content.segments, [{ kind: "text", text: "你是**" }]);
  assert.deepEqual(deliveredMessage.content.segments, [{ kind: "text", text: "你是**" }]);
});
```

- [ ] **Step 2: 运行测试确认失败**

```bash
npm run build && node --test dist/chat/room-gateway.test.js --test-name-pattern="sensitive"
```

预期：两个 hello 拒绝测试与打码测试匹配 "sensitive" 模式——hello 拒绝测试 FAIL（identity 仍被锁定/无错误帧），打码测试此时应已 PASS（Task 9 已接线）。clean hello 测试不匹配该模式，在 Step 4 的全量运行中验证。

- [ ] **Step 3: 实现 hello 拒绝**

`src/chat/room-gateway.ts`：

① `handleHello` 中，身份锁定对象构造处（:344-345 附近，`playerName: normalizePlayerName(envelope.playerName)` 所在的 try 块内），在 identity 构造**之后**插入（保持与该 try 块内其他 `RoomEnvelopeError` 抛法一致）：

```ts
      if (this.moderation?.containsSensitiveName(identity.playerName) === true) {
        this.moderation.recordNameRejection();
        throw new RoomEnvelopeError("invalid_message", "player name contains disallowed content");
      }
```

② `handleLegacyHello` 中，`const playerName = normalizePlayerName(envelope.playerName);`（:416 附近）**之后**插入：

```ts
      if (this.moderation?.containsSensitiveName(playerName) === true) {
        this.moderation.recordNameRejection();
        throw new RoomEnvelopeError("invalid_message", "player name contains disallowed content");
      }
```

> 注意：`this.moderation` 在 Task 9 已声明为 `ModerationService | null`；`?.` 调用后 `recordNameRejection` 需要非空——用局部变量：`const moderation = this.moderation; if (moderation?.containsSensitiveName(...) === true) { moderation.recordNameRejection(); throw ...; }` 以满足 strict TS。

- [ ] **Step 4: 运行测试确认通过**

```bash
npm run build && node --test dist/chat/room-gateway.test.js && npm run check
```

预期：PASS（含既有房间网关测试回归）。

- [ ] **Step 5: 提交**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git add lobby-service/src/chat/room-gateway.ts lobby-service/src/chat/room-gateway.test.ts
git commit -m "feat(lobby): reject sensitive player names in room chat hello"
```

---

### Task 11: 面板开关即时生效 + 状态展示数据（settings PATCH/GET）

**Files:**
- Modify: `src/app.ts`（settings PATCH body 解析 :807-846、mutation 回调、buildServerAdminSettingsResponse :1815）
- Test: `src/app.integration.test.ts`

- [ ] **Step 1: 写失败的集成测试**

`src/app.integration.test.ts` 末尾追加（`hashServerAdminPassword` 已在该文件使用，照现有 import 即可）：

```ts
test("admin settings expose and toggle the sensitive filter at runtime", async () => {
  const password = "sensitive-toggle-password";
  const base = testConfig({
    port: 0,
    sensitiveLexiconDir: writeTestLexicon(),
    serverAdminPasswordHash: hashServerAdminPassword(password),
  });
  const config = {
    ...base,
    chat: { ...base.chat, features: { ...base.chat.features, serverChatEnabled: true } },
  };
  const service = await createLobbyService(config);
  const address = await service.start();
  let socket: WebSocket | undefined;
  try {
    const login = await fetch(`http://127.0.0.1:${address.port}/server-admin/login`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ username: "admin", password }),
    });
    assert.equal(login.status, 200);
    const loginBody = await login.json() as { csrfToken: string };
    const cookie = login.headers.get("set-cookie")?.split(";", 1)[0];
    assert.ok(cookie);
    const authHeaders = { cookie, "x-csrf-token": loginBody.csrfToken };

    // GET 响应带 sensitiveFilter 状态。
    const initial = await fetch(`http://127.0.0.1:${address.port}/server-admin/settings`, {
      headers: authHeaders,
    });
    assert.equal(initial.status, 200);
    const initialSettings = await initial.json() as {
      sensitiveFilterEnabled: boolean;
      sensitiveFilter: {
        enabled: boolean; wordCount: number; maskedMessages: number; rejectedNames: number; loadError: string | null;
      };
    };
    assert.equal(initialSettings.sensitiveFilterEnabled, true);
    assert.equal(initialSettings.sensitiveFilter.enabled, true);
    assert.equal(initialSettings.sensitiveFilter.wordCount, 2);
    assert.equal(initialSettings.sensitiveFilter.loadError, null);

    // 开启状态下建房被拒绝。
    const rejected = await fetch(`http://127.0.0.1:${address.port}/rooms`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        roomName: "敏感词房",
        hostPlayerName: "Host",
        gameMode: "standard",
        version: "1.0.0",
        modVersion: "1.0.0",
        modList: [],
        maxPlayers: 4,
        hostConnectionInfo: { enetPort: 7777, localAddresses: ["127.0.0.1"] },
      }),
    });
    assert.equal(rejected.status, 400);

    // 面板关闭开关，即时生效：同名可建。
    const patchOff = await fetch(`http://127.0.0.1:${address.port}/server-admin/settings`, {
      method: "PATCH",
      headers: { "content-type": "application/json", ...authHeaders },
      body: JSON.stringify({ sensitiveFilterEnabled: false }),
    });
    assert.equal(patchOff.status, 200);
    const offSettings = await patchOff.json() as { sensitiveFilterEnabled: boolean; sensitiveFilter: { enabled: boolean; rejectedNames: number } };
    assert.equal(offSettings.sensitiveFilterEnabled, false);
    assert.equal(offSettings.sensitiveFilter.enabled, false);
    assert.equal(offSettings.sensitiveFilter.rejectedNames, 1);

    const allowed = await fetch(`http://127.0.0.1:${address.port}/rooms`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        roomName: "敏感词房",
        hostPlayerName: "Host",
        gameMode: "standard",
        version: "1.0.0",
        modVersion: "1.0.0",
        modList: [],
        maxPlayers: 4,
        hostConnectionInfo: { enetPort: 7777, localAddresses: ["127.0.0.1"] },
      }),
    });
    assert.equal(allowed.status, 201);

    // 再打开，拒绝恢复。
    const patchOn = await fetch(`http://127.0.0.1:${address.port}/server-admin/settings`, {
      method: "PATCH",
      headers: { "content-type": "application/json", ...authHeaders },
      body: JSON.stringify({ sensitiveFilterEnabled: true }),
    });
    assert.equal(patchOn.status, 200);
    const rejectedAgain = await fetch(`http://127.0.0.1:${address.port}/rooms`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        roomName: "敏感词房二号",
        hostPlayerName: "Host",
        gameMode: "standard",
        version: "1.0.0",
        modVersion: "1.0.0",
        modList: [],
        maxPlayers: 4,
        hostConnectionInfo: { enetPort: 7777, localAddresses: ["127.0.0.1"] },
      }),
    });
    assert.equal(rejectedAgain.status, 400);

    // 非布尔值被拒绝。
    const patchInvalid = await fetch(`http://127.0.0.1:${address.port}/server-admin/settings`, {
      method: "PATCH",
      headers: { "content-type": "application/json", ...authHeaders },
      body: JSON.stringify({ sensitiveFilterEnabled: "yes" }),
    });
    assert.equal(patchInvalid.status, 400);
  } finally {
    socket?.terminate();
    await service.close();
    cleanupTempDir(config);
  }
});
```

- [ ] **Step 2: 运行测试确认失败**

```bash
npm run build && node --test dist/app.integration.test.js --test-name-pattern="sensitive filter at runtime"
```

预期：FAIL（`sensitiveFilterEnabled`/`sensitiveFilter` 字段不存在）。

- [ ] **Step 3: 实现 settings PATCH 与响应**

`src/app.ts`：

① PATCH 路由的 body 类型（:807-814）加字段：

```ts
        sensitiveFilterEnabled?: boolean;
```

② 在 `chatFeatures` 解析块之后加：

```ts
      if (body && Object.hasOwn(body, "sensitiveFilterEnabled")) {
        if (typeof body.sensitiveFilterEnabled !== "boolean") {
          throw new InputError("sensitiveFilterEnabled 必须为布尔值。");
        }
        update.sensitiveFilterEnabled = body.sensitiveFilterEnabled;
      }
```

③ mutation 回调中（`await applyChatGovernance(committed.chatFeatures);` 之后）加：

```ts
        moderation.setEnabled(committed.sensitiveFilterEnabled);
```

④ `buildServerAdminSettingsResponse`（:1815）返回对象加一行：

```ts
      sensitiveFilter: moderation.stats(),
```

- [ ] **Step 4: 运行测试确认通过**

```bash
npm run build && node --test dist/app.integration.test.js && npm run check
```

预期：全部 PASS。

- [ ] **Step 5: 提交**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git add lobby-service/src/app.ts lobby-service/src/app.integration.test.ts
git commit -m "feat(lobby): expose sensitive filter toggle and stats via admin settings"
```

---

### Task 12: 管理面板 UI（server-admin-ui.ts）

**Files:**
- Modify: `src/server-admin-ui.ts`（initialValues :2213 附近、modSyncEnabled Form.Item :2243 附近、helper 区）
- Test: `src/server-admin-ui.test.ts`（如其中已有「页面包含某开关标签」类断言，照模式补一条）

- [ ] **Step 1: 加开关与状态展示**

`src/server-admin-ui.ts`：

① 在 helper 函数区（`normalizeChatFeatures` 定义附近）加：

```js
function sensitiveFilterExtra(filter) {
  if (!filter) {
    return "默认开启。聊天内容命中敏感词时自动以 * 替代；用户名、房间名等名称类字段命中时会被拒绝。";
  }
  if (filter.loadError) {
    return `词库不可用（${filter.loadError}），过滤当前不生效，请检查服务端 lexicon 目录。`;
  }
  return `词库 ${filter.wordCount} 词 · 已打码 ${filter.maskedMessages} 条消息 · 已拒绝 ${filter.rejectedNames} 个名称`;
}
```

② `initialValues`（:2213 附近，`modSyncEnabled: settings.modSyncEnabled !== false,` 之后）加：

```js
                                sensitiveFilterEnabled: settings.sensitiveFilter ? settings.sensitiveFilter.enabled !== false : true,
```

③ 在 `modSyncEnabled` 的 `Form.Item` 块（:2243 附近）之后插入新的开关：

```js
                            h(
                              Form.Item,
                              {
                                name: "sensitiveFilterEnabled",
                                label: "敏感词过滤",
                                extra: sensitiveFilterExtra(settings.sensitiveFilter),
                              },
                              h(ToggleButton, { activeLabel: "已开启", inactiveLabel: "已关闭" })
                            ),
```

> `handleSave` 提交时用 `...values` 展开表单值，`sensitiveFilterEnabled` 会自动进入 PATCH body，无需改提交逻辑。

- [ ] **Step 2: 补 UI 测试（按现有断言模式）**

在 `src/server-admin-ui.test.ts` 中照现有「渲染页面包含 X」断言的写法追加一条：

```ts
test("admin page renders the sensitive filter toggle", () => {
  const html = renderServerAdminPage("0.5.3-test"); // 以本文件现有的调用方式为准
  assert.ok(html.includes("敏感词过滤"));
  assert.ok(html.includes("sensitiveFilterEnabled"));
});
```

> 若该文件渲染函数签名不同，以文件内现有测试的调用方式为准。

- [ ] **Step 3: 运行测试确认通过**

```bash
npm run build && node --test dist/server-admin-ui.test.js && npm run check
```

预期：PASS。

- [ ] **Step 4: 提交**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git add lobby-service/src/server-admin-ui.ts lobby-service/src/server-admin-ui.test.ts
git commit -m "feat(lobby): add sensitive filter toggle to admin dashboard"
```

---

### Task 13: 打包、版本与发布物

**Files:**
- Modify: `package.json`（version 0.5.2 → 0.5.3）
- Modify: `../scripts/package-lobby-service.sh`（EXPECTED_MANIFEST heredoc :65 附近）
- Modify: `src/package-content.test.ts`（expectedFiles :27 附近）
- Modify: `deploy/.env.example`、`deploy/lobby-service.docker.env.example`（如存在；补同样的两个 env 注释）

- [ ] **Step 1: 版本号 bump**

`package.json`：`"version": "0.5.2"` → `"version": "0.5.3"`。

- [ ] **Step 2: 更新打包清单**

① `scripts/package-lobby-service.sh` 的 EXPECTED_MANIFEST heredoc 中，`lobby-service/src/...` 条目区域按字母序加入：

```text
lobby-service/lexicon/SOURCES.md
lobby-service/lexicon/ads.txt
lobby-service/lexicon/misc.txt
lobby-service/lexicon/politics.txt
lobby-service/lexicon/porn.txt
lobby-service/lexicon/violence.txt
lobby-service/src/moderation/dfa.ts
lobby-service/src/moderation/filter.ts
lobby-service/src/moderation/lexicon-loader.ts
lobby-service/src/moderation/moderation-service.ts
lobby-service/src/moderation/normalize.ts
```

② 检查脚本中实际拷贝逻辑（搜 `cp`/`rsync`/`src`）：若按目录拷贝（如 `cp -R lobby-service/src`），只需确认 `lexicon/` 也被拷入 staging——若没有，照 `src` 的拷贝方式补一行 `cp -R "$SOURCE_DIR/lobby-service/lexicon" "$PACKAGE_ROOT/lobby-service/lexicon"`。

③ `src/package-content.test.ts` 的 `expectedFiles` 数组按字母序加入与 ① 相同的 11 行。

- [ ] **Step 3: 更新部署 env 模板**

在 `deploy/.env.example` 与 `deploy/lobby-service.docker.env.example`（如文件存在）中追加与 Task 6 Step 4 相同的两行注释。

- [ ] **Step 4: 运行打包内容测试**

```bash
npm run build && node --test dist/package-content.test.js
```

预期：PASS（该测试会真实执行打包脚本并比对清单；若失败，按输出补齐遗漏文件）。

- [ ] **Step 5: 提交**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git add lobby-service/package.json scripts/package-lobby-service.sh lobby-service/src/package-content.test.ts lobby-service/deploy/
git commit -m "chore(lobby): bump to 0.5.3 and ship lexicon in service package"
```

---

### Task 14: 全量回归

- [ ] **Step 1: 全量检查**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby/lobby-service
npm run check && npm test
```

预期：类型检查通过，全部测试 PASS（包括既有全部回归 + 本计划新增）。

- [ ] **Step 2: 真实词库冒烟**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby/lobby-service
npm run build
node -e "
const { ModerationService } = await import('./dist/moderation/moderation-service.js');
const service = ModerationService.create({ lexiconDir: './lexicon', enabled: true, log: console.log });
console.log('wordCount:', service.wordCount);
console.log('mask 敏感词样例:', JSON.stringify(service.maskChatText('这是一段正常聊天')));
if (service.wordCount < 5000) { throw new Error('词库加载条数异常，请检查 lexicon/'); }
" --input-type=module
```

预期：wordCount ≥ 5000（数万条词汇合并去重后的量级），无 loadError。

> 注意不要在冒烟脚本里放真实敏感词断言到终端输出；用词表首个词做一次 contains 自检即可：`const firstWord = ...`。若 wordCount 明显偏小（如只有几百），检查 Task 1 的合并命令是否丢文件。

- [ ] **Step 3: 最终提交（如有冒烟中发现并修复的问题）**

```bash
cd /Users/mac/Desktop/STS2-Game-Lobby
git status
```

预期：工作区干净（或把修复一并提交）。

---

## 附：设计决策备忘（执行中不要偏离）

1. 打码是等量替换，不改变字符串长度——wire 预算、去重逻辑不受影响。
2. 名称拒绝统一 `InputError`（400 `invalid_request` + `details.reason: "sensitive_content"`），message 固定为「包含敏感词内容，请修改后重试」。WS hello 拒绝复用现有 `RoomEnvelopeError("invalid_message", ...)` 路径，不新增 wire 错误码。
3. 不过滤：管理员 displayName/announcements、房间密码、gameMode/version/modList 等结构化字段、仅写日志的字段。
4. 词库加载失败 fail-open（0 词运行 + 面板显示「词库加载失败」），绝不阻断服务启动。
5. 统计自进程启动起累计，不持久化。
6. 开关状态：env 只种子第一次，之后以 `data/server-admin.json` 为准。
