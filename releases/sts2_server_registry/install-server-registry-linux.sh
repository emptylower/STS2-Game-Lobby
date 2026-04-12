#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PACKAGED_SOURCE_DIR="$SCRIPT_DIR/server-registry"
REPO_SOURCE_DIR="$ROOT_DIR/server-registry"

if [[ -f "$PACKAGED_SOURCE_DIR/package.json" ]]; then
  DEFAULT_SOURCE_DIR="$PACKAGED_SOURCE_DIR"
else
  DEFAULT_SOURCE_DIR="$REPO_SOURCE_DIR"
fi

SOURCE_DIR="${STS2_SERVER_REGISTRY_SOURCE_DIR:-$DEFAULT_SOURCE_DIR}"

if [[ $EUID -eq 0 ]]; then
  DEFAULT_INSTALL_DIR="${STS2_SERVER_REGISTRY_INSTALL_DIR:-/opt/sts2-server-registry}"
else
  DEFAULT_INSTALL_DIR="${STS2_SERVER_REGISTRY_INSTALL_DIR:-$HOME/sts2-server-registry}"
fi

INSTALL_DIR="$DEFAULT_INSTALL_DIR"
SERVICE_NAME="${STS2_SERVER_REGISTRY_SERVICE_NAME:-sts2-server-registry}"
HOST="${STS2_REGISTRY_HOST:-0.0.0.0}"
PORT="${STS2_REGISTRY_PORT:-18787}"
DATABASE_URL="${STS2_REGISTRY_DATABASE_URL:-postgres://postgres:postgres@127.0.0.1:5432/sts2_server_registry}"
PUBLIC_BASE_URL="${STS2_REGISTRY_PUBLIC_BASE_URL:-http://127.0.0.1:18787}"
ADMIN_USERNAME="${STS2_REGISTRY_ADMIN_USERNAME:-admin}"
ADMIN_PASSWORD_HASH="${STS2_REGISTRY_ADMIN_PASSWORD_HASH:-}"
ADMIN_SESSION_SECRET="${STS2_REGISTRY_ADMIN_SESSION_SECRET:-}"
SERVER_TOKEN_SECRET="${STS2_REGISTRY_SERVER_TOKEN_SECRET:-}"
ADMIN_SESSION_TTL_HOURS="${STS2_REGISTRY_ADMIN_SESSION_TTL_HOURS:-168}"
LIGHT_PROBE_INTERVAL_SECONDS="${STS2_REGISTRY_LIGHT_PROBE_INTERVAL_SECONDS:-180}"
BANDWIDTH_PROBE_INTERVAL_SECONDS="${STS2_REGISTRY_BANDWIDTH_PROBE_INTERVAL_SECONDS:-1800}"
PROBE_TIMEOUT_MS="${STS2_REGISTRY_PROBE_TIMEOUT_MS:-5000}"
BANDWIDTH_SAMPLE_BYTES="${STS2_REGISTRY_BANDWIDTH_SAMPLE_BYTES:-8388608}"
PUBLIC_HEARTBEAT_STALE_SECONDS="${STS2_REGISTRY_PUBLIC_HEARTBEAT_STALE_SECONDS:-600}"
PUBLIC_PROBE_STALE_SECONDS="${STS2_REGISTRY_PUBLIC_PROBE_STALE_SECONDS:-600}"
NODE_BIN="${NODE_BIN:-$(command -v node || true)}"
NPM_BIN="${NPM_BIN:-$(command -v npm || true)}"
SKIP_SYSTEMD=0
RUN_AS_USER="${STS2_SERVER_REGISTRY_RUN_AS_USER:-${SUDO_USER:-$USER}}"
RUN_AS_GROUP="${STS2_SERVER_REGISTRY_RUN_AS_GROUP:-$(id -gn "$RUN_AS_USER" 2>/dev/null || true)}"

usage() {
  cat <<'EOF'
Usage:
  ./scripts/install-server-registry-linux.sh [options]

Options:
  --source-dir <path>   Source directory that contains server-registry/package.json.
  --install-dir <path>  Target install root. The app will be copied into <path>/server-registry.
  --service-name <name> systemd service name. Default: sts2-server-registry
  --skip-systemd        Only install files and build the service; do not create/start systemd unit.
  --help                Show this help.
EOF
}

log() {
  printf '[sts2-server-registry] %s\n' "$*"
}

