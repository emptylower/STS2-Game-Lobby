using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLobbyInstallationIdentityTests
{
    [Fact]
    public void Create_and_join_requests_serialize_client_installation_id_with_contract_casing()
    {
        const string installationId = "76561198999999999";

        using JsonDocument create = JsonDocument.Parse(JsonSerializer.Serialize(
            new LobbyCreateRoomRequest { ClientInstallationId = installationId },
            LanConnectJson.Options));
        using JsonDocument join = JsonDocument.Parse(JsonSerializer.Serialize(
            new LobbyJoinRoomRequest { ClientInstallationId = installationId },
            LanConnectJson.Options));

        Assert.Equal(installationId, create.RootElement.GetProperty("clientInstallationId").GetString());
        Assert.Equal(installationId, join.RootElement.GetProperty("clientInstallationId").GetString());
    }

    [Fact]
    public void Every_production_create_and_join_request_populates_the_installation_id()
    {
        string scriptsRoot = Path.Combine(FindRepositoryRoot(), "sts2-lan-connect", "Scripts");
        string[] sources = Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories);
        string combined = string.Join("\n", sources.Select(File.ReadAllText));

        Assert.Equal(1, CountOccurrences(combined, "new LobbyCreateRoomRequest"));
        Assert.Equal(1, CountOccurrences(combined, "new LobbyJoinRoomRequest"));

        string hostFlow = File.ReadAllText(Path.Combine(scriptsRoot, "LanConnectHostFlow.cs"));
        Assert.Contains(
            "ClientInstallationId = LanConnectConfig.GetOrCreateClientNetId().ToString(CultureInfo.InvariantCulture)",
            hostFlow,
            StringComparison.Ordinal);

        string preflight = File.ReadAllText(Path.Combine(
            scriptsRoot,
            "Lobby",
            "ModSync",
            "LanConnectModPreflightCoordinator.cs"));
        Assert.Contains(
            "ClientInstallationId = LanConnectConfig.GetOrCreateClientNetId().ToString(CultureInfo.InvariantCulture)",
            preflight,
            StringComparison.Ordinal);
        Assert.Contains("ClientInstallationId = request.ClientInstallationId", preflight, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_separates_control_routing_identity_from_chat_attribution()
    {
        string runtime = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "sts2-lan-connect",
            "Scripts",
            "Lobby",
            "LanConnectLobbyRuntime.cs"));

        Assert.Contains(
            "_serverChatClientInstallationId = LanConnectConfig.GetOrCreateClientNetId()",
            runtime,
            StringComparison.Ordinal);
        Assert.Contains(
            ".ToString(CultureInfo.InvariantCulture)",
            runtime,
            StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(runtime, "ResolveCurrentChatInstallationId()") >= 11,
            "Expected legacy chat, rich chat, and server-chat attribution to use the installation ID.");
        Assert.Contains(
            "session.NetService.NetId.ToString(),\n                session.RoomSessionId",
            runtime,
            StringComparison.Ordinal);
        Assert.Contains(
            "session.PlayerNetId,\n                session.RoomSessionId",
            runtime,
            StringComparison.Ordinal);
        Assert.DoesNotContain("_serverChatPlayerNetId", runtime, StringComparison.Ordinal);
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
