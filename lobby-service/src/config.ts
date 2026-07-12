import type { ConnectionStrategy } from "./store.js";

export type LobbyServiceConfigErrorCode =
  | "invalid_boolean"
  | "invalid_connection_strategy"
  | "invalid_integer"
  | "invalid_path"
  | "invalid_range";

export class LobbyServiceConfigError extends Error {
  constructor(
    readonly code: LobbyServiceConfigErrorCode,
    readonly environmentKey: string,
    message: string,
  ) {
    super(message);
    this.name = "LobbyServiceConfigError";
  }
}

export interface ChatConfig {
  enabled: boolean;
  historyLimit: number;
  historyTtlMs: number;
  snapshotLimit: number;
  maxPayloadBytes: number;
  maxConnectionsPerIp: number;
  maxConnectionsTotal: number;
  maxPendingTickets: number;
  trustedProxyCidrs: string[];
  ticketRequestsPerMinute: number;
  ipMessagesPerMinute: number;
  connectionBurst: number;
  connectionRefillMs: number;
  slowClientBytes: number;
}

export interface LobbyServiceConfig {
  host: string;
  port: number;
  heartbeatTimeoutMs: number;
  ticketTtlMs: number;
  wsPath: string;
  relayBindHost: string;
  relayPublicHost: string;
  relayPortStart: number;
  relayPortEnd: number;
  relayHostIdleMs: number;
  relayClientIdleMs: number;
  strictGameVersionCheck: boolean;
  strictModVersionCheck: boolean;
  connectionStrategy: ConnectionStrategy;
  publicRoomListEnabled: boolean;
  publicDetailedHealthEnabled: boolean;
  enforceLobbyAccessToken: boolean;
  enforceCreateRoomToken: boolean;
  lobbyAccessToken?: string;
  createRoomToken?: string;
  createRoomTrustedProxies: string[];
  createJoinRateLimitWindowMs: number;
  createJoinRateLimitMaxRequests: number;
  serverAdminUsername: string;
  serverAdminPasswordHash?: string;
  serverAdminSessionSecret?: string;
  serverAdminSessionTtlMs: number;
  serverAdminStateFile: string;
  peerPublicListingEnabledDefault: boolean;
  peer: {
    enabled: boolean;
    selfAddress: string;
    cfDiscoveryBaseUrl: string;
    stateDir: string;
    displayNameOverride: string;
  };
  chat: ChatConfig;
}

