using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectStartupDiagnostics : IDisposable
{
    private const string MirrorPrefix = "sts2_lan_connect patch_diag: ";
    private const string SentinelFileName = "init-sentinel.json";
    private const string StartupLogFileName = "startup.jsonl";

    private static readonly object CurrentSync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _sync = new();
    private readonly LanConnectStartupDiagnosticsOptions _options;
    private readonly bool _enabled;
    private readonly string _sessionId;
    private readonly string _sessionDirectoryName;
    private readonly string _sessionDirectory;
    private readonly string _startupLogPath;
    private readonly string _sentinelPath;
    private readonly LanConnectHarmonyDiagnosticScope? _harmonyScope;
    private long _sequence;
    private SentinelState? _sentinel;
    private bool _completed;
    private bool _disposed;

    private LanConnectStartupDiagnostics(LanConnectStartupDiagnosticsOptions options)
    {
        _options = options;
        _sessionId = string.Empty;
        _sessionDirectoryName = string.Empty;
        _sessionDirectory = string.Empty;
        _startupLogPath = string.Empty;
        _sentinelPath = string.Empty;
    }

    private LanConnectStartupDiagnostics(
        LanConnectStartupDiagnosticsOptions options,
        string diagnosticsRoot,
        string sessionId,
        string sessionDirectoryName,
        string sessionDirectory,
        LanConnectHarmonyDiagnosticScope? harmonyScope)
    {
        _options = options;
        _enabled = true;
        _sessionId = sessionId;
        _sessionDirectoryName = sessionDirectoryName;
        _sessionDirectory = sessionDirectory;
        _startupLogPath = Path.Combine(sessionDirectory, StartupLogFileName);
        _sentinelPath = Path.Combine(diagnosticsRoot, SentinelFileName);
        _harmonyScope = harmonyScope;
    }

    internal static LanConnectStartupDiagnostics? Current
    {
        get
        {
            lock (CurrentSync)
            {
                return _current;
            }
        }
    }
    private static LanConnectStartupDiagnostics? _current;

    internal string SessionDirectory => _sessionDirectory;

    public static LanConnectStartupDiagnostics CreateDefault()
    {
        LanConnectStartupDiagnosticsOptions? options = null;
        try
        {
            options = new LanConnectStartupDiagnosticsOptions
            {
                DiagnosticsRoot = Path.Combine(LanConnectPaths.ResolveWritableDataDirectory(), "diagnostics"),
                MirrorInfo = static message => Log.Info(message),
                Warn = static message => Log.Warn(message)
            };
            return Create(options);
        }
        catch (Exception exception)
        {
            Action<string> warn = options?.Warn ?? (static message => Log.Warn(message));
            WarnSafely(warn, "session_create", exception);
            return new LanConnectStartupDiagnostics(options ?? new LanConnectStartupDiagnosticsOptions
            {
                DiagnosticsRoot = string.Empty,
                Warn = warn
            });
        }
    }

    internal static LanConnectStartupDiagnostics CreateForTesting(LanConnectStartupDiagnosticsOptions options) =>
        Create(options);

    private static LanConnectStartupDiagnostics Create(LanConnectStartupDiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        LanConnectStartupDiagnostics? diagnostics = null;
        try
        {
            string diagnosticsRoot = Path.GetFullPath(options.DiagnosticsRoot);
            Directory.CreateDirectory(diagnosticsRoot);

            DateTimeOffset now = options.UtcNow().ToUniversalTime();
            string sessionId = NormalizeIdentifier(options.SessionIdFactory(), "session");
            string sessionDirectoryName = $"{now:yyyyMMddTHHmmss.fffZ}-{sessionId}";
            string sessionDirectory = Path.Combine(diagnosticsRoot, sessionDirectoryName);
            Directory.CreateDirectory(sessionDirectory);

            LanConnectHarmonyDiagnosticScope? harmonyScope = options.EnableHarmonyDiagnostics
                ? LanConnectHarmonyDiagnosticScope.TryEnable(sessionDirectory, options.Warn)
                : null;
            diagnostics = new LanConnectStartupDiagnostics(
                options,
                diagnosticsRoot,
                sessionId,
                sessionDirectoryName,
                sessionDirectory,
                harmonyScope);

            diagnostics.BeginSession();
            lock (CurrentSync)
            {
                _current = diagnostics;
            }
            return diagnostics;
        }
        catch (Exception exception)
        {
            diagnostics?.Dispose();
            WarnSafely(options.Warn, "session_create", exception);
            return new LanConnectStartupDiagnostics(options);
        }
    }

    public void RunStage(string stageId, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!_enabled)
        {
            action();
            return;
        }

        string normalizedStageId = NormalizeIdentifier(stageId, "unknown_stage");
        int ordinal = LanConnectStartupStages.GetOrdinal(normalizedStageId);
        long startedTimestamp = Stopwatch.GetTimestamp();
        TryDiagnosticOperation(
            "stage_begin",
            () => RecordStage(normalizedStageId, ordinal, "begin", elapsedMilliseconds: null, exception: null));

        try
        {
            action();
        }
        catch (Exception exception)
        {
            TryDiagnosticOperation(
                "stage_failure",
                () => RecordStage(
                    normalizedStageId,
                    ordinal,
                    "failure",
                    Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                    exception));
            _harmonyScope?.Flush();
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }

        TryDiagnosticOperation(
            "stage_success",
            () => RecordStage(
                normalizedStageId,
                ordinal,
                "success",
                Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                exception: null));
    }

    public long RecordPatchBegin(LanConnectPatchDiagnosticDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        long startedTimestamp = Stopwatch.GetTimestamp();
        if (_enabled)
        {
            TryDiagnosticOperation(
                "patch_begin",
                () => RecordPatch(descriptor, "begin", elapsedMilliseconds: null, exception: null));
        }
        return startedTimestamp;
    }

    public void RecordPatchSuccess(LanConnectPatchDiagnosticDescriptor descriptor, long startedTimestamp)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (_enabled)
        {
            TryDiagnosticOperation(
                "patch_success",
                () => RecordPatch(
                    descriptor,
                    "success",
                    Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                    exception: null));
        }
    }

    public void RecordPatchFailure(
        LanConnectPatchDiagnosticDescriptor descriptor,
        long startedTimestamp,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(exception);
        if (_enabled)
        {
            TryDiagnosticOperation(
                "patch_failure",
                () => RecordPatch(
                    descriptor,
                    "failure",
                    Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                    exception));
            _harmonyScope?.Flush();
        }
    }

    public void Complete()
    {
        if (!_enabled || _completed)
        {
            return;
        }

        TryDiagnosticOperation("initialization_complete", CompleteCore);
    }

    private void CompleteCore()
    {
        lock (_sync)
        {
            if (_completed)
            {
                return;
            }

            long sequence = NextSequence();
            _sentinel = new SentinelState
            {
                SchemaVersion = 1,
                SessionId = _sessionId,
                SessionDirectory = _sessionDirectoryName,
                StartedAtUtc = _sentinel?.StartedAtUtc ?? _options.UtcNow().ToUniversalTime(),
                UpdatedAtUtc = _options.UtcNow().ToUniversalTime(),
                Sequence = sequence,
                Completed = true,
                Status = "success",
                Stage = "initialization_complete"
            };
            TryWriteSentinel();
            TryEmit(
                sequence,
                "initialization",
                new Dictionary<string, object?>
                {
                    ["status"] = "success",
                    ["stage_count"] = LanConnectStartupStages.Ordered.Count
                });
            _completed = true;
        }

        TryCleanupOldSessions();
    }

    internal void RecordInfo(string eventName, IReadOnlyDictionary<string, object?> fields)
    {
        if (!_enabled)
        {
            return;
        }

        TryDiagnosticOperation("record_info", () =>
        {
            lock (_sync)
            {
                TryEmit(NextSequence(), NormalizeIdentifier(eventName, "diagnostic_info"), fields);
            }
        });
    }

    internal void Warn(string operation, Exception exception) =>
        WarnSafely(_options.Warn, NormalizeIdentifier(operation, "diagnostic_operation"), exception);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            _harmonyScope?.Dispose();
        }
        catch (Exception exception)
        {
            Warn("harmony_scope_dispose", exception);
        }

        lock (CurrentSync)
        {
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        }
    }

    private void BeginSession()
    {
        SentinelState? previous = TryReadSentinel();
        if (previous is { Completed: false })
        {
            TryEmit(
                NextSequence(),
                "previous_init_incomplete",
                new Dictionary<string, object?>
                {
                    ["previous_stage"] = previous.Stage,
                    ["previous_patch_id"] = previous.PatchId,
                    ["previous_sequence"] = previous.Sequence,
                    ["previous_status"] = previous.Status
                });
        }

        DateTimeOffset now = _options.UtcNow().ToUniversalTime();
        long sequence = NextSequence();
        _sentinel = new SentinelState
        {
            SchemaVersion = 1,
            SessionId = _sessionId,
            SessionDirectory = _sessionDirectoryName,
            StartedAtUtc = now,
            UpdatedAtUtc = now,
            Sequence = sequence,
            Completed = false,
            Status = "begin",
            Stage = "session_create"
        };
        TryWriteSentinel();
        TryEmit(
            sequence,
            "session",
            new Dictionary<string, object?>
            {
                ["status"] = "begin",
                ["platform"] = GetPlatformName(),
                ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["framework"] = RuntimeInformation.FrameworkDescription,
                ["is_android"] = OperatingSystem.IsAndroid()
            });

        if (_options.CaptureArtifacts)
        {
            try
            {
                LanConnectStartupArtifactCollector.Capture(this);
            }
            catch (Exception exception)
            {
                Warn("artifact_capture", exception);
            }
        }
    }

    private void RecordStage(
        string stageId,
        int ordinal,
        string status,
        double? elapsedMilliseconds,
        Exception? exception)
    {
        lock (_sync)
        {
            long sequence = NextSequence();
            LanConnectDiagnosticException? exceptionDescription = exception == null
                ? null
                : LanConnectDiagnosticRedactor.DescribeException(exception);
            _sentinel = (_sentinel ?? new SentinelState()) with
            {
                UpdatedAtUtc = _options.UtcNow().ToUniversalTime(),
                Sequence = sequence,
                Completed = false,
                Status = status,
                Stage = stageId,
                PatchId = string.Equals(status, "begin", StringComparison.Ordinal)
                    ? null
                    : _sentinel?.PatchId,
                ExceptionFingerprint = exceptionDescription?.Fingerprint
            };
            TryWriteSentinel();

            Dictionary<string, object?> fields = new()
            {
                ["status"] = status,
                ["stage"] = stageId,
                ["ordinal"] = ordinal,
                ["total"] = LanConnectStartupStages.Ordered.Count,
                ["elapsed_ms"] = RoundElapsed(elapsedMilliseconds)
            };
            AddExceptionFields(fields, exceptionDescription);
            TryEmit(sequence, "init_stage", fields);
        }
    }

    private void RecordPatch(
        LanConnectPatchDiagnosticDescriptor descriptor,
        string status,
        double? elapsedMilliseconds,
        Exception? exception)
    {
        lock (_sync)
        {
            long sequence = NextSequence();
            LanConnectDiagnosticException? exceptionDescription = exception == null
                ? null
                : LanConnectDiagnosticRedactor.DescribeException(exception);
            _sentinel = (_sentinel ?? new SentinelState()) with
            {
                UpdatedAtUtc = _options.UtcNow().ToUniversalTime(),
                Sequence = sequence,
                Completed = false,
                Status = status,
                PatchId = descriptor.PlanId,
                ExceptionFingerprint = exceptionDescription?.Fingerprint
            };
            TryWriteSentinel();

            Dictionary<string, object?> fields = BuildPatchFields(descriptor, status, elapsedMilliseconds);
            AddExceptionFields(fields, exceptionDescription);
            TryEmit(sequence, "patch", fields);
        }
    }

    private static Dictionary<string, object?> BuildPatchFields(
        LanConnectPatchDiagnosticDescriptor descriptor,
        string status,
        double? elapsedMilliseconds)
    {
        MethodBase target = descriptor.Target;
        MethodInfo hook = descriptor.Hook;
        IReadOnlyList<MethodInfo> hooks = descriptor.AllHooks;
        return new Dictionary<string, object?>
        {
            ["status"] = status,
            ["plan_id"] = descriptor.PlanId,
            ["plan_profile"] = descriptor.PlanProfile,
            ["ordinal"] = descriptor.Ordinal,
            ["total"] = descriptor.Total,
            ["category"] = descriptor.Category,
            ["message_type"] = descriptor.MessageType,
            ["target"] = FormatMethod(target),
            ["hook"] = FormatMethod(hook),
            ["hooks"] = hooks.Select(FormatMethod).ToArray(),
            ["hook_count"] = hooks.Count,
            ["hook_priorities"] = descriptor.AllHookPriorities,
            ["target_is_generic_method"] = target.IsGenericMethod,
            ["target_contains_generic_parameters"] = target.ContainsGenericParameters,
            ["target_generic_arguments"] = FormatGenericArguments(target),
            ["hook_is_generic_method"] = hook.IsGenericMethod,
            ["hook_contains_generic_parameters"] = hook.ContainsGenericParameters,
            ["hook_generic_arguments"] = FormatGenericArguments(hook),
            ["hooks_are_generic_methods"] = hooks.Select(static method => method.IsGenericMethod).ToArray(),
            ["hooks_contain_generic_parameters"] = hooks
                .Select(static method => method.ContainsGenericParameters)
                .ToArray(),
            ["metadata_token"] = TryGetMetadataToken(target),
            ["module_mvid"] = TryGetModuleMvid(target),
            ["harmony_owner"] = descriptor.HarmonyOwner,
            ["harmony_priority"] = descriptor.HarmonyPriority,
            ["elapsed_ms"] = RoundElapsed(elapsedMilliseconds)
        };
    }

    private static void AddExceptionFields(
        IDictionary<string, object?> fields,
        LanConnectDiagnosticException? description)
    {
        if (description == null)
        {
            return;
        }

        fields["exception_type"] = description.Type;
        fields["exception_hresult"] = $"0x{description.HResult:X8}";
        fields["exception_stack"] = description.Stack;
        fields["exception_fingerprint"] = description.Fingerprint;
    }

    private void TryEmit(long sequence, string eventName, IReadOnlyDictionary<string, object?> fields)
    {
        Dictionary<string, object?> payload = new()
        {
            ["timestamp_utc"] = _options.UtcNow().ToUniversalTime().ToString("O"),
            ["session_id"] = _sessionId,
            ["sequence"] = sequence,
            ["event"] = eventName
        };
        foreach ((string key, object? value) in fields)
        {
            payload[NormalizeIdentifier(key, "field")] = SanitizeValue(value);
        }

        string json;
        try
        {
            json = JsonSerializer.Serialize(payload, JsonOptions);
        }
        catch (Exception exception)
        {
            Warn("event_serialize", exception);
            json = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["timestamp_utc"] = _options.UtcNow().ToUniversalTime().ToString("O"),
                ["session_id"] = _sessionId,
                ["sequence"] = sequence,
                ["event"] = "diagnostic_serialization_failure"
            }, JsonOptions);
        }

        try
        {
            using FileStream stream = new(
                _startupLogPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);
            using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        catch (Exception exception)
        {
            Warn("startup_jsonl_write", exception);
        }

        try
        {
            _options.MirrorInfo(MirrorPrefix + json);
        }
        catch
        {
        }
    }

    private SentinelState? TryReadSentinel()
    {
        try
        {
            if (!File.Exists(_sentinelPath))
            {
                return null;
            }
            return JsonSerializer.Deserialize<SentinelState>(File.ReadAllText(_sentinelPath), JsonOptions);
        }
        catch (Exception exception)
        {
            Warn("sentinel_read", exception);
            return null;
        }
    }

    private void TryWriteSentinel()
    {
        if (_sentinel == null)
        {
            return;
        }

        string temporaryPath = _sentinelPath + $".{_sessionId}.tmp";
        try
        {
            string json = JsonSerializer.Serialize(_sentinel, JsonOptions);
            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            using (StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _sentinelPath, overwrite: true);
        }
        catch (Exception exception)
        {
            Warn("sentinel_write", exception);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception)
            {
                Warn("sentinel_temp_cleanup", exception);
            }
        }
    }

    private void TryCleanupOldSessions()
    {
        try
        {
            string diagnosticsRoot = Path.GetDirectoryName(_sentinelPath)
                ?? throw new InvalidOperationException("Diagnostics root is unavailable.");
            DirectoryInfo root = new(diagnosticsRoot);
            List<DirectoryInfo> sessions = root.EnumerateDirectories()
                .Where(static directory => IsSessionDirectoryName(directory.Name))
                .OrderBy(static directory => directory.Name, StringComparer.Ordinal)
                .ToList();
            long totalBytes = sessions.Sum(GetDirectorySizeBestEffort);

            while ((sessions.Count > Math.Max(1, _options.MaxSessions) ||
                    totalBytes > Math.Max(0, _options.MaxTotalBytes)) &&
                   sessions.Count > 1)
            {
                DirectoryInfo? candidate = sessions.FirstOrDefault(directory =>
                    !string.Equals(directory.FullName, _sessionDirectory, StringComparison.Ordinal));
                if (candidate == null)
                {
                    break;
                }

                long candidateBytes = GetDirectorySizeBestEffort(candidate);
                if (!IsDirectChild(diagnosticsRoot, candidate.FullName))
                {
                    sessions.Remove(candidate);
                    continue;
                }

                try
                {
                    candidate.Delete(recursive: true);
                    sessions.Remove(candidate);
                    totalBytes = Math.Max(0, totalBytes - candidateBytes);
                }
                catch (Exception exception)
                {
                    Warn("session_cleanup", exception);
                    sessions.Remove(candidate);
                }
            }
        }
        catch (Exception exception)
        {
            Warn("session_rotation", exception);
        }
    }

    private long GetDirectorySizeBestEffort(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(static file => file.Length);
        }
        catch (Exception exception)
        {
            Warn("session_size", exception);
            return 0;
        }
    }

    private static bool IsDirectChild(string parent, string candidate)
    {
        string normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        string? candidateParent = Path.GetDirectoryName(Path.GetFullPath(candidate));
        return string.Equals(normalizedParent, candidateParent, StringComparison.Ordinal);
    }

    private static bool IsSessionDirectoryName(string name)
    {
        const int timestampLength = 20;
        if (name.Length <= timestampLength || name[timestampLength] != '-')
        {
            return false;
        }
        if (!DateTimeOffset.TryParseExact(
                name[..timestampLength],
                "yyyyMMdd'T'HHmmss.fff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out _))
        {
            return false;
        }
        return name[(timestampLength + 1)..].All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');
    }

    private long NextSequence() => ++_sequence;

    private static object? SanitizeValue(object? value) => value switch
    {
        null => null,
        string text => LanConnectDiagnosticRedactor.RedactText(text),
        IEnumerable<string> strings => strings.Select(LanConnectDiagnosticRedactor.RedactText).ToArray(),
        _ => value
    };

    private static string NormalizeIdentifier(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        StringBuilder builder = new(Math.Min(value.Length, 128));
        foreach (char character in value.Take(128))
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.'
                ? character
                : '_');
        }
        return builder.ToString();
    }

    private static string FormatMethod(MethodBase method)
    {
        string declaringType = method.DeclaringType?.FullName ?? "<global>";
        string parameters = string.Join(",", method.GetParameters().Select(static parameter =>
            $"{FormatType(parameter.ParameterType)} {parameter.Name}"));
        string returnType = method is MethodInfo methodInfo ? FormatType(methodInfo.ReturnType) : "void";
        return $"{returnType} {declaringType}.{method.Name}({parameters})";
    }

    private static string FormatType(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        string name = type.GetGenericTypeDefinition().FullName ?? type.Name;
        int marker = name.IndexOf('`');
        if (marker >= 0)
        {
            name = name[..marker];
        }
        return $"{name}<{string.Join(",", type.GetGenericArguments().Select(FormatType))}>";
    }

    private static string[] FormatGenericArguments(MethodBase method) => method.IsGenericMethod
        ? method.GetGenericArguments().Select(FormatType).ToArray()
        : [];

    private static int? TryGetMetadataToken(MethodBase method)
    {
        try
        {
            return method.MetadataToken;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetModuleMvid(MethodBase method)
    {
        try
        {
            return method.Module.ModuleVersionId.ToString("D");
        }
        catch
        {
            return null;
        }
    }

    private static double? RoundElapsed(double? elapsedMilliseconds) =>
        elapsedMilliseconds.HasValue ? Math.Round(elapsedMilliseconds.Value, 3) : null;

    private void TryDiagnosticOperation(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Warn(operation, exception);
        }
    }

    private static string GetPlatformName()
    {
        if (OperatingSystem.IsAndroid())
        {
            return "android";
        }
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }
        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }
        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }
        return "unknown";
    }

    private static void WarnSafely(Action<string> warn, string operation, Exception exception)
    {
        try
        {
            LanConnectDiagnosticException description = LanConnectDiagnosticRedactor.DescribeException(exception);
            warn(
                $"sts2_lan_connect patch diagnostics warning: operation={operation} " +
                $"exception={description.Type} hresult=0x{description.HResult:X8} fingerprint={description.Fingerprint}");
        }
        catch
        {
        }
    }

    private sealed record SentinelState
    {
        public int SchemaVersion { get; init; } = 1;
        public string SessionId { get; init; } = string.Empty;
        public string SessionDirectory { get; init; } = string.Empty;
        public DateTimeOffset StartedAtUtc { get; init; }
        public DateTimeOffset UpdatedAtUtc { get; init; }
        public long Sequence { get; init; }
        public bool Completed { get; init; }
        public string Status { get; init; } = string.Empty;
        public string Stage { get; init; } = string.Empty;
        public string? PatchId { get; init; }
        public string? ExceptionFingerprint { get; init; }
    }
}
