using System.Collections.Generic;

namespace Sts2LanConnect.Scripts;

internal enum LanConnectLanHostMode
{
    Standard,
    Daily,
    Custom
}

internal readonly record struct LanConnectLanHostModeAvailability(
    bool Standard,
    bool Daily,
    bool Custom);

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

    public static bool TryResolve(
        long id,
        LanConnectLanHostModeAvailability availability,
        out LanConnectLanHostMode mode)
    {
        foreach (LanConnectLanHostModeOption option in Options)
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

    private static bool IsAvailable(
        LanConnectLanHostMode mode,
        LanConnectLanHostModeAvailability availability) => mode switch
    {
        LanConnectLanHostMode.Standard => availability.Standard,
        LanConnectLanHostMode.Daily => availability.Daily,
        LanConnectLanHostMode.Custom => availability.Custom,
        _ => false
    };
}
