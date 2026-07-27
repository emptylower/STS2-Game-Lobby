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

test("mask never changes string length for NFC-stable input", () => {
  const filter = filterWith(["敏感词"]);
  const input = "前敏感词后";
  const result = filter.mask(input);
  assert.equal(result.text.length, input.length);
});

test("mask applies spans to the NFC-normalized text so non-NFC input stays aligned", () => {
  // "Á" 是两个 UTF-16 unit，NFC 后合并为 "Á"（一个 unit）。
  // 打码必须作用于 NFC 串，否则 span 下标会与原串错位。
  const filter = filterWith(["敏感词"]);
  const result = filter.mask("Á敏感词");
  assert.deepEqual(result, { text: "Á***", masked: true });
});
