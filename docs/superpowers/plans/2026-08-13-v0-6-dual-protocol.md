# STS2 LAN Connect v0.6 Dual Protocol Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship client and lobby-service `0.6.0-alpha.1` with a default `0.3.x-0.5.x` compatible `4/5-bit` room mode and an opt-in `tail_v1` room mode that keeps the STS2 body at vanilla `2/3-bit`, supports 2-8 players, and coexists with RitsuLib only through proven public contracts.

**Architecture:** Install one atomic client protocol dispatcher at startup, freeze one immutable room selection before the game transport starts, and route each message through either the historical compat codec or the new vanilla-body-plus-LAN-Tail codec. The lobby service owns canonical profile/version selection and rejects incompatible clients before issuing tickets; the game handshake repeats `tail_v1` validation. RitsuLib integration is gated by a real-assembly prototype and may not fall back to RC4 private postfix composition.

**Tech Stack:** Godot 4.5.1, .NET 9/C#, Harmony 2.4.x, xUnit 2.9, GdUnit4 5.0, Node 20, strict ESM TypeScript, Node `node:test`.

## Global Constraints

- Approved design: `docs/superpowers/specs/2026-08-13-v0-6-dual-protocol-design.md`.
- Client and lobby-service target version: `0.6.0-alpha.1`; keep `0.5.5` documented as the stable client.
- Default create mode: `compat_4_5_v1`; opt-in mode: `tail_v1`.
- `compat_4_5_v1` supports LAN Connect `0.3.x-0.6.x`, uses slot/list widths `4/5`, supports 2-8 players, and rejects any RitsuLib presence.
- `tail_v1` requires client capability >=0.6, keeps the STS2 body at slot/list widths `2/3`, carries the authoritative 2-8 player roster in LAN Tail v1, and never uses the legacy `8/3` path.
- `legacy_4p` is fixture-only; production create/join/control requests from `<0.3.0` return `client_update_required` before a room or ticket is allocated.
- Room profile and selected protocol/critical-extension versions are frozen at create time and never renegotiated during the room lifetime.
- RitsuLib coexistence may use only public, versioned APIs. Never reflect private `SerializePatch<T>`, invoke private postfixes, infer owner priority, or unpatch another owner.
- If the Ritsu ordering/cursor/inventory feasibility gate fails, stop the Ritsu task and return to design review. Do not restore the RC4 bridge.
- Tail v1 binary layout, limits, canonical entry ordering, reserved IDs, authority checks, and rejection codes are copied exactly from the approved design.
- Cross-runtime DTO changes must be mirrored between `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyModels.cs` and `lobby-service/src/*.ts`.
- New TypeScript production files must be added to `scripts/package-lobby-service.sh`, `scripts/verify-release.sh`, and `lobby-service/src/package-content.test.ts`.
- Do not edit `releases/` manually; use source and package scripts only.
- Preserve unrelated existing `.omo/`, `.zcode/`, and `tem/` worktree changes; never stage them.
- Run .NET and GdUnit tests with `-m:1`.

## Planned File Structure

### Client protocol domain

- Create `sts2-lan-connect/Scripts/Protocol/LanConnectClientVersion.cs`: SemVer/prerelease parsing and generation classification.
- Create `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolProfile.cs`: strict canonical/legacy API profile parsing.
- Create `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolOffer.cs`: immutable peer capability offer.
- Create `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolSelection.cs`: immutable server-selected room protocol.
- Create `sts2-lan-connect/Scripts/Protocol/LanConnectCreateRoomIntent.cs`: explicit create profile, player limit, and local offer.
- Create `sts2-lan-connect/Scripts/Protocol/LanConnectSessionProtocolState.cs`: tentative/frozen/closing session lease state machine.
- Create `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolFailure.cs`: shared HTTP/Tail rejection model.
- Create `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolException.cs`: fail-closed protocol contract exception.
- Create `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolUiMessages.cs`: Chinese user-facing protocol errors.
- Create `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolFailureMapper.cs`: lossless HTTP/Tail/local failure conversion.
- Create `sts2-lan-connect/Scripts/Protocol/LanConnectHostAttemptResult.cs`: structured host create/publish outcome.

### Client Tail codec

- Create `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectTailEntry.cs`: canonical entry value and UTF-8 ID ordering.
- Create `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectTailEnvelope.cs`: decoded immutable envelope.
- Create `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectTailCodec.cs`: bounded envelope read/write.
- Create `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectCapabilitiesCodec.cs`: peer offer/session selection payloads.
- Create `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectRosterCodec.cs`: authoritative roster payload and vanilla player sub-payloads.
- Create `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectRejectionCodec.cs`: rejection payload and codes 1-10.
- Create `sts2-lan-connect/Scripts/Protocol/LanConnectRosterProjection.cs`: canonical order and vanilla four-player projection.
- Create `sts2-lan-connect/Scripts/Protocol/LanConnectRosterAuthorityState.cs`: host/revision/connection-table validation.

### Client patches and capability integration

- Create `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectProtocolPatchDispatcher.cs`: one atomic Harmony owner and frozen-profile routing.
- Create `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectCompatWirePatches.cs`: isolated historical `4/5` path.
- Create `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs`: ten-message-type matrix, `ClientConnectionFailedMessage` rejection Tail, and restoration.
- Create `sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectExternalCapabilityCollector.cs`: local offer collection.
- Create `sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectRitsuLibPublicApiAdapter.cs`: public-contract-only Ritsu integration.
- Create `sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectCapabilityDigest.cs`: canonical selection digest.

### Lobby service protocol domain

- Create `lobby-service/src/client-version.ts`: client version signal parsing/classification.
- Create `lobby-service/src/protocol-profile.ts`: canonical profile parsing and legacy DTO projection.
- Create `lobby-service/src/protocol-capabilities.ts`: deterministic selection, critical-extension whitelist, digest.
- Create `lobby-service/src/protocol-errors.ts`: typed error codes/details.
- Create `lobby-service/src/room-api-view.ts`: canonical internal room to legacy/canonical API views.

### Fixtures and evidence

- Create `research/prototypes/v0.6-tail-ritsulib/`: tracked test harness source only; no third-party/game DLLs.
- Create `test-fixtures/protocol/v0.6/`: reviewed golden vectors and captured `0.5.5` DTO fixtures.
- Create `docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_FEASIBILITY_ZH.md`: feasibility evidence and gate verdicts.
- Create `docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_ACCEPTANCE_ZH.md`: platform/mode/player-count acceptance matrix.

---

### Task 0: Prove Tail and RitsuLib Feasibility Before Production Changes

**Files:**
- Create: `research/prototypes/v0.6-tail-ritsulib/Sts2TailPrototype.csproj`
- Create: `research/prototypes/v0.6-tail-ritsulib/RitsuInteropMatrixTests.cs`
- Create: `research/prototypes/v0.6-tail-ritsulib/PublicContractInspectionTests.cs`
- Create: `research/prototypes/v0.6-tail-ritsulib/RitsuInteropHarness.cs`
- Create: `research/prototypes/v0.6-tail-ritsulib/PublicRitsuContract.cs`
- Create: `research/prototypes/v0.6-tail-ritsulib/AndroidProtocolProbe.cs`
- Create: `research/prototypes/v0.6-tail-ritsulib/Sts2TailAndroidProbe.csproj`
- Create: `research/prototypes/v0.6-tail-ritsulib/sts2_lan_v06_probe.json`
- Create: `research/prototypes/v0.6-tail-ritsulib/tail-probe-complete-v1.bin`
- Create: `research/prototypes/v0.6-tail-ritsulib/tail-probe-complete-v1.json`
- Create: `research/prototypes/v0.6-tail-ritsulib/verify_android_probe.py`
- Create: `research/prototypes/v0.6-tail-ritsulib/package_tree_hash.py`
- Create: `research/prototypes/v0.6-tail-ritsulib/run_android_probe.sh`
- Create: `research/prototypes/v0.6-tail-ritsulib/README.md`
- Create: `docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_FEASIBILITY_ZH.md`

**Interfaces:**
- Consumes: real target-version `sts2.dll`, `0Harmony.dll`, and a real built/released `STS2-RitsuLib.dll`, supplied through MSBuild properties.
- Produces: a documented `PASS` or `BLOCKED` verdict for ordering, cursor ownership, four installation combinations, complete extension enumeration/classification, Android dynamic-generic behavior, and the existing Ritsu sidecar bridge.

- [ ] **Step 1: Create a property-driven prototype project that never packages third-party DLLs**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
    <Reference Include="sts2" HintPath="$(Sts2DataDir)/sts2.dll" Private="true" />
    <Reference Include="0Harmony" HintPath="$(Sts2DataDir)/0Harmony.dll" Private="true" />
    <Reference Include="STS2-RitsuLib" HintPath="$(RitsuLibAssembly)" Private="true" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the four-combination red tests around raw bytes and cursor positions**

```csharp
[Theory]
[InlineData(false, false)]
[InlineData(true, false)]
[InlineData(false, true)]
[InlineData(true, true)]
public void Lan_tail_bytes_and_cursor_are_stable(bool senderHasRitsu, bool receiverHasRitsu)
{
    InteropResult result = RitsuInteropHarness.RoundTrip(senderHasRitsu, receiverHasRitsu);
    Assert.Equal(InteropFixtures.ExpectedLanTail, result.LanTailBytes);
    Assert.Equal(result.LanTailEndBit, result.RitsuReadStartBit);
    Assert.Equal(InteropFixtures.ExpectedMessage, result.Message);
}
```

Define the prototype-only contracts in `RitsuInteropHarness.cs`:

```csharp
internal sealed record InteropResult(
    byte[] LanTailBytes,
    long LanTailEndBit,
    long RitsuReadStartBit,
    string Message);

internal static class InteropFixtures
{
    // A complete valid Tail v1 container, not just the magic. The byte table
    // and every offset are independently reviewed in the adjacent fixture JSON.
    internal static readonly byte[] ExpectedLanTail = FixtureFiles.ReadBytes(
        "tail-probe-complete-v1.bin");
    internal const string ExpectedMessage = "round-trip-ok";
}

internal static class RitsuInteropHarness
{
    internal static InteropResult RoundTrip(bool senderHasRitsu, bool receiverHasRitsu)
    {
        throw new NotSupportedException(
            "The feasibility harness must be completed against the real public RitsuLib contract.");
    }
}
```

The intentional `NotSupportedException` is the initial red state. Replace it only with calls available on public RitsuLib types discovered by `PublicContractInspectionTests`; do not use non-public binding flags.

Freeze `tail-probe-complete-v1.bin` to exactly these 36 bytes:

```text
53 54 53 4c 41 4e 30 31
01 00 00 00 00 1c 00 01 00 01
09 6c 61 6e 2e 70 72 6f 62 65
00 01 01 00 00 00 01 01
```

This is `STSLAN01`, container v1, flags 0, 28-byte container body, session protocol 1, one critical `lan.probe` v1 entry with payload `01`. Total length is 36 bytes, LAN end/Ritsu start are both bit 288, and SHA-256 is `cfc9097350801026775fd333fb19c6758becbffd4142f58bab0884f4231f5cfa`. The adjacent JSON fixes every field offset/value. Neither file may be generated by the prototype codec.

- [ ] **Step 3: Run the prototype and verify that it initially fails until a public ordering contract is found**

Run:

```bash
test -f "$RITSULIB_ASSEMBLY"
test -f "/Users/mac/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64/sts2.dll"
test -f "/Users/mac/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64/0Harmony.dll"
file "$RITSULIB_ASSEMBLY"

DOTNET_ROOT="/Users/mac/.dotnet" PATH="/Users/mac/.dotnet:$PATH" \
/Users/mac/.dotnet/dotnet test \
  "research/prototypes/v0.6-tail-ritsulib/Sts2TailPrototype.csproj" \
  -m:1 \
  -p:Sts2DataDir="/Users/mac/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64" \
  -p:RitsuLibAssembly="$RITSULIB_ASSEMBLY"
```

Expected: at least one ordering/public-contract assertion fails until the real API proves `vanilla -> LAN -> optional Ritsu` and cursor handoff without private patch access.

- [ ] **Step 4: Inspect only public RitsuLib types and assert complete inventory/classification support**

```csharp
internal sealed record PublicRitsuContract(
    bool CanEnumerateAllNetworkExtensions,
    bool CanClassifyCriticality,
    bool CanCoordinateTailOrder,
    IReadOnlyList<string> ReferencedMembers)
{
    internal static PublicRitsuContract Load(Assembly assembly)
    {
        Type[] publicTypes = assembly.GetExportedTypes();
        string[] publicMembers = publicTypes
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(member => $"{member.DeclaringType?.FullName}.{member.Name}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return PublicContractRules.Evaluate(publicTypes, publicMembers);
    }
}

internal static class PublicContractRules
{
    internal static PublicRitsuContract Evaluate(Type[] publicTypes, IReadOnlyList<string> publicMembers)
    {
        ArgumentNullException.ThrowIfNull(publicTypes);
        ArgumentNullException.ThrowIfNull(publicMembers);
        return new PublicRitsuContract(
            CanEnumerateAllNetworkExtensions: false,
            CanClassifyCriticality: false,
            CanCoordinateTailOrder: false,
            ReferencedMembers: publicMembers);
    }
}

[Fact]
public void Public_api_exposes_complete_network_extension_inventory()
{
    PublicRitsuContract contract = PublicRitsuContract.Load(typeof(RitsuLibFramework).Assembly);
    Assert.True(contract.CanEnumerateAllNetworkExtensions);
    Assert.True(contract.CanClassifyCriticality);
    Assert.True(contract.CanCoordinateTailOrder);
    Assert.DoesNotContain(contract.ReferencedMembers, name => name.Contains("SerializePatch", StringComparison.Ordinal));
}
```

