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

test("find returns longest non-overlapping original spans with stable fallback metadata", () => {
  const filter = filterWith(["敏感", "敏感词"]);
  assert.deepEqual(filter.find("说敏 感词吧"), [{
    termId: "term_敏感词",
    normalizedTerm: "敏感词",
    displayTerm: "敏 感词",
    category: "unknown",
    source: "unknown",
    start: 1,
    end: 5,
  }]);
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

test("ASCII words require non-ASCII boundaries so English text is not false-positived", () => {
  const filter = filterWith(["av", "sm", "da", "test"]);
  // 嵌入更长 ASCII 序列时不命中。
  assert.equal(filter.contains("have"), false);
  assert.equal(filter.contains("small"), false);
  assert.equal(filter.contains("standard"), false);
  assert.equal(filter.contains("data"), false);
  assert.equal(filter.contains("testing"), false);
  // 独立出现或被非 ASCII 包围时命中。
  assert.equal(filter.contains("看 av 片"), true);
  assert.equal(filter.contains("这是test"), true);
  assert.deepEqual(filter.mask("看 av 片"), { text: "看 ** 片", masked: true });
});

test("mixed CJK-ASCII words only check the ASCII edge", () => {
  const filter = filterWith(["bt种子"]);
  assert.equal(filter.contains("看bt种子"), true);
  assert.equal(filter.contains("abt种子"), false);
});

test("pure CJK words keep substring matching without boundary rules", () => {
  const filter = filterWith(["敏感词"]);
  assert.equal(filter.contains("这是敏感词啊"), true);
});

test("single ASCII letter word only matches standalone", () => {
  const filter = filterWith(["b"]);
  assert.equal(filter.contains("black"), false);
  assert.equal(filter.contains("a b c"), true); // "a b c" 中 b 与 a/c 之间有空白间隙 → 存在边界 → 命中
  assert.equal(filter.contains("甲b乙"), true);
});

test("mask applies spans to the NFC-normalized text so non-NFC input stays aligned", () => {
  // "Á" 是两个 UTF-16 unit，NFC 后合并为 "Á"（一个 unit）。
  // 打码必须作用于 NFC 串，否则 span 下标会与原串错位。
  const filter = filterWith(["敏感词"]);
  const result = filter.mask("Á敏感词");
  assert.deepEqual(result, { text: "Á***", masked: true });
});

test("ASCII boundary respects gaps from stripped whitespace and punctuation", () => {
  const filter = filterWith(["av", "test"]);
  // 原文直接相邻 → 词内部 → 不命中。
  assert.equal(filter.contains("have"), false);
  assert.equal(filter.contains("testing"), false);
  // 间隙（被剔除的空白/标点）构成边界 → 命中。
  assert.equal(filter.contains("av movie"), true);
  assert.equal(filter.contains("a v"), true);
  assert.equal(filter.contains("test room"), true);
  assert.equal(filter.contains("av.movie"), true);
  assert.deepEqual(filter.mask("av movie"), { text: "** movie", masked: true });
});
