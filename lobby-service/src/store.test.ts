import test from "node:test";
import assert from "node:assert/strict";
import {
  LobbyStore,
  LobbyStoreError,
  MaxSupportedRoomPlayers,
  type CreateRoomResult,
  type JoinRoomInput,
  type KickPlayerResult,
} from "./store.js";
import type { LobbyModDescriptor } from "./mod-sync/protocol.js";

const baseConfig = {
  heartbeatTimeoutMs: 35_000,
  ticketTtlMs: 120_000,
  strictGameVersionCheck: true,
  strictModVersionCheck: true,
  connectionStrategy: "direct-first" as const,
};

function sequenceIds(...ids: string[]) {
  const queue = [...ids];
  return () => {
    const next = queue.shift();
    if (!next) {
      throw new Error("sequenceIds exhausted");
    }
    return next;
  };
}

function requireKicked(result: KickPlayerResult) {
  assert.equal(result.outcome, "kicked");
  if (result.outcome !== "kicked") {
    throw new Error("Expected kicked outcome");
  }
  return result;
}

function createBasicRoom(store: LobbyStore): CreateRoomResult {
  return store.createRoom(
    {
      roomName: "基础房间",
      hostPlayerName: "Host",
      clientInstallationId: "install-host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.5.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
        localAddresses: ["192.168.1.10"],
      },
    },
    "203.0.113.10",
  );
}

function basicJoinInput(): JoinRoomInput {
  return {
    playerName: "Guest",
    playerNetId: "slot-guest",
    clientInstallationId: "install-guest",
    version: "1.2.3",
    modVersion: "0.5.0",
  };
}

const WireCacheSignatureA = `wcv1:${"a".repeat(43)}`;
const WireCacheSignatureB = `wcv1:${"b".repeat(43)}`;

function createWireCacheRoom(
  store: LobbyStore,
  wireCacheSignatureV1: string | undefined,
): CreateRoomResult {
  return store.createRoom(
    {
      roomName: "网络编码测试房间",
      hostPlayerName: "Host",
      clientInstallationId: "install-host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.5.0",
      wireCacheSignatureV1,
      maxPlayers: 4,
      hostConnectionInfo: { enetPort: 33771 },
    },
    "203.0.113.10",
  );
}

test("create and join share a HostSession roomSessionId", () => {
  const store = new LobbyStore(baseConfig, { id: sequenceIds("room", "control", "room-session", "ticket") });
  const created = createBasicRoom(store);
  const joined = store.joinRoom(created.roomId, basicJoinInput());
  assert.equal(created.roomSessionId, "room-session");
  assert.equal(joined.roomSessionId, created.roomSessionId);
});

test("validateHostControl reconnect preserves HostSession roomSessionId", () => {
  const store = new LobbyStore(baseConfig, {
    id: sequenceIds("room", "control", "room-session", "ticket"),
  });
  const created = createBasicRoom(store);
  const hostSession = store.validateHostControl(created.roomId, created.controlChannelId, created.hostToken);
  assert.equal(hostSession.roomSessionId, created.roomSessionId);
  assert.equal(hostSession.roomSessionId, "room-session");
});

test("republished room creates a new HostSession roomSessionId", () => {
  const store = new LobbyStore(baseConfig, {
    id: sequenceIds(
      "room-a",
      "control-a",
      "session-a",
      "room-b",
      "control-b",
      "session-b",
    ),
  });
  const first = createBasicRoom(store);
  store.deleteRoom(first.roomId, first.hostToken);
  const second = createBasicRoom(store);
  assert.equal(first.roomSessionId, "session-a");
  assert.equal(second.roomSessionId, "session-b");
  assert.notEqual(first.roomSessionId, second.roomSessionId);
});

test("room chat context is a defensive snapshot of Phase 1 authority", () => {
  const peerPlayerNetIds = new Set(["net:host", "net:guest"]);
  const store = new LobbyStore(baseConfig, {
    id: sequenceIds("room", "control", "room-session"),
    peerPlayerNetIds: (roomId, roomSessionId) => {
      assert.equal(roomId, "room");
      assert.equal(roomSessionId, "room-session");
      return peerPlayerNetIds;
    },
  });
  const created = createBasicRoom(store);
  store.updateRoomSettings(created.roomId, created.hostToken, { chatEnabled: false });

  const first = store.getRoomChatContext(created.roomId);
  assert.ok(first);
  assert.equal(first.roomId, created.roomId);
  assert.equal(first.roomSessionId, created.roomSessionId);
  assert.equal(first.chatEnabled, false);
  assert.deepEqual([...first.peerPlayerNetIds], ["net:host", "net:guest"]);

  (first.peerPlayerNetIds as Set<string>).add("net:forged");
  const second = store.getRoomChatContext(created.roomId);
  assert.ok(second);
  assert.notEqual(second, first);
  assert.notEqual(second.peerPlayerNetIds, first.peerPlayerNetIds);
  assert.deepEqual([...second.peerPlayerNetIds], ["net:host", "net:guest"]);
  assert.deepEqual([...peerPlayerNetIds], ["net:host", "net:guest"]);

  store.deleteRoom(created.roomId, created.hostToken);
  assert.equal(store.getRoomChatContext(created.roomId), undefined);
});

test("room chat context defaults to an empty peer snapshot without a registry dependency", () => {
  const store = new LobbyStore(baseConfig, {
    id: sequenceIds("room", "control", "room-session", "ticket"),
  });
  const created = createBasicRoom(store);
  store.joinRoom(created.roomId, { ...basicJoinInput(), playerNetId: "net:guest" });

  const context = store.getRoomChatContext(created.roomId);
  assert.ok(context);
  assert.deepEqual([...context.peerPlayerNetIds], []);
});

test("room list summaries do not expose roomSessionId", () => {
  const store = new LobbyStore(baseConfig, {
    id: sequenceIds("room", "control", "room-session"),
  });
  const created = createBasicRoom(store);
  const rooms = store.listRooms();
  assert.equal(rooms.length, 1);
  assert.equal(rooms[0]?.roomId, created.roomId);
  assert.equal(Object.hasOwn(rooms[0] as object, "roomSessionId"), false);
});