The all-false implementation is the initial red state. Change `PublicContractRules.Evaluate` to return `true` only when exported API members explicitly provide complete enumeration, critical classification, and Tail-order/cursor coordination. Naming similarity is not evidence: each accepted member must be invoked by another prototype test against a real registration.

- [ ] **Step 5: Add a game-loadable Android probe entrypoint**

Because the repository has no Android xUnit export runner, add a separate `Sts2TailAndroidProbe.csproj` test-only MOD assembly with no xUnit/Test SDK dependency. It references the Android STS2/Harmony assemblies with `Private=false`; RitsuLib is optional and loaded by the game MOD loader. Add this exact staging target:

```xml
<Target Name="StageAndroidProbe" AfterTargets="Build" Condition="'$(BuildAndroidProbe)' == 'true'">
  <PropertyGroup>
    <AndroidProbeOutput>$(MSBuildProjectDirectory)/artifacts/android-probe</AndroidProbeOutput>
  </PropertyGroup>
  <RemoveDir Directories="$(AndroidProbeOutput)" />
  <MakeDir Directories="$(AndroidProbeOutput)" />
  <Copy SourceFiles="$(TargetPath);$(MSBuildProjectDirectory)/sts2_lan_v06_probe.json"
        DestinationFolder="$(AndroidProbeOutput)" />
</Target>
```

With `BuildAndroidProbe=true`, assert `artifacts/android-probe/` contains exactly the probe DLL and manifest; it must not contain STS2, Harmony, RitsuLib, xUnit, or test-adapter DLLs.

The checked-in probe manifest is:

```json
{
  "id": "sts2_lan_v06_probe",
  "name": "STS2 LAN v0.6 Protocol Probe",
  "author": "STS2 LAN Connect",
  "version": "0.0.0-probe",
  "description": "Test-only Android protocol feasibility probe.",
  "has_dll": true,
  "has_pck": false,
  "affects_gameplay": false
}
```

The probe DLL contains `[ModInitializer(nameof(Init))]`; the loader discovers the initializer from the DLL, not from a manifest `entrypoint`. The probe reads `sts2_lan_v06_probe_runtime.json` beside its DLL. In encode mode it writes one Android-runtime sender fixture. In decode mode it reads both sender fixtures. Fixed marker schemas:

```text
encode: {"phase":"encode","ritsuPresent":false,"passed":true,"sts2Version":"...","fixtureSha256":"...","fixtureLength":36,"lanTailSha256":"cfc909...","lanEndBit":288,"ritsuStartBit":288,"containsOpenGeneric":false,"invalidProgram":false,"ritsuManifestId":null,"ritsuManifestVersion":null,"ritsuSelectedAssembly":null,"ritsuPatchOwners":[]}

decode: {"phase":"decode","ritsuPresent":true,"passed":true,"sts2Version":"...","containsOpenGeneric":false,"invalidProgram":false,"ritsuManifestId":"STS2-RitsuLib","ritsuManifestVersion":"...","ritsuSelectedAssembly":"lib/<api>/STS2-RitsuLib.dll","ritsuPatchOwners":["..."],"results":[{"senderRitsu":false,"messageOk":true,"lanTailSha256":"cfc909...","lanEndBit":288,"ritsuStartBit":288},{"senderRitsu":true,"messageOk":true,"lanTailSha256":"cfc909...","lanEndBit":288,"ritsuStartBit":288}]}
```

Prefix each JSON with `STS2_LAN_V06_ANDROID_PROBE `. Two encode markers plus two decode markers yield four distinct sender/receiver combinations. The verifier checks every required field/type and uniqueness.

On exception it must emit `passed:false`, exception type/message/inner type, and exit initialization without applying production patches.

- [ ] **Step 6: Build, install, launch, and collect the Android probe on the existing MuMu test device**

Build the probe using the Android STS2 managed assembly directory extracted from the tested installation:

```bash
test -n "$ANDROID_STS2_DATA_DIR"
test -f "$ANDROID_STS2_DATA_DIR/sts2.dll"
test -f "$ANDROID_STS2_DATA_DIR/0Harmony.dll"
test -f "$RITSULIB_ASSEMBLY"

DOTNET_ROOT="/Users/mac/.dotnet" PATH="/Users/mac/.dotnet:$PATH" \
/Users/mac/.dotnet/dotnet build \
  research/prototypes/v0.6-tail-ritsulib/Sts2TailAndroidProbe.csproj \
  -p:Sts2DataDir="$ANDROID_STS2_DATA_DIR" \
  -p:BuildAndroidProbe=true
```

`run_android_probe.sh` requires and validates these environment variables:

```text
ADB_BIN
ANDROID_SERIAL
ANDROID_STS2_PACKAGE
ANDROID_MOD_DIR
ANDROID_STS2_LOG_PATH
ANDROID_STS2_LOG
ANDROID_PROBE_DLL
ANDROID_PROBE_MANIFEST
ANDROID_RITSULIB_PACKAGE_DIR
RITSULIB_PACKAGE_TREE_SHA256
ANDROID_PROBE_FIXTURE_PATH
ANDROID_PROBE_INPUT_DIR
ANDROID_PROBE_RUNTIME_CONFIG
```

Its executable flow is:

```bash
run_case() {
  case_name="$1"
  ritsu_enabled="$2"
  mode="$3"
  "$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_PROBE_DLL" "$ANDROID_MOD_DIR/"
  "$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_PROBE_MANIFEST" "$ANDROID_MOD_DIR/"
  if [ "$ritsu_enabled" = true ]; then
    test -d "$ANDROID_RITSULIB_PACKAGE_DIR"
    test -f "$ANDROID_RITSULIB_PACKAGE_DIR/mod_manifest.json"
    test -f "$ANDROID_RITSULIB_PACKAGE_DIR/STS2-RitsuLib.dll"
    test "$(python3 research/prototypes/v0.6-tail-ritsulib/package_tree_hash.py "$ANDROID_RITSULIB_PACKAGE_DIR")" = "$RITSULIB_PACKAGE_TREE_SHA256"
    "$ADB_BIN" -s "$ANDROID_SERIAL" shell rm -rf "$ANDROID_MOD_DIR/STS2-RitsuLib"
    "$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_RITSULIB_PACKAGE_DIR" "$ANDROID_MOD_DIR/STS2-RitsuLib"
  else
    "$ADB_BIN" -s "$ANDROID_SERIAL" shell rm -rf "$ANDROID_MOD_DIR/STS2-RitsuLib"
  fi
  python3 -c \
    'import json,sys; json.dump({"mode":sys.argv[1],"fixturePath":sys.argv[2],"inputDir":sys.argv[3]}, open(sys.argv[4], "w"), separators=(",", ":"))' \
    "$mode" "$ANDROID_PROBE_FIXTURE_PATH" "$ANDROID_PROBE_INPUT_DIR" "$ANDROID_PROBE_RUNTIME_CONFIG"
  "$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_PROBE_RUNTIME_CONFIG" "$ANDROID_MOD_DIR/sts2_lan_v06_probe_runtime.json"
  "$ADB_BIN" -s "$ANDROID_SERIAL" shell am force-stop "$ANDROID_STS2_PACKAGE"
  "$ADB_BIN" -s "$ANDROID_SERIAL" logcat -c
  "$ADB_BIN" -s "$ANDROID_SERIAL" shell monkey -p "$ANDROID_STS2_PACKAGE" -c android.intent.category.LAUNCHER 1
  sleep 30
  "$ADB_BIN" -s "$ANDROID_SERIAL" pull "$ANDROID_STS2_LOG_PATH" "$ANDROID_STS2_LOG.$case_name"
}

# Produce both sender fixtures in real Android game processes.
run_case encode-without-ritsu false encode
"$ADB_BIN" -s "$ANDROID_SERIAL" pull "$ANDROID_PROBE_FIXTURE_PATH" "$ANDROID_STS2_LOG.sender-without.bin"
run_case encode-with-ritsu true encode
"$ADB_BIN" -s "$ANDROID_SERIAL" pull "$ANDROID_PROBE_FIXTURE_PATH" "$ANDROID_STS2_LOG.sender-with.bin"

# Decode both fixtures in each real receiver configuration.
"$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_STS2_LOG.sender-without.bin" "$ANDROID_MOD_DIR/sender-without.bin"
"$ADB_BIN" -s "$ANDROID_SERIAL" push "$ANDROID_STS2_LOG.sender-with.bin" "$ANDROID_MOD_DIR/sender-with.bin"
run_case decode-without-ritsu false decode
run_case decode-with-ritsu true decode

python3 research/prototypes/v0.6-tail-ritsulib/verify_android_probe.py \
  "$ANDROID_STS2_LOG.encode-without-ritsu" \
  "$ANDROID_STS2_LOG.encode-with-ritsu" \
  "$ANDROID_STS2_LOG.decode-without-ritsu" \
  "$ANDROID_STS2_LOG.decode-with-ritsu"
```

If the MOD directory or game log requires emulator root, the configured `ADB_BIN`/MuMu bridge must already expose those paths; inability to push/pull is a `BLOCKED` Android gate, not permission to skip it. Record the concrete environment values, STS2 package/build, ABI, Ritsu assembly version, and script output in feasibility evidence.

The four separate game processes prove real Android sender and receiver behavior with Ritsu absent and present. Verify with a deterministic local parser:

```bash
test -f "$ANDROID_STS2_LOG.encode-without-ritsu"
test -f "$ANDROID_STS2_LOG.encode-with-ritsu"
test -f "$ANDROID_STS2_LOG.decode-without-ritsu"
test -f "$ANDROID_STS2_LOG.decode-with-ritsu"
python3 research/prototypes/v0.6-tail-ritsulib/verify_android_probe.py \
  "$ANDROID_STS2_LOG.encode-without-ritsu" \
  "$ANDROID_STS2_LOG.encode-with-ritsu" \
  "$ANDROID_STS2_LOG.decode-without-ritsu" \
  "$ANDROID_STS2_LOG.decode-with-ritsu"
```

`verify_android_probe.py` exits 0 only when exactly one terminal marker exists per process, Ritsu presence matches each process, both sender fixtures are complete valid Tail containers, four distinct sender/receiver results exist, and every required Boolean is true. It exits nonzero for missing/duplicate markers, wrong STS2/Ritsu versions, fewer than four combinations, open generics, cursor drift, or any exception.

Before accepting each `with-ritsu` marker, require it to report the loaded Ritsu manifest ID/version and at least one expected Ritsu Harmony owner/target. A copied but uninitialized DLL is `BLOCKED`, not “Ritsu present.”

`ANDROID_RITSULIB_PACKAGE_DIR` must be an unpacked official matching release asset or variant-pack `mods/STS2-RitsuLib/` directory, copied recursively without flattening. `package_tree_hash.py` hashes sorted relative paths plus every file SHA-256; compare it with `RITSULIB_PACKAGE_TREE_SHA256` recorded from the separately downloaded release asset. Preserve `mod_manifest.json`, root loader, `lib/<api-version>/`, variant manifest, and dependencies. The marker must report manifest ID/version, selected API assembly path/version, and at least one expected Ritsu Harmony owner/target; otherwise the gate is `BLOCKED`.

- [ ] **Step 7: Record Android reflection and Harmony evidence**

The probe payload/evidence must include:

```text
IsGenericMethod
IsGenericMethodDefinition
ContainsGenericParameters
closed target FullDescription
Harmony owner/target count
LAN start/end bit positions
Ritsu start/end bit positions
exception and inner exception, if any
```

Expected: no `InvalidProgramException`, no open generic target passed to Harmony, and identical LAN bytes/cursors.

- [ ] **Step 8: Evaluate the existing sidecar bridge independently**

Read and test the public methods currently used by `LanConnectRitsuLibLobbyCompatibility`. Record whether its NetService/handshake behavior can be preserved through public APIs without transpiling RitsuLib methods. Mark the sidecar gate separately from message-tail coexistence.

- [ ] **Step 9: Write the gate verdict**

The evidence document must contain this exact decision table:

```markdown
| Gate | Verdict | Evidence |
|---|---|---|
| Tail order/cursor | PASS/BLOCKED | command + raw offsets |
| Four install combinations | PASS/BLOCKED | test cases |
| Complete extension inventory | PASS/BLOCKED | public API members |
| Critical classification | PASS/BLOCKED | public API members |
| Android generic/Harmony | PASS/BLOCKED | device/build/log |
| Sidecar public replacement | PASS/BLOCKED | API + runtime result |
```

If any of the first five gates is `BLOCKED`, explicitly state: “RitsuLib free mixing is removed from the executable alpha scope pending design review; the RC4 private bridge remains prohibited.”

- [ ] **Step 10: Commit prototype source and evidence only**

```bash
git add research/prototypes/v0.6-tail-ritsulib/Sts2TailPrototype.csproj research/prototypes/v0.6-tail-ritsulib/RitsuInteropMatrixTests.cs research/prototypes/v0.6-tail-ritsulib/PublicContractInspectionTests.cs research/prototypes/v0.6-tail-ritsulib/RitsuInteropHarness.cs research/prototypes/v0.6-tail-ritsulib/PublicRitsuContract.cs research/prototypes/v0.6-tail-ritsulib/AndroidProtocolProbe.cs research/prototypes/v0.6-tail-ritsulib/Sts2TailAndroidProbe.csproj research/prototypes/v0.6-tail-ritsulib/sts2_lan_v06_probe.json research/prototypes/v0.6-tail-ritsulib/tail-probe-complete-v1.bin research/prototypes/v0.6-tail-ritsulib/tail-probe-complete-v1.json research/prototypes/v0.6-tail-ritsulib/verify_android_probe.py research/prototypes/v0.6-tail-ritsulib/package_tree_hash.py research/prototypes/v0.6-tail-ritsulib/run_android_probe.sh research/prototypes/v0.6-tail-ritsulib/README.md docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_FEASIBILITY_ZH.md
git commit -m "test: establish v0.6 protocol feasibility gates"
```

