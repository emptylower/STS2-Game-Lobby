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

    [Fact]
    public void Creates_legacy_award_without_relic_index_field()
    {
        object player = new();
        object relic = new();

        LegacyResult result = Assert.IsType<LegacyResult>(TreasurePatches.CreateRelicPickingResult(
            typeof(LegacyResult), "only-one", player, relic, 5));

        Assert.Equal("only-one", result.type);
        Assert.Same(player, result.player);
        Assert.Same(relic, result.relic);
    }

    [Fact]
    public void Creates_current_award_with_original_relic_index()
    {
        object player = new();
        object relic = new();

        CurrentResult result = Assert.IsType<CurrentResult>(TreasurePatches.CreateRelicPickingResult(
            typeof(CurrentResult), "consolation", player, relic, 7));

        Assert.Equal("consolation", result.type);
        Assert.Same(player, result.player);
        Assert.Same(relic, result.relic);
        Assert.Equal(7, result.relicIndex);
    }

    [Fact]
    public void Invokes_legacy_fight_with_three_arguments()
    {
        object players = new();
        object relic = new();
        Func<int> generateMove = () => 2;

        LegacyResult result = Assert.IsType<LegacyResult>(TreasurePatches.GenerateRelicFight(
            typeof(LegacyResult), players, relic, 4, generateMove));

        Assert.Same(players, result.player);
        Assert.Same(relic, result.relic);
        Assert.Equal(2, result.type);
    }

    [Fact]
    public void Invokes_current_fight_with_original_relic_index()
    {
        object players = new();
        object relic = new();
        Func<int> generateMove = () => 1;

        CurrentResult result = Assert.IsType<CurrentResult>(TreasurePatches.GenerateRelicFight(
            typeof(CurrentResult), players, relic, 6, generateMove));

        Assert.Same(players, result.player);
        Assert.Same(relic, result.relic);
        Assert.Equal(1, result.type);
        Assert.Equal(6, result.relicIndex);
    }

    [Fact]
    public void Rejects_unknown_fight_signature_instead_of_dropping_the_award()
    {
        Assert.Throws<MissingMethodException>(() => TreasurePatches.GenerateRelicFight(
            typeof(TestMethods), new object(), new object(), 0, (Func<int>)(() => 0)));
    }

    private class LegacyResult
    {
        public object? type;
        public object? player;
        public object? relic;

        public LegacyResult() { }

        public static LegacyResult GenerateRelicFight(object players, object relic, Func<int> generateMove) =>
            new() { player = players, relic = relic, type = generateMove() };
    }

    private sealed class CurrentResult : LegacyResult
    {
        public int relicIndex;

        public CurrentResult() { }

        public static CurrentResult GenerateRelicFight(object players, object relic, int relicIndex, Func<int> generateMove) =>
            new() { player = players, relic = relic, relicIndex = relicIndex, type = generateMove() };
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
