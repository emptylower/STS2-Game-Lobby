import test from "node:test";
import assert from "node:assert/strict";
import { LobbyStore, LobbyStoreError } from "./store.js";

const baseConfig = {
  heartbeatTimeoutMs: 35_000,
  ticketTtlMs: 120_000,
  strictGameVersionCheck: true,
  strictModVersionCheck: true,
  connectionStrategy: "direct-first" as const,
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

test("createRoom infers legacy_4p protocol profile for old 0.2.2 four-player rooms", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "旧版本四人房",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.2.2",
      modList: ["sts2_lan_connect"],
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
  );

  assert.equal(created.room.protocolProfile, "legacy_4p");

  const joined = store.joinRoom(created.roomId, {
    playerName: "Guest",
    version: "1.2.3",
    modVersion: "0.2.2",
    modList: ["sts2_lan_connect"],
  });
  assert.equal(joined.room.protocolProfile, "legacy_4p");
});

test("createRoom infers legacy_4p protocol profile for prefixed 0.2.2 mod versions", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "跨端旧版本四人房",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "iOS.0.2.2",
      modList: ["sts2_lan_connect"],
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
  );

  assert.equal(created.room.protocolProfile, "legacy_4p");
});

test("createRoom preserves explicit protocol profile and echoes it in joins", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "扩展协议房",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.2.3",
      protocolProfile: "extended_8p",
      maxPlayers: 8,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
  );

  assert.equal(created.room.protocolProfile, "extended_8p");
  assert.equal(store.listRooms()[0]?.protocolProfile, "extended_8p");

  const joined = store.joinRoom(created.roomId, {
    playerName: "Guest",
    version: "1.2.3",
    modVersion: "0.2.3",
  });
  assert.equal(joined.room.protocolProfile, "extended_8p");
});

test("createRoom does not infer legacy_4p when the RMP mod is advertised", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "RMP四人房",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.2.2",
      modList: ["RemoveMultiplayerPlayerLimit"],
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
  );

  assert.equal(created.room.protocolProfile, "extended_8p");
});

test("listRooms stays stable when heartbeat updates last seen time", () => {
  const store = new LobbyStore(baseConfig);
  const olderCreatedAt = new Date("2026-03-14T08:00:00.000Z");
  const newerCreatedAt = new Date("2026-03-14T08:01:00.000Z");
  const older = store.createRoom(
    {
      roomName: "较早房间",
      hostPlayerName: "Host-A",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
    olderCreatedAt,
  );
  const newer = store.createRoom(
    {
      roomName: "较新房间",
      hostPlayerName: "Host-B",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33772,
      },
    },
    "203.0.113.11",
    newerCreatedAt,
  );

  store.heartbeat(older.roomId, {
    hostToken: older.hostToken,
    currentPlayers: 1,
    status: "open",
  }, new Date("2026-03-14T08:10:00.000Z"));

  const rooms = store.listRooms();
  assert.deepEqual(rooms.map((room) => room.roomId), [newer.roomId, older.roomId]);
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

test("joinRoom rejects rooms that already started", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "已开局房间",
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

  store.heartbeat(created.roomId, {
    hostToken: created.hostToken,
    currentPlayers: 2,
    status: "starting",
  });

  assert.throws(
    () =>
      store.joinRoom(created.roomId, {
        playerName: "Guest",
        version: "1.2.3",
        modVersion: "0.1.0",
      }),
    (error: unknown) =>
      error instanceof LobbyStoreError &&
      error.code === "room_started" &&
      error.statusCode === 409,
  );
});

test("saved run rooms can still join after status becomes starting when a slot is available", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "续局重连房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
      savedRun: {
        saveKey: "save-key-rejoin",
        slots: [
          { netId: "1", characterId: "IRONCLAD", characterName: "铁甲战士", isHost: true },
          { netId: "222", characterId: "SILENT", characterName: "静默猎手", isHost: false },
        ],
        connectedPlayerNetIds: ["1"],
      },
    },
    "203.0.113.10",
  );

  store.heartbeat(created.roomId, {
    hostToken: created.hostToken,
    currentPlayers: 1,
    status: "starting",
    connectedPlayerNetIds: ["1"],
  });

  const joined = store.joinRoom(created.roomId, {
    playerName: "Guest",
    version: "1.2.3",
    modVersion: "0.1.0",
    desiredSavePlayerNetId: "222",
  });

  assert.equal(joined.room.roomId, created.roomId);
  assert.equal(joined.room.status, "open");
});

test("saved run rooms surface as open in room list when they still have reconnect slots", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "续局大厅展示房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
      savedRun: {
        saveKey: "save-key-list",
        slots: [
          { netId: "1", characterId: "IRONCLAD", characterName: "铁甲战士", isHost: true },
          { netId: "222", characterId: "SILENT", characterName: "静默猎手", isHost: false },
        ],
        connectedPlayerNetIds: ["1"],
      },
    },
    "203.0.113.10",
  );

  store.heartbeat(created.roomId, {
    hostToken: created.hostToken,
    currentPlayers: 1,
    status: "starting",
    connectedPlayerNetIds: ["1"],
  });

  const rooms = store.listRooms();
  assert.equal(rooms[0]?.status, "open");
  assert.equal(rooms[0]?.savedRun?.slots[1]?.isConnected, false);
});

