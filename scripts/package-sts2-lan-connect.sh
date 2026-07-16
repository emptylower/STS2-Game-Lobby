#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=release-package-common.sh
source "$ROOT_DIR/scripts/release-package-common.sh"

ASSEMBLY_NAME="sts2_lan_connect"
PACKAGE_NAME="$ASSEMBLY_NAME"
ZIP_NAME="$ASSEMBLY_NAME-release.zip"
BUILD_SCRIPT="$ROOT_DIR/scripts/build-sts2-lan-connect.sh"
PROJECT_DIR="$ROOT_DIR/sts2-lan-connect"
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
OUTPUT_EXPLICIT=0
OUTPUT_DIR_RAW=""
WORK_DIR=""

usage() {
  cat <<'EOF'
Usage:
  ./scripts/package-sts2-lan-connect.sh [options]

Options:
  --output-dir <path>  Write the staged package and zip below this directory.
  --skip-build         Reuse canonical DLL/PCK build artifacts without reading historical release output.
  --help               Show this help.
EOF
}

cleanup() {
  if [[ -n "$WORK_DIR" && -d "$WORK_DIR" ]]; then
    rm -rf "$WORK_DIR"
  fi
}

read_default_value() {
  local key="$1"
  [[ -f "$LOCAL_DEFAULTS_FILE" ]] || return
  sed -nE "s/^[[:space:]]*\"$key\":[[:space:]]*\"([^\"]*)\".*/\1/p" "$LOCAL_DEFAULTS_FILE" | head -n 1
}

