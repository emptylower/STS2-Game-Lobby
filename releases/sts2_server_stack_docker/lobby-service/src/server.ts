import { randomUUID } from "node:crypto";
import { once } from "node:events";
import express, { type NextFunction, type Request, type Response } from "express";
import { createServer } from "node:http";
import { WebSocketServer, type WebSocket } from "ws";
import { CreateRoomBandwidthGuard } from "./bandwidth-guard.js";
import { assertRelayCreateReady, assertRelayJoinReady } from "./join-guard.js";
import { RoomRelayManager } from "./relay.js";
import { cleanupExpiredRooms } from "./room-cleanup.js";
import { signServerAdminSession, verifyServerAdminPassword, verifySignedServerAdminSession } from "./server-admin-auth.js";
import { ServerAdminStateStore } from "./server-admin-state.js";
import { createServerRegistrySyncService } from "./server-admin-sync.js";
import { renderServerAdminPage } from "./server-admin-ui.js";
import {
  LobbyStore,
  LobbyStoreError,
  type ConnectionStrategy,
  type CreateRoomInput,
  type HeartbeatInput,
  type JoinRoomInput,
} from "./store.js";

const MaxLobbyPlayers = 256;

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
  serverAdminUsername: process.env.SERVER_ADMIN_USERNAME ?? "admin",
  serverAdminPasswordHash: optionalEnv(process.env.SERVER_ADMIN_PASSWORD_HASH),
  serverAdminSessionSecret: optionalEnv(process.env.SERVER_ADMIN_SESSION_SECRET),
  serverAdminSessionTtlMs: Number.parseInt(process.env.SERVER_ADMIN_SESSION_TTL_HOURS ?? "168", 10) * 60 * 60 * 1000,
  serverAdminStateFile: process.env.SERVER_ADMIN_STATE_FILE ?? `${process.cwd()}/data/server-admin.json`,
  serverRegistryBaseUrl: optionalEnv(process.env.SERVER_REGISTRY_BASE_URL) ?? "",
  serverRegistrySyncIntervalMs: Number.parseInt(process.env.SERVER_REGISTRY_SYNC_INTERVAL_SECONDS ?? "180", 10) * 1000,
  serverRegistrySyncTimeoutMs: Number.parseInt(process.env.SERVER_REGISTRY_SYNC_TIMEOUT_MS ?? "5000", 10),
  serverRegistryPublicBaseUrl: process.env.SERVER_REGISTRY_PUBLIC_BASE_URL ?? buildRegistryPublicBaseUrl(),
  serverRegistryPublicWsUrl: process.env.SERVER_REGISTRY_PUBLIC_WS_URL ?? buildRegistryPublicWsUrl(),
  serverRegistryBandwidthProbeUrl: process.env.SERVER_REGISTRY_BANDWIDTH_PROBE_URL ?? buildRegistryBandwidthProbeUrl(),
  serverRegistryProbeFileBytes: Number.parseInt(process.env.SERVER_REGISTRY_PROBE_FILE_BYTES ?? String(100 * 1024 * 1024), 10),
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
const serverAdminStateStore = new ServerAdminStateStore(env.serverAdminStateFile);
const createRoomBandwidthGuard = new CreateRoomBandwidthGuard();
const serverRegistrySync = createServerRegistrySyncService({
  env: {
    registryBaseUrl: env.serverRegistryBaseUrl,
    timeoutMs: env.serverRegistrySyncTimeoutMs,
    publicBaseUrl: env.serverRegistryPublicBaseUrl,
    publicWsUrl: env.serverRegistryPublicWsUrl,
    bandwidthProbeUrl: env.serverRegistryBandwidthProbeUrl,
  },
  stateStore: serverAdminStateStore,
  getRoomCount: () => store.listRooms().length,
  getGuardSnapshot: () => getCreateRoomGuardSnapshot(),
});
const serverAdminSessions = new Map<string, ServerAdminSession>();

const app = express();
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

