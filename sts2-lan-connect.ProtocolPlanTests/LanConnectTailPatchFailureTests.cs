using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.ProtocolPlanTests;

public sealed class LanConnectTailPatchFailureTests
{
    [Fact]
    public void Rollback_failure_does_not_replace_the_original_patch_exception()
    {
        string dispatcherId = $"{LanConnectProtocolPatchDispatcher.HarmonyId}.rollback.{Guid.NewGuid():N}";
        Harmony dispatcher = new(dispatcherId);
        InvalidOperationException original = new("first patch failure");
        bool rollbackAttempted = false;

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            LanConnectProtocolPatchDispatcher.ApplyAtomicForTesting(
                dispatcher,
                [_ => throw original],
                (_, _) =>
                {
                    rollbackAttempted = true;
                    throw new IOException("injected rollback failure");
                },
                emitRollbackDiagnostics: false));

        Assert.True(rollbackAttempted);
        Assert.Same(original, thrown);
    }

    [Fact]
    public void Every_patch_ordinal_records_the_exact_failure_and_preserves_the_original()
    {
        const bool isAndroid = false;
        LanConnectTailPatchPlan plan = LanConnectTailMessagePatches.ResolvePatchPlan(
            typeof(PacketWriter).Assembly);
        string diagnosticsRoot = Path.Combine(
            Path.GetTempPath(),
            $"sts2-lan-connect-patch-ordinal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(diagnosticsRoot);

        try
        {
            for (int failureOrdinal = 1; failureOrdinal <= plan.Steps.Count; failureOrdinal++)
            {
                string dispatcherId = $"{LanConnectProtocolPatchDispatcher.HarmonyId}.ordinal.{Guid.NewGuid():N}";
                string externalId = $"external.ordinal.{Guid.NewGuid():N}";
                Harmony dispatcher = new(dispatcherId);
                Harmony external = new(externalId);
                List<string> attempted = [];
                List<string> mirroredEvents = [];
                InvalidOperationException original = new($"injected ordinal {failureOrdinal}");
                MethodInfo externalTarget = AccessTools.Method(
                    typeof(PacketWriter),
                    nameof(PacketWriter.Reset),
                    Type.EmptyTypes)!;
                MethodInfo externalPrefix = AccessTools.Method(
                    typeof(LanConnectTailPatchFailureTests),
                    nameof(ExternalResetPrefix))!;

                try
                {
                    external.Patch(externalTarget, prefix: new HarmonyMethod(externalPrefix));
                    using LanConnectStartupDiagnostics diagnostics =
                        LanConnectStartupDiagnostics.CreateForTesting(new LanConnectStartupDiagnosticsOptions
                        {
                            DiagnosticsRoot = diagnosticsRoot,
                            SessionIdFactory = () => $"{(isAndroid ? "android" : "desktop")}-{failureOrdinal:D2}",
                            MirrorInfo = mirroredEvents.Add,
                            CaptureArtifacts = false,
                            EnableHarmonyDiagnostics = false
                        });

                    InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
                        LanConnectProtocolPatchDispatcher.ApplyAtomicForTesting(
                            dispatcher,
                            [harmony => LanConnectTailMessagePatches.ApplyPlanWithInjectedPatcherForTesting(
                                harmony,
                                plan,
                                (_, step) =>
                                {
                                    attempted.Add(step.Id);
                                    if (attempted.Count == failureOrdinal)
                                    {
                                        throw original;
                                    }

                                    PatchStep(harmony, step);
                                })],
                            emitRollbackDiagnostics: false));

                    Assert.Same(original, thrown);
                    Assert.Equal(failureOrdinal, attempted.Count);
                    Assert.Equal(plan.Steps[failureOrdinal - 1].Id, attempted[^1]);
                    AssertOwnerHasNoPatches(dispatcherId);
                    Assert.Contains(Harmony.GetPatchInfo(externalTarget)!.Prefixes, patch => patch.owner == externalId);

                    JsonElement[] patchEvents = mirroredEvents
                        .Where(static line => line.StartsWith("sts2_lan_connect patch_diag: ", StringComparison.Ordinal))
                        .Select(static line => JsonDocument.Parse(
                            line["sts2_lan_connect patch_diag: ".Length..]).RootElement.Clone())
                        .Where(static element => element.GetProperty("event").GetString() == "patch")
                        .ToArray();
                    JsonElement last = patchEvents[^1];
                    LanConnectTailPatchStep failedStep = plan.Steps[failureOrdinal - 1];
                    Assert.Equal("failure", last.GetProperty("status").GetString());
                    Assert.Equal(failedStep.Id, last.GetProperty("plan_id").GetString());
                    Assert.Equal(plan.Profile, last.GetProperty("plan_profile").GetString());
                    Assert.Equal(failureOrdinal, last.GetProperty("ordinal").GetInt32());
                    Assert.Contains(failedStep.Target.Name, last.GetProperty("target").GetString(), StringComparison.Ordinal);
                    Assert.DoesNotContain(
                        patchEvents,
                        element => element.GetProperty("ordinal").GetInt32() > failureOrdinal);
                }
                finally
                {
                    dispatcher.UnpatchAll(dispatcherId);
                    external.UnpatchAll(externalId);
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(diagnosticsRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void PatchStep(Harmony harmony, LanConnectTailPatchStep step) =>
        harmony.Patch(
            step.Target,
            prefix: CreateHarmonyMethod(step.Prefix, step.PrefixPriority),
            postfix: CreateHarmonyMethod(step.Postfix, step.PostfixPriority),
            finalizer: CreateHarmonyMethod(step.Finalizer, step.FinalizerPriority));

    private static HarmonyMethod? CreateHarmonyMethod(MethodInfo? method, int? priority)
    {
        if (method == null)
        {
            return null;
        }

        HarmonyMethod harmonyMethod = new(method);
        if (priority.HasValue)
        {
            harmonyMethod.priority = priority.Value;
        }
        return harmonyMethod;
    }

    private static void AssertOwnerHasNoPatches(string owner)
    {
        Assert.DoesNotContain(Harmony.GetAllPatchedMethods(), method =>
        {
            Patches? patches = Harmony.GetPatchInfo(method);
            return patches != null && patches.Prefixes
                .Concat(patches.Postfixes)
                .Concat(patches.Transpilers)
                .Concat(patches.Finalizers)
                .Any(patch => patch.owner == owner);
        });
    }

    private static void ExternalResetPrefix() { }
}