test("relaxed compatibility can skip game and mod version checks", () => {
  const store = new LobbyStore({
    ...baseConfig,
    strictGameVersionCheck: false,
    strictModVersionCheck: false,
  });
  const created = store.createRoom(
    {
      roomName: "测试兼容房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "2026.3.13-mobile",
      modVersion: "cross-end-preview",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
  );

  const joined = store.joinRoom(created.roomId, {
    playerName: "Guest",
    version: "2026.3.13-pc",
    modVersion: "test-build",
  });

  assert.equal(joined.room.roomId, created.roomId);
});

test("joinRoom returns missing mod details when mod lists differ", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "模组差异房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.0",
      modList: ["BaseMod", "SharedMod", "HostOnlyMod"],
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
        version: "1.2.3",
        modVersion: "0.1.0",
        modList: ["BaseMod", "SharedMod", "LocalOnlyMod"],
      }),
    (error: unknown) =>
      error instanceof LobbyStoreError &&
      error.code === "mod_mismatch" &&
      error.statusCode === 409 &&
      typeof error.details === "object" &&
      error.details !== null &&
      JSON.stringify(error.details) === JSON.stringify({
        roomModVersion: "0.1.0",
        requestedModVersion: "0.1.0",
        missingModsOnLocal: ["HostOnlyMod"],
        missingModsOnHost: ["LocalOnlyMod"],
      }),
  );
});

test("joinRoom treats trailing .0 mod version suffix as equivalent", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "模组版本归一化房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.1.2",
      modList: ["BaseMod", "SharedMod"],
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
  );

  const joined = store.joinRoom(created.roomId, {
    playerName: "Guest",
    version: "1.2.3",
    modVersion: "0.1.2.0",
    modList: ["BaseMod", "SharedMod"],
  });

  assert.equal(joined.room.modVersion, "0.1.2");
});

test("kickPlayer blocks kicked player from re-joining", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "踢人测试房间",
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

  store.kickPlayer(created.roomId, created.hostToken, "12345");
  assert.equal(store.isPlayerKicked(created.roomId, "12345"), true);
  assert.equal(store.isPlayerKicked(created.roomId, "99999"), false);

  assert.throws(
    () =>
      store.joinRoom(created.roomId, {
        playerName: "Kicked Guest",
        version: "1.2.3",
        modVersion: "0.1.0",
        playerNetId: "12345",
      }),
    (error: unknown) =>
      error instanceof LobbyStoreError &&
      error.code === "kicked" &&
      error.statusCode === 403,
  );

  // A different player can still join
  const joined = store.joinRoom(created.roomId, {
    playerName: "Other Guest",
    version: "1.2.3",
    modVersion: "0.1.0",
    playerNetId: "99999",
  });
  assert.equal(joined.room.roomId, created.roomId);
});

test("kickPlayer rejects invalid host token", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "踢人鉴权测试",
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

  assert.throws(
    () => store.kickPlayer(created.roomId, "wrong-token", "12345"),
    (error: unknown) =>
      error instanceof LobbyStoreError &&
      error.code === "invalid_host_token" &&
      error.statusCode === 401,
  );
});

test("updateRoomSettings persists and returns settings", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "设置测试房间",
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

  // Default settings
  const defaults = store.getRoomSettings(created.roomId);
  assert.equal(defaults.chatEnabled, true);

  // Update settings
  const updated = store.updateRoomSettings(created.roomId, created.hostToken, { chatEnabled: false });
  assert.equal(updated.chatEnabled, false);

  // Verify persisted
  const retrieved = store.getRoomSettings(created.roomId);
  assert.equal(retrieved.chatEnabled, false);

  // Toggle back
  const restored = store.updateRoomSettings(created.roomId, created.hostToken, { chatEnabled: true });
  assert.equal(restored.chatEnabled, true);
});

test("getRoomSettings returns default for non-existent room", () => {
  const store = new LobbyStore(baseConfig);
  const settings = store.getRoomSettings("non-existent-room");
  assert.equal(settings.chatEnabled, true);
});

test("joinRoom without playerNetId does not check kicked list", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "不传 netId 房间",
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

  store.kickPlayer(created.roomId, created.hostToken, "12345");

  // Without playerNetId, the kicked check is skipped (backward compat)
  const joined = store.joinRoom(created.roomId, {
    playerName: "Legacy Client",
    version: "1.2.3",
    modVersion: "0.1.0",
  });
  assert.equal(joined.room.roomId, created.roomId);
});

test("relay-only strategy omits direct candidates", () => {
  const store = new LobbyStore({
    ...baseConfig,
    connectionStrategy: "relay-only",
  });
  const created = store.createRoom(
    {
      roomName: "只走 relay",
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

  assert.equal(joined.connectionPlan.strategy, "relay-only");
  assert.deepEqual(joined.connectionPlan.directCandidates, []);
});
