# Android Harmony Crash Mitigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce the ~33% SIGSEGV crash rate on Android ARM64 during mod initialization by isolating each Harmony patch call, skipping unnecessary patches, and adding platform diagnostics.

**Architecture:** Wrap every `harmony.Patch()` call in individual try-catch blocks with per-patch logging. Skip `StartSteamHost` on Android (never called). Isolate each gameplay patch group so one failure doesn't cascade. Add platform/patch summary diagnostics to `Entry.Init()`.

**Tech Stack:** C# / .NET 9 / Harmony 2.4.2 / Godot 4.5.1

---

### Task 1: Add safe patch helper to LanConnectLobbyCapacityPatches

**Files:**
- Modify: `sts2-lan-connect/Scripts/LanConnectLobbyCapacityPatches.cs`

- [ ] **Step 1: Add a `TrySafePatch` helper method and refactor `Apply()` to use it**

Replace the entire `Apply()` method body. Each `harmony.Patch()` call becomes a `TrySafePatch()` call that catches exceptions individually. Skip `StartSteamHost` on Android.

```csharp
// In LanConnectLobbyCapacityPatches.cs, add at top of file:
using System.Runtime.InteropServices;

// Replace the existing Apply() method with:
public static void Apply(Harmony harmony)
{
    int applied = 0;
    int skipped = 0;
    int failed = 0;

    bool isAndroid = OperatingSystem.IsAndroid();

    // StartENetHost — needed on all platforms
    MethodInfo? startENet = AccessTools.Method(typeof(NetHostGameService), nameof(NetHostGameService.StartENetHost));
    if (TrySafePatch(harmony, startENet, nameof(StartENetHostPrefix), null, "StartENetHost"))
        applied++;
    else
        failed++;

    // StartSteamHost — skip on Android (Steam hosting does not exist on Android)
    if (isAndroid)
    {
        Log.Info("sts2_lan_connect capacity: skipped StartSteamHost (Android, not applicable).");
        skipped++;
    }
    else
    {
        MethodInfo? startSteam = AccessTools.Method(typeof(NetHostGameService), nameof(NetHostGameService.StartSteamHost));
        if (TrySafePatch(harmony, startSteam, nameof(StartSteamHostPrefix), null, "StartSteamHost"))
            applied++;
        else
            failed++;
    }

    // StartRunLobby constructor
    ConstructorInfo? lobbyCtor = AccessTools.Constructor(typeof(StartRunLobby),
        new[] { typeof(GameMode), typeof(INetGameService), typeof(IStartRunLobbyListener), typeof(int) });
    if (TrySafePatch(harmony, lobbyCtor, null, nameof(StartRunLobbyCtorPostfix), "StartRunLobby.ctor"))
        applied++;
    else
        failed++;

    // OnConnectedToClientAsHost
    MethodInfo? onConnected = AccessTools.Method(typeof(StartRunLobby), "OnConnectedToClientAsHost");
    if (TrySafePatch(harmony, onConnected, nameof(SyncMaxPlayersPrefix), null, "OnConnectedToClientAsHost"))
        applied++;
    else
        failed++;

    // HandleClientLobbyJoinRequestMessage
    MethodInfo? handleJoin = AccessTools.Method(typeof(StartRunLobby), "HandleClientLobbyJoinRequestMessage");
    if (TrySafePatch(harmony, handleJoin, nameof(SyncMaxPlayersPrefix), null, "HandleClientLobbyJoinRequestMessage"))
        applied++;
    else
        failed++;

    Log.Info($"sts2_lan_connect gameplay: lobby capacity patches applied={applied}, skipped={skipped}, failed={failed}.");
}

// Add this new helper method:
private static bool TrySafePatch(Harmony harmony, MethodBase? target, string? prefixName, string? postfixName, string label)
{
    if (target == null)
    {
        Log.Warn($"sts2_lan_connect capacity: target method not found for {label}, skipping.");
        return false;
    }

    try
    {
        HarmonyMethod? prefix = prefixName != null
            ? new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), prefixName)
            : null;
        HarmonyMethod? postfix = postfixName != null
            ? new HarmonyMethod(typeof(LanConnectLobbyCapacityPatches), postfixName)
            : null;

        harmony.Patch(target, prefix: prefix, postfix: postfix);
        Log.Info($"sts2_lan_connect capacity: patched {label}.");
        return true;
    }
    catch (Exception ex)
    {
        Log.Warn($"sts2_lan_connect capacity: failed to patch {label}: {ex.GetType().Name}: {ex.Message}");
        return false;
    }
}
```

- [ ] **Step 2: Verify the build compiles**

