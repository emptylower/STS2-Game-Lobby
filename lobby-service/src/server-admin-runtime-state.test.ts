import assert from "node:assert/strict";
import { spawn, type ChildProcess } from "node:child_process";
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { createLobbyService, type LobbyService } from "./app.js";
import { loadLobbyServiceConfig } from "./config.js";
import { hashServerAdminPassword } from "./server-admin-auth.js";

type PeerRuntimeState = "disabled" | "unconfigured" | "private" | "joining" | "joined";

interface PeerRecordSeed {
  address: string;
  publicKey: string;
  status: "active" | "offline";
}

interface StartedServer {
  baseUrl: string;
  close: () => Promise<void>;
  stateFile: string;
  tempDir: string;
}

const serverEntry = fileURLToPath(new URL("./server.js", import.meta.url));
const adminPassword = "test-password";
const adminPasswordHash = hashServerAdminPassword(adminPassword);

test("server admin settings reports peerRuntimeState=disabled when peer network is disabled", async (t) => {
  const started = await startLobbyServer({
    peerNetworkEnabled: false,
    selfAddress: "https://self.example",
    publicListingEnabled: true,
  });
  t.after(() => stopLobbyServer(started));

  const settings = await getSettings(started);
  assert.equal(settings.peerNetworkEnabled, false);
  assert.equal(settings.peerRuntimeState, "disabled");
});

test("server admin settings reports peerRuntimeState=unconfigured when self address is missing", async (t) => {
  const started = await startLobbyServer({
    peerNetworkEnabled: true,
    selfAddress: "",
    publicListingEnabled: true,
  });
  t.after(() => stopLobbyServer(started));

  const settings = await getSettings(started);
  assert.equal(settings.peerNetworkEnabled, true);
  assert.equal(settings.peerRuntimeState, "unconfigured");
});

test("server admin settings reports peerRuntimeState=private when public listing is disabled", async (t) => {
  const started = await startLobbyServer({
    peerNetworkEnabled: true,
    selfAddress: "https://self.example",
    publicListingEnabled: false,
    peers: [{ address: "https://peer.example", publicKey: "peer-public-key", status: "active" }],
  });
  t.after(() => stopLobbyServer(started));

  const settings = await getSettings(started);
  assert.equal(settings.peerNetworkEnabled, true);
  assert.equal(settings.peerRuntimeState, "private");
});

test("server admin settings reports peerRuntimeState=joining before any external active peer is known", async (t) => {
  const started = await startLobbyServer({
    peerNetworkEnabled: true,
    selfAddress: "https://self.example",
    publicListingEnabled: true,
    peers: [{ address: "https://peer.example", publicKey: "peer-public-key", status: "offline" }],
  });
  t.after(() => stopLobbyServer(started));

  const settings = await getSettings(started);
  assert.equal(settings.peerNetworkEnabled, true);
  assert.equal(settings.peerRuntimeState, "joining");
});

test("server admin settings reports peerRuntimeState=joining when only a canonicalized self variant is active", async (t) => {
  const started = await startLobbyServer({
    peerNetworkEnabled: true,
    selfAddress: "https://self.example",
    publicListingEnabled: true,
    peers: [{ address: "https://self.example/", publicKey: "peer-public-key", status: "active" }],
  });
  t.after(() => stopLobbyServer(started));

  const settings = await getSettings(started);
  assert.equal(settings.peerNetworkEnabled, true);
  assert.equal(settings.peerRuntimeState, "joining");
});

test("server admin settings reports peerRuntimeState=joined when an external active peer exists", async (t) => {
  const started = await startLobbyServer({
    peerNetworkEnabled: true,
    selfAddress: "https://self.example",
    publicListingEnabled: true,
    peers: [{ address: "https://peer.example", publicKey: "peer-public-key", status: "active" }],
  });
  t.after(() => stopLobbyServer(started));

  const settings = await getSettings(started);
  assert.equal(settings.peerNetworkEnabled, true);
  assert.equal(settings.peerRuntimeState, "joined");
});

