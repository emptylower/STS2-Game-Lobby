#!/usr/bin/env bash
# Run only the native dispatch regression suite, in one Godot Mono process.
set -euo pipefail

task_repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
task_project_dir="$task_repo_root/tools/PlayerLeftDispatchCheck"
task_dotnet_bin="${DOTNET_BIN:-$(command -v dotnet || true)}"
task_godot_bin="${GODOT_BIN:-$(command -v godot451-mono || command -v godot || true)}"
task_game_data="${STS2_DATA_DIR:?Set STS2_DATA_DIR to the installed game data directory}"
task_mod_assembly="${LAN_CONNECT_ASSEMBLY:-$task_repo_root/sts2-lan-connect/.godot/mono/temp/bin/Debug/sts2_lan_connect.dll}"
task_log_file="${LAN_CONNECT_DISPATCH_LOG:-${TMPDIR:-/tmp}/sts2-player-left-dispatch.log}"

for task_required_file in "$task_dotnet_bin" "$task_godot_bin" "$task_mod_assembly" \
    "$task_game_data/sts2.dll" "$task_game_data/0Harmony.dll" "$task_game_data/Steamworks.NET.dll"; do
    if [[ ! -f "$task_required_file" ]]; then
        echo "Missing required file: $task_required_file" >&2
        exit 2
    fi
done

# Passing both forms avoids /tmp symlink and non-ASCII project-path mismatches
# in Godot's generated ScriptPath metadata.
task_project_dir_base64="$(printf '%s' "$task_project_dir/" | base64 | tr -d '\r\n')"
"$task_dotnet_bin" build "$task_project_dir/PlayerLeftDispatchCheck.csproj" \
    -m:1 -nodeReuse:false -p:UseSharedCompilation=false \
    "-p:Sts2DataDir=$task_game_data" "-p:LanConnectAssembly=$task_mod_assembly" \
    "-p:GodotProjectDir=$task_project_dir/" "-p:GodotProjectDirBase64=$task_project_dir_base64"

"$task_godot_bin" --headless --path "$task_project_dir" --log-file "$task_log_file"
