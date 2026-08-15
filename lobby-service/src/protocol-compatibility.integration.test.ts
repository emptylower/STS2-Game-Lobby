import assert from "node:assert/strict";
import test from "node:test";
import { LobbyStore, type CreateRoomInput, type JoinRoomInput } from "./store.js";

const config = {
  heartbeatTimeoutMs: 60_000,
  ticketTtlMs: 30_000,
  strictGameVersionCheck: true,
  strictModVersionCheck: false,
  connectionStrategy: "direct-first" as const,
};

function createInput(maxPlayers: number): CreateRoomInput {
  return {
    roomName: "v0.6 compat",
    hostPlayerName: "Host",
    gameMode: "standard",
    version: "0.110.1",
    modVersion: "0.6.0-alpha.1",
    clientVersion: "0.6.0-alpha.1",
    protocolProfileV2: "compat_4_5_v1",
    protocolOffer: {
      lanProtocolMin: 1,
      lanProtocolMax: 1,
      clientVersion: "0.6.0-alpha.1",
      ritsuLibPresent: false,
      ritsuLibSidecarAvailable: false,
    },
    maxPlayers,
    hostConnectionInfo: { enetPort: 33771 },
  };
}

function joinInput(): JoinRoomInput {
  return {
    playerName: "Guest",
    version: "0.110.1",
    modVersion: "0.6.0-alpha.1",
    clientVersion: "0.6.0-alpha.1",
    protocolOffer: {
      lanProtocolMin: 1,
      lanProtocolMax: 1,
      clientVersion: "0.6.0-alpha.1",
      ritsuLibPresent: false,
      ritsuLibSidecarAvailable: false,
    },
  };
}

test("current compat rooms freeze the 4/5 profile independently of player count", () => {
  for (const maxPlayers of [4, 8]) {
    const store = new LobbyStore(config);
    const created = store.createRoom(createInput(maxPlayers), "127.0.0.1");
    const joined = store.joinRoom(created.roomId, joinInput());

    assert.equal(created.room.protocolProfileV2, "compat_4_5_v1");
    assert.equal(created.room.protocolProfile, "extended_8p");
    assert.equal(created.room.protocolSelection.profile, "compat_4_5_v1");
    assert.equal(created.room.protocolSelection.selectedLanProtocolVersion, 0);
    assert.equal(created.room.protocolSelection.carrier, "none");
    assert.equal(created.room.protocolSelection.ritsuLibPresent, false);
    assert.equal(joined.room.protocolProfileV2, "compat_4_5_v1");
  }
});

test("current compat rooms reject Ritsu before room or ticket allocation", () => {
  const hostStore = new LobbyStore(config);
  const hostInput = createInput(8);
  hostInput.protocolOffer = { ...hostInput.protocolOffer!, ritsuLibPresent: true, ritsuLibSidecarAvailable: true };
  assert.throws(() => hostStore.createRoom(hostInput, "127.0.0.1"), hasCode("ritsulib_not_allowed_in_compat_mode"));
  assert.equal(hostStore.listRooms().length, 0);

  const joinStore = new LobbyStore(config);
  const created = joinStore.createRoom(createInput(8), "127.0.0.1");
  const request = joinInput();
  request.protocolOffer = { ...request.protocolOffer!, ritsuLibPresent: true, ritsuLibSidecarAvailable: true };
  assert.throws(() => joinStore.joinRoom(created.roomId, request), hasCode("ritsulib_not_allowed_in_compat_mode"));
});

function hasCode(code: string) {
  return (error: unknown) => Boolean(error && typeof error === "object" && "code" in error && error.code === code);
}
