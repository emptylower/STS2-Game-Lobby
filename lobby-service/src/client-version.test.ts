import assert from "node:assert/strict";
import test from "node:test";
import { classifyClientVersion, normalizeClientVersion, parseClientVersion } from "./client-version.js";

const hasCode = (code: string) => (error: unknown) => Boolean(error && typeof error === "object" && "code" in error && error.code === code);

test("classifies supported generations and rejects clients older than 0.3", () => {
  assert.equal(classifyClientVersion(undefined, "0.5.5"), "compat_0_3_0_5");
  assert.equal(classifyClientVersion("0.6.0-alpha.1", "0.6.0-alpha.1"), "canonical_0_6_plus");
  assert.throws(() => classifyClientVersion(undefined, "0.2.2"), hasCode("client_update_required"));
  assert.throws(() => classifyClientVersion(undefined, "garbage"), hasCode("client_update_required"));
});

test("requires an exact explicit client version for canonical callers", () => {
  assert.throws(() => classifyClientVersion(undefined, "0.6.0-alpha.1"), hasCode("client_update_required"));
  assert.throws(() => classifyClientVersion("0.6.0-alpha.2", "0.6.0-alpha.1"), hasCode("client_update_required"));
  assert.equal(normalizeClientVersion("1.0.0", "1.0.0"), "1.0.0");
});

test("uses strict SemVer-shaped signals without extracting embedded digits", () => {
  assert.equal(parseClientVersion("0.6.0-alpha.1").canonical, "0.6.0-alpha.1");
  assert.throws(() => parseClientVersion("v0.6.0"), hasCode("client_update_required"));
  assert.throws(() => parseClientVersion("mod-0.6.0"), hasCode("client_update_required"));
});
