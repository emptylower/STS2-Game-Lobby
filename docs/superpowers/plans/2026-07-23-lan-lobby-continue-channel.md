# LAN / Lobby Continue Channel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist host creation channel (`lan` vs `lobby`) on multiplayer save bindings so pure-LAN continue-run never auto-publishes a lobby room, while lobby-created saves keep auto-republish.

**Architecture:** Add `HostChannel` on `LanConnectSavedRoomBinding`, pure helpers for resolve/decision, register host origin on LAN/lobby create in `LanConnectLobbyRuntime`, persist on `SaveManager.Saved`, and gate `LanConnectContinueRunLobbyAutoPublisher` before `POST /rooms`. Missing/unknown channel defaults to `lobby` for backward compatibility.

**Tech Stack:** C# 12, .NET 9, Godot 4.5 client mod, xUnit in `sts2-lan-connect.Tests`.

**Approved spec:** `docs/superpowers/specs/2026-07-23-lan-lobby-continue-channel-design.md`

**Branch:** `fix/lan-lobby-continue-channel` (already created; design commit present)

---

## File Map

| File | Responsibility |
|------|----------------|
| Create `sts2-lan-connect/Scripts/Lobby/LanConnectHostChannels.cs` | Constants + `Resolve` / `IsValid` / `Describe` |
| Create `sts2-lan-connect/Scripts/Lobby/LanConnectContinueRunPublishDecision.cs` | Pure decision from effective channel → `publish` / `skip_lan_origin` |
| Modify `sts2-lan-connect/Scripts/Lobby/LanConnectMultiplayerSaveRoomBinding.cs` | `HostChannel` on binding; `PersistHostBinding(..., hostChannel, source)`; keep or obsolete old `PersistBinding` as lobby wrapper |
| Modify `sts2-lan-connect/Scripts/LanConnectConfig.cs` | `CloneBinding` copies `HostChannel`; do **not** rewrite empty channel on normalize |
| Modify `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyRuntime.cs` | Host origin state; `RegisterHostOrigin`; `OnRunSaved` branch; disconnect clear |
| Modify `sts2-lan-connect/Scripts/LanConnectHostFlow.cs` | After successful LAN ENet start → register `lan` origin |
| Modify `sts2-lan-connect/Scripts/Lobby/LanConnectContinueRunLobbyAutoPublisher.cs` | Resolve channel; skip publish when `lan`; log decision |
| Modify `sts2-lan-connect/Scripts/Lobby/LanConnectSaveDiagnostics.cs` | `bindingHostChannel` + `effectiveHostChannel` |
| Modify `sts2-lan-connect/Scripts/Lobby/LanConnectMultiplayerSaveCompatibility.cs` | Abandon removes binding by saveKey when possible |
| Create `sts2-lan-connect.Tests/Lobby/LanConnectHostChannelsTests.cs` | Resolve / valid / decision tests |
| Create `sts2-lan-connect.Tests/Lobby/LanConnectHostChannelBindingTests.cs` | Persist host channel + clone round-trip if testable without Godot SaveManager |

**Do not edit:** `releases/`, service DTO/API, join slot / `NotInSaveGame` logic.

**Commands (repo root `/Users/mac/Desktop/STS2-Game-Lobby`):**

```bash
export PATH="/Users/mac/.dotnet:$PATH"
export DOTNET_ROOT="/Users/mac/.dotnet"
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj --filter "FullyQualifiedName~LanConnectHostChannel"
# full client build when tasks that touch host/runtime done:
DOTNET_BIN=/Users/mac/.dotnet/dotnet \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
bash scripts/build-sts2-lan-connect.sh
```

---

### Task 1: Host channel constants and pure decision helper

