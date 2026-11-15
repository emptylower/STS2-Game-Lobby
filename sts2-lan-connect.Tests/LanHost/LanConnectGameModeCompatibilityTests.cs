using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.LanHost;

public sealed class LanConnectGameModeCompatibilityTests
{
    [Theory]
    [InlineData(LegacyMode.Standard, "standard")]
    [InlineData(LegacyMode.Daily, "daily")]
    [InlineData(LegacyMode.Custom, "custom")]
    public void Reads_legacy_saved_mode_without_requiring_custom_flag(LegacyMode mode, string expected)
    {
        Assert.Equal(expected, LanConnectGameModeCompatibility.GetSavedRunMode(new LegacyRun { GameMode = mode }));
    }

    [Theory]
    [InlineData(CurrentMode.Standard, false, "standard")]
    [InlineData(CurrentMode.Daily, false, "daily")]
    [InlineData(CurrentMode.Dread, false, "dread")]
    [InlineData(CurrentMode.Blitz, false, "blitz")]
    [InlineData(CurrentMode.Standard, true, "custom")]
    [InlineData(CurrentMode.Dread, true, "custom")]
    [InlineData(CurrentMode.Blitz, true, "custom")]
    public void Reads_current_saved_mode_and_explicit_custom_flag(CurrentMode mode, bool custom, string expected)
    {
        Assert.Equal(expected, LanConnectGameModeCompatibility.GetSavedRunMode(new CurrentRun
        {
            GameMode = mode,
            IsCustomGame = custom
        }));
    }

    [Fact]
    public void Rejects_save_shape_without_mode_instead_of_silently_selecting_standard()
    {
        Assert.Throws<MissingMemberException>(() => LanConnectGameModeCompatibility.GetSavedRunMode(new object()));
    }

    [Theory]
    [InlineData(0, false, "standard")]
    [InlineData(0, true, "standard")]
    [InlineData(2, false, "custom")]
    [InlineData(2, true, "daily")]
    public void Routes_old_save_without_mode_using_modifiers_and_daily_time(int modifierCount, bool daily, string expected)
    {
        OldRunWithoutMode run = new()
        {
            Modifiers = Enumerable.Repeat(new object(), modifierCount).Where(_ => true),
            DailyTime = daily ? DateTime.UnixEpoch : null,
        };

        Assert.Equal(expected, LanConnectGameModeCompatibility.GetSavedRunMode(run));
    }

    [Fact]
    public void Routes_old_save_with_collection_modifiers()
    {
        Assert.Equal("custom", LanConnectGameModeCompatibility.GetSavedRunMode(new OldRunWithoutMode
        {
            Modifiers = new[] { new object() },
        }));
    }

    [Theory]
    [InlineData("Blitz", "MegaCrit.Sts2.Core.Nodes.Screens.Blitz.NBlitzScreen")]
    [InlineData("Dread", "MegaCrit.Sts2.Core.Nodes.Screens.Dread.NDreadScreen")]
    public void Additional_modes_require_both_a_named_enum_value_and_the_native_screen(string mode, string screen)
    {
        Assert.False(LanConnectGameModeCompatibility.SupportsMode(typeof(LegacyMode), _ => true, mode));
        Assert.False(LanConnectGameModeCompatibility.SupportsMode(typeof(CurrentMode), _ => false, mode));
        Assert.True(LanConnectGameModeCompatibility.SupportsMode(typeof(CurrentMode), name => name == screen, mode));
    }

    [Theory]
    [InlineData("blitz", "极速", "极速模式")]
    [InlineData(" DREAD ", "恐惧", "恐惧模式")]
    [InlineData("custom", "自定义", "自定义模式")]
    [InlineData("daily", "挑战", "多人每日挑战")]
    [InlineData("standard", "STD", "标准模式")]
    public void Room_pills_and_mode_details_distinguish_the_new_modes(string mode, string pill, string label)
    {
        Assert.Equal(pill, LanConnectGameModeCompatibility.GetRoomModePillText(mode));
        Assert.Equal(label, LanConnectGameModeCompatibility.GetModeLabel(mode));
    }

    public enum LegacyMode { Standard, Daily, Custom }
    public enum CurrentMode { Standard, Daily, Dread, Blitz }

    private sealed class LegacyRun
    {
        public LegacyMode GameMode { get; init; }
    }

    private sealed class CurrentRun
    {
        public CurrentMode GameMode { get; init; }
        public bool IsCustomGame { get; init; }
    }

    private sealed class OldRunWithoutMode
    {
        public IEnumerable<object> Modifiers { get; init; } = [];
        public DateTime? DailyTime { get; init; }
    }
}