test("server admin settings PATCH returns runtime fields without persisting them", async (t) => {
  const started = await startLobbyServer({
    peerNetworkEnabled: true,
    selfAddress: "https://self.example",
    publicListingEnabled: false,
    peers: [{ address: "https://peer.example", publicKey: "peer-public-key", status: "active" }],
  });
  t.after(() => stopLobbyServer(started));

  const response = await patchSettings(started, {
    displayName: "测试服务器",
    publicListingEnabled: true,
    announcements: [],
  });
  assert.equal(response.peerNetworkEnabled, true);
  assert.equal(response.peerRuntimeState, "joined");

  const persisted = JSON.parse(readFileSync(started.stateFile, "utf8")) as Record<string, unknown>;
  assert.equal(Object.prototype.hasOwnProperty.call(persisted, "peerNetworkEnabled"), false);
  assert.equal(Object.prototype.hasOwnProperty.call(persisted, "peerRuntimeState"), false);
});

test("CLI process exits cleanly on SIGTERM", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-peer-runtime-cli-"));
  const stateFile = join(tempDir, "server-admin.json");
  writeFileSync(
    stateFile,
    JSON.stringify({
      displayName: "",
      publicListingEnabled: true,
      announcements: [],
    }),
    "utf8",
  );

  const child = spawn(process.execPath, [serverEntry], {
    env: {
      ...process.env,
      HOST: "127.0.0.1",
      PORT: "0",
      SERVER_ADMIN_USERNAME: "admin",
      SERVER_ADMIN_PASSWORD_HASH: adminPasswordHash,
      SERVER_ADMIN_SESSION_SECRET: "test-session-secret",
      SERVER_ADMIN_STATE_FILE: stateFile,
      PEER_NETWORK_ENABLED: "false",
      PEER_SELF_ADDRESS: "",
      PEER_STATE_DIR: join(tempDir, "peer"),
      PEER_CF_DISCOVERY_BASE_URL: "",
    },
    stdio: ["ignore", "pipe", "pipe"],
  });

  let logs = "";
  child.stdout?.on("data", (chunk) => {
    logs += String(chunk);
  });
  child.stderr?.on("data", (chunk) => {
    logs += String(chunk);
  });

  try {
    await waitForCliReady(child, () => logs);
    child.kill("SIGTERM");
    const code = await waitForExit(child, 5_000);
    assert.equal(code, 0, logs);
  } finally {
    if (child.exitCode === null) {
      child.kill("SIGKILL");
      await waitForExit(child, 2_000);
    }
    rmSync(tempDir, { recursive: true, force: true });
  }
});