**Files:**
- Create: `sts2-lan-connect/Scripts/Lobby/LanConnectHostChannels.cs`
- Create: `sts2-lan-connect/Scripts/Lobby/LanConnectContinueRunPublishDecision.cs`
- Create: `sts2-lan-connect.Tests/Lobby/LanConnectHostChannelsTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectHostChannelsTests
{
    [Theory]
    [InlineData("lan", "lan")]
    [InlineData("LAN", "lan")]
    [InlineData("lobby", "lobby")]
    [InlineData("Lobby", "lobby")]
    [InlineData(null, "lobby")]
    [InlineData("", "lobby")]
    [InlineData("   ", "lobby")]
    [InlineData("steam", "lobby")]
    public void Resolve_maps_values_to_effective_channel(string? input, string expected)
    {
        Assert.Equal(expected, LanConnectHostChannels.Resolve(input));
    }

    [Theory]
    [InlineData("lan", true)]
    [InlineData("lobby", true)]
    [InlineData("", false)]
    [InlineData("steam", false)]
    public void IsValid_only_allows_lan_and_lobby(string input, bool expected)
    {
        Assert.Equal(expected, LanConnectHostChannels.IsValid(input));
    }

    [Fact]
    public void DecideContinueRunPublish_skips_lan_only()
    {
        Assert.Equal(
            LanConnectContinueRunPublishDecisionKind.SkipLanOrigin,
            LanConnectContinueRunPublishDecision.Decide(LanConnectHostChannels.Lan));
        Assert.Equal(
            LanConnectContinueRunPublishDecisionKind.Publish,
            LanConnectContinueRunPublishDecision.Decide(LanConnectHostChannels.Lobby));
        Assert.Equal(
            LanConnectContinueRunPublishDecisionKind.Publish,
            LanConnectContinueRunPublishDecision.Decide(LanConnectHostChannels.Resolve(null)));
    }
}
```

- [ ] **Step 2: Run tests — expect fail (types missing)**

```bash
export PATH="/Users/mac/.dotnet:$PATH" && export DOTNET_ROOT="/Users/mac/.dotnet"
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj --filter "FullyQualifiedName~LanConnectHostChannelsTests" --no-restore 2>&1 | tail -40
# if restore needed:
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj --filter "FullyQualifiedName~LanConnectHostChannelsTests"
```

Expected: compile error or fail finding `LanConnectHostChannels`.

- [ ] **Step 3: Implement helpers**

`LanConnectHostChannels.cs`:

```csharp
using System;
using Godot;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectHostChannels
{
    public const string Lan = "lan";
    public const string Lobby = "lobby";

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().ToLowerInvariant();
        return normalized is Lan or Lobby;
    }

    /// <summary>
    /// Missing/empty/unknown → lobby (compat). Unknown non-empty logs warning.
    /// </summary>
    public static string Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Lobby;
        }

        string normalized = value.Trim().ToLowerInvariant();
        if (normalized is Lan or Lobby)
        {
            return normalized;
        }

        GD.Print($"sts2_lan_connect host_channel: unknown value '{value}', treating as lobby");
        return Lobby;
    }

    public static string DescribePersisted(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<missing>" : value.Trim();
    }
}

internal enum LanConnectContinueRunPublishDecisionKind
{
    Publish,
    SkipLanOrigin
}

internal static class LanConnectContinueRunPublishDecision
{
    public static LanConnectContinueRunPublishDecisionKind Decide(string effectiveHostChannel)
    {
        return string.Equals(effectiveHostChannel, LanConnectHostChannels.Lan, StringComparison.Ordinal)
            ? LanConnectContinueRunPublishDecisionKind.SkipLanOrigin
            : LanConnectContinueRunPublishDecisionKind.Publish;
    }

    public static string ToLogToken(LanConnectContinueRunPublishDecisionKind decision)
    {
        return decision == LanConnectContinueRunPublishDecisionKind.SkipLanOrigin
            ? "skip_lan_origin"
            : "publish";
    }
}
```

Put `LanConnectContinueRunPublishDecision` either in the same file or split into `LanConnectContinueRunPublishDecision.cs` as preferred; keep both in `Sts2LanConnect.Scripts`.

- [ ] **Step 4: Run tests — expect pass**

```bash
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj --filter "FullyQualifiedName~LanConnectHostChannelsTests"
```

Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add sts2-lan-connect/Scripts/Lobby/LanConnectHostChannels.cs \
  sts2-lan-connect/Scripts/Lobby/LanConnectContinueRunPublishDecision.cs \
  sts2-lan-connect.Tests/Lobby/LanConnectHostChannelsTests.cs
