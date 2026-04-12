import { randomBytes, randomUUID, scryptSync, timingSafeEqual } from "node:crypto";

export type RoomStatus = "open" | "starting" | "full" | "closed";
export type RelayState = "disabled" | "planned" | "ready";
export type ConnectionStrategy = "direct-first" | "relay-first" | "relay-only";
export type ProtocolProfile = "legacy_4p" | "extended_8p";

const Legacy4pProtocolProfile: ProtocolProfile = "legacy_4p";
const Extended8pProtocolProfile: ProtocolProfile = "extended_8p";
const LegacyCompatibleModVersion = "0.2.2";
const RmpAdvertisedModId = "RemoveMultiplayerPlayerLimit";

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
  strategy: ConnectionStrategy;
  relayAllowed: boolean;
  controlChannelId: string;
  directCandidates: DirectEndpoint[];
  relayEndpoint?: RelayEndpoint | undefined;
}

export interface SavedRunSlotInput {
  netId: string;
  characterId?: string | undefined;
  characterName?: string | undefined;
  playerName?: string | undefined;
  isHost?: boolean | undefined;
}

export interface SavedRunSlot {
  netId: string;
  characterId: string;
  characterName: string;
  playerName: string;
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
  protocolProfile: ProtocolProfile;
  relayState: RelayState;
  createdAt: Date;
  lastHeartbeatAt: Date;
  savedRun?: SavedRunInfo | undefined;
}

export interface Room extends RoomSummary {
  passwordHash?: string | undefined;
  hostConnectionInfo: HostConnectionInfo;
  modList: string[];
}

export interface JoinTicket {
  ticketId: string;
  roomId: string;
  issuedAt: Date;
  expiresAt: Date;
  connectionPlan: ConnectionPlan;
}

export interface RoomSettings {
  chatEnabled: boolean;
}

export interface HostSession {
  roomId: string;
  controlChannelId: string;
  hostToken: string;
  relayState: RelayState;
  lastSeenAt: Date;
  kickedPlayerNetIds: Set<string>;
  roomSettings: RoomSettings;
}

export interface CreateRoomInput {
  roomName: string;
  password?: string | undefined;
  hostPlayerName: string;
  gameMode: string;
  version: string;
  modVersion: string;
  modList?: string[] | undefined;
  protocolProfile?: ProtocolProfile | string | undefined;
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
  modList?: string[] | undefined;
  desiredSavePlayerNetId?: string | undefined;
  playerNetId?: string | undefined;
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
  strictGameVersionCheck?: boolean;
  strictModVersionCheck?: boolean;
  connectionStrategy?: ConnectionStrategy;
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

export interface ModMismatchDetails {
  roomModVersion: string;
  requestedModVersion: string;
  missingModsOnLocal?: string[] | undefined;
  missingModsOnHost?: string[] | undefined;
}

export class LobbyStoreError extends Error {
  constructor(
    readonly statusCode: number,
    readonly code: string,
    message: string,
    readonly details?: unknown,
  ) {
    super(message);
  }
}

export class LobbyStore {
  private readonly rooms = new Map<string, Room>();
  private readonly tickets = new Map<string, JoinTicket>();
  private readonly hostSessions = new Map<string, HostSession>();
  private readonly config: Required<StoreConfig>;

  constructor(config: StoreConfig) {
    this.config = {
      heartbeatTimeoutMs: config.heartbeatTimeoutMs,
      ticketTtlMs: config.ticketTtlMs,
      strictGameVersionCheck: config.strictGameVersionCheck ?? true,
      strictModVersionCheck: config.strictModVersionCheck ?? true,
      connectionStrategy: config.connectionStrategy ?? "direct-first",
    };
  }

  listRooms(now = new Date()): RoomSummary[] {
    return [...this.rooms.values()]
      .filter((room) => room.status !== "closed")
      .sort((left, right) => {
        const statusDiff = getRoomStatusSortRank(left.status) - getRoomStatusSortRank(right.status);
        if (statusDiff !== 0) {
          return statusDiff;
        }

        const createdDiff = right.createdAt.getTime() - left.createdAt.getTime();
        if (createdDiff !== 0) {
          return createdDiff;
        }

        return left.roomName.localeCompare(right.roomName, "zh-Hans-CN");
      })
      .map((room) => this.toRoomSummary(room));
  }

