#!/usr/bin/env bash
set -euo pipefail

ASSEMBLY_NAME="sts2_lan_connect"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEFAULT_GAME_DIR="${STS2_ROOT:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2}"
DEFAULT_APP_PATH="${STS2_APP_PATH:-$DEFAULT_GAME_DIR/SlayTheSpire2.app}"
DEFAULT_USERDATA_DIR="${STS2_USERDATA_DIR:-$HOME/Library/Application Support/SlayTheSpire2}"
PACKAGE_DIR="${PACKAGE_DIR:-$SCRIPT_DIR}"
CODESIGN_BIN="${CODESIGN_BIN:-$(command -v codesign || true)}"
MANIFEST_FILE_NAME="$ASSEMBLY_NAME.json"
APP_PATH="$DEFAULT_APP_PATH"
USERDATA_DIR="$DEFAULT_USERDATA_DIR"
SYNC_SAVES=1
REFRESH_SIGNATURE=1
DRY_RUN=0
ACTION="toggle"
INSTALL_PAYLOAD_FILES=()

usage() {
  cat <<'EOF'
Usage:
  ./install-sts2-lan-connect-macos.sh [options]

Options:
  --install             Force install.
  --uninstall           Force uninstall.
  --app-path <path>     SlayTheSpire2.app full path.
  --game-dir <path>     Game root directory that contains SlayTheSpire2.app.
  --data-dir <path>     SlayTheSpire2 user data directory.
  --package-dir <path>  Directory that contains sts2_lan_connect.dll/.pck.
  --dry-run             With --install, validate and print the payload plan without writes.
  --no-save-sync        Install only; skip save migration/sync.
  --skip-codesign       Skip refreshing the macOS app bundle signature after install/uninstall.
  --help                Show this help.

Default behavior:
  - If sts2_lan_connect is already installed, the script uninstalls it.
  - If sts2_lan_connect is not installed, the script installs it.

Install behavior:
  1. Copies the mod files into the game's mods/sts2_lan_connect directory.
  2. Copies sts2_lan_connect.json and lobby-defaults.json when present in the package.
  3. Refreshes the macOS app bundle signature unless --skip-codesign is used.
  4. Performs a one-way sync from non-modded saves into modded saves unless --no-save-sync is used.

Uninstall behavior:
  1. Removes the game's mods/sts2_lan_connect directory.
  2. Refreshes the macOS app bundle signature unless --skip-codesign is used.

Notes:
  - Close the game before running this script.
  - Do not hand-copy files into SlayTheSpire2.app on macOS; use this installer so the app bundle signature stays launchable.
  - Re-run the script any time you want to re-sync vanilla saves into modded saves.
EOF
}

log() {
  printf '[sts2-lan-connect] %s\n' "$*"
}

die() {
  printf '[sts2-lan-connect] ERROR: %s\n' "$*" >&2
  exit 1
}

is_mod_installed() {
  local mod_dir="$1"
  [[ -d "$mod_dir" ]] || return 1
  find "$mod_dir" -mindepth 1 -maxdepth 1 -print -quit | grep -q .
}

ensure_game_not_running() {
  if pgrep -f '/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2' >/dev/null 2>&1; then
    die "Slay the Spire 2 is still running. Close the game before installing or uninstalling the mod."
  fi
}

validate_package_dir() {
  local required_file
  for required_file in \
    "$ASSEMBLY_NAME.dll" \
    "$ASSEMBLY_NAME.pck" \
    lobby-defaults.json \
    LICENSE \
    THIRD_PARTY_NOTICES; do
    [[ -f "$PACKAGE_DIR/$required_file" && ! -L "$PACKAGE_DIR/$required_file" ]] || \
      die "Missing required package input: $PACKAGE_DIR/$required_file"
  done
}

prepare_install_payload_plan() {
  local optional_file
  validate_package_dir
  INSTALL_PAYLOAD_FILES=(
    "$ASSEMBLY_NAME.dll"
    "$ASSEMBLY_NAME.pck"
    lobby-defaults.json
    LICENSE
    THIRD_PARTY_NOTICES
  )
  for optional_file in "$MANIFEST_FILE_NAME" STS2_LAN_CONNECT_USER_GUIDE_ZH.md; do
    if [[ -f "$PACKAGE_DIR/$optional_file" && ! -L "$PACKAGE_DIR/$optional_file" ]]; then
      INSTALL_PAYLOAD_FILES+=("$optional_file")
    fi
  done
}

