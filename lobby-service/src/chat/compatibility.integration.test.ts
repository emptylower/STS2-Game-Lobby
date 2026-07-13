import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { WebSocket } from "ws";
import { createLobbyService } from "../app.js";
import { loadLobbyServiceConfig, type LobbyServiceConfig } from "../config.js";

type JsonFrame = Record<string, unknown> & { type: string };

interface CreatedRoom {
  roomId: string;
  roomSessionId: string;
  controlChannelId: string;
  hostToken: string;
  room: {
    maxPlayers: number;
    modVersion: string;
    relayState: string;
  };
  relayEndpoint: { host: string; port: number };
}

interface JoinedRoom {
  ticketId: string;
  roomSessionId: string;
  room: { modVersion: string; relayState: string };
  connectionPlan: {
    controlChannelId: string;
    relayAllowed: boolean;
    relayEndpoint: { host: string; port: number };
  };
}

interface LegacyFixture {
  modVersion: "0.4.0" | "0.2.2";
}

const fixtures: LegacyFixture[] = [
  { modVersion: "0.4.0" },
  { modVersion: "0.2.2" },
];

function compatibilityConfig(): LobbyServiceConfig {
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-lobby-compat-"));
  const relayPortStart = 45_000 + (process.pid % 10_000);
  return loadLobbyServiceConfig({
    HOST: "127.0.0.1",
    PORT: "0",
    PEER_NETWORK_ENABLED: "false",
    PEER_SELF_ADDRESS: "",
    PEER_CF_DISCOVERY_BASE_URL: "",
    SERVER_ADMIN_STATE_FILE: join(tempDir, "server-admin.json"),
    PEER_STATE_DIR: join(tempDir, "peer"),
    ENFORCE_LOBBY_ACCESS_TOKEN: "false",
    ENFORCE_CREATE_ROOM_TOKEN: "false",
    SERVER_CHAT_ENABLED: "true",
    RELAY_BIND_HOST: "127.0.0.1",
    RELAY_PORT_START: String(relayPortStart),
    RELAY_PORT_END: String(relayPortStart + 8),
  });
}

function cleanupTempDir(config: LobbyServiceConfig): void {
  rmSync(join(config.serverAdminStateFile, ".."), { recursive: true, force: true });
}

function waitForFrame(
  socket: WebSocket,
  predicate: (frame: JsonFrame) => boolean,
  timeoutMs = 2_000,
): Promise<JsonFrame> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      cleanup();
      reject(new Error("control websocket frame timeout"));
    }, timeoutMs);
    const onMessage = (data: WebSocket.RawData) => {
      const frame = JSON.parse(data.toString()) as JsonFrame;
      if (!predicate(frame)) return;
      cleanup();
      resolve(frame);
    };
    const onClose = (code: number) => {
      cleanup();
      reject(new Error(`control websocket closed before frame (${code})`));
    };
    const cleanup = () => {
      clearTimeout(timer);
      socket.off("message", onMessage);
      socket.off("close", onClose);
    };
    socket.on("message", onMessage);
    socket.once("close", onClose);
  });
}

async function openControlWebSocket(
  url: string,
): Promise<{ socket: WebSocket; connected: JsonFrame }> {
  const socket = new WebSocket(url);
  const connectedPromise = waitForFrame(socket, (frame) => frame.type === "connected");
  await new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => {
      socket.terminate();
      reject(new Error("websocket connect timeout"));
    }, 2_000);
    socket.once("open", () => {
      clearTimeout(timer);
      resolve();
    });
    socket.once("error", (error) => {
      clearTimeout(timer);
      reject(error);
    });
  });
  return { socket, connected: await connectedPromise };
}

function rejectedWebSocketStatus(url: string): Promise<number> {
  const socket = new WebSocket(url);
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      socket.terminate();
      reject(new Error("websocket rejection timeout"));
    }, 2_000);
    socket.once("open", () => {
      clearTimeout(timer);
      socket.terminate();
      reject(new Error("websocket unexpectedly upgraded"));
    });
    socket.once("unexpected-response", (_request, response) => {
      clearTimeout(timer);
      const status = response.statusCode ?? 0;
      response.resume();
      resolve(status);
    });
    socket.once("error", () => {
      // `unexpected-response` is authoritative for an HTTP upgrade rejection.
    });
  });
}

