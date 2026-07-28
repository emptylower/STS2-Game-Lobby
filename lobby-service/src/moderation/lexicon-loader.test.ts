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

test("single-character words are dropped to avoid false positives", () => {
  const dir = lexiconDir({ "a.txt": "1\n日\n屄\nav\n敏感词\n" });
  const lexicon = loadLexicon(dir);
  // "1"、"日"、"屄" 归一化长度为 1，全部丢弃；"av"（2）与"敏感词"（3）保留。
  assert.equal(lexicon.wordCount, 2);
});
