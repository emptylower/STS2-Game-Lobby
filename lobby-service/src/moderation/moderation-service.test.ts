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