test("createRoom exposes room summary in listRooms", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "测试房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.5.0",
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

test("createRoom rejects pre-0.3 callers before allocating a room id", () => {
  let idAllocations = 0;
  const store = new LobbyStore(baseConfig, { id: () => { idAllocations += 1; return `id-${idAllocations}`; } });
  assert.throws(
    () => store.createRoom(
      {
        roomName: "过期客户端房间",
        hostPlayerName: "Host",
        gameMode: "standard",
        version: "1.2.3",
        modVersion: "0.2.2",
        maxPlayers: 4,
        hostConnectionInfo: { enetPort: 33771 },
      },
      "203.0.113.10",
    ),
    (error: unknown) => error instanceof LobbyStoreError && error.code === "client_update_required",
  );
  assert.equal(idAllocations, 0);
  assert.equal(store.listRooms().length, 0);
});

const tailFingerprint = "sha256:v1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
const otherTailFingerprint = "sha256:v1:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

function tailCreateInput(ritsuLibPresent: boolean, ritsuLibSidecarAvailable = ritsuLibPresent) {
  return {
    roomName: "0.6 协议房",
    hostPlayerName: "Host",
    gameMode: "standard",
    version: "1.2.3",
    modVersion: "0.6.1-alpha.1",
    clientVersion: "0.6.1-alpha.1",
    protocolProfileV2: "tail_v1" as const,
    protocolOffer: {
      lanProtocolMin: 1,
      lanProtocolMax: 1,
      clientVersion: "0.6.1-alpha.1",
      ritsuLibPresent,
      ritsuLibSidecarAvailable,
      registryFingerprint: tailFingerprint,
    },
    maxPlayers: 8,
    hostConnectionInfo: { enetPort: 33771 },
  };
}

function tailJoinInput(ritsuLibPresent: boolean, ritsuLibSidecarAvailable = ritsuLibPresent): JoinRoomInput {
  return {
    playerName: "Guest",
    version: "1.2.3",
    modVersion: "0.6.1-alpha.1",
    clientVersion: "0.6.1-alpha.1",
    protocolOffer: {
      lanProtocolMin: 1,
      lanProtocolMax: 1,
      clientVersion: "0.6.1-alpha.1",
      ritsuLibPresent,
      ritsuLibSidecarAvailable,
    },
    registryFingerprint: tailFingerprint,
  };
}

test("Tail rooms freeze deterministic homogeneous carriers and issue per-ticket nonces", () => {
  for (const ritsuLibPresent of [false, true]) {
    const store = new LobbyStore(baseConfig);
    const created = store.createRoom(tailCreateInput(ritsuLibPresent), "203.0.113.10");
    assert.equal(created.room.protocolSelection.carrier, "native_bus_v1");
    assert.equal(created.room.protocolSelection.registryFingerprint, tailFingerprint);
    assert.equal(created.room.protocolSelection.minimumClientVersion, "0.6.1-alpha.1");
    assert.equal(created.protocolSelection, created.room.protocolSelection);
    assert.equal(created.room.protocolSelection.ritsuLibPresent, ritsuLibPresent);
    const first = store.joinRoom(created.roomId, tailJoinInput(ritsuLibPresent));
    const second = store.joinRoom(created.roomId, tailJoinInput(ritsuLibPresent));
    assert.match(first.protocolFlowNonce, /^[0-9a-f]{32}$/);
    assert.match(second.protocolFlowNonce, /^[0-9a-f]{32}$/);
    assert.notEqual(first.protocolFlowNonce, second.protocolFlowNonce);
    assert.equal(Object.hasOwn(store.listRooms()[0] as object, "protocolFlowNonce"), false);
  }
});

test("Tail create tolerates an unavailable sidecar and rejects a missing fingerprint before every allocation", () => {
  let idAllocations = 0;
  const store = new LobbyStore(baseConfig, { id: () => { idAllocations += 1; return `id-${idAllocations}`; } });
  // 0.5.18 事故正面回归：Ritsu 存在但 sidecar 不可用 => 照常创建（native 载体）。
  const created = store.createRoom(tailCreateInput(true, false), "203.0.113.10");
  assert.equal(created.room.protocolSelection.carrier, "native_bus_v1");

  // fingerprint 缺失/非法在房间分配前拒绝。
  assert.throws(
    () => {
      const input = tailCreateInput(true);
      delete (input.protocolOffer as { registryFingerprint?: string }).registryFingerprint;
      store.createRoom(input, "203.0.113.10");
    },
    (error: unknown) => error instanceof LobbyStoreError && error.code === "lan_registry_fingerprint_required",
  );
  assert.equal(store.listRooms().length, 1);
});

test("Tail join gate chain rejects presence mismatch, joiner-sidecar-free presence, bad fingerprints and old clients before ticket allocation", () => {
  const cases = [
    { host: true, joiner: false, sidecar: false, code: "ritsulib_presence_mismatch" },
    { host: false, joiner: true, sidecar: true, code: "ritsulib_presence_mismatch" },
    { host: true, joiner: true, sidecar: false, code: null },
    { host: true, joiner: true, fingerprint: "sha256:v1:" + "c".repeat(64), code: "lan_registry_fingerprint_mismatch" },
    { host: true, joiner: true, fingerprint: "bad", code: "lan_registry_fingerprint_required" },
  ] as Array<{ host: boolean; joiner: boolean; sidecar: boolean; fingerprint?: string; code: string | null }>;
  for (const scenario of cases) {
    let idAllocations = 0;
    const store = new LobbyStore(baseConfig, { id: () => { idAllocations += 1; return `id-${idAllocations}`; } });
    const created = store.createRoom(tailCreateInput(scenario.host), "203.0.113.10");
    const afterCreate = idAllocations;
    const input = tailJoinInput(scenario.joiner, scenario.sidecar);
    if (scenario.fingerprint !== undefined) {
      input.registryFingerprint = scenario.fingerprint;
    }

    if (scenario.code === null) {
      // 0.5.18 事故正面回归：joiner 侧 sidecar 不可用照常签发工单。
      const joined = store.joinRoom(created.roomId, input);
      assert.match(joined.protocolFlowNonce, /^[0-9a-f]{32}$/);
    } else {
      assert.throws(
        () => store.joinRoom(created.roomId, input),
        (error: unknown) => error instanceof LobbyStoreError && error.code === scenario.code,
      );
      assert.equal(idAllocations, afterCreate, scenario.code ?? "unnamed");
    }
  }

  // 旧 0.6 客户端（无 fingerprint 字段、版本过旧）在工单签发路径被拒。
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(tailCreateInput(true), "203.0.113.10");
  const legacyJoiner = tailJoinInput(true);
  delete legacyJoiner.registryFingerprint;
  legacyJoiner.clientVersion = "0.6.0";
  legacyJoiner.modVersion = "0.6.0";
  legacyJoiner.protocolOffer = legacyJoiner.protocolOffer
    ? { ...legacyJoiner.protocolOffer, clientVersion: "0.6.0" }
    : undefined;
  assert.throws(
    () => store.joinRoom(created.roomId, legacyJoiner),
    (error: unknown) => error instanceof LobbyStoreError && error.code === "lan_registry_fingerprint_required",
  );
  const fingerprintButOld = tailJoinInput(true);
  fingerprintButOld.clientVersion = "0.6.0";
  fingerprintButOld.modVersion = "0.6.0";
  fingerprintButOld.protocolOffer = fingerprintButOld.protocolOffer
    ? { ...fingerprintButOld.protocolOffer, clientVersion: "0.6.0" }
    : undefined;
  assert.throws(
    () => store.joinRoom(created.roomId, fingerprintButOld),
    (error: unknown) => error instanceof LobbyStoreError && error.code === "lan_client_version_too_old",
  );
  assert.notEqual(otherTailFingerprint, tailFingerprint);
});

test("Tail control binding validates version and digest before allocating a binding", () => {
  let idAllocations = 0;
  const store = new LobbyStore(baseConfig, { id: () => { idAllocations += 1; return `id-${idAllocations}`; } });
  const created = store.createRoom(tailCreateInput(false), "203.0.113.10");
  const joined = store.joinRoom(created.roomId, tailJoinInput(false));
  const afterTicket = idAllocations;
  assert.throws(
    () => store.validateClientControl(created.roomId, created.controlChannelId, joined.ticketId, "0.6.1-alpha.1", "0".repeat(64)),
    (error: unknown) => error instanceof LobbyStoreError && error.code === "capability_digest_mismatch",
  );
  assert.equal(idAllocations, afterTicket);
  const redeemed = store.validateClientControl(
    created.roomId,
    created.controlChannelId,
    joined.ticketId,
    "0.6.1-alpha.1",
    created.room.protocolSelection.capabilityDigest,
  );
  assert.equal(redeemed.protocolFlowNonce, joined.protocolFlowNonce);
  assert.equal(idAllocations, afterTicket + 1);
});

test("createRoom preserves explicit protocol profile and echoes it in joins", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "扩展协议房",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.5.3",
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
    modVersion: "0.5.3",
  });
  assert.equal(joined.room.protocolProfile, "extended_8p");
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
      modVersion: "0.5.0",
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
      modVersion: "0.5.0",
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
      modVersion: "0.5.0",
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
    modVersion: "0.5.0",
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
      modVersion: "0.5.0",
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
        modVersion: "0.5.0",
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
      modVersion: "0.5.0",
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

