# v0.6 Tail and RitsuLib feasibility probe

This directory is a test-only gate. It does not package game, Harmony, or RitsuLib DLLs. Supply real target assemblies through MSBuild properties and do not treat a successful compile as permission to integrate RitsuLib into production.

## Desktop public-contract test

```sh
DOTNET_ROOT=/Users/mac/.dotnet PATH=/Users/mac/.dotnet:$PATH \
  /Users/mac/.dotnet/dotnet test Sts2TailPrototype.csproj -m:1 \
  -p:Sts2DataDir=/path/to/sts2-data \
  -p:RitsuLibAssembly=/path/to/STS2-RitsuLib.dll
```

`RitsuInteropMatrixTests` uses the frozen 36-byte `tail-probe-complete-v1.bin` fixture. The adjacent JSON independently specifies its offsets and SHA-256. The matrix calls only the public `RegisterBytes`, `Write`, and `Read` Ritsu APIs. `PublicContractInspectionTests` is intentionally a blocking test until the real assembly exposes public APIs for all-extension inventory, criticality, and explicit tail/cursor coordination. Private registration stores, reflection of non-public members, RC4 postfix calls, unpatching, and manual postfix invocation are prohibited.

## Android probe

```sh
DOTNET_ROOT=/Users/mac/.dotnet PATH=/Users/mac/.dotnet:$PATH \
  /Users/mac/.dotnet/dotnet build Sts2TailAndroidProbe.csproj -m:1 \
  -p:Sts2DataDir="$ANDROID_STS2_DATA_DIR" -p:BuildAndroidProbe=true
```

`artifacts/android-probe/` must contain only `sts2_lan_v06_probe.dll` and its manifest. `run_android_probe.sh` requires all device, log, fixture, package-tree, and Ritsu package variables documented in the feasibility brief. It launches four separate processes: both sender states and both receiver states. The deterministic verifier accepts exactly one terminal JSON marker per process and rejects missing Ritsu initialization, open generic targets, cursor drift, or `InvalidProgramException`.

Hash a downloaded unpacked Ritsu release package before running it:

```sh
python3 package_tree_hash.py "$ANDROID_RITSULIB_PACKAGE_DIR"
```

Do not check in `bin/`, `obj/`, `artifacts/`, DLLs, local logs, or downloaded packages.
