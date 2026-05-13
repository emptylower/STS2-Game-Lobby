import { randomUUID } from "node:crypto";
import express, { type NextFunction, type Request, type Response } from "express";
import { createServer } from "node:http";
import { WebSocketServer, type WebSocket } from "ws";
import { CreateRoomBandwidthGuard } from "./bandwidth-guard.js";
import { assertRelayCreateReady, assertRelayJoinReady } from "./join-guard.js";
import { RoomRelayManager } from "./relay.js";
import { cleanupExpiredRooms } from "./room-cleanup.js";
import { signServerAdminSession, verifyServerAdminPassword, verifySignedServerAdminSession } from "./server-admin-auth.js";
import { ServerAdminStateStore } from "./server-admin-state.js";
import { renderServerAdminPage } from "./server-admin-ui.js";
import {
  LobbyStore,
  LobbyStoreError,
  type ConnectionStrategy,
  type CreateRoomInput,
  type HeartbeatInput,
  type JoinRoomInput,
} from "./store.js";
import { join } from "node:path";
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

const env = {
  host: process.env.HOST ?? "0.0.0.0",
  port: Number.parseInt(process.env.PORT ?? "8787", 10),
  heartbeatTimeoutMs: Number.parseInt(process.env.HEARTBEAT_TIMEOUT_SECONDS ?? "35", 10) * 1000,
  ticketTtlMs: Number.parseInt(process.env.TICKET_TTL_SECONDS ?? "120", 10) * 1000,
  wsPath: process.env.WS_PATH ?? "/control",
  relayBindHost: process.env.RELAY_BIND_HOST ?? process.env.HOST ?? "0.0.0.0",
  relayPublicHost: process.env.RELAY_PUBLIC_HOST ?? "",
  relayPortStart: Number.parseInt(process.env.RELAY_PORT_START ?? "39000", 10),
  relayPortEnd: Number.parseInt(process.env.RELAY_PORT_END ?? "39149", 10),
  relayHostIdleMs: Number.parseInt(process.env.RELAY_HOST_IDLE_SECONDS ?? "20", 10) * 1000,
  relayClientIdleMs: Number.parseInt(process.env.RELAY_CLIENT_IDLE_SECONDS ?? "90", 10) * 1000,
  strictGameVersionCheck: parseBooleanEnv(process.env.STRICT_GAME_VERSION_CHECK, true),
  strictModVersionCheck: parseBooleanEnv(process.env.STRICT_MOD_VERSION_CHECK, true),
  connectionStrategy: parseConnectionStrategyEnv(process.env.CONNECTION_STRATEGY),
  publicRoomListEnabled: parseBooleanEnv(process.env.PUBLIC_ROOM_LIST_ENABLED, false),
  publicDetailedHealthEnabled: parseBooleanEnv(process.env.PUBLIC_DETAILED_HEALTH_ENABLED, false),
  enforceLobbyAccessToken: parseBooleanEnv(process.env.ENFORCE_LOBBY_ACCESS_TOKEN, true),
  enforceCreateRoomToken: parseBooleanEnv(process.env.ENFORCE_CREATE_ROOM_TOKEN, true),
  lobbyAccessToken: optionalEnv(process.env.LOBBY_ACCESS_TOKEN) ?? optionalEnv(process.env.CREATE_ROOM_TOKEN),
  createRoomToken: optionalEnv(process.env.CREATE_ROOM_TOKEN) ?? optionalEnv(process.env.LOBBY_ACCESS_TOKEN),
  createRoomTrustedProxies: parseTrustedProxyCidrs(process.env.CREATE_ROOM_TRUSTED_PROXIES),
  createJoinRateLimitWindowMs: Number.parseInt(process.env.CREATE_JOIN_RATE_LIMIT_WINDOW_MS ?? "60000", 10),
  createJoinRateLimitMaxRequests: Number.parseInt(process.env.CREATE_JOIN_RATE_LIMIT_MAX_REQUESTS ?? "30", 10),
  serverAdminUsername: process.env.SERVER_ADMIN_USERNAME ?? "admin",
  serverAdminPasswordHash: optionalEnv(process.env.SERVER_ADMIN_PASSWORD_HASH),
  serverAdminSessionSecret: optionalEnv(process.env.SERVER_ADMIN_SESSION_SECRET),
  serverAdminSessionTtlMs: Number.parseInt(process.env.SERVER_ADMIN_SESSION_TTL_HOURS ?? "168", 10) * 60 * 60 * 1000,
  serverAdminStateFile: process.env.SERVER_ADMIN_STATE_FILE ?? `${process.cwd()}/data/server-admin.json`,
  // Initial value used when the persisted admin state file does not yet
  // contain `publicListingEnabled`. The admin panel toggle is the source of
  // truth at runtime; this only seeds fresh installs.
  peerPublicListingEnabledDefault: parseBooleanEnv(process.env.PEER_PUBLIC_LISTING_ENABLED, true),
};

