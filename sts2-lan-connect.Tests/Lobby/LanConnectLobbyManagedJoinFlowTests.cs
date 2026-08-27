using System.Reflection;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLobbyManagedJoinFlowTests
{
    [Fact]
    public void Accepts_identical_game_versions()
    {
        Assert.Null(LanConnectLobbyManagedJoinFlow.GetGameVersionMismatchMessage("v0.109.0", "v0.109.0"));
    }

    [Theory]
    [InlineData("v0.109.0", "0.109.0")]
    [InlineData("V0.109.0", "v0.109.0")]
    public void Accepts_equivalent_game_versions_with_optional_v_prefix(string host, string local)
    {
        Assert.Null(LanConnectLobbyManagedJoinFlow.GetGameVersionMismatchMessage(host, local));
    }

    [Fact]
    public void Rejects_different_game_versions_with_actionable_message()
    {
        string message = Assert.IsType<string>(
            LanConnectLobbyManagedJoinFlow.GetGameVersionMismatchMessage("v0.108.0", "v0.109.0"));

        Assert.Contains("游戏版本不匹配", message, StringComparison.Ordinal);
        Assert.Contains("房主版本：v0.108.0", message, StringComparison.Ordinal);
        Assert.Contains("当前版本：v0.109.0", message, StringComparison.Ordinal);
        Assert.Contains("完全相同的游戏版本", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolves_legacy_connect_signature_with_concrete_service()
    {
        MethodInfo method = LanConnectLobbyManagedJoinFlow.ResolveCompatibleConnectMethod(
            typeof(ILegacyInitializer),
            typeof(TestNetService));

        Assert.Equal(typeof(TestNetService), method.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void Resolves_current_connect_signature_with_service_interface()
    {
        MethodInfo method = LanConnectLobbyManagedJoinFlow.ResolveCompatibleConnectMethod(
            typeof(ICurrentInitializer),
            typeof(TestNetService));

        Assert.Equal(typeof(ITestNetService), method.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void Rejects_connect_signature_that_cannot_accept_service()
    {
        Assert.Throws<MissingMethodException>(() =>
            LanConnectLobbyManagedJoinFlow.ResolveCompatibleConnectMethod(
                typeof(IIncompatibleInitializer),
                typeof(TestNetService)));
    }

    [Fact]
    public void Resolves_duck_typed_concrete_initializer_that_implements_no_interface()
    {
        MethodInfo method = LanConnectLobbyManagedJoinFlow.ResolveCompatibleConnectMethod(
            typeof(ConcreteDuckInitializer),
            typeof(TestNetService));

        Assert.Equal("Connect", method.Name);
        Assert.Equal(typeof(Task<DuckConnectResult?>), method.ReturnType);
        Assert.Equal(typeof(TestNetService), method.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), method.GetParameters()[1].ParameterType);
    }

    private sealed class DuckConnectResult;

    private sealed class ConcreteDuckInitializer
    {
        public Task<DuckConnectResult?> Connect(TestNetService service, CancellationToken cancellationToken)
        {
            return Task.FromResult<DuckConnectResult?>(null);
        }
    }

    [Fact]
    public void Reads_legacy_flat_peer_version_fields()
    {
        LegacyInitialGameInfo message = new()
        {
            version = "v0.109.0",
            idDatabaseHash = 109,
            gameplayAffectingMods = [" mod-b ", "mod-a", "mod-a"],
            otherMods =
            [
                "utility-mod",
                "sts2_lan_connect_wire:v1:wcv1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:bits=2,3,4,5"
            ]
        };

        LanConnectLobbyHandshakeCompatibility.PeerVersionSnapshot snapshot =
            LanConnectLobbyHandshakeCompatibility.ReadInitialGameInfo(message);

        Assert.Equal("v0.109.0", snapshot.Version);
        Assert.Equal(109UL, snapshot.IdDatabaseHash);
        Assert.Equal(["mod-a", "mod-b"], snapshot.GameplayAffectingMods);
        Assert.Equal(["utility-mod"], snapshot.OtherMods);
        Assert.Equal(LanConnectWireCacheHandshakeTokenStatus.Valid, snapshot.WireCacheToken.Status);
        Assert.Equal(5, snapshot.WireCacheToken.Token!.PropertyIdBitSize);
        Assert.Null(snapshot.RawVersionInfo);
    }

    [Fact]
    public void Reads_current_nested_peer_version_fields()
    {
        CurrentInitialGameInfo message = new()
        {
            versionInfo = new TestPeerVersionInfo
            {
                version = "v0.110.1",
                idDatabaseHash = 110,
                gameplayAffectingMods = ["mod-current"],
                otherMods = ["utility-current"]
            }
        };

        LanConnectLobbyHandshakeCompatibility.PeerVersionSnapshot snapshot =
            LanConnectLobbyHandshakeCompatibility.ReadInitialGameInfo(message);

        Assert.Equal("v0.110.1", snapshot.Version);
        Assert.Equal(110UL, snapshot.IdDatabaseHash);
        Assert.Equal(["mod-current"], snapshot.GameplayAffectingMods);
        Assert.Equal(["utility-current"], snapshot.OtherMods);
        Assert.Equal(LanConnectWireCacheHandshakeTokenStatus.Absent, snapshot.WireCacheToken.Status);
        Assert.IsType<TestPeerVersionInfo>(snapshot.RawVersionInfo);
    }

    [Fact]
    public void Marks_current_game_message_without_version_fields_as_metadata_unavailable()
    {
        CurrentGameInitialMessage message = new() { sessionState = 1, gameMode = 2 };

        LanConnectLobbyHandshakeCompatibility.PeerVersionSnapshot snapshot =
            LanConnectLobbyHandshakeCompatibility.ReadInitialGameInfo(message);

        Assert.False(snapshot.HasVersionMetadata);
        Assert.Equal(string.Empty, snapshot.Version);
        Assert.Equal(0UL, snapshot.IdDatabaseHash);
        Assert.Empty(snapshot.GameplayAffectingMods);
        Assert.Empty(snapshot.OtherMods);
        Assert.Equal(LanConnectWireCacheHandshakeTokenStatus.Absent, snapshot.WireCacheToken.Status);
        Assert.Null(snapshot.RawVersionInfo);
    }

    [Fact]
    public void Reads_legacy_mods_field_alias()
    {
        LegacyAliasedModsInitialGameInfo message = new()
        {
            version = "v0.107.1",
            idDatabaseHash = 107,
            mods = ["legacy-mod"]
        };

        LanConnectLobbyHandshakeCompatibility.PeerVersionSnapshot snapshot =
            LanConnectLobbyHandshakeCompatibility.ReadInitialGameInfo(message);

        Assert.Equal(["legacy-mod"], snapshot.GameplayAffectingMods);
    }

    [Fact]
    public void Preserves_duplicate_other_mod_sentinels_for_ambiguity_detection()
    {
        string sentinel =
            "sts2_lan_connect_wire:v1:wcv1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        LegacyInitialGameInfo message = new()
        {
            version = "v0.110.1",
            idDatabaseHash = 110,
            otherMods = [sentinel, sentinel]
        };

        LanConnectLobbyHandshakeCompatibility.PeerVersionSnapshot snapshot =
            LanConnectLobbyHandshakeCompatibility.ReadInitialGameInfo(message);

        Assert.Equal(LanConnectWireCacheHandshakeTokenStatus.Duplicate, snapshot.WireCacheToken.Status);
        Assert.Empty(snapshot.OtherMods);
    }

    [Fact]
    public void Leaves_legacy_join_request_unchanged_when_version_info_is_absent()
    {
        LegacyJoinRequest request = new() { marker = 42 };

        LegacyJoinRequest populated =
            LanConnectLobbyHandshakeCompatibility.AttachLocalVersionInfo(request);

        Assert.Equal(42, populated.marker);
    }

    [Fact]
    public void Populates_current_join_request_when_version_info_is_present()
    {
        CurrentJoinRequest populated =
            LanConnectLobbyHandshakeCompatibility.AttachLocalVersionInfo(default(CurrentJoinRequest));

        Assert.Equal("local-current", populated.versionInfo.version);
        Assert.Equal(999U, populated.versionInfo.idDatabaseHash);
    }

    [Fact]
    public void Populates_old_and_new_failure_info_layouts()
    {
        TestPeerVersionInfo remotePeerInfo = new()
        {
            version = "remote",
            idDatabaseHash = 110,
            gameplayAffectingMods = ["host-mod"],
            otherMods =
            [
                "remote-utility",
                "sts2_lan_connect_wire:v1:wcv1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
            ]
        };
        LanConnectLobbyHandshakeCompatibility.PeerVersionSnapshot snapshot = new(
            remotePeerInfo.version,
            remotePeerInfo.idDatabaseHash,
            remotePeerInfo.gameplayAffectingMods,
            [],
            LanConnectWireCacheHandshakeToken.Parse(null),
            remotePeerInfo);
        List<string> missingOnLocal = ["host-mod"];
        List<string> missingOnHost = ["local-mod"];

        LegacyFailureExtraInfo legacy = LanConnectLobbyHandshakeCompatibility.PopulateFailureExtraInfo(
            new LegacyFailureExtraInfo(), snapshot, missingOnLocal, missingOnHost);
        CurrentFailureExtraInfo current = LanConnectLobbyHandshakeCompatibility.PopulateFailureExtraInfo(
            new CurrentFailureExtraInfo(), snapshot, missingOnLocal, missingOnHost);

        Assert.Same(missingOnLocal, legacy.missingModsOnLocal);
        Assert.Same(missingOnHost, legacy.missingModsOnHost);
        Assert.Equal("local-current", current.localInfo.version);
        Assert.Equal("remote", current.remoteInfo.version);
        Assert.Equal(["local-utility"], current.localInfo.otherMods);
        Assert.Equal(["remote-utility"], current.remoteInfo.otherMods);
        Assert.False(current.localIsHost);
    }

    private interface ITestNetService;

    private sealed class TestNetService : ITestNetService;

    private sealed class OtherNetService;

    private interface ILegacyInitializer
    {
        Task<int> Connect(TestNetService service, CancellationToken cancellationToken);
    }

    private interface ICurrentInitializer
    {
        Task<int> Connect(ITestNetService service, CancellationToken cancellationToken);
    }

    private interface IIncompatibleInitializer
    {
        Task<int> Connect(OtherNetService service, CancellationToken cancellationToken);
    }

#pragma warning disable CS0649
    private struct CurrentGameInitialMessage
    {
        public int sessionState;
        public int gameMode;
    }

    private struct LegacyInitialGameInfo
    {
        public string version;
        public uint idDatabaseHash;
        public List<string>? gameplayAffectingMods;
        public List<string>? otherMods;
    }

    private struct CurrentInitialGameInfo
    {
        public TestPeerVersionInfo versionInfo;
    }

    private struct LegacyAliasedModsInitialGameInfo
    {
        public string version;
        public uint idDatabaseHash;
        public List<string>? mods;
    }

    private struct TestPeerVersionInfo
    {
        public string version;
        public uint idDatabaseHash;
        public List<string> gameplayAffectingMods;
        public List<string>? otherMods;

        public static TestPeerVersionInfo LocalDefault() => new()
        {
            version = "local-current",
            idDatabaseHash = 999,
            gameplayAffectingMods = ["local-mod"],
            otherMods = ["local-utility"]
        };
    }

    private struct LegacyJoinRequest
    {
        public int marker;
    }

    private struct CurrentJoinRequest
    {
        public TestPeerVersionInfo versionInfo;
    }

    private sealed class LegacyFailureExtraInfo
    {
        public List<string>? missingModsOnLocal;
        public List<string>? missingModsOnHost;
    }

    private sealed class CurrentFailureExtraInfo
    {
        public TestPeerVersionInfo localInfo;
        public TestPeerVersionInfo remoteInfo;
        public bool localIsHost;
    }
#pragma warning restore CS0649
}
