#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ASSEMBLY_NAME="sts2_lan_connect"
BUILD_SCRIPT="$ROOT_DIR/scripts/build-sts2-lan-connect.sh"
PROJECT_DIR="$ROOT_DIR/sts2-lan-connect"
PACKAGE_ROOT="$PROJECT_DIR/release/$ASSEMBLY_NAME"
PACKAGE_BUILD_MOD_DIR="$PROJECT_DIR/release/.build_mod_output/$ASSEMBLY_NAME"
PCK_FILE="$PROJECT_DIR/build/$ASSEMBLY_NAME.pck"
DLL_FILE="$PROJECT_DIR/.godot/mono/temp/bin/Debug/$ASSEMBLY_NAME.dll"
GUIDE_FILE="$ROOT_DIR/docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md"
RELEASE_README="$ROOT_DIR/docs/CLIENT_RELEASE_README_ZH.md"
MAC_INSTALLER="$ROOT_DIR/scripts/install-sts2-lan-connect-macos.sh"
MAC_INSTALLER_COMMAND="$ROOT_DIR/scripts/install-sts2-lan-connect-macos.command"
WIN_INSTALLER="$ROOT_DIR/scripts/install-sts2-lan-connect-windows.ps1"
WIN_INSTALLER_BAT="$ROOT_DIR/scripts/install-sts2-lan-connect-windows.bat"
DEFAULTS_FILE_NAME="lobby-defaults.json"
MANIFEST_FILE_NAME="$ASSEMBLY_NAME.json"
LOCAL_DEFAULTS_FILE="$PROJECT_DIR/$DEFAULTS_FILE_NAME"
LOCAL_MANIFEST_FILE="$PROJECT_DIR/$MANIFEST_FILE_NAME"
SKIP_BUILD=0

usage() {
  cat <<'EOF'
Usage:
  ./scripts/package-sts2-lan-connect.sh [options]

Options:
  --skip-build   Reuse the existing DLL/PCK artifacts and refresh only the release directory and zip.
  --help         Show this help.

Environment:
  STS2_LOBBY_DEFAULT_BASE_URL
  STS2_LOBBY_DEFAULT_WS_URL
  STS2_LOBBY_DEFAULT_REGISTRY_BASE_URL
  STS2_LOBBY_DEFAULT_CREATE_ROOM_TOKEN
  STS2_LOBBY_COMPATIBILITY_PROFILE
  STS2_LOBBY_CONNECTION_STRATEGY
  STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL
  STS2_LOBBY_SEEDS_FILE   (defaults to <repo>/data/seeds.json)
EOF
}

die() {
  echo "$*" >&2
  exit 1
}

require_file() {
  local path="$1"
  [[ -f "$path" ]] || die "Expected file is missing from release package: $path"
}

verify_package_manifest() {
  local package_dir="$1"

  require_file "$package_dir/$ASSEMBLY_NAME.dll"
  require_file "$package_dir/$ASSEMBLY_NAME.pck"
  require_file "$package_dir/$MANIFEST_FILE_NAME"
  require_file "$package_dir/README.md"
  require_file "$package_dir/STS2_LAN_CONNECT_USER_GUIDE_ZH.md"
  require_file "$package_dir/install-sts2-lan-connect-macos.sh"
  require_file "$package_dir/install-sts2-lan-connect-macos.command"
  require_file "$package_dir/install-sts2-lan-connect-windows.ps1"
  require_file "$package_dir/install-sts2-lan-connect-windows.bat"
}

verify_zip_manifest() {
  local zip_path="$1"
  local zip_listing
  zip_listing="$(zipinfo -1 "$zip_path")"

  [[ "$zip_listing" == *"$ASSEMBLY_NAME/install-sts2-lan-connect-windows.bat"* ]] || die "Release zip is missing install-sts2-lan-connect-windows.bat"
  [[ "$zip_listing" == *"$ASSEMBLY_NAME/install-sts2-lan-connect-windows.ps1"* ]] || die "Release zip is missing install-sts2-lan-connect-windows.ps1"
  [[ "$zip_listing" == *"$ASSEMBLY_NAME/install-sts2-lan-connect-macos.sh"* ]] || die "Release zip is missing install-sts2-lan-connect-macos.sh"
  [[ "$zip_listing" == *"$ASSEMBLY_NAME/install-sts2-lan-connect-macos.command"* ]] || die "Release zip is missing install-sts2-lan-connect-macos.command"
  [[ "$zip_listing" == *"$ASSEMBLY_NAME/$MANIFEST_FILE_NAME"* ]] || die "Release zip is missing $MANIFEST_FILE_NAME"
}

clean_release_noise() {
  mkdir -p "$PROJECT_DIR/release"
  find "$PROJECT_DIR/release" -maxdepth 1 \( \
    -name '.DS_Store' \
    -o -name "$ASSEMBLY_NAME 2" \
    -o -name "$ASSEMBLY_NAME 3" \
    -o -name '联机大厅*.zip' \
    -o -name '游戏大厅mod*.zip' \
  \) -exec rm -rf {} +
}

