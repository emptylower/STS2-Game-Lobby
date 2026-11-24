# Native PlayerLeft dispatch checks

This narrow Godot Mono harness executes the persistent `LanConnectPlayerLeftDispatchTests` suite through the actual Harmony dispatch prefix and native message buffer. It uses fake ENet transports and skips unrelated message-registry startup; sender, channel, session, body and roster validation remain active. It does not launch a game session or contact Steam.

Build the mod first, then run from the repository:

```sh
STS2_DATA_DIR='/path/to/game/data_sts2_macos_arm64' \
GODOT_BIN='/path/to/Godot_mono.app/Contents/MacOS/Godot' \
bash scripts/testing/run-player-left-dispatch.sh
```

Set `LAN_CONNECT_ASSEMBLY` to test another mod build; the default is the project's Debug DLL. A compatible .NET 9 runtime is required by the game and Harmony. If the machine also selects .NET 10, provide a .NET 9-only `DOTNET_ROOT` and `DOTNET_ROLL_FORWARD=LatestMajor` for this command. This does not change global runtime settings.

The harness first verifies that a deliberately failing GdUnit assertion throws. It then runs 14 cases and exits zero only when all pass. Output includes `P1_DISPATCH_ASSERTION_CONTROL_PASS` and `P1_DISPATCH_RESULTS passed=14 total=14`. The script builds and runs serially, and all engine work is headless. Game and mod DLLs are referenced from local paths and are not committed here.
