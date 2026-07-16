import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { WebSocket } from "ws";
import { createLobbyService } from "../app.js";
import { loadLobbyServiceConfig } from "../config.js";
import type { ChatFeatureVersions } from "./feature-resolver.js";
import type { ChatContent } from "./protocol.js";

type Frame = Record<string, unknown> & { type: string };

interface TrackedPeer {
  name: string;
  socket: WebSocket;
  frames: Frame[];
}

const allVersions: ChatFeatureVersions = {
  richContentVersion: 1,
  emojiSetVersion: 1,
  itemRefVersion: 1,
  combatRefVersion: 1,
};

const noItemVersions: ChatFeatureVersions = {
  ...allVersions,
  itemRefVersion: 0,
};

const noCombatVersions: ChatFeatureVersions = {
  ...allVersions,
  combatRefVersion: 0,
};

const disabledVersions: ChatFeatureVersions = {
  richContentVersion: 0,
  emojiSetVersion: 0,
  itemRefVersion: 0,
  combatRefVersion: 0,
};

function waitForFrame(
  peer: TrackedPeer,
  start: number,
  predicate: (frame: Frame) => boolean,
): Promise<Frame> {
  const existing = peer.frames.slice(start).find(predicate);
  if (existing) return Promise.resolve(existing);
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      cleanup();
      reject(new Error(`${peer.name} frame timeout`));
    }, 2_000);
    const onMessage = (data: WebSocket.RawData) => {
      const frame = JSON.parse(data.toString()) as Frame;
      if (!predicate(frame)) return;
      cleanup();
      resolve(frame);
    };
    const onClose = (code: number) => {
      cleanup();
      reject(new Error(`${peer.name} closed before frame (${code})`));
    };
    const cleanup = () => {
      clearTimeout(timer);
      peer.socket.off("message", onMessage);
      peer.socket.off("close", onClose);
    };
    peer.socket.on("message", onMessage);
    peer.socket.once("close", onClose);
  });
}

function openPeer(name: string, url: string): Promise<TrackedPeer> {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url);
    const peer: TrackedPeer = { name, socket, frames: [] };
    const timer = setTimeout(() => fail(new Error(`${name} connect timeout`)), 2_000);
    const onMessage = (data: WebSocket.RawData) => {
      const frame = JSON.parse(data.toString()) as Frame;
      peer.frames.push(frame);
      if (frame.type === "connected") {
        cleanup();
        resolve(peer);
      }
    };
    const onError = (error: Error) => fail(error);
    const onClose = (code: number) => fail(new Error(`${name} closed while connecting (${code})`));
    const cleanup = () => {
      clearTimeout(timer);
      socket.off("error", onError);
      socket.off("close", onClose);
    };
    const fail = (error: Error) => {
      cleanup();
      socket.terminate();
      reject(error);
    };
    socket.on("message", onMessage);
    socket.once("error", onError);
    socket.once("close", onClose);
  });
}