test("cleanupExpired retains an expired room while its authenticated relay host is active", () => {
  const store = new LobbyStore(baseConfig);
  const now = new Date("2026-03-10T00:00:00.000Z");
  const created = store.createRoom(
    {
      roomName: "中继进行中房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.5.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
    now,
  );
  const expiredAt = new Date(now.getTime() + 40_000);

  const retained = store.cleanupExpired(expiredAt, (roomId) => roomId === created.roomId);
  assert.deepEqual(retained, []);
  assert.equal(store.listRooms().length, 1);

  const deleted = store.cleanupExpired(expiredAt);
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
      modVersion: "0.5.0",
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
    modVersion: "0.5.0",
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
      modVersion: "0.5.0",
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
        modVersion: "0.5.0",
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
        modVersion: "0.5.0",
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
      modVersion: "0.5.0",
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
        modVersion: "0.5.0",
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
      modVersion: "0.5.0",
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
    modVersion: "0.5.0",
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
      modVersion: "0.5.0",
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
      modVersion: "0.5.0",
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
    modVersion: "0.5.1",
  });

  assert.equal(joined.room.roomId, created.roomId);
});

test("joinRoom allows equal wire cache signatures", () => {
  const store = new LobbyStore(baseConfig);
  const created = createWireCacheRoom(store, WireCacheSignatureA);

  const joined = store.joinRoom(created.roomId, {
    ...basicJoinInput(),
    wireCacheSignatureV1: WireCacheSignatureA,
  });

  assert.equal(joined.room.roomId, created.roomId);
});

test("joinRoom treats a malformed wire cache signature as absent rather than mismatched", () => {
  const store = new LobbyStore(baseConfig);
  const created = createWireCacheRoom(store, WireCacheSignatureA);

  // A peer that sends a placeholder, a truncated digest, or a future token shape must
  // still be allowed: rejecting a compatible player is worse than missing an
  // incompatible one, which the in-band handshake gate also checks.
  for (const malformed of ["unavailable", "wcv1:", "wcv1:tooshort", "wcv2:whatever"]) {
    const joined = store.joinRoom(created.roomId, {
      ...basicJoinInput(),
      wireCacheSignatureV1: malformed,
    });
    assert.equal(joined.room.roomId, created.roomId);
  }
});

test("createRoom treats a malformed host wire cache signature as absent", () => {
  const store = new LobbyStore(baseConfig);
  const created = createWireCacheRoom(store, "not-a-signature");

  // The host advertised garbage, so nothing is authoritative and a valid joiner passes.
  const joined = store.joinRoom(created.roomId, {
    ...basicJoinInput(),
    wireCacheSignatureV1: WireCacheSignatureB,
  });

  assert.equal(joined.room.roomId, created.roomId);
});

test("joinRoom still allows a signature-missing join when the warning sink throws", () => {
  const store = new LobbyStore(baseConfig, {
    warn: () => {
      throw new Error("warn sink failed");
    },
  });
  const created = createWireCacheRoom(store, WireCacheSignatureA);

  const joined = store.joinRoom(created.roomId, basicJoinInput());

  assert.equal(joined.room.roomId, created.roomId);
});

