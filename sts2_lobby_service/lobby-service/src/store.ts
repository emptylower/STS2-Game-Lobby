import { randomBytes, randomUUID, scryptSync, timingSafeEqual } from "node:crypto";

export type RoomStatus = "open" | "starting" | "full" | "closed";
export type RelayState = "disabled" | "planned" | "ready";

export interface HostConnectionInfo {
  enetPort: number;
  localAddresses: string[];
  remoteAddress: string;
}

export interface DirectEndpoint {
  label: string;
  ip: string;
  port: number;
}

export interface RelayEndpoint {
  host: string;
  port: number;
}

export interface ConnectionPlan {
  strategy: "direct-first";
  relayAllowed: boolean;
  controlChannelId: string;
  directCandidates: DirectEndpoint[];
  relayEndpoint?: RelayEndpoint | undefined;
}

export interface SavedRunSlotInput {
  netId: string;
  characterId?: string | undefined;
  characterName?: string | undefined;
  isHost?: boolean | undefined;
}

export interface SavedRunSlot {
  netId: string;
  characterId: string;
  characterName: string;
  isHost: boolean;
  isConnected: boolean;
}

export interface SavedRunInfo {
  saveKey: string;
  slots: SavedRunSlot[];
  connectedPlayerNetIds: string[];
}

export interface RoomSummary {
  roomId: string;
  roomName: string;
  hostPlayerName: string;
  requiresPassword: boolean;
  status: RoomStatus;
  gameMode: string;
  currentPlayers: number;
  maxPlayers: number;
  version: string;
  modVersion: string;
  relayState: RelayState;
  createdAt: Date;
  lastHeartbeatAt: Date;
  savedRun?: SavedRunInfo | undefined;
}

export interface Room extends RoomSummary {
  passwordHash?: string | undefined;
  hostConnectionInfo: HostConnectionInfo;
}

export interface JoinTicket {
  ticketId: string;
  roomId: string;
  issuedAt: Date;
  expiresAt: Date;
  connectionPlan: ConnectionPlan;
}

export interface HostSession {
  roomId: string;
  controlChannelId: string;
  hostToken: string;
  relayState: RelayState;
  lastSeenAt: Date;
}

export interface CreateRoomInput {
  roomName: string;
  password?: string | undefined;
  hostPlayerName: string;
  gameMode: string;
  version: string;
  modVersion: string;
  maxPlayers: number;
  hostConnectionInfo: {
    enetPort: number;
    localAddresses?: string[];
  };
  savedRun?: {
    saveKey: string;
    slots: SavedRunSlotInput[];
    connectedPlayerNetIds?: string[];
  };
}

export interface JoinRoomInput {
  playerName: string;
  password?: string | undefined;
  version: string;
  modVersion: string;
  desiredSavePlayerNetId?: string | undefined;
}

export interface HeartbeatInput {
  hostToken: string;
  currentPlayers: number;
  status: RoomStatus | string;
  connectedPlayerNetIds?: string[] | undefined;
}

export interface StoreConfig {
  heartbeatTimeoutMs: number;
  ticketTtlMs: number;
  roomTombstoneMs?: number;
  ignoreVersionMismatch?: boolean;
  forceRelayOnly?: boolean;
}

interface RoomTombstone {
  roomId: string;
  hostToken: string;
  expiresAt: number;
}

export interface CreateRoomResult {
  roomId: string;
  controlChannelId: string;
  hostToken: string;
  heartbeatIntervalSeconds: number;
  room: RoomSummary;
  relayEndpoint?: RelayEndpoint | undefined;
}

export interface JoinRoomResult {
  ticketId: string;
  roomId: string;
  issuedAt: Date;
  expiresAt: Date;
  room: RoomSummary;
  connectionPlan: ConnectionPlan;
}

export class LobbyStoreError extends Error {
  constructor(
    readonly statusCode: number,
    readonly code: string,
    message: string,
  ) {
    super(message);
  }
}