test("real room service routes each whole combat message by recipient capability", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "sts2-room-routing-"));
  const relayPortStart = 46_000 + ((process.pid * 17) % 15_000);
  const config = loadLobbyServiceConfig({
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
  const service = await createLobbyService(config);
  const peers: TrackedPeer[] = [];

  try {
    const address = await service.start();
    const httpBase = `http://127.0.0.1:${address.port}`;
    const wsBase = `ws://127.0.0.1:${address.port}${config.wsPath}`;
    const createResponse = await fetch(`${httpBase}/rooms`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        roomName: "combat-routing",
        hostPlayerName: "Host",
        gameMode: "standard",
        version: "1.0.0",
        modVersion: "0.4.0",
        modList: ["sts2_lan_connect"],
        maxPlayers: 8,
        hostConnectionInfo: { enetPort: 7777, localAddresses: ["127.0.0.1"] },
      }),
    });
    assert.equal(createResponse.status, 201);
    const created = await createResponse.json() as {
      roomId: string;
      roomSessionId: string;
      controlChannelId: string;
      hostToken: string;
    };

    const sender = await openPeer(
      "sender",
      `${wsBase}?roomId=${created.roomId}&controlChannelId=${created.controlChannelId}`
        + `&role=host&token=${created.hostToken}`,
    );
    peers.push(sender);

    const peerDefinitions = [
      ["full", "net:full", allVersions],
      ["noItem", "net:no-item", noItemVersions],
      ["noCombat", "net:no-combat", noCombatVersions],
      ["disabled", "net:disabled", disabledVersions],
      ["legacy", "net:legacy", null],
    ] as const;
    const recipients: TrackedPeer[] = [];
    for (const [name, playerNetId] of peerDefinitions) {
      const joinResponse = await fetch(`${httpBase}/rooms/${created.roomId}/join`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          playerName: name,
          playerNetId,
          version: "1.0.0",
          modVersion: "0.4.0",
          modList: ["sts2_lan_connect"],
        }),
      });
      assert.equal(joinResponse.status, 200, name);
      const joined = await joinResponse.json() as { ticketId: string };
      const recipient = await openPeer(
        name,
        `${wsBase}?roomId=${created.roomId}&controlChannelId=${created.controlChannelId}`
          + `&role=client&ticketId=${joined.ticketId}`,
      );
      peers.push(recipient);
      recipients.push(recipient);
    }

    const senderReadyStart = sender.frames.length;
    sender.socket.send(JSON.stringify({
      type: "host_hello",
      roomId: created.roomId,
      controlChannelId: created.controlChannelId,
      role: "host",
      playerName: "Host",
      playerNetId: "net:host",
      roomChatVersions: allVersions,
    }));
    const senderReady = await waitForFrame(sender, senderReadyStart, (frame) => frame.type === "room_chat_ready");
    assert.deepEqual(senderReady.enabledFeatures, allVersions);

    for (let index = 0; index < recipients.length; index += 1) {
      const recipient = recipients[index]!;
      const [name, playerNetId, versions] = peerDefinitions[index]!;
      const start = recipient.frames.length;
      recipient.socket.send(JSON.stringify({
        type: "client_hello",
        roomId: created.roomId,
        controlChannelId: created.controlChannelId,
        role: "client",
        playerName: name,
        playerNetId,
        ...(versions === null ? {} : { roomChatVersions: versions }),
      }));
      if (versions === null) {
        const pong = waitForFrame(recipient, start, (frame) => frame.type === "pong");
        recipient.socket.send(JSON.stringify({ type: "ping" }));
        await pong;
      } else {
        const ready = await waitForFrame(recipient, start, (frame) => frame.type === "room_chat_ready");
        const expected = versions.richContentVersion === 0 ? disabledVersions : versions;
        assert.deepEqual(ready.enabledFeatures, expected, name);
      }
    }

    const allPeers = [sender, ...recipients];
    const sendCase = async (
      clientMessageId: string,
      content: ChatContent,
      fallback: string,
      richPeerNames: ReadonlySet<string>,
    ) => {
      const starts = new Map(allPeers.map((peer) => [peer.name, peer.frames.length]));
      const ackPromise = waitForFrame(
        sender,
        starts.get(sender.name)!,
        (frame) => frame.type === "room_chat_ack" && frame.clientMessageId === clientMessageId,
      );
      const deliveryPromises = allPeers.map((peer) => waitForFrame(
        peer,
        starts.get(peer.name)!,
        (frame) => richPeerNames.has(peer.name)
          ? frame.type === "room_chat_message"
            && (frame.message as Record<string, unknown> | undefined)?.plainTextFallback === fallback
          : frame.type === "room_chat" && frame.messageText === fallback,
      ));
      sender.socket.send(JSON.stringify({
        type: "room_chat_v2",
        protocolVersion: 1,
        clientMessageId,
        roomId: created.roomId,
        roomSessionId: created.roomSessionId,
        content,
      }));
      const [ack, deliveries] = await Promise.all([ackPromise, Promise.all(deliveryPromises)]);
      const message = ack.message as Record<string, unknown>;
      assert.equal(message.senderName, "Host");
      assert.equal(message.senderId, "net:host");
      assert.equal(message.roomId, created.roomId);
      assert.equal(message.roomSessionId, created.roomSessionId);
      assert.equal(message.plainTextFallback, fallback);
      assert.deepEqual(message.content, content);

      for (let index = 0; index < allPeers.length; index += 1) {
        const peer = allPeers[index]!;
        const delivery = deliveries[index]!;
        if (richPeerNames.has(peer.name)) {
          assert.deepEqual(delivery, {
            type: "room_chat_message",
            protocolVersion: 1,
            message,
          }, peer.name);
        } else {
          assert.deepEqual(delivery, {
            type: "room_chat",
            roomId: created.roomId,
            playerName: "Host",
            playerNetId: "net:host",
            messageId: message.messageId,
            messageText: fallback,
            sentAtUnixMs: Date.parse(String(message.sentAt)),
          }, peer.name);
        }
      }

      const pongPromises = allPeers.map((peer) => {
        const start = peer.frames.length;
        const pong = waitForFrame(peer, start, (frame) => frame.type === "pong");
        peer.socket.send(JSON.stringify({ type: "ping" }));
        return pong;
      });
      await Promise.all(pongPromises);
      for (const peer of allPeers) {
        const matchingDeliveries = peer.frames.filter((frame) => (
          frame.type === "room_chat_message"
            ? (frame.message as Record<string, unknown> | undefined)?.messageId === message.messageId
            : frame.type === "room_chat" && frame.messageId === message.messageId
        ));
        assert.equal(matchingDeliveries.length, 1, `${peer.name} delivery count`);
      }
    };

    await sendCase(
      "11111111-1111-4111-8111-111111111111",
      { formatVersion: 1, segments: [{ kind: "text", text: "plain" }] },
      "plain",
      new Set(["sender", "full", "noItem", "noCombat", "disabled"]),
    );
    await sendCase(
      "22222222-2222-4222-8222-222222222222",
      { formatVersion: 1, segments: [{ kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" }] },
      "[Card]",
      new Set(["sender", "full", "noCombat"]),
    );
    await sendCase(
      "33333333-3333-4333-8333-333333333333",
      {
        formatVersion: 1,
        segments: [
          { kind: "item_ref", itemType: "card", modelId: "MegaCrit.Strike" },
          {
            kind: "power_state",
            modelId: "MegaCrit.Strength",
            amount: 2,
            roomSessionId: created.roomSessionId,
            ownerPlayerNetId: "net:host",
          },
        ],
      },
      "[Card][Power]",
      new Set(["sender", "full"]),
    );
    await sendCase(
      "44444444-4444-4444-8444-444444444444",
      {
        formatVersion: 1,
        segments: [{
          kind: "target_ref",
          targetKind: "player",
          targetKey: "net:full",
          roomSessionId: created.roomSessionId,
        }],
      },
      "[Player]",
      new Set(["sender", "full", "noItem"]),
    );
    const monsterClientMessageId = "55555555-5555-4555-8555-555555555555";
    const monsterStarts = new Map(allPeers.map((peer) => [peer.name, peer.frames.length]));
    const monsterErrorPromise = waitForFrame(
      sender,
      monsterStarts.get(sender.name)!,
      (frame) => frame.type === "room_chat_error"
        && frame.clientMessageId === monsterClientMessageId,
    );
    sender.socket.send(JSON.stringify({
      type: "room_chat_v2",
      protocolVersion: 1,
      clientMessageId: monsterClientMessageId,
      roomId: created.roomId,
      roomSessionId: created.roomSessionId,
      content: {
        formatVersion: 1,
        segments: [{
          kind: "target_ref",
          targetKind: "monster",
          targetKey: "unstable-monster-id",
          roomSessionId: created.roomSessionId,
        }],
      },
    }));
    const monsterError = await monsterErrorPromise;
    assert.equal(monsterError.code, "feature_disabled");

    const monsterBarrier = allPeers.map((peer) => {
      const start = peer.frames.length;
      const pong = waitForFrame(peer, start, (frame) => frame.type === "pong");
      peer.socket.send(JSON.stringify({ type: "ping" }));
      return pong;
    });
    await Promise.all(monsterBarrier);
    for (const peer of allPeers) {
      const framesAfterMonster = peer.frames.slice(monsterStarts.get(peer.name)!);
      assert.equal(framesAfterMonster.some((frame) => (
        frame.type === "room_chat_ack"
        || frame.type === "room_chat_message"
        || frame.type === "room_chat"
      )), false, `${peer.name} monster delivery`);
    }
  } finally {
    for (const peer of peers) peer.socket.terminate();
    await service.close();
    rmSync(tempDir, { recursive: true, force: true });
  }
});
