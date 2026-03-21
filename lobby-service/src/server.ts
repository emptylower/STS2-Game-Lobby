import express, { type NextFunction, type Request, type Response } from "express";
import { createServer } from "node:http";
import { WebSocketServer, type WebSocket } from "ws";
import { assertRelayCreateReady, assertRelayJoinReady } from "./join-guard.js";
import { RoomRelayManager } from "./relay.js";
import { cleanupExpiredRooms } from "./room-cleanup.js";
import {
  LobbyStore,
  LobbyStoreError,
  type ConnectionStrategy,
  type CreateRoomInput,
  type HeartbeatInput,
  type JoinRoomInput,
} from "./store.js";

const env = {
  host: process.env.HOST ?? "0.0.0.0",
  port: Number.parseInt(process.env.PORT ?? "8787", 10),
  heartbeatTimeoutMs: Number.parseInt(process.env.HEARTBEAT_TIMEOUT_SECONDS ?? "35", 10) * 1000,
  ticketTtlMs: Number.parseInt(process.env.TICKET_TTL_SECONDS ?? "120", 10) * 1000,
  wsPath: process.env.WS_PATH ?? "/control",
  relayBindHost: process.env.RELAY_BIND_HOST ?? process.env.HOST ?? "0.0.0.0",
  relayPublicHost: process.env.RELAY_PUBLIC_HOST ?? "",
  relayPortStart: Number.parseInt(process.env.RELAY_PORT_START ?? "39000", 10),
  relayPortEnd: Number.parseInt(process.env.RELAY_PORT_END ?? "39511", 10),
  relayHostIdleMs: Number.parseInt(process.env.RELAY_HOST_IDLE_SECONDS ?? "20", 10) * 1000,
  relayClientIdleMs: Number.parseInt(process.env.RELAY_CLIENT_IDLE_SECONDS ?? "90", 10) * 1000,
  strictGameVersionCheck: parseBooleanEnv(process.env.STRICT_GAME_VERSION_CHECK, true),
  strictModVersionCheck: parseBooleanEnv(process.env.STRICT_MOD_VERSION_CHECK, true),
  connectionStrategy: parseConnectionStrategyEnv(process.env.CONNECTION_STRATEGY),
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
  res.json({
    ok: true,
    rooms: store.listRooms().length,
    strictGameVersionCheck: env.strictGameVersionCheck,
    strictModVersionCheck: env.strictModVersionCheck,
    connectionStrategy: env.connectionStrategy,
  });
});

app.get("/probe", (_req, res) => {
  res.json({
    ok: true,
  });
});

app.get("/rooms", (_req, res) => {
  cleanupExpiredRoomsNow();
  res.json(store.listRooms());
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
    const body = req.body as Partial<CreateRoomInput> | undefined;
    const roomInput: CreateRoomInput = {
      roomName: requiredString(body?.roomName, "roomName"),
      password: optionalString(body?.password),
      hostPlayerName: requiredString(body?.hostPlayerName, "hostPlayerName"),
      gameMode: requiredString(body?.gameMode, "gameMode"),
      version: requiredString(body?.version, "version"),
      modVersion: requiredString(body?.modVersion, "modVersion"),
      modList: optionalStringArray(body?.modList),
      maxPlayers: positiveInt(body?.maxPlayers, "maxPlayers", 1, 8),
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
      `[lobby] create room roomId=${room.roomId} roomName="${room.room.roomName}" hostPlayer="${room.room.hostPlayerName}" version=${room.room.version} modVersion=${room.room.modVersion} remote=${requestIp(req)} relay=${relayEndpoint ? formatHostPort(relayEndpoint.host, relayEndpoint.port) : "disabled"} relayState=${room.room.relayState}`,
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
    });
    const relayEndpoint = relayManager.getRoomEndpoint(req.params.id, resolveAdvertisedRelayHost(req));
    if (relayEndpoint) {
      response.connectionPlan.relayAllowed = true;
      response.connectionPlan.relayEndpoint = relayEndpoint;
    }
    const relayStatus = relayManager.getRoomStatus(req.params.id);
    assertRelayJoinReady(env.connectionStrategy, response.room.relayState, relayStatus.hasActiveHost);
    console.log(
      `[lobby] join ticket issued roomId=${req.params.id} player="${body?.playerName ?? ""}" roomModVersion=${response.room.modVersion} ticketId=${response.ticketId} remote=${requestIp(req)} strategy=${response.connectionPlan.strategy} direct=${response.connectionPlan.directCandidates.length} relay=${relayEndpoint ? formatHostPort(relayEndpoint.host, relayEndpoint.port) : "disabled"} relayState=${response.room.relayState} relayHost=${relayStatus.hasActiveHost ? relayStatus.activeHostDetail : "unregistered"} relayClients=${relayStatus.clientCount}`,
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
      currentPlayers: positiveInt(body?.currentPlayers, "currentPlayers", 1, 8),
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
    };

    addPeer(peer);
    sendJson(socket, {
      type: "connected",
      roomId,
      controlChannelId,
      role,
    });

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

        if (parsed.type === "pong" || parsed.type === "host_hello" || parsed.type === "client_hello") {
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

server.listen(env.port, env.host, () => {
  console.log(`[lobby] listening on http://${formatHostPort(env.host, env.port)} (ws path ${env.wsPath})`);
  console.log(
    `[relay] enabled udp://${formatHostPort(env.relayBindHost, env.relayPortStart)}-${env.relayPortEnd} publicHost=${env.relayPublicHost || "<request-host>"}`,
  );
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

function resolveAdvertisedRelayHost(req: Request) {
  if (env.relayPublicHost.trim()) {
    return trimIpv6Brackets(env.relayPublicHost.trim());
  }

  const hostHeader = req.headers.host?.trim();
  if (!hostHeader) {
    return "127.0.0.1";
  }

  try {
    return trimIpv6Brackets(new URL(`http://${hostHeader}`).hostname);
  } catch {
    return trimIpv6Brackets(hostHeader.replace(/:\d+$/, ""));
  }
}

function formatHostPort(host: string, port: number) {
  const trimmed = trimIpv6Brackets(host.trim());
  if (trimmed.includes(":")) {
    return `[${trimmed}]:${port}`;
  }

  return `${trimmed}:${port}`;
}

function trimIpv6Brackets(value: string) {
  return value.replace(/^\[(.*)\]$/, "$1");
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

function requiredQuery(url: URL, key: string) {
  const value = url.searchParams.get(key);
  if (!value || value.trim() === "") {
    throw new InputError(`缺少查询参数 ${key}。`);
  }

  return value.trim();
}

class InputError extends Error {}

interface ControlPeer {
  socket: WebSocket;
  roomId: string;
  controlChannelId: string;
  role: "host" | "client";
  lastSeenAt: number;
}

process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);

function shutdown() {
  clearInterval(cleanupInterval);
  wss.close();
  relayManager.close();
  server.close(() => {
    process.exit(0);
  });
}
