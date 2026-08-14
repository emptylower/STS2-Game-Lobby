# STS2 LAN Connect v0.6 Dual Protocol Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship client and lobby-service `0.6.0-alpha.1` with a default `0.3.x-0.5.x` compatible `4/5-bit` room mode and an opt-in `tail_v1` room mode that keeps the STS2 body at vanilla `2/3-bit`, supports 2-8 players, and permits RitsuLib only when every peer has the same frozen RitsuLib presence.

**Architecture:** Install one atomic client protocol dispatcher at startup and freeze one immutable room profile/carrier before transport. Compat uses the historical codec. Tail rooms keep the vanilla `2/3-bit` body and use one carrier-neutral LAN protocol container: Ritsu-absent rooms append it as a standalone Tail; all-Ritsu rooms carry the exact container through RitsuLib's public typed-sidecar API and pair it with the vanilla message before any handler runs. The lobby service rejects presence/carrier mismatch before tickets. Pure direct-IP remains compat-only in alpha. LAN Connect never owns or reorders RitsuLib message-tail patches.

**Tech Stack:** Godot 4.5.1, .NET 9/C#, Harmony 2.4.x, xUnit 2.9, GdUnit4 5.0, Node 20, strict ESM TypeScript, Node `node:test`.

## Global Constraints

- Approved design: `docs/superpowers/specs/2026-08-13-v0-6-dual-protocol-design.md`.
- Client and lobby-service target version: `0.6.0-alpha.1`; keep `0.5.5` documented as the stable client.
- Default create mode: `compat_4_5_v1`; opt-in mode: `tail_v1`.
- `compat_4_5_v1` supports LAN Connect `0.3.x-0.6.x`, uses slot/list widths `4/5`, supports 2-8 players, and rejects any RitsuLib presence.
- `tail_v1` requires client capability >=0.6, keeps the STS2 body at slot/list widths `2/3`, carries the authoritative 2-8 player roster in LAN protocol container v1, and never uses the legacy `8/3` path.
- `legacy_4p` is fixture-only; production create/join/control requests from `<0.3.0` return `client_update_required` before a room or ticket is allocated.
- Room profile, selected protocol, carrier, and RitsuLib presence are frozen at create time and never renegotiated during the room lifetime.
- Ritsu-absent Tail uses `standalone_tail_v1`; Ritsu-present Tail uses `ritsulib_sidecar_v1`. The latter may call only public typed-sidecar/session/direct-`INetGameService` APIs. Never call `RitsuNetMessageTailExtensions.Write/Read`, enumerate private registries, reflect private patches, infer owner priority, or unpatch another owner.
- If real sidecar reachability, paired-message barrier, Android, or pre-ticket mismatch gates fail, stop the Ritsu task and return to design review. Complete extension inventory/classification is explicitly not required. Do not restore the RC4 bridge or independent double-Tail ordering.
- Pure direct-IP create/join exposes compat only in `alpha.1`; Tail must fail locally before transport and must not auto-switch profiles.
- LAN container and sidecar carrier layouts, limits, canonical entry ordering, reserved IDs, authority checks, and rejection codes are copied exactly from the approved design.
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
- Create `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectTailCodec.cs`: bounded carrier-neutral container encode/decode; no PacketWriter alignment ownership.
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
- Create `sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectRitsuLibSidecarCarrier.cs`: public-contract-only Ritsu carrier, pairing cache, and handler barrier.
- Create `sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectCapabilityDigest.cs`: canonical selection digest.

### Lobby service protocol domain

- Create `lobby-service/src/client-version.ts`: client version signal parsing/classification.
- Create `lobby-service/src/protocol-profile.ts`: canonical profile parsing and legacy DTO projection.
- Create `lobby-service/src/protocol-capabilities.ts`: deterministic protocol/RitsuLib-presence selection and digest.
- Create `lobby-service/src/protocol-errors.ts`: typed error codes/details.
- Create `lobby-service/src/room-api-view.ts`: canonical internal room to legacy/canonical API views.

### Fixtures and evidence

- Create `research/prototypes/v0.6-tail-ritsulib/`: tracked test harness source only; no third-party/game DLLs.
- Create `test-fixtures/protocol/v0.6/`: reviewed golden vectors and captured `0.5.5` DTO fixtures.
- Create `docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_FEASIBILITY_ZH.md`: feasibility evidence and gate verdicts.
- Create `docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_ACCEPTANCE_ZH.md`: platform/mode/player-count acceptance matrix.

---

### Task 0: Prove Standalone and Ritsu Sidecar Carriers Before Production Changes

**Files:**
- Create: `research/prototypes/v0.6-tail-ritsulib/Sts2TailPrototype.csproj`
- Create: `research/prototypes/v0.6-tail-ritsulib/RitsuInteropMatrixTests.cs`
- Create: `research/prototypes/v0.6-tail-ritsulib/PublicContractInspectionTests.cs`
- Create: `research/prototypes/v0.6-tail-ritsulib/RitsuInteropHarness.cs`
- Create: `research/prototypes/v0.6-tail-ritsulib/PublicRitsuContract.cs`
- Create: `research/prototypes/v0.6-tail-ritsulib/SidecarCarrierProbe.cs`
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
- Produces: a documented `PASS` or `BLOCKED` verdict for the standalone carrier, real two-process Ritsu typed-sidecar reachability and paired-message handler barrier, Android runtime behavior, and replacement of the old private resolver bridge. Production pre-ticket mismatch side effects are proved in Task 2 after the real store/app selection exists. Extension inventory/classification and independent double-Tail ordering are outside the revised design.

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

- [ ] **Step 2: Write carrier and side-effect red tests**

```csharp
[Fact]
public void Standalone_carrier_round_trips_the_frozen_container()
{
    CarrierResult result = RitsuInteropHarness.RoundTripStandalone();
    Assert.Equal(InteropFixtures.ExpectedContainer, result.ContainerBytes);
    Assert.InRange(result.ContainerStartBit - result.VanillaBodyEndBit, 0, 7);
    Assert.Equal(0, result.ContainerStartBit % 8);
    Assert.Equal(288, result.ContainerEndBit - result.ContainerStartBit);
    Assert.True(result.AlignmentPaddingWasZero);
}

[Fact]
public async Task Ritsu_sidecar_pairs_before_vanilla_handler()
{
    SidecarCarrierResult result = await RitsuInteropHarness.RunRealTwoProcessSidecarAsync();
    Assert.Equal(InteropFixtures.ExpectedContainer, result.ContainerBytes);
    Assert.True(result.TrustedTicketHintBootstrappedReachability);
    Assert.True(result.SidecarReachableBeforeFirstLanFlow);
    Assert.True(result.HandlerBlockedUntilPairValidated);
    Assert.True(result.VanillaBytesMatchFixture);
    Assert.False(result.StandaloneTailPresent);
    Assert.True(result.HintClearedOnTeardown);
    Assert.True(result.ReusedPeerIdStartsUnknown);
}

```