print_install_payload_plan() {
  local mod_dir="$1"
  local payload_file
  for payload_file in "${INSTALL_PAYLOAD_FILES[@]}"; do
    printf 'DRY-RUN copy: %s -> %s\n' \
      "$PACKAGE_DIR/$payload_file" \
      "$mod_dir/$payload_file"
  done
}

verify_installed_payload() {
  local mod_dir="$1"

  cmp -s "$PACKAGE_DIR/$ASSEMBLY_NAME.dll" "$mod_dir/$ASSEMBLY_NAME.dll" || die "Installed DLL does not match the selected package directory: $PACKAGE_DIR"
  cmp -s "$PACKAGE_DIR/$ASSEMBLY_NAME.pck" "$mod_dir/$ASSEMBLY_NAME.pck" || die "Installed PCK does not match the selected package directory: $PACKAGE_DIR"
}

migrate_legacy_config_if_needed() {
  local mod_dir="$1"
  local legacy_config="$mod_dir/config.json"
  local target_config="$USERDATA_DIR/$ASSEMBLY_NAME/config.json"

  if [[ ! -f "$legacy_config" ]]; then
    return
  fi

  mkdir -p "$(dirname "$target_config")"
  if [[ ! -f "$target_config" || "$legacy_config" -nt "$target_config" ]]; then
    cp -f "$legacy_config" "$target_config"
    log "Migrated legacy config.json into user data: $target_config"
  fi
}

refresh_app_signature() {
  if [[ "$REFRESH_SIGNATURE" -eq 0 ]]; then
    log "Skipping app bundle signature refresh (--skip-codesign)."
    return
  fi

  if [[ -z "$CODESIGN_BIN" || ! -x "$CODESIGN_BIN" ]]; then
    die "codesign not found. Re-run with --skip-codesign only if you accept the risk that the game may stop launching."
  fi

  log "Refreshing macOS app bundle signature: $APP_PATH"
  "$CODESIGN_BIN" --force --deep --sign - "$APP_PATH" >/dev/null
  "$CODESIGN_BIN" --verify --deep --strict --verbose=2 "$APP_PATH" >/dev/null
  log "App bundle signature refreshed."
}

uninstall_mod() {
  local mod_dir="$1"

  if [[ ! -e "$mod_dir" ]]; then
    log "Mod is not installed. Nothing to uninstall."
    exit 0
  fi

  migrate_legacy_config_if_needed "$mod_dir"
  log "Uninstalling mod from: $mod_dir"
  rm -rf "$mod_dir"
  refresh_app_signature
  log "Mod uninstalled."
}

backup_profile_if_needed() {
  local source_profile="$1"
  local backup_profile="$2"

  if [[ ! -d "$source_profile" ]]; then
    return
  fi

  if ! find "$source_profile" -type f -print -quit | grep -q .; then
    return
  fi

  mkdir -p "$(dirname "$backup_profile")"
  cp -R "$source_profile" "$backup_profile"
  backups_created=$((backups_created + 1))
}

sync_profile_saves() {
  local platform_name="$1"
  local user_dir="$2"
  local profile_dir="$3"
  local profile_name
  local source_saves
  local dest_profile
  local dest_saves

  profile_name="$(basename "$profile_dir")"
  source_saves="$profile_dir/saves"
  [[ -d "$source_saves" ]] || return

  dest_profile="$user_dir/modded/$profile_name"
  dest_saves="$dest_profile/saves"

  backup_profile_if_needed "$dest_profile" "$backup_root/$platform_name/$(basename "$user_dir")/$profile_name"
  mkdir -p "$dest_saves"

  while IFS= read -r -d '' source_file; do
    local relative_path
    local dest_file
    relative_path="${source_file#$source_saves/}"
    dest_file="$dest_saves/$relative_path"
    mkdir -p "$(dirname "$dest_file")"

    if [[ ! -e "$dest_file" || "$source_file" -nt "$dest_file" ]]; then
      cp -f "$source_file" "$dest_file"
      files_copied=$((files_copied + 1))
    fi
  done < <(find "$source_saves" -type f -print0)

  profiles_synced=$((profiles_synced + 1))
}

