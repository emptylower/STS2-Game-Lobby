using System.Reflection;
using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Diagnostics;

[Collection("Startup diagnostics globals")]
public sealed class LanConnectStartupDiagnosticsTests
{
    [Fact]
    public void Entry_creates_the_session_before_logging_and_wraps_all_ten_stages()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "sts2-lan-connect",
            "Scripts",
            "Entry.cs"));

        int createIndex = source.IndexOf("LanConnectStartupDiagnostics.CreateDefault()", StringComparison.Ordinal);
        int firstLogIndex = source.IndexOf("Log.Info(", StringComparison.Ordinal);

        Assert.True(createIndex >= 0);
        Assert.True(firstLogIndex > createIndex);
        Assert.Equal(11, CountOccurrences(source, "diagnostics.RunStage("));
        Assert.Equal(
            [
                "config_load",
                "external_mod_detection",
                "tail_runtime_configure",
                "native_bus_startup_check",
                "sentry_compatibility",
                "accessibility_bridge",
                "multiplayer_compatibility",
                "gameplay_patches",
                "scene_ready_patches",
                "lobby_runtime",
                "room_chat_overlay"
            ],
            LanConnectStartupStages.Ordered);

        string[] expectedBindings =
        [
            "LanConnectStartupStages.ConfigLoad, LanConnectConfig.Load",
            "LanConnectStartupStages.ExternalModDetection, LanConnectExternalModDetection.Detect",
            "LanConnectStartupStages.TailRuntimeConfigure,",
            "LanConnectStartupStages.SentryCompatibility, LanConnectSentryCompatibilityPatches.Initialize",
            "LanConnectStartupStages.AccessibilityBridge, LanConnectAccessibilityBridge.Initialize",
            "LanConnectStartupStages.MultiplayerCompatibility, LanConnectMultiplayerCompatibility.Initialize",
            "LanConnectStartupStages.GameplayPatches, LanConnectGameplayPatches.Initialize",
            "LanConnectStartupStages.SceneReadyPatches, LanConnectSceneReadyPatches.Apply",
            "LanConnectStartupStages.LobbyRuntime,",
            "LanConnectStartupStages.RoomChatOverlay, LanConnectRoomChatOverlay.Install"
        ];
        int previousBinding = -1;
        foreach (string binding in expectedBindings)
        {
            int bindingIndex = source.IndexOf(binding, StringComparison.Ordinal);
            Assert.True(bindingIndex > previousBinding, $"Missing or out-of-order startup binding: {binding}");
            previousBinding = bindingIndex;
        }
        Assert.Contains("LanConnectTailMessagePatches.ConfigureRuntime(LanConnectTailMessageRuntime.Shared)", source);
        Assert.Contains("LanConnectLobbyRuntime.Install(enableItemLinkCapture: true)", source);
    }

    [Fact]
    public void Stages_are_written_synchronously_in_order_and_completion_is_atomic()
    {
        using TemporaryDirectory temporary = new();
        List<string> mirror = [];
        List<string> calls = [];
        LanConnectStartupDiagnosticsOptions options = CreateOptions(
            temporary.Path,
            "ordered",
            mirror.Add);

        using LanConnectStartupDiagnostics diagnostics =
            LanConnectStartupDiagnostics.CreateForTesting(options);
        foreach (string stage in LanConnectStartupStages.Ordered)
        {
            diagnostics.RunStage(stage, () => calls.Add(stage));

            string currentLog = File.ReadAllText(Path.Combine(diagnostics.SessionDirectory, "startup.jsonl"));
            Assert.Contains($"\"stage\":\"{stage}\"", currentLog, StringComparison.Ordinal);
            Assert.Contains("\"status\":\"success\"", currentLog, StringComparison.Ordinal);
        }
        diagnostics.Complete();

        Assert.Equal(LanConnectStartupStages.Ordered, calls);
        JsonElement[] events = ReadEvents(diagnostics.SessionDirectory);
        JsonElement[] stages = events
            .Where(element => element.GetProperty("event").GetString() == "init_stage")
            .ToArray();
        Assert.Equal(22, stages.Length);
        Assert.Equal(
            Enumerable.Range(1, 11).SelectMany(static ordinal => new[] { ordinal, ordinal }),
            stages.Select(element => element.GetProperty("ordinal").GetInt32()));
        Assert.All(stages.Where(static (_, index) => index % 2 == 0), element =>
            Assert.Equal("begin", element.GetProperty("status").GetString()));
        Assert.All(stages.Where(static (_, index) => index % 2 == 1), element =>
        {
            Assert.Equal("success", element.GetProperty("status").GetString());
            Assert.True(element.GetProperty("elapsed_ms").GetDouble() >= 0);
        });

        using JsonDocument sentinel = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(temporary.Path, "init-sentinel.json")));
        Assert.True(sentinel.RootElement.GetProperty("completed").GetBoolean());
        Assert.Equal("initialization_complete", sentinel.RootElement.GetProperty("stage").GetString());
        Assert.Empty(Directory.EnumerateFiles(temporary.Path, "*.tmp", SearchOption.TopDirectoryOnly));
        Assert.Equal(events.Length, mirror.Count(static line =>
            line.StartsWith("sts2_lan_connect patch_diag: ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Next_session_reports_the_previous_stage_patch_and_sequence()
    {
        using TemporaryDirectory temporary = new();
        InvalidOperationException productFailure = new("token=must-not-appear");

        using (LanConnectStartupDiagnostics first = LanConnectStartupDiagnostics.CreateForTesting(
                   CreateOptions(temporary.Path, "first")))
        {
            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
                first.RunStage(LanConnectStartupStages.MultiplayerCompatibility, () =>
                {
                    LanConnectPatchDiagnosticDescriptor descriptor = CreatePatchDescriptor("android.serializer.01");
                    long started = first.RecordPatchBegin(descriptor);
                    first.RecordPatchFailure(descriptor, started, productFailure);
                    throw productFailure;
                }));
            Assert.Same(productFailure, thrown);
        }

        using LanConnectStartupDiagnostics second = LanConnectStartupDiagnostics.CreateForTesting(
            CreateOptions(temporary.Path, "second"));
        JsonElement recovery = Assert.Single(
            ReadEvents(second.SessionDirectory),
            element => element.GetProperty("event").GetString() == "previous_init_incomplete");

        Assert.Equal(
            LanConnectStartupStages.MultiplayerCompatibility,
            recovery.GetProperty("previous_stage").GetString());
        Assert.Equal("android.serializer.01", recovery.GetProperty("previous_patch_id").GetString());
        Assert.True(recovery.GetProperty("previous_sequence").GetInt64() > 0);

        string allEvidence = File.ReadAllText(Path.Combine(second.SessionDirectory, "startup.jsonl")) +
                             File.ReadAllText(Path.Combine(temporary.Path, "init-sentinel.json"));
        Assert.DoesNotContain("must-not-appear", allEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostic_write_failure_never_replaces_the_product_exception()
    {
        using TemporaryDirectory temporary = new();
        List<string> warnings = [];
        using LanConnectStartupDiagnostics diagnostics = LanConnectStartupDiagnostics.CreateForTesting(
            CreateOptions(temporary.Path, "writefail", warn: warnings.Add));

        string startupLog = Path.Combine(diagnostics.SessionDirectory, "startup.jsonl");
        File.Delete(startupLog);
        Directory.CreateDirectory(startupLog);
        InvalidOperationException productFailure = new("password=private");

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            diagnostics.RunStage(LanConnectStartupStages.ConfigLoad, () => throw productFailure));

        Assert.Same(productFailure, thrown);
        Assert.Contains(warnings, warning => warning.Contains("startup_jsonl_write", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, warning => warning.Contains("private", StringComparison.Ordinal));
    }

    [Fact]
    public void Successful_completion_keeps_current_evidence_and_prunes_oldest_sessions()
    {
        using TemporaryDirectory temporary = new();
        for (int index = 0; index < 4; index++)
        {
            string oldSession = Path.Combine(temporary.Path, $"2025010{index + 1}T000000.000Z-old{index}");
            Directory.CreateDirectory(oldSession);
            using FileStream stream = File.Create(Path.Combine(oldSession, "startup.jsonl"));
            stream.SetLength(24L * 1024L * 1024L);
        }
        string unrelatedDirectory = Path.Combine(temporary.Path, "manual-evidence-do-not-delete");
        Directory.CreateDirectory(unrelatedDirectory);
        File.WriteAllText(Path.Combine(unrelatedDirectory, "notes.txt"), "retained");

        using LanConnectStartupDiagnostics diagnostics = LanConnectStartupDiagnostics.CreateForTesting(
            CreateOptions(temporary.Path, "current") with
            {
                MaxSessions = 3,
                MaxTotalBytes = 64L * 1024L * 1024L
            });
        string currentSession = diagnostics.SessionDirectory;
        diagnostics.Complete();

        string[] sessions = Directory.GetDirectories(temporary.Path);
        Assert.Contains(currentSession, sessions);
        Assert.Contains(unrelatedDirectory, sessions);
        string[] timestampedSessions = sessions
            .Where(path => !string.Equals(path, unrelatedDirectory, StringComparison.Ordinal))
            .ToArray();
        Assert.True(timestampedSessions.Length <= 3);
        Assert.True(timestampedSessions.Sum(GetDirectorySize) <= 64L * 1024L * 1024L);
    }

    [Fact]
    public void Redactor_removes_sensitive_assignments_network_addresses_urls_and_paths()
    {
        const string raw =
            "steam://join/path?q=secret 10.20.30.40 2001:db8::1234 AA:BB:CC:DD:EE:FF " +
            "/Users/alice/private/config.json C:\\Users\\alice\\private\\config.json " +
            "\\\\desktop-name\\private-share\\config.json " +
            "player_name=Alice Smith platform_id=76561198012345678 room_name=Friday Run " +
            "ticket=join secret password=hunter two token=abc 123 config={secret value} chat=hello world";

        string redacted = LanConnectDiagnosticRedactor.RedactText(raw);

        foreach (string secret in new[]
                 {
                     "Alice", "Smith", "76561198012345678", "Friday Run", "join secret", "hunter two",
                     "abc 123", "{secret value}", "hello world", "steam://", "10.20.30.40", "2001:db8::1234",
                     "AA:BB:CC:DD:EE:FF", "/Users/alice", "C:\\Users\\alice", "desktop-name"
                 })
        {
            Assert.DoesNotContain(secret, redacted, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("<url>", redacted, StringComparison.Ordinal);
        Assert.Contains("<path>", redacted, StringComparison.Ordinal);
        Assert.Contains("<redacted>", redacted, StringComparison.Ordinal);
    }

    private static LanConnectStartupDiagnosticsOptions CreateOptions(
        string root,
        string sessionId,
        Action<string>? mirror = null,
        Action<string>? warn = null) => new()
    {
        DiagnosticsRoot = root,
        UtcNow = static () => new DateTimeOffset(2026, 8, 20, 4, 0, 0, TimeSpan.Zero),
        SessionIdFactory = () => sessionId,
        MirrorInfo = mirror ?? (static _ => { }),
        Warn = warn ?? (static _ => { }),
        CaptureArtifacts = false,
        EnableHarmonyDiagnostics = false
    };

    private static LanConnectPatchDiagnosticDescriptor CreatePatchDescriptor(string planId)
    {
        MethodInfo target = typeof(LanConnectStartupDiagnosticsTests).GetMethod(
            nameof(PatchTarget),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo hook = typeof(LanConnectStartupDiagnosticsTests).GetMethod(
            nameof(PatchHook),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return new LanConnectPatchDiagnosticDescriptor(
            planId,
            1,
            15,
            "serializer",
            "FakeMessage",
            target,
            hook,
            "sts2_lan_connect.protocol.v1",
            400);
    }

    private static JsonElement[] ReadEvents(string sessionDirectory) =>
        File.ReadLines(Path.Combine(sessionDirectory, "startup.jsonl"))
            .Select(static line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();

    private static long GetDirectorySize(string path) =>
        Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private static void PatchTarget() { }
    private static void PatchHook() { }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"sts2-lan-connect-diag-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
