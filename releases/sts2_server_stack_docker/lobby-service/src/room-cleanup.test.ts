import test from "node:test";
import assert from "node:assert/strict";
import { cleanupExpiredRooms } from "./room-cleanup.js";

test("cleanupExpiredRooms removes relay sessions and closes sockets for every expired room", () => {
  const removedRelayRooms: string[] = [];
  const closedSockets: Array<{ roomId: string; code: number; reason: string }> = [];
  const logLines: string[] = [];

  const deletedRoomIds = cleanupExpiredRooms({
    cleanupExpired: () => ["room-a", "room-b"],
    removeRelayRoom: (roomId) => {
      removedRelayRooms.push(roomId);
    },
    closeRoomSockets: (roomId, code, reason) => {
      closedSockets.push({ roomId, code, reason });
    },
    log: (message) => {
      logLines.push(message);
    },
  });

  assert.deepEqual(deletedRoomIds, ["room-a", "room-b"]);
  assert.deepEqual(removedRelayRooms, ["room-a", "room-b"]);
  assert.deepEqual(closedSockets, [
    { roomId: "room-a", code: 4001, reason: "room_expired" },
    { roomId: "room-b", code: 4001, reason: "room_expired" },
  ]);
  assert.deepEqual(logLines, [
    "[lobby] room expired roomId=room-a",
    "[lobby] room expired roomId=room-b",
  ]);
});
