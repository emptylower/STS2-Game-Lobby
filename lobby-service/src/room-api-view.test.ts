import assert from "node:assert/strict";
import test from "node:test";
import { selectRoomProtocol } from "./protocol-capabilities.js";
import { toRoomApiView, toUnversionedRoomApiView } from "./room-api-view.js";

const selection = selectRoomProtocol(
  {
    lanProtocolMin: 0,
    lanProtocolMax: 0,
    clientVersion: "0.5.5",
    ritsuLibPresent: false,
    ritsuLibSidecarAvailable: false,
  },
  { profile: "compat_4_5_v1", lanProtocolMin: 1, lanProtocolMax: 1, maxPlayers: 8, gameVersion: "0.110.1" },
);
const room = { roomId: "room", protocolProfileV2: "compat_4_5_v1" as const, protocolSelection: selection };

test("projects old create/join views without making legacy strings internal state", () => {
  assert.deepEqual(toRoomApiView(room, "compat_0_3_0_5"), {
    roomId: "room",
    protocolProfile: "extended_8p",
  });
});

test("canonical and unversioned views expose frozen selection", () => {
  const canonical = toRoomApiView(room, "canonical_0_6_plus");
  assert.equal(canonical.protocolProfile, "compat_4_5_v1");
  assert.equal(canonical.protocolSelection, selection);
  const unversioned = toUnversionedRoomApiView(room);
  assert.equal(unversioned.protocolProfile, "extended_8p");
  assert.equal(unversioned.protocolSelection, selection);
});