install_mod() {
  local mod_dir="$1"
  local temp_mod_dir
  local payload_file

  prepare_install_payload_plan

  temp_mod_dir="${mod_dir}.tmp.$$"

  migrate_legacy_config_if_needed "$mod_dir"
  rm -rf "$temp_mod_dir"
  mkdir -p "$temp_mod_dir"

  log "Installing mod files to: $mod_dir"
  for payload_file in "${INSTALL_PAYLOAD_FILES[@]}"; do
    cp -f "$PACKAGE_DIR/$payload_file" "$temp_mod_dir/$payload_file"
  done

  mkdir -p "$(dirname "$mod_dir")"
  rm -rf "$mod_dir"
  mv "$temp_mod_dir" "$mod_dir"
  verify_installed_payload "$mod_dir"
  refresh_app_signature

  if [[ "$SYNC_SAVES" -eq 0 ]]; then
    log "Save sync skipped (--no-save-sync)."
    exit 0
  fi

  if [[ ! -d "$USERDATA_DIR" ]]; then
    log "User data directory '$USERDATA_DIR' does not exist yet. Installation finished without save sync."
    exit 0
  fi

  timestamp="$(date +%Y%m%d-%H%M%S)"
  backup_root="$USERDATA_DIR/sts2_lan_connect_backups/$timestamp"
  profiles_synced=0
  files_copied=0
  backups_created=0

  for platform_name in steam default; do
    platform_dir="$USERDATA_DIR/$platform_name"
    [[ -d "$platform_dir" ]] || continue

    while IFS= read -r -d '' user_dir; do
      while IFS= read -r -d '' profile_dir; do
        sync_profile_saves "$platform_name" "$user_dir" "$profile_dir"
      done < <(find "$user_dir" -mindepth 1 -maxdepth 1 -type d -name 'profile*' -print0)
    done < <(find "$platform_dir" -mindepth 1 -maxdepth 1 -type d -print0)
  done

  log "Save sync finished. Profiles scanned: $profiles_synced, files copied: $files_copied, backups created: $backups_created"
  log "This is a one-way sync from vanilla saves into modded saves. Re-run the installer any time you want to sync again."
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --install)
      ACTION="install"
      shift
      ;;
    --uninstall)
      ACTION="uninstall"
      shift
      ;;
    --app-path)
      [[ $# -ge 2 ]] || die "--app-path requires a value"
      APP_PATH="$2"
      shift 2
      ;;
    --game-dir)
      [[ $# -ge 2 ]] || die "--game-dir requires a value"
      APP_PATH="$2/SlayTheSpire2.app"
      shift 2
      ;;
    --data-dir)
      [[ $# -ge 2 ]] || die "--data-dir requires a value"
      USERDATA_DIR="$2"
      shift 2
      ;;
    --package-dir)
      [[ $# -ge 2 ]] || die "--package-dir requires a value"
      PACKAGE_DIR="$2"
      shift 2
      ;;
    --dry-run)
      DRY_RUN=1
      shift
      ;;
    --no-save-sync)
      SYNC_SAVES=0
      shift
      ;;
    --skip-codesign)
      REFRESH_SIGNATURE=0
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

if [[ "$DRY_RUN" -eq 1 && "$ACTION" != "install" ]]; then
  die "--dry-run requires --install"
fi

TARGET_MOD_DIR="$APP_PATH/Contents/MacOS/mods/$ASSEMBLY_NAME"

if [[ "$DRY_RUN" -eq 1 ]]; then
  prepare_install_payload_plan
  print_install_payload_plan "$TARGET_MOD_DIR"
  exit 0
fi

if [[ ! -d "$APP_PATH" ]]; then
  die "Could not find SlayTheSpire2.app at '$APP_PATH'"
fi

ensure_game_not_running

EFFECTIVE_ACTION="$ACTION"
if [[ "$ACTION" == "toggle" ]]; then
  if is_mod_installed "$TARGET_MOD_DIR"; then
    EFFECTIVE_ACTION="uninstall"
  else
    EFFECTIVE_ACTION="install"
  fi
fi

if [[ "$EFFECTIVE_ACTION" == "uninstall" ]]; then
  uninstall_mod "$TARGET_MOD_DIR"
else
  install_mod "$TARGET_MOD_DIR"
fi