Run:
```bash
cd /Users/mac/Desktop/STS-Game-Lobby/STS2-Game-Lobby && \
export PATH="/Users/mac/.dotnet:$PATH" && \
export DOTNET_ROOT="/Users/mac/.dotnet" && \
DOTNET_BIN=/Users/mac/.dotnet/dotnet \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
bash scripts/build-sts2-lan-connect.sh
```
Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add sts2-lan-connect/Scripts/LanConnectLobbyCapacityPatches.cs
git commit -m "feat: isolate capacity patches with per-patch try-catch, skip Steam on Android"
```

---

### Task 2: Isolate each gameplay patch group in LanConnectGameplayPatches

**Files:**
- Modify: `sts2-lan-connect/Scripts/LanConnectGameplayPatches.cs`

- [ ] **Step 1: Refactor `Initialize()` to wrap each patch group individually**

Replace the existing `Initialize()` method body (keep the `_initialized` guard and RMP check). Each sub-module gets its own try-catch so one failure doesn't prevent the rest from applying.

```csharp
// Replace the try/catch block inside Initialize() with:
int applied = 0;
int failed = 0;

if (TryApplyGroup("difficulty scaling", () => DifficultyScalingPatches.Apply(HarmonyInstance)))
    applied++;
else
    failed++;

if (TryApplyGroup("rest site", () => RestSitePatches.Apply(HarmonyInstance)))
    applied++;
else
    failed++;

if (TryApplyGroup("merchant", () => MerchantPatches.Apply(HarmonyInstance)))
    applied++;
else
    failed++;

if (TryApplyGroup("treasure", () => TreasurePatches.Apply(HarmonyInstance)))
    applied++;
else
    failed++;

if (TryApplyGroup("lobby capacity", () => LanConnectLobbyCapacityPatches.Apply(HarmonyInstance)))
    applied++;
else
    failed++;

Log.Info($"sts2_lan_connect gameplay: patch groups applied={applied}, failed={failed}.");
```

- [ ] **Step 2: Add the `TryApplyGroup` helper**

Add this private static method to the class:

```csharp
private static bool TryApplyGroup(string groupName, Action apply)
{
    try
    {
        apply();
        return true;
    }
    catch (Exception ex)
    {
        Log.Error($"sts2_lan_connect gameplay: {groupName} patches failed: {ex.GetType().Name}: {ex.Message}");
        return false;
    }
}
```

- [ ] **Step 3: Verify the build compiles**

Run the same build command as Task 1 Step 2.
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add sts2-lan-connect/Scripts/LanConnectGameplayPatches.cs
git commit -m "feat: isolate gameplay patch groups so one failure does not cascade"
```

---

### Task 3: Isolate serialization patches individually

**Files:**
- Modify: `sts2-lan-connect/Scripts/LanConnectSerializationPatches.cs`

- [ ] **Step 1: Wrap each `HarmonyInstance.Patch()` call in the three `Patch*` methods**

The serialization patches are the most critical (without them, >4 player networking breaks). Wrap each individually so we get per-patch diagnostics if one fails on a specific device.

In `PatchLobbyPlayerSlotId()`, replace:

```csharp
private static void PatchLobbyPlayerSlotId()
{
    MethodInfo? serialize = AccessTools.Method(typeof(LobbyPlayer), nameof(LobbyPlayer.Serialize));
    MethodInfo? deserialize = AccessTools.Method(typeof(LobbyPlayer), nameof(LobbyPlayer.Deserialize));

    TrySafePatch(serialize, nameof(TranspileLobbyPlayerSerialize), "LobbyPlayer.Serialize");
    TrySafePatch(deserialize, nameof(TranspileLobbyPlayerDeserialize), "LobbyPlayer.Deserialize");
}
```

In `PatchClientLobbyJoinResponseList()`, replace:

```csharp
private static void PatchClientLobbyJoinResponseList()
{
    MethodInfo? serialize = AccessTools.Method(typeof(ClientLobbyJoinResponseMessage), nameof(ClientLobbyJoinResponseMessage.Serialize));
    MethodInfo? deserialize = AccessTools.Method(typeof(ClientLobbyJoinResponseMessage), nameof(ClientLobbyJoinResponseMessage.Deserialize));

    TrySafePatch(serialize, nameof(TranspileJoinResponseSerialize), "JoinResponse.Serialize");
    TrySafePatch(deserialize, nameof(TranspileJoinResponseDeserialize), "JoinResponse.Deserialize");
}
```

In `PatchLobbyBeginRunList()`, replace:

```csharp
private static void PatchLobbyBeginRunList()
{
    MethodInfo? serialize = AccessTools.Method(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Serialize));
    MethodInfo? deserialize = AccessTools.Method(typeof(LobbyBeginRunMessage), nameof(LobbyBeginRunMessage.Deserialize));

    TrySafePatch(serialize, nameof(TranspileBeginRunSerialize), "BeginRun.Serialize");
    TrySafePatch(deserialize, nameof(TranspileBeginRunDeserialize), "BeginRun.Deserialize");
}
```

