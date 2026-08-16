import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { assertJoinerCompatible, parseProtocolOffer, selectRoomProtocol } from "./protocol-capabilities.js";

const hasCode = (code: string) => (error: unknown) => Boolean(error && typeof error === "object" && "code" in error && error.code === code);
const offer = (ritsuLibPresent: boolean, sidecar = ritsuLibPresent) => ({
  lanProtocolMin: 1,
  lanProtocolMax: 1,
  clientVersion: "0.6.0-alpha.1",
  ritsuLibPresent,
  ritsuLibSidecarAvailable: sidecar,
});
const policy = (profile: "compat_4_5_v1" | "tail_v1", ritsuSignature = "aabb") => ({
  profile,
  lanProtocolMin: 1,
  lanProtocolMax: 1,
  maxPlayers: 8,
  gameVersion: "0.110.1",
  wireCacheSignatureV1: ritsuSignature,
});

test("freezes both homogeneous Tail carriers and rejects presence mismatch in both directions", () => {
  const ritsu = selectRoomProtocol(offer(true), policy("tail_v1"));
  const standalone = selectRoomProtocol(offer(false), policy("tail_v1"));
  assert.equal(ritsu.carrier, "ritsulib_sidecar_v1");
  assert.equal(standalone.carrier, "standalone_tail_v1");
  assert.doesNotThrow(() => assertJoinerCompatible(ritsu, offer(true)));
  assert.doesNotThrow(() => assertJoinerCompatible(standalone, offer(false)));
  assert.throws(() => assertJoinerCompatible(ritsu, offer(false)), hasCode("ritsulib_presence_mismatch"));
  assert.throws(() => assertJoinerCompatible(standalone, offer(true)), hasCode("ritsulib_presence_mismatch"));
  assert.match(ritsu.capabilityDigest, /^[0-9a-f]{64}$/);
  assert.equal(Object.isFrozen(ritsu), true);
});

test("fails closed for unavailable sidecar and incompatible LAN ranges", () => {
  assert.throws(() => selectRoomProtocol(offer(true, false), policy("tail_v1")), hasCode("ritsulib_sidecar_unavailable"));
  const ritsu = selectRoomProtocol(offer(true), policy("tail_v1"));
  assert.throws(() => assertJoinerCompatible(ritsu, offer(true, false)), hasCode("ritsulib_sidecar_unavailable"));
  assert.throws(
    () => selectRoomProtocol({ ...offer(false), lanProtocolMin: 2, lanProtocolMax: 2 }, policy("tail_v1")),
    hasCode("lan_protocol_version_mismatch"),
  );
  assert.throws(() => selectRoomProtocol(offer(true), policy("compat_4_5_v1")), hasCode("ritsulib_not_allowed_in_compat_mode"));
});

test("strictly parses bounded capability offers", () => {
  assert.deepEqual(parseProtocolOffer(offer(false)), offer(false));
  assert.throws(() => parseProtocolOffer({ ...offer(false), unexpected: true }));
  assert.throws(() => parseProtocolOffer({ ...offer(false), ritsuLibSidecarAvailable: true }));
  assert.throws(() => parseProtocolOffer({ ...offer(false), clientVersion: "" }));
});

test("preserves case-sensitive Base64URL wire cache signatures", () => {
  const signature = "wcv1:D5-qRxko7ywoZJWzaOM9Q49NNOWP1Jr2qXc_Nk204uU";
  const selection = selectRoomProtocol(offer(false), policy("tail_v1", `  ${signature}  `));

  assert.equal(selection.wireCacheSignature, signature);
});

test("matches reviewed capability digest vectors", () => {
  const url = new URL("../../test-fixtures/protocol/v0.6/capability-digest-v1.json", import.meta.url);
  const vectors = JSON.parse(readFileSync(url, "utf8")) as Array<{
    name: string;
    profile: "compat_4_5_v1" | "tail_v1";
    offer: ReturnType<typeof offer>;
    policy: ReturnType<typeof policy>;
    expectedDigest: string;
  }>;
  for (const vector of vectors) {
    assert.equal(selectRoomProtocol(vector.offer, vector.policy).capabilityDigest, vector.expectedDigest, vector.name);
  }
});