Do not stage real DLLs, `bin/`, `obj/`, logs containing user paths, `tem/`, or unrelated files.

---

### Task 1: Capture and Freeze Real 0.5.5 API Compatibility Fixtures

**Files:**
- Create: `test-fixtures/protocol/v0.6/v0.5.5-create-request.json`
- Create: `test-fixtures/protocol/v0.6/v0.5.5-create-response.json`
- Create: `test-fixtures/protocol/v0.6/v0.5.5-room-list-response.json`
- Create: `test-fixtures/protocol/v0.6/v0.5.5-join-request.json`
- Create: `test-fixtures/protocol/v0.6/v0.5.5-join-response.json`
- Create: `test-fixtures/protocol/v0.6/v0.5.5-host-control.json`
- Create: `test-fixtures/protocol/v0.6/v0.5.5-client-control.json`
- Create: `test-fixtures/protocol/v0.6/compat-v0.3.x-join-response.bin`
- Create: `test-fixtures/protocol/v0.6/compat-v0.3.x-player-joined.bin`
- Create: `test-fixtures/protocol/v0.6/compat-v0.3.x-begin-run.bin`
- Create: `test-fixtures/protocol/v0.6/compat-v0.5.5-join-response.bin`
- Create: `test-fixtures/protocol/v0.6/compat-v0.5.5-player-joined.bin`
- Create: `test-fixtures/protocol/v0.6/compat-v0.5.5-begin-run.bin`
- Create: `test-fixtures/protocol/v0.6/README.md`
- Modify: `sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj:38-42`
- Test: `sts2-lan-connect.Tests/Protocol/LanConnectCompatWireFixtureTests.cs`
- Test: `lobby-service/src/protocol-compatibility.integration.test.ts`

**Interfaces:**
- Consumes: unmodified released `0.5.5` and one actually released `0.3.x` client plus the current compatible service/game runtime.
- Produces: reviewed JSON/WS fixtures for DTO projection and raw join-response/player-joined/begin-run `4/5-bit` fixtures proving the supported client generations share the same game wire.

- [ ] **Step 1: Capture create/list/join/control traffic from a real 0.5.5 client**

Capture exact request/response JSON after redacting tokens, IP addresses, passwords, installation IDs, and player names. Replace volatile values with stable literals such as `room-fixture`, `ticket-fixture`, and `2026-01-01T00:00:00.000Z`; do not alter field presence or enum strings.

- [ ] **Step 2: Capture raw game messages from released 0.3.x and 0.5.5 clients**

Use the same two-player lobby state and capture serialized `ClientLobbyJoinResponseMessage`, `PlayerJoinedMessage`, and `LobbyBeginRunMessage` bytes before transport encryption/framing. Record exact client/game versions and annotate message header/body offsets. Confirm both generations encode slot/list widths `4/5`; if bytes differ outside expected volatile IDs/seed fields, stop and revise the claimed compat generation before production changes.

- [ ] **Step 3: Add the fixture directory to the xUnit output**

```xml
<None Include="..\test-fixtures\protocol\v0.6\*"
      Link="TestFixtures\ProtocolV06\%(Filename)%(Extension)"
      CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 4: Write a fixture test that documents the captured API and wire contracts**

```ts
test("0.5.5 fixtures use extended_8p and omit v0.6 capability fields", async () => {
  const create = await readFixture("v0.5.5-create-request.json");
  assert.equal(create.modVersion, "0.5.5");
  assert.equal(create.protocolProfile, "extended_8p");
  assert.equal(create.protocolProfileV2, undefined);
  assert.equal(create.protocolCapabilities, undefined);
});
```

Add a C# fixture test that parses the annotated message vectors with explicit `4/5` widths and rejects parsing with `8/3` or `2/3`.

- [ ] **Step 5: Run the focused fixture tests**

```bash
cd lobby-service
npm run build
node --test dist/protocol-compatibility.integration.test.js

cd ..
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj \
  --filter "FullyQualifiedName~LanConnectCompatWireFixtureTests" -m:1
```

Expected: PASS for captured field shape; if capture contradicts the design’s assumed version signal/profile shape, stop and update the design before Task 2.

- [ ] **Step 6: Commit the independently captured contract**

```bash
git add test-fixtures/protocol/v0.6/v0.5.5-create-request.json test-fixtures/protocol/v0.6/v0.5.5-create-response.json test-fixtures/protocol/v0.6/v0.5.5-room-list-response.json test-fixtures/protocol/v0.6/v0.5.5-join-request.json test-fixtures/protocol/v0.6/v0.5.5-join-response.json test-fixtures/protocol/v0.6/v0.5.5-host-control.json test-fixtures/protocol/v0.6/v0.5.5-client-control.json test-fixtures/protocol/v0.6/compat-v0.3.x-join-response.bin test-fixtures/protocol/v0.6/compat-v0.3.x-player-joined.bin test-fixtures/protocol/v0.6/compat-v0.3.x-begin-run.bin test-fixtures/protocol/v0.6/compat-v0.5.5-join-response.bin test-fixtures/protocol/v0.6/compat-v0.5.5-player-joined.bin test-fixtures/protocol/v0.6/compat-v0.5.5-begin-run.bin test-fixtures/protocol/v0.6/README.md sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj sts2-lan-connect.Tests/Protocol/LanConnectCompatWireFixtureTests.cs lobby-service/src/protocol-compatibility.integration.test.ts
git commit -m "test: capture v0.5.5 lobby protocol fixtures"
```

---

### Task 2: Add Canonical Service Profiles, Versions, Selection, and API Views

**Files:**
- Create: `lobby-service/src/client-version.ts`
- Create: `lobby-service/src/client-version.test.ts`
- Create: `lobby-service/src/protocol-profile.ts`
- Create: `lobby-service/src/protocol-profile.test.ts`
- Create: `lobby-service/src/protocol-capabilities.ts`
- Create: `lobby-service/src/protocol-capabilities.test.ts`
- Create: `lobby-service/src/protocol-errors.ts`
- Create: `lobby-service/src/room-api-view.ts`
- Create: `lobby-service/src/room-api-view.test.ts`
- Create: `test-fixtures/protocol/v0.6/capability-digest-v1.json`
- Modify: `lobby-service/src/store.ts:9-16,66-104,172-206,239-287,328-598,1110-1137,1243-1325`
- Modify: `lobby-service/src/app.ts:473-493,577-585,593-768,1301-1383,2814-2849`
- Modify: `lobby-service/src/store.test.ts:208-306`
- Modify: `lobby-service/src/app.integration.test.ts`
- Modify: `scripts/package-lobby-service.sh`
- Modify: `scripts/verify-release.sh`
- Modify: `lobby-service/src/package-content.test.ts`

**Interfaces:**
- Consumes: Task 1’s real `0.5.5` fixtures.
- Produces:
  - `classifyClientVersion(clientVersion?: string, modVersion?: string): ClientApiGeneration`
  - `parseRequestedProfile(input): ProtocolProfile`
  - `selectRoomProtocol(hostOffer, verifiedExtensions): RoomProtocolSelection`
  - `validateJoinOffer(selection, joinerOffer): void`
  - `toRoomApiView(room, generation): RoomApiView`
  - immutable `RoomProtocolSelection` and `capabilityDigest` stored on each room and ticket.
  - `/probe.capabilities.dualProtocolApiVersion = 1` plus supported profile and version fields.

- [ ] **Step 1: Write failing client-version tests**

```ts
test("classifies supported generations and rejects 0.2", () => {
  assert.equal(classifyClientVersion(undefined, "0.5.5"), "compat_0_3_0_5");
  assert.equal(classifyClientVersion("0.6.0-alpha.1", "0.6.0-alpha.1"), "canonical_0_6_plus");
  assert.throws(() => classifyClientVersion(undefined, "0.2.2"), hasCode("client_update_required"));
  assert.throws(() => classifyClientVersion(undefined, "garbage"), hasCode("client_update_required"));
});
```

- [ ] **Step 2: Run the test and verify red**

```bash
cd lobby-service && npm run build && node --test dist/client-version.test.js
```

Expected: FAIL because `client-version.ts` does not exist.

- [ ] **Step 3: Implement strict SemVer generation classification**

Use a small internal parser that accepts `major.minor.patch` plus an optional `-prerelease` suffix, compares only numeric core for generation boundaries, and preserves the canonical original string. Reject missing/mismatched 0.6 `clientVersion` and `modVersion` rather than extracting arbitrary embedded digits.

- [ ] **Step 4: Write failing profile migration tests**

```ts
test("keeps legacy strings at the API boundary only", () => {
  assert.equal(parseRequestedProfile({ generation: "compat_0_3_0_5", legacyProfile: "extended_8p" }), "compat_4_5_v1");
  assert.equal(projectProfile("compat_4_5_v1", "compat_0_3_0_5"), "extended_8p");
  assert.throws(() => parseRequestedProfile({ generation: "legacy_0_2", legacyProfile: "legacy_4p" }), hasCode("client_update_required"));
  assert.throws(() => parseRequestedProfile({ generation: "canonical_0_6_plus", canonicalProfile: "unknown" }), hasCode("protocol_profile_unsupported"));
});
```

- [ ] **Step 5: Implement canonical profile parsing without fallback**

Define only:

```ts
export type ProtocolProfile = "compat_4_5_v1" | "tail_v1";
export type LegacyProtocolProfile = "legacy_4p" | "extended_8p";
```

`legacy_4p` must never be returned as a runtime profile.

- [ ] **Step 6: Write selection and critical-extension tests**

```ts
test("selects the highest server-verified common versions deterministically", () => {
  const result = selectRoomProtocol(
    offer({ lan: [1, 2], critical: [{ id: "run.data", min: 1, max: 3 }] }),
    whitelist({ lan: [1, 1], critical: [{ id: "run.data", min: 1, max: 2 }] }),
  );
  assert.equal(result.selectedLanProtocolVersion, 1);
  assert.deepEqual(result.criticalExtensions, [{ id: "run.data", selectedVersion: 2 }]);
  assert.match(result.capabilityDigest, /^[0-9a-f]{64}$/);
});
```

Also test duplicate IDs, unknown critical IDs, empty intersections, canonical UTF-8 ordering, extra/missing joiner critical extensions, and compat+Ritsu rejection.

- [ ] **Step 7: Implement immutable selection and digest**

For `compat_4_5_v1`, selection is protocol version `0`, minimum client `0.3.0`, no Ritsu, and no critical extensions. For `tail_v1`, select LAN version 1 and the server-verified critical versions. Encode canonical digest bytes exactly as design §4.3 (`LANSEL01`, schema/profile/version/max players, length-prefixed normalized versions/signature, sorted extensions in network byte order), then SHA-256 lowercase hex. Add reviewed empty-extension and multibyte-ID cases to `capability-digest-v1.json`; both TypeScript and Task 3 C# tests must consume the same fixture.

- [ ] **Step 8: Refactor store internal types to canonical state**

Add `clientVersion`, `clientApiGeneration`, and `protocolSelection` to `Room`; copy generation, profile, selection, and joiner digest into `JoinTicket`. Move profile/version/capability validation before room/ticket ID allocation. Remove `resolveRoomProtocolProfile()` from `toRoomSummary()`; summaries copy frozen state only.

- [ ] **Step 9: Split public list/create/join views from store state**

The unversioned `/rooms` response keeps legacy `protocolProfile` for old clients and adds optional `protocolProfileV2` plus `protocolCapabilities` for 0.6. Create/join responses use the caller generation: old clients receive `extended_8p`; 0.6 receives canonical fields.

- [ ] **Step 10: Add app parsers and enforce ticket-before-control invariants**

Parse `clientVersion`, `protocolProfileV2`, and bounded capability offers explicitly. Old control URLs continue to work for compat rooms because generation is read from room/ticket. Tail control connections must present matching `clientVersion` and capability digest; mismatches are rejected before binding.

- [ ] **Step 11: Advertise the exact dual-protocol API on `/probe`**

Return optional fields under `capabilities`:

```json
{
  "dualProtocolApiVersion": 1,
  "supportedProtocolProfiles": ["compat_4_5_v1", "tail_v1"],
  "lanProtocolMin": 1,
  "lanProtocolMax": 1,
  "minimumClientVersion": "0.3.0",
  "tailV1MinimumClientVersion": "0.6.0-alpha.1"
}
```

Add integration assertions that old capability fields are unchanged and these values are exact.

- [ ] **Step 12: Document deployment restart as the legacy-room cleanup mechanism**

Rooms are in-memory. Require a service restart during 0.6 deployment and test that a new store starts without imported `legacy_4p` state. Do not add persistence or a speculative migration loader.

- [ ] **Step 13: Run focused and full service tests**

```bash
cd lobby-service
npm run build
node --test \
  dist/client-version.test.js \
  dist/protocol-profile.test.js \
  dist/protocol-capabilities.test.js \
  dist/room-api-view.test.js \
  dist/store.test.js \
  dist/app.integration.test.js \
  dist/protocol-compatibility.integration.test.js