Define the prototype-only contracts in `RitsuInteropHarness.cs`:

```csharp
internal sealed record CarrierResult(
    byte[] ContainerBytes,
    long VanillaBodyEndBit,
    long ContainerStartBit,
    long ContainerEndBit,
    bool AlignmentPaddingWasZero);

internal sealed record SidecarCarrierResult(
    byte[] ContainerBytes,
    bool TrustedTicketHintBootstrappedReachability,
    bool SidecarReachableBeforeFirstLanFlow,
    bool HandlerBlockedUntilPairValidated,
    bool VanillaBytesMatchFixture,
    bool StandaloneTailPresent,
    bool HintClearedOnTeardown,
    bool ReusedPeerIdStartsUnknown);

internal static class InteropFixtures
{
    // A complete valid Tail v1 container, not just the magic. The byte table
    // and every offset are independently reviewed in the adjacent fixture JSON.
    internal static readonly byte[] ExpectedContainer = FixtureFiles.ReadBytes(
        "tail-probe-complete-v1.bin");
}

internal static class RitsuInteropHarness
{
    internal static CarrierResult RoundTripStandalone() =>
        throw new NotSupportedException("Implement the real standalone carrier.");

    internal static Task<SidecarCarrierResult> RunRealTwoProcessSidecarAsync() =>
        throw new NotSupportedException("Run two real game processes through the public sidecar API.");
}
```

The standalone test additionally asserts `ContainerStartBit >= VanillaBodyEndBit`, `ContainerStartBit - VanillaBodyEndBit <= 7`, `ContainerStartBit % 8 == 0`, `ContainerEndBit - ContainerStartBit == 288`, and `AlignmentPaddingWasZero`. The 36 fixture bytes begin at `ContainerStartBit`; alignment bits are carrier metadata and are never included in the sidecar payload.

The intentional `NotSupportedException` is the initial red state. A single-process manual `RegisterBytes/Write/Read` sequence cannot make this test green. The Ritsu case must launch two real game processes, create an explicit prototype-only trusted ticket/peer/flow binding, bootstrap first-message reachability with public `SetPeerReachabilityHint`, send the frozen container as a required typed message, pair it with a real vanilla message, and prove the production handler did not run early. Clear the hint back to `Unknown` during teardown and prove a later session epoch cannot reuse it. Do not use non-public binding flags.

Freeze `tail-probe-complete-v1.bin` to exactly these 36 bytes:

```text
53 54 53 4c 41 4e 30 31
01 00 00 00 00 1c 00 01 00 01
09 6c 61 6e 2e 70 72 6f 62 65
00 01 01 00 00 00 01 01
```

This is `STSLAN01`, container v1, flags 0, 28-byte container body, session protocol 1, one critical `lan.probe` v1 entry with payload `01`. Total length is 36 bytes and SHA-256 is `cfc9097350801026775fd333fb19c6758becbffd4142f58bab0884f4231f5cfa`. The exact same 36 bytes must appear directly in standalone mode and inside the Ritsu sidecar frame. The adjacent JSON fixes every field offset/value. Neither file may be generated by the prototype codec.

- [ ] **Step 3: Run the prototype and verify red until a real sidecar carrier exists**

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

Expected: the standalone test may pass; the two-process sidecar/barrier test fails until implemented. Method existence or manual cursor composition is not acceptable evidence. Do not claim lobby allocation behavior here; Task 2 owns that production integration gate.

- [ ] **Step 4: Assert the exact public sidecar contract without inventory or Tail-owner access**

```csharp
internal sealed record PublicRitsuContract(
    bool HasTypedRequiredDescriptor,
    bool HasDirectNetServiceSend,
    bool HasPublicReachabilityHint,
    bool HasSessionReachability,
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
        return new PublicRitsuContract(false, false, false, false, publicMembers);
    }
}

[Fact]
public void Public_api_exposes_required_typed_sidecar_contract_without_private_access()
{
    PublicRitsuContract contract = PublicRitsuContract.Load(typeof(RitsuLibFramework).Assembly);
    Assert.True(contract.HasTypedRequiredDescriptor);
    Assert.True(contract.HasDirectNetServiceSend);
    Assert.True(contract.HasPublicReachabilityHint);
    Assert.True(contract.HasSessionReachability);
    Assert.DoesNotContain(contract.ReferencedMembers, name => name.Contains("SerializePatch", StringComparison.Ordinal));
}
```

The false implementation is the initial red state. Set each flag only for the exact exported typed-message descriptor/registry, direct `INetGameService` send, public `SetPeerReachabilityHint`/`ObserveNetService`/`CanSendToPeer`, and session subscription APIs. Every accepted member must also be exercised in the two-process probe, including trusted-binding bootstrap and hint cleanup on disconnect/epoch change. Do not call or inspect `RitsuNetMessageTailExtensions`, extension inventories, private registrations, patch owners, or private resolver methods.

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

The probe DLL contains `[ModInitializer(nameof(Init))]`; the loader discovers the initializer from the DLL, not from a manifest `entrypoint`. The probe reads `sts2_lan_v06_probe_runtime.json` beside its DLL. Standalone mode writes and reads the frozen container through a real message-tail path. Ritsu mode starts two real game processes, establishes public sidecar reachability, sends the same container in a required typed message, pairs it with a vanilla message, and records whether the handler stayed blocked until validation. Fixed terminal marker schemas:

```text
standalone: {"phase":"standalone","carrier":"standalone_tail_v1","ritsuPresent":false,"passed":true,"sts2Version":"...","containerSha256":"cfc909...","containerLength":36,"vanillaBodyEndBit":123,"containerStartBit":128,"containerEndBit":416,"alignmentPaddingWasZero":true,"invalidProgram":false}

sidecar: {"phase":"sidecar","carrier":"ritsulib_sidecar_v1","ritsuPresent":true,"passed":true,"sts2Version":"...","ritsuManifestId":"STS2-RitsuLib","ritsuManifestVersion":"...","ritsuSelectedAssembly":"lib/<api>/STS2-RitsuLib.dll","containerSha256":"cfc909...","containerLength":36,"trustedTicketHintBootstrappedReachability":true,"sidecarReachableBeforeFirstLanFlow":true,"handlerBlockedUntilPairValidated":true,"vanillaBytesMatchFixture":true,"standaloneTailPresent":false,"hintClearedOnTeardown":true,"reusedPeerIdStartsUnknown":true,"invalidProgram":false}
```

