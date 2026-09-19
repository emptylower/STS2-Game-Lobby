#!/usr/bin/env bash
# Publish the packaged client (releases/sts2_lan_connect) to the Steam Workshop item 游戏大厅.
#
# steamcmd runs with an isolated HOME so its cached login token lives in its own config.vdf.
# On this project's Mac steamcmd otherwise shares ~/Library/Application Support/Steam with the
# Steam desktop client, which rewrites config.vdf from memory and wipes steamcmd's ConnectCache
# within minutes, forcing a password + Steam Guard login before every push.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_ID="2868840"
ITEM_ID="3749766330"
STEAM_USER="${STEAM_WORKSHOP_USER:-xixingshu_zylo}"
STEAMCMD_HOME="${STEAMCMD_HOME:-$HOME/.steamcmd-home}"
STEAMCMD_BIN="${STEAMCMD_BIN:-$(command -v steamcmd || true)}"
CONTENT_DIR="$ROOT_DIR/releases/sts2_lan_connect"
DESCRIPTION_FILE="$ROOT_DIR/docs/STEAM_WORKSHOP_DESCRIPTION_ZH.txt"
# Valve's KeyValues reader rejects tokens much past 1 KB; stay well inside what has worked.
MAX_FIELD_BYTES=1300

MODE=""
CHANGENOTE_FILE=""
DRY_RUN=0

usage() {
  cat <<'EOF'
Usage:
  ./scripts/publish-steam-workshop.sh --login
  ./scripts/publish-steam-workshop.sh --changenote docs/STEAM_WORKSHOP_UPDATE_V<x>_ZH.txt [--dry-run]
  ./scripts/publish-steam-workshop.sh --verify

Modes:
  --login               Interactive one-time login into the isolated steamcmd home
                        (password + Steam Guard approval). Run this yourself in a terminal.
  --changenote <file>   Push releases/sts2_lan_connect with <file> as the change note, then push
                        docs/STEAM_WORKSHOP_DESCRIPTION_ZH.txt as the description (two separate
                        builds: one combined VDF overflows the KeyValues token limit), then verify.
  --verify              Only print what is live (manifest id, size, update time, description match).
  --dry-run             With --changenote: write and print the VDFs, do not contact Steam.

Environment: STEAMCMD_HOME (default ~/.steamcmd-home), STEAM_WORKSHOP_USER, STEAMCMD_BIN.
EOF
}

die() { printf '[publish-steam-workshop] ERROR: %s\n' "$*" >&2; exit 1; }
log() { printf '[publish-steam-workshop] %s\n' "$*"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --login) MODE="login"; shift ;;
    --verify) MODE="verify"; shift ;;
    --changenote)
      [[ $# -ge 2 ]] || die "--changenote requires a file"
      MODE="publish"; CHANGENOTE_FILE="$2"; shift 2 ;;
    --dry-run) DRY_RUN=1; shift ;;
    --help|-h) usage; exit 0 ;;
    *) die "Unknown option: $1" ;;
  esac
done
[[ -n "$MODE" ]] || { usage; exit 1; }

run_steamcmd() {
  [[ -n "$STEAMCMD_BIN" && -x "$STEAMCMD_BIN" ]] || die "steamcmd not found (brew install --cask steamcmd)"
  mkdir -p "$STEAMCMD_HOME"
  chmod 700 "$STEAMCMD_HOME"
  HOME="$STEAMCMD_HOME" "$STEAMCMD_BIN" "$@"
}

verify_live() {
  curl -fsS -X POST "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/" \
    -d itemcount=1 -d "publishedfileids[0]=$ITEM_ID" |
    DESCRIPTION_FILE="$DESCRIPTION_FILE" python3 -c '
import json, os, sys, time
d = json.load(sys.stdin)["response"]["publishedfiledetails"][0]
print("live manifest (hcontent_file):", d["hcontent_file"])
print("live file_size:", d["file_size"])
print("live time_updated:", time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(d["time_updated"])))
repo = open(os.environ["DESCRIPTION_FILE"], encoding="utf-8").read().strip()
print("live description matches repo file:", d["description"].strip() == repo)
'
}

