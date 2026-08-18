import test from "node:test";
import assert from "node:assert/strict";
import { cleanupExpiredRooms } from "./room-cleanup.js";

test("cleanupExpiredRooms retains active relay rooms and tears down only idle expired rooms", () => {
  const removedRelayRooms: string[] = [];
  const closedSockets: Array<{ roomId: string; code: number; reason: string }> = [];
  const logLines: string[] = [];

  const deletedRoomIds = cleanupExpiredRooms({
    cleanupExpired: (_now, shouldRetainRoom) => ["room-active", "room-idle"]
      .filter((roomId) => !shouldRetainRoom(roomId)),
    hasActiveRelayHost: (roomId) => roomId === "room-active",
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

  assert.deepEqual(deletedRoomIds, ["room-idle"]);
  assert.deepEqual(removedRelayRooms, ["room-idle"]);
  assert.deepEqual(closedSockets, [
    { roomId: "room-idle", code: 4001, reason: "room_expired" },
  ]);
  assert.deepEqual(logLines, [
    "[lobby] room expired roomId=room-idle",
  ]);
});