git commit -m "feat(client): add host channel resolve and continue-run decision helpers"
```

---

### Task 2: Persist `HostChannel` on save bindings

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectMultiplayerSaveRoomBinding.cs`
- Modify: `sts2-lan-connect/Scripts/LanConnectConfig.cs` (`CloneBinding`)
- Create: `sts2-lan-connect.Tests/Lobby/LanConnectHostChannelBindingTests.cs` (optional pure tests of normalize/resolve on binding DTOs)

- [ ] **Step 1: Add field on DTO**

In `LanConnectSavedRoomBinding`:

```csharp
public string HostChannel { get; set; } = string.Empty;
```

Optionally on `LanConnectResolvedRoomBinding` add:

```csharp
public string HostChannel { get; init; } = string.Empty;
public string EffectiveHostChannel => LanConnectHostChannels.Resolve(HostChannel);
```

Update `Resolve(SerializableRun run)` to copy stored `HostChannel` when binding exists (raw value, not rewritten).

- [ ] **Step 2: Replace/extend persist API**

Replace `PersistBinding` call sites to use:

```csharp
public static void PersistHostBinding(
    SerializableRun run,
    string roomName,
    string? password,
    string gameMode,
    string hostChannel,
    string source)
{
    string trimmedRoomName = LanConnectConfig.SanitizeRoomName(roomName);
    if (string.IsNullOrWhiteSpace(trimmedRoomName))
    {
        GD.Print($"sts2_lan_connect save_binding: skip persist because room name is empty. source={source}");
        return;
    }

    if (!LanConnectHostChannels.IsValid(hostChannel))
    {
        GD.Print($"sts2_lan_connect save_binding: reject invalid hostChannel='{hostChannel}' source={source}");
        return;
    }

    string normalizedChannel = hostChannel.Trim().ToLowerInvariant();

    LanConnectSavedRoomBinding binding = new()
    {
        SaveKey = BuildSaveKey(run),
        RoomName = trimmedRoomName,
        Password = string.IsNullOrWhiteSpace(password) ? string.Empty : LanConnectConfig.SanitizeRoomPassword(password),
        GameMode = string.IsNullOrWhiteSpace(gameMode) ? GetLobbyGameMode(run) : gameMode.Trim(),
        HostChannel = normalizedChannel,
        RunStartTime = run.StartTime,
        PlayerCount = run.Players.Count,
        PlayerSignature = BuildPlayerSignature(run),
        PlayerNames = BuildPlayerNamesForPersist(run),
        UpdatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    LanConnectConfig.UpsertSaveRoomBinding(binding);
    GD.Print(
        $"sts2_lan_connect save_binding: persisted source={source}, saveKey={binding.SaveKey}, roomName='{binding.RoomName}', passwordSet={!string.IsNullOrWhiteSpace(binding.Password)}, hostChannel={binding.HostChannel}, playerCount={binding.PlayerCount}, signature={binding.PlayerSignature}");
}
```

Keep a thin obsolete wrapper if needed for compile during intermediate steps:

```csharp
public static void PersistBinding(SerializableRun run, string roomName, string? password, string gameMode, string source)
    => PersistHostBinding(run, roomName, password, gameMode, LanConnectHostChannels.Lobby, source);
```

Final state: all call sites use `PersistHostBinding` with explicit channel; wrapper may remain as lobby default only if something external still needs it — prefer removing after call sites updated.

- [ ] **Step 3: CloneBinding**

In `LanConnectConfig.CloneBinding`, copy:

```csharp
HostChannel = binding.HostChannel ?? string.Empty,
```

Do **not** force empty → `"lobby"` in `NormalizeDefaultsUnsafe`.

- [ ] **Step 4: Unit test invalid channel rejected + clone field**

If `Upsert`/`TryGet` need filesystem, test pure DTO:

```csharp
[Fact]
public void SavedRoomBinding_host_channel_defaults_empty()
{
    var b = new LanConnectSavedRoomBinding();
    Assert.Equal(string.Empty, b.HostChannel);
    Assert.Equal(LanConnectHostChannels.Lobby, LanConnectHostChannels.Resolve(b.HostChannel));
}
```

- [ ] **Step 5: Commit**

