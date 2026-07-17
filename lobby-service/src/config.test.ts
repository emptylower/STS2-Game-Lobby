import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { LobbyServiceConfigError, loadLobbyServiceConfig } from "./config.js";

const CHAT_TEMPLATE_DEFAULTS = {
  SERVER_CHAT_ENABLED: "false",
  SERVER_CHAT_RICH_CONTENT_ENABLED: "true",
  SERVER_CHAT_EMOJI_ENABLED: "true",
  SERVER_CHAT_ITEM_REFS_ENABLED: "true",
  ROOM_CHAT_V2_ENABLED: "true",
  ROOM_CHAT_COMBAT_REFS_ENABLED: "true",
  SERVER_CHAT_HISTORY_LIMIT: "100",
  SERVER_CHAT_HISTORY_TTL_HOURS: "24",
  SERVER_CHAT_SNAPSHOT_LIMIT: "50",
  SERVER_CHAT_MAX_PAYLOAD_BYTES: "8192",
  SERVER_CHAT_MAX_CONNECTIONS_PER_IP: "10",
  SERVER_CHAT_MAX_CONNECTIONS_TOTAL: "500",
  SERVER_CHAT_MAX_PENDING_TICKETS: "2000",
  SERVER_CHAT_TRUSTED_PROXY_CIDRS: "",
  SERVER_CHAT_TICKET_REQUESTS_PER_MINUTE: "20",
  SERVER_CHAT_IP_MESSAGES_PER_MINUTE: "30",
  SERVER_CHAT_CONNECTION_BURST: "5",
  SERVER_CHAT_CONNECTION_REFILL_MS: "2000",
  SERVER_CHAT_SLOW_CLIENT_BYTES: "262144",
} as const;

const MOD_SYNC_TEMPLATE_DEFAULTS = {
  MOD_SYNC_ENABLED: "false",
  MOD_SYNC_MAX_DESCRIPTORS: "64",
  MOD_SYNC_MAX_PAYLOAD_BYTES: "65536",
} as const;

type TemplateFormat = "dotenv" | "systemd";

function readTemplateSettings(template: string, format: TemplateFormat): Array<[string, string]> {
  const settings: Array<[string, string]> = [];
  for (const line of template.split(/\r?\n/)) {
    const match = format === "dotenv"
      ? /^([A-Z][A-Z0-9_]*)=(.*)$/.exec(line)
      : /^Environment="([A-Z][A-Z0-9_]*)=(.*)"$/.exec(line);
    if (match?.[1] !== undefined && match[2] !== undefined) {
      settings.push([match[1], match[2]]);
    }
  }
  return settings;
}

test("config templates define every chat setting exactly once with parser defaults", () => {
  const templates: Array<[string, string, TemplateFormat]> = [
    [
      "lobby-service/.env.example",
      readFileSync(new URL("../.env.example", import.meta.url), "utf8"),
      "dotenv",
    ],
    [
      "deploy/lobby-service.env.example",
      readFileSync(new URL("../../deploy/lobby-service.env.example", import.meta.url), "utf8"),
      "dotenv",
    ],
    [
      "lobby-service/deploy/sts2-lobby.service.example",
      readFileSync(new URL("../deploy/sts2-lobby.service.example", import.meta.url), "utf8"),
      "systemd",
    ],
  ];
  const expectedKeys = Object.keys(CHAT_TEMPLATE_DEFAULTS).sort();
  const parserDefaults = loadLobbyServiceConfig({}).chat;

  for (const [templateName, template, format] of templates) {
    const entries = readTemplateSettings(template, format);
    const chatEntries = entries.filter(([key]) =>
      key.startsWith("SERVER_CHAT_") || key.startsWith("ROOM_CHAT_"));
    assert.deepEqual(
      chatEntries.map(([key]) => key).sort(),
      expectedKeys,
      `${templateName} must contain the exact chat key set`,
    );

    for (const [setting, expectedValue] of Object.entries(CHAT_TEMPLATE_DEFAULTS)) {
      assert.equal(
        chatEntries.filter(([key]) => key === setting).length,
        1,
        `${templateName} must define ${setting} exactly once`,
      );
      assert.equal(
        chatEntries.find(([key]) => key === setting)?.[1],
        expectedValue,
        `${templateName} must define ${setting}=${expectedValue}`,
      );
    }

    assert.deepEqual(
      loadLobbyServiceConfig(Object.fromEntries(chatEntries)).chat,
      parserDefaults,
      `${templateName} values must round-trip through the real parser`,
    );
  }

  const systemd = templates[2]?.[1] ?? "";
  assert.ok(
    systemd.lastIndexOf('Environment="SERVER_CHAT_SLOW_CLIENT_BYTES=262144"')
      < systemd.indexOf("EnvironmentFile="),
    "the operator EnvironmentFile must follow built-in defaults so it can override them",
  );
});