  createRoom(input: CreateRoomInput, remoteAddress: string, now = new Date()): CreateRoomResult {
    const roomId = randomUUID();
    const controlChannelId = randomUUID();
    const hostToken = randomToken();
    const password = input.password?.trim();
    const modList = normalizeModList(input.modList ?? []);
    const protocolProfile = normalizeProtocolProfile(
      input.protocolProfile,
      input.maxPlayers,
      input.modVersion,
      modList,
    );
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
      modList,
      protocolProfile,
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
      kickedPlayerNetIds: new Set(),
      roomSettings: { chatEnabled: true },
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
    const room = this.requireRoom(roomId);
    const hostSession = this.requireHostSession(roomId);
    const requestedVersion = input.version.trim();
    const requestedModVersion = input.modVersion.trim();
    const requestedModList = normalizeModList(input.modList ?? []);
    const availableSavedRunSlots = getAvailableSavedRunSlots(room);
    const canResumeSavedRun = room.savedRun !== undefined && availableSavedRunSlots.length > 0;

    const playerNetId = input.playerNetId?.trim();
    if (playerNetId && hostSession.kickedPlayerNetIds.has(playerNetId)) {
      throw new LobbyStoreError(403, "kicked", "你已被房主移出该房间，无法重新加入。");
    }

    if (room.status === "closed") {
      throw new LobbyStoreError(410, "room_closed", "该房间已经关闭。");
    }

    if (room.status === "starting" && !canResumeSavedRun) {
      throw new LobbyStoreError(409, "room_started", "该房间已经开始游戏，当前不允许再加入。");
    }

    if (!room.savedRun && room.currentPlayers >= room.maxPlayers) {
      throw new LobbyStoreError(409, "room_full", "该房间已满。");
    }

    if (this.config.strictGameVersionCheck && room.version !== requestedVersion) {
      throw new LobbyStoreError(
        409,
        "version_mismatch",
        `游戏版本不匹配。房间版本：${room.version}；当前客户端版本：${requestedVersion}。`,
      );
    }

    if (this.config.strictModVersionCheck) {
      const modMismatch = describeModMismatch(room, requestedModVersion, requestedModList);
      if (modMismatch) {
        throw new LobbyStoreError(409, modMismatch.code, modMismatch.message, modMismatch.details);
      }
    }

    if (room.requiresPassword && !verifyPassword(input.password ?? "", room.passwordHash)) {
      throw new LobbyStoreError(401, "invalid_password", "房间密码错误。");
    }

    if (room.savedRun) {
      const connectedPlayerNetIds = new Set(room.savedRun.connectedPlayerNetIds);
      if (availableSavedRunSlots.length === 0) {
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
      } else if (availableSavedRunSlots.length > 1) {
        throw new LobbyStoreError(409, "save_slot_required", "该续局房间需要先选择一个可接管角色。");
      }
    } else if (room.currentPlayers >= room.maxPlayers) {
      throw new LobbyStoreError(409, "room_full", "该房间已满。");
    }

    const connectionPlan = buildConnectionPlan(room, hostSession, this.config.connectionStrategy);
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
    const hostSession = this.requireHostSession(roomId);
    this.assertHostToken(hostSession, hostToken);
    this.removeRoom(roomId);
  }