test("joinRoom rejects different wire cache signatures and names both signatures", () => {
  const store = new LobbyStore(baseConfig);
  const created = createWireCacheRoom(store, WireCacheSignatureA);

  assert.throws(
    () => store.joinRoom(created.roomId, {
      ...basicJoinInput(),
      wireCacheSignatureV1: WireCacheSignatureB,
    }),
    (error: unknown) =>
      error instanceof LobbyStoreError
      && error.statusCode === 409
      && error.code === "wire_cache_signature_mismatch"
      && error.message.includes(WireCacheSignatureA)
      && error.message.includes(WireCacheSignatureB),
  );
});

test("joinRoom allows and warns when the host signature is present and joiner signature is absent", () => {
  const warnings: string[] = [];
  const store = new LobbyStore(baseConfig, { warn: (message) => warnings.push(message) });
  const created = createWireCacheRoom(store, WireCacheSignatureA);

  const joined = store.joinRoom(created.roomId, basicJoinInput());

  assert.equal(joined.room.roomId, created.roomId);
  assert.equal(warnings.length, 1);
  assert.match(warnings[0]!, /host=wcv1:/);
  assert.match(warnings[0]!, /joiner=<absent>/);
});

test("joinRoom allows and warns when the host signature is absent and joiner signature is present", () => {
  const warnings: string[] = [];
  const store = new LobbyStore(baseConfig, { warn: (message) => warnings.push(message) });
  const created = createWireCacheRoom(store, undefined);

  const joined = store.joinRoom(created.roomId, {
    ...basicJoinInput(),
    wireCacheSignatureV1: WireCacheSignatureA,
  });

  assert.equal(joined.room.roomId, created.roomId);
  assert.equal(warnings.length, 1);
  assert.match(warnings[0]!, /host=<absent>/);
  assert.match(warnings[0]!, /joiner=wcv1:/);
});

test("joinRoom allows when both wire cache signatures are absent without warning", () => {
  const warnings: string[] = [];
  const store = new LobbyStore(baseConfig, { warn: (message) => warnings.push(message) });
  const created = createWireCacheRoom(store, undefined);

  const joined = store.joinRoom(created.roomId, basicJoinInput());

  assert.equal(joined.room.roomId, created.roomId);
  assert.deepEqual(warnings, []);
});

test("repeated wire cache mismatches create no ticket or saved-run reservation state", () => {
  const store = new LobbyStore(baseConfig, {
    id: sequenceIds("room", "control", "room-session", "ticket"),
  });
  const created = store.createRoom(
    {
      roomName: "网络编码续局房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.5.0",
      wireCacheSignatureV1: WireCacheSignatureA,
      maxPlayers: 4,
      hostConnectionInfo: { enetPort: 33771 },
      savedRun: {
        saveKey: "save-1",
        slots: [
          { netId: "1", isHost: true },
          { netId: "2", isHost: false },
        ],
        connectedPlayerNetIds: ["1"],
      },
    },
    "203.0.113.10",
  );

  for (let attempt = 0; attempt < 3; attempt += 1) {
    assert.throws(
      () => store.joinRoom(created.roomId, {
        ...basicJoinInput(),
        wireCacheSignatureV1: WireCacheSignatureB,
        desiredSavePlayerNetId: "2",
      }),
      (error: unknown) =>
        error instanceof LobbyStoreError && error.code === "wire_cache_signature_mismatch",
    );
  }

  const afterRejections = store.listRooms()[0]!;
  assert.equal(afterRejections.currentPlayers, 1);
  assert.deepEqual(afterRejections.savedRun?.connectedPlayerNetIds, ["1"]);
  assert.equal(afterRejections.savedRun?.slots[1]?.isConnected, false);
  assert.equal(store.hasTicketForRoom(created.roomId, "ticket"), false);

  const joined = store.joinRoom(created.roomId, {
    ...basicJoinInput(),
    wireCacheSignatureV1: WireCacheSignatureA,
    desiredSavePlayerNetId: "2",
  });
  assert.equal(joined.ticketId, "ticket", "rejections must not allocate ticket IDs");
  assert.equal(store.hasTicketForRoom(created.roomId, "ticket"), true);
});

test("joinRoom returns missing mod details when mod lists differ", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "模组差异房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.5.0",
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
        modVersion: "0.5.0",
        modList: ["BaseMod", "SharedMod", "LocalOnlyMod"],
      }),
    (error: unknown) =>
      error instanceof LobbyStoreError &&
      error.code === "mod_mismatch" &&
      error.statusCode === 409 &&
      typeof error.details === "object" &&
      error.details !== null &&
      JSON.stringify(error.details) === JSON.stringify({
        roomModVersion: "0.5.0",
        requestedModVersion: "0.5.0",
        missingModsOnLocal: ["HostOnlyMod"],
        missingModsOnHost: ["LocalOnlyMod"],
      }),
  );
});

test("joinRoom accepts equal supported mod versions", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "模组版本归一化房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.5.2",
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
    modVersion: "0.5.2",
    modList: ["BaseMod", "SharedMod"],
  });

  assert.equal(joined.room.modVersion, "0.5.2");
});

test("kickPlayer bans only the current occupant installation and never the shared save slot", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "踢人测试房间",
      hostPlayerName: "Host",
      clientInstallationId: "install-host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.5.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
  );

  const originalOwner = store.joinRoom(created.roomId, {
    playerName: "Original Owner",
    version: "1.2.3",
    modVersion: "0.5.0",
    playerNetId: "save-slot-12345",
    clientInstallationId: "install-original",
  });
  assert.equal(
    store.validateClientControl(created.roomId, created.controlChannelId, originalOwner.ticketId)
      .clientInstallationId,
    "install-original",
  );
  const currentOccupant = store.joinRoom(created.roomId, {
    playerName: "Current Occupant",
    version: "1.2.3",
    modVersion: "0.5.0",
    playerNetId: "save-slot-12345",
    clientInstallationId: "install-takeover",
  });
  const currentBinding = store.validateClientControl(
    created.roomId,
    created.controlChannelId,
    currentOccupant.ticketId,
  );
  assert.equal(currentBinding.playerNetId, "save-slot-12345");

  assert.throws(
    () => store.validateClientControl(
      created.roomId,
      created.controlChannelId,
      originalOwner.ticketId,
    ),
    (error: unknown) => error instanceof LobbyStoreError && error.code === "invalid_ticket",
  );
  const kicked = requireKicked(store.kickPlayer(
    created.roomId,
    created.hostToken,
    "save-slot-12345",
    currentBinding.controlBindingId,
  ));
  assert.equal(kicked.binding.clientInstallationId, "install-takeover");
  assert.equal(kicked.persistentBanCreated, true);
  assert.equal(
    store.kickPlayer(created.roomId, created.hostToken, "save-slot-12345").outcome,
    "not_found",
  );
  assert.equal(store.isClientInstallationKicked(created.roomId, "install-takeover"), true);
  assert.equal(store.isClientInstallationKicked(created.roomId, "install-original"), false);

  assert.throws(
    () =>
      store.joinRoom(created.roomId, {
        playerName: "Kicked Occupant",
        version: "1.2.3",
        modVersion: "0.5.0",
        playerNetId: "save-slot-12345",
        clientInstallationId: "install-takeover",
      }),
    (error: unknown) =>
      error instanceof LobbyStoreError &&
      error.code === "kicked" &&
      error.statusCode === 403,
  );

  const rejoined = store.joinRoom(created.roomId, {
    playerName: "Original Owner",
    version: "1.2.3",
    modVersion: "0.5.0",
    playerNetId: "save-slot-12345",
    clientInstallationId: "install-original",
  });
  assert.equal(rejoined.room.roomId, created.roomId);
});

