interface CleanupExpiredRoomsDeps {
  cleanupExpired: (now: Date, shouldRetainRoom: (roomId: string) => boolean) => string[];
  hasActiveRelayHost: (roomId: string) => boolean;
  removeRelayRoom: (roomId: string) => void;
  closeRoomSockets: (roomId: string, code: number, reason: string) => void;
  log: (message: string) => void;
}

export function cleanupExpiredRooms(deps: CleanupExpiredRoomsDeps, now = new Date()) {
  const deletedRoomIds = deps.cleanupExpired(now, deps.hasActiveRelayHost);
  for (const roomId of deletedRoomIds) {
    deps.removeRelayRoom(roomId);
    deps.closeRoomSockets(roomId, 4001, "room_expired");
    deps.log(`[lobby] room expired roomId=${roomId}`);
  }

  return deletedRoomIds;
}
