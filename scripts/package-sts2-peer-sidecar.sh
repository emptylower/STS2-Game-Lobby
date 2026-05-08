#!/usr/bin/env bash
# scripts/package-sts2-peer-sidecar.sh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SIDECAR_DIR="$ROOT_DIR/sts2-peer-sidecar"
OUT_DIR="$ROOT_DIR/releases/sts2_peer_sidecar"
TARBALL="$OUT_DIR/sts2-peer-sidecar.tar.gz"

cd "$ROOT_DIR/lobby-service" && npm run build
cd "$SIDECAR_DIR" && npm install && npm run build

mkdir -p "$OUT_DIR"
rm -f "$TARBALL"

tar -czf "$TARBALL" \
  -C "$ROOT_DIR" \
  sts2-peer-sidecar/dist \
  sts2-peer-sidecar/node_modules \
  sts2-peer-sidecar/package.json \
  sts2-peer-sidecar/deploy

echo "wrote $TARBALL"