export class LobbyStore {
  private readonly rooms = new Map<string, Room>();
  private readonly tickets = new Map<string, JoinTicket>();
  private readonly hostSessions = new Map<string, HostSession>();
  private readonly tombstones = new Map<string, RoomTombstone>();

  constructor(private readonly config: StoreConfig) {}

  listRooms(now = new Date()): RoomSummary[] {
    this.cleanupExpired(now);
    return [...this.rooms.values()]
      .filter((room) => room.status !== "closed")
      .sort((left, right) => right.lastHeartbeatAt.getTime() - left.lastHeartbeatAt.getTime())
      .map((room) => this.toRoomSummary(room));
  }

  createRoom(input: CreateRoomInput, remoteAddress: string, now = new Date()): CreateRoomResult {
    const roomId = randomUUID();
    const controlChannelId = randomUUID();
    const hostToken = randomToken();
    const password = input.password?.trim();
    const room: Room = {
      roomId,
      roomName: input.roomName.trim(),
      hostPlayerName: input.hostPlayerName.trim(),
      requiresPassword: Boolean(password),
      passwordHash: password ? hashPassword(password) : undefined,
      status: "open",
      gameMode: input.gameMode.trim(),
      currentPlayers: 1,
      maxPlayers: input.maxPlayers,
      version: input.version.trim(),
      modVersion: input.modVersion.trim(),
      relayState: "disabled",
      createdAt: now,
      lastHeartbeatAt: now,
      hostConnectionInfo: {
        enetPort: input.hostConnectionInfo.enetPort,
        localAddresses: normalizeAddressList(input.hostConnectionInfo.localAddresses ?? []),
        remoteAddress: normalizeRemoteAddress(remoteAddress),
      },
      savedRun: normalizeSavedRunInput(input.savedRun),
    };

    const hostSession: HostSession = {
      roomId,
      controlChannelId,
      hostToken,
      relayState: "disabled",
      lastSeenAt: now,
    };

    this.rooms.set(roomId, room);
    this.hostSessions.set(roomId, hostSession);

    return {
      roomId,
      controlChannelId,
      hostToken,
      heartbeatIntervalSeconds: Math.max(3, Math.floor(this.config.heartbeatTimeoutMs / 3000)),
      room: this.toRoomSummary(room),
    };
  }

  joinRoom(roomId: string, input: JoinRoomInput, now = new Date()): JoinRoomResult {
    this.cleanupExpired(now);
    const room = this.requireRoom(roomId);
    const hostSession = this.requireHostSession(roomId);

    if (room.status === "closed") {
      throw new LobbyStoreError(410, "room_closed", "该房间已经关闭。");
    }

    if (room.currentPlayers >= room.maxPlayers) {
      throw new LobbyStoreError(409, "room_full", "该房间已满。");
    }

    if (!this.config.ignoreVersionMismatch && room.version !== input.version.trim()) {
      throw new LobbyStoreError(409, "version_mismatch", "游戏版本不匹配。");
    }

    if (room.modVersion !== input.modVersion.trim()) {
      throw new LobbyStoreError(409, "mod_version_mismatch", "MOD 版本不匹配。");
    }

    if (room.requiresPassword && !verifyPassword(input.password ?? "", room.passwordHash)) {
      throw new LobbyStoreError(401, "invalid_password", "房间密码错误。");
    }

    if (room.savedRun) {
      const connectedPlayerNetIds = new Set(room.savedRun.connectedPlayerNetIds);
      const availableSlots = room.savedRun.slots.filter((slot) => !connectedPlayerNetIds.has(slot.netId));
      if (availableSlots.length === 0) {
        throw new LobbyStoreError(409, "save_slot_unavailable", "该续局房间当前没有可接管角色。");
      }

      const desiredSavePlayerNetId = input.desiredSavePlayerNetId?.trim();
      if (desiredSavePlayerNetId) {
        const selectedSlot = room.savedRun.slots.find((slot) => slot.netId === desiredSavePlayerNetId);
        if (!selectedSlot) {
          throw new LobbyStoreError(409, "save_slot_invalid", "所选续局角色不存在。");
        }

        if (connectedPlayerNetIds.has(desiredSavePlayerNetId)) {
          throw new LobbyStoreError(409, "save_slot_unavailable", "所选续局角色已被其他玩家接管。");
        }
      } else if (availableSlots.length > 1) {
        throw new LobbyStoreError(409, "save_slot_required", "该续局房间需要先选择一个可接管角色。");
      }
    }

    const connectionPlan = buildConnectionPlan(room, hostSession, this.config.forceRelayOnly ?? false);
    const ticket: JoinTicket = {
      ticketId: randomUUID(),
      roomId,
      issuedAt: now,
      expiresAt: new Date(now.getTime() + this.config.ticketTtlMs),
      connectionPlan,
    };
    this.tickets.set(ticket.ticketId, ticket);

    return {
      ticketId: ticket.ticketId,
      roomId,
      issuedAt: ticket.issuedAt,
      expiresAt: ticket.expiresAt,
      room: this.toRoomSummary(room),
      connectionPlan,
    };
  }