write_lobby_defaults() {
  local target="$1"
  local base_url="${STS2_LOBBY_DEFAULT_BASE_URL:-}"
  local ws_url="${STS2_LOBBY_DEFAULT_WS_URL:-}"
  local registry_base_url="${STS2_LOBBY_DEFAULT_REGISTRY_BASE_URL:-}"
  local compatibility_profile="${STS2_LOBBY_COMPATIBILITY_PROFILE:-}"
  local connection_strategy="${STS2_LOBBY_CONNECTION_STRATEGY:-}"
  local create_room_token="${STS2_LOBBY_DEFAULT_CREATE_ROOM_TOKEN:-}"
  local seeds_file="${STS2_LOBBY_SEEDS_FILE:-$ROOT_DIR/data/seeds.json}"
  local seed_peers_array="[]"
  local cf_discovery_base_url="${STS2_LOBBY_DEFAULT_CF_DISCOVERY_BASE_URL:-}"
  local addrs

  if [[ -z "$base_url" ]]; then
    release_copy_file "$LOCAL_DEFAULTS_FILE" "$target" 0644
    return
  fi

  registry_base_url="${registry_base_url:-$(read_default_value "registryBaseUrl")}"
  compatibility_profile="${compatibility_profile:-$(read_default_value "compatibilityProfile")}"
  connection_strategy="${connection_strategy:-$(read_default_value "connectionStrategy")}"
  create_room_token="${create_room_token:-$(read_default_value "createRoomToken")}"
  compatibility_profile="${compatibility_profile:-test_relaxed}"
  connection_strategy="${connection_strategy:-relay-only}"
  if [[ -z "$ws_url" ]]; then
    case "$base_url" in
      https://*) ws_url="wss://${base_url#https://}" ;;
      http://*) ws_url="ws://${base_url#http://}" ;;
      *) release_die "STS2_LOBBY_DEFAULT_BASE_URL must start with http:// or https://" ;;
    esac
    ws_url="${ws_url%/}/control"
  fi
  if [[ -f "$seeds_file" ]]; then
    addrs="$(sed -nE 's/.*"address"[[:space:]]*:[[:space:]]*"([^"]*)".*/"\1"/p' "$seeds_file" | paste -sd, -)"
    [[ -z "$addrs" ]] || seed_peers_array="[$addrs]"
  fi
  cf_discovery_base_url="${cf_discovery_base_url%/}"
  cat > "$target" <<EOF
{
  "baseUrl": "$base_url",
  "registryBaseUrl": "$registry_base_url",
  "createRoomToken": "$create_room_token",
  "wsUrl": "$ws_url",
  "compatibilityProfile": "$compatibility_profile",
  "connectionStrategy": "$connection_strategy",
  "cfDiscoveryBaseUrl": "$cf_discovery_base_url",
  "seedPeers": $seed_peers_array
}
EOF
  chmod 0644 "$target"
}

verify_lobby_defaults_runtime_fields() {
  local defaults_path="$1"
  grep -q '"baseUrl"' "$defaults_path" || release_die "Generated lobby-defaults.json is missing baseUrl"
  grep -q '"cfDiscoveryBaseUrl"[[:space:]]*:[[:space:]]*"https://sts2-gamelobby-register.xyz"' "$defaults_path" || \
    release_die "Generated lobby-defaults.json is missing public cfDiscoveryBaseUrl"
  if [[ -f "${STS2_LOBBY_SEEDS_FILE:-$ROOT_DIR/data/seeds.json}" ]]; then
    grep -q '"seedPeers"[[:space:]]*:[[:space:]]*\[' "$defaults_path" || \
      release_die "Generated lobby-defaults.json is missing seedPeers"
    grep -q 'http://lt.syx2023.icu:52000' "$defaults_path" || \
      release_die "Generated lobby-defaults.json does not include bundled seed peers"
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
    --skip-build)
      SKIP_BUILD=1
      shift
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
  release_prepare_output_dir "$PROJECT_DIR/release" "$ROOT_DIR" 1
fi

WORK_DIR="$(mktemp -d "$RELEASE_OUTPUT_DIR/.tmp-client-package.XXXXXX")"
trap cleanup EXIT INT TERM HUP
STAGE_PARENT="$WORK_DIR/stage"
PACKAGE_ROOT="$STAGE_PARENT/$PACKAGE_NAME"
STAGED_ZIP="$WORK_DIR/$ZIP_NAME"
EXPECTED_MANIFEST="$WORK_DIR/expected-files.txt"
mkdir -p "$PACKAGE_ROOT"
cat > "$EXPECTED_MANIFEST" <<EOF
$ASSEMBLY_NAME.dll
$ASSEMBLY_NAME.pck
$MANIFEST_FILE_NAME
$DEFAULTS_FILE_NAME
README.md
STS2_LAN_CONNECT_USER_GUIDE_ZH.md
install-sts2-lan-connect-macos.sh
install-sts2-lan-connect-macos.command
install-sts2-lan-connect-windows.ps1
install-sts2-lan-connect-windows.bat
LICENSE
THIRD_PARTY_NOTICES
EOF

if [[ "$SKIP_BUILD" -eq 0 ]]; then
  PACKAGE_BUILD_MOD_DIR="$WORK_DIR/build-artifacts/$ASSEMBLY_NAME"
  STS2_MODS_DIR="$PACKAGE_BUILD_MOD_DIR" \
  STS2_BUILD_LOCK_DIR="$WORK_DIR/build-lock" \
  GODOT_LOG_FILE="$WORK_DIR/godot-build.log" \
    "$BUILD_SCRIPT"
  DLL_SOURCE="$PACKAGE_BUILD_MOD_DIR/$ASSEMBLY_NAME.dll"
  PCK_SOURCE="$PACKAGE_BUILD_MOD_DIR/$ASSEMBLY_NAME.pck"
else
  DLL_SOURCE="$DLL_FILE"
  PCK_SOURCE="$PCK_FILE"
fi

release_copy_file "$DLL_SOURCE" "$PACKAGE_ROOT/$ASSEMBLY_NAME.dll" 0644
release_copy_file "$PCK_SOURCE" "$PACKAGE_ROOT/$ASSEMBLY_NAME.pck" 0644
release_copy_file "$LOCAL_MANIFEST_FILE" "$PACKAGE_ROOT/$MANIFEST_FILE_NAME" 0644
write_lobby_defaults "$PACKAGE_ROOT/$DEFAULTS_FILE_NAME"
verify_lobby_defaults_runtime_fields "$PACKAGE_ROOT/$DEFAULTS_FILE_NAME"
release_copy_file "$RELEASE_README" "$PACKAGE_ROOT/README.md" 0644
release_copy_file "$GUIDE_FILE" "$PACKAGE_ROOT/STS2_LAN_CONNECT_USER_GUIDE_ZH.md" 0644
release_copy_file "$MAC_INSTALLER" "$PACKAGE_ROOT/install-sts2-lan-connect-macos.sh" 0755
release_copy_file "$MAC_INSTALLER_COMMAND" "$PACKAGE_ROOT/install-sts2-lan-connect-macos.command" 0755
release_copy_file "$WIN_INSTALLER" "$PACKAGE_ROOT/install-sts2-lan-connect-windows.ps1" 0644
release_copy_file "$WIN_INSTALLER_BAT" "$PACKAGE_ROOT/install-sts2-lan-connect-windows.bat" 0644
release_copy_file "$ROOT_DIR/LICENSE" "$PACKAGE_ROOT/LICENSE" 0644
release_copy_file "$ROOT_DIR/THIRD_PARTY_NOTICES" "$PACKAGE_ROOT/THIRD_PARTY_NOTICES" 0644

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