```bash
git add sts2-lan-connect/Scripts/Lobby/LanConnectMultiplayerSaveRoomBinding.cs \
  sts2-lan-connect/Scripts/LanConnectConfig.cs \
  sts2-lan-connect.Tests/Lobby/LanConnectHostChannelBindingTests.cs
git commit -m "feat(client): persist HostChannel on multiplayer save bindings"
```

---

### Task 3: Gate continue-run auto publisher

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectContinueRunLobbyAutoPublisher.cs`

- [ ] **Step 1: In `PublishAsync`, after Resolve binding, decide**

```csharp
LanConnectResolvedRoomBinding binding = LanConnectMultiplayerSaveRoomBinding.Resolve(context.Run);
LanConnectSavedRoomBinding? storedBinding = LanConnectConfig.TryGetSaveRoomBinding(binding.SaveKey);
string persistedHostChannel = storedBinding?.HostChannel ?? binding.HostChannel;
string effectiveHostChannel = LanConnectHostChannels.Resolve(persistedHostChannel);
var decision = LanConnectContinueRunPublishDecision.Decide(effectiveHostChannel);

GD.Print(
    $"sts2_lan_connect continue_run_publish: attempt screen={context.ScreenType}, source={source}, saveKey={binding.SaveKey}, storedBinding={binding.HasStoredBinding}, roomName='{binding.RoomName}', passwordSet={!string.IsNullOrWhiteSpace(binding.Password)}, persistedHostChannel={LanConnectHostChannels.DescribePersisted(persistedHostChannel)}, effectiveHostChannel={effectiveHostChannel}, decision={LanConnectContinueRunPublishDecision.ToLogToken(decision)}");

if (decision == LanConnectContinueRunPublishDecisionKind.SkipLanOrigin)
{
    CompletedScreens.Add(screen.GetInstanceId());
    GD.Print(
        $"sts2_lan_connect continue_run_publish: skip LAN-origin save screen={context.ScreenType}, saveKey={binding.SaveKey}, effectiveHostChannel={effectiveHostChannel}, decision=skip_lan_origin");
    return;
}
```

- [ ] **Step 2: On successful lobby publish, persist with lobby channel**

Replace:

```csharp
LanConnectMultiplayerSaveRoomBinding.PersistBinding(...);
```

with:

```csharp
LanConnectMultiplayerSaveRoomBinding.PersistHostBinding(
    context.Run,
    binding.RoomName,
    binding.Password,
    binding.GameMode,
    LanConnectHostChannels.Lobby,
    "continue_save_publish");
```

- [ ] **Step 3: Commit**

```bash
git add sts2-lan-connect/Scripts/Lobby/LanConnectContinueRunLobbyAutoPublisher.cs
git commit -m "fix(client): skip lobby auto-publish for LAN-origin continue-run saves"
```

---

### Task 4: Runtime host origin + save event persistence

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectLobbyRuntime.cs`
- Modify: `sts2-lan-connect/Scripts/LanConnectHostFlow.cs`
- Modify: call sites of `PersistBinding` in runtime to `PersistHostBinding(..., Lobby, ...)`

- [ ] **Step 1: Add nested state + API on runtime**