test("new server preserves 0.4.0 and 0.2.2 legacy lobby/control compatibility", async (t) => {
  for (const fixture of fixtures) {
    await t.test(`mod ${fixture.modVersion}`, async () => {
      const config = compatibilityConfig();
      const service = await createLobbyService(config);
      const address = await service.start();
      const sockets: WebSocket[] = [];

      try {
        const createBody: Record<string, unknown> = {
          roomName: `compat-${fixture.modVersion}`,
          hostPlayerName: "Host",
          gameMode: "standard",
          version: "1.0.0",
          modVersion: fixture.modVersion,
          modList: ["sts2_lan_connect"],
          maxPlayers: 4,
          hostConnectionInfo: {
            enetPort: 7777,
            localAddresses: ["127.0.0.1"],
          },
        };

        const createResponse = await fetch(`http://127.0.0.1:${address.port}/rooms`, {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify(createBody),
        });
        assert.equal(createResponse.status, 201, `create status drifted for ${fixture.modVersion}`);
        const created = (await createResponse.json()) as CreatedRoom;
        assert.equal(created.room.modVersion, fixture.modVersion);
        assert.equal(created.room.maxPlayers, 4);
        assert.equal(created.room.relayState, "planned");
        assert.equal(typeof created.roomSessionId, "string");
        assert.ok(created.roomSessionId.length > 0);
        assert.ok(created.relayEndpoint.port > 0, "create must retain Relay-adjacent allocation");

        const joinResponse = await fetch(
          `http://127.0.0.1:${address.port}/rooms/${encodeURIComponent(created.roomId)}/join`,
          {
            method: "POST",
            headers: { "content-type": "application/json" },
            body: JSON.stringify({
              playerName: "Silent",
              version: "1.0.0",
              modVersion: fixture.modVersion,
              modList: ["sts2_lan_connect"],
              playerNetId: "222",
            }),
          },
        );
        assert.equal(joinResponse.status, 200, `join status drifted for ${fixture.modVersion}`);
        const joined = (await joinResponse.json()) as JoinedRoom;
        assert.equal(joined.roomSessionId, created.roomSessionId, "roomSessionId must be additive and stable");
        assert.equal(joined.room.modVersion, fixture.modVersion);
        assert.equal(joined.room.relayState, "planned");
        assert.equal(joined.connectionPlan.controlChannelId, created.controlChannelId);
        assert.equal(joined.connectionPlan.relayAllowed, true);
        assert.deepEqual(joined.connectionPlan.relayEndpoint, created.relayEndpoint);

        const queryBase =
          `roomId=${encodeURIComponent(created.roomId)}` +
          `&controlChannelId=${encodeURIComponent(created.controlChannelId)}`;
        const openedHost = await openControlWebSocket(
          `ws://127.0.0.1:${address.port}${config.wsPath}?${queryBase}` +
            `&role=host&token=${encodeURIComponent(created.hostToken)}`,
        );
        const host = openedHost.socket;
        sockets.push(host);
        const openedClient = await openControlWebSocket(
          `ws://127.0.0.1:${address.port}${config.wsPath}?${queryBase}` +
            `&role=client&ticketId=${encodeURIComponent(joined.ticketId)}`,
        );
        const client = openedClient.socket;
        sockets.push(client);

        assert.equal(openedHost.connected.role, "host");
        assert.equal(openedClient.connected.role, "client");

        const hostHello =
          `{"type":"host_hello","roomId":"${created.roomId}",` +
          `"controlChannelId":"${created.controlChannelId}","role":"host","playerName":"Host"}`;
        const clientHello =
          `{"type":"client_hello","roomId":"${created.roomId}",` +
          `"controlChannelId":"${created.controlChannelId}","role":"client",` +
          `"ticketId":"${joined.ticketId}","playerName":"Silent","playerNetId":"222"}`;
        host.send(hostHello);
        client.send(clientHello);

        const expectedRoomChat = {
          type: "room_chat",
          roomId: created.roomId,
          controlChannelId: created.controlChannelId,
          role: "client",
          ticketId: joined.ticketId,
          playerName: "Silent",
          playerNetId: "222",
          messageId: "legacy-message-1",
          messageText: "legacy text",
          sentAtUnixMs: 1_783_857_600_000,
        };
        const relayedPromise = waitForFrame(
          host,
          (frame) => frame.type === "room_chat" && frame.messageId === "legacy-message-1",
        );
        const roomChat =
          `{"type":"room_chat","roomId":"${created.roomId}",` +
          `"controlChannelId":"${created.controlChannelId}","role":"client",` +
          `"ticketId":"${joined.ticketId}","playerName":"Silent","playerNetId":"222",` +
          `"messageId":"legacy-message-1","messageText":"legacy text",` +
          `"sentAtUnixMs":1783857600000}`;
        client.send(roomChat);
        assert.deepEqual(await relayedPromise, expectedRoomChat, "legacy room_chat fields must remain exact");

        assert.equal(
          await rejectedWebSocketStatus(
            `ws://127.0.0.1:${address.port}/chat?${queryBase}` +
              `&role=host&token=${encodeURIComponent(created.hostToken)}`,
          ),
          401,
          "/chat must not accept /control query credentials",
        );
      } finally {
        for (const socket of sockets) socket.terminate();
        await service.close();
        cleanupTempDir(config);
      }
    });
  }
});