export function loadLobbyServiceConfig(source: NodeJS.ProcessEnv): LobbyServiceConfig {
  const host = source.HOST ?? "0.0.0.0";
  const relayPortStart = parseInteger(source, "RELAY_PORT_START", 39000, 1, 65535);
  const relayPortEnd = parseInteger(source, "RELAY_PORT_END", 39149, 1, 65535);
  const lobbyAccessToken = optionalEnv(source.LOBBY_ACCESS_TOKEN) ?? optionalEnv(source.CREATE_ROOM_TOKEN);
  const createRoomToken = optionalEnv(source.CREATE_ROOM_TOKEN) ?? optionalEnv(source.LOBBY_ACCESS_TOKEN);
  const serverAdminPasswordHash = optionalEnv(source.SERVER_ADMIN_PASSWORD_HASH);
  const serverAdminSessionSecret = optionalEnv(source.SERVER_ADMIN_SESSION_SECRET);
  if (relayPortEnd < relayPortStart) {
    throw new LobbyServiceConfigError(
      "invalid_range",
      "RELAY_PORT_END",
      "RELAY_PORT_END must be greater than or equal to RELAY_PORT_START",
    );
  }

  return {
    host,
    port: parseInteger(source, "PORT", 8787, 1, 65535),
    heartbeatTimeoutMs: parsePositiveInteger(source, "HEARTBEAT_TIMEOUT_SECONDS", 35) * 1000,
    ticketTtlMs: parsePositiveInteger(source, "TICKET_TTL_SECONDS", 120) * 1000,
    wsPath: parseWebSocketPath(source.WS_PATH ?? "/control"),
    relayBindHost: source.RELAY_BIND_HOST ?? host,
    relayPublicHost: source.RELAY_PUBLIC_HOST ?? "",
    relayPortStart,
    relayPortEnd,
    relayHostIdleMs: parsePositiveInteger(source, "RELAY_HOST_IDLE_SECONDS", 20) * 1000,
    relayClientIdleMs: parsePositiveInteger(source, "RELAY_CLIENT_IDLE_SECONDS", 90) * 1000,
    strictGameVersionCheck: parseBoolean(source, "STRICT_GAME_VERSION_CHECK", true),
    strictModVersionCheck: parseBoolean(source, "STRICT_MOD_VERSION_CHECK", true),
    connectionStrategy: parseConnectionStrategy(source.CONNECTION_STRATEGY),
    publicRoomListEnabled: parseBoolean(source, "PUBLIC_ROOM_LIST_ENABLED", false),
    publicDetailedHealthEnabled: parseBoolean(source, "PUBLIC_DETAILED_HEALTH_ENABLED", false),
    enforceLobbyAccessToken: parseBoolean(source, "ENFORCE_LOBBY_ACCESS_TOKEN", true),
    enforceCreateRoomToken: parseBoolean(source, "ENFORCE_CREATE_ROOM_TOKEN", true),
    ...(lobbyAccessToken == null ? {} : { lobbyAccessToken }),
    ...(createRoomToken == null ? {} : { createRoomToken }),
    createRoomTrustedProxies: parseCommaSeparatedValues(source.CREATE_ROOM_TRUSTED_PROXIES),
    createJoinRateLimitWindowMs: parsePositiveInteger(source, "CREATE_JOIN_RATE_LIMIT_WINDOW_MS", 60000),
    createJoinRateLimitMaxRequests: parsePositiveInteger(source, "CREATE_JOIN_RATE_LIMIT_MAX_REQUESTS", 30),
    serverAdminUsername: source.SERVER_ADMIN_USERNAME ?? "admin",
    ...(serverAdminPasswordHash == null ? {} : { serverAdminPasswordHash }),
    ...(serverAdminSessionSecret == null ? {} : { serverAdminSessionSecret }),
    serverAdminSessionTtlMs: parsePositiveInteger(source, "SERVER_ADMIN_SESSION_TTL_HOURS", 168) * 60 * 60 * 1000,
    serverAdminStateFile: source.SERVER_ADMIN_STATE_FILE ?? `${process.cwd()}/data/server-admin.json`,
    peerPublicListingEnabledDefault: parseBoolean(source, "PEER_PUBLIC_LISTING_ENABLED", true),
    peer: {
      enabled: parseBoolean(source, "PEER_NETWORK_ENABLED", true),
      selfAddress: source.PEER_SELF_ADDRESS ?? "",
      cfDiscoveryBaseUrl: source.PEER_CF_DISCOVERY_BASE_URL ?? "",
      stateDir: source.PEER_STATE_DIR ?? "./data/peer",
      displayNameOverride: (source.PEER_DISPLAY_NAME ?? "").trim(),
    },
    chat: loadChatConfig(source),
  };
}