test("kickPlayer rejects invalid host token", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "踢人鉴权测试",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.5.0",
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
      modVersion: "0.5.0",
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

test("declaring the original owner's public slot as an installation id cannot ban the owner", () => {
  const warnings: string[] = [];
  const store = new LobbyStore(baseConfig, { warn: (message) => warnings.push(message) });
  const created = store.createRoom(
    {
      roomName: "不传 netId 房间",
      hostPlayerName: "Host",
      clientInstallationId: "install-host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.5.0",
      maxPlayers: 4,
      hostConnectionInfo: {
        enetPort: 33771,
      },
    },
    "203.0.113.10",
  );

  const originalOwner = store.joinRoom(created.roomId, {
    playerName: "Original Owner",
    clientInstallationId: "76561198000000001",
    version: "1.2.3",
    modVersion: "0.5.0",
    playerNetId: "76561198000000001",
  });
  store.validateClientControl(created.roomId, created.controlChannelId, originalOwner.ticketId);

  const legacyTaker = store.joinRoom(created.roomId, {
    playerName: "Hostile Taker",
    clientInstallationId: "76561198000000001",
    version: "1.2.3",
    modVersion: "0.5.0",
    playerNetId: "76561198000000001",
  });
  const legacyBinding = store.validateClientControl(
    created.roomId,
    created.controlChannelId,
    legacyTaker.ticketId,
  );
  assert.equal(legacyBinding.identityKind, "legacy");
  assert.equal(legacyBinding.clientInstallationId, undefined);
  assert.equal(warnings.length, 1);
  assert.match(warnings[0]!, /identity mode=legacy/);

  const kicked = requireKicked(store.kickPlayer(
    created.roomId,
    created.hostToken,
    "76561198000000001",
    legacyBinding.controlBindingId,
  ));
  assert.equal(kicked.binding.identityKind, "legacy");
  assert.equal(kicked.persistentBanCreated, false);
  assert.equal(store.isClientInstallationKicked(created.roomId, "76561198000000001"), false);

  const ownerRejoin = store.joinRoom(created.roomId, {
    playerName: "Original Owner",
    clientInstallationId: "76561198000000001",
    version: "1.2.3",
    modVersion: "0.5.0",
    playerNetId: "76561198000000001",
  });
  assert.equal(ownerRejoin.room.roomId, created.roomId);

  const legacyRejoin = store.joinRoom(created.roomId, {
    playerName: "Hostile Taker",
    clientInstallationId: "76561198000000001",
    version: "1.2.3",
    modVersion: "0.5.0",
    playerNetId: "76561198000000001",
  });
  assert.equal(legacyRejoin.room.roomId, created.roomId);
  assert.equal(warnings.length, 1);
});

test("a declared installation id matching another known save slot is treated as legacy", () => {
  const store = new LobbyStore(baseConfig, { warn: () => undefined });
  const created = store.createRoom(
    {
      roomName: "已知槽位身份测试",
      hostPlayerName: "Host",
      clientInstallationId: "install-host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.5.0",
      maxPlayers: 4,
      hostConnectionInfo: { enetPort: 33771 },
      savedRun: {
        saveKey: "save-key",
        slots: [
          { netId: "76561198000000001", playerName: "A" },
          { netId: "76561198000000002", playerName: "B" },
        ],
      },
    },
    "203.0.113.10",
  );

  const joined = store.joinRoom(created.roomId, {
    playerName: "Attacker",
    version: "1.2.3",
    modVersion: "0.5.0",
    desiredSavePlayerNetId: "76561198000000002",
    playerNetId: "76561198000000002",
    clientInstallationId: "76561198000000001",
  });
  const ticket = store.validateClientControl(
    created.roomId,
    created.controlChannelId,
    joined.ticketId,
  );

  assert.equal(ticket.identityKind, "legacy");
  assert.equal(ticket.clientInstallationId, undefined);
});

test("a legacy numeric game net id never becomes a persistent installation identity", () => {
  const store = new LobbyStore(baseConfig, { warn: () => undefined });
  const created = createBasicRoom(store);
  const joined = store.joinRoom(created.roomId, {
    ...basicJoinInput(),
    playerNetId: "76561198000000002",
    clientInstallationId: "76561198000000001",
  });
  const ticket = store.validateClientControl(
    created.roomId,
    created.controlChannelId,
    joined.ticketId,
  );

  assert.equal(ticket.identityKind, "legacy");
  assert.equal(ticket.clientInstallationId, undefined);
  const kicked = requireKicked(store.kickPlayer(
    created.roomId,
    created.hostToken,
    "76561198000000002",
    ticket.controlBindingId,
  ));
  assert.equal(kicked.persistentBanCreated, false);
  assert.equal(store.isClientInstallationKicked(created.roomId, "76561198000000001"), false);
});

test("installation kick does not outlive the room session", () => {
  const store = new LobbyStore(baseConfig, { warn: () => undefined });
  const first = createBasicRoom(store);
  const firstJoin = store.joinRoom(first.roomId, basicJoinInput());
  const firstBinding = store.validateClientControl(
    first.roomId,
    first.controlChannelId,
    firstJoin.ticketId,
  );
  store.kickPlayer(first.roomId, first.hostToken, "slot-guest", firstBinding.controlBindingId);
  store.deleteRoom(first.roomId, first.hostToken);

  const second = createBasicRoom(store);
  const joined = store.joinRoom(second.roomId, {
    ...basicJoinInput(),
    playerNetId: "same-slot",
    clientInstallationId: "install-guest",
  });
  assert.equal(joined.room.roomId, second.roomId);
});

test("kick authority survives a control disconnect until the slot is kicked", () => {
  const store = new LobbyStore(baseConfig);
  const created = createBasicRoom(store);
  const joined = store.joinRoom(created.roomId, basicJoinInput());
  const binding = store.validateClientControl(
    created.roomId,
    created.controlChannelId,
    joined.ticketId,
  );

  const kicked = requireKicked(store.kickPlayer(
    created.roomId,
    created.hostToken,
    "slot-guest",
    binding.controlBindingId,
  ));
  assert.equal(kicked.binding.clientInstallationId, "install-guest");
  assert.equal(kicked.persistentBanCreated, true);
  assert.throws(
    () => store.joinRoom(created.roomId, basicJoinInput()),
    (error: unknown) => error instanceof LobbyStoreError && error.code === "kicked",
  );
});

test("a legacy slot-only kick disconnects without creating a persistent ban", () => {
  const store = new LobbyStore(baseConfig);
  const created = createBasicRoom(store);
  const joined = store.joinRoom(created.roomId, basicJoinInput());
  store.validateClientControl(created.roomId, created.controlChannelId, joined.ticketId);

  const kicked = requireKicked(store.kickPlayer(
    created.roomId,
    created.hostToken,
    "slot-guest",
  ));

  assert.equal(kicked.legacyRequest, true);
  assert.equal(kicked.persistentBanCreated, false);
  assert.equal(store.isClientInstallationKicked(created.roomId, "install-guest"), false);
  assert.equal(store.joinRoom(created.roomId, basicJoinInput()).room.roomId, created.roomId);
});

test("a stale binding handle cannot ban the replacement occupant", () => {
  const warnings: string[] = [];
  const store = new LobbyStore(baseConfig, { warn: (message) => warnings.push(message) });
  const created = createBasicRoom(store);
  const firstJoin = store.joinRoom(created.roomId, {
    ...basicJoinInput(),
    clientInstallationId: "install-first",
  });
  const firstBinding = store.validateClientControl(
    created.roomId,
    created.controlChannelId,
    firstJoin.ticketId,
  );
  const replacementJoin = store.joinRoom(created.roomId, {
    ...basicJoinInput(),
    clientInstallationId: "install-replacement",
  });
  const replacementBinding = store.validateClientControl(
    created.roomId,
    created.controlChannelId,
    replacementJoin.ticketId,
  );

  const staleKick = store.kickPlayer(
    created.roomId,
    created.hostToken,
    "slot-guest",
    firstBinding.controlBindingId,
  );

  assert.equal(staleKick.outcome, "stale_binding");
  assert.equal(store.isClientInstallationKicked(created.roomId, "install-first"), false);
  assert.equal(store.isClientInstallationKicked(created.roomId, "install-replacement"), false);
  assert.equal(warnings.some((message) => message.includes("stale kick rejected")), true);

  const currentKick = requireKicked(store.kickPlayer(
    created.roomId,
    created.hostToken,
    "slot-guest",
    replacementBinding.controlBindingId,
  ));
  assert.equal(currentKick.binding.clientInstallationId, "install-replacement");
  assert.equal(currentKick.persistentBanCreated, true);
});

test("a legacy slot-only kick is rejected after that slot has rebound", () => {
  const warnings: string[] = [];
  const store = new LobbyStore(baseConfig, { warn: (message) => warnings.push(message) });
  const created = createBasicRoom(store);
  const firstJoin = store.joinRoom(created.roomId, {
    ...basicJoinInput(),
    clientInstallationId: "install-first",
  });
  store.validateClientControl(created.roomId, created.controlChannelId, firstJoin.ticketId);
  const replacementJoin = store.joinRoom(created.roomId, {
    ...basicJoinInput(),
    clientInstallationId: "install-replacement",
  });
  const replacementBinding = store.validateClientControl(
    created.roomId,
    created.controlChannelId,
    replacementJoin.ticketId,
  );

  assert.equal(
    store.kickPlayer(created.roomId, created.hostToken, "slot-guest").outcome,
    "legacy_rebound",
  );
  assert.equal(store.isClientInstallationKicked(created.roomId, "install-first"), false);
  assert.equal(store.isClientInstallationKicked(created.roomId, "install-replacement"), false);
  assert.equal(warnings.some((message) => message.includes("legacy kick rejected")), true);

  const modernKick = requireKicked(store.kickPlayer(
    created.roomId,
    created.hostToken,
    "slot-guest",
    replacementBinding.controlBindingId,
  ));
  assert.equal(modernKick.binding.clientInstallationId, "install-replacement");
});

test("kicked installation ids evict the oldest entry at the configured cap", () => {
  const warnings: string[] = [];
  const store = new LobbyStore({
    ...baseConfig,
    maxKickedClientInstallationIds: 2,
  }, { warn: (message) => warnings.push(message) });
  const created = createBasicRoom(store);

  for (let index = 0; index < 3; index += 1) {
    const joined = store.joinRoom(created.roomId, {
      ...basicJoinInput(),
      playerNetId: `slot-${index}`,
      clientInstallationId: `install-${index}`,
    });
    const binding = store.validateClientControl(
      created.roomId,
      created.controlChannelId,
      joined.ticketId,
    );
    store.kickPlayer(
      created.roomId,
      created.hostToken,
      `slot-${index}`,
      binding.controlBindingId,
    );
  }

  assert.equal(store.isClientInstallationKicked(created.roomId, "install-0"), false);
  assert.equal(store.isClientInstallationKicked(created.roomId, "install-1"), true);
  assert.equal(store.isClientInstallationKicked(created.roomId, "install-2"), true);
  assert.equal(warnings.filter((message) => message.includes("kicked installation id evicted")).length, 1);
});

test("every active occupant remains kickable at the supported room capacity", () => {
  const warnings: string[] = [];
  const store = new LobbyStore(baseConfig, { warn: (message) => warnings.push(message) });
  const created = store.createRoom(
    {
      roomName: "满容量绑定测试",
      hostPlayerName: "Host",
      clientInstallationId: "install-host",
      gameMode: "standard",
      version: "1.2.3",
      modVersion: "0.5.0",
      maxPlayers: MaxSupportedRoomPlayers,
      hostConnectionInfo: { enetPort: 33771 },
    },
    "203.0.113.10",
  );
  const bindings = new Map<string, string>();

  for (let index = 0; index < MaxSupportedRoomPlayers; index += 1) {
    const playerNetId = `slot-${index}`;
    const joined = store.joinRoom(created.roomId, {
      ...basicJoinInput(),
      playerNetId,
      clientInstallationId: `install-${index}`,
    });
    const binding = store.validateClientControl(
      created.roomId,
      created.controlChannelId,
      joined.ticketId,
    );
    bindings.set(playerNetId, binding.controlBindingId!);
  }

  const first = requireKicked(store.kickPlayer(
    created.roomId,
    created.hostToken,
    "slot-0",
    bindings.get("slot-0"),
  ));
  const lastSlot = `slot-${MaxSupportedRoomPlayers - 1}`;
  const last = requireKicked(store.kickPlayer(
    created.roomId,
    created.hostToken,
    lastSlot,
    bindings.get(lastSlot),
  ));
  assert.equal(first.binding.clientInstallationId, "install-0");
  assert.equal(last.binding.clientInstallationId, `install-${MaxSupportedRoomPlayers - 1}`);
  assert.equal(warnings.some((message) => message.includes("binding capacity exceeded")), false);
});

test("client control binding cap evicts oldest inactive slots and their rebound markers", () => {
  const activePlayerNetIds = new Set<string>();
  const warnings: string[] = [];
  const store = new LobbyStore({
    ...baseConfig,
    maxClientControlBindingsPerRoom: 3,
  }, {
    peerPlayerNetIds: () => activePlayerNetIds,
    warn: (message) => warnings.push(message),
  });
  const created = createBasicRoom(store);
  const bind = (playerNetId: string, clientInstallationId: string) => {
    const joined = store.joinRoom(created.roomId, {
      ...basicJoinInput(),
      playerNetId,
      clientInstallationId,
    });
    return store.validateClientControl(
      created.roomId,
      created.controlChannelId,
      joined.ticketId,
    );
  };

  bind("1001", "install-control-active");
  activePlayerNetIds.add("1001");
  bind("1002", "install-game-active");
  bind("1003", "install-obsolete-first");
  bind("1003", "install-obsolete-replacement");
  store.heartbeat(created.roomId, {
    hostToken: created.hostToken,
    currentPlayers: 3,
    status: "open",
    connectedPlayerNetIds: ["1002"],
  });

  bind("1004", "install-incoming");
  assert.deepEqual(
    store.listClientControlBindingHandles(created.roomId, created.hostToken)
      .map((binding) => binding.playerNetId),
    ["1001", "1002", "1004"],
  );

  store.heartbeat(created.roomId, {
    hostToken: created.hostToken,
    currentPlayers: 3,
    status: "open",
    connectedPlayerNetIds: ["1002"],
  });
  bind("1003", "install-obsolete-returned");
  assert.deepEqual(
    store.listClientControlBindingHandles(created.roomId, created.hostToken)
      .map((binding) => binding.playerNetId),
    ["1001", "1002", "1003"],
  );
  assert.equal(
    store.kickPlayer(created.roomId, created.hostToken, "1003").outcome,
    "kicked",
  );
  assert.equal(
    warnings.some((message) => message.includes("playerNetId=1001")),
    false,
  );
  assert.equal(
    warnings.some((message) => message.includes("playerNetId=1002")),
    false,
  );
  assert.equal(
    warnings.filter((message) => message.includes("client control binding evicted")).length,
    2,
  );
  assert.equal(warnings[0]?.includes("playerNetId=1003"), true);
  assert.equal(warnings[1]?.includes("playerNetId=1004"), true);
});

test("client control binding cap refuses a newcomer when every retained slot is active", () => {
  const activePlayerNetIds = new Set<string>();
  const warnings: string[] = [];
  const store = new LobbyStore({
    ...baseConfig,
    maxClientControlBindingsPerRoom: 2,
  }, {
    peerPlayerNetIds: () => activePlayerNetIds,
    warn: (message) => warnings.push(message),
  });
  const created = createBasicRoom(store);
  const bind = (playerNetId: string) => {
    const joined = store.joinRoom(created.roomId, {
      ...basicJoinInput(),
      playerNetId,
      clientInstallationId: `install-${playerNetId}`,
    });
    return store.validateClientControl(
      created.roomId,
      created.controlChannelId,
      joined.ticketId,
    );
  };

  bind("2001");
  activePlayerNetIds.add("2001");
  bind("2002");
  store.heartbeat(created.roomId, {
    hostToken: created.hostToken,
    currentPlayers: 3,
    status: "open",
    connectedPlayerNetIds: ["2002"],
  });

  assert.throws(
    () => bind("2003"),
    (error: unknown) => error instanceof LobbyStoreError && error.code === "binding_capacity",
  );
  assert.deepEqual(
    store.listClientControlBindingHandles(created.roomId, created.hostToken)
      .map((binding) => binding.playerNetId),
    ["2001", "2002"],
  );
  assert.equal(warnings.some((message) => message.includes("playerNetId=2003")), true);
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
      modVersion: "0.5.0",
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
    modVersion: "0.5.0",
  });

  assert.equal(joined.connectionPlan.strategy, "relay-only");
  assert.deepEqual(joined.connectionPlan.directCandidates, []);
});

