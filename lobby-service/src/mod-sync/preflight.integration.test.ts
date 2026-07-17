import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { createLobbyService } from "../app.js";
import { loadLobbyServiceConfig, type LobbyServiceConfig } from "../config.js";
import type { LobbyModDescriptor } from "./protocol.js";

const HOST_INVENTORY: LobbyModDescriptor[] = [
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

function testConfig(overrides: Partial<LobbyServiceConfig> = {}): LobbyServiceConfig {
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-mod-preflight-"));
  const base = loadLobbyServiceConfig({
    HOST: "127.0.0.1",
    PORT: "0",
    PEER_NETWORK_ENABLED: "false",
    SERVER_ADMIN_STATE_FILE: join(tempDir, "server-admin.json"),
    PEER_STATE_DIR: join(tempDir, "peer"),
    ENFORCE_LOBBY_ACCESS_TOKEN: "false",
    ENFORCE_CREATE_ROOM_TOKEN: "false",
    PUBLIC_ROOM_LIST_ENABLED: "true",
    MOD_SYNC_ENABLED: "true",
    STRICT_GAME_VERSION_CHECK: "false",
    STRICT_MOD_VERSION_CHECK: "false",
  });
  return { ...base, ...overrides };
}

function cleanup(config: LobbyServiceConfig): void {
  rmSync(join(config.serverAdminStateFile, ".."), { recursive: true, force: true });
}

async function createRoom(
  port: number,
  overrides: Record<string, unknown> = {},
): Promise<{ roomId: string; hostToken: string; room: Record<string, unknown> }> {
  const response = await fetch(`http://127.0.0.1:${port}/rooms`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      roomName: "mod-preflight",
      password: "room-secret",
      hostPlayerName: "Host",
      gameMode: "standard",
      version: "v0.109.0",
      modVersion: "0.5.1",
      modList: [],
      hostModInventory: HOST_INVENTORY,
      maxPlayers: 4,
      hostConnectionInfo: { enetPort: 7777, localAddresses: ["127.0.0.1"] },
      ...overrides,
    }),
  });
  assert.equal(response.status, 201);
  return response.json() as Promise<{ roomId: string; hostToken: string; room: Record<string, unknown> }>;
}

async function preflight(
  port: number,
  roomId: string,
  overrides: Record<string, unknown> = {},
): Promise<Response> {
  return fetch(`http://127.0.0.1:${port}/rooms/${roomId}/mod-preflight`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
      playerName: "Guest",
      password: "room-secret",
      gameVersion: "v0.109.0",
      modSyncProtocolVersion: 1,
      localMods: [],
      ...overrides,
    }),
  });
}

test("probe advertises exact enabled and disabled mod sync capabilities", async () => {
  for (const enabled of [true, false]) {
    const config = testConfig({ port: 0, modSyncEnabled: enabled });
    const service = await createLobbyService(config);
    const address = await service.start();
    try {
      const response = await fetch(`http://127.0.0.1:${address.port}/probe`);
      const body = await response.json() as { capabilities: Record<string, unknown> };
      assert.equal(body.capabilities.modSyncProtocolVersion, 1);
      assert.equal(body.capabilities.modSyncEnabled, enabled);
    } finally {
      await service.close();
      cleanup(config);
    }
  }
});

test("private preflight returns canonical differences without leaking inventory publicly", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const address = await service.start();
  try {
    const created = await createRoom(address.port);
    assert.equal(Object.hasOwn(created, "hostModInventory"), false);
    assert.equal(Object.hasOwn(created.room, "hostModInventory"), false);

    const listResponse = await fetch(`http://127.0.0.1:${address.port}/rooms`);
    const rooms = await listResponse.json() as Array<Record<string, unknown>>;
    assert.equal(Object.hasOwn(rooms[0]!, "hostModInventory"), false);

    const response = await preflight(address.port, created.roomId);
    assert.equal(response.status, 200);
    const body = await response.json() as Record<string, unknown> & {
      missingWorkshopMods: LobbyModDescriptor[];
      missingManualMods: LobbyModDescriptor[];
    };
    assert.equal(body.enabled, true);
    assert.equal(body.protocolVersion, 1);
    assert.equal(body.hostInventoryAvailable, true);
    assert.deepEqual(body.missingWorkshopMods.map((mod) => mod.id), ["host.workshop"]);
    assert.deepEqual(body.missingManualMods.map((mod) => mod.id), ["shared.dependency"]);
    assert.equal(Object.hasOwn(body, "ticketId"), false);
    assert.equal(Object.hasOwn(body, "inventoryHash"), false);
  } finally {
    await service.close();
    cleanup(config);
  }
});

test("preflight validates password before returning private inventory details", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const address = await service.start();
  try {
    const created = await createRoom(address.port);
    const response = await preflight(address.port, created.roomId, {
      password: "wrong",
      localMods: [{ ...HOST_INVENTORY[0], downloadUrl: "https://invalid.example/private.zip" }],
    });
    assert.equal(response.status, 401);
    const text = await response.text();
    assert.match(text, /invalid_password/);
    assert.doesNotMatch(text, /host\.workshop|shared\.dependency|3747497501/);
  } finally {
    await service.close();
    cleanup(config);
  }
});

test("preflight hard-blocks game mismatch before returning mod differences", async () => {
  const config = testConfig({ port: 0, strictGameVersionCheck: false, strictModVersionCheck: false });
  const service = await createLobbyService(config);
  const address = await service.start();
  try {
    const created = await createRoom(address.port, { version: "v0.108.0" });
    const response = await preflight(address.port, created.roomId, { gameVersion: "v0.109.0" });
    assert.equal(response.status, 200);
    const body = await response.json() as {
      gameVersion: { exactMatch: boolean };
      canContinueRelaxed: boolean;
      missingWorkshopMods: unknown[];
      missingManualMods: unknown[];
    };
    assert.equal(body.gameVersion.exactMatch, false);
    assert.equal(body.canContinueRelaxed, false);
    assert.deepEqual(body.missingWorkshopMods, []);
    assert.deepEqual(body.missingManualMods, []);
  } finally {
    await service.close();
    cleanup(config);
  }
});

