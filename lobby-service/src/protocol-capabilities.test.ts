import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { assertJoinerCompatible, parseProtocolOffer, selectRoomProtocol } from "./protocol-capabilities.js";

const hasCode = (code: string) => (error: unknown) => Boolean(error && typeof error === "object" && "code" in error && error.code === code);
const fingerprint = "sha256:v1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
const otherFingerprint = "sha256:v1:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
const offer = (ritsuLibPresent: boolean, sidecar = ritsuLibPresent) => ({
  lanProtocolMin: 1,
  lanProtocolMax: 1,
  clientVersion: "0.6.1-alpha.1",
  ritsuLibPresent,
  ritsuLibSidecarAvailable: sidecar,
  registryFingerprint: fingerprint,
});
const policy = (profile: "compat_4_5_v1" | "tail_v1", ritsuSignature = "aabb") => ({
  profile,
  lanProtocolMin: 1,
  lanProtocolMax: 1,
  maxPlayers: 8,
  gameVersion: "0.110.1",
  wireCacheSignatureV1: ritsuSignature,
});

test("freezes native_bus_v1 for both presence states and rejects presence mismatch in both directions", () => {
  const ritsu = selectRoomProtocol(offer(true), policy("tail_v1"));
  const standalone = selectRoomProtocol(offer(false), policy("tail_v1"));
  // native_bus_v1：tail_v1 一律 native 载体，完全忽略 Ritsu presence。
  assert.equal(ritsu.carrier, "native_bus_v1");
  assert.equal(standalone.carrier, "native_bus_v1");
  assert.equal(ritsu.registryFingerprint, fingerprint);
  assert.equal(ritsu.minimumClientVersion, "0.6.1-alpha.1");
  assert.doesNotThrow(() => assertJoinerCompatible(ritsu, offer(true)));
  assert.doesNotThrow(() => assertJoinerCompatible(standalone, offer(false)));
  assert.throws(() => assertJoinerCompatible(ritsu, offer(false)), hasCode("ritsulib_presence_mismatch"));
  assert.throws(() => assertJoinerCompatible(standalone, offer(true)), hasCode("ritsulib_presence_mismatch"));
  assert.match(ritsu.capabilityDigest, /^[0-9a-f]{64}$/);
  assert.equal(Object.isFrozen(ritsu), true);
});

test("ignores sidecar availability, requires create fingerprint, and fails closed for bad ranges", () => {
  // 0.5.18 事故正面回归：Ritsu 存在但 sidecar 不可用 => 照常创建/加入（native 载体）。
  const presentButUnavailable = selectRoomProtocol(offer(true, false), policy("tail_v1"));
  assert.equal(presentButUnavailable.carrier, "native_bus_v1");
  assert.doesNotThrow(() => assertJoinerCompatible(presentButUnavailable, offer(true, false)));
  // 创建侧 fingerprint 必填且格式合法（缺失/非法同一码）。
  assert.throws(
    () => selectRoomProtocol({ ...offer(true), registryFingerprint: undefined }, policy("tail_v1")),
    hasCode("lan_registry_fingerprint_required"),
  );
  assert.throws(
    () => selectRoomProtocol({ ...offer(true), registryFingerprint: "sha256:v1:XYZ" }, policy("tail_v1")),
    hasCode("lan_registry_fingerprint_required"),
  );
  assert.throws(
    () => selectRoomProtocol({ ...offer(false), lanProtocolMin: 2, lanProtocolMax: 2 }, policy("tail_v1")),
    hasCode("lan_protocol_version_mismatch"),
  );
  assert.throws(() => selectRoomProtocol(offer(true), policy("compat_4_5_v1")), hasCode("ritsulib_not_allowed_in_compat_mode"));
});

test("strictly parses bounded capability offers", () => {
  assert.deepEqual(parseProtocolOffer(offer(false)), offer(false));
  assert.throws(() => parseProtocolOffer({ ...offer(false), unexpected: true }));
  // presence 与 sidecar 可用性不再耦合（诊断位允许任意组合）。
  assert.doesNotThrow(() => parseProtocolOffer({ ...offer(false), ritsuLibSidecarAvailable: true }));
  assert.throws(() => parseProtocolOffer({ ...offer(false), clientVersion: "" }));
  // 格式非法不在 parse 层拒绝（由 selectRoomProtocol/join 门禁统一 409 lan_registry_fingerprint_required）。
  assert.equal(parseProtocolOffer({ ...offer(false), registryFingerprint: "sha256:v1:abc" }).registryFingerprint, "sha256:v1:abc");
  assert.doesNotThrow(() => parseProtocolOffer({ ...offer(false), ritsuLibVersion: "0.5.18" }));
  assert.throws(() => parseProtocolOffer({ ...offer(false), ritsuLibVersion: "x".repeat(33) }));
  assert.equal(
    parseProtocolOffer({ ...offer(false), registryFingerprint: otherFingerprint }).registryFingerprint,
    otherFingerprint,
  );
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