Prefix each JSON with `STS2_LAN_V06_ANDROID_PROBE `. One clean terminal marker per fresh process is required. The verifier checks every required field/type and uniqueness. Mismatched presence is validated by Task 2's real service integration tests and must never enter either carrier.

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

`run_android_probe.sh` requires explicit device/package/MOD/log paths, the verified official Android Ritsu package tree/hash, the probe DLL/manifest, and a desktop peer command/address for the all-Ritsu sidecar case. It must validate every path before mutation. Each case must force-stop the app, remove the previous probe log, clear logcat, write a fresh runtime config beside the probe DLL, launch once, wait with a bounded poll for one terminal marker, force-stop again, and pull one uniquely named log. It must never reuse an append-only game log.

Run exactly three cases:

```text
android-standalone-write-read:
  Android without Ritsu; one real message path writes and reads the frozen container.

android-ritsu-sidecar-host:
  Android with verified Ritsu package; desktop with matching Ritsu is the client.
  Prove public sidecar reachability, frame-before-vanilla send, and host handler barrier.

android-ritsu-sidecar-client:
  Desktop with matching Ritsu is the host; Android with Ritsu is the client.
  Prove the same barrier in the reverse platform direction.
```

`run_android_probe.sh` must start/stop the paired desktop probe itself and record both endpoint logs. The desktop command and Android config share an explicit `flowNonce`; no implicit fixed port, directory, or `sender.bin` path is allowed. If the MOD directory/log requires emulator root, the configured bridge must already expose it. Missing device, peer launcher, paths, or official package evidence is `BLOCKED`, not permission to skip.

Verify all five fresh logs (standalone Android plus two Android/desktop pairs) with one deterministic parser invocation. The parser rejects missing/duplicate terminal markers, reused timestamps/flow nonces, carrier/presence drift, unexpected file locations, or unpaired endpoints.

`verify_android_probe.py` exits 0 only when exactly one terminal marker exists per freshly truncated process log, carrier/presence match, the inner container bytes match the reviewed fixture, standalone round-trip succeeds, a trusted test-ticket hint bootstraps reachability before the first LAN flow, the handler barrier holds, vanilla bytes stay unchanged, no standalone Tail appears in the Ritsu process, teardown clears the hint, reused peer IDs start `Unknown`, and every required Boolean is true. It exits nonzero for missing/duplicate markers, wrong STS2/Ritsu versions, presence drift, barrier leakage, stale hint state, byte drift, or any exception.

Before accepting each Ritsu marker, require it to report the loaded manifest ID/version, selected API assembly, `RitsuLibFramework.IsInitialized`, the registered descriptor opcode, and observed sidecar session/peer reachability. A copied but uninitialized DLL is `BLOCKED`, not “Ritsu present.”

`ANDROID_RITSULIB_PACKAGE_DIR` must be an unpacked official matching release asset or variant-pack `mods/STS2-RitsuLib/` directory, copied recursively without flattening. `package_tree_hash.py` hashes sorted relative paths plus every file SHA-256; compare it with `RITSULIB_PACKAGE_TREE_SHA256` recorded from the separately downloaded release asset. Preserve `mod_manifest.json`, root loader, `lib/<api-version>/`, variant manifest, and dependencies. The marker must report manifest ID/version, selected API assembly path/version, initialized framework, descriptor opcode and sidecar reachability; otherwise the gate is `BLOCKED`.

- [ ] **Step 7: Record Android carrier and handler-barrier evidence**

The probe payload/evidence must include:

```text
carrier and flow nonce
container SHA-256/length
sidecar descriptor opcode
trusted ticket/peer binding and public hint bootstrap timestamp
sidecar session/peer reachability timestamps
paired vanilla message kind/sequence
handler entered timestamp
standalone Tail present/absent
hint cleanup and reused-peer initial reachability
exception and inner exception, if any
```

Expected: identical inner LAN container bytes, sidecar reachability before the first frame, validated pair before handler entry, vanilla bytes unchanged, no standalone Tail in Ritsu cases, and no `InvalidProgramException`.

- [ ] **Step 8: Prove the public sidecar carrier replaces the private resolver bridge**

Use only public APIs that accept the actual `INetGameService`: observe the service, drive/await session reachability, register the required typed descriptor, send/receive the carrier frame, and dispose the subscription. Prove the old `RunManager` resolver transpiler is unnecessary and can be deleted. No message-tail coexistence test is allowed to substitute for this gate.

- [ ] **Step 9: Write the gate verdict**

The evidence document must contain this exact decision table:

```markdown
| Gate | Verdict | Evidence |
|---|---|---|
| Standalone carrier bytes/cursor | PASS/BLOCKED | real message path + raw offsets |
| All-Ritsu sidecar reachability/barrier | PASS/BLOCKED | two processes + public API + handler evidence |
| Android standalone/sidecar runtime | PASS/BLOCKED | device/build/fresh logs |
| Private resolver bridge removal | PASS/BLOCKED | direct `INetGameService` runtime result |
```

If the all-Ritsu sidecar/barrier, Android, or bridge-removal gate is `BLOCKED`, explicitly state: “RitsuLib homogeneous-room support is removed from the executable alpha scope pending design review; the RC4 private bridge and independent double-Tail remain prohibited.” Record Task 2's pre-ticket mismatch gate as a required downstream gate, not Task 0 PASS evidence. Extension inventory/classification is not a gate and must not be inspected.

- [ ] **Step 10: Commit prototype source and evidence only**

