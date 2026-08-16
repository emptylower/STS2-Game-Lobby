using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLobbyWireCacheRequestTests
{
    [Fact]
    public void Present_signatures_serialize_on_create_and_join_requests()
    {
        const string signature = "wcv1:test-signature";

        using JsonDocument create = JsonDocument.Parse(JsonSerializer.Serialize(
            new LobbyCreateRoomRequest { WireCacheSignatureV1 = signature },
            LanConnectJson.Options));
        using JsonDocument join = JsonDocument.Parse(JsonSerializer.Serialize(
            new LobbyJoinRoomRequest { WireCacheSignatureV1 = signature },
            LanConnectJson.Options));

        Assert.Equal(signature, create.RootElement.GetProperty("wireCacheSignatureV1").GetString());
        Assert.Equal(signature, join.RootElement.GetProperty("wireCacheSignatureV1").GetString());
    }

    [Fact]
    public void Unavailable_signatures_are_omitted_from_create_and_join_requests()
    {
        using JsonDocument create = JsonDocument.Parse(JsonSerializer.Serialize(
            new LobbyCreateRoomRequest { WireCacheSignatureV1 = null },
            LanConnectJson.Options));
        using JsonDocument join = JsonDocument.Parse(JsonSerializer.Serialize(
            new LobbyJoinRoomRequest { WireCacheSignatureV1 = null },
            LanConnectJson.Options));

        Assert.False(create.RootElement.TryGetProperty("wireCacheSignatureV1", out _));
        Assert.False(join.RootElement.TryGetProperty("wireCacheSignatureV1", out _));
    }

    [Fact]
    public void Every_production_request_construction_site_carries_the_signature()
    {
        string scriptsRoot = Path.Combine(FindRepositoryRoot(), "sts2-lan-connect", "Scripts");
        string[] sources = Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories);
        string combined = string.Join("\n", sources.Select(File.ReadAllText));

        Assert.Equal(1, CountOccurrences(combined, "new LobbyCreateRoomRequest"));
        Assert.Equal(1, CountOccurrences(combined, "new LobbyJoinRoomRequest"));

        string hostFlow = File.ReadAllText(Path.Combine(scriptsRoot, "LanConnectHostFlow.cs"));
        Assert.Contains(
            "WireCacheSignatureV1 = wireCacheSignature",
            hostFlow,
            StringComparison.Ordinal);

        string preflight = File.ReadAllText(Path.Combine(
            scriptsRoot,
            "Lobby",
            "ModSync",
            "LanConnectModPreflightCoordinator.cs"));
        Assert.Contains(
            "WireCacheSignatureV1 = LanConnectWireCacheDiagnostics.GetCurrentResult().Snapshot?.Signature",
            preflight,
            StringComparison.Ordinal);
        Assert.Contains(
            "WireCacheSignatureV1 = request.WireCacheSignatureV1",
            preflight,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Continue_run_publication_preserves_a_frozen_missing_signature()
    {
        LanConnectProtocolSelection frozenSelection = new(
            LanConnectProtocolProfile.Compat4x5V1,
            SelectedLanProtocolVersion: 0,
            LanConnectProtocolCarrier.None,
            MinimumClientVersion: "0.6.0-alpha.1",
            MaxPlayers: 4,
            GameVersion: "saved-game-version",
            WireCacheSignature: null,
            RitsuLibPresent: false,
            CapabilityDigest: string.Empty);

        (string gameVersion, string? wireCacheSignature) =
            LanConnectHostFlow.ResolveCreateRoomProtocolIdentity(
                frozenSelection,
                currentGameVersion: "current-game-version",
                currentWireCacheSignature: "wcv1:current");

        Assert.Equal("saved-game-version", gameVersion);
        Assert.Null(wireCacheSignature);
    }

    [Fact]
    public void Wire_cache_mismatch_failure_explains_the_fix_and_preserves_both_signatures()
    {
        const string serviceMessage =
            "网络编码签名不匹配。房主签名：wcv1:host；加入者签名：wcv1:joiner。";

        string message = LanConnectCompatibilityMatrix.DescribeJoinFailureCode(
            "wire_cache_signature_mismatch",
            serviceMessage);

        Assert.Contains("内容/MOD 表产生了不同的网络编码", message, StringComparison.Ordinal);
        Assert.Contains("对齐双方的 MOD 列表和版本", message, StringComparison.Ordinal);
        Assert.Contains("wcv1:host", message, StringComparison.Ordinal);
        Assert.Contains("wcv1:joiner", message, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "sts2-lan-connect")) &&
                Directory.Exists(Path.Combine(current.FullName, "sts2-lan-connect.Tests")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the STS2-Game-Lobby repository root.");
    }
}