const peerEnv = {
  enabled: process.env.PEER_NETWORK_ENABLED !== "false",
  selfAddress: process.env.PEER_SELF_ADDRESS ?? "",
  cfDiscoveryBaseUrl: process.env.PEER_CF_DISCOVERY_BASE_URL ?? "",
  stateDir: process.env.PEER_STATE_DIR ?? "./data/peer",
  // Optional override for the public-facing server name. When unset, falls back
  // to the admin-panel-managed displayName, which itself falls back to a host-
  // based label. Resolved per-request so admin panel edits propagate live.
  displayNameOverride: (process.env.PEER_DISPLAY_NAME ?? "").trim(),
};

const store = new LobbyStore({
  heartbeatTimeoutMs: env.heartbeatTimeoutMs,
  ticketTtlMs: env.ticketTtlMs,
  strictGameVersionCheck: env.strictGameVersionCheck,
  strictModVersionCheck: env.strictModVersionCheck,
  connectionStrategy: env.connectionStrategy,
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
});
const createRoomBandwidthGuard = new CreateRoomBandwidthGuard();
const serverAdminSessions = new Map<string, ServerAdminSession>();
const createJoinRateLimitHits = new Map<string, RateLimitBucket>();

const app = express();
app.disable("x-powered-by");
app.use(express.json({ limit: "32kb" }));
app.use((req, res, next) => {
  const startedAt = Date.now();
  res.on("finish", () => {
    const durationMs = Date.now() - startedAt;
    console.log(
      `[http] ${req.method} ${req.originalUrl} ip=${requestIp(req)} status=${res.statusCode} durationMs=${durationMs}`,
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
  });
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
  res.type("html").send(renderServerAdminPage());
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

    const session = createServerAdminSession(username);
    setServerAdminCookie(res, session.id);
    res.json({
      id: session.id,
      username: session.username,
      expiresAt: new Date(session.expiresAt).toISOString(),
    });
  } catch (error) {
    next(error);
  }
});

app.post("/server-admin/logout", (req, res, next) => {
  try {
    const session = requireServerAdminSession(req);
    serverAdminSessions.delete(session.id);
    clearServerAdminCookie(res);
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
    });
  } catch (error) {
    next(error);
  }
});

app.get("/server-admin/settings", (req, res, next) => {
  try {
    requireServerAdminSession(req);
    const guardSnapshot = getCreateRoomGuardSnapshot();
    const relayTrafficSnapshot = relayManager.getTrafficSnapshot();
    res.json({
      ...serverAdminStateStore.getSettingsView(),
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
    });
  } catch (error) {
    next(error);
  }
});

app.patch("/server-admin/settings", (req, res, next) => {
  (async () => {
    requireServerAdminSession(req);
    const body = req.body as {
      displayName?: string;
      publicListingEnabled?: boolean;
      bandwidthCapacityMbps?: number | null;
      announcements?: unknown;
    } | undefined;
    const settings = serverAdminStateStore.updateSettings({
      displayName: typeof body?.displayName === "string" ? body.displayName : "",
      publicListingEnabled: Boolean(body?.publicListingEnabled),
      bandwidthCapacityMbps: optionalPositiveNumber(body?.bandwidthCapacityMbps, "bandwidthCapacityMbps", 100_000),
      announcements: body?.announcements,
    });
    const guardSnapshot = getCreateRoomGuardSnapshot();
    const relayTrafficSnapshot = relayManager.getTrafficSnapshot();
    res.json({
      ...settings,
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
    });
  })().catch((error) => {
    next(error);
  });
});

