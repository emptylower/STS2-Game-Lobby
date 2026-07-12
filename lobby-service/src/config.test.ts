import assert from "node:assert/strict";
import test from "node:test";
import { loadLobbyServiceConfig } from "./config.js";

test("loadLobbyServiceConfig applies phase 1 chat defaults", () => {
  const chat = loadLobbyServiceConfig({}).chat;

  assert.deepEqual(chat, {
    enabled: false,
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

test("loadLobbyServiceConfig accepts true and false chat booleans", () => {
  assert.equal(loadLobbyServiceConfig({ SERVER_CHAT_ENABLED: "true" }).chat.enabled, true);
  assert.equal(loadLobbyServiceConfig({ SERVER_CHAT_ENABLED: "false" }).chat.enabled, false);
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
    /WS_PATH must begin with \//,
  );
});

test("loadLobbyServiceConfig reserves the chat WebSocket path", () => {
  assert.throws(
    () => loadLobbyServiceConfig({ WS_PATH: "/chat" }), /WS_PATH must not be \/chat/);
});
