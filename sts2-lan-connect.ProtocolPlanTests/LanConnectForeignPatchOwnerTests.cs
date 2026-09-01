using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.ProtocolPlanTests;

// Regression tests for the alpha.1..alpha.8 "lobby never appears" failure: a foreign patch
// declared on a generic type (RitsuLib's SerializePatch<TMessage>.Postfix) poisons the closed
// generic target for every later patcher under Harmony 2.4.2. See
// docs/STS2_LAN_CONNECT_ALPHA8_LOBBY_MISSING_RCA_ZH.md.
public sealed class LanConnectForeignPatchOwnerTests : IDisposable
{
    private readonly List<Harmony> _harmonyInstances = [];

    public LanConnectForeignPatchOwnerTests()
    {
        LanConnectTranspilerUtils.LogInfoSink = static _ => { };
        LanConnectTranspilerUtils.LogWarnSink = static _ => { };
        LanConnectSerializationPatches.LogInfoSink = static _ => { };
        LanConnectSerializationPatches.LogWarnSink = static _ => { };
        LanConnectSerializationPatches.LogErrorSink = static _ => { };
    }

    public void Dispose()
    {
        // Keep every log sink neutralized while unpatching: wrapper regeneration can execute
        // our transpilers, and a real Log call crashes the xUnit host (no Godot native runtime).
        LanConnectTranspilerUtils.LogInfoSink = static _ => { };
        LanConnectTranspilerUtils.LogWarnSink = static _ => { };
        LanConnectSerializationPatches.LogInfoSink = static _ => { };
        LanConnectSerializationPatches.LogWarnSink = static _ => { };
        LanConnectSerializationPatches.LogErrorSink = static _ => { };
        try
        {
            // Foreign owners first: while the poisoning postfix is gone, regenerating the
            // closed generic wrapper with only our (non-generic-declared) patches is safe.
            foreach (Harmony harmony in _harmonyInstances)
            {
                TryUnpatch(harmony);
            }

            TryUnpatch(new Harmony(LanConnectProtocolPatchDispatcher.HarmonyId));
        }
        finally
        {
            LanConnectSerializationPatches.ResetAppliedAfterExternalRollback();
            LanConnectDegradedMode.ResetForTesting();
            // Intentionally not restoring the log sinks: no test host can survive a real Log
            // call, and leaving no-op sinks can only make the rest of the process safer.
        }
    }

    private static void TryUnpatch(Harmony harmony)
    {
        try
        {
            harmony.UnpatchAll(harmony.Id);
        }
        catch
        {
            // Best-effort cleanup: unpatching a poisoned target can throw InvalidProgramException.
        }
    }

    [Fact]
    public void Generic_declared_foreign_postfix_does_not_break_plan_application()
    {
        Harmony foreign = CreateForeignHarmony();
        MethodInfo beginRunTarget = PatchForeignGenericPostfix(foreign, typeof(LobbyBeginRunMessage));
        MethodInfo initialGameInfoTarget = PatchForeignGenericPostfix(foreign, typeof(InitialGameInfoMessage));

        LanConnectTailPatchPlan plan = LanConnectTailMessagePatches.ResolvePatchPlan(
            typeof(PacketWriter).Assembly);
        Assert.Equal("native_bus_v1", plan.Profile);
        Assert.Equal(0, plan.GenericTargetCount);

        Harmony ours = CreateHarmony("plan");
        Exception? failure = Record.Exception(() =>
            LanConnectTailMessagePatches.ApplyPlanQuietlyForTesting(ours, plan));
        Assert.Null(failure);

        // Spot-check that our own non-generic steps actually landed.
        MethodInfo writerReset = AccessTools.Method(typeof(PacketWriter), nameof(PacketWriter.Reset), Type.EmptyTypes)!;
        Assert.Contains(Harmony.GetPatchInfo(writerReset)!.Prefixes, patch => patch.owner == ours.Id);
        MethodInfo deserialize = AccessTools.Method(
            typeof(NetMessageBus),
            nameof(NetMessageBus.TryDeserializeMessage),
            [typeof(byte[]), typeof(INetMessage).MakeByRefType(), typeof(ulong?).MakeByRefType()])!;
        Assert.Contains(Harmony.GetPatchInfo(deserialize)!.Postfixes, patch => patch.owner == ours.Id);

        // The foreign owner must survive our plan untouched.
        Assert.Contains(Harmony.GetPatchInfo(beginRunTarget)!.Postfixes, patch => patch.owner == foreign.Id);
        Assert.Contains(Harmony.GetPatchInfo(initialGameInfoTarget)!.Postfixes, patch => patch.owner == foreign.Id);
    }