test("config templates define mod sync disabled with exact parser safety limits", () => {
  const templates: Array<[string, string, TemplateFormat]> = [
    ["lobby-service/.env.example", readFileSync(new URL("../.env.example", import.meta.url), "utf8"), "dotenv"],
    ["deploy/lobby-service.env.example", readFileSync(new URL("../../deploy/lobby-service.env.example", import.meta.url), "utf8"), "dotenv"],
    ["lobby-service/deploy/lobby-service.docker.env.example", readFileSync(new URL("../deploy/lobby-service.docker.env.example", import.meta.url), "utf8"), "dotenv"],
    ["lobby-service/deploy/sts2-lobby.service.example", readFileSync(new URL("../deploy/sts2-lobby.service.example", import.meta.url), "utf8"), "systemd"],
  ];

  for (const [templateName, template, format] of templates) {
    const entries = readTemplateSettings(template, format).filter(([key]) => key.startsWith("MOD_SYNC_"));
    assert.deepEqual(
      Object.fromEntries(entries),
      MOD_SYNC_TEMPLATE_DEFAULTS,
      `${templateName} must define mod sync defaults exactly once`,
    );
  }

  const readme = readFileSync(new URL("../README.md", import.meta.url), "utf8");
  assert.match(readme, /MOD_SYNC_ENABLED/);
  assert.match(readme, /默认关闭/);
  assert.match(readme, /MOD 清单.*不.*公开.*\/rooms/s);
  assert.match(readme, /不会.*(?:DLL|PCK|ZIP).*(?:下载|传输)/s);
});

test("README documents CREATE_ROOM_TOKEN read fallback without granting chat ticket access", () => {
  const readme = readFileSync(new URL("../README.md", import.meta.url), "utf8");

  assert.match(
    readme,
    /When `LOBBY_ACCESS_TOKEN` is unset, `CREATE_ROOM_TOKEN` also authorizes protected room-list and detailed-health reads\./,
  );
  assert.match(readme, /`CREATE_ROOM_TOKEN` alone does not authorize chat tickets\./);
});

test("operator docs lock chat privacy generation rollback and temporary release boundaries", () => {
  const documents: Array<[string, string]> = [
    ["README.md", readFileSync(new URL("../../README.md", import.meta.url), "utf8")],
    ["lobby-service/README.md", readFileSync(new URL("../README.md", import.meta.url), "utf8")],
    [
      "docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md",
      readFileSync(new URL("../../docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md", import.meta.url), "utf8"),
    ],
  ];

  for (const [name, document] of documents) {
    for (const required of [
      /(?:未验证|unverified)/i,
      /roomSessionId/,
      /60 UTF-16/i,
      /monster/i,
      /SERVER_ADMIN_STATE_FILE/,
      /lobby-service\/\.env\.example/,
      /deploy\/lobby-service\.env\.example/,
      /lobby-service\/deploy\/sts2-lobby\.service\.example/,
      /releases\//,
      /typing\.dll/i,
    ]) {
      assert.match(document, required, `${name} missing ${required}`);
    }
    assert.doesNotMatch(document, /persistent chat history|verified nickname|持久化聊天历史|已验证昵称/i);
  }
});

test("loadLobbyServiceConfig applies phase 1 chat defaults", () => {
  const chat = loadLobbyServiceConfig({}).chat;

  assert.deepEqual(chat, {
    features: {
      serverChatEnabled: false,
      richContentEnabled: true,
      emojiEnabled: true,
      itemRefsEnabled: true,
      roomChatV2Enabled: true,
      roomCombatRefsEnabled: true,
    },
    historyLimit: 100,
    historyTtlMs: 86_400_000,
    snapshotLimit: 50,
    maxPayloadBytes: 8192,
    maxConnectionsPerIp: 10,
    maxConnectionsTotal: 500,
    maxPendingTickets: 2000,
    trustedProxyCidrs: [],
    ticketRequestsPerMinute: 20,
    ipMessagesPerMinute: 30,
    connectionBurst: 5,
    connectionRefillMs: 2000,
    slowClientBytes: 262_144,
  });
});

