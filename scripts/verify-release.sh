#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=release-package-common.sh
source "$ROOT_DIR/scripts/release-package-common.sh"

ARTIFACTS_ONLY=0
TEMP_ROOT=""

usage() {
  cat <<'EOF'
Usage:
  ./scripts/verify-release.sh [--artifacts-only]

Options:
  --artifacts-only  Skip source test suites; still build and verify fresh temporary packages.
  --help            Show this help.

All package outputs are created beneath one trapped temporary directory. Caller-selected
output paths, including releases/, are intentionally unsupported.
EOF
}

cleanup() {
  if [[ -n "$TEMP_ROOT" && -d "$TEMP_ROOT" ]]; then
    rm -rf "$TEMP_ROOT"
  fi
}

resolve_tool() {
  local environment_value="$1"
  shift
  local candidate
  if [[ -n "$environment_value" && -x "$environment_value" ]]; then
    printf '%s\n' "$environment_value"
    return
  fi
  for candidate in "$@"; do
    if [[ -n "$candidate" && -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return
    fi
  done
  return 1
}

assert_manifest() {
  local package_dir="$1"
  local expected_file="$2"
  local actual_file="$3"
  (cd "$package_dir" && find . -type f -print | sed 's#^\./##' | LC_ALL=C sort) > "$actual_file"
  LC_ALL=C sort "$expected_file" -o "$expected_file"
  cmp -s "$expected_file" "$actual_file" || {
    diff -u "$expected_file" "$actual_file" >&2 || true
    release_die "Verified package differs from the explicit allowlist: $package_dir"
  }
}

assert_zip_manifest() {
  local zip_path="$1"
  local package_name="$2"
  local expected_file="$3"
  local actual_file="$4"
  local seen_file="$actual_file.seen"
  zipinfo -1 "$zip_path" > "$seen_file"
  [[ "$(LC_ALL=C sort "$seen_file" | uniq -d | wc -l | tr -d ' ')" -eq 0 ]] || \
    release_die "Archive contains duplicate entries: $zip_path"
  while IFS= read -r entry; do
    [[ "$entry" != /* && "$entry" != *'\\'* ]] || release_die "Unsafe archive entry: $entry"
    case "/$entry/" in
      */../*) release_die "Archive traversal entry: $entry" ;;
    esac
    [[ "$entry" == "$package_name" || "$entry" == "$package_name/"* ]] || \
      release_die "Archive entry escapes package root: $entry"
    [[ "$entry" == */ ]] || printf '%s\n' "${entry#"$package_name/"}"
  done < "$seen_file" | LC_ALL=C sort > "$actual_file"
  cmp -s "$expected_file" "$actual_file" || {
    diff -u "$expected_file" "$actual_file" >&2 || true
    release_die "Verified archive differs from the explicit allowlist: $zip_path"
  }
}

assert_no_contamination() {
  local manifest="$1"
  local client_manifest="$2"
  local entry
  local lower
  while IFS= read -r entry; do
    lower="$(printf '%s' "$entry" | tr '[:upper:]' '[:lower:]')"
    case "/$lower/" in
      *'/typing.dll/'*|*'/.env/'*|*'/.git/'*|*'/test/'*|*'/tests/'*|*'.test.ts/'*|*'.test.js/'*|*'/server-admin.json/'*|*'/admin-state/'*)
        release_die "Package contamination detected: $entry"
        ;;
    esac
    case "$lower" in
      *secret*|*.png|*.jpg|*.jpeg|*.ttf|*.otf)
        release_die "Package contamination detected: $entry"
        ;;
      *.pck)
        if [[ "$client_manifest" -ne 1 || "$entry" != "sts2_lan_connect.pck" ]]; then
          release_die "Unexpected PCK in package: $entry"
        fi
        ;;
    esac
  done < "$manifest"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --artifacts-only)
      [[ "$ARTIFACTS_ONLY" -eq 0 ]] || release_die "--artifacts-only may be specified only once"
      ARTIFACTS_ONLY=1
      shift
      ;;
    --output-dir)
      release_die "--output-dir is unsupported; verification always uses a trapped temporary directory and refuses releases/"
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *releases/*|releases|*/releases)
      release_die "Caller-selected releases/ paths are forbidden"
      ;;
    *)
      release_die "Unknown option: $1"
      ;;
  esac