  heartbeat(roomId: string, input: HeartbeatInput, now = new Date()) {
    this.cleanupExpired(now);
    const tombstone = this.tombstones.get(roomId);
    if (tombstone && tombstone.hostToken === input.hostToken) {
      return null;
    }

    const room = this.requireRoom(roomId);
    const hostSession = this.requireHostSession(roomId);
    this.assertHostToken(hostSession, input.hostToken);

    room.currentPlayers = clamp(Math.max(1, input.currentPlayers), 1, room.maxPlayers);
    room.status = normalizeStatus(input.status, room.currentPlayers, room.maxPlayers);
    room.lastHeartbeatAt = now;
    hostSession.lastSeenAt = now;
    if (room.savedRun && input.connectedPlayerNetIds) {
      room.savedRun.connectedPlayerNetIds = normalizeNetIdList(input.connectedPlayerNetIds);
      room.savedRun.slots = room.savedRun.slots.map((slot) => ({
        ...slot,
        isConnected: room.savedRun?.connectedPlayerNetIds.includes(slot.netId) ?? false,
      }));
    }
    return this.toRoomSummary(room);
  }

  deleteRoom(roomId: string, hostToken: string) {
    const tombstone = this.tombstones.get(roomId);
    if (tombstone) {
      if (tombstone.hostToken !== hostToken) {
        throw new LobbyStoreError(401, "invalid_host_token", "房主令牌无效。");
      }

      return;
    }

    const hostSession = this.requireHostSession(roomId);
    this.assertHostToken(hostSession, hostToken);
    this.removeRoom(roomId, hostToken, new Date());
  }

  cleanupExpired(now = new Date()) {
    for (const [roomId, tombstone] of this.tombstones.entries()) {
      if (tombstone.expiresAt <= now.getTime()) {
        this.tombstones.delete(roomId);
      }
    }

    const deletedRoomIds: string[] = [];
    for (const room of this.rooms.values()) {
      const hostSession = this.hostSessions.get(room.roomId);
      const roomExpired = now.getTime() - room.lastHeartbeatAt.getTime() > this.config.heartbeatTimeoutMs;
      const hostExpired = hostSession
        ? now.getTime() - hostSession.lastSeenAt.getTime() > this.config.heartbeatTimeoutMs
        : true;
      if (roomExpired || hostExpired) {
        this.removeRoom(room.roomId, hostSession?.hostToken, now);
        deletedRoomIds.push(room.roomId);
      }
    }

    for (const ticket of this.tickets.values()) {
      if (ticket.expiresAt.getTime() <= now.getTime()) {
        this.tickets.delete(ticket.ticketId);
      }
    }

    return deletedRoomIds;
  }

  touchHostSession(roomId: string, now = new Date()) {
    const hostSession = this.requireHostSession(roomId);
    hostSession.lastSeenAt = now;
  }

  setRelayState(roomId: string, relayState: RelayState) {
    const hostSession = this.hostSessions.get(roomId);
    if (!hostSession) {
      return;
    }

    hostSession.relayState = relayState;
  }

