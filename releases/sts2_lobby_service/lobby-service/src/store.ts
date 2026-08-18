import { createHash, randomBytes, randomUUID, scryptSync, timingSafeEqual } from "node:crypto";
import { resolveModDiff } from "./mod-sync/diff.js";
import type { LobbyModDescriptor, ModDiffResult } from "./mod-sync/protocol.js";
import { canonicalModInventoryJson, validateModInventory } from "./mod-sync/validator.js";
import {
  classifyClientVersion,
  normalizeClientVersion,
  type ClientApiGeneration,
} from "./client-version.js";
import {
  assertJoinerCompatible,
  selectRoomProtocol,
  type ProtocolOffer,
  type RoomProtocolSelection,
} from "./protocol-capabilities.js";
import { ProtocolContractError } from "./protocol-errors.js";
import {
  parseRequestedProfile,
  projectProfile,
  type LegacyProtocolProfile,
  type ProtocolProfile,
} from "./protocol-profile.js";

export type RoomStatus = "open" | "starting" | "full" | "closed";
export type RelayState = "disabled" | "planned" | "ready";
export type ConnectionStrategy = "direct-first" | "relay-first" | "relay-only";
export const MaxSupportedRoomPlayers = 8;

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
  protocolProfile: LegacyProtocolProfile;
  protocolProfileV2: ProtocolProfile;
  protocolSelection: RoomProtocolSelection;
  relayState: RelayState;
  createdAt: Date;
  lastHeartbeatAt: Date;
  savedRun?: SavedRunInfo | undefined;
}

export interface Room extends Omit<RoomSummary, "protocolProfile"> {
  clientVersion: string;
  clientApiGeneration: ClientApiGeneration;
  passwordHash?: string | undefined;
  hostConnectionInfo: HostConnectionInfo;
  modList: string[];
  wireCacheSignatureV1?: string | undefined;
  hostModInventory?: LobbyModDescriptor[] | undefined;
  hostModInventoryHash?: string | undefined;
}

export interface JoinTicket {
  ticketId: string;
  roomId: string;
  playerName: string;
  playerNetId?: string | undefined;
  clientInstallationId?: string | undefined;
  identityKind: "installation" | "legacy";
  controlBindingId?: string | undefined;
  issuedAt: Date;
  expiresAt: Date;
  connectionPlan: ConnectionPlan;
  clientVersion: string;
  clientApiGeneration: ClientApiGeneration;
  protocolProfileV2: ProtocolProfile;
  protocolSelection: RoomProtocolSelection;
  joinerCapabilityDigest: string;
  protocolFlowNonce: string;
}

export interface ClientControlBinding {
  bindingId: string;
  boundSequence: number;
  roomId: string;
  playerName: string;
  playerNetId?: string | undefined;
  clientInstallationId?: string | undefined;
  identityKind: "installation" | "legacy";
  boundAt: Date;
  protocolFlowNonce: string;
}

export type KickPlayerResult =
  | {
    outcome: "kicked";
    binding: ClientControlBinding;
    persistentBanCreated: boolean;
    legacyRequest: boolean;
  }
  | {
    outcome: "stale_binding";
    requestedBindingId: string;
    currentBindingId: string;
  }
  | {
    outcome: "legacy_rebound";
  }
  | {
    outcome: "not_found";
  };

export interface ClientControlBindingHandle {
  bindingId: string;
  playerNetId: string;
  playerName: string;
  protocolFlowNonce: string;
}

export interface RoomSettings {
  chatEnabled: boolean;
}

export interface RoomChatContext {
  readonly roomId: string;
  readonly roomSessionId: string;
  readonly chatEnabled: boolean;
  readonly peerPlayerNetIds: ReadonlySet<string>;
}

export interface HostSession {
  roomId: string;
  roomSessionId: string;
  controlChannelId: string;
  hostToken: string;
  hostPlayerName: string;
  clientInstallationId?: string | undefined;
  relayState: RelayState;
  lastSeenAt: Date;
  reportedPlayerNetIds?: Set<string> | undefined;
  reportedBindingSequence?: number | undefined;
  // This client-owned ID can be reset to evade a kick. Unevadable bans require a
  // server-issued credential, which is deliberately outside this room-lifetime fix.
  kickedClientInstallationIds: Set<string>;
  legacyIdentityMode: boolean;
  legacyIdentityModeLogged: boolean;
  roomSettings: RoomSettings;
}