done

TEMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/sts2-release-verify.XXXXXX")"
trap cleanup EXIT INT TERM HUP
CLIENT_OUTPUT="$TEMP_ROOT/client output"
SERVICE_OUTPUT="$TEMP_ROOT/service output"

if [[ "$ARTIFACTS_ONLY" -eq 0 ]]; then
  (
    cd "$ROOT_DIR/lobby-service"
    npm run check
    npm run test
  )

  DOTNET_BIN="$(resolve_tool \
    "${DOTNET_BIN:-}" \
    "$(command -v dotnet || true)" \
    "$HOME/.dotnet/dotnet")" || release_die "dotnet executable not found"
  export DOTNET_BIN
  "$DOTNET_BIN" test "$ROOT_DIR/sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj" -m:1
  "$DOTNET_BIN" test "$ROOT_DIR/sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj" \
    --settings "$ROOT_DIR/sts2-lan-connect.GdUnitTests/gdunit4.runsettings" -m:1
fi

DOTNET_BIN="$(resolve_tool \
  "${DOTNET_BIN:-}" \
  "$(command -v dotnet || true)" \
  "$HOME/.dotnet/dotnet")" || release_die "dotnet executable not found"
GODOT_BIN="$(resolve_tool \
  "${GODOT_BIN:-}" \
  "$(command -v godot451-mono || true)" \
  "$(command -v godot || true)" \
  "$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot" \
  "/Applications/Godot_mono.app/Contents/MacOS/Godot" \
  "/Applications/Godot.app/Contents/MacOS/Godot")" || release_die "Godot Mono executable not found"
export DOTNET_BIN GODOT_BIN

"$ROOT_DIR/scripts/package-sts2-lan-connect.sh" --output-dir "$CLIENT_OUTPUT"
"$ROOT_DIR/scripts/package-lobby-service.sh" --output-dir "$SERVICE_OUTPUT"

CLIENT_PACKAGE="$CLIENT_OUTPUT/sts2_lan_connect"
CLIENT_ZIP="$CLIENT_OUTPUT/sts2_lan_connect-release.zip"
SERVICE_PACKAGE="$SERVICE_OUTPUT/sts2_lobby_service"
SERVICE_ZIP="$SERVICE_OUTPUT/sts2_lobby_service.zip"
CLIENT_EXPECTED="$TEMP_ROOT/client-expected.txt"
SERVICE_EXPECTED="$TEMP_ROOT/service-expected.txt"

cat > "$CLIENT_EXPECTED" <<'EOF'
LICENSE
README.md
STS2_LAN_CONNECT_USER_GUIDE_ZH.md
THIRD_PARTY_NOTICES
install-sts2-lan-connect-macos.command
install-sts2-lan-connect-macos.sh
install-sts2-lan-connect-windows.bat
install-sts2-lan-connect-windows.ps1
lobby-defaults.json
sts2_lan_connect.dll
sts2_lan_connect.json
sts2_lan_connect.pck
EOF

cat > "$SERVICE_EXPECTED" <<'EOF'
LICENSE
README.md
THIRD_PARTY_NOTICES
install-lobby-service-linux.sh
lobby-service/.dockerignore
lobby-service/.env.example
lobby-service/Dockerfile
lobby-service/deploy/.env.example
lobby-service/deploy/docker-compose.lobby-service.yml
lobby-service/deploy/lobby-service.docker.env.example
lobby-service/deploy/sts2-lobby.service.example
lobby-service/package-lock.json
lobby-service/package.json
lobby-service/scripts/generate-server-admin-password-hash.mjs
lobby-service/src/app.ts
lobby-service/src/bandwidth-guard.ts
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
lobby-service/src/client-ip.ts
lobby-service/src/config.ts
lobby-service/src/join-guard.ts
lobby-service/src/mod-sync/diff.ts
lobby-service/src/mod-sync/protocol.ts
lobby-service/src/mod-sync/validator.ts
lobby-service/src/peer/auto-announce.ts
lobby-service/src/peer/bootstrap.ts
lobby-service/src/peer/gossip.ts
lobby-service/src/peer/handlers/announce.ts
lobby-service/src/peer/handlers/health.ts
lobby-service/src/peer/handlers/heartbeat.ts
lobby-service/src/peer/handlers/list.ts
lobby-service/src/peer/handlers/metrics.ts
lobby-service/src/peer/identity.ts
lobby-service/src/peer/prober.ts
lobby-service/src/peer/seeds-loader.ts
lobby-service/src/peer/store.ts
lobby-service/src/peer/types.ts
lobby-service/src/relay.ts
lobby-service/src/rolling-bandwidth.ts
lobby-service/src/room-cleanup.ts
lobby-service/src/server-admin-auth.ts
lobby-service/src/server-admin-state.ts
lobby-service/src/server-admin-ui.ts
lobby-service/src/server.ts
lobby-service/src/store.ts
lobby-service/tsconfig.json
EOF

