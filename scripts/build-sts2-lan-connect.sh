#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_DIR="$ROOT_DIR/sts2-lan-connect"
ASSEMBLY_NAME="sts2_lan_connect"
DOTNET_BIN="${DOTNET_BIN:-$(command -v dotnet || true)}"
GODOT_BIN="${GODOT_BIN:-$(command -v godot451-mono || command -v godot || true)}"
INSTALL_SCRIPT="$ROOT_DIR/scripts/install-sts2-lan-connect-macos.sh"
DEFAULT_BUILD_OUTPUT_DIR="$PROJECT_DIR/release/.build_mod_output/$ASSEMBLY_NAME"
MOD_OUTPUT_DIR="${STS2_MODS_DIR:-$DEFAULT_BUILD_OUTPUT_DIR}"
BUILD_LOCK_DIR="$PROJECT_DIR/release/.build_lock"
GODOT_LOG_FILE="${GODOT_LOG_FILE:-${TMPDIR:-/tmp}/sts2_lan_connect_build_${$}.log}"
PCK_SOURCE="$PROJECT_DIR/build/$ASSEMBLY_NAME.pck"
DLL_SOURCE="$PROJECT_DIR/.godot/mono/temp/bin/Debug/$ASSEMBLY_NAME.dll"
DEFAULTS_FILE_NAME="lobby-defaults.json"
MANIFEST_FILE_NAME="$ASSEMBLY_NAME.json"
LEGACY_MANIFEST_FILE_NAME="mod_manifest.json"
LOCAL_DEFAULTS_FILE="$PROJECT_DIR/$DEFAULTS_FILE_NAME"
LOCAL_MANIFEST_FILE="$PROJECT_DIR/$MANIFEST_FILE_NAME"
INSTALL_AFTER_BUILD=0
SYNC_SAVES=1

usage() {
  cat <<'EOF'
Usage:
  ./scripts/build-sts2-lan-connect.sh [options]

Options:
  --install           Build into the staging directory, then run the macOS installer.
  --mods-dir <path>   Override the build staging directory.
  --no-save-sync      Forwarded to the installer when used with --install.
  --help              Show this help.

Environment:
  STS2_MODS_DIR
  STS2_LOBBY_DEFAULT_BASE_URL
  STS2_LOBBY_DEFAULT_WS_URL
  STS2_LOBBY_DEFAULT_REGISTRY_BASE_URL
  STS2_LOBBY_COMPATIBILITY_PROFILE
  STS2_LOBBY_CONNECTION_STRATEGY
EOF
}

log() {
  printf '[build-sts2-lan-connect] %s\n' "$*"
}

die() {
  printf '[build-sts2-lan-connect] ERROR: %s\n' "$*" >&2
  exit 1
}

read_default_value() {
  local key="$1"

  if [[ ! -f "$LOCAL_DEFAULTS_FILE" ]]; then
    return
  fi

  sed -nE "s/^[[:space:]]*\"$key\":[[:space:]]*\"([^\"]*)\".*/\1/p" "$LOCAL_DEFAULTS_FILE" | head -n 1
}

cleanup() {
  if [[ -d "$BUILD_LOCK_DIR" ]]; then
    rmdir "$BUILD_LOCK_DIR" >/dev/null 2>&1 || true
  fi
}

