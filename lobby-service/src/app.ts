import { randomUUID } from "node:crypto";
import express, { type Express, type NextFunction, type Request, type Response } from "express";
import { readFileSync } from "node:fs";
import { createServer, type IncomingMessage, type Server as HttpServer } from "node:http";
import { join } from "node:path";
import { WebSocketServer, type WebSocket } from "ws";
import type { LobbyServiceConfig } from "./config.js";
import {
  consumeCreateJoinRateLimit,
  getCreateRoomToken,
  getLobbyAccessToken,
  ipMatchesCidr,
  normalizeIp,
  resolveClientIp,
  type ClientIpRequest,
} from "./client-ip.js";
import { CreateRoomBandwidthGuard } from "./bandwidth-guard.js";
import { assertRelayCreateReady, assertRelayJoinReady } from "./join-guard.js";
import {
  MOD_SYNC_MINIMUM_CLIENT_VERSION,
  MOD_SYNC_PROTOCOL_VERSION,
  type LobbyModDescriptor,
} from "./mod-sync/protocol.js";
import { ModSyncValidationError, validateModInventory } from "./mod-sync/validator.js";
import { RoomRelayManager } from "./relay.js";
import { cleanupExpiredRooms } from "./room-cleanup.js";
import {
  createServerAdminCsrfToken,
  digestServerAdminCsrfToken,
  signServerAdminSession,
  verifyServerAdminCsrfToken,
  verifyServerAdminPassword,
  verifySignedServerAdminSession,
} from "./server-admin-auth.js";
import { ServerAdminStateStore } from "./server-admin-state.js";
import { renderServerAdminPage } from "./server-admin-ui.js";
import {
  LobbyStore,
  LobbyStoreError,
  type CreateRoomInput,
  type HeartbeatInput,
  type JoinRoomInput,
} from "./store.js";
import { loadOrCreateIdentity } from "./peer/identity.js";
import { PeerStore } from "./peer/store.js";
import { mountHealth } from "./peer/handlers/health.js";
import { mountList } from "./peer/handlers/list.js";
import { mountAnnounce } from "./peer/handlers/announce.js";
import { mountHeartbeat } from "./peer/handlers/heartbeat.js";
import { GossipScheduler } from "./peer/gossip.js";
import { loadSeedsFromCf } from "./peer/seeds-loader.js";
import { bootstrapPeers } from "./peer/bootstrap.js";
import { announceToBootstrappedPeers } from "./peer/auto-announce.js";
import { mountMetrics } from "./peer/handlers/metrics.js";
import { ChatPeerError, ChatPeerRegistry } from "./chat/peer-registry.js";
import {
  governanceToFeatureVersions,
  PHASE_4_CHAT_FEATURES,
  resolveEnabledFeatures,
  type ChatFeatureGovernance,
} from "./chat/feature-resolver.js";
import { ServerChatGateway, type ServerChatGatewayOptions } from "./chat/gateway.js";
import { RateLimitError, SlidingWindowLimiter } from "./chat/rate-limiter.js";
import { RoomChatGateway } from "./chat/room-gateway.js";
import {
  ChatTicketError,
  ChatTicketStore,
  type ReservedChatTicket,
} from "./chat/ticket-store.js";
import { installUpgradeRouter, type ChatUpgradeDecision } from "./chat/upgrade-router.js";

const MaxLobbyPlayers = 256;
const MaxRoomNameLength = 80;
const MaxPlayerNameLength = 32;
const MaxGameModeLength = 32;
const MaxVersionLength = 32;
const MaxModVersionLength = 32;
const MaxProtocolProfileLength = 32;
const MaxPasswordLength = 64;
const MaxModListEntries = 128;
const MaxModListEntryLength = 64;
const MaxHostLocalAddressCount = 16;
const MaxHostLocalAddressLength = 64;
const MaxSavedRunSlots = 16;
const MaxSavedRunConnectedNetIds = 16;
const MaxNetIdLength = 64;
const MaxCharacterIdLength = 64;
const MaxCharacterNameLength = 64;
const ChatMaxMessageChars = 300;
const ChatMaxSegments = 32;
const ChatMaxEntitiesPhase3 = 12;
const ChatHistoryLimitPhase3 = 50;
const ChatProtocolVersion = 1;


function readLobbyServiceVersion() {
  try {
    const raw = readFileSync(new URL("../package.json", import.meta.url), "utf8");
    const parsed = JSON.parse(raw) as { version?: unknown };
    return typeof parsed.version === "string" && parsed.version.trim().length > 0 ? parsed.version.trim() : "unknown";
  } catch {
    return "unknown";
  }
}

const lobbyServiceVersion = readLobbyServiceVersion();

type PeerRuntimeState = "disabled" | "unconfigured" | "private" | "joining" | "joined";

class InputError extends Error {}

type RateLimitBucket = {
  hits: number[];
  lastSeenAt: number;
};

class HttpError extends Error {
  constructor(
    readonly statusCode: number,
    readonly code: string,
    message: string,
  ) {
    super(message);
  }
}

function isJsonParseError(error: unknown): boolean {
  if (!error || typeof error !== "object") {
    return false;
  }

  const candidate = error as { type?: unknown; status?: unknown; statusCode?: unknown };
  const status = typeof candidate.status === "number" ? candidate.status : candidate.statusCode;
  return candidate.type === "entity.parse.failed"
    || candidate.type === "entity.too.large"
    || (error instanceof SyntaxError && typeof status === "number" && status >= 400 && status < 500);
}

interface ControlPeer {
  socket: WebSocket;
  connectionSessionId: string;
  roomId: string;
  roomSessionId: string;
  controlChannelId: string;
  role: "host" | "client";
  lastSeenAt: number;
  ticketId?: string;
  playerNetId?: string;
  playerName?: string;
}

interface ServerAdminSession {
  id: string;
  username: string;
  expiresAt: number;
  csrfToken: string;
  csrfDigest: Buffer;
}


export interface LobbyService {
  app: Express;
  httpServer: HttpServer;
  start(): Promise<{ host: string; port: number }>;
  close(): Promise<void>;
}

export interface LobbyServiceDependencies {
  createChatPeerRegistry?: () => ChatPeerRegistry;
  chatGatewayOptions?: Pick<ServerChatGatewayOptions, "heartbeatTickMs">;
  beforeServerAdminMutation?: (
    kind: "settings" | "clear-history",
  ) => void | Promise<void>;
}

export function createProductionDependencies(): LobbyServiceDependencies {
  return {};
}

