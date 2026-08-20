using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.ProtocolPlanTests;

public sealed class LanConnectStartupArtifactCollectorTests
{
    [Fact]
    public void Artifact_inventory_records_versions_mvids_and_readable_hashes_without_paths()
    {
        string temporary = Path.Combine(
            Path.GetTempPath(),
            $"sts2-lan-connect-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            using LanConnectStartupDiagnostics diagnostics =
                LanConnectStartupDiagnostics.CreateForTesting(new LanConnectStartupDiagnosticsOptions
                {
                    DiagnosticsRoot = temporary,
                    SessionIdFactory = static () => "artifacts",
                    MirrorInfo = static _ => { },
                    Warn = static _ => { },
                    CaptureArtifacts = true,
                    EnableHarmonyDiagnostics = false
                });
            JsonElement[] events = File.ReadLines(Path.Combine(diagnostics.SessionDirectory, "startup.jsonl"))
                .Select(static line => JsonDocument.Parse(line).RootElement.Clone())
                .ToArray();

            JsonElement[] components = events
                .Where(element => element.GetProperty("event").GetString() == "component_info")
                .ToArray();
            Assert.Equal(
                ["game", "mod", "harmony", "monomod", "sts2mobile", "ritsulib"],
                components.Select(element => element.GetProperty("component").GetString()));
            Assert.All(components.Where(element => element.GetProperty("state").GetString() == "loaded"), element =>
            {
                Assert.False(string.IsNullOrWhiteSpace(element.GetProperty("version").GetString()));
                Assert.True(Guid.TryParse(element.GetProperty("module_mvid").GetString(), out _));
            });

            JsonElement[] hashes = events
                .Where(element => element.GetProperty("event").GetString() == "artifact_hash")
                .ToArray();
            Assert.Contains(hashes, element => element.GetProperty("artifact").GetString() == "sts2.dll");
            Assert.Contains(hashes, element => element.GetProperty("artifact").GetString() == "sts2_lan_connect.dll");
            Assert.Contains(hashes, element => element.GetProperty("artifact").GetString() == "0Harmony.dll");
            Assert.All(hashes, element =>
            {
                Assert.Matches("^[0-9a-f]{64}$", element.GetProperty("sha256").GetString()!);
                Assert.DoesNotContain("/", element.GetProperty("file_name").GetString(), StringComparison.Ordinal);
                Assert.DoesNotContain("\\", element.GetProperty("file_name").GetString(), StringComparison.Ordinal);
            });

            string jsonl = File.ReadAllText(Path.Combine(diagnostics.SessionDirectory, "startup.jsonl"));
            Assert.DoesNotContain(temporary, jsonl, StringComparison.Ordinal);
            Assert.DoesNotContain(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                jsonl,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                $"\"{Environment.MachineName}\"",
                jsonl,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                Directory.Delete(temporary, recursive: true);
            }
            catch
            {
            }
        }
    }
}
