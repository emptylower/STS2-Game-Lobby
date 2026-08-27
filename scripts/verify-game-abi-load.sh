#!/usr/bin/env bash
# 用 GameAbiLoadCheck 工具验证 mod dll 能否在指定游戏 data 目录下完成真实 Assembly.GetTypes() 加载。
#
# 用法:
#   ./scripts/verify-game-abi-load.sh [--data-dir <dir>] [--dll <path>]
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

GAME_DATA_DIR="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64"
MOD_DLL="$REPO_ROOT/sts2-lan-connect/release/.build_mod_output/sts2_lan_connect/sts2_lan_connect.dll"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --data-dir)
      GAME_DATA_DIR="$2"
      shift 2
      ;;
    --dll)
      MOD_DLL="$2"
      MOD_DLL_EXPLICIT=1
      shift 2
      ;;
    *)
      echo "unknown argument: $1" >&2
      echo "usage: $0 [--data-dir <dir>] [--dll <path>]" >&2
      exit 2
      ;;
  esac
done

if [[ -n "${MOD_DLL_EXPLICIT:-}" && ! -f "$MOD_DLL" ]]; then
  echo "mod dll passed via --dll not found: $MOD_DLL" >&2
  exit 2
fi

if [[ ! -f "$MOD_DLL" ]]; then
  FALLBACK="$REPO_ROOT/releases/sts2_lan_connect/sts2_lan_connect.dll"
  if [[ -f "$FALLBACK" ]]; then
    MOD_DLL="$FALLBACK"
  else
    echo "mod dll not found at either location:" >&2
    echo "  - sts2-lan-connect/release/.build_mod_output/sts2_lan_connect/sts2_lan_connect.dll" >&2
    echo "  - releases/sts2_lan_connect/sts2_lan_connect.dll" >&2
    exit 2
  fi
fi

if [[ ! -d "$GAME_DATA_DIR" ]]; then
  echo "game data dir not found: $GAME_DATA_DIR" >&2
  exit 2
fi

exec dotnet run --project "$REPO_ROOT/sts2-lan-connect/tools/GameAbiLoadCheck" -- "$MOD_DLL" "$GAME_DATA_DIR"
