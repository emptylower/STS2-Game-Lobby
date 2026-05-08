#!/usr/bin/env bash
# sts2-peer-sidecar/deploy/install.sh
set -euo pipefail

TARBALL="${1:-sts2-peer-sidecar.tar.gz}"
INSTALL_DIR="/opt/sts2-peer-sidecar"
ENV_DIR="/etc/sts2-peer-sidecar"
USER_NAME="sts2sidecar"

if [[ "$EUID" -ne 0 ]]; then
  echo "must run as root" >&2; exit 1
fi

if ! id "$USER_NAME" &>/dev/null; then
  useradd --system --no-create-home --shell /usr/sbin/nologin "$USER_NAME"
fi

mkdir -p "$INSTALL_DIR" "$ENV_DIR"
tar -xzf "$TARBALL" -C "$INSTALL_DIR" --strip-components=1
chown -R "$USER_NAME":"$USER_NAME" "$INSTALL_DIR"

if [[ ! -f "$ENV_DIR/sidecar.env" ]]; then
  cat >"$ENV_DIR/sidecar.env" <<'EOF'
LOBBY_PUBLIC_BASE_URL=https://your-lobby.example.com
PEER_LISTEN_PORT=18800
PEER_CF_DISCOVERY_BASE_URL=https://your-cf-domain.example.com
PEER_STATE_DIR=/var/lib/sts2-peer-sidecar
EOF
  echo "wrote default env to $ENV_DIR/sidecar.env — edit before starting"
fi

mkdir -p /var/lib/sts2-peer-sidecar
chown -R "$USER_NAME":"$USER_NAME" /var/lib/sts2-peer-sidecar

cp "$INSTALL_DIR/deploy/sts2-peer-sidecar.service" /etc/systemd/system/
systemctl daemon-reload
echo "installed; enable with: sudo systemctl enable --now sts2-peer-sidecar"
