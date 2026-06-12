using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectAccessibilityBridge
{
    private const string UiManagerTypeName = "SayTheSpire2.UI.UIManager";

    private static MethodInfo? _setFocusedControlMethod;
    private static bool _initialized;
    private static bool _disabled;

    public static bool IsAvailable => _initialized && !_disabled && _setFocusedControlMethod != null;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _setFocusedControlMethod = TryFindSetFocusedControlMethod(AppDomain.CurrentDomain.GetAssemblies());
        Log.Info(_setFocusedControlMethod == null
            ? "sts2_lan_connect accessibility_bridge: say-the-spire2 SetFocusedControl not available"
            : "sts2_lan_connect accessibility_bridge: say-the-spire2 SetFocusedControl detected");
    }

    public static bool TryAnnounce(Control control)
    {
        if (!_initialized)
        {
            Initialize();
        }

        if (_disabled || _setFocusedControlMethod == null || !GodotObject.IsInstanceValid(control))
        {
            return false;
        }

        try
        {
            _setFocusedControlMethod.Invoke(null, new object?[] { control, null });
            return true;
        }
        catch (Exception ex)
        {
            _disabled = true;
            Log.Warn($"sts2_lan_connect accessibility_bridge: disabled after invocation failure: {ex.Message}");
            return false;
        }
    }

    internal static MethodInfo? TryFindSetFocusedControlMethod(
        IEnumerable<Assembly> assemblies,
        string uiManagerTypeName = UiManagerTypeName)
    {
        Type? uiManagerType = assemblies
            .Select(assembly => assembly.GetType(uiManagerTypeName, throwOnError: false))
            .FirstOrDefault(type => type != null);

        MethodInfo? method = uiManagerType?.GetMethod(
            "SetFocusedControl",
            BindingFlags.Public | BindingFlags.Static);
        if (method == null)
        {
            return null;
        }

        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length != 2)
        {
            return null;
        }

        if (!typeof(Control).IsAssignableFrom(parameters[0].ParameterType))
        {
            return null;
        }

        return method;
    }
}