```bash
git add research/prototypes/v0.6-tail-ritsulib/Sts2TailPrototype.csproj research/prototypes/v0.6-tail-ritsulib/RitsuInteropMatrixTests.cs research/prototypes/v0.6-tail-ritsulib/PublicContractInspectionTests.cs research/prototypes/v0.6-tail-ritsulib/RitsuInteropHarness.cs research/prototypes/v0.6-tail-ritsulib/PublicRitsuContract.cs research/prototypes/v0.6-tail-ritsulib/SidecarCarrierProbe.cs research/prototypes/v0.6-tail-ritsulib/AndroidProtocolProbe.cs research/prototypes/v0.6-tail-ritsulib/Sts2TailAndroidProbe.csproj research/prototypes/v0.6-tail-ritsulib/sts2_lan_v06_probe.json research/prototypes/v0.6-tail-ritsulib/tail-probe-complete-v1.bin research/prototypes/v0.6-tail-ritsulib/tail-probe-complete-v1.json research/prototypes/v0.6-tail-ritsulib/verify_android_probe.py research/prototypes/v0.6-tail-ritsulib/package_tree_hash.py research/prototypes/v0.6-tail-ritsulib/run_android_probe.sh research/prototypes/v0.6-tail-ritsulib/README.md docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_FEASIBILITY_ZH.md
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
- Modify: `scripts/verify-release.sh`

**Interfaces:**
- Consumes: Task 1’s real `0.5.5` fixtures.
- Produces:
  - `classifyClientVersion(clientVersion?: string, modVersion?: string): ClientApiGeneration`
  - `parseRequestedProfile(input): ProtocolProfile`
  - `selectRoomProtocol(hostOffer, serverPolicy): RoomProtocolSelection`
  - `validateJoinOffer(selection, joinerOffer): void`
  - `toRoomApiView(room, generation): RoomApiView`
  - immutable `RoomProtocolSelection` and `capabilityDigest` stored on each room and ticket; each ticket also receives an independent 16-byte `protocolFlowNonce` projected as 32 lowercase hex.
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

- [ ] **Step 6: Write protocol selection and RitsuLib presence-parity tests**

```ts
test("freezes host RitsuLib presence and rejects a mismatched joiner", () => {
  const result = selectRoomProtocol(
    offer({ lan: [1, 2], ritsuLibPresent: true, ritsuLibSidecarAvailable: true }),
    serverPolicy({ lan: [1, 1] }),
  );
  assert.equal(result.selectedLanProtocolVersion, 1);
  assert.equal(result.ritsuLibPresent, true);
  assert.equal(result.carrier, "ritsulib_sidecar_v1");
  assert.throws(
    () => assertJoinerCompatible(result, offer({ lan: [1, 1], ritsuLibPresent: false })),
    hasCode("ritsulib_presence_mismatch"),
  );
  assert.match(result.capabilityDigest, /^[0-9a-f]{64}$/);
});
```

Also test both presence-mismatch directions, both matching-presence directions, Ritsu present with sidecar unavailable for host and joiner, empty LAN intersections, compat+Ritsu rejection, and mutation attempts after selection freeze. Do not add an extension whitelist or inspect RitsuLib registrations.

Add real `store.test.ts`/`app.integration.test.ts` cases with injected counters around room ID, slot, ticket, control binding, and transport/relay initializer creation. Both presence mismatch directions and both sidecar-unavailable roles must return their structured codes with every counter at zero. A pure `assertJoinerCompatible` unit test is necessary but not sufficient for this downstream gate.

- [ ] **Step 7: Implement immutable selection and digest**

For `compat_4_5_v1`, selection is protocol version `0`, carrier `none`, minimum client `0.3.0`, and RitsuLib absent. For `tail_v1`, select LAN version 1, freeze host presence, and deterministically select `standalone_tail_v1` or `ritsulib_sidecar_v1`; Ritsu present without sidecar readiness returns `ritsulib_sidecar_unavailable`. Encode canonical digest bytes exactly as revised design §4.3 (`LANSEL01`, schema/profile/version/carrier/max players, length-prefixed normalized versions/signature, then presence flags), then SHA-256 lowercase hex. Add reviewed compat, standalone, and Ritsu-sidecar cases to `capability-digest-v1.json`; TypeScript and Task 3 C# tests consume the same fixture.

- [ ] **Step 8: Refactor store internal types to canonical state**

Add `clientVersion`, `clientApiGeneration`, and `protocolSelection` to `Room`; copy generation, profile, selection, and joiner digest into `JoinTicket`. After every validation passes, generate a cryptographically random 16-byte `protocolFlowNonce`; expose its 32 lowercase hex form only to the join response and matching host control binding, never room lists or JavaScript numeric fields. Move validation before room/ticket/nonce allocation. Remove `resolveRoomProtocolProfile()` from `toRoomSummary()`; summaries copy frozen state only.

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

Use records and immutable values. Compat has LAN protocol `0` and carrier `None`; Tail v1 has selected protocol `1` and carrier `StandaloneTailV1` or `RitsuLibSidecarV1`, derived from frozen presence/readiness. Include profile, carrier, max players 2-8, minimum client, game version, wire-cache signature, frozen RitsuLib presence, and digest.

Define `LanConnectProtocolException` as an unretryable exception carrying a required non-null `LanConnectProtocolFailure Failure`. Define `LanConnectProtocolFailureMapper` as the only conversion seam for HTTP `LobbyServiceException`, Tail rejection codes 1-10 (including code 10 `ritsulib_sidecar_unavailable`), and local selection/codec failures; unknown codes retain their raw code. Change `LobbyJoinAttemptResult` to carry `ProtocolFailure`, add `LanConnectHostAttemptResult`, and add `ProtocolFailure` to `LanConnectModPreflightJoinResult`. `StartLobbyHostAsync`, `PublishExistingHostToLobbyAsync`, lobby join, and direct-IP join return these values instead of erasing them into `bool`/text. Lobby/direct join catches `LanConnectProtocolException` before candidate/broad catches, aborts all remaining candidates, preserves `Failure`, disconnects any current transport, and releases the tentative lease. Auto-rejoin clears `_pendingClientReconnect` and presents one structured message on protocol failure; it continues polling only for room-not-found or retryable transport failures. Cancellation remains cancellation, while unrelated exceptions retain existing internal-error behavior.

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
    RestartHarness harness = RestartHarness.WithProtocolFailure(Failure.RitsuLibPresenceMismatch(requiredPresent: true));
    await harness.RunAsync();
    Assert.Null(harness.PendingReconnect);
    Assert.Equal(1, harness.JoinAttempts);
    Assert.Single(harness.PresentedProtocolFailures);
}
```

- [ ] **Step 4: Write and pass the cross-runtime capability digest vectors**