app.get("/health", (_req, res) => {
  cleanupExpiredRoomsNow();
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

app.get("/registry/bandwidth-probe.bin", async (_req, res, next) => {
  try {
    res.setHeader("content-type", "application/octet-stream");
    res.setHeader("content-length", String(env.serverRegistryProbeFileBytes));
    res.setHeader("cache-control", "no-store");
    await streamZeroBytes(res, env.serverRegistryProbeFileBytes);
  } catch (error) {
    next(error);
  }
});

app.get("/rooms", (_req, res) => {
  cleanupExpiredRoomsNow();
  res.json(store.listRooms());
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
      roomName: requiredString(body?.roomName, "roomName"),
      password: optionalString(body?.password),
      hostPlayerName: requiredString(body?.hostPlayerName, "hostPlayerName"),
      gameMode: requiredString(body?.gameMode, "gameMode"),
      version: requiredString(body?.version, "version"),
      modVersion: requiredString(body?.modVersion, "modVersion"),
      modList: optionalStringArray(body?.modList),
      protocolProfile: optionalString(body?.protocolProfile),
      maxPlayers: positiveInt(body?.maxPlayers, "maxPlayers", 1, MaxLobbyPlayers),
      hostConnectionInfo: {
        enetPort: positiveInt(body?.hostConnectionInfo?.enetPort, "hostConnectionInfo.enetPort", 1, 65535),
        localAddresses: Array.isArray(body?.hostConnectionInfo?.localAddresses)
          ? body?.hostConnectionInfo?.localAddresses
              .filter((value): value is string => typeof value === "string")
              .map((value) => value.trim())
          : [],
      },
    };

    if (body?.savedRun) {
      roomInput.savedRun = {
        saveKey: requiredString(body.savedRun.saveKey, "savedRun.saveKey"),
        slots: Array.isArray(body.savedRun.slots)
          ? body.savedRun.slots
              .filter((value) => Boolean(value) && typeof value === "object")
              .map((slot, index) => {
                const candidate = slot as unknown as Record<string, unknown>;
                return {
                  netId: requiredString(candidate.netId, `savedRun.slots[${index}].netId`),
                  characterId: optionalString(candidate.characterId),
                  characterName: optionalString(candidate.characterName),
                  playerName: optionalString(candidate.playerName),
                  isHost: Boolean(candidate.isHost),
                };
              })
          : [],
        connectedPlayerNetIds: Array.isArray(body.savedRun.connectedPlayerNetIds)
          ? body.savedRun.connectedPlayerNetIds
              .filter((value): value is string => typeof value === "string")
              .map((value) => value.trim())
          : [],
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
    cleanupExpiredRoomsNow();
    const body = req.body as Partial<JoinRoomInput> | undefined;
    const response = store.joinRoom(req.params.id, {
      playerName: requiredString(body?.playerName, "playerName"),
      password: optionalString(body?.password),
      version: requiredString(body?.version, "version"),
      modVersion: requiredString(body?.modVersion, "modVersion"),
      modList: optionalStringArray(body?.modList),
      desiredSavePlayerNetId: optionalString(body?.desiredSavePlayerNetId),
      playerNetId: optionalString(body?.playerNetId),
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
      registryBaseUrl: env.serverRegistryBaseUrl,
      publicBaseUrl: env.serverRegistryPublicBaseUrl,
      publicWsUrl: env.serverRegistryPublicWsUrl,
      bandwidthProbeUrl: env.serverRegistryBandwidthProbeUrl,
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
    await serverRegistrySync.runNow();
    const guardSnapshot = getCreateRoomGuardSnapshot();
    const relayTrafficSnapshot = relayManager.getTrafficSnapshot();
    res.json({
      ...settings,
      registryBaseUrl: env.serverRegistryBaseUrl,
      publicBaseUrl: env.serverRegistryPublicBaseUrl,
      publicWsUrl: env.serverRegistryPublicWsUrl,
      bandwidthProbeUrl: env.serverRegistryBandwidthProbeUrl,
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
const serverRegistrySyncInterval = setInterval(() => {
  void serverRegistrySync.runNow();
}, env.serverRegistrySyncIntervalMs);

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
  if (env.serverRegistryBaseUrl) {
    console.log(`[server-admin] registry sync target ${env.serverRegistryBaseUrl}`);
    void serverRegistrySync.runNow();
  } else {
    console.log("[server-admin] registry sync disabled until SERVER_REGISTRY_BASE_URL is configured");
  }
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
  const forwarded = req.headers["x-forwarded-for"];
  if (typeof forwarded === "string" && forwarded.trim()) {
    return forwarded.split(",")[0]!.trim();
  }

  return req.socket.remoteAddress ?? "";
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

async function streamZeroBytes(res: Response, totalBytes: number) {
  const chunk = Buffer.alloc(64 * 1024, 0);
  let remaining = totalBytes;
  while (remaining > 0) {
    const nextLength = Math.min(chunk.length, remaining);
    const canContinue = res.write(chunk.subarray(0, nextLength));
    remaining -= nextLength;
    if (!canContinue) {
      await once(res, "drain");
    }
  }
  res.end();
}

function buildRegistryPublicBaseUrl() {
  const host = process.env.RELAY_PUBLIC_HOST?.trim()
    || (process.env.HOST?.trim() && process.env.HOST !== "0.0.0.0" ? process.env.HOST.trim() : "127.0.0.1");
  const port = process.env.PORT?.trim() || "8787";
  return `http://${host}:${port}`;
}

function buildRegistryPublicWsUrl() {
  const baseUrl = process.env.SERVER_REGISTRY_PUBLIC_BASE_URL?.trim() || buildRegistryPublicBaseUrl();
  try {
    const url = new URL(baseUrl);
    const scheme = url.protocol === "https:" ? "wss:" : "ws:";
    return `${scheme}//${url.host}/control`;
  } catch {
    return "ws://127.0.0.1:8787/control";
  }
}

function buildRegistryBandwidthProbeUrl() {
  const baseUrl = process.env.SERVER_REGISTRY_PUBLIC_BASE_URL?.trim() || buildRegistryPublicBaseUrl();
  return `${baseUrl.replace(/\/+$/, "")}/registry/bandwidth-probe.bin`;
}

function getCreateRoomGuardSnapshot() {
  const state = serverAdminStateStore.getState();
  const relayTrafficSnapshot = relayManager.getTrafficSnapshot();
  return createRoomBandwidthGuard.getSnapshot({
    currentBandwidthMbps: relayTrafficSnapshot.currentBandwidthMbps,
    bandwidthCapacityMbps: state.bandwidthCapacityMbps,
    probePeak7dCapacityMbps: state.probePeak7dCapacityMbps,
    createRoomGuardApplies: Boolean(env.serverRegistryBaseUrl && state.serverId && state.serverToken),
  });
}

function requiredString(value: unknown, name: string) {
  if (typeof value !== "string" || value.trim() === "") {
    throw new InputError(`${name} 不能为空。`);
  }

  return value.trim();
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
  clearInterval(cleanupInterval);
  clearInterval(serverRegistrySyncInterval);
  wss.close();
  relayManager.close();
  server.close(() => {
    process.exit(0);
  });
}
