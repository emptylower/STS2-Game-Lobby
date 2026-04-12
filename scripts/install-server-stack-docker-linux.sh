#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

if [[ -f "$SCRIPT_DIR/deploy/docker-compose.public-stack.yml" && -d "$SCRIPT_DIR/lobby-service" && -d "$SCRIPT_DIR/server-registry" ]]; then
  DEFAULT_SOURCE_DIR="$SCRIPT_DIR"
else
  DEFAULT_SOURCE_DIR="$ROOT_DIR"
fi

if [[ $EUID -eq 0 ]]; then
  DEFAULT_INSTALL_DIR="${STS2_SERVER_STACK_INSTALL_DIR:-/opt/sts2-server-stack-docker}"
else
  DEFAULT_INSTALL_DIR="${STS2_SERVER_STACK_INSTALL_DIR:-$HOME/sts2-server-stack-docker}"
fi

SOURCE_DIR="$DEFAULT_SOURCE_DIR"
INSTALL_DIR="$DEFAULT_INSTALL_DIR"
PROJECT_NAME="${STS2_SERVER_STACK_PROJECT_NAME:-sts2-public-stack}"
SKIP_BUILD=0
SKIP_UP=0

usage() {
  cat <<'EOF'
Usage:
  ./install-server-stack-docker-linux.sh [options]

Options:
  --source-dir <path>   Source directory that contains deploy/, lobby-service/, and server-registry/.
  --install-dir <path>  Target install root. Default: /opt/sts2-server-stack-docker when run as root.
  --project-name <name> docker compose project name. Default: sts2-public-stack
  --skip-build          Copy files and env templates, but skip docker compose build.
  --skip-up             Do not run docker compose up -d.
  --help                Show this help.
EOF
}

log() {
  printf '[sts2-server-stack-docker] %s\n' "$*"
}

die() {
  printf '[sts2-server-stack-docker] ERROR: %s\n' "$*" >&2
  exit 1
}

copy_tree() {
  local source_dir="$1"
  local target_dir="$2"

  if command -v rsync >/dev/null 2>&1; then
    rsync -a \
      --delete \
      --exclude node_modules \
      --exclude dist \
      --exclude release \
      --exclude data \
      --exclude .env \
      "$source_dir/" "$target_dir/"
  else
    rm -rf "$target_dir"
    mkdir -p "$target_dir"
    cp -R "$source_dir/." "$target_dir/"
    rm -rf \
      "$target_dir/node_modules" \
      "$target_dir/dist" \
      "$target_dir/release" \
      "$target_dir/data"
    rm -f "$target_dir/.env"
  fi
}

ensure_runtime_env() {
  local example_path="$1"
  local target_path="$2"
  if [[ ! -f "$target_path" ]]; then
    cp "$example_path" "$target_path"
    log "Created env template: $target_path"
    return 0
  fi
  return 1
}

env_has_placeholders() {
  local env_path="$1"
  grep -Eq 'CHANGE_ME|YOUR_PUBLIC_HOST' "$env_path"
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
    --project-name)
      [[ $# -ge 2 ]] || die "--project-name requires a value"
      PROJECT_NAME="$2"
      shift 2
      ;;
    --skip-build)
      SKIP_BUILD=1
      shift
      ;;
    --skip-up)
      SKIP_UP=1
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

[[ -d "$SOURCE_DIR/deploy" ]] || die "Source directory '$SOURCE_DIR' is missing deploy/"
[[ -d "$SOURCE_DIR/lobby-service" ]] || die "Source directory '$SOURCE_DIR' is missing lobby-service/"
[[ -d "$SOURCE_DIR/server-registry" ]] || die "Source directory '$SOURCE_DIR' is missing server-registry/"
command -v docker >/dev/null 2>&1 || die "docker was not found. Install Docker first."
docker compose version >/dev/null 2>&1 || die "docker compose was not found. Install Docker Compose v2 first."

mkdir -p "$INSTALL_DIR"
copy_tree "$SOURCE_DIR/deploy" "$INSTALL_DIR/deploy"
copy_tree "$SOURCE_DIR/lobby-service" "$INSTALL_DIR/lobby-service"
copy_tree "$SOURCE_DIR/server-registry" "$INSTALL_DIR/server-registry"

mkdir -p \
  "$INSTALL_DIR/deploy/data/lobby-service" \
  "$INSTALL_DIR/deploy/data/postgres"

created_any_env=0
ensure_runtime_env "$INSTALL_DIR/deploy/lobby-service.env.example" "$INSTALL_DIR/deploy/lobby-service.env" && created_any_env=1 || true
ensure_runtime_env "$INSTALL_DIR/deploy/server-registry.env.example" "$INSTALL_DIR/deploy/server-registry.env" && created_any_env=1 || true
ensure_runtime_env "$INSTALL_DIR/deploy/postgres.env.example" "$INSTALL_DIR/deploy/postgres.env" && created_any_env=1 || true
ensure_runtime_env "$INSTALL_DIR/deploy/.env.example" "$INSTALL_DIR/deploy/.env" && created_any_env=1 || true

compose_cmd=(
  docker compose
  -p "$PROJECT_NAME"
  -f "$INSTALL_DIR/deploy/docker-compose.public-stack.yml"
)

if [[ "$SKIP_BUILD" -eq 0 ]]; then
  log "Building Docker images"
  "${compose_cmd[@]}" build
fi

if [[ "$created_any_env" -eq 1 ]]; then
  log "Edit the generated env files under $INSTALL_DIR/deploy before starting containers."
  exit 0
fi

if env_has_placeholders "$INSTALL_DIR/deploy/lobby-service.env" \
  || env_has_placeholders "$INSTALL_DIR/deploy/server-registry.env" \
  || env_has_placeholders "$INSTALL_DIR/deploy/postgres.env"; then
  die "Env files still contain CHANGE_ME / YOUR_PUBLIC_HOST placeholders. Update them before deployment."
fi

if [[ "$SKIP_UP" -eq 1 ]]; then
  log "docker compose up skipped."
  exit 0
fi

log "Starting Docker stack"
"${compose_cmd[@]}" up -d
log "Done. Health checks:"
log "  curl http://127.0.0.1:8787/health"
log "  curl http://127.0.0.1:18787/health"