function loadChatConfig(source: NodeJS.ProcessEnv): ChatConfig {
  const historyLimit = parseInteger(source, "SERVER_CHAT_HISTORY_LIMIT", 100, 1, 1000);
  const snapshotLimit = parseIntegerValue(source, "SERVER_CHAT_SNAPSHOT_LIMIT", 50);
  if (snapshotLimit < 0 || snapshotLimit > historyLimit) {
    throw new LobbyServiceConfigError(
      "invalid_range",
      "SERVER_CHAT_SNAPSHOT_LIMIT",
      "SERVER_CHAT_SNAPSHOT_LIMIT must be between 0 and SERVER_CHAT_HISTORY_LIMIT",
    );
  }

  return {
    enabled: parseBoolean(source, "SERVER_CHAT_ENABLED", false),
    historyLimit,
    historyTtlMs: parseInteger(source, "SERVER_CHAT_HISTORY_TTL_HOURS", 24, 1, 168) * 60 * 60 * 1000,
    snapshotLimit,
    maxPayloadBytes: parseInteger(source, "SERVER_CHAT_MAX_PAYLOAD_BYTES", 8192, 1024, 65536),
    maxConnectionsPerIp: parsePositiveInteger(source, "SERVER_CHAT_MAX_CONNECTIONS_PER_IP", 10),
    maxConnectionsTotal: parsePositiveInteger(source, "SERVER_CHAT_MAX_CONNECTIONS_TOTAL", 500),
    maxPendingTickets: parsePositiveInteger(source, "SERVER_CHAT_MAX_PENDING_TICKETS", 2000),
    trustedProxyCidrs: parseCommaSeparatedValues(source.SERVER_CHAT_TRUSTED_PROXY_CIDRS),
    ticketRequestsPerMinute: parsePositiveInteger(source, "SERVER_CHAT_TICKET_REQUESTS_PER_MINUTE", 20),
    ipMessagesPerMinute: parsePositiveInteger(source, "SERVER_CHAT_IP_MESSAGES_PER_MINUTE", 30),
    connectionBurst: parsePositiveInteger(source, "SERVER_CHAT_CONNECTION_BURST", 5),
    connectionRefillMs: parsePositiveInteger(source, "SERVER_CHAT_CONNECTION_REFILL_MS", 2000),
    slowClientBytes: parsePositiveInteger(source, "SERVER_CHAT_SLOW_CLIENT_BYTES", 262_144),
  };
}

function parseBoolean(source: NodeJS.ProcessEnv, name: string, fallback: boolean): boolean {
  const value = source[name];
  if (value == null) {
    return fallback;
  }

  const normalized = value.trim();
  if (normalized === "true") {
    return true;
  }
  if (normalized === "false") {
    return false;
  }

  throw new LobbyServiceConfigError(
    "invalid_boolean",
    name,
    `Invalid boolean value for ${name}: ${value}. Expected true or false.`,
  );
}

function parseInteger(source: NodeJS.ProcessEnv, name: string, fallback: number, min: number, max: number): number {
  const value = parseIntegerValue(source, name, fallback);
  if (value < min || value > max) {
    throw new LobbyServiceConfigError("invalid_range", name, `${name} must be between ${min} and ${max}`);
  }

  return value;
}

function parsePositiveInteger(source: NodeJS.ProcessEnv, name: string, fallback: number): number {
  const value = parseIntegerValue(source, name, fallback);
  if (value < 1) {
    throw new LobbyServiceConfigError("invalid_range", name, `${name} must be greater than or equal to 1`);
  }

  return value;
}

function parseIntegerValue(source: NodeJS.ProcessEnv, name: string, fallback: number): number {
  const value = source[name];
  if (value == null) {
    return fallback;
  }

  const normalized = value.trim();
  if (!/^-?\d+$/.test(normalized)) {
    throw new LobbyServiceConfigError("invalid_integer", name, `${name} must be an integer`);
  }

  const parsed = Number(normalized);
  if (!Number.isSafeInteger(parsed)) {
    throw new LobbyServiceConfigError("invalid_integer", name, `${name} must be an integer`);
  }

  return parsed;
}

function parseWebSocketPath(value: string): string {
  if (!value.startsWith("/")) {
    throw new LobbyServiceConfigError("invalid_path", "WS_PATH", "WS_PATH must begin with /");
  }
  if (value === "/chat") {
    throw new LobbyServiceConfigError("invalid_path", "WS_PATH", "WS_PATH must not be /chat");
  }

  return value;
}

function parseConnectionStrategy(value: string | undefined): ConnectionStrategy {
  const normalized = value?.trim().toLowerCase() ?? "direct-first";
  if (normalized === "direct-first" || normalized === "relay-first" || normalized === "relay-only") {
    return normalized;
  }

  throw new LobbyServiceConfigError(
    "invalid_connection_strategy",
    "CONNECTION_STRATEGY",
    `Invalid CONNECTION_STRATEGY value: ${value}`,
  );
}

function parseCommaSeparatedValues(value: string | undefined): string[] {
  if (!value) {
    return [];
  }

  return value
    .split(",")
    .map((entry) => entry.trim())
    .filter(Boolean);
}

function optionalEnv(value: string | undefined): string | undefined {
  const normalized = value?.trim();
  return normalized ? normalized : undefined;
}