  cleanupExpired(now = new Date()) {
    const deletedRoomIds: string[] = [];
    for (const room of this.rooms.values()) {
      const hostSession = this.hostSessions.get(room.roomId);
      const roomExpired = now.getTime() - room.lastHeartbeatAt.getTime() > this.config.heartbeatTimeoutMs;
      const hostExpired = hostSession
        ? now.getTime() - hostSession.lastSeenAt.getTime() > this.config.heartbeatTimeoutMs
        : true;
      if (roomExpired || hostExpired) {
        this.removeRoom(room.roomId);
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

  kickPlayer(roomId: string, hostToken: string, playerNetId: string) {
    const hostSession = this.requireHostSession(roomId);
    this.assertHostToken(hostSession, hostToken);
    hostSession.kickedPlayerNetIds.add(playerNetId);
  }

  isPlayerKicked(roomId: string, playerNetId: string) {
    const hostSession = this.hostSessions.get(roomId);
    if (!hostSession) {
      return false;
    }

    return hostSession.kickedPlayerNetIds.has(playerNetId);
  }

  updateRoomSettings(roomId: string, hostToken: string, settings: Partial<RoomSettings>): RoomSettings {
    const hostSession = this.requireHostSession(roomId);
    this.assertHostToken(hostSession, hostToken);
    if (settings.chatEnabled !== undefined) {
      hostSession.roomSettings.chatEnabled = settings.chatEnabled;
    }

    return { ...hostSession.roomSettings };
  }

  getRoomSettings(roomId: string): RoomSettings {
    const hostSession = this.hostSessions.get(roomId);
    if (!hostSession) {
      return { chatEnabled: true };
    }

    return { ...hostSession.roomSettings };
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

  private removeRoom(roomId: string) {
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
    const visibleStatus = getPublicRoomStatus(room);
    const protocolProfile = resolveRoomProtocolProfile(room);
    return {
      roomId: room.roomId,
      roomName: room.roomName,
      hostPlayerName: room.hostPlayerName,
      requiresPassword: room.requiresPassword,
      status: visibleStatus,
      gameMode: room.gameMode,
      currentPlayers: room.currentPlayers,
      maxPlayers: room.maxPlayers,
      version: room.version,
      modVersion: room.modVersion,
      protocolProfile,
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

function buildConnectionPlan(room: Room, hostSession: HostSession, strategy: ConnectionStrategy): ConnectionPlan {
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

  if (strategy !== "relay-only") {
    pushCandidate("public", room.hostConnectionInfo.remoteAddress);
    room.hostConnectionInfo.localAddresses.forEach((ip, index) => {
      pushCandidate(`lan_${index + 1}`, ip);
    });
  }

  return {
    strategy,
    relayAllowed: false,
    controlChannelId: hostSession.controlChannelId,
    directCandidates,
  };
}

function describeModMismatch(
  room: Room,
  requestedModVersion: string,
  requestedModList: string[],
): { code: "mod_mismatch" | "mod_version_mismatch"; message: string; details: ModMismatchDetails } | null {
  const details: ModMismatchDetails = {
    roomModVersion: room.modVersion,
    requestedModVersion,
  };

  if (room.modList.length > 0 && requestedModList.length > 0) {
    const roomMods = new Set(room.modList);
    const requestedMods = new Set(requestedModList);
    const missingModsOnLocal = room.modList.filter((mod) => !requestedMods.has(mod));
    const missingModsOnHost = requestedModList.filter((mod) => !roomMods.has(mod));
    if (missingModsOnLocal.length > 0 || missingModsOnHost.length > 0) {
      details.missingModsOnLocal = missingModsOnLocal;
      details.missingModsOnHost = missingModsOnHost;
      return {
        code: "mod_mismatch",
        message: buildModMismatchMessage(details),
        details,
      };
    }
  }

  if (normalizeComparableVersion(room.modVersion) !== normalizeComparableVersion(requestedModVersion)) {
    return {
      code: "mod_version_mismatch",
      message: `MOD 版本不匹配。房间版本：${room.modVersion}；当前客户端版本：${requestedModVersion}。`,
      details,
    };
  }

  return null;
}

function buildModMismatchMessage(details: ModMismatchDetails) {
  const parts: string[] = ["MOD 不一致。"];
  if (details.missingModsOnLocal && details.missingModsOnLocal.length > 0) {
    parts.push(`你缺少：${details.missingModsOnLocal.join("、")}。`);
  }

  if (details.missingModsOnHost && details.missingModsOnHost.length > 0) {
    parts.push(`房主缺少：${details.missingModsOnHost.join("、")}。`);
  }

  if (normalizeComparableVersion(details.roomModVersion) !== normalizeComparableVersion(details.requestedModVersion)) {
    parts.push(`房间版本：${details.roomModVersion}；当前客户端版本：${details.requestedModVersion}。`);
  }

  return parts.join(" ");
}

function getAvailableSavedRunSlots(room: Room) {
  if (!room.savedRun) {
    return [];
  }

  const connectedPlayerNetIds = new Set(room.savedRun.connectedPlayerNetIds);
  return room.savedRun.slots.filter((slot) => !connectedPlayerNetIds.has(slot.netId));
}

function getPublicRoomStatus(room: Room): RoomStatus {
  if (room.savedRun && room.status === "starting" && getAvailableSavedRunSlots(room).length > 0) {
    return "open";
  }

  return room.status;
}

function normalizeComparableVersion(value: string) {
  const trimmed = value.trim();
  if (!trimmed) {
    return trimmed;
  }

  const comparableSource = /^\d+(?:\.\d+)*$/.test(trimmed)
    ? trimmed
    : extractEmbeddedComparableVersion(trimmed);
  if (!comparableSource) {
    return trimmed;
  }

  const segments = comparableSource.split(".");
  const normalized = segments.map((segment) => String(Number.parseInt(segment, 10)));
  let end = normalized.length - 1;
  while (end > 0 && normalized[end] === "0") {
    end -= 1;
  }

  return normalized.slice(0, end + 1).join(".");
}

function extractEmbeddedComparableVersion(value: string) {
  const match = value.match(/\d+(?:\.\d+)+/);
  return match?.[0];
}

function compareComparableVersions(left: string, right: string) {
  const leftNormalized = normalizeComparableVersion(left);
  const rightNormalized = normalizeComparableVersion(right);
  if (!/^\d+(?:\.\d+)*$/.test(leftNormalized) || !/^\d+(?:\.\d+)*$/.test(rightNormalized)) {
    return null;
  }

  const leftParts = leftNormalized.split(".").map((segment) => Number.parseInt(segment, 10));
  const rightParts = rightNormalized.split(".").map((segment) => Number.parseInt(segment, 10));
  const length = Math.max(leftParts.length, rightParts.length);
  for (let index = 0; index < length; index += 1) {
    const leftValue = leftParts[index] ?? 0;
    const rightValue = rightParts[index] ?? 0;
    if (leftValue !== rightValue) {
      return leftValue < rightValue ? -1 : 1;
    }
  }

  return 0;
}

function isLegacyCompatibleModVersion(modVersion: string) {
  const comparison = compareComparableVersions(modVersion, LegacyCompatibleModVersion);
  return comparison != null && comparison <= 0;
}

function advertisesRmpMod(modList: string[]) {
  return modList.some((value) => value.localeCompare(RmpAdvertisedModId, "en", { sensitivity: "accent" }) === 0);
}

function normalizeProtocolProfile(
  requestedProfile: string | undefined,
  maxPlayers: number,
  modVersion: string,
  modList: string[],
): ProtocolProfile {
  const normalized = requestedProfile?.trim().toLowerCase();
  if (normalized === Legacy4pProtocolProfile) {
    return Legacy4pProtocolProfile;
  }

  if (normalized === Extended8pProtocolProfile) {
    return Extended8pProtocolProfile;
  }

  if (maxPlayers === 4 && isLegacyCompatibleModVersion(modVersion) && !advertisesRmpMod(modList)) {
    return Legacy4pProtocolProfile;
  }

  return Extended8pProtocolProfile;
}

function resolveRoomProtocolProfile(room: Pick<Room, "protocolProfile" | "maxPlayers" | "modVersion" | "modList">) {
  return normalizeProtocolProfile(room.protocolProfile, room.maxPlayers, room.modVersion, room.modList);
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

function normalizeModList(mods: string[]) {
  return mods
    .map((value) => value.trim())
    .filter((value, index, source) => value !== "" && source.indexOf(value) === index)
    .sort((left, right) => left.localeCompare(right, "en"));
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
      playerName: slot.playerName?.trim() ?? "",
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

function getRoomStatusSortRank(status: RoomStatus) {
  switch (status) {
    case "open":
      return 0;
    case "starting":
      return 1;
    case "full":
      return 2;
    default:
      return 3;
  }
}

function randomToken() {
  return randomBytes(24).toString("base64url");
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}
