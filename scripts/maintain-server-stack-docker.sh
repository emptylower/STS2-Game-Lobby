#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

if [[ -f "$SCRIPT_DIR/deploy/docker-compose.public-stack.yml" ]]; then
  DEFAULT_INSTALL_DIR="$SCRIPT_DIR"
elif [[ $EUID -eq 0 ]]; then
  DEFAULT_INSTALL_DIR="${STS2_SERVER_STACK_INSTALL_DIR:-/opt/sts2-server-stack-docker}"
else
  DEFAULT_INSTALL_DIR="${STS2_SERVER_STACK_INSTALL_DIR:-$HOME/sts2-server-stack-docker}"
fi

INSTALL_DIR="$DEFAULT_INSTALL_DIR"
PROJECT_NAME="${STS2_SERVER_STACK_PROJECT_NAME:-sts2-public-stack}"

usage() {
  cat <<'EOF'
Usage:
  ./maintain-server-stack-docker.sh [options] <command> [args]

Options:
  --install-dir <path>  Stack install directory. Default: /opt/sts2-server-stack-docker when run as root.
  --project-name <name> docker compose project name. Default: sts2-public-stack
  --help                Show this help.

Commands:
  status
  start
  stop
  down
  restart [service]
  rebuild
  logs [service] [--follow] [--tail N]
  backup
  prune-images
EOF
}

log() {
  printf '[sts2-server-stack-maintain] %s\n' "$*" >&2
}

die() {
  printf '[sts2-server-stack-maintain] ERROR: %s\n' "$*" >&2
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
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
    --help|-h)
      usage
      exit 0
      ;;
    *)
      break
      ;;
  esac
done

COMMAND="${1:-}"
[[ -n "$COMMAND" ]] || {
  usage
  exit 1
}
shift || true

COMPOSE_FILE="$INSTALL_DIR/deploy/docker-compose.public-stack.yml"
[[ -f "$COMPOSE_FILE" ]] || die "Compose file not found: $COMPOSE_FILE"
command -v docker >/dev/null 2>&1 || die "docker was not found."

compose_cmd=(
  docker compose
  -p "$PROJECT_NAME"
  -f "$COMPOSE_FILE"
)

case "$COMMAND" in
  status)
    "${compose_cmd[@]}" ps
    ;;
  start)
    "${compose_cmd[@]}" up -d
    ;;
  stop)
    "${compose_cmd[@]}" stop
    ;;
  down)
    "${compose_cmd[@]}" down
    ;;
  restart)
    if [[ $# -gt 0 ]]; then
      "${compose_cmd[@]}" restart "$1"
    else
      "${compose_cmd[@]}" restart
    fi
    ;;
  rebuild)
    "${compose_cmd[@]}" up -d --build
    ;;
  logs)
    service=""
    follow=0
    tail_lines="200"
    if [[ $# -gt 0 && "$1" != --* ]]; then
      service="$1"
      shift
    fi
    while [[ $# -gt 0 ]]; do
      case "$1" in
        --follow|-f)
          follow=1
          shift
          ;;
        --tail)
          [[ $# -ge 2 ]] || die "--tail requires a value"
          tail_lines="$2"
          shift 2
          ;;
        *)
          die "Unknown logs option: $1"
          ;;
      esac
    done
    log "Docker logs are rotated automatically by json-file (10m x 5 files per container)."
    if [[ "$follow" -eq 1 ]]; then
      if [[ -n "$service" ]]; then
        "${compose_cmd[@]}" logs --tail "$tail_lines" -f "$service"
      else
        "${compose_cmd[@]}" logs --tail "$tail_lines" -f
      fi
    else
      if [[ -n "$service" ]]; then
        "${compose_cmd[@]}" logs --tail "$tail_lines" "$service"
      else
        "${compose_cmd[@]}" logs --tail "$tail_lines"
      fi
    fi
    ;;
  backup)
    timestamp="$(date '+%Y%m%d-%H%M%S')"
    backup_dir="$INSTALL_DIR/backups"
    backup_path="$backup_dir/sts2-server-stack-$timestamp.tgz"
    mkdir -p "$backup_dir"
    tar -czf "$backup_path" \
      -C "$INSTALL_DIR" \
      deploy/lobby-service.env \
      deploy/server-registry.env \
      deploy/postgres.env \
      deploy/data
    echo "$backup_path"
    ;;
  prune-images)
    docker image prune -f
    ;;
  *)
    die "Unknown command: $COMMAND"
    ;;
esac