app.use((error: unknown, _req: Request, res: Response, _next: NextFunction) => {
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
const wss = new WebSocketServer({ server, path: env.wsPath });
const roomPeers = new Map<string, Set<ControlPeer>>();

wss.on("connection", (socket, req) => {
  try {
    const requestUrl = new URL(req.url ?? env.wsPath, `http://${req.headers.host ?? "127.0.0.1"}`);
    const roomId = requiredQuery(requestUrl, "roomId");
    const controlChannelId = requiredQuery(requestUrl, "controlChannelId");
    const role = requiredQuery(requestUrl, "role");

    if (role !== "host" && role !== "client") {
      throw new InputError("role 必须为 host 或 client。");
    }

    if (role === "host") {
      store.validateHostControl(roomId, controlChannelId, requiredQuery(requestUrl, "token"));
    } else {
      store.validateClientControl(roomId, controlChannelId, requiredQuery(requestUrl, "ticketId"));
    }

    const peer: ControlPeer = {
      socket,
      roomId,
      controlChannelId,
      role,
      lastSeenAt: Date.now(),
      ...(role === "client" ? { ticketId: requiredQuery(requestUrl, "ticketId") } : {}),
    };

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

        if (parsed.type === "pong" || parsed.type === "host_hello") {
          return;
        }

        if (parsed.type === "client_hello") {
          peer.playerNetId = String(parsed.playerNetId ?? "");
          peer.playerName = String(parsed.playerName ?? "");
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

const cleanupInterval = setInterval(() => {
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

if (peerEnv.enabled && peerEnv.selfAddress) {
  const identity = await loadOrCreateIdentity(peerEnv.stateDir);
  const peerStore = new PeerStore(join(peerEnv.stateDir, "peers.json"));
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

  // Defer bootstrap + auto-announce to AFTER server.listen() succeeds.
  // Load-bearing: if listen() fails (EADDRINUSE, perms, etc.) the process
  // exits before announcing to upstream peers. Without this, systemd
  // crash-loops would hammer /peers/announce on every seed lobby every
  // few seconds, trip their per-IP rate limit (5/h), and lock the
  // operator's IP out of legitimate re-announcement for an hour.
  const cfDiscoveryBaseUrl = peerEnv.cfDiscoveryBaseUrl;
  if (cfDiscoveryBaseUrl) {
    server.once("listening", () => {
      void (async () => {
        try {
          const seeds = await loadSeedsFromCf(cfDiscoveryBaseUrl);
          await bootstrapPeers({ store: peerStore, selfAddress: peerEnv.selfAddress, seeds });
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
          const announceTargets = peerStore.list().filter((p) => p.address !== peerEnv.selfAddress).length;
          if (announceTargets > 0) {
            await announceToBootstrappedPeers({
              store: peerStore,
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
    });
  }

  const scheduler = new GossipScheduler({
    store: peerStore,
    selfAddress: peerEnv.selfAddress,
    selfPublicKey: identity.publicKey,
    seedAddresses: [],
    postHeartbeat: async (addr, body) => {
      await fetch(`${addr.replace(/\/+$/, "")}/peers/heartbeat`, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
    },
  });
  scheduler.start();
  console.log(
    `[peer] mounted; self=${peerEnv.selfAddress} displayName="${resolvePeerDisplayName()}" cf=${peerEnv.cfDiscoveryBaseUrl || "(none)"}`,
  );

  // Refresh the self-entry's displayName periodically so admin panel edits
  // propagate without needing a restart. The /peers/health response already
  // resolves live, but the local /peers list cache needs an explicit poke.
  setInterval(() => {
    const fresh = resolvePeerDisplayName();
    const existing = peerStore.get(peerEnv.selfAddress);
    if (!existing || existing.displayName === fresh) return;
    void peerStore.upsert({ ...existing, displayName: fresh, lastSeen: new Date().toISOString() });
  }, 60_000).unref();
} else {
  console.log("[peer] disabled (set PEER_SELF_ADDRESS to enable)");
}

server.listen(env.port, env.host, () => {
  console.log(`[lobby] listening on http://${env.host}:${env.port} (ws path ${env.wsPath})`);
  console.log(
    `[relay] enabled udp://${env.relayBindHost}:${env.relayPortStart}-${env.relayPortEnd} publicHost=${env.relayPublicHost || "<request-host>"}`,
  );
  console.log(`[server-admin] panel ready at http://${env.host}:${env.port}/server-admin`);
  if (isServerAdminConfigured()) {
    console.log(`[server-admin] login enabled for ${env.serverAdminUsername}`);
  } else {
    console.log("[server-admin] login disabled until SERVER_ADMIN_PASSWORD_HASH and SERVER_ADMIN_SESSION_SECRET are configured");
  }
  const listingMode = serverAdminStateStore.getState().publicListingEnabled ? "public" : "private";
  console.log(`[server-admin] decentralized listing mode: ${listingMode} (toggle via admin panel)`);
});

function addPeer(peer: ControlPeer) {
  let peers = roomPeers.get(peer.roomId);
  if (!peers) {
    peers = new Set();
    roomPeers.set(peer.roomId, peers);
  }

  peers.add(peer);
}

function removePeer(peer: ControlPeer) {
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
  return cleanupExpiredRooms({
    cleanupExpired: (cleanupNow) => store.cleanupExpired(cleanupNow),
    removeRelayRoom: (roomId) => relayManager.removeRoom(roomId),
    closeRoomSockets,
    log: (message) => console.log(message),
  }, now);
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
  return normalizeRemoteIp(req.socket.remoteAddress ?? "");
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
  const session: ServerAdminSession = {
    id: `session_${randomUUID()}`,
    username,
    expiresAt: Date.now() + env.serverAdminSessionTtlMs,
  };
  serverAdminSessions.set(session.id, session);
  return session;
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

function setServerAdminCookie(res: Response, sessionId: string) {
  const token = signServerAdminSession(sessionId, env.serverAdminSessionSecret!);
  res.setHeader("Set-Cookie", `sts2_server_admin_session=${token}; Path=/; HttpOnly; SameSite=Lax; Max-Age=${Math.floor(env.serverAdminSessionTtlMs / 1000)}`);
}

function clearServerAdminCookie(res: Response) {
  res.setHeader("Set-Cookie", "sts2_server_admin_session=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0");
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

function parseTrustedProxyCidrs(value: string | undefined) {
  if (!value) {
    return [];
  }

  return value
    .split(",")
    .map((entry) => entry.trim())
    .filter(Boolean);
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

function getLobbyAccessToken(req: Request) {
  return optionalString(req.header("x-lobby-access-token"))
    ?? optionalString(req.header("authorization"))?.replace(/^Bearer\s+/i, "");
}

function getCreateRoomToken(req: Request) {
  return optionalString(req.header("x-create-room-token"));
}

function isTrustedOperationsRequest(req: Request) {
  const remote = requestIp(req);
  if (!remote) {
    return false;
  }

  return env.createRoomTrustedProxies.some((candidate: string) => ipMatchesCandidate(remote, candidate));
}

function ipMatchesCandidate(ip: string, candidate: string) {
  const normalizedCandidate = candidate.trim();
  if (!normalizedCandidate) {
    return false;
  }

  if (normalizedCandidate === "*") {
    return true;
  }

  const normalizedIp = normalizeRemoteIp(ip);
  if (!normalizedIp) {
    return false;
  }

  if (normalizedCandidate.includes("/")) {
    const [base, prefix] = normalizedCandidate.split("/", 2);
    const prefixLength = Number.parseInt(prefix ?? "", 10);
    if (!Number.isInteger(prefixLength)) {
      return false;
    }

    const normalizedBase = normalizeRemoteIp(base ?? "");
    if (!normalizedBase) {
      return false;
    }

    const ipBytes = ipToBytes(normalizedIp);
    const baseBytes = ipToBytes(normalizedBase);
    if (!ipBytes || !baseBytes || ipBytes.length !== baseBytes.length) {
      return false;
    }

    return bytesMatchPrefix(ipBytes, baseBytes, prefixLength);
  }

  return normalizedIp === normalizeRemoteIp(normalizedCandidate);
}

function normalizeRemoteIp(value: string) {
  const trimmed = value.trim();
  if (!trimmed) {
    return "";
  }

  if (trimmed.startsWith("::ffff:")) {
    const mapped = trimmed.slice("::ffff:".length);
    return mapped || trimmed;
  }

  return trimmed;
}

function ipToBytes(value: string) {
  const normalized = normalizeRemoteIp(value);
  if (!normalized) {
    return null;
  }

  const ipv4 = ipv4ToBytes(normalized);
  if (ipv4) {
    return ipv4;
  }

  return ipv6ToBytes(normalized);
}

function ipv4ToBytes(value: string) {
  const parts = value.split(".");
  if (parts.length !== 4) {
    return null;
  }

  const bytes: number[] = [];
  for (const part of parts) {
    const parsed = Number.parseInt(part, 10);
    if (!Number.isInteger(parsed) || parsed < 0 || parsed > 255) {
      return null;
    }

    bytes.push(parsed);
  }

  return bytes;
}

function ipv6ToBytes(value: string) {
  if (!value.includes(":")) {
    return null;
  }

  const lower = value.toLowerCase();
  const doubleColonIndex = lower.indexOf("::");
  if (doubleColonIndex !== lower.lastIndexOf("::")) {
    return null;
  }

  const expandPart = (part: string) => part.split(":").filter((segment) => segment.length > 0);
  const left = doubleColonIndex >= 0 ? expandPart(lower.slice(0, doubleColonIndex)) : expandPart(lower);
  const right = doubleColonIndex >= 0 ? expandPart(lower.slice(doubleColonIndex + 2)) : [];
  if (doubleColonIndex < 0 && left.length !== 8) {
    return null;
  }

  const missing = doubleColonIndex >= 0 ? 8 - (left.length + right.length) : 0;
  if (missing < 0) {
    return null;
  }

  const groups = [
    ...left,
    ...Array.from({ length: missing }, () => "0"),
    ...right,
  ];
  if (groups.length !== 8) {
    return null;
  }

  const bytes: number[] = [];
  for (const group of groups) {
    if (!/^[0-9a-f]{1,4}$/.test(group)) {
      return null;
    }

    const parsed = Number.parseInt(group, 16);
    bytes.push((parsed >> 8) & 0xff, parsed & 0xff);
  }

  return bytes;
}

function bytesMatchPrefix(ipBytes: number[], baseBytes: number[], prefixLength: number) {
  if (prefixLength <= 0) {
    return true;
  }

  const totalBits = ipBytes.length * 8;
  if (prefixLength > totalBits) {
    return false;
  }

  const fullBytes = Math.floor(prefixLength / 8);
  const remainingBits = prefixLength % 8;

  for (let index = 0; index < fullBytes; index++) {
    if (ipBytes[index] !== baseBytes[index]) {
      return false;
    }
  }

  if (remainingBits === 0) {
    return true;
  }

  const mask = (0xff << (8 - remainingBits)) & 0xff;
  return (ipBytes[fullBytes]! & mask) === (baseBytes[fullBytes]! & mask);
}

function optionalString(value: unknown) {
  if (typeof value !== "string") {
    return undefined;
  }

  const trimmed = value.trim();
  return trimmed === "" ? undefined : trimmed;
}

function optionalEnv(value: string | undefined) {
  const normalized = value?.trim();
  return normalized ? normalized : undefined;
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

function assertCreateJoinRateLimit(req: Request, scope: string) {
  const now = Date.now();
  const ip = requestIp(req) || "unknown";
  const key = `${scope}:${ip}`;
  const windowMs = Math.max(1000, env.createJoinRateLimitWindowMs);
  const maxRequests = Math.max(1, env.createJoinRateLimitMaxRequests);

  cleanupRateLimitBuckets(now, windowMs);

  const bucket = createJoinRateLimitHits.get(key) ?? {
    hits: [],
    lastSeenAt: now,
  };
  const recent = bucket.hits.filter((timestamp) => now - timestamp < windowMs);
  if (recent.length >= maxRequests) {
    bucket.hits = recent;
    bucket.lastSeenAt = now;
    createJoinRateLimitHits.set(key, bucket);
    throw new HttpError(429, "rate_limited", "请求过于频繁，请稍后再试。");
  }

  recent.push(now);
  bucket.hits = recent;
  bucket.lastSeenAt = now;
  createJoinRateLimitHits.set(key, bucket);
}

function cleanupRateLimitBuckets(now: number, windowMs: number) {
  for (const [key, bucket] of createJoinRateLimitHits.entries()) {
    if (now - bucket.lastSeenAt >= windowMs && bucket.hits.every((timestamp) => now - timestamp >= windowMs)) {
      createJoinRateLimitHits.delete(key);
    }
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
            netId: includeSensitiveSavedRun ? slot.netId : "",
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

function parseBooleanEnv(value: string | undefined, fallback: boolean) {
  if (value == null || value.trim() === "") {
    return fallback;
  }

  const normalized = value.trim().toLowerCase();
  if (normalized === "1" || normalized === "true" || normalized === "yes" || normalized === "on") {
    return true;
  }

  if (normalized === "0" || normalized === "false" || normalized === "no" || normalized === "off") {
    return false;
  }

  throw new Error(`Invalid boolean env value: ${value}`);
}

function parseConnectionStrategyEnv(value: string | undefined): ConnectionStrategy {
  const normalized = value?.trim().toLowerCase() ?? "direct-first";
  if (normalized === "direct-first" || normalized === "relay-first" || normalized === "relay-only") {
    return normalized;
  }

  throw new Error(`Invalid CONNECTION_STRATEGY value: ${value}`);
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

interface ControlPeer {
  socket: WebSocket;
  roomId: string;
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
}

process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);

function shutdown() {
  closeRuntimeResources(() => {
    process.exit(0);
  });
}

function closeRuntimeResources(onClosed?: () => void) {
  clearInterval(cleanupInterval);
  wss.close();
  relayManager.close();
  server.close(() => {
    onClosed?.();
  });
}

export const __testHooks = {
  normalizeRemoteIp,
  ipMatchesCandidate,
  hasLobbyReadAccessToken,
  getLobbyAccessToken,
  getCreateRoomToken,
  assertCreateJoinRateLimit,
  cleanupRateLimitBuckets,
  createJoinRateLimitHits,
  closeRuntimeResources,
};