- [ ] **Step 2: Add the `TrySafePatch` helper and a patch counter**

Add these to the class:

```csharp
private static int _patchedCount;
private static int _failedCount;

private static void TrySafePatch(MethodInfo? target, string transpilerName, string label)
{
    if (target == null)
    {
        Log.Warn($"sts2_lan_connect serialization: target not found for {label}, skipping.");
        _failedCount++;
        return;
    }

    try
    {
        HarmonyInstance.Patch(target, transpiler: new HarmonyMethod(
            typeof(LanConnectSerializationPatches), transpilerName));
        _patchedCount++;
    }
    catch (Exception ex)
    {
        Log.Error($"sts2_lan_connect serialization: failed to patch {label}: {ex.GetType().Name}: {ex.Message}");
        _failedCount++;
    }
}
```

Update the summary log in `Apply()` to include counts:

```csharp
// Replace the existing Log.Info line at the end of the try block with:
Log.Info(
    $"sts2_lan_connect serialization: patches applied={_patchedCount}, failed={_failedCount}. " +
    $"slotId={LanConnectConstants.VanillaSlotIdBits}->{LanConnectConstants.ExtendedSlotIdBits}, " +
    $"lobbyList={LanConnectConstants.VanillaLobbyListBits}->{LanConnectConstants.ExtendedLobbyListBits}");
```

- [ ] **Step 3: Verify the build compiles**

Run the same build command as Task 1 Step 2.
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add sts2-lan-connect/Scripts/LanConnectSerializationPatches.cs
git commit -m "feat: isolate serialization patches with per-patch try-catch and counters"
```

---

### Task 4: Add platform diagnostics to Entry.Init()

**Files:**
- Modify: `sts2-lan-connect/Scripts/Entry.cs`

- [ ] **Step 1: Add platform diagnostic logging at the start of `Init()`**

```csharp
using System.Runtime.InteropServices;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2LanConnect.Scripts;

[ModInitializer(nameof(Init))]
public static class Entry
{
    public static void Init()
    {
        Log.Info(
            $"sts2_lan_connect init: platform={RuntimeInformation.OSDescription}, " +
            $"arch={RuntimeInformation.ProcessArchitecture}, " +
            $"isAndroid={OperatingSystem.IsAndroid()}, " +
            $"framework={RuntimeInformation.FrameworkDescription}");

        LanConnectConfig.Load();
        LanConnectExternalModDetection.Detect();
        LanConnectMultiplayerCompatibility.Initialize();
        LanConnectGameplayPatches.Initialize();
        LanConnectLobbyRuntime.Install();
        LanConnectRoomChatOverlay.Install();
        LanConnectRuntimeMonitor.Install();
        Log.Info("sts2_lan_connect initialized with runtime monitor.");
    }
}
```

- [ ] **Step 2: Verify the build compiles**

Run the same build command as Task 1 Step 2.
Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add sts2-lan-connect/Scripts/Entry.cs
git commit -m "feat: add platform diagnostics at mod init for Android crash triage"
```

---

### Task 5: Build, install, and verify on macOS

**Files:**
- No file changes — verification only.

- [ ] **Step 1: Full build with install**

```bash
cd /Users/mac/Desktop/STS-Game-Lobby/STS2-Game-Lobby && \
export PATH="/Users/mac/.dotnet:$PATH" && \
export DOTNET_ROOT="/Users/mac/.dotnet" && \
DOTNET_BIN=/Users/mac/.dotnet/dotnet \
GODOT_BIN="/Users/mac/Applications/Godot_mono.app/Contents/MacOS/Godot" \
bash scripts/build-sts2-lan-connect.sh --install
```
Expected: Build succeeds, mod installed to local game directory.

- [ ] **Step 2: Launch game on macOS and verify log output**

Check `~/Library/Application Support/SlayTheSpire2/logs/godot.log` for:
- `sts2_lan_connect init: platform=... arch=... isAndroid=False`
- `capacity: patched StartENetHost`
- `capacity: patched StartSteamHost` (should appear on macOS, NOT skipped)
- `capacity: patched StartRunLobby.ctor`
- `capacity: patched OnConnectedToClientAsHost`
- `capacity: patched HandleClientLobbyJoinRequestMessage`
- `lobby capacity patches applied=5, skipped=0, failed=0`
- `patch groups applied=5, failed=0`
- All existing functionality works as before.

- [ ] **Step 3: Commit verification notes (optional)**

No code changes needed. Proceed to Android testing if a device is available.
