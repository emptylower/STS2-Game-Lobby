import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { createLobbyService } from "./app.js";
import { loadLobbyServiceConfig, type LobbyServiceConfig } from "./config.js";

function testConfig(overrides: Partial<LobbyServiceConfig> = {}): LobbyServiceConfig {
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-lobby-app-"));
  const base = loadLobbyServiceConfig({
    HOST: "127.0.0.1",
    PORT: "0",
    PEER_NETWORK_ENABLED: "false",
    PEER_SELF_ADDRESS: "",
    PEER_CF_DISCOVERY_BASE_URL: "",
    SERVER_ADMIN_STATE_FILE: join(tempDir, "server-admin.json"),
    PEER_STATE_DIR: join(tempDir, "peer"),
    ENFORCE_LOBBY_ACCESS_TOKEN: "false",
    ENFORCE_CREATE_ROOM_TOKEN: "false",
  });

  return {
    ...base,
    ...overrides,
    peer: {
      ...base.peer,
      ...(overrides.peer ?? {}),
    },
    chat: {
      ...base.chat,
      ...(overrides.chat ?? {}),
    },
  };
}

test("factory does not listen until start and closes all resources", async () => {
  const config = testConfig({ port: 0 });
  const service = await createLobbyService(config);
  assert.equal(service.httpServer.listening, false);
  const address = await service.start();
  assert.ok(address.port > 0);
  await service.close();
  assert.equal(service.httpServer.listening, false);

  // Best-effort cleanup of temp admin state directory.
  try {
    rmSync(join(config.serverAdminStateFile, ".."), { recursive: true, force: true });
  } catch {
    // ignore cleanup failures
  }
});
