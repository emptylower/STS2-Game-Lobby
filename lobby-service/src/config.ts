import type { ConnectionStrategy } from "./store.js";
import type { ChatFeatureGovernance } from "./chat/feature-resolver.js";

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
  features: ChatFeatureSettings;
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

export type ChatFeatureSettings = ChatFeatureGovernance;

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
  modSyncEnabled: boolean;
  modSyncMaxDescriptors: number;
  modSyncMaxPayloadBytes: number;
  connectionStrategy: ConnectionStrategy;
  publicRoomListEnabled: boolean;
  publicDetailedHealthEnabled: boolean;
  enforceLobbyAccessToken: boolean;
  enforceCreateRoomToken: boolean;
  lobbyAccessToken?: string;
  chatAccessToken?: string;
  createRoomToken?: string;
  createRoomTrustedProxies: string[];
  createJoinRateLimitWindowMs: number;
  createJoinRateLimitMaxRequests: number;
  serverAdminUsername: string;
  serverAdminPasswordHash?: string;
  serverAdminSessionSecret?: string;
  serverAdminSessionTtlMs: number;
  serverAdminStateFile: string;
  serverUpdateEnabled: boolean;
  serverUpdateDataDir: string;
  serverUpdateCheckIntervalMs: number;
  serverUpdateReleaseApiUrl?: string;
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
  const chatAccessToken = optionalEnv(source.LOBBY_ACCESS_TOKEN);
  const lobbyAccessToken = chatAccessToken ?? optionalEnv(source.CREATE_ROOM_TOKEN);
  const createRoomToken = optionalEnv(source.CREATE_ROOM_TOKEN) ?? optionalEnv(source.LOBBY_ACCESS_TOKEN);
  const serverAdminPasswordHash = optionalEnv(source.SERVER_ADMIN_PASSWORD_HASH);
  const serverAdminSessionSecret = optionalEnv(source.SERVER_ADMIN_SESSION_SECRET);
  const serverUpdateReleaseApiUrl = optionalEnv(source.SERVER_UPDATE_RELEASES_API_URL);

  return {
    host,
    port: parseLegacyInteger(source, "PORT", 8787),
    heartbeatTimeoutMs: parseLegacyInteger(source, "HEARTBEAT_TIMEOUT_SECONDS", 35) * 1000,
    ticketTtlMs: parseLegacyInteger(source, "TICKET_TTL_SECONDS", 120) * 1000,
    wsPath: parseWebSocketPath(source.WS_PATH ?? "/control"),
    relayBindHost: source.RELAY_BIND_HOST ?? host,
    relayPublicHost: source.RELAY_PUBLIC_HOST ?? "",
    relayPortStart: parseLegacyInteger(source, "RELAY_PORT_START", 39000),
    relayPortEnd: parseLegacyInteger(source, "RELAY_PORT_END", 39149),
    relayHostIdleMs: parseLegacyInteger(source, "RELAY_HOST_IDLE_SECONDS", 20) * 1000,
    relayClientIdleMs: parseLegacyInteger(source, "RELAY_CLIENT_IDLE_SECONDS", 90) * 1000,
    strictGameVersionCheck: parseLegacyBoolean(source.STRICT_GAME_VERSION_CHECK, true),
    strictModVersionCheck: parseLegacyBoolean(source.STRICT_MOD_VERSION_CHECK, true),
    modSyncEnabled: parseBoolean(source, "MOD_SYNC_ENABLED", true),
    modSyncMaxDescriptors: parseInteger(source, "MOD_SYNC_MAX_DESCRIPTORS", 64, 1, 64),
    modSyncMaxPayloadBytes: parseInteger(source, "MOD_SYNC_MAX_PAYLOAD_BYTES", 65_536, 1024, 65_536),
    connectionStrategy: parseConnectionStrategy(source.CONNECTION_STRATEGY),
    publicRoomListEnabled: parseLegacyBoolean(source.PUBLIC_ROOM_LIST_ENABLED, false),
    publicDetailedHealthEnabled: parseLegacyBoolean(source.PUBLIC_DETAILED_HEALTH_ENABLED, false),
    enforceLobbyAccessToken: parseLegacyBoolean(source.ENFORCE_LOBBY_ACCESS_TOKEN, true),
    enforceCreateRoomToken: parseLegacyBoolean(source.ENFORCE_CREATE_ROOM_TOKEN, true),
    ...(lobbyAccessToken == null ? {} : { lobbyAccessToken }),
    ...(chatAccessToken == null ? {} : { chatAccessToken }),
    ...(createRoomToken == null ? {} : { createRoomToken }),
    createRoomTrustedProxies: parseCommaSeparatedValues(source.CREATE_ROOM_TRUSTED_PROXIES),
    createJoinRateLimitWindowMs: parseLegacyInteger(source, "CREATE_JOIN_RATE_LIMIT_WINDOW_MS", 60000),
    createJoinRateLimitMaxRequests: parseLegacyInteger(source, "CREATE_JOIN_RATE_LIMIT_MAX_REQUESTS", 30),
    serverAdminUsername: source.SERVER_ADMIN_USERNAME ?? "admin",
    ...(serverAdminPasswordHash == null ? {} : { serverAdminPasswordHash }),
    ...(serverAdminSessionSecret == null ? {} : { serverAdminSessionSecret }),
    serverAdminSessionTtlMs: parseLegacyInteger(source, "SERVER_ADMIN_SESSION_TTL_HOURS", 168) * 60 * 60 * 1000,
    serverAdminStateFile: source.SERVER_ADMIN_STATE_FILE ?? `${process.cwd()}/data/server-admin.json`,
    serverUpdateEnabled: parseBoolean(source, "SERVER_UPDATE_ENABLED", true),
    serverUpdateDataDir: source.SERVER_UPDATE_DATA_DIR ?? `${process.cwd()}/data/service-update`,
    serverUpdateCheckIntervalMs: parseInteger(source, "SERVER_UPDATE_CHECK_INTERVAL_MINUTES", 360, 15, 1440) * 60 * 1000,
    ...(serverUpdateReleaseApiUrl == null ? {} : { serverUpdateReleaseApiUrl }),
    peerPublicListingEnabledDefault: parseLegacyBoolean(source.PEER_PUBLIC_LISTING_ENABLED, true),
    peer: {
      enabled: source.PEER_NETWORK_ENABLED !== "false",
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
    features: {
      serverChatEnabled: parseBoolean(source, "SERVER_CHAT_ENABLED", false),
      richContentEnabled: parseBoolean(source, "SERVER_CHAT_RICH_CONTENT_ENABLED", true),
      emojiEnabled: parseBoolean(source, "SERVER_CHAT_EMOJI_ENABLED", true),
      itemRefsEnabled: parseBoolean(source, "SERVER_CHAT_ITEM_REFS_ENABLED", true),
      roomChatV2Enabled: parseBoolean(source, "ROOM_CHAT_V2_ENABLED", true),
      roomCombatRefsEnabled: parseBoolean(source, "ROOM_CHAT_COMBAT_REFS_ENABLED", true),
    },
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

function parseLegacyBoolean(value: string | undefined, fallback: boolean): boolean {
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

function parseBoolean(source: NodeJS.ProcessEnv, name: string, fallback: boolean): boolean {
  const value = source[name];
  if (value == null) {
    return fallback;
  }

  const normalized = value;
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

function parseLegacyInteger(source: NodeJS.ProcessEnv, name: string, fallback: number): number {
  return Number.parseInt(source[name] ?? `${fallback}`, 10);
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

  throw new Error(`Invalid CONNECTION_STRATEGY value: ${value}`);
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
