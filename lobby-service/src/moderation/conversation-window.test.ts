import assert from "node:assert/strict";
import test from "node:test";
import { ModerationConversationWindow } from "./conversation-window.js";
import type { SensitiveMatch } from "./filter.js";

function findXi(text: string): SensitiveMatch[] {
  const start = text.indexOf("习近平");
  return start < 0 ? [] : [{
    termId: "term_xi",
    normalizedTerm: "习近平",
    displayTerm: "习近平",
    category: "test",
    source: "test",
    start,
    end: start + 3,
  }];
}

test("builds a cross-message candidate from one sender's trailing short burst", () => {
  let now = 1_000;
  const window = new ModerationConversationWindow({ now: () => now });
  window.recordCommitted("server:alice", "m1", "习");
  now += 100;
  window.recordCommitted("server:alice", "m2", "近");
  now += 100;

  assert.deepEqual(window.buildCandidate("server:alice", "平", findXi), {
    text: "习近平",
    relatedMessageIds: ["m1", "m2"],
    fragmentCount: 3,
  });
  assert.equal(window.buildCandidate("server:bob", "平", findXi), null);
});

test("long messages and TTL boundaries prevent unrelated conversation joins", () => {
  let now = 1_000;
  const window = new ModerationConversationWindow({ now: () => now, ttlMs: 1_000 });
  window.recordCommitted("sender", "m1", "习");
  window.recordCommitted("sender", "m2", "这是一条普通长消息");
  assert.equal(window.buildCandidate("sender", "近平", findXi), null);

  window.recordCommitted("sender", "m3", "习");
  now += 1_001;
  assert.equal(window.buildCandidate("sender", "近平", findXi), null);
});

test("redacted and blocked fragments remain semantic context without repeating revoke ids", () => {
  const window = new ModerationConversationWindow();
  window.recordCommitted("sender", "m1", "习");
  window.recordCommitted("sender", "m2", "近");
  window.markRedacted("sender", ["m1", "m2"]);
  window.recordBlocked("sender", "平");

  const candidate = window.buildCandidate("sender", "下", (text) => {
    const start = text.indexOf("习近平");
    return start < 0 ? [] : [{ ...findXi("习近平")[0]!, start, end: start + 3 }];
  });
  assert.deepEqual(candidate, {
    text: "习近平下",
    relatedMessageIds: [],
    fragmentCount: 4,
  });
});
