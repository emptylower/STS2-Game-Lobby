using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectServerVersionResolverTests
{
    // Live /probe fixtures captured 2026-09-12/13 (plan §1.4) — byte-for-byte.
    private const string ProbeV04Fixture = """{"ok":true}""";
    private const string ProbeV051Fixture =
        """{"ok":true,"capabilities":{"serverChatVersion":1,"roomChatProtocolVersion":1,"richContentVersion":1,"emojiSetVersion":1,"itemRefVersion":1,"combatRefVersion":1,"maxMessageChars":300,"maxSegments":32,"maxEntities":12,"historyLimit":50,"modSyncProtocolVersion":1,"modSyncEnabled":true,"modSyncMinimumClientVersion":"0.5.1"}}""";
    private const string ProbeV061Fixture =
        """{"ok":true,"capabilities":{"serverChatVersion":1,"roomChatProtocolVersion":1,"richContentVersion":1,"emojiSetVersion":1,"itemRefVersion":1,"combatRefVersion":1,"maxMessageChars":300,"maxSegments":32,"maxEntities":12,"historyLimit":50,"modSyncProtocolVersion":1,"modSyncEnabled":true,"modSyncMinimumClientVersion":"0.5.1","wireCacheSignatureV1Enforced":true,"dualProtocolApiVersion":1,"supportedProtocolProfiles":["compat_4_5_v1","tail_v1"],"lanProtocolMin":1,"lanProtocolMax":1,"minimumClientVersion":"0.3.0","tailV1MinimumClientVersion":"0.6.1-alpha.1","tailV1Carrier":"native_bus_v1"}}""";
    private const string ProbeV062Alpha1Fixture =
        """{"ok":true,"capabilities":{"serverChatVersion":1,"roomChatProtocolVersion":1,"richContentVersion":1,"emojiSetVersion":1,"itemRefVersion":1,"combatRefVersion":1,"maxMessageChars":300,"maxSegments":32,"maxEntities":12,"historyLimit":50,"modSyncProtocolVersion":1,"modSyncEnabled":true,"modSyncMinimumClientVersion":"0.5.1","wireCacheSignatureV1Enforced":true,"dualProtocolApiVersion":1,"supportedProtocolProfiles":["compat_4_5_v1","tail_v1"],"lanProtocolMin":1,"lanProtocolMax":1,"minimumClientVersion":"0.3.0","tailV1MinimumClientVersion":"0.6.2-alpha.1","tailV1Carrier":"native_bus_v1"}}""";

    private static LobbyProbeResponse? ParseProbe(string json) =>
        JsonSerializer.Deserialize<LobbyProbeResponse>(json, LanConnectJson.Options);

    [Fact]
    public void Live_probe_fixtures_map_to_their_version_tiers()
    {
        ServerVersionInfo v04 = LanConnectServerVersionResolver.FromProbe(ParseProbe(ProbeV04Fixture));
        Assert.Equal(ServerVersionSource.Inferred, v04.Source);
        Assert.Equal((0, 4), (v04.Major, v04.Minor));
        Assert.Equal("0.4.x 或更早（推断）", v04.Display);
        Assert.True(v04.IsTooOldForThisClient);

        ServerVersionInfo v05 = LanConnectServerVersionResolver.FromProbe(ParseProbe(ProbeV051Fixture));
        Assert.Equal(ServerVersionSource.Inferred, v05.Source);
        Assert.Equal((0, 5), (v05.Major, v05.Minor));
        Assert.Equal("0.5.x（推断）", v05.Display);
        Assert.True(v05.IsTooOldForThisClient);

        ServerVersionInfo v061 = LanConnectServerVersionResolver.FromProbe(ParseProbe(ProbeV061Fixture));
        Assert.Equal(ServerVersionSource.Inferred, v061.Source);
        Assert.Equal((0, 6), (v061.Major, v061.Minor));
        Assert.Equal("0.6.x（推断）", v061.Display);
        Assert.False(v061.IsTooOldForThisClient);

        ServerVersionInfo v062 = LanConnectServerVersionResolver.FromProbe(ParseProbe(ProbeV062Alpha1Fixture));
        Assert.Equal(ServerVersionSource.Inferred, v062.Source);
        Assert.Equal((0, 6), (v062.Major, v062.Minor));
        Assert.False(v062.IsTooOldForThisClient);
    }

    [Fact]
    public void Reported_serviceVersion_overrides_probe_inference()
    {
        LobbyProbeResponse? probe = ParseProbe(
            """{"ok":true,"capabilities":{"serverChatVersion":1,"modSyncProtocolVersion":1,"serviceVersion":"0.7.0"}}""");

        ServerVersionInfo version = LanConnectServerVersionResolver.FromProbe(probe);

        Assert.Equal(ServerVersionSource.Reported, version.Source);
        Assert.Equal((0, 7), (version.Major, version.Minor));
        Assert.Equal("0.7.0", version.Display);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData("v0.6.1")]
    [InlineData("0.6")]
    [InlineData("0.6.1.2")]
    [InlineData("0.6.x")]
    [InlineData("99999999999.1.0")]
    public void FromReported_rejects_non_semantic_strings(string? candidate)
    {
        Assert.Null(LanConnectServerVersionResolver.FromReported(candidate));
    }

    [Fact]
    public void FromReported_rejects_overlong_strings_and_accepts_semantic_ones()
    {
        string overlong = new string('1', 62) + ".2.3";
        Assert.True(overlong.Length > 64);
        Assert.Null(LanConnectServerVersionResolver.FromReported(overlong));

        ServerVersionInfo? parsed = LanConnectServerVersionResolver.FromReported(" 0.6.2-alpha.2 ");
        Assert.NotNull(parsed);
        Assert.Equal(ServerVersionSource.Reported, parsed!.Source);
        Assert.Equal((0, 6), (parsed.Major, parsed.Minor));
        Assert.Equal("0.6.2-alpha.2", parsed.Display);
    }

    [Fact]
    public void Ok_false_probes_are_unknown_even_with_capability_fields()
    {
        LobbyProbeResponse? probe = ParseProbe("""{"ok":false,"capabilities":{"serverChatVersion":1}}""");

        ServerVersionInfo version = LanConnectServerVersionResolver.FromProbe(probe);

        Assert.Equal(ServerVersionSource.Unknown, version.Source);
        Assert.False(version.IsKnown);
        Assert.False(version.IsTooOldForThisClient);
        Assert.Equal("未知", version.Display);
    }

    [Fact]
    public void Explicit_null_capabilities_is_unknown()
    {
        LobbyProbeResponse? probe = ParseProbe("""{"ok":true,"capabilities":null}""");

        Assert.Null(probe!.Capabilities);
        ServerVersionInfo version = LanConnectServerVersionResolver.FromProbe(probe);

        Assert.Equal(ServerVersionSource.Unknown, version.Source);
    }

    [Fact]
    public void Non_json_probes_are_unknown()
    {
        // A non-2xx or non-JSON body never reaches FromProbe in production —
        // the caller passes null. Both must land on Unknown.
        Assert.Equal(ServerVersionSource.Unknown, LanConnectServerVersionResolver.FromProbe(null).Source);
        Assert.Equal(ServerVersionInfo.Unknown, LanConnectServerVersionResolver.FromProbe(null));
    }
}