async function startLobbyServer(options: {
  peerNetworkEnabled: boolean;
  selfAddress: string;
  publicListingEnabled: boolean;
  peers?: PeerRecordSeed[];
}): Promise<StartedServer> {
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-peer-runtime-state-"));
  const stateFile = join(tempDir, "server-admin.json");
  const peerStateDir = join(tempDir, "peer");
  const peerStoreFile = join(peerStateDir, "peers.json");

  writeFileSync(
    stateFile,
    JSON.stringify({
      displayName: "",
      publicListingEnabled: options.publicListingEnabled,
      announcements: [],
    }),
    "utf8",
  );

  if (options.peers && options.peers.length > 0) {
    mkdirSync(dirname(peerStoreFile), { recursive: true });
    const now = new Date().toISOString();
    writeFileSync(
      peerStoreFile,
      JSON.stringify({
        version: 1,
        peers: options.peers.map((peer) => ({
          address: peer.address,
          publicKey: peer.publicKey,
          firstSeen: now,
          lastSeen: now,
          consecutiveProbeFailures: peer.status === "active" ? 0 : 3,
          status: peer.status,
          source: "seed",
        })),
      }),
      "utf8",
    );
  }

  const config = loadLobbyServiceConfig({
    HOST: "127.0.0.1",
    PORT: "0",
    SERVER_ADMIN_USERNAME: "admin",
    SERVER_ADMIN_PASSWORD_HASH: adminPasswordHash,
    SERVER_ADMIN_SESSION_SECRET: "test-session-secret",
    SERVER_ADMIN_STATE_FILE: stateFile,
    PEER_NETWORK_ENABLED: options.peerNetworkEnabled ? "true" : "false",
    PEER_SELF_ADDRESS: options.selfAddress,
    PEER_STATE_DIR: peerStateDir,
    PEER_CF_DISCOVERY_BASE_URL: "",
    ENFORCE_LOBBY_ACCESS_TOKEN: "false",
    ENFORCE_CREATE_ROOM_TOKEN: "false",
  });

  const service: LobbyService = await createLobbyService(config);
  const address = await service.start();
  const baseUrl = `http://127.0.0.1:${address.port}`;

  return {
    baseUrl,
    stateFile,
    tempDir,
    close: async () => {
      await service.close();
    },
  };
}

async function stopLobbyServer(started: StartedServer) {
  try {
    await started.close();
  } finally {
    rmSync(started.tempDir, { recursive: true, force: true });
  }
}

async function getSettings(started: StartedServer): Promise<{ peerNetworkEnabled: boolean; peerRuntimeState: PeerRuntimeState }> {
  const cookie = await login(started.baseUrl);
  const response = await fetch(`${started.baseUrl}/server-admin/settings`, {
    headers: { Cookie: cookie },
  });
  const text = await response.text();
  assert.equal(response.status, 200, text);
  return JSON.parse(text) as { peerNetworkEnabled: boolean; peerRuntimeState: PeerRuntimeState };
}

async function patchSettings(
  started: StartedServer,
  body: { displayName: string; publicListingEnabled: boolean; announcements: unknown[] },
): Promise<{ peerNetworkEnabled: boolean; peerRuntimeState: PeerRuntimeState }> {
  const cookie = await login(started.baseUrl);
  const response = await fetch(`${started.baseUrl}/server-admin/settings`, {
    method: "PATCH",
    headers: {
      "content-type": "application/json",
      Cookie: cookie,
    },
    body: JSON.stringify(body),
  });
  const text = await response.text();
  assert.equal(response.status, 200, text);
  return JSON.parse(text) as { peerNetworkEnabled: boolean; peerRuntimeState: PeerRuntimeState };
}

async function login(baseUrl: string): Promise<string> {
  const response = await fetch(`${baseUrl}/server-admin/login`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ username: "admin", password: adminPassword }),
  });
  const text = await response.text();
  assert.equal(response.status, 200, text);
  const setCookie = response.headers.get("set-cookie");
  assert.ok(setCookie, "expected login response to set a cookie");
  return setCookie.split(";", 1)[0]!;
}

async function waitForCliReady(child: ChildProcess, getLogs: () => string): Promise<void> {
  const startedAt = Date.now();
  while (Date.now() - startedAt < 10_000) {
    if (child.exitCode !== null) {
      throw new Error(`server exited before becoming ready\n${getLogs()}`);
    }
    if (getLogs().includes("[lobby] listening on")) {
      return;
    }
    await delay(50);
  }
  throw new Error(`timed out waiting for CLI readiness\n${getLogs()}`);
}

async function waitForExit(child: ChildProcess, timeoutMs: number): Promise<number | null> {
  const startedAt = Date.now();
  while (child.exitCode === null && Date.now() - startedAt < timeoutMs) {
    await delay(50);
  }

  if (child.exitCode === null) {
    child.kill("SIGKILL");
    while (child.exitCode === null) {
      await delay(20);
    }
  }

  return child.exitCode;
}

async function delay(ms: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms));
}
