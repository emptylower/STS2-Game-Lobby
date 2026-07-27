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
