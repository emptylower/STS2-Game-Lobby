#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=release-package-common.sh
source "$ROOT_DIR/scripts/release-package-common.sh"

SOURCE_DIR="$ROOT_DIR/lobby-service"
PACKAGE_NAME="sts2_lobby_service"
ZIP_NAME="$PACKAGE_NAME.zip"
INSTALLER="$ROOT_DIR/scripts/install-lobby-service-linux.sh"
OUTPUT_EXPLICIT=0
OUTPUT_DIR_RAW=""
WORK_DIR=""

usage() {
  cat <<'EOF'
Usage:
  ./scripts/package-lobby-service.sh [options]

Options:
  --output-dir <path>  Write the staged package and zip below this directory.
  --help               Show this help.
EOF
}

cleanup() {
  if [[ -n "$WORK_DIR" && -d "$WORK_DIR" ]]; then
    rm -rf "$WORK_DIR"
  fi
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output-dir)
      [[ "$OUTPUT_EXPLICIT" -eq 0 ]] || release_die "--output-dir may be specified only once"
      [[ $# -ge 2 && -n "$2" && "$2" != --* ]] || release_die "--output-dir requires a value"
      OUTPUT_EXPLICIT=1
      OUTPUT_DIR_RAW="$2"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      release_die "Unknown option: $1"
      ;;
  esac
done

if [[ "$OUTPUT_EXPLICIT" -eq 1 ]]; then
  release_prepare_output_dir "$OUTPUT_DIR_RAW" "$ROOT_DIR" 0
else
  release_prepare_output_dir "$SOURCE_DIR/release" "$ROOT_DIR" 1
fi

WORK_DIR="$(mktemp -d "$RELEASE_OUTPUT_DIR/.tmp-service-package.XXXXXX")"
trap cleanup EXIT INT TERM HUP
STAGE_PARENT="$WORK_DIR/stage"
PACKAGE_ROOT="$STAGE_PARENT/$PACKAGE_NAME"
STAGED_ZIP="$WORK_DIR/$ZIP_NAME"
EXPECTED_MANIFEST="$WORK_DIR/expected-files.txt"
mkdir -p "$PACKAGE_ROOT"
cat > "$EXPECTED_MANIFEST" <<'EOF'
LICENSE
THIRD_PARTY_NOTICES
README.md
install-lobby-service-linux.sh
lobby-service/.dockerignore
lobby-service/.env.example
lobby-service/Dockerfile
lobby-service/package-lock.json
lobby-service/package.json
lobby-service/tsconfig.json
lobby-service/scripts/generate-server-admin-password-hash.mjs
lobby-service/deploy/.env.example
lobby-service/deploy/docker-compose.lobby-service.yml
lobby-service/deploy/lobby-service.docker.env.example
lobby-service/deploy/sts2-lobby.service.example
lobby-service/src/app.ts
lobby-service/src/bandwidth-guard.ts
lobby-service/src/client-ip.ts
lobby-service/src/config.ts
lobby-service/src/join-guard.ts
lobby-service/src/relay.ts
lobby-service/src/rolling-bandwidth.ts
lobby-service/src/room-cleanup.ts
lobby-service/src/server-admin-auth.ts
lobby-service/src/server-admin-state.ts
lobby-service/src/server-admin-ui.ts
lobby-service/src/server.ts
lobby-service/src/store.ts
lobby-service/src/chat/dedupe-cache.ts
lobby-service/src/chat/feature-resolver.ts
lobby-service/src/chat/gateway.ts
lobby-service/src/chat/history-buffer.ts
lobby-service/src/chat/peer-registry.ts
lobby-service/src/chat/protocol.ts
lobby-service/src/chat/rate-limiter.ts
lobby-service/src/chat/room-gateway.ts
lobby-service/src/chat/ticket-store.ts
lobby-service/src/chat/upgrade-router.ts
lobby-service/src/peer/auto-announce.ts
lobby-service/src/peer/bootstrap.ts
lobby-service/src/peer/gossip.ts
lobby-service/src/peer/identity.ts
lobby-service/src/peer/prober.ts
lobby-service/src/peer/seeds-loader.ts
lobby-service/src/peer/store.ts
lobby-service/src/peer/types.ts
lobby-service/src/peer/handlers/announce.ts
lobby-service/src/peer/handlers/health.ts
lobby-service/src/peer/handlers/heartbeat.ts
lobby-service/src/peer/handlers/list.ts
lobby-service/src/peer/handlers/metrics.ts
EOF

while IFS= read -r package_path; do
  case "$package_path" in
    LICENSE|THIRD_PARTY_NOTICES)
      source_path="$ROOT_DIR/$package_path"
      ;;
    README.md)
      source_path="$SOURCE_DIR/README.md"
      ;;
    install-lobby-service-linux.sh)
      source_path="$INSTALLER"
      ;;
    lobby-service/*)
      source_path="$ROOT_DIR/$package_path"
      ;;
    *)
      release_die "Unknown service allowlist entry: $package_path"
      ;;
  esac
  mode=0644
  [[ "$package_path" != "install-lobby-service-linux.sh" ]] || mode=0755
  release_copy_file "$source_path" "$PACKAGE_ROOT/$package_path" "$mode"
done < "$EXPECTED_MANIFEST"

release_assert_manifest "$PACKAGE_ROOT" "$EXPECTED_MANIFEST" "$WORK_DIR"
release_normalize_tree "$PACKAGE_ROOT"
release_create_deterministic_zip "$STAGE_PARENT" "$PACKAGE_NAME" "$STAGED_ZIP"
release_assert_zip_manifest "$STAGED_ZIP" "$PACKAGE_NAME" "$EXPECTED_MANIFEST" "$WORK_DIR"
release_assert_legal_bytes "$PACKAGE_ROOT" "$STAGED_ZIP" "$PACKAGE_NAME" "$ROOT_DIR"
release_publish_atomic "$PACKAGE_ROOT" "$STAGED_ZIP" "$RELEASE_OUTPUT_DIR" "$PACKAGE_NAME" "$ZIP_NAME" "$WORK_DIR"

FINAL_ZIP="$RELEASE_OUTPUT_DIR/$ZIP_NAME"
printf 'Package directory: %s\n' "$RELEASE_OUTPUT_DIR/$PACKAGE_NAME"
printf 'Package archive: %s\n' "$FINAL_ZIP"
printf 'Package SHA-256: %s\n' "$(shasum -a 256 "$FINAL_ZIP" | awk '{print $1}')"
