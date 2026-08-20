using HarmonyLib;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Diagnostics;

[CollectionDefinition("Startup diagnostics globals", DisableParallelization = true)]
public sealed class StartupDiagnosticsGlobalCollection;

[Collection("Startup diagnostics globals")]
public sealed class LanConnectHarmonyDiagnosticScopeTests
{
    [Fact]
    public void Scope_enables_private_harmony_diagnostics_and_restores_every_prior_global()
    {
        Assert.True(LanConnectMonoModSwitches.IsAvailable);

        string root = Path.Combine(Path.GetTempPath(), $"sts2-lan-connect-harmony-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        bool originalDebug = Harmony.DEBUG;
        StreamWriter? originalWriter = FileLog.LogWriter;
        bool hadDump = LanConnectMonoModSwitches.TryGetValue("DMDDumpTo", out object? originalDump);
        bool hadType = LanConnectMonoModSwitches.TryGetValue("DMDType", out object? originalType);
        bool hadDebug = LanConnectMonoModSwitches.TryGetValue("DMDDebug", out object? originalDmdDebug);
        using MemoryStream previousStream = new();
        using StreamWriter previousWriter = new(previousStream) { AutoFlush = true };

        try
        {
            Harmony.DEBUG = false;
            FileLog.LogWriter = previousWriter;
            LanConnectMonoModSwitches.SetValue("DMDDumpTo", "prior-dump");
            LanConnectMonoModSwitches.SetValue("DMDType", "prior-type");
            LanConnectMonoModSwitches.SetValue("DMDDebug", "prior-debug");

            string sessionDirectory;
            using (LanConnectStartupDiagnostics diagnostics = LanConnectStartupDiagnostics.CreateForTesting(new()
                   {
                       DiagnosticsRoot = root,
                       UtcNow = static () => new DateTimeOffset(2026, 8, 20, 4, 0, 0, TimeSpan.Zero),
                       SessionIdFactory = static () => "harmony",
                       CaptureArtifacts = false,
                       EnableHarmonyDiagnostics = true
                   }))
            {
                sessionDirectory = diagnostics.SessionDirectory;
                Assert.True(Harmony.DEBUG);
                Assert.NotSame(previousWriter, FileLog.LogWriter);
                Assert.True(LanConnectMonoModSwitches.TryGetValue("DMDDumpTo", out object? activeDump));
                Assert.Equal(Path.Combine(sessionDirectory, "dmd"), activeDump);
                Assert.True(LanConnectMonoModSwitches.TryGetValue("DMDType", out object? activeType));
                Assert.Equal("prior-type", activeType);
                Assert.True(LanConnectMonoModSwitches.TryGetValue("DMDDebug", out object? activeDebug));
                Assert.Equal("prior-debug", activeDebug);

                FileLog.Log("private harmony evidence");
                FileLog.Log("source=/Users/alice/private/Patch.cs token=do-not-log");
            }

            Assert.False(Harmony.DEBUG);
            Assert.Same(previousWriter, FileLog.LogWriter);
            Assert.True(LanConnectMonoModSwitches.TryGetValue("DMDDumpTo", out object? restoredDump));
            Assert.Equal("prior-dump", restoredDump);
            Assert.True(LanConnectMonoModSwitches.TryGetValue("DMDType", out object? restoredType));
            Assert.Equal("prior-type", restoredType);
            Assert.True(LanConnectMonoModSwitches.TryGetValue("DMDDebug", out object? restoredDebug));
            Assert.Equal("prior-debug", restoredDebug);
            string harmonyLog = File.ReadAllText(Path.Combine(sessionDirectory, "harmony.log"));
            Assert.Contains("private harmony evidence", harmonyLog, StringComparison.Ordinal);
            Assert.DoesNotContain("/Users/alice", harmonyLog, StringComparison.Ordinal);
            Assert.DoesNotContain("do-not-log", harmonyLog, StringComparison.Ordinal);
        }
        finally
        {
            Harmony.DEBUG = originalDebug;
            FileLog.LogWriter = originalWriter;
            RestoreSwitch("DMDDumpTo", hadDump, originalDump);
            RestoreSwitch("DMDType", hadType, originalType);
            RestoreSwitch("DMDDebug", hadDebug, originalDmdDebug);
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void RestoreSwitch(string name, bool existed, object? value)
    {
        if (existed)
        {
            LanConnectMonoModSwitches.SetValue(name, value);
        }
        else
        {
            LanConnectMonoModSwitches.ClearValue(name);
        }
    }
}
