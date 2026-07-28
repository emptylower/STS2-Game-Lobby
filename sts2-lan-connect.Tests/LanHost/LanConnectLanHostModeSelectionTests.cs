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
