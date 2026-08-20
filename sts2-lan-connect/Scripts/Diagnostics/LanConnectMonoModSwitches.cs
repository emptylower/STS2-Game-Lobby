using System.Reflection;
using HarmonyLib;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectMonoModSwitches
{
    public const string DmdDumpTo = "DMDDumpTo";

    private static readonly Type? SwitchesType =
        typeof(Harmony).Assembly.GetType("MonoMod.Utils.Switches", throwOnError: false) ??
        typeof(Harmony).Assembly.GetType("MonoMod.Switches", throwOnError: false);

    private static readonly MethodInfo? TryGetSwitchValueMethod = FindMethod(
        "TryGetSwitchValue",
        typeof(string),
        typeof(object).MakeByRefType());

    private static readonly MethodInfo? SetSwitchValueMethod = FindMethod(
        "SetSwitchValue",
        typeof(string),
        typeof(object));

    private static readonly MethodInfo? ClearSwitchValueMethod = FindMethod(
        "ClearSwitchValue",
        typeof(string));

    public static bool IsAvailable =>
        TryGetSwitchValueMethod != null && SetSwitchValueMethod != null && ClearSwitchValueMethod != null;

    public static bool TryGetValue(string switchName, out object? value)
    {
        if (TryGetSwitchValueMethod == null)
        {
            value = null;
            return false;
        }

        object?[] arguments = [switchName, null];
        bool found = TryGetSwitchValueMethod.Invoke(null, arguments) is true;
        value = arguments[1];
        return found;
    }

    public static void SetValue(string switchName, object? value)
    {
        if (SetSwitchValueMethod == null)
        {
            throw new MissingMethodException("MonoMod switch setter is unavailable.");
        }
        _ = SetSwitchValueMethod.Invoke(null, [switchName, value]);
    }

    public static void ClearValue(string switchName)
    {
        if (ClearSwitchValueMethod == null)
        {
            throw new MissingMethodException("MonoMod switch clearer is unavailable.");
        }
        _ = ClearSwitchValueMethod.Invoke(null, [switchName]);
    }

    private static MethodInfo? FindMethod(string name, params Type[] parameterTypes) =>
        SwitchesType?.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: parameterTypes,
            modifiers: null);
}