write_lobby_defaults() {
  local target_dir="$1"
  local base_url="${STS2_LOBBY_DEFAULT_BASE_URL:-}"
  local ws_url="${STS2_LOBBY_DEFAULT_WS_URL:-}"
  local registry_base_url="${STS2_LOBBY_DEFAULT_REGISTRY_BASE_URL:-}"
  local compatibility_profile="${STS2_LOBBY_COMPATIBILITY_PROFILE:-}"
  local connection_strategy="${STS2_LOBBY_CONNECTION_STRATEGY:-}"
  local default_registry_base_url
  local default_compatibility_profile
  local default_connection_strategy

  if [[ -n "$base_url" ]]; then
    default_registry_base_url="$(read_default_value "registryBaseUrl")"
    default_compatibility_profile="$(read_default_value "compatibilityProfile")"
    default_connection_strategy="$(read_default_value "connectionStrategy")"
    registry_base_url="${registry_base_url:-$default_registry_base_url}"
    compatibility_profile="${compatibility_profile:-${default_compatibility_profile:-test_relaxed}}"
    connection_strategy="${connection_strategy:-${default_connection_strategy:-relay-only}}"

    if [[ -z "$ws_url" ]]; then
      case "$base_url" in
        https://*) ws_url="wss://${base_url#https://}" ;;
        http://*) ws_url="ws://${base_url#http://}" ;;
        *)
          echo "STS2_LOBBY_DEFAULT_BASE_URL must start with http:// or https://" >&2
          exit 1
          ;;
      esac
      ws_url="${ws_url%/}/control"
    fi

    cat > "$target_dir/$DEFAULTS_FILE_NAME" <<EOF
{
  "baseUrl": "$base_url",
  "registryBaseUrl": "${registry_base_url:-}",
  "wsUrl": "$ws_url",
  "compatibilityProfile": "$compatibility_profile",
  "connectionStrategy": "$connection_strategy"
}
EOF
    return
  fi

  if [[ -f "$LOCAL_DEFAULTS_FILE" ]]; then
    cp "$LOCAL_DEFAULTS_FILE" "$target_dir/$DEFAULTS_FILE_NAME"
  fi
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --install)
      INSTALL_AFTER_BUILD=1
      shift
      ;;
    --mods-dir)
      [[ $# -ge 2 ]] || die "--mods-dir requires a value"
      MOD_OUTPUT_DIR="$2"
      shift 2
      ;;
    --no-save-sync)
      SYNC_SAVES=0
      shift
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

mkdir -p "$PROJECT_DIR/release"

if ! mkdir "$BUILD_LOCK_DIR" 2>/dev/null; then
  die "Another sts2_lan_connect build/package step is already running. Re-run this script after the other process exits."
fi
trap cleanup EXIT

mkdir -p "$(dirname "$GODOT_LOG_FILE")"

if [[ -z "$DOTNET_BIN" || ! -x "$DOTNET_BIN" ]]; then
  die "dotnet not found at $DOTNET_BIN"
fi

if [[ -z "$GODOT_BIN" || ! -x "$GODOT_BIN" ]]; then
  die "Godot not found at $GODOT_BIN"
fi

"$DOTNET_BIN" build "$PROJECT_DIR/$ASSEMBLY_NAME.csproj" "/p:Sts2ModsDir=$MOD_OUTPUT_DIR"
"$GODOT_BIN" --headless --log-file "$GODOT_LOG_FILE" --path "$PROJECT_DIR" --script "$PROJECT_DIR/tools/build_pck.gd"

mkdir -p "$MOD_OUTPUT_DIR"
rm -f "$MOD_OUTPUT_DIR/"*.dll "$MOD_OUTPUT_DIR/"*.pck
rm -f "$MOD_OUTPUT_DIR/$MANIFEST_FILE_NAME"
rm -f "$MOD_OUTPUT_DIR/$LEGACY_MANIFEST_FILE_NAME"
cp "$DLL_SOURCE" "$MOD_OUTPUT_DIR/"
cp "$PCK_SOURCE" "$MOD_OUTPUT_DIR/"
if [[ -f "$LOCAL_MANIFEST_FILE" ]]; then
  cp "$LOCAL_MANIFEST_FILE" "$MOD_OUTPUT_DIR/$MANIFEST_FILE_NAME"
fi
rm -f "$MOD_OUTPUT_DIR/$DEFAULTS_FILE_NAME"
write_lobby_defaults "$MOD_OUTPUT_DIR"

log "Build artifacts staged at: $MOD_OUTPUT_DIR"
log "Godot headless log: $GODOT_LOG_FILE"

if [[ "$INSTALL_AFTER_BUILD" -eq 1 ]]; then
  install_args=(--install --package-dir "$MOD_OUTPUT_DIR")
  if [[ "$SYNC_SAVES" -eq 0 ]]; then
    install_args+=(--no-save-sync)
  fi

  log "Running installer with staged artifacts."
  "$INSTALL_SCRIPT" "${install_args[@]}"
fi