  validateHostControl(roomId: string, controlChannelId: string, token: string) {
    const hostSession = this.requireHostSession(roomId);
    if (hostSession.controlChannelId !== controlChannelId) {
      throw new LobbyStoreError(401, "invalid_control_channel", "控制通道无效。");
    }

    this.assertHostToken(hostSession, token);
    return hostSession;
  }

  validateClientControl(roomId: string, controlChannelId: string, ticketId: string) {
    const hostSession = this.requireHostSession(roomId);
    if (hostSession.controlChannelId !== controlChannelId) {
      throw new LobbyStoreError(401, "invalid_control_channel", "控制通道无效。");
    }

    const ticket = this.tickets.get(ticketId);
    if (!ticket || ticket.roomId !== roomId) {
      throw new LobbyStoreError(401, "invalid_ticket", "加入票据无效。");
    }

    if (ticket.expiresAt.getTime() <= Date.now()) {
      this.tickets.delete(ticket.ticketId);
      throw new LobbyStoreError(401, "expired_ticket", "加入票据已过期。");
    }

    return ticket;
  }

  hasTicketForRoom(roomId: string, ticketId: string) {
    const ticket = this.tickets.get(ticketId);
    if (!ticket) {
      return false;
    }

    if (ticket.roomId !== roomId) {
      return false;
    }

    if (ticket.expiresAt.getTime() <= Date.now()) {
      this.tickets.delete(ticket.ticketId);
      return false;
    }

    return true;
  }

  private requireRoom(roomId: string) {
    const room = this.rooms.get(roomId);
    if (!room) {
      throw new LobbyStoreError(404, "room_not_found", "房间不存在或已过期。");
    }

    return room;
  }

  private requireHostSession(roomId: string) {
    const hostSession = this.hostSessions.get(roomId);
    if (!hostSession) {
      throw new LobbyStoreError(404, "host_session_not_found", "房主会话不存在。");
    }

    return hostSession;
  }

  private assertHostToken(hostSession: HostSession, hostToken: string) {
    if (hostSession.hostToken !== hostToken) {
      throw new LobbyStoreError(401, "invalid_host_token", "房主令牌无效。");
    }
  }

  private removeRoom(roomId: string, hostToken?: string, now = new Date()) {
    if (hostToken && this.config.roomTombstoneMs && this.config.roomTombstoneMs > 0) {
      this.tombstones.set(roomId, {
        roomId,
        hostToken,
        expiresAt: now.getTime() + this.config.roomTombstoneMs,
      });
    }

    this.rooms.delete(roomId);
    this.hostSessions.delete(roomId);
    for (const ticket of this.tickets.values()) {
      if (ticket.roomId === roomId) {
        this.tickets.delete(ticket.ticketId);
      }
    }
  }

  private toRoomSummary(room: Room): RoomSummary {
    const relayState = this.hostSessions.get(room.roomId)?.relayState ?? "disabled";
    return {
      roomId: room.roomId,
      roomName: room.roomName,
      hostPlayerName: room.hostPlayerName,
      requiresPassword: room.requiresPassword,
      status: room.status,
      gameMode: room.gameMode,
      currentPlayers: room.currentPlayers,
      maxPlayers: room.maxPlayers,
      version: room.version,
      modVersion: room.modVersion,
      relayState,
      createdAt: room.createdAt,
      lastHeartbeatAt: room.lastHeartbeatAt,
      savedRun: room.savedRun
        ? {
            saveKey: room.savedRun.saveKey,
            connectedPlayerNetIds: [...room.savedRun.connectedPlayerNetIds],
            slots: room.savedRun.slots.map((slot) => ({ ...slot })),
          }
        : undefined,
    };
  }
}

