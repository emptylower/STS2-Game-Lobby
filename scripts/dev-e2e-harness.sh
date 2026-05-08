#!/usr/bin/env bash
# scripts/dev-e2e-harness.sh
# 起 1 个 Worker (wrangler dev) + 3 个 lobby v0.3 + 1 个 sidecar，输出端点。
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cleanup() { kill $(jobs -p) 2>/dev/null || true; }
trap cleanup EXIT

cd "$ROOT_DIR/cf-worker" && npm run dev &
sleep 2

CF_URL="http://127.0.0.1:8787"
echo "CF Worker: $CF_URL"

cd "$ROOT_DIR/lobby-service" && npm run build

PORT_A=18001 PORT_B=18002 PORT_C=18003

PEER_SELF_ADDRESS="http://127.0.0.1:$PORT_A" PEER_STATE_DIR="/tmp/peer-A" \
PEER_CF_DISCOVERY_BASE_URL="$CF_URL" PORT="$PORT_A" \
node "$ROOT_DIR/lobby-service/dist/server.js" &
PEER_SELF_ADDRESS="http://127.0.0.1:$PORT_B" PEER_STATE_DIR="/tmp/peer-B" \
PEER_CF_DISCOVERY_BASE_URL="$CF_URL" PORT="$PORT_B" \
node "$ROOT_DIR/lobby-service/dist/server.js" &
PEER_SELF_ADDRESS="http://127.0.0.1:$PORT_C" PEER_STATE_DIR="/tmp/peer-C" \
PEER_CF_DISCOVERY_BASE_URL="$CF_URL" PORT="$PORT_C" \
node "$ROOT_DIR/lobby-service/dist/server.js" &

cd "$ROOT_DIR/sts2-peer-sidecar" && npm run build
LOBBY_PUBLIC_BASE_URL="http://127.0.0.1:$PORT_A" PEER_LISTEN_PORT=18800 \
PEER_STATE_DIR="/tmp/sidecar" PEER_CF_DISCOVERY_BASE_URL="$CF_URL" \
node "$ROOT_DIR/sts2-peer-sidecar/dist/index.js" &

echo ""
echo "Endpoints:"
echo "  CF Worker  : $CF_URL"
echo "  Lobby A    : http://127.0.0.1:$PORT_A"
echo "  Lobby B    : http://127.0.0.1:$PORT_B"
echo "  Lobby C    : http://127.0.0.1:$PORT_C"
echo "  Sidecar    : http://127.0.0.1:18800"
echo ""
wait