const hostModInventory: LobbyModDescriptor[] = [
  {
    id: "host.workshop",
    version: "2.0.0",
    role: "gameplay",
    source: "steam_workshop",
    workshopFileId: "3747497501",
    dependencies: ["shared.dependency"],
  },
  {
    id: "shared.dependency",
    version: "1.0.0",
    role: "dependency",
    source: "mods_directory",
    dependencies: [],
  },
];

test("modPreflight stores inventory privately without mutating room or issuing a ticket", () => {
  const store = new LobbyStore(
    { ...baseConfig, strictModVersionCheck: false },
    { id: sequenceIds("room", "control", "room-session", "ticket") },
  );
  const created = store.createRoom(
    {
      roomName: "私有预检房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "v0.109.0",
      modVersion: "0.5.1",
      hostModInventory,
      maxPlayers: 4,
      hostConnectionInfo: { enetPort: 33771 },
    },
    "203.0.113.10",
  );

  assert.equal(Object.hasOwn(created.room, "hostModInventory"), false);
  assert.equal(Object.hasOwn(store.listRooms()[0]!, "hostModInventory"), false);
  const before = store.listRooms()[0]!;
  const result = store.modPreflight(created.roomId, {
    playerName: "Guest",
    gameVersion: "v0.109.0",
    localMods: [],
  });
  const after = store.listRooms()[0]!;

  assert.equal(result.hostInventoryAvailable, true);
  assert.deepEqual(result.missingWorkshopMods.map((mod) => mod.id), ["host.workshop"]);
  assert.deepEqual(result.missingManualMods.map((mod) => mod.id), ["shared.dependency"]);
  assert.equal(result.canContinueRelaxed, true);
  assert.equal(after.currentPlayers, before.currentPlayers);
  assert.equal(after.status, before.status);

  const joined = store.joinRoom(created.roomId, {
    playerName: "Guest",
    version: "v0.109.0",
    modVersion: "0.5.1",
  });
  assert.equal(joined.ticketId, "ticket", "preflight must not consume an id or issue a ticket");
});