assert_manifest "$CLIENT_PACKAGE" "$CLIENT_EXPECTED" "$TEMP_ROOT/client-actual.txt"
assert_manifest "$SERVICE_PACKAGE" "$SERVICE_EXPECTED" "$TEMP_ROOT/service-actual.txt"
assert_zip_manifest "$CLIENT_ZIP" sts2_lan_connect "$CLIENT_EXPECTED" "$TEMP_ROOT/client-zip-actual.txt"
assert_zip_manifest "$SERVICE_ZIP" sts2_lobby_service "$SERVICE_EXPECTED" "$TEMP_ROOT/service-zip-actual.txt"
assert_no_contamination "$CLIENT_EXPECTED" 1
assert_no_contamination "$SERVICE_EXPECTED" 0

for legal_file in LICENSE THIRD_PARTY_NOTICES; do
  source_hash="$(shasum -a 256 "$ROOT_DIR/$legal_file" | awk '{print $1}')"
  client_hash="$(shasum -a 256 "$CLIENT_PACKAGE/$legal_file" | awk '{print $1}')"
  service_hash="$(shasum -a 256 "$SERVICE_PACKAGE/$legal_file" | awk '{print $1}')"
  [[ "$source_hash" == "$client_hash" && "$source_hash" == "$service_hash" ]] || \
    release_die "$legal_file package hashes differ from source"
  unzip -p "$CLIENT_ZIP" "sts2_lan_connect/$legal_file" | cmp -s - "$ROOT_DIR/$legal_file" || \
    release_die "Client archive $legal_file differs from source"
  unzip -p "$SERVICE_ZIP" "sts2_lobby_service/$legal_file" | cmp -s - "$ROOT_DIR/$legal_file" || \
    release_die "Service archive $legal_file differs from source"
done

FAKE_APP="$TEMP_ROOT/Fake Game With Spaces/SlayTheSpire2.app"
FAKE_DATA="$TEMP_ROOT/Fake User Data"
mkdir -p "$FAKE_APP/Contents/MacOS" "$FAKE_DATA"
INSTALL_PLAN="$TEMP_ROOT/install-plan.txt"
"$ROOT_DIR/scripts/install-sts2-lan-connect-macos.sh" \
  --install \
  --dry-run \
  --no-save-sync \
  --skip-codesign \
  --package-dir "$CLIENT_PACKAGE" \
  --app-path "$FAKE_APP" \
  --data-dir "$FAKE_DATA" > "$INSTALL_PLAN"
for required in sts2_lan_connect.dll sts2_lan_connect.pck lobby-defaults.json LICENSE THIRD_PARTY_NOTICES; do
  grep -F "DRY-RUN copy: $CLIENT_PACKAGE/$required -> $FAKE_APP/Contents/MacOS/mods/sts2_lan_connect/$required" "$INSTALL_PLAN" >/dev/null || \
    release_die "Installer dry-run omitted required staged payload: $required"
done

printf 'Verified client archive: %s\n' "$CLIENT_ZIP"
printf 'Client SHA-256: %s\n' "$(shasum -a 256 "$CLIENT_ZIP" | awk '{print $1}')"
printf 'Verified service archive: %s\n' "$SERVICE_ZIP"
printf 'Service SHA-256: %s\n' "$(shasum -a 256 "$SERVICE_ZIP" | awk '{print $1}')"
printf 'Release verification passed; temporary outputs will now be removed.\n'
