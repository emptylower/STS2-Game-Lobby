using System.Reflection;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Patches;

public sealed class TreasurePatchesCompatibilityTests
{
    [Fact]
    public void Detects_current_nullable_skip_signature()
    {
        MethodInfo method = typeof(TestMethods).GetMethod(nameof(TestMethods.Current))!;

        Assert.True(TreasurePatches.UsesNativeNullableSkip(method));
    }

    [Fact]
    public void Keeps_legacy_integer_skip_on_compatibility_path()
    {
        MethodInfo method = typeof(TestMethods).GetMethod(nameof(TestMethods.Legacy))!;

        Assert.False(TreasurePatches.UsesNativeNullableSkip(method));
    }

    private static class TestMethods
    {
        public static void Current(int? index)
        {
        }

        public static void Legacy(int index)
        {
        }
    }
}