test("modPreflight validates room state and password before returning private differences", () => {
  const store = new LobbyStore({ ...baseConfig, strictModVersionCheck: false });
  const created = store.createRoom(
    {
      roomName: "密码预检房间",
      password: "room-secret",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "v0.109.0",
      modVersion: "0.5.1",
      hostModInventory,
      maxPlayers: 4,
      hostConnectionInfo: { enetPort: 33771 },
    },
    "203.0.113.10",
  );

  assert.throws(
    () => store.modPreflight(created.roomId, {
      playerName: "Guest",
      password: "wrong",
      gameVersion: "v0.109.0",
      localMods: [],
    }),
    (error: unknown) => error instanceof LobbyStoreError && error.code === "invalid_password",
  );

  store.heartbeat(created.roomId, {
    hostToken: created.hostToken,
    currentPlayers: 1,
    status: "starting",
  });
  assert.throws(
    () => store.modPreflight(created.roomId, {
      playerName: "Guest",
      password: "room-secret",
      gameVersion: "v0.109.0",
      localMods: [],
    }),
    (error: unknown) => error instanceof LobbyStoreError && error.code === "room_started",
  );
});

test("modPreflight hard-blocks game mismatches even when all legacy checks are relaxed", () => {
  const store = new LobbyStore({
    ...baseConfig,
    strictGameVersionCheck: false,
    strictModVersionCheck: false,
  });
  const created = store.createRoom(
    {
      roomName: "跨版本预检房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "v0.108.0",
      modVersion: "0.5.1",
      hostModInventory,
      maxPlayers: 4,
      hostConnectionInfo: { enetPort: 33771 },
    },
    "203.0.113.10",
  );

  const result = store.modPreflight(created.roomId, {
    playerName: "Guest",
    gameVersion: "v0.109.0",
    localMods: [{ ...hostModInventory[0]!, version: "different" }],
  });

  assert.equal(result.gameVersion.exactMatch, false);
  assert.equal(result.canContinueRelaxed, false);
  assert.deepEqual(result.missingWorkshopMods, []);
  assert.deepEqual(result.missingManualMods, []);
  assert.deepEqual(result.extraGameplayMods, []);
  assert.deepEqual(result.versionMismatches, []);
});

