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
                    HostChannel = LanConnectHostChannels.Lan
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
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
