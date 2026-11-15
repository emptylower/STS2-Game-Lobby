using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using HarmonyLib;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Patches;

public sealed class DifficultyScalingPatchesCompatibilityTests
{
    [Fact]
    public void Patches_legacy_run_state_field_player_count()
    {
        FieldInfo runState = typeof(LegacyScalingModel).GetField(nameof(LegacyScalingModel._runState))!;
        AssertPatchesPlayerCount(typeof(LegacyScalingModel), new CodeInstruction(OpCodes.Ldfld, runState));
    }

    [Fact]
    public void Patches_current_inherited_run_state_property_player_count()
    {
        MethodInfo runState = typeof(CurrentScalingModel).GetProperty(nameof(ScalingModelBase.RunState))!.GetMethod!;
        AssertPatchesPlayerCount(typeof(CurrentScalingModel), new CodeInstruction(OpCodes.Call, runState));
    }

    [Fact]
    public void Does_not_patch_an_unrelated_count_after_run_state_access()
    {
        MethodInfo runState = typeof(CurrentScalingModel).GetProperty(nameof(ScalingModelBase.RunState))!.GetMethod!;
        CodeInstruction[] instructions =
        [
            new(OpCodes.Call, runState),
            new(OpCodes.Callvirt, typeof(FakeRunState).GetProperty(nameof(FakeRunState.OtherItems))!.GetMethod),
            new(OpCodes.Callvirt, typeof(List<int>).GetProperty(nameof(List<int>.Count))!.GetMethod),
        ];

        CodeInstruction[] result = DifficultyScalingPatches.PatchPlayersCountTranspiler(
            instructions, typeof(CurrentScalingModel).GetMethod(nameof(CurrentScalingModel.ModifyBlock))!).ToArray();

        Assert.Equal(instructions, result);
    }

    [Fact]
    public void Has_no_hard_reference_to_either_scaling_model_namespace()
    {
        using PEReader pe = new(File.OpenRead(typeof(DifficultyScalingPatches).Assembly.Location));
        MetadataReader metadata = pe.GetMetadataReader();

        Assert.DoesNotContain(metadata.TypeReferences, handle =>
        {
            TypeReference reference = metadata.GetTypeReference(handle);
            return metadata.GetString(reference.Name) == "MultiplayerScalingModel"
                && metadata.GetString(reference.Namespace).StartsWith("MegaCrit.Sts2.Core.Models", StringComparison.Ordinal);
        });
    }

    private static void AssertPatchesPlayerCount(Type scalingModel, CodeInstruction runStateLoad)
    {
        MethodInfo helper = typeof(DifficultyScalingPatches).GetMethod(
            nameof(DifficultyScalingPatches.GetEffectivePlayerCount), BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo count = typeof(List<int>).GetProperty(nameof(List<int>.Count))!.GetMethod!;
        CodeInstruction[] instructions =
        [
            runStateLoad,
            new(OpCodes.Nop),
            new(OpCodes.Callvirt, typeof(FakeRunState).GetProperty(nameof(FakeRunState.Players))!.GetMethod),
            new(OpCodes.Callvirt, count),
            new(OpCodes.Pop),
            new(OpCodes.Callvirt, count),
        ];

        CodeInstruction[] result = DifficultyScalingPatches.PatchPlayersCountTranspiler(
            instructions, scalingModel.GetMethod("ModifyBlock")!).ToArray();

        Assert.Equal(instructions.Length + 1, result.Length);
        Assert.True(result[4].Calls(helper));
        Assert.Single(result, instruction => instruction.Calls(helper));
    }

    private sealed class FakeRunState
    {
        public List<int> Players { get; } = [];
        public List<int> OtherItems { get; } = [];
    }

    private sealed class LegacyScalingModel
    {
        public FakeRunState _runState = new();
        public void ModifyBlock() { }
    }

    private class ScalingModelBase
    {
        public FakeRunState RunState { get; } = new();
    }

    private sealed class CurrentScalingModel : ScalingModelBase
    {
        public void ModifyBlock() { }
    }
}