test("snapshot limit cannot exceed history", () => {
  assert.throws(
    () => loadLobbyServiceConfig({ SERVER_CHAT_HISTORY_LIMIT: "20", SERVER_CHAT_SNAPSHOT_LIMIT: "50" }),
    /SERVER_CHAT_SNAPSHOT_LIMIT must be between 0 and SERVER_CHAT_HISTORY_LIMIT/,
  );
});

test("loadLobbyServiceConfig returns structured errors for malformed configuration", () => {
  assert.throws(
    () => loadLobbyServiceConfig({ SERVER_CHAT_ENABLED: "yes" }),
    (error: unknown) =>
      error instanceof Error &&
      "code" in error &&
      error.code === "invalid_boolean" &&
      "environmentKey" in error &&
      error.environmentKey === "SERVER_CHAT_ENABLED",
  );
});

test("loadLobbyServiceConfig rejects malformed boolean values", () => {
  assert.throws(
    () => loadLobbyServiceConfig({ SERVER_CHAT_ENABLED: "yes" }),
    /Invalid boolean value for SERVER_CHAT_ENABLED/,
  );
});

test("loadLobbyServiceConfig accepts true and false governance booleans", () => {
  const settings = [
    ["SERVER_CHAT_ENABLED", "serverChatEnabled"],
    ["SERVER_CHAT_RICH_CONTENT_ENABLED", "richContentEnabled"],
    ["SERVER_CHAT_EMOJI_ENABLED", "emojiEnabled"],
    ["SERVER_CHAT_ITEM_REFS_ENABLED", "itemRefsEnabled"],
    ["ROOM_CHAT_V2_ENABLED", "roomChatV2Enabled"],
    ["ROOM_CHAT_COMBAT_REFS_ENABLED", "roomCombatRefsEnabled"],
  ] as const;
  for (const [environmentKey, property] of settings) {
    assert.equal(loadLobbyServiceConfig({ [environmentKey]: "true" }).chat.features[property], true);
    assert.equal(loadLobbyServiceConfig({ [environmentKey]: "false" }).chat.features[property], false);
  }
});

test("loadLobbyServiceConfig rejects non-literal governance booleans", () => {
  for (const environmentKey of [
    "SERVER_CHAT_ENABLED",
    "SERVER_CHAT_RICH_CONTENT_ENABLED",
    "SERVER_CHAT_EMOJI_ENABLED",
    "SERVER_CHAT_ITEM_REFS_ENABLED",
    "ROOM_CHAT_V2_ENABLED",
    "ROOM_CHAT_COMBAT_REFS_ENABLED",
  ]) {
    for (const value of ["TRUE", "1", "yes", " true ", ""]) {
      assert.throws(
        () => loadLobbyServiceConfig({ [environmentKey]: value }),
        new RegExp(`Invalid boolean value for ${environmentKey}`),
      );
    }
  }
});

test("loadLobbyServiceConfig preserves legacy boolean aliases and blank defaults", () => {
  const booleanSettings = [
    ["STRICT_GAME_VERSION_CHECK", "strictGameVersionCheck", true],
    ["STRICT_MOD_VERSION_CHECK", "strictModVersionCheck", true],
    ["PUBLIC_ROOM_LIST_ENABLED", "publicRoomListEnabled", false],
    ["PUBLIC_DETAILED_HEALTH_ENABLED", "publicDetailedHealthEnabled", false],
    ["ENFORCE_LOBBY_ACCESS_TOKEN", "enforceLobbyAccessToken", true],
    ["ENFORCE_CREATE_ROOM_TOKEN", "enforceCreateRoomToken", true],
    ["PEER_PUBLIC_LISTING_ENABLED", "peerPublicListingEnabledDefault", true],
  ] as const;

  for (const [environmentKey, configKey, fallback] of booleanSettings) {
    assert.equal(loadLobbyServiceConfig({ [environmentKey]: "" })[configKey], fallback);

    for (const value of ["1", "TRUE", "yes", "On"]) {
      assert.equal(loadLobbyServiceConfig({ [environmentKey]: value })[configKey], true);
    }
    for (const value of ["0", "FALSE", "no", "Off"]) {
      assert.equal(loadLobbyServiceConfig({ [environmentKey]: value })[configKey], false);
    }
  }
});