```csharp
private HostOriginState? _hostOrigin;

internal void RegisterHostOrigin(
    NetHostGameService netService,
    string hostChannel,
    string roomName,
    string? password,
    string gameMode)
{
    if (netService == null)
    {
        return;
    }

    if (!LanConnectHostChannels.IsValid(hostChannel))
    {
        GD.Print($"sts2_lan_connect lobby runtime: reject host origin hostChannel='{hostChannel}'");
        return;
    }

    _hostOrigin = new HostOriginState(
        netService,
        hostChannel.Trim().ToLowerInvariant(),
        LanConnectConfig.SanitizeRoomName(roomName),
        string.IsNullOrWhiteSpace(password) ? null : LanConnectConfig.SanitizeRoomPassword(password),
        string.IsNullOrWhiteSpace(gameMode) ? LanConnectConstants.DefaultGameMode : gameMode.Trim());

    GD.Print(
        $"sts2_lan_connect lobby runtime: registered host origin channel={_hostOrigin.HostChannel}, roomName='{_hostOrigin.RoomName}'");
}

internal void ClearHostOrigin(NetHostGameService? netService = null)
{
    if (_hostOrigin == null)
    {
        return;
    }

    if (netService != null && !ReferenceEquals(_hostOrigin.NetService, netService))
    {
        return;
    }

    GD.Print($"sts2_lan_connect lobby runtime: cleared host origin channel={_hostOrigin.HostChannel}");
    _hostOrigin = null;
}

private sealed class HostOriginState
{
    public HostOriginState(NetHostGameService netService, string hostChannel, string roomName, string? password, string gameMode)
    {
        NetService = netService;
        HostChannel = hostChannel;
        RoomName = roomName;
        Password = password;
        GameMode = gameMode;
        netService.Disconnected += OnDisconnected;
    }

    public NetHostGameService NetService { get; }
    public string HostChannel { get; }
    public string RoomName { get; }
    public string? Password { get; }
    public string GameMode { get; }
    public bool HasPersisted { get; set; }

    private void OnDisconnected(NetErrorInfo _)
    {
        NetService.Disconnected -= OnDisconnected;
        if (LanConnectLobbyRuntime.Instance != null)
        {
            LanConnectLobbyRuntime.Instance.ClearHostOrigin(NetService);
        }
    }
}
```

Adjust `Disconnected` handler signature to match existing `NetHostGameService` events (same pattern as `HostedRoomSession`).

- [ ] **Step 2: `AttachHostedRoom` registers lobby origin**

After creating session / existing attach path, call:

```csharp
RegisterHostOrigin(
    netService,
    LanConnectHostChannels.Lobby,
    metadata.RoomName,
    metadata.Password,
    metadata.GameMode);
```

- [ ] **Step 3: Rewrite `PersistBindingForCurrentSave`**

```csharp
private void PersistBindingForCurrentSave(string source)
{
    HostedRoomSession? session = _activeSession;
    if (session != null)
    {
        if (!LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(out SerializableRun? run, out string failureReason) || run == null)
        {
            GD.Print($"sts2_lan_connect lobby runtime: skip save binding persist source={source}, reason={failureReason}");
            return;
        }

        LanConnectMultiplayerSaveRoomBinding.PersistHostBinding(
            run,
            session.Metadata.RoomName,
            session.Metadata.Password,
            session.Metadata.GameMode,
            LanConnectHostChannels.Lobby,
            source);
        return;
    }

    HostOriginState? origin = _hostOrigin;
    if (origin == null)
    {
        return;
    }

    if (origin.NetService.Type != NetGameType.Host)
    {
        return;
    }

    if (!LanConnectMultiplayerSaveRoomBinding.TryLoadCurrentMultiplayerRun(out SerializableRun? lanRun, out string lanFailure) || lanRun == null)
    {
        GD.Print($"sts2_lan_connect lobby runtime: skip LAN host binding persist source={source}, reason={lanFailure}");
        return;
    }

    string roomName = string.IsNullOrWhiteSpace(origin.RoomName)
        ? "LAN 联机房间"
        : origin.RoomName;

    LanConnectMultiplayerSaveRoomBinding.PersistHostBinding(
        lanRun,
        roomName,
        origin.Password,
        origin.GameMode,
        origin.HostChannel,
        source);
    origin.HasPersisted = true;
}
```

- [ ] **Step 4: `StartLanHostAsync` after successful ENet start**

```csharp
// after StartENetHost success, before PushHostScreen:
LanConnectLobbyRuntime.Instance?.RegisterHostOrigin(
    netService,
    LanConnectHostChannels.Lan,
    "LAN 联机房间",
    password: null,
    gameMode: LanConnectMultiplayerSaveRoomBinding.GetLobbyGameMode(gameMode));
```

If `Instance` is null, log error and continue (ENet still runs; continue-run protection may be missing).

On LAN host failure paths, do not register.

- [ ] **Step 5: Clear origin on `_ExitTree`**

```csharp
_hostOrigin = null; // or ClearHostOrigin() after unhooking disconnect
```

- [ ] **Step 6: Commit**