# Reads a BBCode text file and makes it safe to embed in a VDF string.
vdf_field() {
  local file="$1" label="$2" text bytes
  [[ -f "$file" ]] || die "$label file not found: $file"
  text="$(python3 - "$file" 2>&1 <<'PY'
import sys
s = open(sys.argv[1], encoding="utf-8").read().strip()
if '"' in s:
    sys.exit('contains ASCII double quotes; use 「」 instead (they terminate the VDF string)')
print(s.replace("\\", "\\\\"), end="")
PY
)" || die "$label $file: $text"
  bytes="$(printf '%s' "$text" | wc -c | tr -d ' ')"
  [[ "$bytes" -le "$MAX_FIELD_BYTES" ]] || die "$label is $bytes bytes; keep it under $MAX_FIELD_BYTES (KeyValues token limit)"
  printf '%s' "$text"
}

case "$MODE" in
  login)
    log "Logging $STEAM_USER into the isolated steamcmd home: $STEAMCMD_HOME"
    run_steamcmd +login "$STEAM_USER" +quit
    ;;
  verify)
    verify_live
    ;;
  publish)
    [[ -f "$CONTENT_DIR/sts2_lan_connect.dll" && -f "$CONTENT_DIR/sts2_lan_connect.json" ]] || \
      die "releases/sts2_lan_connect is not a packaged client; refresh releases/ first"
    version="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$CONTENT_DIR/sts2_lan_connect.json")"
    changenote="$(vdf_field "$CHANGENOTE_FILE" "change note")"
    description="$(vdf_field "$DESCRIPTION_FILE" "description")"
    grep -q "$version" "$DESCRIPTION_FILE" || die "description does not mention packaged version $version"

    work_dir="$(mktemp -d)"
    trap 'rm -rf "$work_dir"' EXIT
    content_vdf="$work_dir/content.vdf"
    description_vdf="$work_dir/description.vdf"
    printf '"workshopitem"\n{\n\t"appid"\t\t"%s"\n\t"publishedfileid"\t\t"%s"\n\t"contentfolder"\t\t"%s"\n\t"changenote"\t\t"%s"\n}\n' \
      "$APP_ID" "$ITEM_ID" "$CONTENT_DIR" "$changenote" > "$content_vdf"
    printf '"workshopitem"\n{\n\t"appid"\t\t"%s"\n\t"publishedfileid"\t\t"%s"\n\t"description"\t\t"%s"\n}\n' \
      "$APP_ID" "$ITEM_ID" "$description" > "$description_vdf"

    log "Packaged client version: $version"
    if [[ "$DRY_RUN" -eq 1 ]]; then
      log "Dry run; VDFs that would be pushed:"
      cat "$content_vdf" "$description_vdf"
      exit 0
    fi

    log "Pushing content + change note..."
    run_steamcmd +login "$STEAM_USER" +workshop_build_item "$content_vdf" +quit </dev/null | tee "$work_dir/content.log"
    grep -q "Committing update...Success" "$work_dir/content.log" || \
      die "content push did not succeed (if it says 'Cached credentials not found', run --login first)"
    log "Pushing description..."
    run_steamcmd +login "$STEAM_USER" +workshop_build_item "$description_vdf" +quit </dev/null | tee "$work_dir/description.log"
    grep -q "Committing update...Success" "$work_dir/description.log" || die "description push did not succeed"

    build_log="$(dirname "$(readlink -f "$STEAMCMD_BIN" 2>/dev/null || echo "$STEAMCMD_BIN")")/MacOS/workshopbuilds/depot_build_$APP_ID.log"
    [[ -f "$build_log" ]] && grep -E "Changed:|Added:|Removed:|Summary:|New manifestID" "$build_log" | tail -12 || true
    log "Live state:"
    verify_live
    ;;
esac
