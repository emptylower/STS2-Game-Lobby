import { LobbyStoreError, type ConnectionStrategy, type RelayState } from "./store.js";

export function assertRelayCreateReady(strategy: ConnectionStrategy, hasRelayEndpoint: boolean) {
  if (strategy !== "relay-only") {
    return;
  }

  if (hasRelayEndpoint) {
    return;
  }

  throw new LobbyStoreError(503, "relay_unavailable", "当前 relay 资源已满，请稍后再试。");
}

export function assertRelayJoinReady(
  strategy: ConnectionStrategy,
  _relayState: RelayState,
  hasActiveHost: boolean,
) {
  if (strategy !== "relay-only") {
    return;
  }

  if (hasActiveHost) {
    return;
  }

  throw new LobbyStoreError(409, "relay_host_not_ready", "房主的 relay 尚未注册完成，请稍后刷新后再试。");
}