Read `test-fixtures/protocol/v0.6/capability-digest-v1.json`, encode the exact revised design §4.3 layout, and assert the same expected SHA-256 hex as the TypeScript test. Include compat/none, Ritsu-absent/standalone, and Ritsu-present/sidecar vectors; prove carrier changes the digest.

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
type ProtocolOfferDto = {
  lanProtocolMin: number;
  lanProtocolMax: number;
  clientVersion: string;
  ritsuLibPresent: boolean;
  ritsuLibSidecarAvailable: boolean;
};
type ProtocolSelectionDto = {
  profile: "compat_4_5_v1" | "tail_v1";
  selectedLanProtocolVersion: number;
  carrier: "none" | "standalone_tail_v1" | "ritsulib_sidecar_v1";
  minimumClientVersion: string;
  maxPlayers: number;
  gameVersion: string;
  wireCacheSignature: string | null;
  ritsuLibPresent: boolean;
  capabilityDigest: string;
};
type ProtocolErrorDetailsDto = {
  requiredClientVersion?: string;
  requiredRitsuLibPresent?: boolean;
  detail?: string;
};
type JoinProtocolBindingDto = {
  protocolFlowNonce: string; // exactly 32 lowercase hex characters
};
```

Placement is fixed: create request adds `clientVersion`, `protocolProfileV2`, and `protocolOffer`; create response adds `protocolSelection`; room-list item adds `protocolProfileV2` and `protocolSelection`; join/preflight request adds `clientVersion` and `protocolOffer`; join response and matching host control event add the same `protocolFlowNonce`; control hello adds `clientVersion` and `capabilityDigest`; `LobbyErrorDetails` adds the three error-detail fields. Validate nonce as exactly 32 lowercase hex and decode to 16 bytes without numeric conversion. Legacy fields stay where Task 1 fixtures require them. Add the listed probe capabilities. Enforce all string bounds in both runtimes.

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

Pure direct-IP joins have no service ticket or carrier-neutral presence preflight. Keep them `compat_4_5_v1` only in `alpha.1`; do not expose a Tail intent. Local Ritsu presence returns `ritsulib_not_allowed_in_compat_mode` before creating any initializer. Change `LanConnectDirectJoinFlow.JoinAsync` from `Task<bool>` to `Task<LobbyJoinAttemptResult>` and update `Patches.JoinFriendScreen` to present `ProtocolFailure` through `LanConnectProtocolUiMessages`; do not create a generic popup for a known protocol failure. Add tests proving zero transport attempts for local Ritsu and that no code path parses Tail selection from direct-IP `InitialGameInfoMessage`.

- [ ] **Step 10: Persist the selection for continue-run**

Increment the saved-room binding schema and persist canonical profile, selected LAN version, carrier, frozen RitsuLib presence, and digest. Continue-run publication reuses this selection; it cannot call a profile-by-player-count helper.

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

### Task 4: Implement the Carrier-Neutral LAN Protocol Container v1

**Files:**
- Create: `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectTailEntry.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectTailEnvelope.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectTailCodec.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectTailCodecGoldenVectorTests.cs`
- Create: `test-fixtures/protocol/v0.6/*.bin`
- Create: `test-fixtures/protocol/v0.6/*.json`

**Interfaces:**
- Consumes: only byte spans and the approved container schema; it has no PacketReader/PacketWriter or Ritsu dependency.
- Produces:
  - `LanConnectTailCodec.Encode(ushort sessionProtocolVersion, IReadOnlyList<LanConnectTailEntry>): byte[]`
  - `LanConnectTailCodec.Decode(ReadOnlySpan<byte>): LanConnectTailEnvelope`
  - exact container-byte ownership for both carriers; it neither reads nor writes standalone alignment padding.

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

Enforce `STSLAN01`, version 1, flags 0, network byte order, 256 KiB total, 32 entries, 64 KiB each, 64-byte nonempty IDs, critical flag bit only, UTF-8 ordinal entry ordering, duplicate rejection, checked lengths, and exact container consumption. The codec starts at magic and knows nothing about PacketWriter alignment. Standalone placement/padding and Ritsu sidecar framing belong to Tasks 5 and 9 respectively.

- [ ] **Step 5: Add adversarial envelope tests**

Cover bad magic/version/flags, duplicate IDs, out-of-order input canonicalization, oversized count/ID/payload/container, integer overflow, truncation at every header boundary, unknown critical entry, unknown noncritical skip, and illegal LAN trailing bytes. Nonzero standalone alignment padding is a Task 5 carrier test, not a container-codec case.

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
- Create: `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectStandaloneTailCarrier.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectCapabilitiesCodec.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectRosterCodec.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Tail/LanConnectRejectionCodec.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectRosterProjection.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/LanConnectRosterAuthorityState.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectAtomicPatchDispatcherTests.cs`
- Create: `sts2-lan-connect.Tests/Protocol/LanConnectStandaloneTailCarrierTests.cs`
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
- Produces: one Harmony owner installed once; compat `4/5` dispatch; Ritsu-absent standalone Tail outer hooks; carrier-neutral capability/roster/rejection semantics; and normal join validation before the original handler. Until Task 9 installs the sidecar carrier, any Ritsu-present selection fails closed with `ritsulib_sidecar_unavailable`.

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

Use the pinned target STS2 assembly to identify closed `NetMessageBus.SerializeMessage<T>` and the outer `TryDeserializeMessage` path for `InitialGameInfoMessage`, all three request/response pairs, built-in `ClientConnectionFailedMessage`, `PlayerJoinedMessage`, and begin-run. Record the verified assembly facts: all three response structs have no success/failure discriminator; LoadJoin/Rejoin deserialize `SerializableRun` before any container; `ClientConnectionFailedMessage` is built-in ID 34 with bounded vanilla fields. Treat responses as success-only and use failure message + carrier-provided LAN container as the protocol rejection path. Task 5 implements only standalone placement but exposes pre-handler pairing seams used by Task 9. Register handlers before connection, unregister in `finally`, and validate every required target/handler atomically.

`LanConnectStandaloneTailCarrier` alone owns PacketWriter/PacketReader byte alignment: it writes at most seven zero bits after the vanilla body, records the magic offset, appends the exact Task 4 container bytes, and rejects nonzero padding or any cursor/length mismatch. Its tests prove the extracted bytes from magic through container end equal the carrier-neutral fixture exactly.

- [ ] **Step 4: Write red capabilities codec tests and run them red**

Test peer offer record 1 and session selection record 2, presence/readiness flags, carrier enum bounds, presence-to-carrier invariants, session version 0 for join request, selected version 1 for room messages, HTTP/container selection equality, and all duplicate version-field equality rules.

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

Before implementing each codec, write and run its failing tests. Roster vectors cover 2-8 player metadata/child bit payloads, canonical projection, identity/slot uniqueness, authority/revision, limits, bootstrap baseline, and exact child consumption. Rejection vectors cover codes 1-10 and bounds. Standalone full-message rejection vectors contain built-in ID 34 + sender + vanilla failure body + LAN container selection/rejection; they forbid roster/other entries, non-host sender, wrong waiting phase, and duplicate delivery. Separately assert Ritsu-present selection cannot enter standalone serialization.

- [ ] **Step 9: Implement the minimal roster/rejection/projection seam and make focused tests green**

Implement only what the normal join response needs. Keep LoadJoin/Rejoin current-snapshot rules in Task 6 and PlayerJoined mutation checks in Task 7.

- [ ] **Step 10: Write failing successful join-response and LAN rejection full-message tests**

`ClientLobbyJoinResponseMessage` must contain session selection + full roster and forbid rejection. `ClientConnectionFailedMessage` with LAN Tail must contain session selection + rejection, forbid roster/other LAN entries, and be accepted only from the host while one of the three request completions is pending. Missing/forbidden entries, wrong record kinds, body/Tail mismatch, non-host authority, malformed Tail, and rejection outside a pending request must prevent every original response handler from receiving a success value.

- [ ] **Step 11: Implement Tail join-response projection and restoration**

For `standalone_tail_v1`, write the vanilla four-player projection with original `2/3` widths, append the LAN container, and bootstrap/restore immediately after outer deserialization but before the managed response task completes. Initialize host revision at 1. A valid rejection completes the pending request with `LanConnectProtocolException`, disconnects, and makes later success ineligible. Compat remains `4/5`; Ritsu-sidecar selection fails closed here and is enabled only by Task 9.

- [ ] **Step 12: Remove the RC4 private composition tests and code**

Delete `RitsuLibAssemblyName`, detached postfix state/delegate/record, `DetachRitsuBeginRunPostfix`, `CreateBeginRunPostfixInvoker`, restore logic, and tests asserting third-party detach/restore. Replace them with regression tests asserting no external `Unpatch` and no private postfix references.

- [ ] **Step 13: Run xUnit and real-assembly GdUnit integration tests**

```bash
DOTNET_ROOT="/Users/mac/.dotnet" /Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj \
  --filter "FullyQualifiedName~LanConnectAtomicPatchDispatcherTests|FullyQualifiedName~LanConnectStandaloneTailCarrierTests|FullyQualifiedName~LanConnectInitialGameInfoTailTests|FullyQualifiedName~LanConnectJoinRequestTailTests|FullyQualifiedName~LanConnectJoinResponseTailTests|FullyQualifiedName~LanConnectCapabilitiesCodecTests|FullyQualifiedName~LanConnectRoster|FullyQualifiedName~LanConnectRejection|FullyQualifiedName~LanConnectSerializationPatchesCompatibilityTests" -m:1

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
git add sts2-lan-connect/Scripts/Entry.cs sts2-lan-connect/Scripts/LanConnectMultiplayerCompatibility.cs sts2-lan-connect/Scripts/LanConnectSerializationPatches.cs sts2-lan-connect/Scripts/Protocol/Patches/LanConnectProtocolPatchDispatcher.cs sts2-lan-connect/Scripts/Protocol/Patches/LanConnectCompatWirePatches.cs sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs sts2-lan-connect/Scripts/Protocol/Tail/LanConnectStandaloneTailCarrier.cs sts2-lan-connect/Scripts/Protocol/Tail/LanConnectCapabilitiesCodec.cs sts2-lan-connect/Scripts/Protocol/Tail/LanConnectRosterCodec.cs sts2-lan-connect/Scripts/Protocol/Tail/LanConnectRejectionCodec.cs sts2-lan-connect/Scripts/Protocol/LanConnectRosterProjection.cs sts2-lan-connect/Scripts/Protocol/LanConnectRosterAuthorityState.cs sts2-lan-connect/Scripts/Lobby/LanConnectLobbyManagedJoinFlow.cs sts2-lan-connect/Scripts/Lobby/LanConnectLobbyJoinFlow.cs sts2-lan-connect/Scripts/Lobby/LanConnectDirectJoinFlow.cs sts2-lan-connect.Tests/Patches/LanConnectSerializationPatchesCompatibilityTests.cs sts2-lan-connect.Tests/Protocol/LanConnectAtomicPatchDispatcherTests.cs sts2-lan-connect.Tests/Protocol/LanConnectStandaloneTailCarrierTests.cs sts2-lan-connect.Tests/Protocol/LanConnectInitialGameInfoTailTests.cs sts2-lan-connect.Tests/Protocol/LanConnectJoinRequestTailTests.cs sts2-lan-connect.Tests/Protocol/LanConnectJoinResponseTailTests.cs sts2-lan-connect.Tests/Protocol/LanConnectCapabilitiesCodecTests.cs sts2-lan-connect.Tests/Protocol/LanConnectRosterCodecGoldenVectorTests.cs sts2-lan-connect.Tests/Protocol/LanConnectRosterProjectionTests.cs sts2-lan-connect.Tests/Protocol/LanConnectRejectionCodecTests.cs sts2-lan-connect.GdUnitTests/Protocol/LanConnectTailMessageBusTests.cs sts2-lan-connect.GdUnitTests/Protocol/LanConnectRosterRestoreTests.cs test-fixtures/protocol/v0.6/tail-initial-game-info-v1.bin test-fixtures/protocol/v0.6/tail-initial-game-info-v1.json test-fixtures/protocol/v0.6/tail-lobby-join-request-v1.bin test-fixtures/protocol/v0.6/tail-lobby-join-request-v1.json test-fixtures/protocol/v0.6/tail-lobby-join-response-v1.bin test-fixtures/protocol/v0.6/tail-lobby-join-response-v1.json test-fixtures/protocol/v0.6/tail-protocol-rejection-v1.bin test-fixtures/protocol/v0.6/tail-protocol-rejection-v1.json
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

Create reviewed `.bin + .json` vectors for `ClientLoadJoinRequestMessage`, successful `ClientLoadJoinResponseMessage`, `ClientRejoinRequestMessage`, successful `ClientRejoinResponseMessage`, and built-in `ClientConnectionFailedMessage` + rejection container while each request phase is pending. Each success JSON fixes vanilla-body boundary, standalone container offsets, selection/offer record kind, authority peer, roster revision, bootstrap carrier mapping, projection, and exact `2/3-bit` body widths. The rejection JSON fixes ID 34, vanilla failure-body boundary, and standalone container boundary. Task 9 reuses the exact container bytes inside sidecar frames. Expected bytes may not come from production encoders.

- [ ] **Step 2: Write request matrix tests red**

Both requests require `sessionProtocolVersion=0` and peer offer, and forbid roster/rejection. Test transport-sender identity, ticket/selection/carrier compatibility, malformed/missing container, wrong record kind, and capability change after ticket. Prove invalid requests never reach the original handlers.

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

Tail mode sends a safe vanilla player/projection plus carrier-neutral session selection + full snapshot; standalone appends it, while Task 9 sidecar pairs it. Compat remains `4/5`. The receiver validates host, peer, revision, projection, and carrier, then restores full state before the original handler. On failure, block the handler and disconnect with a structured protocol failure.

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

### Task 8: Complete the Standalone Begin-Run Flow and Remove the RC4 Bridge

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
- Produces: final host snapshot semantics, exact standalone-container restoration before the original begin-run handler, reusable pairing seam for Task 9, and no RC4 private Ritsu composition code.

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

Delete the old private resolver bridge only after Task 0 proves direct public `INetGameService` sidecar operation; Task 9 installs its replacement carrier.

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

### Task 9: Enforce Presence Parity and Implement the Public Ritsu Sidecar Carrier

**Precondition:** Task 0’s standalone bytes, two-process sidecar reachability/barrier, private bridge removal, and Android gates are `PASS`; Task 2's real store/app tests prove both presence mismatch directions and sidecar-unavailable host/joiner paths allocate zero slot/ticket/control/transport. If not, do not execute this task.

**Files:**
- Create: `sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectRitsuLibSidecarCarrier.cs`
- Create: `sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectSidecarPairingCache.cs`
- Create: `sts2-lan-connect.GdUnitTests/Protocol/LanConnectRitsuSidecarCarrierTests.cs`
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
- Produces: canonical presence/carrier offers and selections, pre-ticket mismatch/readiness rejection, the design §4.2.1 public typed-sidecar frame, bounded pairing cache, and a fail-closed handler barrier for all ten message kinds. It never appends a standalone LAN Tail in Ritsu rooms and never inventories RitsuLib extensions.

- [ ] **Step 1: Add a test-only real Ritsu assembly input without packaging it**

Configure the GdUnit test project to require `$(RitsuLibAssembly)` for this test class. If the property is absent, the test must fail with an explicit missing-gate message, not skip. Do not add the DLL to package scripts, the solution output allowlist, or Git.

- [ ] **Step 2: Write carrier, pairing, and barrier tests with the production adapter**

Assert identical inner LAN container bytes for standalone and sidecar carriers. For each of the ten message kinds, cover frame-first, vanilla-first within bounds, duplicate/conflicting frame, wrong sender/recipient/flow/message kind/sequence, per-direction counters starting at 1, sequence exhaustion, timeout, cache limits, and handler release only after full validation. Allow only cross-stream interleaving; reject reordering or gaps inside either ordered stream, and prove the receiver pairs the next ordinal rather than an arbitrary same-kind message. For `PlayerJoinedMessage` and `LobbyBeginRunMessage`, use at least three peers with distinct flow nonces: submit one frame per recipient, assert identical inner containers, then send one vanilla broadcast; missing binding or one failed submission must not leak the broadcast to an unpaired handler. Assert vanilla bytes remain exact fixtures and `standaloneTailPresent=false` for Ritsu selection. Assert both mixed-presence directions fail before any carrier or transport allocation.

- [ ] **Step 3: Implement the public sidecar carrier exactly as proven**

Expose explicit readiness outcomes: framework absent, framework present and required typed-sidecar ready, and framework present but sidecar unavailable. Readiness requires the exact public descriptor/registry, direct `INetGameService` send, `SetPeerReachabilityHint`/`ObserveNetService`/`CanSendToPeer`, and session subscription APIs proven in Task 0. The unavailable outcome blocks host, joiner, load join and rejoin locally with `ritsulib_sidecar_unavailable`. Do not inspect Tail registrations or private patch state.

- [ ] **Step 4: Enforce frozen presence in client and service gates**

The host submits presence plus carrier readiness; the service freezes `standalone_tail_v1` or `ritsulib_sidecar_v1` into selection/digest. A joiner must match presence and support the carrier before slot/ticket allocation. Both mismatch directions return `ritsulib_presence_mismatch`; readiness failure returns `ritsulib_sidecar_unavailable`. Immediately before transport and before each sidecar flow, every participant rechecks detected presence/readiness against the frozen selection so post-ticket drift fails closed. LAN Connect does not compare RitsuLib versions or extensions.

- [ ] **Step 5: Replace the private resolver bridge and install the paired-message barrier**

Delete the transpiler/private resolver patch. Register the required descriptor once and pass the active `INetGameService` directly. Only after validating the lobby ticket, nonce, transport peer and frozen selection, seed public reachability with `SetPeerReachabilityHint(peer, Supported)`, observe the service, require `CanSendToPeer`, send the §4.2.1 frame before vanilla, and pair by sender/recipient/flow/message kind/per-direction sequence. Clear the hint to `Unknown` plus every pairing counter/cache on ticket cancellation, disconnect and session epoch change; prove peer-ID reuse cannot inherit it. Preserve order inside each stream and tolerate only bounded interleaving between streams. For host broadcasts, submit a recipient-specific frame for every target before the single vanilla broadcast; never share a flow nonce. Keep at most 16 pairs per peer, 256 KiB of sidecar frame/container per pair, and 5 seconds; do not copy or count the full vanilla `SerializableRun` payload against this cache. A timeout/conflict blocks the original handler and disconnects. If any public prerequisite is unavailable, fail closed; never fall back to independent double-Tail. Direct-IP must never seed a manual hint.

- [ ] **Step 6: Run real-assembly tests and service gates**

```bash
DOTNET_ROOT="/Users/mac/.dotnet" PATH="/Users/mac/.dotnet:$PATH" \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
/Users/mac/.dotnet/dotnet test \
  sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj \
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings \
  -p:RitsuLibAssembly="$RITSULIB_ASSEMBLY" \
  --filter "FullyQualifiedName~LanConnectRitsuSidecarCarrierTests" -m:1

cd lobby-service
npm run build
node --test dist/protocol-capabilities.test.js dist/store.test.js dist/app.integration.test.js
```

- [ ] **Step 7: Repeat Windows and Android homogeneous/mismatch smokes**

Record startup, ticket, carrier, descriptor opcode/reachability, paired frame/vanilla sequence, handler-release time, begin-run, and synchronized state for both homogeneous combinations. Record zero slot/ticket/transport counters for both mixed-presence directions and readiness failures for unsupported host/joiner. Any early handler, standalone Tail in a Ritsu message, vanilla/container byte drift, missed rejection, or `InvalidProgramException` blocks this task.

- [ ] **Step 8: Commit public Ritsu integration**

```bash
git add sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectRitsuLibSidecarCarrier.cs sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectSidecarPairingCache.cs sts2-lan-connect/Scripts/Protocol/Capabilities/LanConnectExternalCapabilityCollector.cs sts2-lan-connect/Scripts/Protocol/Patches/LanConnectTailMessagePatches.cs sts2-lan-connect/Scripts/LanConnectExternalModDetection.cs sts2-lan-connect/Scripts/Lobby/LanConnectRitsuLibLobbyCompatibility.cs sts2-lan-connect/Scripts/LanConnectGameplayPatches.cs sts2-lan-connect.GdUnitTests/sts2_lan_connect.GdUnitTests.csproj sts2-lan-connect.GdUnitTests/Protocol/LanConnectRitsuSidecarCarrierTests.cs lobby-service/src/protocol-capabilities.ts lobby-service/src/protocol-capabilities.test.ts lobby-service/src/store.ts docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_FEASIBILITY_ZH.md
git commit -m "feat(protocol): enforce homogeneous RitsuLib rooms"
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
- Produces: default compat selector and opt-in Tail selector for lobby rooms, 2-8 player input, pre-create local validation, room protocol/carrier/Ritsu requirement display, compat-only direct-IP UI, and messages for every defined rejection code across all flows.

- [ ] **Step 1: Write red UI state tests**

Assert lobby create defaults to compat, profile does not change with player count, Tail warning states “仅支持 0.6+；RitsuLib 状态必须一致”, compat warning states “支持 0.3-0.5，不支持 RitsuLib”, room cards expose carrier plus “需要 RitsuLib” or “无 RitsuLib”, direct-IP exposes compat only, and room profile/carrier/presence cannot change after submission.

- [ ] **Step 2: Add the protocol selector to the existing create dialog**

Add `_protocolProfileOption`, description label, and warning label in the existing dialog flow. Keep edits localized. Options:

```text
兼容旧版客户端（默认）
0.6 新协议（RitsuLib 状态必须一致）
```

Max players becomes 2-8 and no longer determines profile.

Keep direct-IP fixed to compat in `alpha.1`. Do not render a Tail choice or infer/retry another profile. When local Ritsu is detected, reject before `_actionInFlight` and before any transport initializer with the structured compat error.

- [ ] **Step 3: Gate create locally before `_actionInFlight`**

Compat+Ritsu rejects locally. Tail requires a complete current offer containing detected presence and sidecar readiness, selected profile/carrier availability, and compatible lobby probe fields. Ritsu present but sidecar unavailable returns `ritsulib_sidecar_unavailable`; it must block host and joiner paths. Create passes an explicit intent to `LanConnectHostFlow`; returned selection must preserve presence and deterministically select the expected carrier.

- [ ] **Step 4: Implement stable structured error messages**

Map all service string codes and Tail numeric codes to one enum/value. Use structured required client version and required RitsuLib presence fields. Keep raw `detail` in logs only. Unknown codes show a generic protocol rejection and preserve the numeric/string code in diagnostics.

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

Document the two lobby create modes, direct-IP compat-only limitation, 0.2 end of support, 0.3-0.5 compat requirement, compat Ritsu rejection, Tail 0.6 minimum, standalone versus Ritsu-sidecar carriers, presence-parity/readiness rules, lack of Ritsu version/extension guarantees, update/restart steps, expected profile/carrier/digest/revision logs, and known alpha limitations.

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
  --settings sts2-lan-connect.GdUnitTests/gdunit4.runsettings \
  -p:RitsuLibAssembly="$RITSULIB_ASSEMBLY" -m:1
```

The command must fail before test discovery when `RITSULIB_ASSEMBLY` is absent or invalid; real-Ritsu carrier tests may not be conditionally skipped in the final gate.

- [ ] **Step 7: Build and run the complete temporary-package release gate**

```bash
export PATH="/Users/mac/.dotnet:$PATH" DOTNET_ROOT="/Users/mac/.dotnet"
test -f "$RITSULIB_ASSEMBLY"
DOTNET_BIN="/Users/mac/.dotnet/dotnet" \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
RITSULIB_ASSEMBLY="$RITSULIB_ASSEMBLY" \
bash scripts/verify-release.sh
```

Expected: service checks/tests, xUnit, GdUnit including mandatory real-Ritsu sidecar carrier tests, client/service package allowlists, installer dry-run, legal files, and fresh package reproducibility all pass. No Ritsu/game/prototype DLL appears in public packages.

- [ ] **Step 8: Execute and record the real platform acceptance matrix**

Required smoke cases:

```text
Windows <-> Windows
Windows <-> Android
Windows <-> macOS
Android <-> macOS
Tail success: no Ritsu/no Ritsu and Ritsu/Ritsu
Tail rejection: Ritsu host/no-Ritsu joiner and no-Ritsu host/Ritsu joiner before ticket
Tail readiness rejection: Ritsu host or joiner with sidecar unavailable before transport
Direct-IP: compat only; local Ritsu and any Tail intent rejected before transport
Tail continuation: InLoadedLobby load join and Running rejoin, including one 5+ player case
Compat: 0.6 <-> real 0.5.5
Compat: Ritsu host rejection and Ritsu joiner rejection
Player counts: 2/4/5/8 across principal smokes
Player counts: 3/6/7 each in at least one cross-platform run
```

Each new-lobby success requires ticket, connection, ready, begin-run, first synchronized game state, and no divergence/timeout. Each continuation success requires ticket, accepted LoadJoin/Rejoin response, full real-slot roster restoration, resumed state, and no retry after injected protocol rejection. Each mismatch/readiness case must prove zero ticket/slot/transport allocation. Direct-IP exclusions must prove zero initializer calls. Attach both endpoint logs and record profile, carrier, selected protocol, digest, roster revision, standalone cursor or sidecar frame/handler timestamps, and frozen/detected presence/readiness.

- [ ] **Step 9: Record the go/no-go verdict**

`GO` requires every revised acceptance criterion, including Task 0 sidecar/barrier gates, both mismatch directions, readiness failures, and Android evidence. A failed all-Ritsu sidecar/barrier or Android gate means `0.6.0-alpha.1` is incomplete and requires design review; do not quietly ship “Ritsu unsupported” under the revised release notes.

- [ ] **Step 10: Commit version, docs, and verification evidence**

```bash
git add sts2-lan-connect/sts2_lan_connect.csproj sts2-lan-connect/sts2_lan_connect.json lobby-service/package.json lobby-service/package-lock.json lobby-service/README.md sts2-lan-connect.Tests/Packaging/LanConnectPackageContentTests.cs lobby-service/src/package-content.test.ts scripts/verify-release.sh README.md CHANGELOG.md docs/CLIENT_RELEASE_README_ZH.md docs/STS2_LAN_CONNECT_USER_GUIDE_ZH.md docs/STEAM_WORKSHOP_DESCRIPTION_ZH.txt docs/RELEASE_NOTES_V0.6.0_ALPHA1_ZH.md docs/testing/STS2_LAN_CONNECT_V0.6_ALPHA1_ACCEPTANCE_ZH.md
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