resolve_artifact() {
  local description="$1"
  shift

  local candidate
  for candidate in "$@"; do
    if [[ -f "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return
    fi
  done

  die "Could not find $description. Re-run without --skip-build, or build the mod first."
}

read_default_value() {
  local key="$1"

  if [[ ! -f "$LOCAL_DEFAULTS_FILE" ]]; then
    return
  fi

  sed -nE "s/^[[:space:]]*\"$key\":[[:space:]]*\"([^\"]*)\".*/\1/p" "$LOCAL_DEFAULTS_FILE" | head -n 1
}

write_lobby_defaults() {
  local target_dir="$1"
  local base_url="${STS2_LOBBY_DEFAULT_BASE_URL:-}"
  local ws_url="${STS2_LOBBY_DEFAULT_WS_URL:-}"
  local registry_base_url="${STS2_LOBBY_DEFAULT_REGISTRY_BASE_URL:-}"
  local compatibility_profile="${STS2_LOBBY_COMPATIBILITY_PROFILE:-}"
  local connection_strategy="${STS2_LOBBY_CONNECTION_STRATEGY:-}"
  local create_room_token="${STS2_LOBBY_DEFAULT_CREATE_ROOM_TOKEN:-}"
  local default_registry_base_url
  local default_compatibility_profile
  local default_connection_strategy

  if [[ -n "$base_url" ]]; then
    default_registry_base_url="$(read_default_value "registryBaseUrl")"
    default_compatibility_profile="$(read_default_value "compatibilityProfile")"
    default_connection_strategy="$(read_default_value "connectionStrategy")"
    create_room_token="${create_room_token:-$(read_default_value "createRoomToken")}" 
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

    local seeds_file="${STS2_LOBBY_SEEDS_FILE:-$ROOT_DIR/data/seeds.json}"
    local seed_peers_array="[]"
    if [[ -f "$seeds_file" ]]; then
      local addrs
      addrs="$(sed -nE 's/.*"address"[[:space:]]*:[[:space:]]*"([^"]*)".*/"\1"/p' "$seeds_file" | paste -sd, -)"
      if [[ -n "$addrs" ]]; then
        seed_peers_array="[$addrs]"
      fi
    fi
    local cf_discovery_base_url="${STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL:-}"
    cf_discovery_base_url="${cf_discovery_base_url%/}"

    cat > "$target_dir/$DEFAULTS_FILE_NAME" <<EOF
{
  "baseUrl": "$base_url",
  "registryBaseUrl": "${registry_base_url:-}",
  "createRoomToken": "${create_room_token:-}",
  "wsUrl": "$ws_url",
  "compatibilityProfile": "$compatibility_profile",
  "connectionStrategy": "$connection_strategy",
  "cfDiscoveryBaseUrl": "$cf_discovery_base_url",
  "seedPeers": $seed_peers_array
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
    --skip-build)
      SKIP_BUILD=1
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

if [[ "$SKIP_BUILD" -eq 0 ]]; then
  STS2_MODS_DIR="$PACKAGE_BUILD_MOD_DIR" "$BUILD_SCRIPT"
fi

clean_release_noise

DLL_SOURCE="$(resolve_artifact \
  "$ASSEMBLY_NAME.dll" \
  "$DLL_FILE" \
  "$PACKAGE_BUILD_MOD_DIR/$ASSEMBLY_NAME.dll" \
  "$PACKAGE_ROOT/$ASSEMBLY_NAME.dll")"
PCK_SOURCE="$(resolve_artifact \
  "$ASSEMBLY_NAME.pck" \
  "$PCK_FILE" \
  "$PACKAGE_BUILD_MOD_DIR/$ASSEMBLY_NAME.pck" \
  "$PACKAGE_ROOT/$ASSEMBLY_NAME.pck")"

rm -rf "$PACKAGE_ROOT"
mkdir -p "$PACKAGE_ROOT"
cp "$DLL_SOURCE" "$PACKAGE_ROOT/$ASSEMBLY_NAME.dll"
cp "$PCK_SOURCE" "$PACKAGE_ROOT/$ASSEMBLY_NAME.pck"
cp "$LOCAL_MANIFEST_FILE" "$PACKAGE_ROOT/$MANIFEST_FILE_NAME"
cp "$RELEASE_README" "$PACKAGE_ROOT/README.md"
cp "$GUIDE_FILE" "$PACKAGE_ROOT/"
cp "$MAC_INSTALLER" "$PACKAGE_ROOT/"
cp "$MAC_INSTALLER_COMMAND" "$PACKAGE_ROOT/"
cp "$WIN_INSTALLER" "$PACKAGE_ROOT/"
cp "$WIN_INSTALLER_BAT" "$PACKAGE_ROOT/"
rm -f "$PACKAGE_ROOT/$DEFAULTS_FILE_NAME"
write_lobby_defaults "$PACKAGE_ROOT"
chmod +x "$PACKAGE_ROOT/install-sts2-lan-connect-macos.sh"
chmod +x "$PACKAGE_ROOT/install-sts2-lan-connect-macos.command"
verify_package_manifest "$PACKAGE_ROOT"

cd "$PROJECT_DIR/release"
rm -f "${ASSEMBLY_NAME}-release.zip"
zip -qr "${ASSEMBLY_NAME}-release.zip" "$ASSEMBLY_NAME"
verify_zip_manifest "${ASSEMBLY_NAME}-release.zip"
echo "Package created at: $PROJECT_DIR/release/${ASSEMBLY_NAME}-release.zip"