test("loadLobbyServiceConfig preserves PEER_NETWORK_ENABLED exact false behavior", () => {
  assert.equal(loadLobbyServiceConfig({ PEER_NETWORK_ENABLED: "false" }).peer.enabled, false);
  assert.equal(loadLobbyServiceConfig({ PEER_NETWORK_ENABLED: "FALSE" }).peer.enabled, true);
  assert.equal(loadLobbyServiceConfig({ PEER_NETWORK_ENABLED: "0" }).peer.enabled, true);
  assert.equal(loadLobbyServiceConfig({ PEER_NETWORK_ENABLED: "" }).peer.enabled, true);
});

test("loadLobbyServiceConfig preserves legacy numeric prefix parsing and rate limit clamping inputs", () => {
  const config = loadLobbyServiceConfig({
    HEARTBEAT_TIMEOUT_SECONDS: "30s",
    CREATE_JOIN_RATE_LIMIT_WINDOW_MS: "0",
    CREATE_JOIN_RATE_LIMIT_MAX_REQUESTS: "0",
  });

  assert.equal(config.heartbeatTimeoutMs, 30_000);
  assert.equal(Math.max(1000, config.createJoinRateLimitWindowMs), 1000);
  assert.equal(Math.max(1, config.createJoinRateLimitMaxRequests), 1);
});

test("loadLobbyServiceConfig rejects malformed integer values", () => {
  assert.throws(
    () => loadLobbyServiceConfig({ SERVER_CHAT_HISTORY_LIMIT: "twenty" }),
    /SERVER_CHAT_HISTORY_LIMIT must be an integer/,
  );
});

test("loadLobbyServiceConfig validates chat payload byte bounds", () => {
  assert.throws(
    () => loadLobbyServiceConfig({ SERVER_CHAT_MAX_PAYLOAD_BYTES: "1023" }),
    /SERVER_CHAT_MAX_PAYLOAD_BYTES must be between 1024 and 65536/,
  );
  assert.throws(
    () => loadLobbyServiceConfig({ SERVER_CHAT_MAX_PAYLOAD_BYTES: "65537" }),
    /SERVER_CHAT_MAX_PAYLOAD_BYTES must be between 1024 and 65536/,
  );
  assert.equal(loadLobbyServiceConfig({ SERVER_CHAT_MAX_PAYLOAD_BYTES: "1024" }).chat.maxPayloadBytes, 1024);
  assert.equal(loadLobbyServiceConfig({ SERVER_CHAT_MAX_PAYLOAD_BYTES: "65536" }).chat.maxPayloadBytes, 65536);
});

test("loadLobbyServiceConfig validates chat history bounds", () => {
  assert.throws(
    () => loadLobbyServiceConfig({ SERVER_CHAT_HISTORY_LIMIT: "0" }),
    /SERVER_CHAT_HISTORY_LIMIT must be between 1 and 1000/,
  );
  assert.throws(
    () => loadLobbyServiceConfig({ SERVER_CHAT_HISTORY_LIMIT: "1001" }),
    /SERVER_CHAT_HISTORY_LIMIT must be between 1 and 1000/,
  );
});

test("loadLobbyServiceConfig accepts chat history limit lower and upper bounds", () => {
  assert.equal(
    loadLobbyServiceConfig({ SERVER_CHAT_HISTORY_LIMIT: "1", SERVER_CHAT_SNAPSHOT_LIMIT: "1" }).chat.historyLimit,
    1,
  );
  assert.equal(
    loadLobbyServiceConfig({ SERVER_CHAT_HISTORY_LIMIT: "1000", SERVER_CHAT_SNAPSHOT_LIMIT: "1000" }).chat.historyLimit,
    1000,
  );
});

test("loadLobbyServiceConfig validates chat history TTL bounds", () => {
  assert.throws(
    () => loadLobbyServiceConfig({ SERVER_CHAT_HISTORY_TTL_HOURS: "0" }),
    /SERVER_CHAT_HISTORY_TTL_HOURS must be between 1 and 168/,
  );
  assert.throws(
    () => loadLobbyServiceConfig({ SERVER_CHAT_HISTORY_TTL_HOURS: "169" }),
    /SERVER_CHAT_HISTORY_TTL_HOURS must be between 1 and 168/,
  );
  assert.equal(loadLobbyServiceConfig({ SERVER_CHAT_HISTORY_TTL_HOURS: "168" }).chat.historyTtlMs, 604_800_000);
});

