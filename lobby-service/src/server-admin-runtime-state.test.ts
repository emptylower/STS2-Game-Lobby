import assert from "node:assert/strict";
import { spawn, type ChildProcess } from "node:child_process";
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { createServer as createNetServer } from "node:net";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { hashServerAdminPassword } from "./server-admin-auth.js";

type PeerRuntimeState = "disabled" | "unconfigured" | "private" | "joining" | "joined";

interface PeerRecordSeed {
  address: string;
  publicKey: string;
  status: "active" | "offline";
}

interface StartedServer {
  baseUrl: string;
  child: ChildProcess;
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

async function startLobbyServer(options: {
  peerNetworkEnabled: boolean;
  selfAddress: string;
  publicListingEnabled: boolean;
  peers?: PeerRecordSeed[];
}): Promise<StartedServer> {
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-peer-runtime-state-"));
  const port = await reservePort();
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

  const child = spawn(process.execPath, [serverEntry], {
    env: {
      ...process.env,
      HOST: "127.0.0.1",
      PORT: String(port),
      SERVER_ADMIN_USERNAME: "admin",
      SERVER_ADMIN_PASSWORD_HASH: adminPasswordHash,
      SERVER_ADMIN_SESSION_SECRET: "test-session-secret",
      SERVER_ADMIN_STATE_FILE: stateFile,
      PEER_NETWORK_ENABLED: options.peerNetworkEnabled ? "true" : "false",
      PEER_SELF_ADDRESS: options.selfAddress,
      PEER_STATE_DIR: peerStateDir,
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

  const baseUrl = `http://127.0.0.1:${port}`;
  await waitForServerReady(baseUrl, child, () => logs);
  return { baseUrl, child, stateFile, tempDir };
}

async function stopLobbyServer(started: StartedServer) {
  try {
    if (started.child.exitCode === null) {
      started.child.kill("SIGTERM");
      await waitForExit(started.child, 5_000);
    }
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

async function reservePort(): Promise<number> {
  return await new Promise<number>((resolve, reject) => {
    const server = createNetServer();
    server.on("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      if (!address || typeof address === "string") {
        server.close(() => reject(new Error("failed to reserve test port")));
        return;
      }

      server.close((error) => {
        if (error) {
          reject(error);
          return;
        }
        resolve(address.port);
      });
    });
  });
}

async function waitForServerReady(baseUrl: string, child: ChildProcess, getLogs: () => string): Promise<void> {
  const startedAt = Date.now();
  while (Date.now() - startedAt < 10_000) {
    if (child.exitCode !== null) {
      throw new Error(`server exited before becoming ready\n${getLogs()}`);
    }

    try {
      const response = await fetch(`${baseUrl}/probe`);
      if (response.ok) {
        return;
      }
    } catch {
      // keep polling until the server is reachable
    }

    await delay(100);
  }

  throw new Error(`timed out waiting for server readiness\n${getLogs()}`);
}

async function waitForExit(child: ChildProcess, timeoutMs: number): Promise<void> {
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
}

async function delay(ms: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms));
}