die() {
  printf '[sts2-server-registry] ERROR: %s\n' "$*" >&2
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --source-dir)
      [[ $# -ge 2 ]] || die "--source-dir requires a value"
      SOURCE_DIR="$2"
      shift 2
      ;;
    --install-dir)
      [[ $# -ge 2 ]] || die "--install-dir requires a value"
      INSTALL_DIR="$2"
      shift 2
      ;;
    --service-name)
      [[ $# -ge 2 ]] || die "--service-name requires a value"
      SERVICE_NAME="$2"
      shift 2
      ;;
    --skip-systemd)
      SKIP_SYSTEMD=1
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

[[ -n "$NODE_BIN" && -x "$NODE_BIN" ]] || die "node was not found. Install Node.js 20+ first."
[[ -n "$NPM_BIN" && -x "$NPM_BIN" ]] || die "npm was not found. Install Node.js 20+ first."
[[ -f "$SOURCE_DIR/package.json" ]] || die "Source directory '$SOURCE_DIR' does not contain package.json"

node_major="$("$NODE_BIN" -p 'process.versions.node.split(".")[0]')"
if [[ "$node_major" -lt 20 ]]; then
  die "Node.js 20+ is required. Current version: $("$NODE_BIN" -v)"
fi

APP_DIR="$INSTALL_DIR/server-registry"
ENV_FILE="$APP_DIR/.env"
START_SCRIPT="$INSTALL_DIR/start-server-registry.sh"
NODE_BIN_DIR="$(dirname "$NODE_BIN")"
SYSTEM_PATH="$NODE_BIN_DIR:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"

mkdir -p "$INSTALL_DIR"

if command -v rsync >/dev/null 2>&1; then
  log "Copying registry files to: $APP_DIR"
  mkdir -p "$APP_DIR"
  rsync -a --delete --exclude node_modules --exclude dist --exclude .env "$SOURCE_DIR/" "$APP_DIR/"
else
  log "rsync not found; using a clean copy fallback."
  env_backup=""
  if [[ -f "$ENV_FILE" ]]; then
    env_backup="$(mktemp)"
    cp "$ENV_FILE" "$env_backup"
  fi
  rm -rf "$APP_DIR"
  mkdir -p "$APP_DIR"
  cp -R "$SOURCE_DIR/." "$APP_DIR/"
  rm -rf "$APP_DIR/node_modules" "$APP_DIR/dist"
  rm -f "$APP_DIR/.env"
  if [[ -n "$env_backup" ]]; then
    cp "$env_backup" "$ENV_FILE"
    rm -f "$env_backup"
  fi
fi

if [[ ! -f "$ENV_FILE" ]]; then
  cat > "$ENV_FILE" <<EOF
HOST=$HOST
PORT=$PORT
DATABASE_URL=$DATABASE_URL
PUBLIC_BASE_URL=$PUBLIC_BASE_URL
ADMIN_USERNAME=$ADMIN_USERNAME
ADMIN_PASSWORD_HASH=$ADMIN_PASSWORD_HASH
ADMIN_SESSION_SECRET=$ADMIN_SESSION_SECRET
SERVER_TOKEN_SECRET=$SERVER_TOKEN_SECRET
ADMIN_SESSION_TTL_HOURS=$ADMIN_SESSION_TTL_HOURS
LIGHT_PROBE_INTERVAL_SECONDS=$LIGHT_PROBE_INTERVAL_SECONDS
BANDWIDTH_PROBE_INTERVAL_SECONDS=$BANDWIDTH_PROBE_INTERVAL_SECONDS
PROBE_TIMEOUT_MS=$PROBE_TIMEOUT_MS
BANDWIDTH_SAMPLE_BYTES=$BANDWIDTH_SAMPLE_BYTES
PUBLIC_HEARTBEAT_STALE_SECONDS=$PUBLIC_HEARTBEAT_STALE_SECONDS
PUBLIC_PROBE_STALE_SECONDS=$PUBLIC_PROBE_STALE_SECONDS
EOF
  log "Created default environment file: $ENV_FILE"
else
  log "Keeping existing environment file: $ENV_FILE"
fi

log "Installing Node.js dependencies"
(cd "$APP_DIR" && "$NPM_BIN" ci)

log "Building server registry"
(cd "$APP_DIR" && "$NPM_BIN" run build)

cat > "$START_SCRIPT" <<EOF
#!/usr/bin/env bash
set -euo pipefail
export PATH="$SYSTEM_PATH"
cd "$APP_DIR"
exec "$NPM_BIN" start
EOF
chmod +x "$START_SCRIPT"

if [[ $EUID -eq 0 ]]; then
  chown -R "$RUN_AS_USER:$RUN_AS_GROUP" "$INSTALL_DIR"
fi

required_boot_env_missing=0
if ! grep -q '^DATABASE_URL=.\+' "$ENV_FILE" \
  || ! grep -q '^ADMIN_PASSWORD_HASH=.\+' "$ENV_FILE" \
  || ! grep -q '^ADMIN_SESSION_SECRET=.\+' "$ENV_FILE" \
  || ! grep -q '^SERVER_TOKEN_SECRET=.\+' "$ENV_FILE"; then
  required_boot_env_missing=1
fi

if [[ "$SKIP_SYSTEMD" -eq 1 ]]; then
  log "systemd installation skipped."
  log "Manual start: $START_SCRIPT"
  exit 0
fi

if [[ "$required_boot_env_missing" -eq 1 ]]; then
  log "Skipping systemd installation because ADMIN_PASSWORD_HASH / ADMIN_SESSION_SECRET / SERVER_TOKEN_SECRET / DATABASE_URL must be configured first."
  log "Manual start after editing $ENV_FILE: $START_SCRIPT"
  exit 0
fi

if ! command -v systemctl >/dev/null 2>&1; then
  log "systemctl not found. Manual start: $START_SCRIPT"
  exit 0
fi

if [[ $EUID -ne 0 ]]; then
  log "Run as root to auto-install systemd. Manual start: $START_SCRIPT"
  exit 0
fi

UNIT_PATH="/etc/systemd/system/$SERVICE_NAME.service"
cat > "$UNIT_PATH" <<EOF
[Unit]
Description=STS2 Server Registry
After=network.target

[Service]
Type=simple
WorkingDirectory=$APP_DIR
EnvironmentFile=$ENV_FILE
Environment=PATH=$SYSTEM_PATH
ExecStart=$START_SCRIPT
Restart=always
RestartSec=3
User=$RUN_AS_USER
Group=$RUN_AS_GROUP

[Install]
WantedBy=multi-user.target
EOF

log "Installed systemd unit: $UNIT_PATH"
systemctl daemon-reload
systemctl enable "$SERVICE_NAME"
if systemctl is-active --quiet "$SERVICE_NAME"; then
  systemctl restart "$SERVICE_NAME"
else
  systemctl start "$SERVICE_NAME"
fi
log "Service started. Health check: curl http://127.0.0.1:$PORT/health"