    [Fact]
    public void Serialization_boundary_failure_is_not_fatal()
    {
        Harmony foreign = CreateForeignHarmony();
        MethodInfo beginRunTarget = PatchForeignGenericPostfix(foreign, typeof(LobbyBeginRunMessage));

        List<string> warnings = [];
        // The legacy desktop generic plan (with its boundary prefix) is gone; the boundary
        // is permanently skipped under native_bus_v1.
        LanConnectSerializationPatches.ResetAppliedAfterExternalRollback();
        LanConnectSerializationPatches.LogWarnSink = warnings.Add;
        LanConnectSerializationPatches.LogInfoSink = static _ => { };
        LanConnectSerializationPatches.LogErrorSink = static _ => { };
        LanConnectTranspilerUtils.LogInfoSink = static _ => { };
        LanConnectTranspilerUtils.LogWarnSink = warnings.Add;

        Exception? failure = Record.Exception(LanConnectSerializationPatches.Apply);

        Assert.Null(failure);
        Assert.True(LanConnectSerializationPatches.IsAppliedForTesting);
        // begin-run boundary is permanently skipped under native_bus_v1 (legacy plan removed),
        // so no boundary warning is emitted any more.

        // All six required transpilers must be in place even though the boundary was skipped.
        Assembly sts2 = typeof(PacketWriter).Assembly;
        Type joinResponseType = sts2.GetType(
            "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinResponseMessage",
            throwOnError: true)!;
        Type beginRunType = typeof(LobbyBeginRunMessage);
        Type slotIdCarrierType = LanConnectSerializationPatches.ResolveSlotIdCarrierType(joinResponseType, beginRunType);
        foreach ((Type declaringType, string methodName, Type parameterType) in new[]
                 {
                     (slotIdCarrierType, "Serialize", typeof(PacketWriter)),
                     (slotIdCarrierType, "Deserialize", typeof(PacketReader)),
                     (joinResponseType, "Serialize", typeof(PacketWriter)),
                     (joinResponseType, "Deserialize", typeof(PacketReader)),
                     (beginRunType, "Serialize", typeof(PacketWriter)),
                     (beginRunType, "Deserialize", typeof(PacketReader))
                 })
        {
            MethodInfo target = AccessTools.Method(declaringType, methodName, [parameterType])!;
            Assert.Contains(
                Harmony.GetPatchInfo(target)!.Transpilers,
                patch => patch.owner == LanConnectProtocolPatchDispatcher.HarmonyId);
        }

        // The foreign postfix on the poisoned closed generic must remain untouched.
        Assert.Contains(Harmony.GetPatchInfo(beginRunTarget)!.Postfixes, patch => patch.owner == foreign.Id);
    }

