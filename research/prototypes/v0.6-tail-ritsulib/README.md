# v0.6 carrier feasibility probe

This is a test-only gate. No game, Harmony, or RitsuLib DLL is tracked here.

`RitsuInteropMatrixTests` proves the frozen 36-byte container through the standalone carrier. `SidecarCarrierProbe` uses only the public v0.5.10 typed-sidecar contract: `RitsuLibSidecarMessageDescriptor<T>`, `RitsuLibSidecarTypedMessageRegistry.Register/Subscribe/SendToHost/SendToPeer`, and `RitsuLibSidecarSessionManager.ObserveNetService/SetPeerReachabilityHint/CanSendToPeer`. It never calls message-tail registration or a `RunManager` resolver.

The sidecar test needs a real two-game-process launcher:

```sh
export STS2_RITSU_SIDECAR_PROBE_COMMAND=/absolute/path/to/real-game-pair-launcher
export STS2_RITSU_SIDECAR_PROBE_EVIDENCE=/absolute/path/to/fresh-evidence.json
```

The launcher receives `--flow-nonce` and `--evidence`. It must start both real game processes with matching RitsuLib, validate a trusted ticket/peer/flow binding, seed `Supported` with the public hint only after validation, use direct `INetGameService` typed send, pair the carrier before the vanilla handler, clear the hint to `Unknown`, and prove peer-ID reuse begins unknown. Without those inputs the test intentionally reports `BLOCKED`.

The Android probe stages only its DLL and manifest. Its standalone mode is independently runnable; its sidecar mode consumes fresh evidence from the paired desktop real-game launcher. `run_android_probe.sh` requires a configured MuMu/ADB device, official unpacked Ritsu package, and desktop pair launcher, then validates one standalone Android log and two Android/desktop sidecar pairs.