test("modPreflight reports unavailable inventory for a v0.5.0 host", () => {
  const store = new LobbyStore({ ...baseConfig, strictModVersionCheck: false });
  const created = createBasicRoom(store);

  const result = store.modPreflight(created.roomId, {
    playerName: "Guest",
    gameVersion: "1.2.3",
    localMods: [],
  });

  assert.equal(result.hostInventoryAvailable, false);
  assert.deepEqual(result.missingWorkshopMods, []);
  assert.deepEqual(result.missingManualMods, []);
});

test("modPreflight disables relaxed continuation when strict mod checks are enabled", () => {
  const store = new LobbyStore(baseConfig);
  const created = store.createRoom(
    {
      roomName: "严格预检房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "v0.109.0",
      modVersion: "0.5.1",
      hostModInventory,
      maxPlayers: 4,
      hostConnectionInfo: { enetPort: 33771 },
    },
    "203.0.113.10",
  );

  const result = store.modPreflight(created.roomId, {
    playerName: "Guest",
    gameVersion: "v0.109.0",
    localMods: [],
  });
  assert.equal(result.canContinueRelaxed, false);
});

test("modPreflight rejects missing full and closed rooms without exposing inventory", () => {
  const input = {
    playerName: "Guest",
    gameVersion: "v0.109.0",
    localMods: [],
  };
  const missingStore = new LobbyStore(baseConfig);
  assert.throws(
    () => missingStore.modPreflight("missing", input),
    (error: unknown) => error instanceof LobbyStoreError && error.code === "room_not_found",
  );

  const fullStore = new LobbyStore(baseConfig);
  const full = fullStore.createRoom(
    {
      roomName: "满员房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "v0.109.0",
      modVersion: "0.5.1",
      hostModInventory,
      maxPlayers: 4,
      hostConnectionInfo: { enetPort: 33771 },
    },
    "203.0.113.10",
  );
  fullStore.heartbeat(full.roomId, { hostToken: full.hostToken, currentPlayers: 4, status: "full" });
  assert.throws(
    () => fullStore.modPreflight(full.roomId, input),
    (error: unknown) => error instanceof LobbyStoreError && error.code === "room_full",
  );

  const closedStore = new LobbyStore(baseConfig);
  const closed = closedStore.createRoom(
    {
      roomName: "关闭房间",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "v0.109.0",
      modVersion: "0.5.1",
      hostModInventory,
      maxPlayers: 4,
      hostConnectionInfo: { enetPort: 33771 },
    },
    "203.0.113.10",
  );
  closedStore.heartbeat(closed.roomId, { hostToken: closed.hostToken, currentPlayers: 1, status: "closed" });
  assert.throws(
    () => closedStore.modPreflight(closed.roomId, input),
    (error: unknown) => error instanceof LobbyStoreError && error.code === "room_closed",
  );
});
