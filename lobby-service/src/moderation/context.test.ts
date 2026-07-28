import assert from "node:assert/strict";
import test from "node:test";
import { normalizeModerationContext, prepareModerationMatches } from "./context.js";
import type { SensitiveMatch } from "./filter.js";

test("context candidates keep sentence boundaries and semantic punctuation", () => {
  const text = "前一句。这里讨论敏感词，不是在使用它！下一句";
  const start = text.indexOf("敏感词");
  const match: SensitiveMatch = {
    termId: "term_1",
    normalizedTerm: "敏感词",
    displayTerm: "敏感词",
    category: "misc",
    source: "misc.txt",
    start,
    end: start + 3,
  };
  const prepared = prepareModerationMatches(text, [match], (value) => `sig:${value}`);
  assert.equal(prepared[0]?.context.text, "这里讨论敏感词，不是在使用它！");
  assert.equal(prepared[0]?.context.normalizedText, "这里讨论敏感词,不是在使用它!");
  assert.equal(prepared[0]?.context.signature, "sig:这里讨论敏感词,不是在使用它!");
});

test("context normalization preserves word order while folding width and whitespace", () => {
  assert.equal(normalizeModerationContext("  ＡＢＣ\t测试  "), "abc 测试");
});
