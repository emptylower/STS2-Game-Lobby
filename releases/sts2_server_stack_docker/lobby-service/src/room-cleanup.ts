interface CleanupExpiredRoomsDeps {
  cleanupExpired: (now?: Date) => string[];
  removeRelayRoom: (roomId: string) => void;
  closeRoomSockets: (roomId: string, code: number, reason: string) => void;
  log: (message: string) => void;
}

export function cleanupExpiredRooms(deps: CleanupExpiredRoomsDeps, now = new Date()) {
  const deletedRoomIds = deps.cleanupExpired(now);
  for (const roomId of deletedRoomIds) {
    deps.removeRelayRoom(roomId);
    deps.closeRoomSockets(roomId, 4001, "room_expired");
    deps.log(`[lobby] room expired roomId=${roomId}`);
  }

  return deletedRoomIds;
}