export async function createLobbyService(
  config: LobbyServiceConfig,
  dependencies: LobbyServiceDependencies = createProductionDependencies(),
): Promise<LobbyService> {
  const env = config;
  const peerEnv = env.peer;
  const roomPeers = new Map<string, Set<ControlPeer>>();

  const store = new LobbyStore({
    heartbeatTimeoutMs: env.heartbeatTimeoutMs,
    ticketTtlMs: env.ticketTtlMs,
    strictGameVersionCheck: env.strictGameVersionCheck,
    strictModVersionCheck: env.strictModVersionCheck,
    connectionStrategy: env.connectionStrategy,
    modSyncMaxDescriptors: env.modSyncMaxDescriptors,
    modSyncMaxPayloadBytes: env.modSyncMaxPayloadBytes,
  }, {
    peerPlayerNetIds: (roomId, roomSessionId) => {
      const playerNetIds = new Set<string>();
      for (const peer of roomPeers.get(roomId) ?? []) {
        if (
          peer.roomSessionId === roomSessionId
          && peer.socket.readyState === peer.socket.OPEN
          && peer.playerNetId
        ) {
          playerNetIds.add(peer.playerNetId);
        }
      }
      return playerNetIds;
    },
  });
  const relayManager = new RoomRelayManager(
    {
      bindHost: env.relayBindHost,
      portStart: env.relayPortStart,
      portEnd: env.relayPortEnd,
      hostIdleMs: env.relayHostIdleMs,
      clientIdleMs: env.relayClientIdleMs,
    },
    ({ phase, roomId, detail }) => {
      if (phase === "relay_allocated" || phase === "relay_host_idle") {
        store.setRelayState(roomId, "planned");
      } else if (phase === "relay_host_registered") {
        store.setRelayState(roomId, "ready");
      } else if (phase === "relay_removed") {
        store.setRelayState(roomId, "disabled");
      }
      console.log(`[relay] ${phase} room=${roomId} ${detail}`);
    },
  );
  const serverAdminStateStore = new ServerAdminStateStore(env.serverAdminStateFile, {
    publicListingEnabledDefault: env.peerPublicListingEnabledDefault,
    modSyncEnabledDefault: env.modSyncEnabled,
    chatFeaturesDefault: env.chat.features,
  });
  const initialChatGovernance = serverAdminStateStore.getState().chatFeatures;
  const createRoomBandwidthGuard = new CreateRoomBandwidthGuard();
  const serverAdminSessions = new Map<string, ServerAdminSession>();
  const createJoinRateLimitHits = new Map<string, RateLimitBucket>();
  const chatTicketStore = new ChatTicketStore({
    maxPendingTickets: env.chat.maxPendingTickets,
  });
  const chatTicketRateLimiter = new SlidingWindowLimiter({
    purpose: "ticket",
    maxRequests: env.chat.ticketRequestsPerMinute,
    windowMs: 60_000,
  });
  const chatPeerRegistry = dependencies.createChatPeerRegistry?.() ?? new ChatPeerRegistry({
    maxTotal: env.chat.maxConnectionsTotal,
    maxPerIp: env.chat.maxConnectionsPerIp,
    slowClientBytes: env.chat.slowClientBytes,
  });
  const chatGateway = new ServerChatGateway({
    peerRegistry: chatPeerRegistry,
    chatEnabled: initialChatGovernance.serverChatEnabled,
    compiledFeatures: PHASE_4_CHAT_FEATURES,
    configuredFeatures: governanceToFeatureVersions(env.chat.features, "server"),
    adminFeatures: governanceToFeatureVersions(initialChatGovernance, "server"),
    maxPayloadBytes: env.chat.maxPayloadBytes,
    historyLimit: env.chat.historyLimit,
    historyTtlMs: env.chat.historyTtlMs,
    snapshotLimit: env.chat.snapshotLimit,
    connectionBurst: env.chat.connectionBurst,
    connectionRefillMs: env.chat.connectionRefillMs,
    ipMessagesPerMinute: env.chat.ipMessagesPerMinute,
    ...dependencies.chatGatewayOptions,
  });
  const currentRoomChatFeatures = resolveEnabledFeatures({
    channel: "room",
    compiled: PHASE_4_CHAT_FEATURES,
    configured: PHASE_4_CHAT_FEATURES,
    channelEnabled: true,
    roomV2Enabled: true,
  });
  const roomChatGateway = new RoomChatGateway({
    compiledFeatures: PHASE_4_CHAT_FEATURES,
    configuredFeatures: governanceToFeatureVersions(env.chat.features, "room"),
    adminFeatures: governanceToFeatureVersions(initialChatGovernance, "room"),
    roomV2Enabled: initialChatGovernance.roomChatV2Enabled,
    getRoomChatContext: (roomId) => store.getRoomChatContext(roomId),
  });
  const reservedChatTicketsByUpgrade = new WeakMap<IncomingMessage, ReservedChatTicket>();
  let peerStore: PeerStore | null = null;
  let gossipScheduler: GossipScheduler | null = null;
  let cleanupInterval: NodeJS.Timeout | null = null;
  let selfEntryRefreshInterval: NodeJS.Timeout | null = null;
  let serverAdminMutations: Promise<void> = Promise.resolve();
  let started = false;
  let closed = false;
  let boundAddress: { host: string; port: number } | null = null;

  function requirePeerStore(): PeerStore {
    if (!peerStore) {
      throw new Error("peer store is not initialized");
    }
    return peerStore;
  }

  const app = express();
  app.disable("x-powered-by");
  app.use("/server-admin", (_req, res, next) => {
    res.setHeader("Cache-Control", "no-store");
    next();
  });
  const jsonEnvelopeLimitBytes = Math.max(32 * 1024, env.modSyncMaxPayloadBytes + 16 * 1024);
  app.use(express.json({ limit: jsonEnvelopeLimitBytes }));
  app.use((req, res, next) => {
    const startedAt = Date.now();
    res.on("finish", () => {
      const durationMs = Date.now() - startedAt;
      const ipField = res.locals.omitClientIpFromLog === true ? "" : ` ip=${requestIp(req)}`;
      console.log(
        `[http] ${req.method} ${req.path}${ipField} status=${res.statusCode} durationMs=${durationMs}`,
      );
    });
    next();
  });

  app.get("/health", (req, res) => {
    cleanupExpiredRoomsNow();
    if (!env.publicDetailedHealthEnabled && !isTrustedOperationsRequest(req) && !hasLobbyReadAccessToken(req)) {
      res.json({
        ok: true,
      });
      return;
    }

    const guardSnapshot = getCreateRoomGuardSnapshot();
    const relayTrafficSnapshot = relayManager.getTrafficSnapshot();
    res.json({
      ok: true,
      rooms: store.listRooms().length,
      strictGameVersionCheck: env.strictGameVersionCheck,
      strictModVersionCheck: env.strictModVersionCheck,
      connectionStrategy: env.connectionStrategy,
      createRoomGuardApplies: guardSnapshot.createRoomGuardApplies,
      createRoomGuardStatus: guardSnapshot.createRoomGuardStatus,
      currentBandwidthMbps: guardSnapshot.currentBandwidthMbps,
      bandwidthCapacityMbps: guardSnapshot.bandwidthCapacityMbps,
      resolvedCapacityMbps: guardSnapshot.resolvedCapacityMbps,
      bandwidthUtilizationRatio: guardSnapshot.bandwidthUtilizationRatio,
      capacitySource: guardSnapshot.capacitySource,
      createRoomThresholdRatio: guardSnapshot.createRoomThresholdRatio,
      relayTrafficWindowMs: relayTrafficSnapshot.windowMs,
      relayTrafficBytesInWindow: relayTrafficSnapshot.totalBytesInWindow,
      relayActiveRooms: relayTrafficSnapshot.activeRooms,
      relayActiveHosts: relayTrafficSnapshot.activeHosts,
      relayActiveClients: relayTrafficSnapshot.activeClients,
    });
  });

  app.get("/probe", (_req, res) => {
    res.json({
      ok: true,
      capabilities: {
        serverChatVersion: 1,
        roomChatProtocolVersion: 1,
        richContentVersion: currentRoomChatFeatures.richContentVersion,
        emojiSetVersion: currentRoomChatFeatures.emojiSetVersion,
        itemRefVersion: currentRoomChatFeatures.itemRefVersion,
        combatRefVersion: currentRoomChatFeatures.combatRefVersion,
        maxMessageChars: ChatMaxMessageChars,
        maxSegments: ChatMaxSegments,
        maxEntities: ChatMaxEntitiesPhase3,
        historyLimit: ChatHistoryLimitPhase3,
        modSyncProtocolVersion: MOD_SYNC_PROTOCOL_VERSION,
        modSyncEnabled: serverAdminStateStore.getState().modSyncEnabled,
        modSyncMinimumClientVersion: MOD_SYNC_MINIMUM_CLIENT_VERSION,
      },
    });
  });

  app.post("/chat/tickets", (req, res) => {
    res.locals.omitClientIpFromLog = true;

    if (Object.keys(req.query).length > 0) {
      throw new InputError("聊天票据请求不接受查询参数。");
    }

    assertLobbyAccessForChat(req);

    const clientIp = resolveChatClientIp(req);
    if (!clientIp) {
      throw new HttpError(400, "invalid_request", "无法解析客户端地址。");
    }

    let rateResult;
    try {
      rateResult = chatTicketRateLimiter.consume(clientIp);
    } catch (error) {
      if (error instanceof RateLimitError) {
        throw new HttpError(503, "server_busy", "聊天票据服务繁忙，请稍后再试。");
      }
      throw error;
    }

    if (!rateResult.allowed) {
      const retryAfterSeconds = Math.max(1, Math.ceil(rateResult.retryAfterMs / 1000));
      res.setHeader("Retry-After", String(retryAfterSeconds));
      throw new HttpError(429, "rate_limited", "请求过于频繁，请稍后再试。");
    }

    const body = req.body as {
      protocolVersion?: unknown;
      playerNetId?: unknown;
      playerName?: unknown;
    } | undefined;

    const allowedFields = new Set(["protocolVersion", "playerNetId", "playerName"]);
    if (!body || Array.isArray(body) || Object.keys(body).some((key) => !allowedFields.has(key))) {
      throw new InputError("请求体只能包含 protocolVersion、playerNetId 与 playerName。");
    }

    if (typeof body?.protocolVersion !== "number" || !Number.isInteger(body.protocolVersion)) {
      throw new InputError("protocolVersion 必须为整数。");
    }

    if (typeof body.playerNetId !== "string" || typeof body.playerName !== "string") {
      throw new InputError("playerNetId 与 playerName 必须为字符串。");
    }

    let issued: { ticket: string; expiresAt: string };
    try {
      issued = chatTicketStore.issue({
        protocolVersion: body.protocolVersion,
        playerNetId: body.playerNetId,
        playerName: body.playerName,
        clientIp,
      });
    } catch (error) {
      if (error instanceof ChatTicketError) {
        if (error.code === "invalid_claims") {
          throw new InputError(error.message);
        }
        if (error.code === "server_busy") {
          throw new HttpError(503, "server_busy", "聊天票据服务繁忙，请稍后再试。");
        }
        throw new HttpError(400, "invalid_request", error.message);
      }
      throw error;
    }

    const webSocketUrl = buildChatWebSocketUrl(req);
    res.json({
      ticket: issued.ticket,
      expiresAt: issued.expiresAt,
      webSocketUrl,
      protocolVersion: ChatProtocolVersion,
    });
    console.log(`[chat] ticket issued expiresAt=${issued.expiresAt}`);
  });

  app.get("/rooms", (req, res) => {
    cleanupExpiredRoomsNow();
    if (!env.publicRoomListEnabled && !isTrustedOperationsRequest(req) && !hasLobbyReadAccessToken(req)) {
      throw new HttpError(403, "room_list_disabled", "房间列表未公开。请通过游戏客户端查询或使用受信请求。");
    }

    const trustedView = isTrustedOperationsRequest(req) || hasLobbyReadAccessToken(req);
    res.json(store.listRooms().map((room) => toRoomListView(room, trustedView)));
  });

  app.get("/announcements", (_req, res) => {
    res.json({
      announcements: serverAdminStateStore.getPublicAnnouncements(),
    });
  });

  app.post("/rooms", (req, res, next) => {
    let createdRoom:
      | {
          roomId: string;
          hostToken: string;
        }
      | undefined;

    try {
      assertCreateJoinRateLimit(req, "create_room");
      assertCreateRoomAuthorized(req);
      cleanupExpiredRoomsNow();
      const guardSnapshot = getCreateRoomGuardSnapshot();
      if (guardSnapshot.createRoomGuardApplies && guardSnapshot.createRoomGuardStatus === "block") {
        throw new LobbyStoreError(
          503,
          "server_bandwidth_near_capacity",
          "当前服务器接近带宽上限。为保证现有连接稳定，请切换到其他公共服务器后再创建房间。",
          {
            currentBandwidthMbps: guardSnapshot.currentBandwidthMbps,
            bandwidthCapacityMbps: guardSnapshot.bandwidthCapacityMbps,
            resolvedCapacityMbps: guardSnapshot.resolvedCapacityMbps,
            bandwidthUtilizationRatio: guardSnapshot.bandwidthUtilizationRatio,
            capacitySource: guardSnapshot.capacitySource,
            createRoomThresholdRatio: guardSnapshot.createRoomThresholdRatio,
          },
        );
      }
      const body = req.body as Partial<CreateRoomInput> | undefined;
      const roomInput: CreateRoomInput = {
        roomName: boundedString(body?.roomName, "roomName", MaxRoomNameLength),
        password: optionalBoundedString(body?.password, "password", MaxPasswordLength),
        hostPlayerName: boundedString(body?.hostPlayerName, "hostPlayerName", MaxPlayerNameLength),
        gameMode: boundedString(body?.gameMode, "gameMode", MaxGameModeLength),
        version: boundedString(body?.version, "version", MaxVersionLength),
        modVersion: boundedString(body?.modVersion, "modVersion", MaxModVersionLength),
        modList: boundedStringArray(body?.modList, "modList", MaxModListEntries, MaxModListEntryLength),
        hostModInventory: body?.hostModInventory === undefined
          ? undefined
          : parseModInventory(body.hostModInventory, "hostModInventory"),
        protocolProfile: optionalBoundedString(body?.protocolProfile, "protocolProfile", MaxProtocolProfileLength),
        maxPlayers: positiveInt(body?.maxPlayers, "maxPlayers", 1, MaxLobbyPlayers),
        hostConnectionInfo: {
          enetPort: positiveInt(body?.hostConnectionInfo?.enetPort, "hostConnectionInfo.enetPort", 1, 65535),
          localAddresses: boundedStringArray(
            body?.hostConnectionInfo?.localAddresses,
            "hostConnectionInfo.localAddresses",
            MaxHostLocalAddressCount,
            MaxHostLocalAddressLength),
        },
      };

      if (body?.savedRun) {
        roomInput.savedRun = {
          saveKey: boundedString(body.savedRun.saveKey, "savedRun.saveKey", MaxNetIdLength),
          slots: Array.isArray(body.savedRun.slots)
            ? (() => {
                if (body.savedRun.slots.length > MaxSavedRunSlots) {
                  throw new InputError(`savedRun.slots 数量不能超过 ${MaxSavedRunSlots} 个。`);
                }

                return body.savedRun.slots
                  .filter((value) => Boolean(value) && typeof value === "object")
                  .map((slot, index) => {
                    const candidate = slot as unknown as Record<string, unknown>;
                    return {
                      netId: boundedString(candidate.netId, `savedRun.slots[${index}].netId`, MaxNetIdLength),
                      characterId: optionalBoundedString(candidate.characterId, `savedRun.slots[${index}].characterId`, MaxCharacterIdLength),
                      characterName: optionalBoundedString(candidate.characterName, `savedRun.slots[${index}].characterName`, MaxCharacterNameLength),
                      playerName: optionalBoundedString(candidate.playerName, `savedRun.slots[${index}].playerName`, MaxPlayerNameLength),
                      isHost: Boolean(candidate.isHost),
                    };
                  });
              })()
            : [],
          connectedPlayerNetIds: boundedStringArray(
            body.savedRun.connectedPlayerNetIds,
            "savedRun.connectedPlayerNetIds",
            MaxSavedRunConnectedNetIds,
            MaxNetIdLength),
        };
      }

      const room = store.createRoom(roomInput, requestIp(req));
      createdRoom = {
        roomId: room.roomId,
        hostToken: room.hostToken,
      };
      const relayEndpoint = relayManager.allocateRoom(room.roomId, room.hostToken, resolveAdvertisedRelayHost(req));
      assertRelayCreateReady(env.connectionStrategy, relayEndpoint != null);
      if (relayEndpoint) {
        room.relayEndpoint = relayEndpoint;
        room.room.relayState = "planned";
      }
      console.log(
        `[lobby] create room roomId=${room.roomId} roomName="${room.room.roomName}" hostPlayer="${room.room.hostPlayerName}" version=${room.room.version} modVersion=${room.room.modVersion} protocolProfile=${room.room.protocolProfile} remote=${requestIp(req)} relay=${relayEndpoint ? `${relayEndpoint.host}:${relayEndpoint.port}` : "disabled"} relayState=${room.room.relayState}`,
      );
      res.status(201).json(room);
    } catch (error) {
      if (createdRoom) {
        try {
          store.deleteRoom(createdRoom.roomId, createdRoom.hostToken);
        } catch {
          // Room may have already been removed as part of rollback.
        }
        relayManager.removeRoom(createdRoom.roomId);
        closeRoomSockets(createdRoom.roomId, 4000, "room_create_failed");
      }
      next(error);
    }
  });

  app.post("/rooms/:id/join", (req, res, next) => {
    try {
      assertCreateJoinRateLimit(req, "join_room");
      cleanupExpiredRoomsNow();
      const body = req.body as Partial<JoinRoomInput> | undefined;
      const response = store.joinRoom(req.params.id, {
        playerName: boundedString(body?.playerName, "playerName", MaxPlayerNameLength),
        password: optionalBoundedString(body?.password, "password", MaxPasswordLength),
        version: boundedString(body?.version, "version", MaxVersionLength),
        modVersion: boundedString(body?.modVersion, "modVersion", MaxModVersionLength),
        modList: boundedStringArray(body?.modList, "modList", MaxModListEntries, MaxModListEntryLength),
        desiredSavePlayerNetId: optionalBoundedString(body?.desiredSavePlayerNetId, "desiredSavePlayerNetId", MaxNetIdLength),
        playerNetId: optionalBoundedString(body?.playerNetId, "playerNetId", MaxNetIdLength),
      });
      const relayEndpoint = relayManager.getRoomEndpoint(req.params.id, resolveAdvertisedRelayHost(req));
      if (relayEndpoint) {
        response.connectionPlan.relayAllowed = true;
        response.connectionPlan.relayEndpoint = relayEndpoint;
      }
      const relayStatus = relayManager.getRoomStatus(req.params.id);
      assertRelayJoinReady(env.connectionStrategy, response.room.relayState, relayStatus.hasActiveHost);
      console.log(
        `[lobby] join ticket issued roomId=${req.params.id} player="${body?.playerName ?? ""}" roomModVersion=${response.room.modVersion} protocolProfile=${response.room.protocolProfile} ticketId=${response.ticketId} remote=${requestIp(req)} strategy=${response.connectionPlan.strategy} direct=${response.connectionPlan.directCandidates.length} relay=${relayEndpoint ? `${relayEndpoint.host}:${relayEndpoint.port}` : "disabled"} relayState=${response.room.relayState} relayHost=${relayStatus.hasActiveHost ? relayStatus.activeHostDetail : "unregistered"} relayClients=${relayStatus.clientCount}`,
      );
      res.json(response);
    } catch (error) {
      next(error);
    }
  });

  app.post("/rooms/:id/mod-preflight", (req, res, next) => {
    try {
      assertCreateJoinRateLimit(req, "join_room");
      const body = req.body as Record<string, unknown> | undefined;
      const allowedFields = new Set([
        "playerName",
        "password",
        "gameVersion",
        "modSyncProtocolVersion",
        "localMods",
      ]);
      if (!body || Array.isArray(body) || Object.keys(body).some((key) => !allowedFields.has(key))) {
        throw new InputError("MOD 预检请求包含不支持的字段。");
      }

      const playerName = boundedString(body.playerName, "playerName", MaxPlayerNameLength);
      const password = optionalBoundedString(body.password, "password", MaxPasswordLength);
      const gameVersion = boundedString(body.gameVersion, "gameVersion", MaxVersionLength);
      const protocolVersion = positiveInt(
        body.modSyncProtocolVersion,
        "modSyncProtocolVersion",
        1,
        Number.MAX_SAFE_INTEGER,
      );
      const localMods = body.localMods;

      if (!serverAdminStateStore.getState().modSyncEnabled || protocolVersion !== MOD_SYNC_PROTOCOL_VERSION) {
        res.json({
          enabled: false,
          protocolVersion: MOD_SYNC_PROTOCOL_VERSION,
          gameVersion: { host: "", local: gameVersion, exactMatch: false },
          missingWorkshopMods: [],
          missingManualMods: [],
          extraGameplayMods: [],
          versionMismatches: [],
          canContinueRelaxed: true,
          hostInventoryAvailable: false,
        });
        return;
      }

      cleanupExpiredRoomsNow();
      const result = store.modPreflight(req.params.id, {
        playerName,
        password,
        gameVersion,
        localMods,
      });
      console.log(
        `[lobby] mod preflight roomId=${req.params.id} hostInventory=${result.hostInventoryAvailable ? "available" : "legacy"} inventoryHash=${result.inventoryHash ?? "none"} missingWorkshop=${result.missingWorkshopMods.length} missingManual=${result.missingManualMods.length} extraGameplay=${result.extraGameplayMods.length} versionMismatch=${result.versionMismatches.length}`,
      );
      res.json({
        enabled: true,
        protocolVersion: MOD_SYNC_PROTOCOL_VERSION,
        gameVersion: result.gameVersion,
        missingWorkshopMods: result.missingWorkshopMods,
        missingManualMods: result.missingManualMods,
        extraGameplayMods: result.extraGameplayMods,
        versionMismatches: result.versionMismatches,
        canContinueRelaxed: result.canContinueRelaxed,
        hostInventoryAvailable: result.hostInventoryAvailable,
      });
    } catch (error) {
      next(error);
    }
  });

  app.post("/rooms/:id/heartbeat", (req, res, next) => {
    try {
      const body = req.body as Partial<HeartbeatInput> | undefined;
      const room = store.heartbeat(req.params.id, {
        hostToken: requiredString(body?.hostToken, "hostToken"),
        currentPlayers: positiveInt(body?.currentPlayers, "currentPlayers", 1, MaxLobbyPlayers),
        status: requiredString(body?.status, "status"),
        connectedPlayerNetIds: Array.isArray(body?.connectedPlayerNetIds)
          ? body.connectedPlayerNetIds
              .filter((value): value is string => typeof value === "string")
              .map((value) => value.trim())
          : undefined,
      });
      res.json({ ok: true, room });
    } catch (error) {
      next(error);
    }
  });

  app.delete("/rooms/:id", (req, res, next) => {
    try {
      const hostToken = requiredString((req.body as { hostToken?: string } | undefined)?.hostToken, "hostToken");
      store.deleteRoom(req.params.id, hostToken);
      relayManager.removeRoom(req.params.id);
      closeRoomSockets(req.params.id, 4000, "room_deleted");
      console.log(`[lobby] room deleted roomId=${req.params.id}`);
      res.status(204).send();
    } catch (error) {
      next(error);
    }
  });

  app.post("/rooms/:id/connection-events", (req, res, next) => {
    try {
      const body = req.body as Record<string, unknown> | undefined;
      const ticketId = optionalString(body?.ticketId);
      if (ticketId && !store.hasTicketForRoom(req.params.id, ticketId)) {
        throw new LobbyStoreError(401, "invalid_ticket", "加入票据无效。");
      }

      const phase = requiredString(body?.phase, "phase");
      const candidateLabel = optionalString(body?.candidateLabel) ?? "<none>";
      const candidateEndpoint = optionalString(body?.candidateEndpoint) ?? "<none>";
      const detail = optionalString(body?.detail) ?? "<none>";
      const playerName = optionalString(body?.playerName) ?? "<unknown>";
      const relayStatus = relayManager.getRoomStatus(req.params.id);
      console.log(
        `[lobby] connection_event roomId=${req.params.id} ticketId=${ticketId ?? "<none>"} player="${playerName}" phase=${phase} candidate=${candidateLabel} endpoint=${candidateEndpoint} detail=${detail} remote=${requestIp(req)} relayHost=${relayStatus.hasActiveHost ? relayStatus.activeHostDetail : "unregistered"} relayClients=${relayStatus.clientCount}`,
      );
      res.status(202).json({ ok: true });
    } catch (error) {
      next(error);
    }
  });

  app.get("/server-admin", (_req, res) => {
    res.type("html").send(renderServerAdminPage(lobbyServiceVersion));
  });

  app.post("/server-admin/login", (req, res, next) => {
    try {
      ensureServerAdminConfigured();
      const body = req.body as { username?: string; password?: string } | undefined;
      const username = requiredString(body?.username, "username");
      const password = requiredString(body?.password, "password");
      if (username !== env.serverAdminUsername || !verifyServerAdminPassword(password, env.serverAdminPasswordHash)) {
        throw new HttpError(401, "invalid_server_admin_credentials", "用户名或密码不正确。");
      }

      const { session, csrfToken } = createServerAdminSession(username);
      setServerAdminCookie(req, res, session.id);
      res.json({
        id: session.id,
        username: session.username,
        expiresAt: new Date(session.expiresAt).toISOString(),
        csrfToken,
      });
    } catch (error) {
      next(error);
    }
  });

  app.post("/server-admin/logout", (req, res, next) => {
    try {
      const session = requireServerAdminSession(req);
      requireServerAdminCsrf(req, session);
      serverAdminSessions.delete(session.id);
      clearServerAdminCookie(req, res);
      res.status(204).send();
    } catch (error) {
      next(error);
    }
  });

  app.get("/server-admin/session", (req, res, next) => {
    try {
      const session = requireServerAdminSession(req);
      res.json({
        id: session.id,
        username: session.username,
        expiresAt: new Date(session.expiresAt).toISOString(),
        csrfToken: session.csrfToken,
      });
    } catch (error) {
      next(error);
    }
  });

  app.get("/server-admin/settings", (req, res, next) => {
    try {
      requireServerAdminSession(req);
      res.json(buildServerAdminSettingsResponse());
    } catch (error) {
      next(error);
    }
  });

  app.patch("/server-admin/settings", (req, res, next) => {
    (async () => {
      const session = requireServerAdminSession(req);
      requireServerAdminCsrf(req, session);
      const body = req.body as {
        displayName?: string;
        publicListingEnabled?: boolean;
        modSyncEnabled?: boolean;
        bandwidthCapacityMbps?: number | null;
        announcements?: unknown;
        chatFeatures?: unknown;
      } | undefined;
      const update: Parameters<ServerAdminStateStore["updateSettings"]>[0] = {};
      if (body && Object.hasOwn(body, "displayName")) {
        update.displayName = typeof body.displayName === "string" ? body.displayName : "";
      }
      if (body && Object.hasOwn(body, "publicListingEnabled")) {
        if (typeof body.publicListingEnabled !== "boolean") {
          throw new InputError("publicListingEnabled 必须为布尔值。");
        }
        update.publicListingEnabled = body.publicListingEnabled;
      }
      if (body && Object.hasOwn(body, "modSyncEnabled")) {
        if (typeof body.modSyncEnabled !== "boolean") {
          throw new InputError("modSyncEnabled 必须为布尔值。");
        }
        update.modSyncEnabled = body.modSyncEnabled;
      }
      if (body && Object.hasOwn(body, "bandwidthCapacityMbps")) {
        update.bandwidthCapacityMbps = optionalPositiveNumber(
          body.bandwidthCapacityMbps,
          "bandwidthCapacityMbps",
          100_000,
        );
      }
      if (body && Object.hasOwn(body, "announcements")) {
        update.announcements = body.announcements;
      }
      if (body && Object.hasOwn(body, "chatFeatures")) {
        update.chatFeatures = parseChatFeaturesPatch(body.chatFeatures);
      }
      const settings = await enqueueServerAdminMutation(async () => {
        await dependencies.beforeServerAdminMutation?.("settings");
        const committed = serverAdminStateStore.updateSettings(update);
        await applyChatGovernance(committed.chatFeatures);
        return committed;
      });
      res.json(buildServerAdminSettingsResponse(settings));
    })().catch((error) => {
      next(error);
    });
  });

  app.post("/server-admin/chat/clear-history", (req, res, next) => {
    (async () => {
      const session = requireServerAdminSession(req);
      requireServerAdminCsrf(req, session);
      const historyEpoch = await enqueueServerAdminMutation(async () => {
        await dependencies.beforeServerAdminMutation?.("clear-history");
        return chatGateway.clearHistory();
      });
      res.json({
        ok: true,
        historyEpoch,
        metrics: buildChatMetrics(),
      });
    })().catch(next);
  });

  app.use((error: unknown, _req: Request, res: Response, _next: NextFunction) => {
    if (isJsonParseError(error)) {
      res.status(400).json({
        code: "invalid_request",
        message: "请求体必须是有效的 JSON。",
      });
      return;
    }

    if (error instanceof LobbyStoreError) {
      res.status(error.statusCode).json({
        code: error.code,
        message: error.message,
        details: error.details,
      });
      return;
    }

    if (error instanceof InputError) {
      res.status(400).json({
        code: "invalid_request",
        message: error.message,
      });
      return;
    }

    if (error instanceof ModSyncValidationError) {
      res.status(400).json({
        code: "invalid_request",
        message: error.message,
      });
      return;
    }

    if (error instanceof HttpError) {
      res.status(error.statusCode).json({
        code: error.code,
        message: error.message,
      });
      return;
    }

    console.error("[lobby] unexpected request error", error);
    res.status(500).json({
      code: "internal_error",
      message: "大厅服务内部错误。",
    });
  });

  const server = createServer(app);
  // Both WebSocket servers are noServer; the single upgrade router owns path dispatch.
  const wss = new WebSocketServer({ noServer: true, maxPayload: env.chat.maxPayloadBytes });
  const chatWss = new WebSocketServer({ noServer: true, maxPayload: env.chat.maxPayloadBytes });
  const uninstallUpgradeRouter = installUpgradeRouter({
    server,
    controlPath: env.wsPath,
    controlWss: wss,
    chatWss,
    authorizeChat: authorizeChatUpgrade,
  });
  chatWss.on("connection", (socket, req) => {
    socket.on("error", () => {
      // ws emits transport errors (including maxPayload) before the close event.
    });
    const reserved = reservedChatTicketsByUpgrade.get(req);
    reservedChatTicketsByUpgrade.delete(req);
    if (!reserved) {
      socket.terminate();
      return;
    }
    try {
      chatGateway.accept(socket, reserved);
    } catch {
      socket.terminate();
    }
  });

  wss.on("connection", (socket, req) => {
    socket.on("error", () => {
      // The close handler owns cleanup after transport-level failures.
    });
    try {
      const requestUrl = new URL(req.url ?? env.wsPath, `http://${req.headers.host ?? "127.0.0.1"}`);
      const roomId = requiredQuery(requestUrl, "roomId");
      const controlChannelId = requiredQuery(requestUrl, "controlChannelId");
      const role = requiredQuery(requestUrl, "role");

      if (role !== "host" && role !== "client") {
        throw new InputError("role 必须为 host 或 client。");
      }

      if (role === "host") {
        store.validateHostControl(
          roomId,
          controlChannelId,
          requiredQuery(requestUrl, "token"),
        );
      } else {
        store.validateClientControl(roomId, controlChannelId, requiredQuery(requestUrl, "ticketId"));
      }
      const roomContext = store.getRoomChatContext(roomId);
      if (!roomContext) {
        throw new InputError("房间会话不存在或已过期。");
      }

      const peer: ControlPeer = {
        socket,
        connectionSessionId: randomUUID(),
        roomId,
        roomSessionId: roomContext.roomSessionId,
        controlChannelId,
        role,
        lastSeenAt: Date.now(),
        ...(role === "client" ? { ticketId: requiredQuery(requestUrl, "ticketId") } : {}),
      };

      roomChatGateway.registerPeer({
        connectionSessionId: peer.connectionSessionId,
        clientIp: resolveChatClientIp(req),
        roomId: peer.roomId,
        roomSessionId: peer.roomSessionId,
        controlChannelId: peer.controlChannelId,
        role: peer.role,
        ...(peer.ticketId === undefined
          ? {}
          : { authenticatedTicketId: peer.ticketId }),
        send: (frame) => sendJson(peer.socket, frame),
        close: (code, reason) => peer.socket.close(code, reason),
      });
      addPeer(peer);
      sendJson(socket, {
        type: "connected",
        roomId,
        controlChannelId,
        role,
      });

      if (role === "client") {
        const settings = store.getRoomSettings(roomId);
        sendJson(socket, { type: "room_settings", roomId, ...settings });
      }

      socket.on("message", (payload) => {
        try {
          peer.lastSeenAt = Date.now();
          if (peer.role === "host") {
            store.touchHostSession(peer.roomId);
          }

          const parsed = parseEnvelope(payload);
          if (!parsed) {
            sendJson(socket, {
              type: "error",
              message: "无法解析控制通道消息。",
            });
            return;
          }

          if (parsed.type === "ping") {
            sendJson(socket, {
              type: "pong",
              roomId: peer.roomId,
              controlChannelId: peer.controlChannelId,
            });
            return;
          }

          if (parsed.type === "pong") {
            return;
          }

          if (roomChatGateway.handleControlEnvelope(
            peer.connectionSessionId,
            parsed,
            payload.toString(),
          )) {
            const identity = roomChatGateway.getLockedIdentity(peer.connectionSessionId);
            if (identity) {
              peer.playerNetId = identity.playerNetId;
              peer.playerName = identity.playerName;
            }
            return;
          }

          if (parsed.type === "kick_player" && peer.role === "host") {
            const targetNetId = String(parsed.targetPlayerNetId ?? "");
            if (!targetNetId) return;
            const hostToken = requiredQuery(requestUrl, "token");
            store.kickPlayer(peer.roomId, hostToken, targetNetId);
            const targetPeer = findPeerByNetId(peer.roomId, targetNetId);
            if (targetPeer) {
              sendJson(targetPeer.socket, {
                type: "kicked",
                roomId: peer.roomId,
                reason: "host_kick",
                message: "你已被房主移出房间。",
              });
              targetPeer.socket.close(4001, "kicked");
            }
            broadcastToRoom(peer, {
              type: "player_kicked",
              roomId: peer.roomId,
              playerNetId: targetNetId,
              playerName: String(parsed.targetPlayerName ?? ""),
            });
            console.log(`[control] kick_player roomId=${peer.roomId} targetNetId=${targetNetId} found=${!!targetPeer}`);
            return;
          }

          if (parsed.type === "room_settings" && peer.role === "host") {
            const hostToken = requiredQuery(requestUrl, "token");
            store.updateRoomSettings(peer.roomId, hostToken, {
              chatEnabled: parsed.chatEnabled !== false,
            });
            const allPeers = roomPeers.get(peer.roomId);
            if (allPeers) {
              for (const p of allPeers) {
                if (p.socket.readyState === p.socket.OPEN) {
                  sendJson(p.socket, {
                    type: "room_settings",
                    roomId: peer.roomId,
                    chatEnabled: parsed.chatEnabled !== false,
                  });
                }
              }
            }
            console.log(`[control] room_settings roomId=${peer.roomId} chatEnabled=${parsed.chatEnabled !== false}`);
            return;
          }

          broadcastToRoom(peer, {
            ...parsed,
            roomId: peer.roomId,
            controlChannelId: peer.controlChannelId,
            role: peer.role,
          });
        } catch (error) {
          const lobbyError = error instanceof LobbyStoreError ? `${error.code}: ${error.message}` : null;
          console.warn(
            `[control] peer message ignored roomId=${peer.roomId} role=${peer.role} reason=${lobbyError ?? "unexpected_error"}`,
          );
          const closeReason = error instanceof Error ? error.message : "control_message_error";
          socket.close(1008, closeReason);
        }
      });

      socket.on("close", () => {
        removePeer(peer);
      });
    } catch (error) {
      const message = error instanceof Error ? error.message : "invalid control channel request";
      socket.close(1008, message);
    }
  });


  async function startPeerRuntime(): Promise<void> {
    if (!(peerEnv.enabled && peerEnv.selfAddress)) {
      console.log("[peer] disabled (set PEER_SELF_ADDRESS to enable)");
      return;
    }

    const identity = await loadOrCreateIdentity(peerEnv.stateDir);
    peerStore = new PeerStore(join(peerEnv.stateDir, "peers.json"));
    await peerStore.load();

    // Resolved at request time so admin panel edits propagate live without a
    // restart. PEER_DISPLAY_NAME wins if set, else the admin-panel name, else a
    // host-based fallback so the field is never empty.
    const resolvePeerDisplayName = (): string => {
      if (peerEnv.displayNameOverride) return peerEnv.displayNameOverride;
      const adminName = serverAdminStateStore.getState().displayName.trim();
      if (adminName) return adminName;
      try {
        const url = new URL(peerEnv.selfAddress);
        return `社区服务器 ${url.host}`;
      } catch {
        return "社区服务器";
      }
    };

    // Self-entry — without this, the local /peers list never returns this node,
    // so the CF aggregator and other peers can't learn this server's displayName
    // from us. Mark it as `source: "self"` so it survives gossip churn.
    const nowIso = new Date().toISOString();
    await peerStore.upsert({
      address: peerEnv.selfAddress,
      publicKey: identity.publicKey,
      displayName: resolvePeerDisplayName(),
      firstSeen: nowIso,
      lastSeen: nowIso,
      consecutiveProbeFailures: 0,
      status: "active",
      source: "self",
    });

    // Resolved at request time so the admin-panel toggle propagates without a
    // restart. When false, /peers/health still answers (direct-IP joins keep
    // working) but the field tells the CF aggregator to skip this node.
    const resolvePublicListing = (): boolean => serverAdminStateStore.getState().publicListingEnabled;

    mountHealth(app, {
      identity,
      address: peerEnv.selfAddress,
      getDisplayName: resolvePeerDisplayName,
      getPublicListing: resolvePublicListing,
    });
    mountList(app, { store: peerStore });
    mountAnnounce(app, { store: peerStore });
    mountHeartbeat(app, { store: peerStore });
    mountMetrics(app, {
      identity,
      address: peerEnv.selfAddress,
      getDisplayName: resolvePeerDisplayName,
      getPublicListing: resolvePublicListing,
      getModSyncCapability: () => ({
        protocolVersion: MOD_SYNC_PROTOCOL_VERSION,
        enabled: serverAdminStateStore.getState().modSyncEnabled,
        minimumClientVersion: MOD_SYNC_MINIMUM_CLIENT_VERSION,
      }),
      getSnapshot: () => {
        const guard = getCreateRoomGuardSnapshot();
        return {
          rooms: store.listRooms().length,
          currentBandwidthMbps: guard.currentBandwidthMbps,
          bandwidthCapacityMbps: guard.bandwidthCapacityMbps,
          resolvedCapacityMbps: guard.resolvedCapacityMbps,
          bandwidthUtilizationRatio: guard.bandwidthUtilizationRatio,
          capacitySource: guard.capacitySource,
          createRoomGuardApplies: guard.createRoomGuardApplies,
          createRoomGuardStatus: guard.createRoomGuardStatus,
        };
      },
    });

    // Bootstrap + auto-announce only after listen succeeds (caller ensures that).
    const cfDiscoveryBaseUrl = peerEnv.cfDiscoveryBaseUrl;
    if (cfDiscoveryBaseUrl) {
      void (async () => {
        try {
          const seeds = await loadSeedsFromCf(cfDiscoveryBaseUrl);
          await bootstrapPeers({ store: requirePeerStore(), selfAddress: peerEnv.selfAddress, seeds });
          // Operator opt-in: only announce self to the network when this node
          // is configured for public visibility. Private nodes still load
          // seeds (so admins can see the network from their dashboard) but
          // do not advertise themselves outward.
          if (!resolvePublicListing()) {
            console.log("[peer] auto-announce skipped (publicListingEnabled=false)");
            return;
          }
          // After bootstrap, push self into every probed peer's announce endpoint
          // so a brand-new lobby becomes discoverable without manual KV ops.
          // Without this, gossip can't propagate self outward (heartbeat only
          // refreshes already-known peers) and the CF aggregator never learns
          // new servers.
          const announceTargets = requirePeerStore().list().filter((p) => p.address !== peerEnv.selfAddress).length;
          if (announceTargets > 0) {
            await announceToBootstrappedPeers({
              store: requirePeerStore(),
              selfAddress: peerEnv.selfAddress,
              selfPublicKey: identity.publicKey,
              selfDisplayName: resolvePeerDisplayName(),
            });
            console.log(`[peer] announced self to ${announceTargets} bootstrapped peer(s)`);
          }
        } catch (err) {
          console.error("[peer] post-listen bootstrap/announce failed:", err);
        }
      })();
    }

    gossipScheduler = new GossipScheduler({
      store: requirePeerStore(),
      selfAddress: peerEnv.selfAddress,
      selfPublicKey: identity.publicKey,
      seedAddresses: [],
      postHeartbeat: async (addr, body) => {
        await fetch(`${addr.replace(/\/+$/, "")}/peers/heartbeat`, {
          method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
        });
      },
    });
    gossipScheduler.start();
    console.log(
      `[peer] mounted; self=${peerEnv.selfAddress} displayName="${resolvePeerDisplayName()}" cf=${peerEnv.cfDiscoveryBaseUrl || "(none)"}`,
    );

    // Refresh the self-entry's displayName periodically so admin panel edits
    // propagate without needing a restart. The /peers/health response already
    // resolves live, but the local /peers list cache needs an explicit poke.
    selfEntryRefreshInterval = setInterval(() => {
      const fresh = resolvePeerDisplayName();
      const existing = requirePeerStore().get(peerEnv.selfAddress);
      if (!existing || existing.displayName === fresh) return;
      void requirePeerStore().upsert({ ...existing, displayName: fresh, lastSeen: new Date().toISOString() });
    }, 60_000);
    selfEntryRefreshInterval.unref();
  }

  async function start(): Promise<{ host: string; port: number }> {
    if (closed) {
      throw new Error("lobby service has been closed");
    }
    if (started && boundAddress) {
      return boundAddress;
    }

    // Avoid overlapping start attempts while recovering from a prior failure.
    if (server.listening) {
      throw new Error("lobby service start is incomplete; call close() or retry after recovery");
    }

    let listenSucceeded = false;
    try {
      await new Promise<void>((resolve, reject) => {
        const onError = (error: Error) => {
          server.off("listening", onListening);
          reject(error);
        };
        const onListening = () => {
          server.off("error", onError);
          resolve();
        };
        server.once("error", onError);
        server.once("listening", onListening);
        server.listen(env.port, env.host);
      });
      listenSucceeded = true;

      const addressInfo = server.address();
      if (!addressInfo || typeof addressInfo === "string") {
        throw new Error("failed to resolve listening address");
      }
      boundAddress = {
        host: addressInfo.address,
        port: addressInfo.port,
      };

      cleanupInterval = setInterval(() => {
        cleanupExpiredRoomsNow();
        cleanupExpiredServerAdminSessions();

        const staleThreshold = Date.now() - env.heartbeatTimeoutMs;
        for (const peers of roomPeers.values()) {
          for (const peer of peers) {
            if (peer.lastSeenAt < staleThreshold) {
              peer.socket.close(1001, "peer_timeout");
            } else {
              sendJson(peer.socket, {
                type: "ping",
                roomId: peer.roomId,
                controlChannelId: peer.controlChannelId,
              });
            }
          }
        }
      }, 5000);
      cleanupInterval.unref();

      relayManager.start();
      await startPeerRuntime();

      started = true;
      return boundAddress;
    } catch (error) {
      // Post-listen (or mid-start) failure must not leave a half-started service
      // that reports success on the next start() while still holding the port.
      if (listenSucceeded || server.listening || cleanupInterval || selfEntryRefreshInterval || gossipScheduler) {
        await abortFailedStart();
      }
      throw error;
    }
  }

  async function abortFailedStart(): Promise<void> {
    started = false;
    boundAddress = null;

    if (cleanupInterval) {
      clearInterval(cleanupInterval);
      cleanupInterval = null;
    }
    if (selfEntryRefreshInterval) {
      clearInterval(selfEntryRefreshInterval);
      selfEntryRefreshInterval = null;
    }
    if (gossipScheduler) {
      gossipScheduler.stop();
      gossipScheduler = null;
    }

    peerStore = null;

    try {
      relayManager.close();
    } catch {
      // best-effort cleanup after partial start
    }

    await closeHttpServerAndConnections();
  }

  async function closeHttpServerAndConnections(): Promise<void> {
    // Keep-alive / idle sockets prevent server.close() from finishing; force them
    // closed when the runtime supports it (Node 18.2+).
    const httpServerWithForceClose = server as HttpServer & {
      closeAllConnections?: () => void;
      closeIdleConnections?: () => void;
    };
    try {
      httpServerWithForceClose.closeIdleConnections?.();
    } catch {
      // ignore
    }
    try {
      httpServerWithForceClose.closeAllConnections?.();
    } catch {
      // ignore
    }

    if (!server.listening) {
      return;
    }

    await new Promise<void>((resolve, reject) => {
      server.close((error) => {
        if (error) {
          reject(error);
          return;
        }
        resolve();
      });
    });
  }

  async function close(): Promise<void> {
    if (closed) {
      return;
    }
    closed = true;
    started = false;
    boundAddress = null;

    if (cleanupInterval) {
      clearInterval(cleanupInterval);
      cleanupInterval = null;
    }
    if (selfEntryRefreshInterval) {
      clearInterval(selfEntryRefreshInterval);
      selfEntryRefreshInterval = null;
    }
    if (gossipScheduler) {
      gossipScheduler.stop();
      gossipScheduler = null;
    }

    // Force-terminate active control-channel peers so wss.close()/http close
    // cannot hang waiting for clients that never disconnect.
    for (const peers of roomPeers.values()) {
      for (const peer of peers) {
        try {
          peer.socket.terminate();
        } catch {
          // ignore socket termination races
        }
      }
    }
    roomPeers.clear();
    roomChatGateway.close();

    uninstallUpgradeRouter();

    await chatGateway.close();

    for (const client of wss.clients) {
      try {
        client.terminate();
      } catch {
        // ignore socket termination races
      }
    }
    for (const client of chatWss.clients) {
      try {
        client.terminate();
      } catch {
        // ignore socket termination races
      }
    }

    await new Promise<void>((resolve) => {
      wss.close(() => resolve());
    });
    await new Promise<void>((resolve) => {
      chatWss.close(() => resolve());
    });

    relayManager.close();

    await closeHttpServerAndConnections();
  }

  function addPeer(peer: ControlPeer) {
    let peers = roomPeers.get(peer.roomId);
    if (!peers) {
      peers = new Set();
      roomPeers.set(peer.roomId, peers);
    }

    peers.add(peer);
  }

  function removePeer(peer: ControlPeer) {
    roomChatGateway.unregisterPeer(peer.connectionSessionId);
    const peers = roomPeers.get(peer.roomId);
    if (!peers) {
      return;
    }

    peers.delete(peer);
    if (peers.size === 0) {
      roomPeers.delete(peer.roomId);
    }
  }

  function closeRoomSockets(roomId: string, code: number, reason: string) {
    const peers = roomPeers.get(roomId);
    if (!peers) {
      return;
    }

    for (const peer of peers) {
      roomChatGateway.unregisterPeer(peer.connectionSessionId);
      peer.socket.close(code, reason);
    }

    roomPeers.delete(roomId);
  }

  function findPeerByNetId(roomId: string, playerNetId: string): ControlPeer | undefined {
    const peers = roomPeers.get(roomId);
    if (!peers) return undefined;
    for (const peer of peers) {
      if (peer.playerNetId === playerNetId) return peer;
    }
    return undefined;
  }

  function cleanupExpiredRoomsNow(now = new Date()) {
    const deletedRoomIds = cleanupExpiredRooms({
      cleanupExpired: (cleanupNow) => store.cleanupExpired(cleanupNow),
      removeRelayRoom: (roomId) => relayManager.removeRoom(roomId),
      closeRoomSockets,
      log: (message) => console.log(message),
    }, now);
    return deletedRoomIds;
  }

  function broadcastToRoom(sender: ControlPeer, envelope: Record<string, unknown>) {
    const peers = roomPeers.get(sender.roomId);
    if (!peers) {
      return;
    }

    for (const peer of peers) {
      if (peer === sender || peer.socket.readyState !== peer.socket.OPEN) {
        continue;
      }

      sendJson(peer.socket, envelope);
    }
  }

  function sendJson(socket: WebSocket, payload: Record<string, unknown>) {
    if (socket.readyState === socket.OPEN) {
      socket.send(JSON.stringify(payload));
    }
  }

  function parseEnvelope(payload: WebSocket.RawData): Record<string, unknown> | null {
    try {
      const text = Buffer.isBuffer(payload) ? payload.toString("utf8") : String(payload);
      const parsed = JSON.parse(text);
      return parsed && typeof parsed === "object" ? parsed as Record<string, unknown> : null;
    } catch {
      return null;
    }
  }

  function requestIp(req: Request) {
    return normalizeIp(req.socket.remoteAddress ?? "");
  }

  function asClientIpRequest(req: Request | IncomingMessage): ClientIpRequest {
    return {
      socket: { remoteAddress: req.socket.remoteAddress },
      headers: req.headers as Record<string, string | string[] | undefined>,
    };
  }

  function resolveChatClientIp(req: Request | IncomingMessage): string {
    return resolveClientIp(asClientIpRequest(req), env.chat.trustedProxyCidrs);
  }

  function buildChatWebSocketUrl(req: Request): string {
    const hostHeader = req.headers.host?.trim();
    const host = hostHeader && hostHeader.length > 0 ? hostHeader : "127.0.0.1";
    const remoteIp = requestIp(req);
    const trustedProxy = remoteIp.length > 0
      && env.chat.trustedProxyCidrs.some((candidate) => ipMatchesCidr(remoteIp, candidate));
    const forwardedProtoHeader = req.headers["x-forwarded-proto"];
    const forwardedProto = (Array.isArray(forwardedProtoHeader)
      ? forwardedProtoHeader[0]
      : forwardedProtoHeader)
      ?.split(",", 1)[0]
      ?.trim()
      .toLowerCase();
    const secure = req.secure || (trustedProxy && forwardedProto === "https");
    return `${secure ? "wss" : "ws"}://${host}/chat`;
  }

  function extractBearerToken(authorization: string | string[] | undefined): string | undefined {
    const value = Array.isArray(authorization) ? authorization[0] : authorization;
    if (typeof value !== "string") {
      return undefined;
    }
    const match = /^Bearer\s+(\S+)$/i.exec(value.trim());
    return match?.[1];
  }

  function assertLobbyAccessForChat(req: Request) {
    if (!env.enforceLobbyAccessToken) {
      return;
    }

    if (!env.chatAccessToken) {
      throw new HttpError(
        503,
        "lobby_access_token_not_configured",
        "服务端已开启大厅访问令牌校验，但未配置访问令牌。",
      );
    }

    const lobbyHeader = req.headers["x-lobby-access-token"];
    const headerToken = (Array.isArray(lobbyHeader) ? lobbyHeader[0] : lobbyHeader)?.trim();
    const providedToken = headerToken || extractBearerToken(req.headers.authorization);
    // CREATE_ROOM_TOKEN (x-create-room-token) never authorizes chat tickets.
    if (!providedToken || providedToken !== env.chatAccessToken) {
      throw new HttpError(401, "lobby_access_forbidden", "需要有效的大厅访问令牌。");
    }
  }

  function authorizeChatUpgrade(req: IncomingMessage): ChatUpgradeDecision {
    const clientIp = resolveChatClientIp(req);
    if (!clientIp) {
      return { ok: false, statusCode: 401 };
    }

    const ticket = extractBearerToken(req.headers.authorization);
    if (!ticket) {
      return { ok: false, statusCode: 401 };
    }

    try {
      chatPeerRegistry.assertCapacity(clientIp);
    } catch (error) {
      if (error instanceof ChatPeerError) {
        return { ok: false, statusCode: 503 };
      }
      throw error;
    }

    let reserved: ReservedChatTicket;
    try {
      reserved = chatTicketStore.reserve(ticket, clientIp, ChatProtocolVersion);
    } catch (error) {
      if (error instanceof ChatTicketError) {
        if (error.code === "server_busy") {
          return { ok: false, statusCode: 503 };
        }
        return { ok: false, statusCode: 401 };
      }
      throw error;
    }

    reservedChatTicketsByUpgrade.set(req, reserved);
    return {
      ok: true,
      commit: () => {
        chatTicketStore.commit(reserved.id);
      },
      release: () => {
        reservedChatTicketsByUpgrade.delete(req);
        chatTicketStore.release(reserved.id);
      },
    };
  }

  function parseCookies(header: string | undefined) {
    const cookies: Record<string, string> = {};
    if (!header) {
      return cookies;
    }

    for (const segment of header.split(";")) {
      const separatorIndex = segment.indexOf("=");
      if (separatorIndex <= 0) {
        continue;
      }

      const key = segment.slice(0, separatorIndex).trim();
      const value = segment.slice(separatorIndex + 1).trim();
      if (key) {
        cookies[key] = value;
      }
    }

    return cookies;
  }

  function isServerAdminConfigured() {
    return Boolean(env.serverAdminPasswordHash && env.serverAdminSessionSecret);
  }

  function ensureServerAdminConfigured() {
    if (!isServerAdminConfigured()) {
      throw new HttpError(503, "server_admin_not_configured", "子面板账号尚未配置。");
    }
  }

  function createServerAdminSession(username: string) {
    const csrfToken = createServerAdminCsrfToken();
    const session: ServerAdminSession = {
      id: `session_${randomUUID()}`,
      username,
      expiresAt: Date.now() + env.serverAdminSessionTtlMs,
      csrfToken,
      csrfDigest: digestServerAdminCsrfToken(csrfToken),
    };
    serverAdminSessions.set(session.id, session);
    return { session, csrfToken };
  }

  function cleanupExpiredServerAdminSessions(now = Date.now()) {
    for (const [sessionId, session] of serverAdminSessions.entries()) {
      if (session.expiresAt <= now) {
        serverAdminSessions.delete(sessionId);
      }
    }
  }

  function requireServerAdminSession(req: Request) {
    ensureServerAdminConfigured();
    cleanupExpiredServerAdminSessions();
    const cookies = parseCookies(req.headers.cookie);
    const sessionId = verifySignedServerAdminSession(cookies.sts2_server_admin_session, env.serverAdminSessionSecret!);
    if (!sessionId) {
      throw new HttpError(401, "server_admin_auth_required", "请先登录子面板。");
    }

    const session = serverAdminSessions.get(sessionId);
    if (!session || session.expiresAt <= Date.now()) {
      serverAdminSessions.delete(sessionId);
      throw new HttpError(401, "server_admin_auth_required", "请先登录子面板。");
    }

    return session;
  }

  function requireServerAdminCsrf(req: Request, session: ServerAdminSession): void {
    const header = req.headers["x-csrf-token"];
    const token = Array.isArray(header) ? undefined : header;
    if (!verifyServerAdminCsrfToken(token, session.csrfDigest)) {
      throw new HttpError(403, "server_admin_csrf_invalid", "安全令牌无效，请重新登录。");
    }
  }

  function setServerAdminCookie(req: Request, res: Response, sessionId: string) {
    const token = signServerAdminSession(sessionId, env.serverAdminSessionSecret!);
    res.setHeader("Set-Cookie", `sts2_server_admin_session=${token}; ${serverAdminCookieAttributes(req, Math.floor(env.serverAdminSessionTtlMs / 1000))}`);
  }

  function clearServerAdminCookie(req: Request, res: Response) {
    res.setHeader("Set-Cookie", `sts2_server_admin_session=; ${serverAdminCookieAttributes(req, 0)}`);
  }

  function serverAdminCookieAttributes(req: Request, maxAge: number): string {
    return `Path=/; HttpOnly; SameSite=Lax; Max-Age=${maxAge}${isReliableHttpsRequest(req) ? "; Secure" : ""}`;
  }

  function isReliableHttpsRequest(req: Request): boolean {
    if (req.secure) return true;
    const remoteIp = requestIp(req);
    const trustedProxy = remoteIp.length > 0
      && env.chat.trustedProxyCidrs.some((candidate) => ipMatchesCidr(remoteIp, candidate));
    if (!trustedProxy) return false;
    const header = req.headers["x-forwarded-proto"];
    const value = (Array.isArray(header) ? header[0] : header)
      ?.split(",", 1)[0]
      ?.trim()
      .toLowerCase();
    return value === "https";
  }

  function resolveAdvertisedRelayHost(req: Request) {
    if (env.relayPublicHost.trim()) {
      return env.relayPublicHost.trim();
    }

    const hostHeader = req.headers.host?.trim();
    if (!hostHeader) {
      return "127.0.0.1";
    }

    try {
      return new URL(`http://${hostHeader}`).hostname;
    } catch {
      return hostHeader.replace(/:\d+$/, "");
    }
  }

  function buildServerAdminSettingsResponse(settings = serverAdminStateStore.getSettingsView()) {
    const guardSnapshot = getCreateRoomGuardSnapshot();
    const relayTrafficSnapshot = relayManager.getTrafficSnapshot();
    return {
      ...settings,
      peerNetworkEnabled: peerEnv.enabled,
      peerRuntimeState: getPeerRuntimeState(settings.publicListingEnabled),
      currentBandwidthMbps: guardSnapshot.currentBandwidthMbps,
      resolvedCapacityMbps: guardSnapshot.resolvedCapacityMbps,
      bandwidthUtilizationRatio: guardSnapshot.bandwidthUtilizationRatio,
      capacitySource: guardSnapshot.capacitySource,
      createRoomGuardStatus: guardSnapshot.createRoomGuardStatus,
      createRoomGuardApplies: guardSnapshot.createRoomGuardApplies,
      createRoomThresholdRatio: guardSnapshot.createRoomThresholdRatio,
      relayTrafficWindowMs: relayTrafficSnapshot.windowMs,
      relayTrafficBytesInWindow: relayTrafficSnapshot.totalBytesInWindow,
      relayActiveRooms: relayTrafficSnapshot.activeRooms,
      relayActiveHosts: relayTrafficSnapshot.activeHosts,
      relayActiveClients: relayTrafficSnapshot.activeClients,
      serverFeatures: resolveAdminFeatures(settings.chatFeatures, "server"),
      roomFeatures: resolveAdminFeatures(settings.chatFeatures, "room"),
      metrics: buildChatMetrics(),
    };
  }

  function resolveAdminFeatures(
    governance: ChatFeatureGovernance,
    channel: "server" | "room",
  ) {
    return resolveEnabledFeatures({
      channel,
      compiled: PHASE_4_CHAT_FEATURES,
      configured: governanceToFeatureVersions(env.chat.features, channel),
      admin: governanceToFeatureVersions(governance, channel),
      channelEnabled: channel === "server" ? governance.serverChatEnabled : true,
      roomV2Enabled: governance.roomChatV2Enabled,
    });
  }

  function buildChatMetrics() {
    const server = chatGateway.getMetrics();
    const room = roomChatGateway.getMetrics();
    return {
      serverConnectionCount: server.connectionCount,
      roomConnectionCount: room.connectionCount,
      serverRetainedHistoryCount: server.retainedHistoryCount,
      historyEpoch: server.historyEpoch,
      serverAcceptedMessages: server.acceptedMessages,
      serverRejectedMessages: server.rejectedMessages,
      roomAcceptedMessages: room.acceptedMessages,
      roomRejectedMessages: room.rejectedMessages,
    };
  }

  async function applyChatGovernance(governance: ChatFeatureGovernance) {
    const transitions = await Promise.allSettled([
      chatGateway.setGovernance({
        chatEnabled: governance.serverChatEnabled,
        configuredFeatures: governanceToFeatureVersions(env.chat.features, "server"),
        adminFeatures: governanceToFeatureVersions(governance, "server"),
      }),
      roomChatGateway.setGovernance({
        configuredFeatures: governanceToFeatureVersions(env.chat.features, "room"),
        adminFeatures: governanceToFeatureVersions(governance, "room"),
        roomV2Enabled: governance.roomChatV2Enabled,
      }),
    ]);
    const failure = transitions.find(
      (transition): transition is PromiseRejectedResult => transition.status === "rejected",
    );
    if (failure) throw failure.reason;
  }

  function enqueueServerAdminMutation<T>(mutation: () => T | Promise<T>): Promise<T> {
    const result = serverAdminMutations.then(mutation);
    serverAdminMutations = result.then(() => undefined, () => undefined);
    return result;
  }

  function parseChatFeaturesPatch(value: unknown): Partial<ChatFeatureGovernance> {
    if (!value || typeof value !== "object" || Array.isArray(value)) {
      throw new InputError("chatFeatures 必须为对象。");
    }
    const allowed = new Set<keyof ChatFeatureGovernance>([
      "serverChatEnabled",
      "richContentEnabled",
      "emojiEnabled",
      "itemRefsEnabled",
      "roomChatV2Enabled",
      "roomCombatRefsEnabled",
    ]);
    const output: Partial<ChatFeatureGovernance> = {};
    for (const [key, entry] of Object.entries(value)) {
      if (!allowed.has(key as keyof ChatFeatureGovernance) || typeof entry !== "boolean") {
        throw new InputError(`chatFeatures.${key} 必须为受支持的布尔开关。`);
      }
      output[key as keyof ChatFeatureGovernance] = entry;
    }
    return output;
  }

  function getPeerRuntimeState(publicListingEnabled: boolean): PeerRuntimeState {
    if (!peerEnv.enabled) {
      return "disabled";
    }

    const normalizedSelfAddress = normalizePeerAddressForRuntimeComparison(peerEnv.selfAddress);
    if (!normalizedSelfAddress) {
      return "unconfigured";
    }

    if (!publicListingEnabled) {
      return "private";
    }

    const hasExternalActivePeer = peerStore?.list().some((peer) => {
      return normalizePeerAddressForRuntimeComparison(peer.address) !== normalizedSelfAddress && peer.status === "active";
    }) ?? false;
    return hasExternalActivePeer ? "joined" : "joining";
  }

  function normalizePeerAddressForRuntimeComparison(address: string): string {
    const trimmed = address.trim();
    if (!trimmed) {
      return "";
    }

    try {
      const url = new URL(trimmed);
      url.hash = "";
      url.search = "";
      url.pathname = url.pathname.replace(/\/+$/, "") || "/";
      return url.toString();
    } catch {
      return trimmed.replace(/\/+$/, "");
    }
  }

  function getCreateRoomGuardSnapshot() {
    const state = serverAdminStateStore.getState();
    const relayTrafficSnapshot = relayManager.getTrafficSnapshot();
    return createRoomBandwidthGuard.getSnapshot({
      currentBandwidthMbps: relayTrafficSnapshot.currentBandwidthMbps,
      bandwidthCapacityMbps: state.bandwidthCapacityMbps,
      probePeak7dCapacityMbps: state.probePeak7dCapacityMbps,
      // The bandwidth-saturation guard is a courtesy for operators who opted
      // their node into the public list — it stops them from getting flooded
      // by random joiners when relay traffic is near capacity. Private nodes
      // are only joined by people who already know the address, so the guard
      // would just get in their way.
      createRoomGuardApplies: state.publicListingEnabled,
    });
  }

  function requiredString(value: unknown, name: string) {
    if (typeof value !== "string" || value.trim() === "") {
      throw new InputError(`${name} 不能为空。`);
    }

    return value.trim();
  }

  function boundedString(value: unknown, name: string, maxLength: number) {
    const normalized = requiredString(value, name);
    if (normalized.length > maxLength) {
      throw new InputError(`${name} 长度不能超过 ${maxLength} 个字符。`);
    }

    return normalized;
  }

  function optionalBoundedString(value: unknown, name: string, maxLength: number) {
    const normalized = optionalString(value);
    if (normalized == null) {
      return undefined;
    }

    if (normalized.length > maxLength) {
      throw new InputError(`${name} 长度不能超过 ${maxLength} 个字符。`);
    }

    return normalized;
  }

  function assertCreateRoomAuthorized(req: Request) {
    if (isTrustedOperationsRequest(req)) {
      return;
    }

    if (!env.enforceCreateRoomToken) {
      return;
    }

    if (!env.createRoomToken) {
      throw new HttpError(503, "create_room_token_not_configured", "服务端已开启建房令牌校验，但未配置建房访问令牌。请联系管理员；如需兼容老版本客户端，可将 ENFORCE_CREATE_ROOM_TOKEN=false。");
    }

    const providedToken = getCreateRoomToken(req)
      ?? optionalString((req.body as { createRoomToken?: string } | undefined)?.createRoomToken);

    if (!providedToken || providedToken !== env.createRoomToken) {
      throw new HttpError(403, "create_room_forbidden", "当前服务器要求建房令牌。请通过受信代理或携带建房令牌访问；如需兼容老版本客户端，可由管理员关闭 ENFORCE_CREATE_ROOM_TOKEN。");
    }
  }

  function hasLobbyReadAccessToken(req: Request) {
    if (!env.enforceLobbyAccessToken) {
      return true;
    }

    if (!env.lobbyAccessToken) {
      return false;
    }

    const providedToken = getLobbyAccessToken(req);
    return providedToken === env.lobbyAccessToken;
  }

  function isTrustedOperationsRequest(req: Request) {
    const remote = requestIp(req);
    if (!remote) {
      return false;
    }

    return env.createRoomTrustedProxies.some((candidate: string) => ipMatchesCidr(remote, candidate));
  }

  function optionalString(value: unknown) {
    if (typeof value !== "string") {
      return undefined;
    }

    const trimmed = value.trim();
    return trimmed === "" ? undefined : trimmed;
  }

  function optionalStringArray(value: unknown) {
    if (!Array.isArray(value)) {
      return undefined;
    }

    return value.filter((entry): entry is string => typeof entry === "string").map((entry) => entry.trim());
  }

  function boundedStringArray(value: unknown, name: string, maxEntries: number, maxEntryLength: number) {
    const normalized = optionalStringArray(value) ?? [];
    if (normalized.length > maxEntries) {
      throw new InputError(`${name} 数量不能超过 ${maxEntries} 个。`);
    }

    for (const [index, entry] of normalized.entries()) {
      if (!entry) {
        throw new InputError(`${name}[${index}] 不能为空。`);
      }
      if (entry.length > maxEntryLength) {
        throw new InputError(`${name}[${index}] 长度不能超过 ${maxEntryLength} 个字符。`);
      }
    }

    return normalized;
  }

  function parseModInventory(value: unknown, name: string): LobbyModDescriptor[] {
    try {
      return validateModInventory(value, {
        maxDescriptors: env.modSyncMaxDescriptors,
        maxPayloadBytes: env.modSyncMaxPayloadBytes,
      });
    } catch (error) {
      if (error instanceof ModSyncValidationError) {
        throw new InputError(`${name} 非法：${error.message}`);
      }
      throw error;
    }
  }

  function assertCreateJoinRateLimit(req: Request, scope: string) {
    const ip = requestIp(req) || "unknown";
    const windowMs = Math.max(1000, env.createJoinRateLimitWindowMs);
    const maxRequests = Math.max(1, env.createJoinRateLimitMaxRequests);

    if (consumeCreateJoinRateLimit(createJoinRateLimitHits, scope, ip, windowMs, maxRequests)) {
      throw new HttpError(429, "rate_limited", "请求过于频繁，请稍后再试。");
    }
  }

  function toRoomListView(room: ReturnType<LobbyStore["listRooms"]>[number], includeSensitiveSavedRun: boolean) {
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
      protocolProfile: room.protocolProfile,
      relayState: room.relayState,
      createdAt: room.createdAt,
      lastHeartbeatAt: room.lastHeartbeatAt,
      savedRun: room.savedRun
        ? {
            slots: room.savedRun.slots.map((slot) => ({
              netId: slot.netId,
              characterId: slot.characterId,
              characterName: slot.characterName,
              playerName: includeSensitiveSavedRun ? slot.playerName : "",
              isHost: slot.isHost,
              isConnected: slot.isConnected,
            })),
            ...(includeSensitiveSavedRun
              ? {
                  saveKey: room.savedRun.saveKey,
                  connectedPlayerNetIds: room.savedRun.connectedPlayerNetIds,
                }
              : {}),
          }
        : undefined,
    };
  }

  function positiveInt(value: unknown, name: string, min: number, max: number) {
    if (typeof value !== "number" || !Number.isInteger(value) || value < min || value > max) {
      throw new InputError(`${name} 必须是 ${min}-${max} 之间的整数。`);
    }

    return value;
  }

  function optionalPositiveNumber(value: unknown, name: string, max: number) {
    if (value == null) {
      return null;
    }

    if (typeof value !== "number" || !Number.isFinite(value) || value <= 0 || value > max) {
      throw new InputError(`${name} 必须是 0-${max} 之间的正数，或留空。`);
    }

    return Math.round(value * 100) / 100;
  }

  function requiredQuery(url: URL, key: string) {
    const value = url.searchParams.get(key);
    if (!value || value.trim() === "") {
      throw new InputError(`缺少查询参数 ${key}。`);
    }

    return value.trim();
  }

  return {
    app,
    httpServer: server,
    start,
    close,
  };
}
