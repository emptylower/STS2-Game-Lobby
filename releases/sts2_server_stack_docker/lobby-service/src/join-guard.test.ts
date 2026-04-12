import test from "node:test";
import assert from "node:assert/strict";
import { assertRelayCreateReady, assertRelayJoinReady } from "./join-guard.js";
import { LobbyStoreError } from "./store.js";

test("assertRelayCreateReady rejects relay-only room creation without an allocated relay endpoint", () => {
  assert.throws(
    () => assertRelayCreateReady("relay-only", false),
    (error: unknown) =>
      error instanceof LobbyStoreError &&
      error.code === "relay_unavailable" &&
      error.statusCode === 503,
  );
});

test("assertRelayCreateReady allows relay-only room creation when a relay endpoint exists", () => {
  assert.doesNotThrow(() => assertRelayCreateReady("relay-only", true));
});

test("assertRelayJoinReady rejects relay-only rooms before host registration", () => {
  assert.throws(
    () => assertRelayJoinReady("relay-only", "planned", false),
    (error: unknown) =>
      error instanceof LobbyStoreError &&
      error.code === "relay_host_not_ready" &&
      error.statusCode === 409,
  );
});

test("assertRelayJoinReady allows relay-only rooms after host registration", () => {
  assert.doesNotThrow(() => assertRelayJoinReady("relay-only", "ready", true));
});

test("assertRelayJoinReady does not block non-relay-only strategies", () => {
  assert.doesNotThrow(() => assertRelayCreateReady("direct-first", false));
  assert.doesNotThrow(() => assertRelayCreateReady("relay-first", false));
  assert.doesNotThrow(() => assertRelayJoinReady("relay-first", "planned", false));
  assert.doesNotThrow(() => assertRelayJoinReady("direct-first", "disabled", false));
});