function buildConnectionPlan(room: Room, hostSession: HostSession, forceRelayOnly = false): ConnectionPlan {
  const directCandidates: DirectEndpoint[] = [];
  const seen = new Set<string>();
  const pushCandidate = (label: string, ip: string) => {
    const normalized = normalizeRemoteAddress(ip);
    if (!normalized || seen.has(normalized)) {
      return;
    }

    seen.add(normalized);
    directCandidates.push({
      label,
      ip: normalized,
      port: room.hostConnectionInfo.enetPort,
    });
  };

  pushCandidate("public", room.hostConnectionInfo.remoteAddress);
  room.hostConnectionInfo.localAddresses.forEach((ip, index) => {
    pushCandidate(`lan_${index + 1}`, ip);
  });

  return {
    strategy: "direct-first",
    relayAllowed: false,
    controlChannelId: hostSession.controlChannelId,
    directCandidates: forceRelayOnly ? [] : directCandidates,
  };
}

function hashPassword(password: string) {
  const salt = randomBytes(16).toString("hex");
  const hash = scryptSync(password, salt, 64).toString("hex");
  return `${salt}:${hash}`;
}

function verifyPassword(password: string, storedHash?: string) {
  if (!storedHash) {
    return false;
  }

  const [salt, expectedHash] = storedHash.split(":");
  if (!salt || !expectedHash) {
    return false;
  }

  const actualHash = scryptSync(password, salt, 64);
  const expected = Buffer.from(expectedHash, "hex");
  return expected.length === actualHash.length && timingSafeEqual(expected, actualHash);
}

function normalizeStatus(status: string, currentPlayers: number, maxPlayers: number): RoomStatus {
  if (currentPlayers >= maxPlayers) {
    return "full";
  }

  const normalized = status.trim().toLowerCase();
  if (normalized === "starting" || normalized === "closed" || normalized === "open") {
    return normalized;
  }

  return "open";
}

function normalizeRemoteAddress(address: string) {
  const trimmed = address.trim();
  if (!trimmed) {
    return "";
  }

  if (trimmed.startsWith("::ffff:")) {
    return trimmed.slice("::ffff:".length);
  }

  return trimmed === "::1" ? "127.0.0.1" : trimmed;
}

function normalizeAddressList(addresses: string[]) {
  const seen = new Set<string>();
  const output: string[] = [];
  for (const address of addresses) {
    const normalized = normalizeRemoteAddress(address);
    if (!normalized || seen.has(normalized)) {
      continue;
    }

    seen.add(normalized);
    output.push(normalized);
  }

  return output;
}

function normalizeSavedRunInput(savedRun: CreateRoomInput["savedRun"]): SavedRunInfo | undefined {
  if (!savedRun) {
    return undefined;
  }

  const saveKey = savedRun.saveKey.trim();
  const slots = savedRun.slots
    .map((slot) => ({
      netId: normalizeNetId(slot.netId),
      characterId: slot.characterId?.trim() ?? "",
      characterName: slot.characterName?.trim() ?? "",
      isHost: Boolean(slot.isHost),
      isConnected: false,
    }))
    .filter((slot, index, source) => source.findIndex((candidate) => candidate.netId === slot.netId) === index);

  if (!saveKey || slots.length === 0) {
    return undefined;
  }

  const normalizedConnectedPlayerNetIds = normalizeNetIdList(savedRun.connectedPlayerNetIds ?? []);
  const connectedPlayerNetIds =
    normalizedConnectedPlayerNetIds.length > 0
      ? normalizedConnectedPlayerNetIds
      : slots.filter((slot) => slot.isHost).map((slot) => slot.netId);

  for (const slot of slots) {
    slot.isConnected = connectedPlayerNetIds.includes(slot.netId);
  }

  return {
    saveKey,
    slots,
    connectedPlayerNetIds,
  };
}

function normalizeNetIdList(values: string[]) {
  return values
    .map((value) => normalizeNetId(value))
    .filter((value, index, source) => source.indexOf(value) === index);
}

function normalizeNetId(value: string) {
  const normalized = value.trim();
  if (!/^\d+$/.test(normalized)) {
    throw new LobbyStoreError(400, "invalid_save_net_id", "续局角色 NetId 非法。");
  }

  return normalized;
}

function randomToken() {
  return randomBytes(24).toString("base64url");
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}
