using System.Collections.Generic;

namespace Sts2LanConnect.Scripts;

internal enum LanConnectLanHostMode
{
    Standard,
    Daily,
    Custom,
    Blitz,
    Dread
}

internal readonly record struct LanConnectLanHostModeAvailability(
    bool Standard,
    bool Daily,
    bool Custom,
    bool Blitz = false,
    bool Dread = false,
    bool SupportsBlitz = false,
    bool SupportsDread = false);

internal readonly record struct LanConnectLanHostModeOption(
    int Id,
    string Label,
    LanConnectLanHostMode Mode);

internal static class LanConnectLanHostModeSelection
{
    public static IReadOnlyList<LanConnectLanHostModeOption> Options { get; } =
    [
        new(0, "标准模式", LanConnectLanHostMode.Standard),
        new(1, "多人每日挑战", LanConnectLanHostMode.Daily),
        new(2, "自定义模式", LanConnectLanHostMode.Custom)
    ];

    public static IReadOnlyList<LanConnectLanHostModeOption> GetOptions(
        LanConnectLanHostModeAvailability availability)
    {
        List<LanConnectLanHostModeOption> options = new(Options);
        if (availability.SupportsBlitz)
        {
            options.Add(new(3, "极速模式", LanConnectLanHostMode.Blitz));
        }

        if (availability.SupportsDread)
        {
            options.Add(new(4, "恐惧模式", LanConnectLanHostMode.Dread));
        }

        return options;
    }

    public static bool TryResolve(
        long id,
        LanConnectLanHostModeAvailability availability,
        out LanConnectLanHostMode mode)
    {
        foreach (LanConnectLanHostModeOption option in GetOptions(availability))
        {
            if (option.Id != id || !IsAvailable(option.Mode, availability))
            {
                continue;
            }

            mode = option.Mode;
            return true;
        }

        mode = default;
        return false;
    }

    internal static bool IsAvailable(
        LanConnectLanHostMode mode,
        LanConnectLanHostModeAvailability availability) => mode switch
    {
        LanConnectLanHostMode.Standard => availability.Standard,
        LanConnectLanHostMode.Daily => availability.Daily,
        LanConnectLanHostMode.Custom => availability.Custom,
        LanConnectLanHostMode.Blitz => availability.SupportsBlitz && availability.Blitz,
        LanConnectLanHostMode.Dread => availability.SupportsDread && availability.Dread,
        _ => false
    };
}