export interface CreateRoomInput {
  roomName: string;
  password?: string | undefined;
  hostPlayerName: string;
  clientInstallationId?: string | undefined;
  gameMode: string;
  version: string;
  modVersion: string;
  clientVersion?: string | undefined;
  modList?: string[] | undefined;
  wireCacheSignatureV1?: string | undefined;
  hostModInventory?: LobbyModDescriptor[] | undefined;
  protocolProfile?: LegacyProtocolProfile | string | undefined;
  protocolProfileV2?: ProtocolProfile | string | undefined;
  protocolOffer?: ProtocolOffer | undefined;
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
  clientInstallationId?: string | undefined;
  password?: string | undefined;
  version: string;
  modVersion: string;
  clientVersion?: string | undefined;
  protocolOffer?: ProtocolOffer | undefined;
  modList?: string[] | undefined;
  wireCacheSignatureV1?: string | undefined;
  desiredSavePlayerNetId?: string | undefined;
  playerNetId?: string | undefined;
}

export interface ModPreflightInput {
  playerName: string;
  password?: string | undefined;
  gameVersion: string;
  localMods: unknown;
}

export interface ModPreflightResult extends ModDiffResult {
  hostInventoryAvailable: boolean;
  inventoryHash?: string | undefined;
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
  modSyncMaxDescriptors?: number;
  modSyncMaxPayloadBytes?: number;
  maxKickedClientInstallationIds?: number;
  maxClientControlBindingsPerRoom?: number;
}

export interface CreateRoomResult {
  roomId: string;
  roomSessionId: string;
  controlChannelId: string;
  hostToken: string;
  heartbeatIntervalSeconds: number;
  room: RoomSummary;
  protocolSelection: RoomProtocolSelection;
  relayEndpoint?: RelayEndpoint | undefined;
}

export interface JoinRoomResult {
  ticketId: string;
  roomId: string;
  roomSessionId: string;
  issuedAt: Date;
  expiresAt: Date;
  room: RoomSummary;
  connectionPlan: ConnectionPlan;
  protocolFlowNonce: string;
}

interface JoinCompatibilityContext {
  room: Room;
  hostSession: HostSession;
  availableSavedRunSlots: SavedRunSlot[];
}

export interface LobbyStoreDependencies {
  id?(): string;
  peerPlayerNetIds?(roomId: string, roomSessionId: string): ReadonlySet<string>;
  warn?(message: string): void;
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
  private readonly clientControlBindings = new Map<string, Map<string, ClientControlBinding>>();
  private readonly reboundClientControlSlots = new Map<string, Set<string>>();
  private readonly config: Required<StoreConfig>;
  private readonly id: () => string;
  private readonly peerPlayerNetIds: (roomId: string, roomSessionId: string) => ReadonlySet<string>;
  private readonly warn: (message: string) => void;
  private nextClientControlBindingSequence = 0;