npm run check
npm run test
```

Expected: all pass; no request from `<0.3` creates a room, ticket, or control binding.

- [ ] **Step 14: Update service package allowlists and commit**

```bash
git add lobby-service/src/client-version.ts lobby-service/src/client-version.test.ts lobby-service/src/protocol-profile.ts lobby-service/src/protocol-profile.test.ts lobby-service/src/protocol-capabilities.ts lobby-service/src/protocol-capabilities.test.ts lobby-service/src/protocol-errors.ts lobby-service/src/room-api-view.ts lobby-service/src/room-api-view.test.ts lobby-service/src/store.ts lobby-service/src/store.test.ts lobby-service/src/app.ts lobby-service/src/app.integration.test.ts test-fixtures/protocol/v0.6/capability-digest-v1.json scripts/package-lobby-service.sh scripts/verify-release.sh lobby-service/src/package-content.test.ts
git commit -m "feat(lobby): add v0.6 protocol selection and gates"
```

---

### Task 3: Add Client Profiles, Offers, Selections, and Session Freeze

**Files:**
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectClientVersion.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolProfile.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolOffer.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolSelection.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectCreateRoomIntent.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectSessionProtocolState.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolException.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolFailure.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolFailureMapper.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectHostAttemptResult.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolUiMessages.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectJoinRetryPolicy.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectCapabilityDigest.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectExternalCapabilityCollector.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectClientVersionTests.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectProtocolProfileTests.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectSessionProtocolStateTests.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectCapabilityDigestTests.cs`
- Modify: `sts2-lan-connect/Scripts/LanConnectConstants.cs:7-26`
- Modify: `sts2-lan-connect/Scripts/LanConnectProtocolProfiles.cs:8-158`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyModels.cs:7-38,123-168,201-268,340-353`
- Modify: `sts2-lan-connect/Scripts/LanConnectHostFlow.cs:109-315`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:3712-3790`
- Modify: `sts2-lan-connect/Scripts/Lobby/ModSync/LanConnectModPreflightCoordinator.cs:37-205`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyJoinFlow.cs:38-120`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectDirectJoinFlow.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyRuntime.cs:478-527,942-1207,1343-1442`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectMultiplayerSaveCompatibility.cs:108-148`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectContinueRunLobbyAutoPublisher.cs:236-282`
- Modify: `sts2-lan-connect/Scripts/LanConnectLobbyCapacityPatches.cs:17-100`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectHostedRoomMetadata.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectMultiplayerSaveRoomBinding.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/ModSync/LanConnectModPreflightCoordinator.cs:14-63,94-248`
- Modify: `sts2-lan-connect/Scripts/Patches.JoinFriendScreen.cs:215-243`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyApiClient.cs:220-237,303-317`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectProtocolFailurePropagationTests.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectJoinRetryPolicyTests.cs`

**Interfaces:**
- Consumes: Task 2’s API fields and selection digest.
- Produces:
  - `LanConnectProtocolProfile.ParseCanonical(string)` and `ParseApiProjection(...)`
  - `LanConnectProtocolOffer CreateCurrent(...)`
  - `LanConnectProtocolSelection ValidateServerSelection(...)`
  - `LanConnectCreateRoomIntent(LanConnectProtocolProfile Profile, int MaxPlayers, LanConnectProtocolOffer Offer)`
   - `LanConnectSessionProtocolLease FreezeHost/FreezeClient(...)`
   - read-only `LanConnectSessionProtocolSnapshot Current` consumed by serialization patches.
   - `LanConnectProtocolFailureMapper.FromService/FromTail/FromLocal(...)`
   - `LanConnectHostAttemptResult(bool Succeeded, string? FailureMessage, LanConnectProtocolFailure? ProtocolFailure)`
   - `LobbyJoinAttemptResult(LobbyJoinAttemptKind Kind, string? FailureMessage, LanConnectProtocolFailure? ProtocolFailure)` used by lobby and direct-IP joins.

- [ ] **Step 1: Write failing SemVer/profile tests**

```csharp
[Theory]
[InlineData("0.2.2", false)]
[InlineData("0.5.5", true)]
[InlineData("0.6.0-alpha.1", true)]
public void Parses_supported_client_generations(string value, bool supported)
{
    Assert.Equal(supported, LanConnectClientVersion.TryParseSupported(value, out _));
}

[Fact]
public void Unknown_profile_never_falls_back()
{
    Assert.Throws<LanConnectProtocolException>(() => LanConnectProtocolProfile.ParseCanonical("unknown"));
}
```

Before changing each public return type, add red caller-level tests proving that the create overlay, continue-run publisher, lobby overlay, direct-join friend screen, mod-preflight consumer, and restart rejoin each consume `ProtocolFailure` before fallback text. Assert one `LanConnectProtocolUiMessages` presentation, no generic internal-error popup, no next candidate/poll for protocol failure, and unchanged cancellation behavior. Only then migrate the signatures and callers.

- [ ] **Step 2: Run focused tests and verify red**

```bash
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj \
  --filter "FullyQualifiedName~LanConnectClientVersionTests|FullyQualifiedName~LanConnectProtocolProfileTests" -m:1
```

- [ ] **Step 3: Implement immutable profile/version/offer/selection values**

Use records and read-only collections. Compat has LAN protocol `0`; Tail v1 has selected protocol `1`. Include profile, max players 2-8, minimum client, game version, wire-cache signature, sorted critical extensions, and digest.

Define `LanConnectProtocolException` as an unretryable exception carrying a required non-null `LanConnectProtocolFailure Failure`. Define `LanConnectProtocolFailureMapper` as the only conversion seam for HTTP `LobbyServiceException`, Tail rejection codes 1-10, and local selection/codec failures; unknown codes retain their raw code. Change `LobbyJoinAttemptResult` to carry `ProtocolFailure`, add `LanConnectHostAttemptResult`, and add `ProtocolFailure` to `LanConnectModPreflightJoinResult`. `StartLobbyHostAsync`, `PublishExistingHostToLobbyAsync`, lobby join, and direct-IP join return these values instead of erasing them into `bool`/text. Lobby/direct join catches `LanConnectProtocolException` before candidate/broad catches, aborts all remaining candidates, preserves `Failure`, disconnects any current transport, and releases the tentative lease. Auto-rejoin clears `_pendingClientReconnect` and presents one structured message on protocol failure; it continues polling only for room-not-found or retryable transport failures. Cancellation remains cancellation, while unrelated exceptions retain existing internal-error behavior.

Add tests with these exact assertions:

```csharp
[Fact]
public async Task Direct_join_preserves_protocol_failure_and_does_not_retry()
{
    LanConnectProtocolFailure failure = Failure.ClientUpdateRequired("0.6.0-alpha.1");
    LobbyJoinAttemptResult result = await DirectJoinHarness.FailWithProtocol(failure);
    Assert.Equal(LobbyJoinAttemptKind.Failed, result.Kind);
    Assert.Same(failure, result.ProtocolFailure);
    Assert.Equal(1, DirectJoinHarness.Attempts);
}

[Fact]
public async Task Restart_rejoin_stops_polling_after_protocol_failure()
{
    RestartHarness harness = RestartHarness.WithProtocolFailure(Failure.CriticalExtensionMismatch("run.data", 2));
    await harness.RunAsync();
    Assert.Null(harness.PendingReconnect);
    Assert.Equal(1, harness.JoinAttempts);
    Assert.Single(harness.PresentedProtocolFailures);
}
```

- [ ] **Step 4: Write and pass the cross-runtime capability digest vectors**

Read `test-fixtures/protocol/v0.6/capability-digest-v1.json`, encode the exact design §4.3 layout, and assert the same expected SHA-256 hex as the TypeScript test. Include empty extensions and at least two UTF-8 IDs supplied in reverse input order.

- [ ] **Step 5: Write the session freeze state-machine tests**

```csharp
[Fact]
public void Frozen_selection_cannot_change_until_the_owner_lease_is_disposed()
{
    var state = new LanConnectSessionProtocolState();
    using LanConnectSessionProtocolLease lease = state.FreezeHost(Selection.TailV1, "room-a");
    Assert.Throws<LanConnectProtocolException>(() => state.FreezeClient(Selection.Compat, "room-a"));
    Assert.Equal(Selection.TailV1, state.Current.Selection);
}
```

Also test idempotent attach of an identical selection, candidate retry, wrong owner reset, closing state, and reset only after transport/session release.

- [ ] **Step 6: Replace mutable profile setters with the lease-backed snapshot**

Remove profile selection by player count and all `0.2.2` inference. Keep `LanConnectProtocolProfiles` temporarily as a thin facade for existing callers, but have bit-width providers read the frozen snapshot and throw if no active session exists during protocol serialization.

- [ ] **Step 7: Mirror service DTOs and probe capability in C#**

Mirror these exact wire shapes in TypeScript and C# (C# uses PascalCase properties with the listed camelCase JSON names):

```ts
type ProtocolExtensionRangeDto = { id: string; minVersion: number; maxVersion: number };
type ProtocolExtensionSelectionDto = { id: string; selectedVersion: number };
type ProtocolOfferDto = {
  lanProtocolMin: number;
  lanProtocolMax: number;
  clientVersion: string;
  ritsuLibPresent: boolean;
  criticalExtensions: ProtocolExtensionRangeDto[];
};
type ProtocolSelectionDto = {
  profile: "compat_4_5_v1" | "tail_v1";
  selectedLanProtocolVersion: number;
  minimumClientVersion: string;
  maxPlayers: number;
  gameVersion: string;
  wireCacheSignature: string | null;
  ritsuLibPresent: boolean;
  criticalExtensions: ProtocolExtensionSelectionDto[];
  capabilityDigest: string;
};
type ProtocolErrorDetailsDto = {
  requiredClientVersion?: string;
  relatedExtensionId?: string;
  requiredExtensionVersion?: number;
  detail?: string;
};
```

Placement is fixed: create request adds `clientVersion`, `protocolProfileV2`, and `protocolOffer`; create response adds `protocolSelection`; room-list item adds `protocolProfileV2` and `protocolSelection`; join/preflight request adds `clientVersion` and `protocolOffer`; join response adds `protocolSelection`; control hello adds `clientVersion` and `capabilityDigest`; `LobbyErrorDetails` adds the four `ProtocolErrorDetailsDto` fields. Legacy `protocolProfile` stays where Task 1 fixtures require it. Add `DualProtocolApiVersion`, `SupportedProtocolProfiles`, `LanProtocolMin/Max`, `MinimumClientVersion`, and `TailV1MinimumClientVersion` to `LobbyProbeCapabilities`. 0.6 clients prefer canonical optional fields; only `extended_8p` is accepted as a legacy projection for compat. `legacy_4p` and unknown strings produce structured local failures. Enforce 32-byte client version, 64-byte extension ID, 512-byte detail, and `1..65535` extension version bounds in both runtimes.

- [ ] **Step 8: Reorder host creation around server selection**

Change `StartLobbyHostAsync` to accept this exact value and return `Task<LanConnectHostAttemptResult>`:

```csharp
internal sealed record LanConnectCreateRoomIntent(
    LanConnectProtocolProfile Profile,
    int MaxPlayers,
    LanConnectProtocolOffer Offer);
```

For Tail/0.6 create, call the service first, validate/freeze its selection, then call `StartENetHost`. If ENet fails, delete the newly registered room and dispose the lease. `PublishExistingHostToLobbyAsync` also returns `Task<LanConnectHostAttemptResult>` so continue-run publication preserves protocol failures. Do not select profile from max players.

Migrate the existing overlay caller in the same step. Until Task 11 adds the visible selector, the overlay creates a hidden/default `LanConnectCreateRoomIntent` for `compat_4_5_v1`. Tail remains unavailable. Do not preserve the old `int? maxPlayersOverride` overload.

- [ ] **Step 9: Freeze join selection before the first transport candidate**

`LanConnectModPreflightCoordinator` adds the local offer to the ticket request and maps protocol `LobbyServiceException` values into `LanConnectModPreflightJoinResult.ProtocolFailure`. `LanConnectLobbyJoinFlow.JoinAsync` validates the returned selection and freezes a tentative client lease before creating direct/relay connection initializers; retries share the same lease. Attach only verifies the same selection. Failure after all candidates disposes the lease. A protocol failure is returned immediately and never enters the next candidate.

Replace the lobby candidate loop's implicit broad retry with `LanConnectJoinRetryPolicy.IsRetryable(Exception)`. It returns true only for `ClientConnectionFailedException` whose reason name is `Timeout`, `HandshakeTimeout`, or `UnknownNetworkError`; it returns false for protocol/authority/codec failures, game or MOD mismatch, run-state rejection, cancellation, and every unexpected/internal exception. Use the same classifier in direct-IP flow except that its existing maximum remains two attempts. Add table-driven tests proving timeout/unknown-network advance to the next candidate and all other categories stop after one attempt.

Pure direct-IP joins have no service selection in alpha. Freeze the local `compat_4_5_v1` selection before constructing `LanConnectLobbyManagedJoinFlow`; Tail direct-IP rooms are not exposed until a separate out-of-band selection exchange is designed. Change `LanConnectDirectJoinFlow.JoinAsync` from `Task<bool>` to `Task<LobbyJoinAttemptResult>` and update `Patches.JoinFriendScreen` to present `ProtocolFailure` through `LanConnectProtocolUiMessages`; do not create a generic `NErrorPopup` for a known protocol failure.

- [ ] **Step 10: Persist the selection for continue-run**

Increment the saved-room binding schema and persist canonical profile, selected LAN version, selected critical extensions, and digest. Continue-run publication reuses this selection; it cannot call a profile-by-player-count helper.

Also migrate both non-lobby host entries in this task:

- `LanConnectHostFlow.StartLanHostAsync` freezes a local compat lease before `StartENetHost`, registers that lease with the LAN host runtime, and disposes it on startup/transport failure.
- `LanConnectMultiplayerSaveCompatibility.StartLoadedRunAsLanHostAsync` restores the saved selection when available; old saves without selection use local compat, never Tail or `8/3`, and freeze before `StartENetHost`.

Cover official continue-run hosts before transport start by extending the existing `StartENetHost`/`StartSteamHost` capacity prefixes: if no explicit selection lease exists, freeze a local compat host lease keyed to the `NetHostGameService` before the original host method runs. `LanConnectContinueRunLobbyAutoPublisher` may later publish only that already frozen compat selection; it cannot select or replace a profile after `context.NetService` exists. Release the guard-created lease from the same host-origin/transport teardown path used by LAN hosts.

- [ ] **Step 11: Run focused and full client tests**

```bash
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj \
  --filter "FullyQualifiedName~LanConnectClientVersionTests|FullyQualifiedName~LanConnectProtocolProfileTests|FullyQualifiedName~LanConnectCapabilityDigestTests|FullyQualifiedName~LanConnectSessionProtocolStateTests|FullyQualifiedName~LanConnectProtocolFailurePropagationTests|FullyQualifiedName~LanConnectLobbyManagedJoinFlowTests|FullyQualifiedName~LanConnectDirectJoinFlowTests|FullyQualifiedName~LanConnectContinueRun|FullyQualifiedName~LanConnectLobbyOverlay" -m:1

DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj -m:1
```

- [ ] **Step 12: Commit the client protocol foundation**

```bash
git add \
  sts2-lan-connect/Scripts/Protocol/LanConnectClientVersion.cs \
  sts2-lan-connect/Scripts/Protocol/LanConnectProtocolProfile.cs \
  sts2-lan-connect/Scripts/Protocol/LanConnectProtocolOffer.cs \
  sts2-lan-connect/Scripts/Protocol/LanConnectProtocolSelection.cs \
  sts2-lan-connect/Scripts/Protocol/LanConnectCreateRoomIntent.cs \
  sts2-lan-connect/Scripts/Protocol/LanConnectSessionProtocolState.cs \
  sts2-lan-connect/Scripts/Protocol/LanConnectProtocolException.cs \
  sts2-lan-connect/Scripts/Protocol/LanConnectProtocolFailure.cs \
  sts2-lan-connect/Scripts/Protocol/LanConnectProtocolFailureMapper.cs \
  sts2-lan-connect/Scripts/Protocol/LanConnectHostAttemptResult.cs \
  sts2-lan-connect/Scripts/Protocol/LanConnectProtocolUiMessages.cs \
  sts2-lan-connect/Scripts/Protocol/LanConnectJoinRetryPolicy.cs \
  sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectCapabilityDigest.cs \
  sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectExternalCapabilityCollector.cs \
  sts2-lan-connect/Scripts/LanConnectConstants.cs \
  sts2-lan-connect/Scripts/LanConnectProtocolProfiles.cs \
  sts2-lan-connect/Scripts/LanConnectHostFlow.cs \
  sts2-lan-connect/Scripts/LanConnectLobbyCapacityPatches.cs \
  sts2-lan-connect/Scripts/Patches.JoinFriendScreen.cs \
  sts2-lan-connect/Scripts/Lobby/LanConnectLobbyModels.cs \
  sts2-lan-connect/Scripts/Lobby/LanConnectLobbyApiClient.cs \
  sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs \
  sts2-lan-connect/Scripts/Lobby/ModSync/LanConnectModPreflightCoordinator.cs \
  sts2-lan-connect/Scripts/Lobby/LanConnectLobbyJoinFlow.cs \
  sts2-lan-connect/Scripts/Lobby/LanConnectDirectJoinFlow.cs \
  sts2-lan-connect/Scripts/Lobby/LanConnectLobbyRuntime.cs \
  sts2-lan-connect/Scripts/Lobby/LanConnectMultiplayerSaveCompatibility.cs \
  sts2-lan-connect/Scripts/Lobby/LanConnectContinueRunLobbyAutoPublisher.cs \
  sts2-lan-connect/Scripts/Lobby/LanConnectHostedRoomMetadata.cs \
  sts2-lan-connect/Scripts/Lobby/LanConnectMultiplayerSaveRoomBinding.cs \
  sts2-lan-connect.Tests/Protocol/LanConnectClientVersionTests.cs \
  sts2-lan-connect.Tests/Protocol/LanConnectProtocolProfileTests.cs \
  sts2-lan-connect.Tests/Protocol/LanConnectSessionProtocolStateTests.cs \
  sts2-lan-connect.Tests/Protocol/LanConnectCapabilityDigestTests.cs \
  sts2-lan-connect.Tests/Protocol/LanConnectProtocolFailurePropagationTests.cs \
  sts2-lan-connect.Tests/Protocol/LanConnectJoinRetryPolicyTests.cs
