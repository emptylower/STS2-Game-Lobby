import test from "node:test";
import assert from "node:assert/strict";
import { LobbyStore, LobbyStoreError } from "./store.js";

const baseConfig = {
  heartbeatTimeoutMs: 35_000,
  ticketTtlMs: 120_000,
};

test("createRoom exposes room summary in listRooms", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "测试房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
        localAddresses: ["192.168.1.10"],
      },
    },
    "203.0.113.10",
  );

  const rooms = store.listRooms();
  assert.equal(rooms.length, 1);
  assert.equal(rooms[0]?.roomId, created.roomId);
  assert.equal(rooms[0]?.requiresPassword, false);
});

test("joinRoom returns direct candidates with public and lan addresses", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "可加入房间",
      hostPlayerName: "Host",
      password: "secret",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
        localAddresses: ["192.168.1.10"],
      },
    },
    "203.0.113.10",
  );

  const joined = store.joinRoom(created.roomId, {
    playerName: "Guest",
    password: "secret",
    version: "1.2.3",
    modVersion: "0.1.0",
  });

  assert.equal(joined.connectionPlan.directCandidates.length, 2);
  assert.equal(joined.connectionPlan.directCandidates[0]?.ip, "203.0.113.10");
  assert.equal(joined.connectionPlan.directCandidates[1]?.ip, "192.168.1.10");
});

test("joinRoom can skip version mismatch when configured", () => {
  const store = new LobbyStore({
    ...baseConfig,
    ignoreVersionMismatch: true,
  });
  const created = store.createRoom(
    {
      roomName: "跨端房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "0.1.0.0",
      modVersion: "1.0.0.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
  );

  const joined = store.joinRoom(created.roomId, {
    playerName: "Guest",
    version: "1.0.0.0",
    modVersion: "1.0.0.0",
  });

  assert.equal(joined.room.roomId, created.roomId);
});

test("joinRoom can force relay-only connection plans", () => {
  const store = new LobbyStore({
    ...baseConfig,
    forceRelayOnly: true,
  });
  const created = store.createRoom(
    {
      roomName: "Relay only 房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
        localAddresses: ["192.168.1.10"],
      },
    },
    "203.0.113.10",
  );

  const joined = store.joinRoom(created.roomId, {
    playerName: "Guest",
    version: "1.2.3",
    modVersion: "0.1.0",
  });

  assert.deepEqual(joined.connectionPlan.directCandidates, []);
});

test("joinRoom rejects wrong password", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "加锁房间",
      hostPlayerName: "Host",
      password: "secret",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
  );

  assert.throws(
    () =>
      store.joinRoom(created.roomId, {
        playerName: "Guest",
        password: "bad",
        version: "1.2.3",
        modVersion: "0.1.0",
      }),
    (error: unknown) =>
      error instanceof LobbyStoreError &&
      error.code === "invalid_password" &&
      error.statusCode === 401,
  );
});

test("cleanupExpired deletes rooms after heartbeat timeout", () => {
  const store = new LobbyStore(baseConfig);
  const now = new Date("2026-03-10T00:00:00.000Z");
  const created = store.createRoom(
    {
      roomName: "会过期房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
    now,
  );

  const deleted = store.cleanupExpired(new Date(now.getTime() + 40_000));
  assert.deepEqual(deleted, [created.roomId]);
  assert.equal(store.listRooms().length, 0);
});

test("deleted rooms accept stale heartbeat and delete during tombstone window", () => {
  const store = new LobbyStore({
    ...baseConfig,
    roomTombstoneMs: 60_000,
  });
  const created = store.createRoom(
    {
      roomName: "删除兜底房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
  );

  store.deleteRoom(created.roomId, created.hostToken);

  const heartbeatRoom = store.heartbeat(created.roomId, {
    hostToken: created.hostToken,
    currentPlayers: 1,
    status: "open",
  });

  assert.equal(heartbeatRoom, null);
  assert.doesNotThrow(() => {
    store.deleteRoom(created.roomId, created.hostToken);
  });
});

test("saved run rooms expose slot occupancy and allow selecting an available slot", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "续局房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
      savedRun: {
        saveKey: "save-key-1",
        slots: [
          { netId: "1", characterId: "IRONCLAD", characterName: "铁甲战士", isHost: true },
          { netId: "222", characterId: "SILENT", characterName: "静默猎手", isHost: false },
        ],
        connectedPlayerNetIds: ["1"],
      },
    },
    "203.0.113.10",
  );

  const rooms = store.listRooms();
  assert.equal(rooms[0]?.savedRun?.slots[0]?.isConnected, true);
  assert.equal(rooms[0]?.savedRun?.slots[1]?.isConnected, false);

  const joined = store.joinRoom(created.roomId, {
    playerName: "Guest",
    version: "1.2.3",
    modVersion: "0.1.0",
    desiredSavePlayerNetId: "222",
  });

  assert.equal(joined.room.savedRun?.saveKey, "save-key-1");
});

test("saved run rooms reject occupied or ambiguous slot joins", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "续局房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
      savedRun: {
        saveKey: "save-key-2",
        slots: [
          { netId: "1", characterId: "IRONCLAD", characterName: "铁甲战士", isHost: true },
          { netId: "222", characterId: "SILENT", characterName: "静默猎手", isHost: false },
          { netId: "333", characterId: "DEFECT", characterName: "故障体", isHost: false },
        ],
        connectedPlayerNetIds: ["1"],
      },
    },
    "203.0.113.10",
  );

  assert.throws(
    () =>
      store.joinRoom(created.roomId, {
        playerName: "Guest",
        version: "1.2.3",
        modVersion: "0.1.0",
        desiredSavePlayerNetId: "999",
      }),
    (error: unknown) =>
      error instanceof LobbyStoreError &&
      error.code === "save_slot_invalid" &&
      error.statusCode === 409,
  );

  assert.throws(
    () =>
      store.joinRoom(created.roomId, {
        playerName: "Guest",
        version: "1.2.3",
        modVersion: "0.1.0",
      }),
    (error: unknown) =>
      error instanceof LobbyStoreError &&
      error.code === "save_slot_required" &&
      error.statusCode === 409,
  );
});