  constructor(config: StoreConfig, deps: LobbyStoreDependencies = {}) {
    this.config = {
      heartbeatTimeoutMs: config.heartbeatTimeoutMs,
      ticketTtlMs: config.ticketTtlMs,
      strictGameVersionCheck: config.strictGameVersionCheck ?? true,
      strictModVersionCheck: config.strictModVersionCheck ?? true,
      connectionStrategy: config.connectionStrategy ?? "direct-first",
      modSyncMaxDescriptors: config.modSyncMaxDescriptors ?? 64,
      modSyncMaxPayloadBytes: config.modSyncMaxPayloadBytes ?? 65_536,
      maxKickedClientInstallationIds: config.maxKickedClientInstallationIds ?? 256,
      maxClientControlBindingsPerRoom:
        config.maxClientControlBindingsPerRoom ?? MaxSupportedRoomPlayers,
    };
    this.id = deps.id ?? (() => randomUUID());
    this.peerPlayerNetIds = deps.peerPlayerNetIds ?? (() => new Set<string>());
    const warn = deps.warn ?? ((message: string) => console.warn(message));
    // A diagnostic must never turn an allowed join into a 500. The signature-missing
    // rows are fail-open by design, so a throwing sink must not undo that.
    this.warn = (message) => {
      try {
        warn(message);
      } catch {
        // Intentionally ignored.
      }
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
    const password = input.password?.trim();
    const modList = normalizeModList(input.modList ?? []);
    const hostModInventory = input.hostModInventory === undefined
      ? undefined
      : validateModInventory(input.hostModInventory, {
          maxDescriptors: this.config.modSyncMaxDescriptors,
          maxPayloadBytes: this.config.modSyncMaxPayloadBytes,
        });
    const hostModInventoryHash = hostModInventory === undefined
      ? undefined
      : createHash("sha256").update(canonicalModInventoryJson(hostModInventory)).digest("hex");
    const { clientVersion, generation, profile, selection } = this.resolveCreateProtocol(input);
    const roomId = this.id();
    const controlChannelId = this.id();
    const roomSessionId = this.id();
    const hostToken = randomToken();
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
      clientVersion,
      clientApiGeneration: generation,
      modList,
      wireCacheSignatureV1: normalizeWireCacheSignatureV1(input.wireCacheSignatureV1),
      hostModInventory,
      hostModInventoryHash,
      protocolProfileV2: profile,
      protocolSelection: selection,
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

    const clientInstallationId = this.resolveClientInstallationIdentity(
      room,
      undefined,
      input.clientInstallationId,
    );
    const hostSession: HostSession = {
      roomId,
      roomSessionId,
      controlChannelId,
      hostToken,
      hostPlayerName: input.hostPlayerName.trim(),
      clientInstallationId,
      relayState: "disabled",
      lastSeenAt: now,
      ...(room.savedRun === undefined
        ? {}
        : {
            reportedPlayerNetIds: new Set(room.savedRun.connectedPlayerNetIds),
            reportedBindingSequence: this.nextClientControlBindingSequence,
          }),
      kickedClientInstallationIds: new Set(),
      legacyIdentityMode: clientInstallationId === undefined,
      legacyIdentityModeLogged: false,
      roomSettings: { chatEnabled: true },
    };

    this.rooms.set(roomId, room);
    this.hostSessions.set(roomId, hostSession);

    return {
      roomId,
      roomSessionId,
      controlChannelId,
      hostToken,
      heartbeatIntervalSeconds: Math.max(3, Math.floor(this.config.heartbeatTimeoutMs / 3000)),
      room: this.toRoomSummary(room),
      protocolSelection: room.protocolSelection,
    };
  }

  findWireCacheMismatchForJoin(roomId: string, input: JoinRoomInput): LobbyStoreError | undefined {
    try {
      this.validateJoinCompatibility(roomId, input, false);
    } catch (error) {
      if (error instanceof LobbyStoreError && error.code === "wire_cache_signature_mismatch") {
        return error;
      }
      if (!(error instanceof LobbyStoreError)) {
        throw error;
      }
    }

    return undefined;
  }

  joinRoom(roomId: string, input: JoinRoomInput, now = new Date()): JoinRoomResult {
    const { room, hostSession, availableSavedRunSlots } =
      this.validateJoinCompatibility(roomId, input, true);

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
    const playerNetId = normalizeOptionalIdentity(input.playerNetId);
    const clientInstallationId = this.resolveClientInstallationIdentity(
      room,
      playerNetId,
      input.clientInstallationId,
    );
    const identityKind = clientInstallationId === undefined ? "legacy" : "installation";
    if (identityKind === "legacy") {
      hostSession.legacyIdentityMode = true;
      this.logLegacyIdentityMode(
        hostSession,
        input.clientInstallationId === undefined
          ? "join request omitted clientInstallationId"
          : "join request declared a public slot id as clientInstallationId",
      );
    }
    const clientVersion = normalizeClientVersion(input.clientVersion, input.modVersion);
    const clientApiGeneration = classifyClientVersion(input.clientVersion, input.modVersion);
    const protocolFlowNonce = randomBytes(16).toString("hex");
    const ticket: JoinTicket = {
      ticketId: this.id(),
      roomId,
      playerName: input.playerName.trim(),
      playerNetId,
      clientInstallationId,
      identityKind,
      issuedAt: now,
      expiresAt: new Date(now.getTime() + this.config.ticketTtlMs),
      connectionPlan,
      clientVersion,
      clientApiGeneration,
      protocolProfileV2: room.protocolProfileV2,
      protocolSelection: room.protocolSelection,
      joinerCapabilityDigest: room.protocolSelection.capabilityDigest,
      protocolFlowNonce,
    };
    this.tickets.set(ticket.ticketId, ticket);

    return {
      ticketId: ticket.ticketId,
      roomId,
      roomSessionId: hostSession.roomSessionId,
      issuedAt: ticket.issuedAt,
      expiresAt: ticket.expiresAt,
      room: this.toRoomSummary(room),
      connectionPlan,
      protocolFlowNonce,
    };
  }

  private validateJoinCompatibility(
    roomId: string,
    input: JoinRoomInput,
    warnWhenSignatureMissing: boolean,
  ): JoinCompatibilityContext {
    const room = this.requireRoom(roomId);
    const hostSession = this.requireHostSession(roomId);
    const requestedVersion = input.version.trim();
    const requestedModVersion = input.modVersion.trim();
    const requestedModList = normalizeModList(input.modList ?? []);
    const requestedWireCacheSignatureV1 = normalizeWireCacheSignatureV1(input.wireCacheSignatureV1);
    const availableSavedRunSlots = getAvailableSavedRunSlots(room);
    const canResumeSavedRun = room.savedRun !== undefined && availableSavedRunSlots.length > 0;

    const joinerGeneration = this.runProtocolValidation(() =>
      classifyClientVersion(input.clientVersion, input.modVersion));
    const joinerVersion = this.runProtocolValidation(() =>
      normalizeClientVersion(input.clientVersion, input.modVersion));
    const joinerProfile = this.runProtocolValidation(() => parseRequestedProfile({
      generation: joinerGeneration,
      legacyProfile: joinerGeneration === "compat_0_3_0_5" ? "extended_8p" : undefined,
      canonicalProfile: joinerGeneration === "canonical_0_6_plus" ? room.protocolProfileV2 : undefined,
    }));
    if (joinerProfile !== room.protocolProfileV2) {
      throw new LobbyStoreError(409, "protocol_profile_unsupported", "加入者不支持房间冻结的多人协议。");
    }
    const joinerOffer = this.resolveOffer(joinerGeneration, joinerVersion, input.protocolOffer);
    this.runProtocolValidation(() => assertJoinerCompatible(room.protocolSelection, joinerOffer));
    if (joinerVersion.length === 0) {
      throw new LobbyStoreError(426, "client_update_required", "客户端版本无效。");
    }

    const clientInstallationId = this.resolveClientInstallationIdentity(
      room,
      input.playerNetId,
      input.clientInstallationId,
    );
    if (clientInstallationId && hostSession.kickedClientInstallationIds.has(clientInstallationId)) {
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

    if (room.wireCacheSignatureV1 && requestedWireCacheSignatureV1) {
      if (room.wireCacheSignatureV1 !== requestedWireCacheSignatureV1) {
        throw new LobbyStoreError(
          409,
          "wire_cache_signature_mismatch",
          `网络编码签名不匹配。房主签名：${room.wireCacheSignatureV1}；加入者签名：${requestedWireCacheSignatureV1}。`,
          {
            roomWireCacheSignatureV1: room.wireCacheSignatureV1,
            requestedWireCacheSignatureV1,
          },
        );
      }
    } else if (warnWhenSignatureMissing && (room.wireCacheSignatureV1 || requestedWireCacheSignatureV1)) {
      this.warn(
        `[lobby] wire cache signature unavailable; allowing join roomId=${roomId} `
        + `host=${room.wireCacheSignatureV1 ?? "<absent>"} `
        + `joiner=${requestedWireCacheSignatureV1 ?? "<absent>"}`,
      );
    }

    return {
      room,
      hostSession,
      availableSavedRunSlots,
    };
  }

  modPreflight(roomId: string, input: ModPreflightInput): ModPreflightResult {
    const room = this.requireRoom(roomId);
    this.requireHostSession(roomId);
    const availableSavedRunSlots = getAvailableSavedRunSlots(room);
    const canResumeSavedRun = room.savedRun !== undefined && availableSavedRunSlots.length > 0;

    if (room.status === "closed") {
      throw new LobbyStoreError(410, "room_closed", "该房间已经关闭。");
    }
    if (room.status === "starting" && !canResumeSavedRun) {
      throw new LobbyStoreError(409, "room_started", "该房间已经开始游戏，当前不允许再加入。");
    }
    if (!room.savedRun && room.currentPlayers >= room.maxPlayers) {
      throw new LobbyStoreError(409, "room_full", "该房间已满。");
    }
    if (room.requiresPassword && !verifyPassword(input.password ?? "", room.passwordHash)) {
      throw new LobbyStoreError(401, "invalid_password", "房间密码错误。");
    }

    const localMods = validateModInventory(input.localMods, {
      maxDescriptors: this.config.modSyncMaxDescriptors,
      maxPayloadBytes: this.config.modSyncMaxPayloadBytes,
    });
    const hostInventoryAvailable = room.hostModInventory !== undefined;
    const diff = resolveModDiff({
      hostGameVersion: room.version,
      localGameVersion: input.gameVersion,
      hostMods: room.hostModInventory ?? [],
      localMods: hostInventoryAvailable ? localMods : [],
    });
    return {
      ...diff,
      canContinueRelaxed: diff.gameVersion.exactMatch && !this.config.strictModVersionCheck,
      hostInventoryAvailable,
      ...(room.hostModInventoryHash === undefined ? {} : { inventoryHash: room.hostModInventoryHash }),
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
    if (input.connectedPlayerNetIds !== undefined) {
      const connectedPlayerNetIds = normalizeNetIdList(input.connectedPlayerNetIds);
      hostSession.reportedPlayerNetIds = new Set(connectedPlayerNetIds);
      hostSession.reportedBindingSequence = this.nextClientControlBindingSequence;
      if (room.savedRun) {
        room.savedRun.connectedPlayerNetIds = connectedPlayerNetIds;
        room.savedRun.slots = room.savedRun.slots.map((slot) => ({
          ...slot,
          isConnected: room.savedRun?.connectedPlayerNetIds.includes(slot.netId) ?? false,
        }));
      }
    }
    return this.toRoomSummary(room);
  }

  deleteRoom(roomId: string, hostToken: string) {
    const hostSession = this.requireHostSession(roomId);
    this.assertHostToken(hostSession, hostToken);
    this.removeRoom(roomId);
  }

  cleanupExpired(
    now = new Date(),
    shouldRetainRoom: (roomId: string) => boolean = () => false,
  ) {
    const deletedRoomIds: string[] = [];
    for (const room of this.rooms.values()) {
      const hostSession = this.hostSessions.get(room.roomId);
      const roomExpired = now.getTime() - room.lastHeartbeatAt.getTime() > this.config.heartbeatTimeoutMs;
      const hostExpired = hostSession
        ? now.getTime() - hostSession.lastSeenAt.getTime() > this.config.heartbeatTimeoutMs
        : true;
      if ((roomExpired || hostExpired) && !shouldRetainRoom(room.roomId)) {
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

  validateHostControl(
    roomId: string,
    controlChannelId: string,
    token: string,
    clientVersion?: string,
    capabilityDigest?: string,
  ) {
    const hostSession = this.requireHostSession(roomId);
    if (hostSession.controlChannelId !== controlChannelId) {
      throw new LobbyStoreError(401, "invalid_control_channel", "控制通道无效。");
    }

    this.assertHostToken(hostSession, token);
    this.assertControlProtocol(this.requireRoom(roomId), clientVersion, capabilityDigest);
    if (hostSession.legacyIdentityMode) {
      this.logLegacyIdentityMode(hostSession, "create request omitted clientInstallationId");
    }
    return hostSession;
  }

  validateClientControl(
    roomId: string,
    controlChannelId: string,
    ticketId: string,
    clientVersion?: string,
    capabilityDigest?: string,
  ) {
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

    if (ticket.controlBindingId !== undefined) {
      throw new LobbyStoreError(401, "ticket_already_redeemed", "加入票据已使用。请重新加入房间获取新票据。");
    }

    if (ticket.protocolProfileV2 === "tail_v1") {
      if (clientVersion !== ticket.clientVersion || capabilityDigest !== ticket.joinerCapabilityDigest) {
        throw new LobbyStoreError(409, "capability_digest_mismatch", "控制通道协议能力与加入票据不一致。");
      }
    }

    const room = this.requireRoom(roomId);
    const redeemedClientInstallationId = this.resolveClientInstallationIdentity(
      room,
      ticket.playerNetId,
      ticket.clientInstallationId,
    );
    if (ticket.clientInstallationId !== undefined && redeemedClientInstallationId === undefined) {
      ticket.clientInstallationId = undefined;
      ticket.identityKind = "legacy";
      hostSession.legacyIdentityMode = true;
      this.logLegacyIdentityMode(
        hostSession,
        "control ticket declared an identity that now matches a public slot id",
      );
    }

    if (
      ticket.clientInstallationId
      && hostSession.kickedClientInstallationIds.has(ticket.clientInstallationId)
    ) {
      this.tickets.delete(ticket.ticketId);
      throw new LobbyStoreError(403, "kicked", "你已被房主移出该房间，无法重新加入。");
    }

    const binding: ClientControlBinding = {
      bindingId: this.id(),
      boundSequence: ++this.nextClientControlBindingSequence,
      roomId,
      playerName: ticket.playerName,
      playerNetId: ticket.playerNetId,
      clientInstallationId: ticket.clientInstallationId,
      identityKind: ticket.identityKind,
      boundAt: new Date(),
      protocolFlowNonce: ticket.protocolFlowNonce,
    };
    if (binding.playerNetId !== undefined) {
      this.rememberClientControlBinding(binding);
      this.invalidateOtherSlotTickets(binding, ticket.ticketId);
    }
    ticket.controlBindingId = binding.bindingId;

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

  kickPlayer(
    roomId: string,
    hostToken: string,
    playerNetId: string,
    requestedBindingId?: string,
  ): KickPlayerResult {
    const hostSession = this.requireHostSession(roomId);
    this.assertHostToken(hostSession, hostToken);
    const roomBindings = this.clientControlBindings.get(roomId);
    const binding = roomBindings?.get(playerNetId);
    if (!binding) {
      return { outcome: "not_found" };
    }

    const normalizedBindingId = normalizeOptionalIdentity(requestedBindingId);
    if (
      normalizedBindingId === undefined
      && this.reboundClientControlSlots.get(roomId)?.has(playerNetId)
    ) {
      this.warn(
        `[lobby] legacy kick rejected roomId=${roomId} playerNetId=${playerNetId} `
        + "reason=legacy_rebound",
      );
      return { outcome: "legacy_rebound" };
    }
    if (normalizedBindingId !== undefined && normalizedBindingId !== binding.bindingId) {
      this.warn(
        `[lobby] stale kick rejected roomId=${roomId} playerNetId=${playerNetId} `
        + `requestedBindingId=${normalizedBindingId} currentBindingId=${binding.bindingId}`,
      );
      return {
        outcome: "stale_binding",
        requestedBindingId: normalizedBindingId,
        currentBindingId: binding.bindingId,
      };
    }

    roomBindings?.delete(playerNetId);
    const legacyRequest = normalizedBindingId === undefined;
    const persistentBanCreated = !legacyRequest
      && binding.identityKind === "installation"
      && binding.clientInstallationId !== undefined;
    if (persistentBanCreated) {
      this.addKickedClientInstallationId(hostSession, binding.clientInstallationId!);
    }

    for (const ticket of this.tickets.values()) {
      if (ticket.roomId === roomId && ticket.playerNetId === playerNetId) {
        this.tickets.delete(ticket.ticketId);
      }
    }
    return {
      outcome: "kicked",
      binding,
      persistentBanCreated,
      legacyRequest,
    };
  }

  listClientControlBindingHandles(
    roomId: string,
    hostToken: string,
  ): ClientControlBindingHandle[] {
    const hostSession = this.requireHostSession(roomId);
    this.assertHostToken(hostSession, hostToken);
    const handles: ClientControlBindingHandle[] = [];
    for (const binding of this.clientControlBindings.get(roomId)?.values() ?? []) {
      if (binding.playerNetId !== undefined) {
        handles.push({
          bindingId: binding.bindingId,
          playerNetId: binding.playerNetId,
          playerName: binding.playerName,
          protocolFlowNonce: binding.protocolFlowNonce,
        });
      }
    }
    return handles;
  }

  isClientInstallationKicked(roomId: string, clientInstallationId: string) {
    const hostSession = this.hostSessions.get(roomId);
    if (!hostSession) {
      return false;
    }

    return hostSession.kickedClientInstallationIds.has(clientInstallationId);
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

  getRoomChatContext(roomId: string): RoomChatContext | undefined {
    const room = this.rooms.get(roomId);
    const hostSession = this.hostSessions.get(roomId);
    if (!room || !hostSession) {
      return undefined;
    }

    return {
      roomId: hostSession.roomId,
      roomSessionId: hostSession.roomSessionId,
      chatEnabled: hostSession.roomSettings.chatEnabled,
      peerPlayerNetIds: new Set(
        this.peerPlayerNetIds(hostSession.roomId, hostSession.roomSessionId),
      ),
    };
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

  private logLegacyIdentityMode(hostSession: HostSession, reason: string) {
    if (hostSession.legacyIdentityModeLogged) {
      return;
    }

    hostSession.legacyIdentityModeLogged = true;
    this.warn(
      `[lobby] room identity mode=legacy roomId=${hostSession.roomId} reason=${reason}; `
      + "legacy kicks disconnect the current slot binding without creating a persistent ban",
    );
  }

  private resolveClientInstallationIdentity(
    room: Room,
    playerNetIdValue: string | undefined,
    clientInstallationIdValue: string | undefined,
  ): string | undefined {
    const clientInstallationId = normalizeOptionalIdentity(clientInstallationIdValue);
    if (clientInstallationId === undefined) {
      return undefined;
    }

    if (/^[0-9]+$/.test(clientInstallationId)) {
      return undefined;
    }

    const playerNetId = normalizeOptionalIdentity(playerNetIdValue);
    if (clientInstallationId === playerNetId) {
      return undefined;
    }

    if (room.savedRun?.slots.some((slot) => slot.netId === clientInstallationId)) {
      return undefined;
    }

    if (this.clientControlBindings.get(room.roomId)?.has(clientInstallationId)) {
      return undefined;
    }

    const hostSession = this.hostSessions.get(room.roomId);
    if (
      hostSession
      && this.peerPlayerNetIds(room.roomId, hostSession.roomSessionId).has(clientInstallationId)
    ) {
      return undefined;
    }

    return clientInstallationId;
  }

  private rememberClientControlBinding(binding: ClientControlBinding) {
    const playerNetId = binding.playerNetId;
    if (playerNetId === undefined) {
      return;
    }

    let roomBindings = this.clientControlBindings.get(binding.roomId);
    if (!roomBindings) {
      roomBindings = new Map();
      this.clientControlBindings.set(binding.roomId, roomBindings);
    }
    const previous = roomBindings.get(playerNetId);
    if (previous !== undefined && previous.bindingId !== binding.bindingId) {
      let reboundSlots = this.reboundClientControlSlots.get(binding.roomId);
      if (!reboundSlots) {
        reboundSlots = new Set();
        this.reboundClientControlSlots.set(binding.roomId, reboundSlots);
      }
      reboundSlots.add(playerNetId);
    }
    roomBindings.delete(playerNetId);
    roomBindings.set(playerNetId, binding);
    if (roomBindings.size <= this.config.maxClientControlBindingsPerRoom) {
      return;
    }

    const hostSession = this.requireHostSession(binding.roomId);
    const activePlayerNetIds = this.peerPlayerNetIds(
      binding.roomId,
      hostSession.roomSessionId,
    );
    for (const [candidatePlayerNetId, candidate] of roomBindings) {
      if (
        candidatePlayerNetId === playerNetId
        || activePlayerNetIds.has(candidatePlayerNetId)
        || hostSession.reportedPlayerNetIds === undefined
        || hostSession.reportedBindingSequence === undefined
        || hostSession.reportedBindingSequence < candidate.boundSequence
        || hostSession.reportedPlayerNetIds.has(candidatePlayerNetId)
      ) {
        continue;
      }

      roomBindings.delete(candidatePlayerNetId);
      const reboundSlots = this.reboundClientControlSlots.get(binding.roomId);
      reboundSlots?.delete(candidatePlayerNetId);
      if (reboundSlots?.size === 0) {
        this.reboundClientControlSlots.delete(binding.roomId);
      }
      this.warn(
        `[lobby] client control binding evicted roomId=${binding.roomId} `
        + `playerNetId=${candidatePlayerNetId} bindingId=${candidate.bindingId} `
        + `reason=inactive capacity=${this.config.maxClientControlBindingsPerRoom}`,
      );
      return;
    }

    roomBindings.delete(playerNetId);
    if (roomBindings.size === 0) {
      this.clientControlBindings.delete(binding.roomId);
    }
    this.warn(
      `[lobby] client control binding refused roomId=${binding.roomId} `
      + `playerNetId=${playerNetId} reason=no_inactive_binding `
      + `capacity=${this.config.maxClientControlBindingsPerRoom}`,
    );
    throw new LobbyStoreError(
      503,
      "binding_capacity",
      "房间控制连接已满，请稍后重试。",
    );
  }

  private invalidateOtherSlotTickets(binding: ClientControlBinding, redeemedTicketId: string) {
    for (const ticket of this.tickets.values()) {
      if (
        ticket.ticketId !== redeemedTicketId
        && ticket.roomId === binding.roomId
        && ticket.playerNetId === binding.playerNetId
      ) {
        this.tickets.delete(ticket.ticketId);
      }
    }
  }

  private addKickedClientInstallationId(hostSession: HostSession, clientInstallationId: string) {
    hostSession.kickedClientInstallationIds.delete(clientInstallationId);
    hostSession.kickedClientInstallationIds.add(clientInstallationId);
    while (hostSession.kickedClientInstallationIds.size > this.config.maxKickedClientInstallationIds) {
      const oldestClientInstallationId = hostSession.kickedClientInstallationIds.values()
        .next().value as string | undefined;
      if (oldestClientInstallationId === undefined) {
        break;
      }
      hostSession.kickedClientInstallationIds.delete(oldestClientInstallationId);
      this.warn(
        `[lobby] kicked installation id evicted roomId=${hostSession.roomId} `
        + `limit=${this.config.maxKickedClientInstallationIds}`,
      );
    }
  }

  private resolveCreateProtocol(input: CreateRoomInput) {
    return this.runProtocolValidation(() => {
      const generation = classifyClientVersion(input.clientVersion, input.modVersion);
      const clientVersion = normalizeClientVersion(input.clientVersion, input.modVersion);
      const profile = parseRequestedProfile({
        generation,
        legacyProfile: input.protocolProfile,
        canonicalProfile: input.protocolProfileV2,
      });
      const offer = this.resolveOffer(generation, clientVersion, input.protocolOffer);
      const selection = selectRoomProtocol(offer, {
        profile,
        lanProtocolMin: 1,
        lanProtocolMax: 1,
        maxPlayers: input.maxPlayers,
        gameVersion: input.version,
        wireCacheSignatureV1: normalizeWireCacheSignatureV1(input.wireCacheSignatureV1),
      });
      return { clientVersion, generation, profile, selection };
    });
  }

  private resolveOffer(
    generation: ClientApiGeneration,
    clientVersion: string,
    offer?: ProtocolOffer,
  ): ProtocolOffer {
    if (offer !== undefined) {
      if (offer.clientVersion !== clientVersion) {
        throw new LobbyStoreError(400, "protocol_profile_unsupported", "protocolOffer.clientVersion 与请求客户端版本不一致。", {
          requiredClientVersion: clientVersion,
        });
      }
      return offer;
    }
    if (generation === "compat_0_3_0_5") {
      return Object.freeze({
        lanProtocolMin: 0,
        lanProtocolMax: 0,
        clientVersion,
        ritsuLibPresent: false,
        ritsuLibSidecarAvailable: false,
      });
    }
    throw new LobbyStoreError(400, "protocol_profile_unsupported", "0.6 客户端必须提交 protocolOffer。", {
      requiredClientVersion: "0.6.0-alpha.1",
    });
  }

  private runProtocolValidation<T>(callback: () => T): T {
    try {
      return callback();
    } catch (error) {
      if (error instanceof LobbyStoreError) throw error;
      if (error instanceof ProtocolContractError) {
        throw new LobbyStoreError(error.statusCode, error.code, error.message, error.details);
      }
      throw error;
    }
  }

  private assertControlProtocol(room: Room, clientVersion?: string, capabilityDigest?: string): void {
    if (room.protocolProfileV2 !== "tail_v1") return;
    if (clientVersion !== room.clientVersion || capabilityDigest !== room.protocolSelection.capabilityDigest) {
      throw new LobbyStoreError(409, "capability_digest_mismatch", "控制通道协议能力与房间不一致。");
    }
  }

  private removeRoom(roomId: string) {
    this.rooms.delete(roomId);
    this.hostSessions.delete(roomId);
    this.clientControlBindings.delete(roomId);
    this.reboundClientControlSlots.delete(roomId);
    for (const ticket of this.tickets.values()) {
      if (ticket.roomId === roomId) {
        this.tickets.delete(ticket.ticketId);
      }
    }
  }

  private toRoomSummary(room: Room): RoomSummary {
    const relayState = this.hostSessions.get(room.roomId)?.relayState ?? "disabled";
    const visibleStatus = getPublicRoomStatus(room);
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
      protocolProfile: projectProfile(room.protocolProfileV2, "compat_0_3_0_5") as LegacyProtocolProfile,
      protocolProfileV2: room.protocolProfileV2,
      protocolSelection: room.protocolSelection,
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

function normalizeOptionalString(value: string | undefined): string | undefined {
  const normalized = value?.trim();
  return normalized ? normalized : undefined;
}

function normalizeOptionalIdentity(value: string | undefined): string | undefined {
  const normalized = value?.trim();
  return normalized ? normalized : undefined;
}

// wcv1: followed by an unpadded base64url SHA-256 digest.
const WIRE_CACHE_SIGNATURE_V1_PATTERN = /^wcv1:[A-Za-z0-9_-]{43}$/;

/**
 * Only a well-formed signature is authoritative. Anything else is treated as ABSENT
 * rather than as a value that can mismatch, so a peer sending a placeholder or a
 * future/foreign token is allowed through instead of being rejected. Rejecting a
 * compatible player is worse than missing an incompatible one, which the in-band
 * handshake gate also checks.
 */
function normalizeWireCacheSignatureV1(value: string | undefined): string | undefined {
  const normalized = normalizeOptionalString(value);
  return normalized && WIRE_CACHE_SIGNATURE_V1_PATTERN.test(normalized) ? normalized : undefined;
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
