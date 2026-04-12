import assert from "node:assert/strict";
import dgram from "node:dgram";
import test from "node:test";
import { setTimeout as sleep } from "node:timers/promises";
import { RoomRelayManager } from "./relay.js";

const MAGIC = Buffer.from("STS2R1", "ascii");

test("relay traffic snapshot reports active sessions and bytes in window", async () => {
  const manager = new RoomRelayManager(
    {
      bindHost: "127.0.0.1",
      portStart: 43100,
      portEnd: 43100,
      hostIdleMs: 5_000,
      clientIdleMs: 5_000,
    },
    () => {},
  );

  const endpoint = manager.allocateRoom("room-test", "token-test", "127.0.0.1");
  assert.ok(endpoint);

  const hostSocket = dgram.createSocket("udp4");
  const clientSocket = dgram.createSocket("udp4");

  try {
    await bindSocket(hostSocket);
    await bindSocket(clientSocket);

    await sendBuffer(hostSocket, buildHostRegister("token-test"), endpoint!.port, endpoint!.host);
    await sleep(80);

    let snapshot = manager.getTrafficSnapshot();
    assert.equal(snapshot.activeRooms, 1);
    assert.equal(snapshot.activeHosts, 1);
    assert.equal(snapshot.activeClients, 0);
    assert.ok(snapshot.totalBytesInWindow > 0);

    const payload = Buffer.alloc(4_096, 0x42);
    for (let index = 0; index < 40; index += 1) {
      await sendBuffer(clientSocket, payload, endpoint!.port, endpoint!.host);
    }

    await sleep(120);
    snapshot = manager.getTrafficSnapshot();
    assert.equal(snapshot.activeRooms, 1);
    assert.equal(snapshot.activeHosts, 1);
    assert.equal(snapshot.activeClients, 1);
    assert.ok(snapshot.totalBytesInWindow > payload.byteLength);
    assert.ok(snapshot.currentBandwidthMbps > 0);
  } finally {
    hostSocket.close();
    clientSocket.close();
    manager.close();
  }
});

function buildHostRegister(token: string) {
  const tokenBuffer = Buffer.from(token, "utf8");
  const message = Buffer.alloc(MAGIC.length + 3 + tokenBuffer.length);
  MAGIC.copy(message, 0);
  message.writeUInt8(1, MAGIC.length);
  message.writeUInt16BE(tokenBuffer.length, MAGIC.length + 1);
  tokenBuffer.copy(message, MAGIC.length + 3);
  return message;
}

function bindSocket(socket: dgram.Socket) {
  return new Promise<void>((resolve, reject) => {
    const onError = (error: Error) => {
      socket.off("listening", onListening);
      reject(error);
    };
    const onListening = () => {
      socket.off("error", onError);
      resolve();
    };
    socket.once("error", onError);
    socket.once("listening", onListening);
    socket.bind(0, "127.0.0.1");
  });
}

function sendBuffer(socket: dgram.Socket, payload: Buffer, port: number, host: string) {
  return new Promise<void>((resolve, reject) => {
    socket.send(payload, port, host, (error) => {
      if (error) {
        reject(error);
        return;
      }

      resolve();
    });
  });
}
