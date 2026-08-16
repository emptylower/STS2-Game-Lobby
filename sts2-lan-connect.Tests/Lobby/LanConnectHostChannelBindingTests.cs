using System.Text.Json;
using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectHostChannelBindingTests
{
    [Fact]
    public void SavedRoomBinding_host_channel_defaults_empty()
    {
        var b = new LanConnectSavedRoomBinding();
        Assert.Equal(0, b.SchemaVersion);
        Assert.Equal(string.Empty, b.HostChannel);
        Assert.Equal(LanConnectHostChannels.Lobby, LanConnectHostChannels.Resolve(b.HostChannel));
    }

    [Fact]
    public void ResolvedRoomBinding_effective_host_channel_defaults_to_lobby()
    {
        var resolved = new LanConnectResolvedRoomBinding
        {
            HostChannel = string.Empty
        };
        Assert.Equal(string.Empty, resolved.HostChannel);
        Assert.Equal(LanConnectHostChannels.Lobby, resolved.EffectiveHostChannel);
    }

    [Theory]
    [InlineData("lan", "lan")]
    [InlineData("lobby", "lobby")]
    [InlineData("", "lobby")]
    [InlineData("LAN", "lan")]
    public void ResolvedRoomBinding_effective_host_channel_resolves(string stored, string expected)
    {
        var resolved = new LanConnectResolvedRoomBinding
        {
            HostChannel = stored
        };
        Assert.Equal(expected, resolved.EffectiveHostChannel);
    }

    [Fact]
    public void Legacy_json_without_schema_version_loads_as_version_zero()
    {
        LanConnectSavedRoomBinding binding = JsonSerializer.Deserialize<LanConnectSavedRoomBinding>(
            """{"SaveKey":"save-1","RoomName":"房间","HostChannel":"lan"}""")!;

        Assert.Equal(0, binding.SchemaVersion);
        Assert.Equal(LanConnectHostChannels.Lan, binding.HostChannel);
    }

    [Fact]
    public void Config_load_and_save_cycle_preserves_schema_version_and_host_channel()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lan-connect-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string inputPath = Path.Combine(directory, "input.json");
        string outputPath = Path.Combine(directory, "output.json");
        LanConnectConfigData input = new()
        {
            SaveRoomBindings =
            [
                new LanConnectSavedRoomBinding
                {
                    SchemaVersion = LanConnectSavedRoomBinding.CurrentSchemaVersion,
                    SaveKey = "save-1",
                    RoomName = "房间",
                    HostChannel = LanConnectHostChannels.Lan,
                    ProtocolProfileV2 = "tail_v1",
                    SelectedLanProtocolVersion = 1,
                    ProtocolCarrier = "standalone_tail_v1",
                    ProtocolMaxPlayers = 8,
                    MinimumClientVersion = "0.6.0-alpha.1",
                    ProtocolGameVersion = "0.111.0",
                    WireCacheSignatureV1 = "aabb",
                    RitsuLibPresent = false,
                    CapabilityDigest = new string('a', 64)
                }
            ]
        };
        File.WriteAllText(inputPath, JsonSerializer.Serialize(input));
        try
        {
            LanConnectConfigData loadedConfig = LanConnectConfigPersistence.Load(inputPath);
            LanConnectSavedRoomBinding loaded = Assert.Single(loadedConfig.SaveRoomBindings);
            loadedConfig.SaveRoomBindings =
            [
                LanConnectConfig.CloneBindingForPersistence(loaded)
            ];
            LanConnectConfigPersistence.Save(outputPath, loadedConfig);
            LanConnectConfigData saved = JsonSerializer.Deserialize<LanConnectConfigData>(
                File.ReadAllText(outputPath))!;

            LanConnectSavedRoomBinding roundTripped = Assert.Single(saved.SaveRoomBindings);
            Assert.Equal(LanConnectSavedRoomBinding.CurrentSchemaVersion, roundTripped.SchemaVersion);
            Assert.Equal("房间", roundTripped.RoomName);
            Assert.Equal(LanConnectHostChannels.Lan, roundTripped.HostChannel);
            Assert.Equal("tail_v1", roundTripped.ProtocolProfileV2);
            Assert.Equal(1, roundTripped.SelectedLanProtocolVersion);
            Assert.Equal("standalone_tail_v1", roundTripped.ProtocolCarrier);
            Assert.Equal(8, roundTripped.ProtocolMaxPlayers);
            Assert.Equal("0.6.0-alpha.1", roundTripped.MinimumClientVersion);
            Assert.Equal("0.111.0", roundTripped.ProtocolGameVersion);
            Assert.Equal("aabb", roundTripped.WireCacheSignatureV1);
            Assert.False(roundTripped.RitsuLibPresent);
            Assert.Equal(new string('a', 64), roundTripped.CapabilityDigest);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