test("loadLobbyServiceConfig accepts the chat history TTL lower bound", () => {
  assert.equal(loadLobbyServiceConfig({ SERVER_CHAT_HISTORY_TTL_HOURS: "1" }).chat.historyTtlMs, 3_600_000);
});

test("loadLobbyServiceConfig requires positive chat capacities", () => {
  for (const name of [
    "SERVER_CHAT_MAX_CONNECTIONS_PER_IP",
    "SERVER_CHAT_MAX_CONNECTIONS_TOTAL",
    "SERVER_CHAT_MAX_PENDING_TICKETS",
  ]) {
    assert.throws(
      () => loadLobbyServiceConfig({ [name]: "0" }),
      new RegExp(`${name} must be greater than or equal to 1`),
    );
  }
});

test("loadLobbyServiceConfig parses comma-separated chat trusted proxy CIDRs", () => {
  const { trustedProxyCidrs } = loadLobbyServiceConfig({
    SERVER_CHAT_TRUSTED_PROXY_CIDRS: " 10.0.0.0/8, 192.168.0.0/16 ,2001:db8::/32 ",
  }).chat;

  assert.deepEqual(trustedProxyCidrs, ["10.0.0.0/8", "192.168.0.0/16", "2001:db8::/32"]);
});

test("loadLobbyServiceConfig requires WS_PATH to begin with a slash", () => {
  assert.throws(
    () => loadLobbyServiceConfig({ WS_PATH: "control" }),
    (error: unknown) =>
      error instanceof LobbyServiceConfigError &&
      error.code === "invalid_path" &&
      error.environmentKey === "WS_PATH" &&
      error.message === "WS_PATH must begin with /",
  );
});

test("loadLobbyServiceConfig reserves WS_PATH /chat for the chat upgrade route", () => {
  assert.throws(
    () => loadLobbyServiceConfig({ WS_PATH: "/chat" }),
    (error: unknown) =>
      error instanceof LobbyServiceConfigError &&
      error.code === "invalid_path" &&
      error.environmentKey === "WS_PATH" &&
      error.message === "WS_PATH must not be /chat",
  );
});

test("loadLobbyServiceConfig preserves the legacy CONNECTION_STRATEGY error", () => {
  assert.throws(
    () => loadLobbyServiceConfig({ CONNECTION_STRATEGY: "unsupported" }),
    (error: unknown) =>
      error instanceof Error &&
      error.constructor === Error &&
      error.name === "Error" &&
      error.message === "Invalid CONNECTION_STRATEGY value: unsupported",
  );
});

test("mod sync config defaults disabled with protocol safety limits", () => {
  const config = loadLobbyServiceConfig({});
  assert.equal(config.modSyncEnabled, false);
  assert.equal(config.modSyncMaxDescriptors, 64);
  assert.equal(config.modSyncMaxPayloadBytes, 65_536);
});

test("mod sync config parses explicit enable and bounded limits", () => {
  const config = loadLobbyServiceConfig({
    MOD_SYNC_ENABLED: "true",
    MOD_SYNC_MAX_DESCRIPTORS: "32",
    MOD_SYNC_MAX_PAYLOAD_BYTES: "32768",
  });
  assert.equal(config.modSyncEnabled, true);
  assert.equal(config.modSyncMaxDescriptors, 32);
  assert.equal(config.modSyncMaxPayloadBytes, 32_768);

  assert.throws(
    () => loadLobbyServiceConfig({ MOD_SYNC_MAX_DESCRIPTORS: "65" }),
    (error: unknown) => error instanceof LobbyServiceConfigError && error.environmentKey === "MOD_SYNC_MAX_DESCRIPTORS",
  );
  assert.throws(
    () => loadLobbyServiceConfig({ MOD_SYNC_MAX_PAYLOAD_BYTES: "65537" }),
    (error: unknown) => error instanceof LobbyServiceConfigError && error.environmentKey === "MOD_SYNC_MAX_PAYLOAD_BYTES",
  );
});