test("preflight returns hostInventoryAvailable false for a v0.5.0 host", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const address = await service.start();
  try {
    const created = await createRoom(address.port, { hostModInventory: undefined });
    const response = await preflight(address.port, created.roomId);
    assert.equal(response.status, 200);
    const body = await response.json() as { hostInventoryAvailable: boolean; missingWorkshopMods: unknown[] };
    assert.equal(body.hostInventoryAvailable, false);
    assert.deepEqual(body.missingWorkshopMods, []);
  } finally {
    await service.close();
    cleanup(config);
  }
});

test("disabled preflight returns structured fallback without issuing a join ticket", async () => {
  const config = testConfig({ port: 0, modSyncEnabled: false });
  const service = await createLobbyService(config);
  const address = await service.start();
  try {
    const created = await createRoom(address.port);
    const response = await preflight(address.port, created.roomId);
    assert.equal(response.status, 200);
    const body = await response.json() as Record<string, unknown>;
    assert.equal(body.enabled, false);
    assert.equal(body.protocolVersion, 1);
    assert.equal(body.hostInventoryAvailable, false);
    assert.equal(Object.hasOwn(body, "ticketId"), false);
  } finally {
    await service.close();
    cleanup(config);
  }
});

test("preflight rejects malicious descriptors and shares the join rate limit", async () => {
  const config = testConfig({
    port: 0,
    createJoinRateLimitMaxRequests: 1,
  });
  const service = await createLobbyService(config);
  const address = await service.start();
  try {
    const created = await createRoom(address.port);
    const invalid = await preflight(address.port, created.roomId, {
      localMods: [{ ...HOST_INVENTORY[0], downloadUrl: "https://invalid.example/mod.zip" }],
    });
    assert.equal(invalid.status, 400);
    assert.equal((await invalid.json() as { code: string }).code, "invalid_request");

    const limited = await preflight(address.port, created.roomId);
    assert.equal(limited.status, 429);
    assert.equal((await limited.json() as { code: string }).code, "rate_limited");
  } finally {
    await service.close();
    cleanup(config);
  }
});

test("preflight accepts a valid canonical inventory between 32 and 64 KiB", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const address = await service.start();
  try {
    const created = await createRoom(address.port);
    const localMods: LobbyModDescriptor[] = Array.from({ length: 18 }, (_, index) => ({
      id: `${String(index).padStart(2, "0")}.${"m".repeat(125)}`,
      version: "v".repeat(64),
      role: "dependency",
      source: "mods_directory",
      dependencies: Array.from(
        { length: 16 },
        (__, dependency) => `dep.${index}.${dependency}.${"x".repeat(110)}`,
      ),
    }));
    const body = JSON.stringify({
      playerName: "Guest",
      password: "room-secret",
      gameVersion: "v0.109.0",
      modSyncProtocolVersion: 1,
      localMods,
    });
    assert.ok(Buffer.byteLength(body) > 32 * 1024);
    assert.ok(Buffer.byteLength(body) < 65_536);

    const response = await fetch(`http://127.0.0.1:${address.port}/rooms/${created.roomId}/mod-preflight`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body,
    });
    assert.equal(response.status, 200);
  } finally {
    await service.close();
    cleanup(config);
  }
});

test("preflight logs only counts and hash without inventory password or tokens", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  const address = await service.start();
  const logs: string[] = [];
  const originalLog = console.log;
  console.log = (...values: unknown[]) => {
    logs.push(values.map((value) => String(value)).join(" "));
  };
  try {
    const created = await createRoom(address.port);
    const response = await preflight(address.port, created.roomId);
    assert.equal(response.status, 200);
  } finally {
    console.log = originalLog;
    await service.close();
    cleanup(config);
  }

  const output = logs.join("\n");
  assert.match(output, /inventoryHash=[0-9a-f]{64}/);
  assert.match(output, /missingWorkshop=1/);
  assert.doesNotMatch(output, /host\.workshop|shared\.dependency|3747497501|room-secret/);
});

test("concurrent preflights are deterministic and never mutate rooms or issue tickets", async () => {
  const config = testConfig({
    port: 0,
    createJoinRateLimitMaxRequests: 100,
  });
  const service = await createLobbyService(config);
  const address = await service.start();
  try {
    const created = await createRoom(address.port);
    const beforeResponse = await fetch(`http://127.0.0.1:${address.port}/rooms`);
    const before = await beforeResponse.json() as Array<Record<string, unknown>>;

    const responses = await Promise.all(Array.from({ length: 24 }, () =>
      preflight(address.port, created.roomId)));
    assert.deepEqual(responses.map((response) => response.status), Array(24).fill(200));
    const bodies = await Promise.all(responses.map((response) => response.json() as Promise<Record<string, unknown>>));
    const canonical = JSON.stringify(bodies[0]);
    assert.ok(bodies.every((body) => JSON.stringify(body) === canonical));
    assert.ok(bodies.every((body) => !Object.hasOwn(body, "ticketId") && !Object.hasOwn(body, "hostToken")));

    const afterResponse = await fetch(`http://127.0.0.1:${address.port}/rooms`);
    const after = await afterResponse.json() as Array<Record<string, unknown>>;
    assert.deepEqual(after, before);
    assert.equal(Object.hasOwn(after[0]!, "hostModInventory"), false);
  } finally {
    await service.close();
    cleanup(config);
  }
});
