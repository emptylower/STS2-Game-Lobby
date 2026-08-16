#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEFAULT_REPO_DIR="$HOME/Desktop/STS-Game-Lobby"
REPO_DIR="${STS2_RELEASE_REPO_DIR:-$DEFAULT_REPO_DIR}"
CLIENT_DIR="$ROOT_DIR/sts2-lan-connect/release/sts2_lan_connect"
CLIENT_ZIP="$ROOT_DIR/sts2-lan-connect/release/sts2_lan_connect-release.zip"
SERVICE_DIR="$ROOT_DIR/lobby-service/release/sts2_lobby_service"
SERVICE_ZIP="$ROOT_DIR/lobby-service/release/sts2_lobby_service.zip"

usage() {
  cat <<'EOF'
Usage:
  ./scripts/sync-release-repo.sh [options]

Options:
  --repo-dir <path>  Local clone of the public release repo. Default: ~/Desktop/STS-Game-Lobby
  --help             Show this help.
EOF
}

die() {
  printf '[sync-release-repo] ERROR: %s\n' "$*" >&2
  exit 1
}

log() {
  printf '[sync-release-repo] %s\n' "$*"
}

clean_repo_noise() {
  local repo_dir="$1"
  find "$repo_dir" \( -name '.DS_Store' -o -name 'Thumbs.db' \) -delete
  rm -f \
    "$repo_dir/游戏大厅mod-多端&UI优化版.zip" \
    "$repo_dir/联机大厅mod.zip" \
    "$repo_dir/游戏大厅mod-多端优化版.zip" \
    "$repo_dir/releases/联机大厅mod.zip" \
    "$repo_dir/releases/游戏大厅mod-多端优化版.zip"
}

sync_root_file() {
  local source_path="$1"
  local target_path="$2"
  mkdir -p "$(dirname "$target_path")"
  cp "$source_path" "$target_path"
}

sync_tree() {
  local source_dir="$1"
  local target_dir="$2"
  rm -rf "$target_dir"
  mkdir -p "$(dirname "$target_dir")"
  cp -R "$source_dir" "$target_dir"
}

trim_synced_source_noise() {
  rm -rf \
    "$REPO_DIR/lobby-service/node_modules" \
    "$REPO_DIR/lobby-service/dist" \
    "$REPO_DIR/lobby-service/release" \
    "$REPO_DIR/sts2-lan-connect/.godot" \
    "$REPO_DIR/sts2-lan-connect/build" \
    "$REPO_DIR/sts2-lan-connect/release"
  find "$REPO_DIR/lobby-service" "$REPO_DIR/sts2-lan-connect" "$REPO_DIR/docs" "$REPO_DIR/scripts" "$REPO_DIR/research" -name '.DS_Store' -delete 2>/dev/null || true
}

require_file() {
  local path="$1"
  [[ -f "$path" ]] || die "Required file not found: $path"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo-dir)
      [[ $# -ge 2 ]] || die "--repo-dir requires a value"
      REPO_DIR="$2"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      die "Unknown option: $1"
      ;;
  esac
done

PUBLIC_RELEASES_DIR="$REPO_DIR/releases"

[[ -d "$REPO_DIR/.git" ]] || die "Public repo '$REPO_DIR' is not a git clone."
[[ -d "$CLIENT_DIR" ]] || die "Client release directory not found: $CLIENT_DIR"
[[ -f "$CLIENT_ZIP" ]] || die "Client release zip not found: $CLIENT_ZIP"
[[ -d "$SERVICE_DIR" ]] || die "Service release directory not found: $SERVICE_DIR"
[[ -f "$SERVICE_ZIP" ]] || die "Service release zip not found: $SERVICE_ZIP"
require_file "$CLIENT_DIR/install-sts2-lan-connect-windows.bat"
require_file "$CLIENT_DIR/install-sts2-lan-connect-windows.ps1"
require_file "$CLIENT_DIR/install-sts2-lan-connect-macos.sh"
require_file "$CLIENT_DIR/install-sts2-lan-connect-macos.command"

log "Syncing source tree..."
sync_root_file "$ROOT_DIR/README.md" "$REPO_DIR/README.md"
sync_root_file "$ROOT_DIR/LICENSE" "$REPO_DIR/LICENSE"
sync_root_file "$ROOT_DIR/global.json" "$REPO_DIR/global.json"
sync_root_file "$ROOT_DIR/.gitignore" "$REPO_DIR/.gitignore"
sync_tree "$ROOT_DIR/docs" "$REPO_DIR/docs"
sync_tree "$ROOT_DIR/research" "$REPO_DIR/research"
sync_tree "$ROOT_DIR/scripts" "$REPO_DIR/scripts"
sync_tree "$ROOT_DIR/sts2-lan-connect" "$REPO_DIR/sts2-lan-connect"
sync_tree "$ROOT_DIR/lobby-service" "$REPO_DIR/lobby-service"
trim_synced_source_noise
# Keep the public repo free of the private registry/admin service and its derived artifacts.
rm -rf \
  "$REPO_DIR/server-registry" \
  "$REPO_DIR/deploy" \
  "$REPO_DIR/releases/sts2_server_registry" \
  "$REPO_DIR/releases/sts2_server_stack_docker"
rm -f \
  "$REPO_DIR/docs/STS2_SERVER_DOCKER_OPERATION_GUIDE_ZH.md" \
  "$REPO_DIR/scripts/install-server-registry-linux.sh" \
  "$REPO_DIR/scripts/install-server-stack-docker-linux.sh" \
  "$REPO_DIR/scripts/maintain-server-stack-docker.sh" \
  "$REPO_DIR/scripts/package-server-registry.sh" \
  "$REPO_DIR/scripts/package-server-stack-docker.sh" \
  "$REPO_DIR/releases/sts2_server_registry.zip" \
  "$REPO_DIR/releases/sts2_server_stack_docker.zip"

log "Syncing release artifacts..."
rm -rf "$PUBLIC_RELEASES_DIR"
mkdir -p "$PUBLIC_RELEASES_DIR"
cp -R "$CLIENT_DIR" "$PUBLIC_RELEASES_DIR/sts2_lan_connect"
cp "$CLIENT_ZIP" "$PUBLIC_RELEASES_DIR/sts2_lan_connect-release.zip"
cp -R "$SERVICE_DIR" "$PUBLIC_RELEASES_DIR/sts2_lobby_service"
cp "$SERVICE_ZIP" "$PUBLIC_RELEASES_DIR/sts2_lobby_service.zip"

log "Removing legacy release-only layout..."
rm -rf "$REPO_DIR/sts2_lan_connect" "$REPO_DIR/sts2_lobby_service"
rm -f "$REPO_DIR/sts2_lan_connect-release.zip" "$REPO_DIR/sts2_lobby_service.zip" "$REPO_DIR/联机大厅mod.zip" "$REPO_DIR/游戏大厅mod-多端优化版.zip"

clean_repo_noise "$REPO_DIR"

log "Sync finished for: $REPO_DIR"
log "Next step: git -C \"$REPO_DIR\" status"