```bash
git add sts2-lan-connect/Scripts/Lobby/LanConnectLobbyRuntime.cs \
  sts2-lan-connect/Scripts/LanConnectHostFlow.cs
git commit -m "feat(client): register LAN/lobby host origin and persist channel on save"
```

---

### Task 5: Diagnostics + abandon cleanup

**Files:**
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectSaveDiagnostics.cs`
- Modify: `sts2-lan-connect/Scripts/Lobby/LanConnectMultiplayerSaveCompatibility.cs`

- [ ] **Step 1: Extend snapshot**

When binding present:

```csharp
string bindingHostChannel = LanConnectHostChannels.DescribePersisted(binding.HostChannel);
string effectiveHostChannel = LanConnectHostChannels.Resolve(binding.HostChannel);
// append: bindingHostChannel=..., effectiveHostChannel=...
```

When binding missing:

```csharp
// binding=missing, effectiveHostChannel=lobby
```

- [ ] **Step 2: Abandon removes binding**

In `AbandonCurrentRunAsync`, before `DeleteCurrentMultiplayerRun()`:

```csharp
if (TryLoadSafeCurrentRun(out SerializableRun? abandonRun, out _) && abandonRun != null)
{
    string saveKey = LanConnectMultiplayerSaveRoomBinding.BuildSaveKey(abandonRun);
    bool removed = LanConnectConfig.RemoveSaveRoomBinding(saveKey);
    GD.Print($"sts2_lan_connect save_compat: abandon removedBinding={removed}, saveKey={saveKey}");
}
else
{
    GD.Print($"sts2_lan_connect save_compat: abandon could not load save for binding cleanup reason={failureReason}");
}
```

Reuse already-loaded run if available from earlier block to avoid double load.

- [ ] **Step 3: Commit**

```bash
git add sts2-lan-connect/Scripts/Lobby/LanConnectSaveDiagnostics.cs \
  sts2-lan-connect/Scripts/Lobby/LanConnectMultiplayerSaveCompatibility.cs
git commit -m "feat(client): log host channel diagnostics and clear binding on abandon"
```

---

### Task 6: Build, full channel tests, push branch

**Files:** none new (verification)

- [ ] **Step 1: Run unit tests**

```bash
export PATH="/Users/mac/.dotnet:$PATH" && export DOTNET_ROOT="/Users/mac/.dotnet"
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj --filter "FullyQualifiedName~LanConnectHostChannel"
```

Expected: pass.

- [ ] **Step 2: Build client mod**

```bash
export PATH="/Users/mac/.dotnet:$PATH" && export DOTNET_ROOT="/Users/mac/.dotnet"
DOTNET_BIN=/Users/mac/.dotnet/dotnet \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
bash scripts/build-sts2-lan-connect.sh
```

Expected: exit 0.

- [ ] **Step 3: Manual checklist (log-based, no force push)**

Document in commit/PR body if not runnable here:

1. LAN create → play → save → quit → continue-run: no `POST /rooms`, log `decision=skip_lan_origin`
2. Lobby create → continue: still publishes
3. Old binding without field: publishes

- [ ] **Step 4: Push branch**

```bash
git status --short --branch
git push -u origin fix/lan-lobby-continue-channel
```

Do not merge to main unless asked. Do not touch `releases/`.

---

## Spec Coverage Checklist

| Spec section | Task |
|--------------|------|
| §4 HostChannel model + Resolve | Task 1–2 |
| §5 PersistHostBinding + LAN room name | Task 2, 4 |
| §6 Create entry registration | Task 4 |
| §7 OnRunSaved branch + host-only | Task 4 |
| §8 Continue-run skip/publish | Task 1, 3 |
| §8.3 Missing → lobby | Task 1, 3 |
| §9 Clear origin + abandon binding | Task 4–5 |
| §10 Diagnostics | Task 5 |
| §12 Unit tests | Task 1–2, 6 |
| §13 #40 acceptance | Task 6 manual |
| §14 Branch + push | Task 6 |

## Self-Review Notes

- No service API changes.
- No `releases/` edits.
- Unknown channel warning uses `GD.Print` consistent with project logging style.
- Client-only; guest join paths must not call `RegisterHostOrigin` or `PersistHostBinding`.
