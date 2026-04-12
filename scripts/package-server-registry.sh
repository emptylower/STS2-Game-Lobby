#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE_DIR="$ROOT_DIR/server-registry"
RELEASE_DIR="$SOURCE_DIR/release"
PACKAGE_NAME="sts2_server_registry"
PACKAGE_ROOT="$RELEASE_DIR/$PACKAGE_NAME"
INSTALLER="$ROOT_DIR/scripts/install-server-registry-linux.sh"

require_file() {
  local path="$1"
  [[ -f "$path" ]] || {
    echo "Expected file is missing from registry package: $path" >&2
    exit 1
  }
}

verify_package_manifest() {
  local package_dir="$1"

  require_file "$package_dir/README.md"
  require_file "$package_dir/install-server-registry-linux.sh"
  require_file "$package_dir/server-registry/Dockerfile"
  require_file "$package_dir/server-registry/.dockerignore"
  require_file "$package_dir/server-registry/package.json"
  require_file "$package_dir/server-registry/package-lock.json"
  require_file "$package_dir/server-registry/tsconfig.json"
  require_file "$package_dir/server-registry/.env.example"
  require_file "$package_dir/server-registry/deploy/.env.example"
  require_file "$package_dir/server-registry/deploy/docker-compose.server-registry.yml"
  require_file "$package_dir/server-registry/deploy/server-registry.docker.env.example"
  require_file "$package_dir/server-registry/deploy/postgres.docker.env.example"
}

verify_zip_manifest() {
  local zip_path="$1"
  local zip_listing
  zip_listing="$(zipinfo -1 "$zip_path")"

  [[ "$zip_listing" == *"$PACKAGE_NAME/install-server-registry-linux.sh"* ]] || {
    echo "Registry zip is missing install-server-registry-linux.sh" >&2
    exit 1
  }
  [[ "$zip_listing" == *"$PACKAGE_NAME/server-registry/Dockerfile"* ]] || {
    echo "Registry zip is missing Dockerfile" >&2
    exit 1
  }
}

[[ -f "$SOURCE_DIR/package.json" ]] || {
  echo "server-registry/package.json not found" >&2
  exit 1
}

rm -rf "$PACKAGE_ROOT"
mkdir -p "$PACKAGE_ROOT/server-registry"

cp -R "$SOURCE_DIR/src" "$PACKAGE_ROOT/server-registry/"
cp "$SOURCE_DIR/package.json" "$PACKAGE_ROOT/server-registry/"
cp "$SOURCE_DIR/package-lock.json" "$PACKAGE_ROOT/server-registry/"
cp "$SOURCE_DIR/tsconfig.json" "$PACKAGE_ROOT/server-registry/"
cp "$SOURCE_DIR/.env.example" "$PACKAGE_ROOT/server-registry/"
cp "$SOURCE_DIR/Dockerfile" "$PACKAGE_ROOT/server-registry/"
cp "$SOURCE_DIR/.dockerignore" "$PACKAGE_ROOT/server-registry/"
cp -R "$SOURCE_DIR/deploy" "$PACKAGE_ROOT/server-registry/"
cp "$SOURCE_DIR/README.md" "$PACKAGE_ROOT/README.md"
cp "$INSTALLER" "$PACKAGE_ROOT/"
chmod +x "$PACKAGE_ROOT/install-server-registry-linux.sh"
verify_package_manifest "$PACKAGE_ROOT"

cd "$RELEASE_DIR"
rm -f "${PACKAGE_NAME}.zip"
zip -qr "${PACKAGE_NAME}.zip" "$PACKAGE_NAME"
verify_zip_manifest "${PACKAGE_NAME}.zip"
echo "Package created at: $RELEASE_DIR/${PACKAGE_NAME}.zip"
