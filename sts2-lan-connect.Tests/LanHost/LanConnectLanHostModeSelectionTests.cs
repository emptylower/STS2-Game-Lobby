using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.LanHost;

public sealed class LanConnectLanHostModeSelectionTests
{
    [Fact]
    public void Options_keep_stable_ids_labels_and_modes()
    {
        Assert.Equal(
            new[]
            {
                new LanConnectLanHostModeOption(0, "标准模式", LanConnectLanHostMode.Standard),
                new LanConnectLanHostModeOption(1, "多人每日挑战", LanConnectLanHostMode.Daily),
                new LanConnectLanHostModeOption(2, "自定义模式", LanConnectLanHostMode.Custom)
            },
            LanConnectLanHostModeSelection.Options);
    }

    [Theory]
    [InlineData(0, (int)LanConnectLanHostMode.Standard)]
    [InlineData(1, (int)LanConnectLanHostMode.Daily)]
    [InlineData(2, (int)LanConnectLanHostMode.Custom)]
    public void Available_option_resolves_to_expected_mode(long id, int expected)
    {
        bool resolved = LanConnectLanHostModeSelection.TryResolve(
            id,
            new LanConnectLanHostModeAvailability(Standard: true, Daily: true, Custom: true),
            out LanConnectLanHostMode mode);

        Assert.True(resolved);
        Assert.Equal((LanConnectLanHostMode)expected, mode);
    }

    [Fact]
    public void Older_game_availability_does_not_offer_new_modes_even_if_their_flags_are_set()
    {
        LanConnectLanHostModeAvailability availability = new(true, true, true, Blitz: true, Dread: true);
        Assert.Equal(LanConnectLanHostModeSelection.Options, LanConnectLanHostModeSelection.GetOptions(availability));
        Assert.False(LanConnectLanHostModeSelection.TryResolve(3, availability, out _));
        Assert.False(LanConnectLanHostModeSelection.TryResolve(4, availability, out _));
    }

    [Fact]
    public void Supported_but_locked_new_modes_remain_visible_and_cannot_start()
    {
        LanConnectLanHostModeAvailability availability = new(true, true, true, SupportsBlitz: true, SupportsDread: true);
        Assert.Equal(new[] { "标准模式", "多人每日挑战", "自定义模式", "极速模式", "恐惧模式" },
            LanConnectLanHostModeSelection.GetOptions(availability).Select(option => option.Label));
        Assert.False(LanConnectLanHostModeSelection.TryResolve(3, availability, out _));
        Assert.False(LanConnectLanHostModeSelection.TryResolve(4, availability, out _));
    }

    [Theory]
    [InlineData(3, (int)LanConnectLanHostMode.Blitz)]
    [InlineData(4, (int)LanConnectLanHostMode.Dread)]
    public void Supported_and_unlocked_new_mode_resolves(long id, int expected)
    {
        Assert.True(LanConnectLanHostModeSelection.TryResolve(id,
            new(true, true, true, Blitz: true, Dread: true, SupportsBlitz: true, SupportsDread: true), out var mode));
        Assert.Equal((LanConnectLanHostMode)expected, mode);
    }

    [Fact]
    public void Mode_support_can_be_independent_for_each_new_mode()
    {
        Assert.Equal(new[] { 0, 1, 2, 4 }, LanConnectLanHostModeSelection.GetOptions(
            new(true, true, true, Dread: true, SupportsDread: true)).Select(option => option.Id));
    }

    [Fact]
    public void Unknown_option_is_rejected()
    {
        bool resolved = LanConnectLanHostModeSelection.TryResolve(
            99,
            new LanConnectLanHostModeAvailability(Standard: true, Daily: true, Custom: true),
            out _);

        Assert.False(resolved);
    }

    [Theory]
    [InlineData(0, false, true, true)]
    [InlineData(1, true, false, true)]
    [InlineData(2, true, true, false)]
    public void Locked_option_is_rejected(long id, bool standard, bool daily, bool custom)
    {
        bool resolved = LanConnectLanHostModeSelection.TryResolve(
            id,
            new LanConnectLanHostModeAvailability(standard, daily, custom),
            out _);

        Assert.False(resolved);
    }
}
