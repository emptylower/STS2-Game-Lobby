#!/usr/bin/env bash
# cf-worker/scripts/seed-kv.sh
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

SEEDS_PATH="${1:-../data/seeds.json}"
if [[ ! -f "$SEEDS_PATH" ]]; then
  echo "seeds file not found: $SEEDS_PATH" >&2
  exit 1
fi

npx wrangler kv key put --binding DISCOVERY_KV "peers:seeds" "$(cat "$SEEDS_PATH")"
npx wrangler kv key put --binding DISCOVERY_KV "announcements" '{"version":1,"updated_at":"2026-05-08T00:00:00Z","items":[]}'
echo "seeded peers:seeds and announcements"