git commit -m "feat(client): freeze v0.6 room protocol selections"
```

Stage only files changed by this task.

---

### Task 4: Implement the Minimal LAN Tail v1 Envelope Primitive

**Files:**
- Create: `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectTailEntry.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectTailEnvelope.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectTailCodec.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectTailCodecGoldenVectorTests.cs`
- Create: `test-fixtures/protocol/v0.6/*.bin`
- Create: `test-fixtures/protocol/v0.6/*.json`

**Interfaces:**
- Consumes: only PacketReader/PacketWriter and the approved envelope schema.
- Produces:
  - `LanConnectTailCodec.Encode(ushort sessionProtocolVersion, IReadOnlyList<LanConnectTailEntry>): byte[]`
  - `LanConnectTailCodec.Decode(ReadOnlySpan<byte>): LanConnectTailEnvelope`
  - `LanConnectTailCodec.Write(PacketWriter, ushort, IReadOnlyList<LanConnectTailEntry>): void`
  - `LanConnectTailCodec.Read(PacketReader): LanConnectTailEnvelope`
  - exact cursor ownership for later message slices.

- [ ] **Step 1: Hand-author the smallest envelope golden vector before implementing the codec**

Create a fixture for session protocol 1 with one critical `lan.capabilities` entry. The JSON sidecar documents every byte offset and expected value; the `.bin` is produced from that reviewed table, not from production code.

- [ ] **Step 2: Write the failing envelope golden test**

```csharp
[Fact]
public void Writes_the_reviewed_single_entry_vector()
{
    byte[] expected = Fixture.ReadBytes("tail-envelope-capabilities-v1.bin");
    byte[] actual = LanConnectTailCodec.Encode(1, [Fixtures.CapabilitiesSelectionEntry]);
    Assert.Equal(expected, actual);
}
```

- [ ] **Step 3: Run and verify red**

```bash
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj \
  --filter "FullyQualifiedName~LanConnectTailCodecGoldenVectorTests" -m:1
```

- [ ] **Step 4: Implement the bounded envelope exactly as specified**

Enforce `STSLAN01`, version 1, flags 0, network byte order, 256 KiB total, 32 entries, 64 KiB each, 64-byte nonempty IDs, critical flag bit only, UTF-8 ordinal entry ordering, duplicate rejection, zero alignment padding, checked lengths, and exact container consumption. Keep optional following bytes untouched for Ritsu.

- [ ] **Step 5: Add adversarial envelope tests**

Cover bad magic/version/flags, nonzero padding, duplicate IDs, out-of-order input canonicalization, oversized count/ID/payload/container, integer overflow, truncation at every header boundary, unknown critical entry, unknown noncritical skip, and illegal LAN trailing bytes.

- [ ] **Step 6: Run the envelope tests**

```bash
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj \
  --filter "FullyQualifiedName~LanConnectTailCodecGoldenVectorTests" -m:1
```

Expected: reviewed envelope bytes and malformed vectors pass; no message-specific payload codec exists yet.

- [ ] **Step 7: Commit the minimal envelope seam**

```bash
git add sts2-lan-connect/Scripts/Protocol/Tail/LanConnectTailEntry.cs sts2-lan-connect/Scripts/Protocol/Tail/LanConnectTailEnvelope.cs sts2-lan-connect/Scripts/Protocol/Tail/LanConnectTailCodec.cs sts2-lan-connect.Tests/Protocol/LanConnectTailCodecGoldenVectorTests.cs test-fixtures/protocol/v0.6/tail-envelope-capabilities-v1.bin test-fixtures/protocol/v0.6/tail-envelope-capabilities-v1.json
git commit -m "feat(protocol): add bounded LAN Tail v1 envelope"
```

---

### Task 5: Install One Atomic Dispatcher and Complete the Capability/Normal-Join Slice

**Files:**
- Create: `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectProtocolPatchDispatcher.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectCompatWirePatches.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectCapabilitiesCodec.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectRosterCodec.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectRejectionCodec.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectRosterProjection.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectRosterAuthorityState.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectAtomicPatchDispatcherTests.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectInitialGameInfoTailTests.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectJoinRequestTailTests.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectJoinResponseTailTests.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectCapabilitiesCodecTests.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectRosterCodecGoldenVectorTests.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectRosterProjectionTests.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectRejectionCodecTests.cs`
- Create: `sts2-lan-connect.GdUnitTests/Protocol/LanConnectTailMessageBusTests.cs`
- Create: `sts2-lan-connect.GdUnitTests/Protocol/LanConnectRosterRestoreTests.cs`
- Modify: `sts2-lan-connect/Scripts/Entry.cs:18-25`
- Modify: `sts2-lan-connect/Scripts/LanConnectMultiplayerCompatibility.cs:10-32`
- Modify: `sts2-lan-connect/Scripts/LanConnectSerializationPatches.cs:14-597`
- Modify: `sts2-lan-connect.Tests/Patches/LanConnectSerializationPatchesCompatibilityTests.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyManagedJoinFlow.cs:35-120,401-500`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyJoinFlow.cs:22-184,295-298`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectDirectJoinFlow.cs:109-150`

**Interfaces:**
- Consumes: Task 3 frozen session snapshot and Task 4 codecs.
- Produces: one Harmony owner installed once; compat `4/5` dispatch; Tail outer serialize/deserialize hooks; InitialGameInfo selection, normal join-request offer, successful normal join-response validation, and request-stage rejection through built-in `ClientConnectionFailedMessage` before the original flow observes any response.

- [ ] **Step 1: Write failing atomic-install tests**

```csharp
[Fact]
public void Any_missing_required_target_rolls_back_the_dispatcher_owner()
{
    var harness = PatchHarness.WithMissingTarget("ClientLobbyJoinResponseMessage.Deserialize");
    Assert.Throws<InvalidOperationException>(() => harness.Apply());
    Assert.Empty(harness.AppliedPatchesOwnedBy(LanConnectProtocolPatchDispatcher.HarmonyId));
}
```

Also assert startup installs once, runtime mode switching never calls `Unpatch`, an idle/unfrozen protocol message fails closed, and rollback never removes another Harmony owner.

- [ ] **Step 2: Extract current precise `4/5` transpilers into the compat component**

Reuse the surgical `LanConnectTranspilerUtils` matching, but providers return `4/5` only for frozen `compat_4_5_v1`. Remove `8/3` and RMP-driven partial ownership from this dispatcher. Preserve complete write/read symmetry.

- [ ] **Step 3: Resolve stable outer boundaries for all ten message types**

Use the pinned target STS2 assembly to identify closed `NetMessageBus.SerializeMessage<T>` and the outer `TryDeserializeMessage` path for `InitialGameInfoMessage`, all three request/response pairs, built-in `ClientConnectionFailedMessage`, `PlayerJoinedMessage`, and begin-run. Record the verified assembly facts: all three response structs have no success/failure discriminator; LoadJoin/Rejoin deserialize `SerializableRun` before any tail; `ClientConnectionFailedMessage` is built-in ID 34 with bounded vanilla fields `disconnectionReason` and `versionInfo`. Treat all three response types as success-only and use failure message + LAN Tail as the sole protocol rejection path. Register its managed-flow handler before connection starts, unregister it in `finally`, and validate every required target/handler before applying any patch.

- [ ] **Step 4: Write red capabilities codec tests and run them red**

Test peer offer record 1 and session selection record 2, bounds, sorted unique critical extensions, min<=max, session version 0 for join request, selected version 1 for room messages, and all duplicate version-field equality rules.

```bash
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj \
  --filter "FullyQualifiedName~LanConnectCapabilitiesCodecTests" -m:1
```

Expected: FAIL before `LanConnectCapabilitiesCodec` exists.

- [ ] **Step 5: Implement the minimal capabilities codec and make it green**

Implement only the offer/selection payload needed by InitialGameInfo and join request/response. Re-run the focused test to PASS.

- [ ] **Step 6: Write InitialGameInfo and join-request full-message golden tests red**

Create independently reviewed `.bin + .json` vectors for both complete STS2 messages. InitialGameInfo requires session selection, forbids roster/rejection, uses selected session protocol 1, and validates host identity. Join request requires peer offer, forbids roster/rejection, uses session protocol 0, and validates that the offer belongs to the transport sender.

- [ ] **Step 7: Implement InitialGameInfo and join-request Tail paths and make them green**

Patch their stable outer boundaries. Missing/malformed/forbidden Tail or identity mismatch blocks the original handler. Run the two focused classes until both pass.

- [ ] **Step 8: Write red roster/projection tests for successful join response and codec tests for LAN rejection**

Before implementing each codec, write and run its failing tests. Roster vectors cover 2-8 player metadata/child bit payloads, canonical projection, identity/slot uniqueness, authority/revision, limits, bootstrap baseline, and exact child consumption. Rejection vectors cover codes 1-10 and bounds. Full-message rejection vectors contain built-in ID 34 + sender + vanilla `ClientConnectionFailedMessage` body + LAN Tail selection/rejection; they forbid roster/other LAN entries, non-host sender, wrong waiting phase, and duplicate delivery. Ritsu ordering/cursor follows Task 0's generic message-tail contract.

- [ ] **Step 9: Implement the minimal roster/rejection/projection seam and make focused tests green**

Implement only what the normal join response needs. Keep LoadJoin/Rejoin current-snapshot rules in Task 6 and PlayerJoined mutation checks in Task 7.

- [ ] **Step 10: Write failing successful join-response and LAN rejection full-message tests**

`ClientLobbyJoinResponseMessage` must contain session selection + full roster and forbid rejection. `ClientConnectionFailedMessage` with LAN Tail must contain session selection + rejection, forbid roster/other LAN entries, and be accepted only from the host while one of the three request completions is pending. Missing/forbidden entries, wrong record kinds, body/Tail mismatch, non-host authority, malformed Tail, and rejection outside a pending request must prevent every original response handler from receiving a success value.

- [ ] **Step 11: Implement Tail join-response projection and restoration**

For Tail mode, write the vanilla four-player projection with original `2/3` widths, append LAN Tail, and bootstrap/restore the authoritative roster immediately after outer deserialization but before `LanConnectLobbyManagedJoinFlow` completes its response task. Initialize the host's first committed roster at revision 1. A valid LAN rejection on `ClientConnectionFailedMessage` completes the currently pending request with `LanConnectProtocolException`, disconnects, and makes any later STS2 response ineligible to complete success. Compat mode remains current `4/5`; neither mode calls Ritsu private code.

- [ ] **Step 12: Remove the RC4 private composition tests and code**

Delete `RitsuLibAssemblyName`, detached postfix state/delegate/record, `DetachRitsuBeginRunPostfix`, `CreateBeginRunPostfixInvoker`, restore logic, and tests asserting third-party detach/restore. Replace them with regression tests asserting no external `Unpatch` and no private postfix references.

- [ ] **Step 13: Run xUnit and real-assembly GdUnit integration tests**

```bash
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj \
  --filter "FullyQualifiedName~LanConnectAtomicPatchDispatcherTests|FullyQualifiedName~LanConnectInitialGameInfoTailTests|FullyQualifiedName~LanConnectJoinRequestTailTests|FullyQualifiedName~LanConnectJoinResponseTailTests|FullyQualifiedName~LanConnectCapabilitiesCodecTests|FullyQualifiedName~LanConnectRoster|FullyQualifiedName~LanConnectRejection|FullyQualifiedName~LanConnectSerializationPatchesCompatibilityTests" -m:1

DOTNET_ROOT="/Users/mac/.dotnet" PATH="/Users/mac/.dotnet:$PATH" \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
/Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings \
  --filter "FullyQualifiedName~LanConnectTailMessageBusTests|FullyQualifiedName~LanConnectRosterRestoreTests" -m:1
```

- [ ] **Step 14: Build the client**

```bash
export PATH="/Users/mac/.dotnet:$PATH" DOTNET_ROOT="/Users/mac/.dotnet"
DOTNET_BIN="/Users/mac/.dotnet/dotnet" \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
bash scripts/build-sts2-lan-connect.sh
```

- [ ] **Step 15: Commit the complete handshake/join-response slice**

```bash
git add sts2-lan-connect/Scripts/Entry.cs sts2-lan-connect/Scripts/LanConnectMultiplayerCompatibility.cs sts2-lan-connect/Scripts/LanConnectSerializationPatches.cs sts2-lan-connect/Scripts/Protocol/Patches/LanConnectProtocolPatchDispatcher.cs sts2-lan-connect/Scripts/Protocol/Patches/LanConnectCompatWirePatches.cs sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs sts2-lan-connect/Scripts/Protocol/Tail/LanConnectCapabilitiesCodec.cs sts2-lan-connect/Scripts/Protocol/Tail/LanConnectRosterCodec.cs sts2-lan-connect/Scripts/Protocol/Tail/LanConnectRejectionCodec.cs sts2-lan-connect/Scripts/Protocol/LanConnectRosterProjection.cs sts2-lan-connect/Scripts/Protocol/LanConnectRosterAuthorityState.cs sts2-lan-connect/Scripts/Lobby/LanConnectLobbyManagedJoinFlow.cs sts2-lan-connect/Scripts/Lobby/LanConnectLobbyJoinFlow.cs sts2-lan-connect/Scripts/Lobby/LanConnectDirectJoinFlow.cs sts2-lan-connect.Tests/Patches/LanConnectSerializationPatchesCompatibilityTests.cs sts2-lan-connect.Tests/Protocol/LanConnectAtomicPatchDispatcherTests.cs sts2-lan-connect.Tests/Protocol/LanConnectInitialGameInfoTailTests.cs sts2-lan-connect.Tests/Protocol/LanConnectJoinRequestTailTests.cs sts2-lan-connect.Tests/Protocol/LanConnectJoinResponseTailTests.cs sts2-lan-connect.Tests/Protocol/LanConnectCapabilitiesCodecTests.cs sts2-lan-connect.Tests/Protocol/LanConnectRosterCodecGoldenVectorTests.cs sts2-lan-connect.Tests/Protocol/LanConnectRosterProjectionTests.cs sts2-lan-connect.Tests/Protocol/LanConnectRejectionCodecTests.cs sts2-lan-connect.GdUnitTests/Protocol/LanConnectTailMessageBusTests.cs sts2-lan-connect.GdUnitTests/Protocol/LanConnectRosterRestoreTests.cs test-fixtures/protocol/v0.6/tail-initial-game-info-v1.bin test-fixtures/protocol/v0.6/tail-initial-game-info-v1.json test-fixtures/protocol/v0.6/tail-lobby-join-request-v1.bin test-fixtures/protocol/v0.6/tail-lobby-join-request-v1.json test-fixtures/protocol/v0.6/tail-lobby-join-response-v1.bin test-fixtures/protocol/v0.6/tail-lobby-join-response-v1.json test-fixtures/protocol/v0.6/tail-protocol-rejection-v1.bin test-fixtures/protocol/v0.6/tail-protocol-rejection-v1.json
git commit -m "feat(protocol): route lobby handshake through LAN Tail v1"
```

---

### Task 6: Cover Loaded-Lobby Join and Running Rejoin End-to-End

**Files:**
- Modify: `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs`
- Modify: `sts2-lan-connect/Scripts/Protocol/LanConnectRosterAuthorityState.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyManagedJoinFlow.cs:24-27,68-143,92-112,418-473`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyRuntime.cs:2600-2698`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectLoadJoinTailTests.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectRejoinTailTests.cs`
- Create: `sts2-lan-connect.GdUnitTests/Protocol/LanConnectContinuationMessageTests.cs`

**Interfaces:**
- Consumes: Task 5 dispatcher, capability/rejection codecs, projection seam, authority state, and structured failure result.
- Produces: peer offers on `ClientLoadJoinRequestMessage`/`ClientRejoinRequestMessage`; success-only STS2 responses with session selection/current authoritative snapshot; request-stage rejection through built-in failure message; fail-closed restoration before `JoinResult.loadJoinResponse`/`rejoinResponse`; terminal structured auto-rejoin behavior.

- [ ] **Step 1: Hand-author complete request/response vectors before implementation**

Create reviewed `.bin + .json` vectors for `ClientLoadJoinRequestMessage`, successful `ClientLoadJoinResponseMessage`, `ClientRejoinRequestMessage`, successful `ClientRejoinResponseMessage`, and built-in `ClientConnectionFailedMessage` + rejection Tail while each request phase is pending. Each success JSON fixes vanilla-body boundary, Tail offsets, selection/offer record kind, authority peer, roster revision, bootstrap carrier mapping, projection, and exact `2/3-bit` body widths. The rejection JSON fixes ID 34, vanilla failure-body boundary, LAN Tail boundary, and optional Ritsu cursor. Expected bytes may not come from production encoders.

- [ ] **Step 2: Write request matrix tests red**

Both requests require `sessionProtocolVersion=0` and peer offer, and forbid roster/rejection. Test transport-sender identity, ticket/selection compatibility, malformed/missing Tail, wrong record kind, and capability change after ticket. Prove invalid requests never reach `HandleClientLoadJoinRequestMessage` or `HandleClientRejoinRequestMessage`.

- [ ] **Step 3: Implement LoadJoin/Rejoin request handling and make it green**

Attach the same local offer used for the ticket. Host validation compares it with the frozen room selection before reading save/run payloads. A mismatch emits built-in `ClientConnectionFailedMessage` with session selection + `lan.rejection` Tail, does not construct/read a response `SerializableRun`, does not mutate roster/load state, and disconnects after the rejection is sent.

- [ ] **Step 4: Write success response, LAN rejection, bootstrap, and revision tests red**

Successful responses require session selection + full roster and forbid rejection. LAN rejection requires session selection + rejection and forbids roster/other/trailing entries. Cover non-host sender, body/Tail projection mismatch, session-membership mismatch, stale revision, unexplained future revision, an idempotent response whose revision equals the latest accepted snapshot, and a fresh-client bootstrap at host revision >1. Prove malformed/rejected messages cannot complete `_loadJoinCompletion`/`_rejoinCompletion` as success or expose `serializableRun`.

- [ ] **Step 5: Implement current-snapshot response restoration**

Host serializes the current committed roster revision; LoadJoin/Rejoin alone never increments it. With no baseline, receiver permits exactly one bootstrap snapshot with revision >0 after validating host, ticket selection/digest, joining player binding, projection, and the state-specific authoritative carrier: `playersAlreadyConnected + joiner` for LoadJoin or `serializableRun` multiplayer roster + rejoin binding for Rejoin. With a baseline, accept equal revision only for a byte-equivalent canonical roster; a greater revision requires a separately verified membership mutation and a lower revision fails closed. Persist no revision in save binding. Restore full real slots before completing the managed-flow response task or exposing `serializableRun` to STS2 screens.

- [ ] **Step 6: Preserve protocol failures through running auto-rejoin**

Convert Tail rejection and decode/authority failures into `LanConnectProtocolException`, then `LobbyJoinAttemptResult.ProtocolFailure`. In `TryReconnectClientAfterRestartAsync`, protocol failure clears `_pendingClientReconnect`, presents `LanConnectProtocolUiMessages.Describe(failure)` once, and returns. `room_not_found` and retryable transport failure still delay/retry. Add a regression assertion that the existing navigation in-flight lock remains held until this terminal decision, preserving the prior restart race fix.

- [ ] **Step 7: Run focused xUnit/GdUnit tests**

```bash
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj \
  --filter "FullyQualifiedName~LanConnectLoadJoinTailTests|FullyQualifiedName~LanConnectRejoinTailTests|FullyQualifiedName~LanConnectProtocolFailurePropagationTests|FullyQualifiedName~LanConnectLobbyManagedJoinFlowTests" -m:1

DOTNET_ROOT="/Users/mac/.dotnet" PATH="/Users/mac/.dotnet:$PATH" \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
/Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings \
  --filter "FullyQualifiedName~LanConnectContinuationMessageTests" -m:1
```

- [ ] **Step 8: Commit the continuation slice**

```bash
git add sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs sts2-lan-connect/Scripts/Protocol/LanConnectRosterAuthorityState.cs sts2-lan-connect/Scripts/Lobby/LanConnectLobbyManagedJoinFlow.cs sts2-lan-connect/Scripts/Lobby/LanConnectLobbyRuntime.cs sts2-lan-connect.Tests/Protocol/LanConnectLoadJoinTailTests.cs sts2-lan-connect.Tests/Protocol/LanConnectRejoinTailTests.cs sts2-lan-connect.GdUnitTests/Protocol/LanConnectContinuationMessageTests.cs test-fixtures/protocol/v0.6/tail-load-join-request-v1.bin test-fixtures/protocol/v0.6/tail-load-join-request-v1.json test-fixtures/protocol/v0.6/tail-load-join-response-v1.bin test-fixtures/protocol/v0.6/tail-load-join-response-v1.json test-fixtures/protocol/v0.6/tail-rejoin-request-v1.bin test-fixtures/protocol/v0.6/tail-rejoin-request-v1.json test-fixtures/protocol/v0.6/tail-rejoin-response-v1.bin test-fixtures/protocol/v0.6/tail-rejoin-response-v1.json test-fixtures/protocol/v0.6/tail-protocol-rejection-load-join-v1.bin test-fixtures/protocol/v0.6/tail-protocol-rejection-load-join-v1.json test-fixtures/protocol/v0.6/tail-protocol-rejection-rejoin-v1.bin test-fixtures/protocol/v0.6/tail-protocol-rejection-rejoin-v1.json
git commit -m "feat(protocol): secure load-join and running rejoin tails"
```

---

### Task 7: Add Authoritative PlayerJoined Full Snapshots

**Files:**
- Modify: `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs`
- Modify: `sts2-lan-connect/Scripts/Protocol/LanConnectRosterAuthorityState.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyRuntime.cs:3524-3555`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectPlayerJoinedTailTests.cs`
- Create: `sts2-lan-connect.GdUnitTests/Protocol/LanConnectPlayerJoinedMessageTests.cs`

**Interfaces:**
- Consumes: Task 5 dispatcher and Task 6 continuation-safe authority state.
- Produces: host-only full roster snapshot on every `PlayerJoinedMessage`, with strictly increasing revision and validated restoration before the original handler.

- [ ] **Step 1: Write red tests for high-slot join and authority**

```csharp
[Fact]
public void Slot_seven_is_restored_only_for_the_newly_connected_peer()
{
    var state = AuthorityHarness.HostWithPlayers(0, 1, 2, 3, 4, 5, 6);
    Snapshot snapshot = state.Join(peerId: 8008, realSlot: 7);
    Assert.True(state.TryAcceptPlayerJoined(hostPeerId: state.HostPeerId, connectedPeerId: 8008, snapshot));
    Assert.Equal(7, state.Players.Single(p => p.Id == 8008).SlotId);
}
```

Add red cases for non-host sender, wrong connected peer, duplicate slot/ID, stale/equal revision, cross-room authority, and body projection mismatch.

Add an independently reviewed complete `PlayerJoinedMessage` `.bin + .json` vector, including the vanilla body boundary, session-selection entry, full roster entry, and exact `2/3-bit` assertions. The expected bytes must not be produced by the production encoder.

- [ ] **Step 2: Implement host snapshot generation from the connected peer table**

Use runtime connection events only as authority inputs. Do not infer authoritative membership solely from the message. Increment room-scoped `rosterRevision` exactly once per accepted mutation.

- [ ] **Step 3: Patch outer PlayerJoined serialization/deserialization**

Tail mode sends a safe vanilla player/projection plus session selection + full snapshot; compat remains `4/5`. The receiver validates host, peer, revision, and projection, then restores full state before the original `HandlePlayerJoinedMessage`. On failure, block the original handler and disconnect with a structured protocol failure.

- [ ] **Step 4: Run focused xUnit/GdUnit tests**

```bash
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj \
  --filter "FullyQualifiedName~LanConnectPlayerJoinedTailTests|FullyQualifiedName~LanConnectRosterProjectionTests" -m:1

DOTNET_ROOT="/Users/mac/.dotnet" PATH="/Users/mac/.dotnet:$PATH" \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
/Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings \
  --filter "FullyQualifiedName~LanConnectPlayerJoinedMessageTests" -m:1
```

- [ ] **Step 5: Commit the vertical slice**

```bash
git add sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs sts2-lan-connect/Scripts/Protocol/LanConnectRosterAuthorityState.cs sts2-lan-connect/Scripts/Lobby/LanConnectLobbyRuntime.cs sts2-lan-connect.Tests/Protocol/LanConnectPlayerJoinedTailTests.cs sts2-lan-connect.GdUnitTests/Protocol/LanConnectPlayerJoinedMessageTests.cs test-fixtures/protocol/v0.6/tail-player-joined-v1.bin test-fixtures/protocol/v0.6/tail-player-joined-v1.json
git commit -m "feat(protocol): restore extended players from authoritative snapshots"
```

---

### Task 8: Complete Begin-Run Tail Flow and Remove the RC4 Bridge

**Files:**
- Modify: `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs`
- Modify: `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectProtocolPatchDispatcher.cs`
- Modify: `sts2-lan-connect/Scripts/LanConnectSerializationPatches.cs:14-597`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyManagedJoinFlow.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectBeginRunTailTests.cs`
- Create: `sts2-lan-connect.GdUnitTests/Protocol/LanConnectBeginRunMessageTests.cs`
- Create: `test-fixtures/protocol/v0.6/tail-begin-run-2p-v1.bin`
- Create: `test-fixtures/protocol/v0.6/tail-begin-run-2p-v1.json`
- Create: `test-fixtures/protocol/v0.6/tail-begin-run-4p-v1.bin`
- Create: `test-fixtures/protocol/v0.6/tail-begin-run-4p-v1.json`
- Create: `test-fixtures/protocol/v0.6/tail-begin-run-5p-v1.bin`
- Create: `test-fixtures/protocol/v0.6/tail-begin-run-5p-v1.json`
- Create: `test-fixtures/protocol/v0.6/tail-begin-run-8p-v1.bin`
- Create: `test-fixtures/protocol/v0.6/tail-begin-run-8p-v1.json`

**Interfaces:**
- Consumes: authoritative roster state from Task 7.
- Produces: final host snapshot in `LobbyBeginRunMessage`, exact Tail restoration before the original begin-run handler, and no RC4 private Ritsu composition code.

- [ ] **Step 1: Write failing begin-run vectors and handler-isolation tests**

Cover every player count 2-8 automatically; require 2/4/5/8 reviewed bytes. Test non-host sender, stale revision, mismatch with connected peers, projection mismatch, missing/malformed Tail, and prove the original handler is not called on failure.

- [ ] **Step 2: Implement final snapshot serialization**

Immediately before host broadcast, reconcile the final session membership/slot binding table. Increment revision exactly once only if this reconciliation discovers a real membership or slot mutation not already committed by `PlayerJoined`/disconnect handling; otherwise reuse the current committed revision. Write the vanilla first-four projection using original `2/3` widths, append capabilities selection + full roster, and set the final message length without invoking any detached third-party patch. Add a test that begin-run with an unchanged roster preserves revision and a test that one pending removal increments exactly once.

- [ ] **Step 3: Implement pre-handler restoration**

After outer deserialize, validate session selection, authority, revision, connection set, canonical projection, and exact Tail consumption. Replace `playersInLobby` with restored real-slot players only after every check passes.

- [ ] **Step 4: Delete all remaining private Ritsu begin-run bridge code and stale log assertions**

Search production/test sources and require zero runtime references to:

```text
DetachedBeginRunPostfix
DetachRitsuBeginRunPostfix
CreateBeginRunPostfixInvoker
ritsuTailBridge
restored detached RitsuLib
```

Do not remove the separately evaluated lobby sidecar bridge in this task.

- [ ] **Step 5: Run focused tests and full client build**

```bash
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj \
  --filter "FullyQualifiedName~LanConnectBeginRunTailTests|FullyQualifiedName~LanConnectAtomicPatchDispatcherTests" -m:1

DOTNET_ROOT="/Users/mac/.dotnet" PATH="/Users/mac/.dotnet:$PATH" \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
/Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings \
  --filter "FullyQualifiedName~LanConnectBeginRunMessageTests" -m:1

export PATH="/Users/mac/.dotnet:$PATH" DOTNET_ROOT="/Users/mac/.dotnet"
DOTNET_BIN="/Users/mac/.dotnet/dotnet" \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
bash scripts/build-sts2-lan-connect.sh
```

- [ ] **Step 6: Perform a no-Ritsu two-client Tail smoke test before enabling the UI option**

Use matching 0.6 local builds. Verify create, ticket, connection, ready, begin-run, and first synchronized game state. Record both logs and raw capability/profile/revision markers in the feasibility document. The Tail UI option remains hidden until this passes.

- [ ] **Step 7: Commit the completed no-Ritsu Tail path**

```bash
git add sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs sts2-lan-connect/Scripts/Protocol/Patches/LanConnectProtocolPatchDispatcher.cs sts2-lan-connect/Scripts/LanConnectSerializationPatches.cs sts2-lan-connect/Scripts/Lobby/LanConnectLobbyManagedJoinFlow.cs sts2-lan-connect.Tests/Protocol/LanConnectBeginRunTailTests.cs sts2-lan-connect.GdUnitTests/Protocol/LanConnectBeginRunMessageTests.cs test-fixtures/protocol/v0.6/tail-begin-run-2p-v1.bin test-fixtures/protocol/v0.6/tail-begin-run-2p-v1.json test-fixtures/protocol/v0.6/tail-begin-run-4p-v1.bin test-fixtures/protocol/v0.6/tail-begin-run-4p-v1.json test-fixtures/protocol/v0.6/tail-begin-run-5p-v1.bin test-fixtures/protocol/v0.6/tail-begin-run-5p-v1.json test-fixtures/protocol/v0.6/tail-begin-run-8p-v1.bin test-fixtures/protocol/v0.6/tail-begin-run-8p-v1.json docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_FEASIBILITY_ZH.md
git commit -m "feat(protocol): complete no-Ritsu Tail v1 start-run flow"
```

---

### Task 9: Integrate RitsuLib Through Public Contracts Only

**Precondition:** Task 0’s Tail order/cursor, four-combination, complete inventory, critical classification, and Android gates are all `PASS`. If not, do not execute this task; return to design review before claiming the approved alpha scope is complete.

**Files:**
- Create: `sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectRitsuLibPublicApiAdapter.cs`
- Create: `sts2-lan-connect.GdUnitTests/Protocol/LanConnectRitsuTailInteropTests.cs`
- Modify: `sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj`
- Modify: `sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectExternalCapabilityCollector.cs`
- Modify: `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs`
- Modify: `sts2-lan-connect/Scripts/LanConnectExternalModDetection.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectRitsuLibLobbyCompatibility.cs`
- Modify: `sts2-lan-connect/Scripts/LanConnectGameplayPatches.cs`
- Modify: `lobby-service/src/protocol-capabilities.ts`
- Modify: `lobby-service/src/protocol-capabilities.test.ts`
- Modify: `lobby-service/src/store.ts`
- Modify: `docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_FEASIBILITY_ZH.md`

**Interfaces:**
- Consumes: the exact public contract proven by Task 0.
- Produces: complete public extension inventory, critical classification/ranges, canonical offer, fixed `LAN Tail -> optional Ritsu Tail` ordering, and room-level critical version gates.

- [ ] **Step 1: Add a test-only real Ritsu assembly input without packaging it**

Configure the GdUnit test project to conditionally load `$(RitsuLibAssembly)` only when supplied. Do not add the DLL to package scripts, the solution output allowlist, or Git.

- [ ] **Step 2: Write the four-combination GdUnit tests with the production adapter**

Assert identical LAN roster bytes, exact LAN end/Ritsu start cursor, correct final message, and no private member references for all four combinations.

- [ ] **Step 3: Implement the public API adapter exactly as proven**

Expose explicit outcomes: framework absent, supported complete inventory, unsupported API, incomplete inventory, unknown classification, unknown critical extension, and ordering contract unavailable. Only the first two outcomes may enter Tail rooms.

- [ ] **Step 4: Add critical extension offer/selection to client and service gates**

The client submits the complete sorted critical range set. The service intersects each with its verified whitelist and freezes the highest common version. Joiners require the identical ID set and ranges covering each selected version. Explicitly classified noncritical extensions do not enter selection.

- [ ] **Step 5: Replace or disable the sidecar bridge according to Task 0 evidence**

If a public sidecar API replacement passed, use it and delete the transpiler/private method patch. If the sidecar public replacement is blocked, keep Tail message coexistence separate and mark sidecar-dependent Ritsu capability as unsupported; do not silently preserve private patching in an otherwise public adapter.

- [ ] **Step 6: Run real-assembly tests and service gates**

```bash
DOTNET_ROOT="/Users/mac/.dotnet" PATH="/Users/mac/.dotnet:$PATH" \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
/Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings \
  -p:RitsuLibAssembly="$RITSULIB_ASSEMBLY" \
  --filter "FullyQualifiedName~LanConnectRitsuTailInteropTests" -m:1

cd lobby-service
npm run build
node --test dist/protocol-capabilities.test.js dist/store.test.js dist/app.integration.test.js
```

- [ ] **Step 7: Repeat Windows and Android four-combination smokes**

Record startup, ticket, Tail cursor, begin-run, and synchronized state evidence. Any `InvalidProgramException`, generic shape mismatch, different LAN bytes, incomplete inventory, or wrong cursor blocks this task.

- [ ] **Step 8: Commit public Ritsu integration**

```bash
git add sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectRitsuLibPublicApiAdapter.cs sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectExternalCapabilityCollector.cs sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs sts2-lan-connect/Scripts/LanConnectExternalModDetection.cs sts2-lan-connect/Scripts/Lobby/LanConnectRitsuLibLobbyCompatibility.cs sts2-lan-connect/Scripts/LanConnectGameplayPatches.cs sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj sts2-lan-connect.GdUnitTests/Protocol/LanConnectRitsuTailInteropTests.cs lobby-service/src/protocol-capabilities.ts lobby-service/src/protocol-capabilities.test.ts lobby-service/src/store.ts docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_FEASIBILITY_ZH.md
git commit -m "feat(protocol): integrate RitsuLib through public contracts"
```

---

### Task 10: Complete the 0.3-0.5 Compat Room End-to-End

**Files:**
- Modify: `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectCompatWirePatches.cs`
- Modify: `sts2-lan-connect/Scripts/Protocol/Patches/LanConnectProtocolPatchDispatcher.cs`
- Modify: `sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectExternalCapabilityCollector.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyModels.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyJoinFlow.cs`
- Modify: `sts2-lan-connect/Scripts/LanConnectHostFlow.cs`
- Modify: `lobby-service/src/room-api-view.ts`
- Modify: `lobby-service/src/store.ts:523-598,1172-1207`
- Modify: `lobby-service/src/app.ts`
- Modify: `lobby-service/src/protocol-compatibility.integration.test.ts`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectCompatFixtureTests.cs`

**Interfaces:**
- Consumes: Task 1 real fixtures and Tasks 2-3 canonical/legacy API adapters.
- Produces: explicit `compat_4_5_v1` rooms that speak `4/5`, project as `extended_8p` to 0.3-0.5, allow 0.6<->0.5.5, reject Ritsu, and never use `8/3`.

- [ ] **Step 1: Write fixture-driven red tests in both runtimes**

Service tests feed captured 0.5.5 create/join/control and assert internal compat selection plus legacy output. C# tests deserialize captured list/join responses and assert canonical mapping to compat.

- [ ] **Step 2: Decouple LAN Connect client version from general MOD equality**

In compat rooms, allow LAN Connect 0.3-0.6 based on profile/version generation. Continue strict inventory/version checks for other gameplay MODs. Never weaken game-version or wire-cache checks.

- [ ] **Step 3: Guarantee fixed `4/5` wire behavior**

Compat mode always returns slot/list widths `4/5`, including four-player rooms. Remove all production access to `Legacy4pSlotIdBits=8`; keep old bytes only in fixtures. The dispatcher cannot automatically downgrade a Tail room when an old client attempts to join.

- [ ] **Step 4: Reject Ritsu before host start or ticket issuance**

Host create with local Ritsu fails locally and at the service. A Ritsu joiner fails before ticket allocation. Error code is `ritsulib_not_allowed_in_compat_mode` with structured details.

- [ ] **Step 5: Preserve old room-list/control shapes**

Unversioned lists expose legacy `protocolProfile=extended_8p` plus optional v0.6 canonical fields. Old control URLs and frames continue working; protocol/digest fields are server-generated and cannot be relayed from clients as authority fields.

- [ ] **Step 6: Run service, C#, and real 0.5.5 compatibility tests**

```bash
cd lobby-service
npm run build
node --test dist/protocol-compatibility.integration.test.js dist/store.test.js dist/app.integration.test.js

cd ..
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj \
  --filter "FullyQualifiedName~LanConnectCompatFixtureTests|FullyQualifiedName~LanConnectProtocolProfileTests" -m:1
```

Then run real 0.6 host/0.5.5 client and 0.5.5 host/0.6 client through ready, begin-run, and first synchronized game state.

- [ ] **Step 7: Commit the compat path**

```bash
git add sts2-lan-connect/Scripts/Protocol/Patches/LanConnectCompatWirePatches.cs sts2-lan-connect/Scripts/Protocol/Patches/LanConnectProtocolPatchDispatcher.cs sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectExternalCapabilityCollector.cs sts2-lan-connect/Scripts/Lobby/LanConnectLobbyModels.cs sts2-lan-connect/Scripts/Lobby/LanConnectLobbyJoinFlow.cs sts2-lan-connect/Scripts/LanConnectHostFlow.cs sts2-lan-connect.Tests/Protocol/LanConnectCompatFixtureTests.cs lobby-service/src/room-api-view.ts lobby-service/src/room-api-view.test.ts lobby-service/src/store.ts lobby-service/src/app.ts lobby-service/src/protocol-compatibility.integration.test.ts
git commit -m "feat(protocol): preserve 0.3-0.5 compat rooms"
```

---

### Task 11: Add Create-Mode UI and Structured Protocol Errors

**Files:**
- Modify: `sts2-lan-connect/Scripts/Protocol/LanConnectProtocolUiMessages.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectProtocolUiMessagesTests.cs`
- Create: `sts2-lan-connect.GdUnitTests/Lobby/LanConnectCreateProtocolDialogTests.cs`
- Create: `sts2-lan-connect.GdUnitTests/Lobby/LanConnectProtocolErrorPresentationTests.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs:161-167,2249-2264,2727-2751,3712-3790,4252-4256,5708-5719,5789-5820,6134-6160`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyRuntime.cs:2600-2698`
- Modify: `sts2-lan-connect/Scripts/Lobby/ModSync/LanConnectModPreflightCoordinator.cs:14-63,94-248`
- Modify: `sts2-lan-connect/Scripts/LanConnectHostFlow.cs:109-315`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectDirectJoinFlow.cs:16-151`
- Modify: `sts2-lan-connect/Scripts/Patches.JoinFriendScreen.cs:215-243`
- Modify: `sts2-lan-connect/Scripts/LanConnectCompatibilityMatrix.cs:40-65`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyModels.cs:123-168`

**Interfaces:**
- Consumes: completed compat/Tail/Ritsu gates and `LanConnectProtocolFailure`.
- Produces: default compat selector, opt-in Tail selector, 2-8 player input independent of profile, pre-create local validation, room protocol display, and messages for all ten rejection codes across host, ticket/preflight, normal join, load join, rejoin, direct join, and restart auto-rejoin surfaces.

- [ ] **Step 1: Write red UI state tests**

Assert default is compat, profile does not change with player count, Tail warning states “仅支持 0.6+”, compat warning states “支持 0.3-0.5，不支持 RitsuLib”, and room profile cannot change after submission.

- [ ] **Step 2: Add the protocol selector to the existing create dialog**

Add `_protocolProfileOption`, description label, and warning label in the existing dialog flow. Keep edits localized. Options:

```text
兼容旧版客户端（默认）
0.6 新协议（支持 RitsuLib）
```

Max players becomes 2-8 and no longer determines profile.

- [ ] **Step 3: Gate create locally before `_actionInFlight`**

Compat+Ritsu rejects locally. Tail requires a complete current offer, supported public Ritsu outcome if present, selected profile availability, and `LobbyProbeCapabilities.DualProtocolApiVersion == 1` with `SupportedProtocolProfiles` containing `tail_v1` and LAN range containing version 1. Create passes an explicit intent to `LanConnectHostFlow`.

- [ ] **Step 4: Implement stable structured error messages**

Map all service string codes and Tail numeric codes to one enum/value. Use structured required version/extension fields. Keep raw `detail` in logs only. Unknown codes show a generic protocol rejection and preserve the numeric/string code in diagnostics.

Assert every public flow consumes the structured field before fallback text: create/publish reads `LanConnectHostAttemptResult.ProtocolFailure`; preflight reads `LanConnectModPreflightJoinResult.ProtocolFailure`; lobby/direct join and restart auto-rejoin read `LobbyJoinAttemptResult.ProtocolFailure`. Known protocol failures use `LanConnectProtocolUiMessages` and never create `NErrorPopup(NetError.InternalError)`; unknown exceptions retain the existing popup. Cancellation produces no protocol popup.

- [ ] **Step 5: Run focused xUnit and GdUnit tests**

```bash
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj \
  --filter "FullyQualifiedName~LanConnectProtocolUiMessagesTests|FullyQualifiedName~LanConnectProtocolProfileTests|FullyQualifiedName~LanConnectProtocolFailurePropagationTests|FullyQualifiedName~LanConnectDirectJoinFlowTests" -m:1

DOTNET_ROOT="/Users/mac/.dotnet" PATH="/Users/mac/.dotnet:$PATH" \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
/Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings \
  --filter "FullyQualifiedName~LanConnectCreateProtocolDialogTests|FullyQualifiedName~LanConnectProtocolErrorPresentationTests" -m:1
```

- [ ] **Step 6: Commit the user-facing mode selection**

```bash
git add sts2-lan-connect/Scripts/Protocol/LanConnectProtocolUiMessages.cs sts2-lan-connect/Scripts/Lobby/LanConnectLobbyOverlay.cs sts2-lan-connect/Scripts/Lobby/LanConnectLobbyRuntime.cs sts2-lan-connect/Scripts/Lobby/ModSync/LanConnectModPreflightCoordinator.cs sts2-lan-connect/Scripts/Lobby/LanConnectDirectJoinFlow.cs sts2-lan-connect/Scripts/LanConnectHostFlow.cs sts2-lan-connect/Scripts/Patches.JoinFriendScreen.cs sts2-lan-connect/Scripts/LanConnectCompatibilityMatrix.cs sts2-lan-connect/Scripts/Lobby/LanConnectLobbyModels.cs sts2-lan-connect.Tests/Protocol/LanConnectProtocolUiMessagesTests.cs sts2-lan-connect.GdUnitTests/Lobby/LanConnectCreateProtocolDialogTests.cs sts2-lan-connect.GdUnitTests/Lobby/LanConnectProtocolErrorPresentationTests.cs
git commit -m "feat(ui): expose v0.6 protocol room modes"
```

---

### Task 12: Version, Package, Document, and Run the Alpha Acceptance Gate

**Files:**
- Modify: `sts2-lan-connect/sts2_lan_connect.csproj:10-13`
- Modify: `sts2-lan-connect/sts2_lan_connect.json:5`
- Modify: `lobby-service/package.json:3`
- Modify: `lobby-service/package-lock.json:3,9`
- Modify: `sts2-lan-connect.Tests/Packaging/LanConnectPackageContentTests.cs`
- Modify: `lobby-service/src/package-content.test.ts`
- Modify: `README.md`
- Modify: `lobby-service/README.md`
- Modify: `CHANGELOG.md`
- Modify: `docs/CLIENT_RELEASE_README_ZH.md`
- Modify: `docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md`
- Modify: `docs/STEAM_WORKSHOP_DESCRIPTION_ZH.txt`
- Create: `docs/RELEASE_NOTES_V0.6.0_ALPHA1_ZH.md`
- Create: `docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_ACCEPTANCE_ZH.md`

**Interfaces:**
- Consumes: all prior automated and real-platform evidence.
- Produces: source/package version `0.6.0-alpha.1`, updated package assertions/docs, full verification evidence, and a go/no-go verdict. This task does not create a Git tag, push, or GitHub Release unless separately requested.

- [ ] **Step 1: Update authoritative versions**

Client:

```xml
<Version>0.6.0-alpha.1</Version>
<AssemblyVersion>0.6.0.0</AssemblyVersion>
<FileVersion>0.6.0.0</FileVersion>
<InformationalVersion>0.6.0-alpha.1</InformationalVersion>
```

Service package/lock root: `0.6.0-alpha.1`. Manifest: `0.6.0-alpha.1`.

- [ ] **Step 2: Replace RC4-specific package assertions**

Assert the alpha version, default compat mode, Tail v1 documentation, removal of `ritsuTailBridge=True`, no recommended RC2-RC4 path, and stable client still `0.5.5`.

- [ ] **Step 3: Update user/release documentation**

Document the two create modes, 0.2 end of support, 0.3-0.5 compat requirement, compat Ritsu rejection, Tail 0.6 minimum, critical extension rules, update/restart steps, expected profile/digest/revision logs, and known alpha limitations.

Update `lobby-service/README.md` with the synchronized service version, `/probe` dual-protocol fields, deployment restart requirement, 0.2 rejection, profile projection rules, and service/client co-upgrade instructions. Add a package-content assertion that the packaged service README contains `0.6.0-alpha.1`.

- [ ] **Step 4: Run full service tests**

```bash
cd lobby-service
npm run check
npm run test
```

- [ ] **Step 5: Run full xUnit**

```bash
cd ..
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj -m:1
```

- [ ] **Step 6: Run full GdUnit**

```bash
DOTNET_ROOT="/Users/mac/.dotnet" PATH="/Users/mac/.dotnet:$PATH" \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
/Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings -m:1
```

- [ ] **Step 7: Build and run the complete temporary-package release gate**

```bash
export PATH="/Users/mac/.dotnet:$PATH" DOTNET_ROOT="/Users/mac/.dotnet"
DOTNET_BIN="/Users/mac/.dotnet/dotnet" \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
bash scripts/verify-release.sh
```

Expected: service checks/tests, xUnit, GdUnit, client/service package allowlists, installer dry-run, legal files, and fresh package reproducibility all pass. No Ritsu/game/prototype DLL appears in public packages.

- [ ] **Step 8: Execute and record the real platform acceptance matrix**

Required smoke cases:

```text
Windows <-> Windows
Windows <-> Android
Windows <-> macOS
Android <-> macOS
Tail: no Ritsu, one-sided Ritsu, two-sided Ritsu
Tail continuation: InLoadedLobby load join and Running rejoin, including one 5+ player case
Compat: 0.6 <-> real 0.5.5
Compat: Ritsu host rejection and Ritsu joiner rejection
Player counts: 2/4/5/8 across principal smokes
Player counts: 3/6/7 each in at least one cross-platform run
```

Each new-lobby success requires ticket, connection, ready, begin-run, first synchronized game state, and no divergence/timeout. Each continuation success requires ticket, InitialGameInfo state branch, accepted LoadJoin/Rejoin response, full real-slot roster restoration, resumed synchronized game state, and no retry after an injected protocol rejection. Attach both endpoint logs and record profile, selected protocol, capability digest, roster revision, Tail cursor, and Ritsu inventory outcome.

- [ ] **Step 9: Record the go/no-go verdict**

`GO` requires every approved design acceptance criterion, including Task 0 Ritsu gates. A failed Ritsu gate means the approved `0.6.0-alpha.1` scope is not complete and requires design review; do not quietly ship “Ritsu unsupported” under the approved release notes.

- [ ] **Step 10: Commit version, docs, and verification evidence**

```bash
git add sts2-lan-connect/sts2_lan_connect.csproj sts2-lan-connect/sts2_lan_connect.json lobby-service/package.json lobby-service/package-lock.json lobby-service/README.md sts2-lan-connect.Tests/Packaging/LanConnectPackageContentTests.cs lobby-service/src/package-content.test.ts README.md CHANGELOG.md docs/CLIENT_RELEASE_README_ZH.md docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md docs/STEAM_WORKSHOP_DESCRIPTION_ZH.txt docs/RELEASE_NOTES_V0.6.0_ALPHA1_ZH.md docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_ACCEPTANCE_ZH.md
git commit -m "release: prepare v0.6.0-alpha.1 candidate"
```

- [ ] **Step 11: Inspect final scope without touching unrelated work**

```bash
git status --short
git diff --check
git log --oneline -12
```

Review only commits/files from this plan. Do not stage or restore unrelated `.omo/`, `.zcode/`, or `tem/` paths.

---

## Execution Notes

- Use an isolated worktree at execution time; this workspace already contains unrelated changes.
- Tasks 0 and 1 are evidence gates. Do not start production protocol edits before Task 1 passes; non-Ritsu Tail work may continue after Task 0 only if the evidence document explicitly scopes Ritsu as blocked, but the approved alpha release cannot be declared complete until the Ritsu gate is resolved or the design is revised.
- Every task ends in a focused commit and an independent review. Review the fixed diff on both standards and approved-spec axes.
- Golden bytes must be reviewed independently; expected bytes may not be generated by the production codec under test.
- Automated fixture compatibility does not replace a real `0.5.5` two-client start-run test.
- `scripts/verify-release.sh` validates temporary packages only. Tagging, pushing, and GitHub Pre-release creation require a separate explicit user request.