    [Fact]
    public void Degraded_mode_blocks_host_and_join()
    {
        LanConnectDegradedMode.ResetForTesting();
        LanConnectDegradedMode.LogErrorSink = static _ => { };
        Assert.False(LanConnectDegradedMode.IsActive);
        Assert.Null(LanConnectDegradedMode.CreateBlockingFailure());

        LanConnectDegradedMode.Enter(LanConnectDegradedMode.ProtocolPatchConflictCode, "test-fingerprint");

        Assert.True(LanConnectDegradedMode.IsActive);
        LanConnectProtocolFailure? failure = LanConnectDegradedMode.CreateBlockingFailure();
        Assert.NotNull(failure);
        Assert.Equal("protocol_patch_conflict", failure.Code);

        // The code must have a dedicated user-facing message, not the generic fallback.
        string text = LanConnectProtocolUiMessages.Describe(failure);
        Assert.DoesNotContain("联机协议拒绝了本次操作", text, StringComparison.Ordinal);
        Assert.Contains("RitsuLib", text, StringComparison.Ordinal);

        // The proactive lobby-entry notice fires once per session; blocked actions keep
        // presenting the failure on every attempt.
        Assert.True(LanConnectDegradedMode.TryConsumeLobbyEntryNotice(out LanConnectProtocolFailure notice));
        Assert.Equal("protocol_patch_conflict", notice.Code);
        Assert.False(LanConnectDegradedMode.TryConsumeLobbyEntryNotice(out _));
        Assert.NotNull(LanConnectDegradedMode.CreateBlockingFailure());

        // Every host/join funnel must consult the degraded gate (UI paths cannot run in xUnit,
        // so assert the wiring at source level, matching the existing compatibility-test style).
        AssertSourceContains("sts2-lan-connect", "Scripts", "LanConnectHostFlow.cs");
        AssertSourceContains("sts2-lan-connect", "Scripts", "Lobby", "LanConnectLobbyOverlay.cs");
        AssertSourceContains("sts2-lan-connect", "Scripts", "Lobby", "LanConnectDirectJoinFlow.cs");
    }

    [Fact]
    public void Harmony_patch_info_roundtrip_loses_generic_instantiation()
    {
        // Environment assertion: Harmony 2.4.2 stores applied patches as
        // (moduleGUID, metadataToken) and resolves them back to the OPEN generic form.
        // If a future Harmony version preserves the instantiation, this test turns red and
        // the non-generic-plan default (F2) can be re-evaluated.
        Harmony foreign = CreateForeignHarmony();
        MethodInfo target = PatchForeignGenericPostfix(foreign, typeof(LobbyBeginRunMessage));

        Patch stored = Harmony.GetPatchInfo(target)!.Postfixes
            .Single(patch => patch.owner == foreign.Id);
        Assert.True(
            stored.PatchMethod.ContainsGenericParameters,
            "Harmony preserved the patch method's generic instantiation; " +
            "the foreign-owner poisoning mechanism no longer applies.");
        Assert.True(stored.PatchMethod.DeclaringType?.ContainsGenericParameters);
    }

    private static void AssertSourceContains(params string[] relativeSegments)
    {
        string source = File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. relativeSegments]));
        Assert.Contains("LanConnectDegradedMode.CreateBlockingFailure", source, StringComparison.Ordinal);
    }

    private Harmony CreateHarmony(string tag)
    {
        Harmony harmony = new($"sts2_lan_connect.tests.foreign_owner.{tag}.{Guid.NewGuid():N}");
        _harmonyInstances.Add(harmony);
        return harmony;
    }

    private Harmony CreateForeignHarmony() => CreateHarmony("foreign");

    // Replicates the RitsuLib shape exactly: the postfix is declared on a generic type and
    // closed via MakeGenericType, which is what poisons the target for later patchers.
    private MethodInfo PatchForeignGenericPostfix(Harmony foreign, Type messageType)
    {
        MethodInfo target = LanConnectSerializationPatches.ResolveGenericSerializeMessageMethod(
            typeof(NetMessageBus),
            messageType);
        Type closedPatchType = typeof(ForeignSerializePatch<>).MakeGenericType(messageType);
        MethodInfo postfix = AccessTools.DeclaredMethod(closedPatchType, nameof(ForeignSerializePatch<INetMessage>.Postfix))
            ?? throw new MissingMethodException(closedPatchType.FullName, "Postfix");
        foreign.Patch(target, postfix: new HarmonyMethod(postfix));
        return target;
    }

    private static class ForeignSerializePatch<TMessage> where TMessage : INetMessage
    {
        public static void Postfix(NetMessageBus __instance, TMessage message, ref int length, ref byte[] __result)
        {
            _ = __instance;
            _ = message;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing STS2-Game-Lobby.sln.");
    }
}
