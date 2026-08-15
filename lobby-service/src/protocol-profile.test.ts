import assert from "node:assert/strict";
import test from "node:test";
import { parseRequestedProfile, projectProfile } from "./protocol-profile.js";

const hasCode = (code: string) => (error: unknown) => Boolean(error && typeof error === "object" && "code" in error && error.code === code);

test("keeps legacy strings at the API boundary only", () => {
  assert.equal(parseRequestedProfile({ generation: "compat_0_3_0_5", legacyProfile: "extended_8p" }), "compat_4_5_v1");
  assert.equal(projectProfile("compat_4_5_v1", "compat_0_3_0_5"), "extended_8p");
  assert.throws(
    () => parseRequestedProfile({ generation: "compat_0_3_0_5", legacyProfile: "legacy_4p" }),
    hasCode("client_update_required"),
  );
  assert.throws(
    () => parseRequestedProfile({ generation: "canonical_0_6_plus", canonicalProfile: "unknown" }),
    hasCode("protocol_profile_unsupported"),
  );
});

test("canonical callers must choose one of the two canonical profiles", () => {
  assert.equal(parseRequestedProfile({ generation: "canonical_0_6_plus", canonicalProfile: "compat_4_5_v1" }), "compat_4_5_v1");
  assert.equal(parseRequestedProfile({ generation: "canonical_0_6_plus", canonicalProfile: "tail_v1" }), "tail_v1");
  assert.equal(projectProfile("tail_v1", "compat_0_3_0_5"), "extended_8p");
});
