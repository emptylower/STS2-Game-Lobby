using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

// A small, bounded trace for one reference's render -> activation -> preview path.
// Only explicitly allowed metadata is logged; chat text and exception messages are excluded.
internal sealed class LanConnectReferenceDiagnostics
{
    internal static LanConnectReferenceDiagnostics Shared { get; set; } = new(message => Log.Info(message));
    internal const int DefaultBudget = 240;
    private const int RepeatsPerReason = 12;
    private static readonly HashSet<string> AllowedFields =
    [
        "view_id", "reference_id", "model_id", "item_type", "upgrade", "status",
        "segments", "item_refs", "references", "content_kind", "source", "input",
        "device", "index", "pressed", "cancelled", "position", "visible", "visible_in_tree", "in_tree",
        "bounds", "actual_bounds", "viewport", "preview_type", "pinned", "cache", "spans"
    ];

    private readonly Action<string> _sink;
    private readonly int _budget;
    private readonly Dictionary<string, int> _repeats = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private int _sequence;

    internal LanConnectReferenceDiagnostics(Action<string> sink, int budget = DefaultBudget)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _budget = Math.Max(0, budget);
    }

    internal static int Identity(object value) => RuntimeHelpers.GetHashCode(value);

    internal void Write(string stage, string reason, params (string Key, object? Value)[] fields) =>
        Emit(stage, reason, null, fields);

    internal void Failure(string stage, string reason, Exception exception,
        params (string Key, object? Value)[] fields) => Emit(stage, reason, exception, fields);

    private void Emit(string stage, string reason, Exception? exception,
        (string Key, object? Value)[] fields)
    {
        try
        {
            Dictionary<string, object?> data = new()
            {
                ["stage"] = Limit(stage),
                ["reason"] = Limit(reason)
            };
            foreach ((string key, object? value) in fields)
            {
                if (AllowedFields.Contains(key))
                {
                    data[key] = value is null or bool or int or long or uint or ulong or float or double
                        ? value
                        : Limit(value.ToString());
                }
            }
            if (exception != null)
            {
                if (exception is TargetInvocationException { InnerException: { } inner })
                {
                    exception = inner;
                }
                data["exception"] = exception.GetType().FullName;
                data["stack"] = new StackTrace(exception, false).GetFrames()?
                    .Take(4)
                    .Select(frame => frame.GetMethod())
                    .Where(method => method != null)
                    .Select(method => $"{method!.DeclaringType?.FullName}.{method.Name}")
                    .ToArray() ?? [];
            }

            lock (_sync)
            {
                if (_sequence >= _budget)
                {
                    return;
                }
                string key = stage + "|" + reason;
                int count = _repeats.GetValueOrDefault(key);
                if (count >= RepeatsPerReason)
                {
                    return;
                }
                _repeats[key] = count + 1;
                data["sequence"] = ++_sequence;
                _sink("sts2_lan_connect reference_diag: " + JsonSerializer.Serialize(data));
            }
        }
        catch
        {
            // Logging must never break chat, including test hosts without Godot native logging.
        }
    }

    private static string Limit(string? value) => value is { Length: > 160 } ? value[..160] : value ?? string.Empty;
}
