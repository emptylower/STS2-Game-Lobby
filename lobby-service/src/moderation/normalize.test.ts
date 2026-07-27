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
  assert.deepEqual(normalizeForMatch("敏​感").chars, ["敏", "感"]);
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
