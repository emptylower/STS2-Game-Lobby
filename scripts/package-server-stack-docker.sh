#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RELEASE_DIR="$ROOT_DIR/releases"
PACKAGE_NAME="sts2_server_stack_docker"
PACKAGE_ROOT="$RELEASE_DIR/$PACKAGE_NAME"

require_file() {
  local path="$1"
  [[ -f "$path" ]] || {
    echo "Required file is missing from Docker package: $path" >&2
    exit 1
  }
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

verify_package_manifest() {
  local package_dir="$1"

  require_file "$package_dir/README.md"
  require_file "$package_dir/install-server-stack-docker-linux.sh"
  require_file "$package_dir/maintain-server-stack-docker.sh"
  require_file "$package_dir/deploy/docker-compose.public-stack.yml"
  require_file "$package_dir/deploy/.env.example"
  require_file "$package_dir/deploy/lobby-service.env.example"
  require_file "$package_dir/deploy/server-registry.env.example"
  require_file "$package_dir/deploy/postgres.env.example"
  require_file "$package_dir/docs/STS2_SERVER_DOCKER_OPERATION_GUIDE_ZH.md"
  require_file "$package_dir/lobby-service/Dockerfile"
  require_file "$package_dir/server-registry/Dockerfile"
}

verify_zip_manifest() {
  local zip_path="$1"
  local zip_listing
  zip_listing="$(zipinfo -1 "$zip_path")"

  [[ "$zip_listing" == *"$PACKAGE_NAME/deploy/docker-compose.public-stack.yml"* ]] || {
    echo "Docker package zip is missing docker-compose.public-stack.yml" >&2
    exit 1
  }
  [[ "$zip_listing" == *"$PACKAGE_NAME/install-server-stack-docker-linux.sh"* ]] || {
    echo "Docker package zip is missing install-server-stack-docker-linux.sh" >&2
    exit 1
  }
}

mkdir -p "$RELEASE_DIR"
rm -rf "$PACKAGE_ROOT"
mkdir -p "$PACKAGE_ROOT/deploy" "$PACKAGE_ROOT/docs"

copy_tree "$ROOT_DIR/lobby-service" "$PACKAGE_ROOT/lobby-service"
copy_tree "$ROOT_DIR/server-registry" "$PACKAGE_ROOT/server-registry"
cp "$ROOT_DIR/README.md" "$PACKAGE_ROOT/README.md"
cp "$ROOT_DIR/deploy/docker-compose.public-stack.yml" "$PACKAGE_ROOT/deploy/"
cp "$ROOT_DIR/deploy/.env.example" "$PACKAGE_ROOT/deploy/"
cp "$ROOT_DIR/deploy/lobby-service.env.example" "$PACKAGE_ROOT/deploy/"
cp "$ROOT_DIR/deploy/server-registry.env.example" "$PACKAGE_ROOT/deploy/"
cp "$ROOT_DIR/deploy/postgres.env.example" "$PACKAGE_ROOT/deploy/"
cp "$ROOT_DIR/docs/STS2_SERVER_DOCKER_OPERATION_GUIDE_ZH.md" "$PACKAGE_ROOT/docs/"
cp "$ROOT_DIR/docs/STS2_LOBBY_DEPLOYMENT_GUIDE_ZH.md" "$PACKAGE_ROOT/docs/"
cp "$ROOT_DIR/scripts/install-server-stack-docker-linux.sh" "$PACKAGE_ROOT/"
cp "$ROOT_DIR/scripts/maintain-server-stack-docker.sh" "$PACKAGE_ROOT/"
chmod +x \
  "$PACKAGE_ROOT/install-server-stack-docker-linux.sh" \
  "$PACKAGE_ROOT/maintain-server-stack-docker.sh"

verify_package_manifest "$PACKAGE_ROOT"

cd "$RELEASE_DIR"
rm -f "${PACKAGE_NAME}.zip"
zip -qr "${PACKAGE_NAME}.zip" "$PACKAGE_NAME"
verify_zip_manifest "${PACKAGE_NAME}.zip"
echo "Package created at: $RELEASE_DIR/${PACKAGE_NAME}.zip"
