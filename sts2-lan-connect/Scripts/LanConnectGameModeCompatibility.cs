using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectGameModeCompatibility
{
    // 0.113 moved custom runs out of GameMode. Keep the UI choice separate from
    // real enum values: the old Custom value is now used by Dread.
    internal static GameMode Custom => Enum.TryParse("Custom", out GameMode mode) ? mode : (GameMode)(-1);

    internal static bool IsCustom(GameMode mode) => mode == Custom;

    internal static string GetLobbyMode(GameMode mode) => IsCustom(mode)
        ? "custom"
        : mode.ToString().ToLowerInvariant();

    internal static bool SupportsMode(string modeName) => SupportsMode(
        typeof(GameMode),
        typeName => typeof(GameMode).Assembly.GetType(typeName) != null,
        modeName);

    internal static bool SupportsMode(Type gameModeType, Func<string, bool> hasScreen, string modeName) =>
        (modeName is "Blitz" or "Dread")
        && Enum.GetNames(gameModeType).Contains(modeName, StringComparer.Ordinal)
        && hasScreen(GetAdditionalHostScreenTypeName(modeName));

    internal static GameMode ResolveAdditionalMode(string modeName)
    {
        if (SupportsMode(modeName) && Enum.TryParse(modeName, out GameMode mode))
        {
            return mode;
        }

        throw new NotSupportedException($"当前游戏版本不支持{GetModeLabel(modeName)}。");
    }

    internal static string GetAdditionalHostScreenTypeName(string modeName) => modeName switch
    {
        "Blitz" => "MegaCrit.Sts2.Core.Nodes.Screens.Blitz.NBlitzScreen",
        "Dread" => "MegaCrit.Sts2.Core.Nodes.Screens.Dread.NDreadScreen",
        _ => throw new ArgumentOutOfRangeException(nameof(modeName), modeName, "Not an additional host mode."),
    };

    internal static string GetModeLabel(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "blitz" => "极速模式",
        "dread" => "恐惧模式",
        "daily" => "多人每日挑战",
        "custom" => "自定义模式",
        "standard" or "" or null => "标准模式",
        _ => mode.Trim(),
    };

    internal static string GetRoomModePillText(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "daily" => "挑战",
        "custom" => "自定义",
        "blitz" => "极速",
        "dread" => "恐惧",
        _ => "STD",
    };

    internal static string GetSavedRunMode(object run)
    {
        Type type = run.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        if (type.GetProperty("IsCustomGame", flags)?.GetValue(run) is true)
        {
            return "custom";
        }

        PropertyInfo? modeProperty = type.GetProperty("GameMode", flags);
        if (modeProperty != null)
        {
            return modeProperty.GetValue(run)?.ToString()?.ToLowerInvariant()
                ?? throw new InvalidOperationException($"{type.FullName}.GameMode is null.");
        }

        // Older saves predate GameMode. Preserve their original routing: modifiers
        // distinguish custom/daily runs, while an unmodified run is standard.
        object? modifiers = (type.GetProperty("Modifiers", flags)
            ?? throw new MissingMemberException(type.FullName, "Modifiers")).GetValue(run);
        bool hasModifiers = modifiers switch
        {
            ICollection collection => collection.Count > 0,
            IEnumerable sequence => sequence.Cast<object?>().Any(),
            _ => throw new InvalidOperationException($"{type.FullName}.Modifiers is not a collection."),
        };
        if (!hasModifiers)
        {
            return "standard";
        }

        PropertyInfo dailyTime = type.GetProperty("DailyTime", flags)
            ?? throw new MissingMemberException(type.FullName, "DailyTime");
        return dailyTime.GetValue(run) != null ? "daily" : "custom";
    }
}
