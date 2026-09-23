using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectConfigPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "sts2-lan-connect-config-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_keeps_previous_valid_config_as_backup()
    {
        string path = Path.Combine(_directory, "config.json");
        LanConnectConfigPersistence.Save(path, ConfigWithBinding("original"));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        LanConnectConfigPersistence.Save(path, ConfigWithBinding("current"));

        Assert.Equal("current", Assert.Single(LanConnectConfigPersistence.Load(path).SaveRoomBindings).SaveKey);
        Assert.Equal("original", Assert.Single(LanConnectConfigPersistence.Load(path + ".backup").SaveRoomBindings).SaveKey);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp-*"));
        AssertPrivateUnixMode(path);
        AssertPrivateUnixMode(path + ".backup");
    }

    [Fact]
    public void Load_recovers_backup_without_losing_corrupt_original()
    {
        string path = Path.Combine(_directory, "config.json");
        LanConnectConfigPersistence.Save(path, ConfigWithBinding("recoverable"));
        LanConnectConfigPersistence.Save(path, ConfigWithBinding("newer"));
        File.WriteAllText(path, "{invalid json");

        LanConnectConfigData recovered = LanConnectConfigPersistence.Load(path, out bool usedBackup);

        Assert.True(usedBackup);
        Assert.Equal("recoverable", Assert.Single(recovered.SaveRoomBindings).SaveKey);
        Assert.Equal("recoverable", Assert.Single(LanConnectConfigPersistence.Load(path).SaveRoomBindings).SaveKey);
        Assert.Equal("recoverable", Assert.Single(LanConnectConfigPersistence.Load(path + ".backup").SaveRoomBindings).SaveKey);
        string corruptPath = Assert.Single(Directory.GetFiles(_directory, "config.json.corrupt-*"));
        Assert.Equal("{invalid json", File.ReadAllText(corruptPath));
        AssertPrivateUnixMode(path);
        AssertPrivateUnixMode(path + ".backup");
        AssertPrivateUnixMode(corruptPath);
    }

    [Fact]
    public void Load_restores_backup_when_primary_is_missing()
    {
        string path = Path.Combine(_directory, "config.json");
        LanConnectConfigPersistence.Save(path, ConfigWithBinding("recoverable"));
        LanConnectConfigPersistence.Save(path, ConfigWithBinding("newer"));
        File.Delete(path);

        LanConnectConfigData recovered = LanConnectConfigPersistence.Load(path, out bool usedBackup);

        Assert.True(usedBackup);
        Assert.Equal("recoverable", Assert.Single(recovered.SaveRoomBindings).SaveKey);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_directory, "config.json.corrupt-*"));
    }

    [Fact]
    public void Load_recovers_backup_when_primary_omits_saved_room_bindings()
    {
        string path = Path.Combine(_directory, "config.json");
        LanConnectConfigPersistence.Save(path, ConfigWithBinding("recoverable"));
        LanConnectConfigPersistence.Save(path, ConfigWithBinding("newer"));
        File.WriteAllText(path, "{}");

        LanConnectConfigData recovered = LanConnectConfigPersistence.Load(path, out bool usedBackup);

        Assert.True(usedBackup);
        Assert.Equal("recoverable", Assert.Single(recovered.SaveRoomBindings).SaveKey);
        Assert.Equal("{}", File.ReadAllText(Assert.Single(
            Directory.GetFiles(_directory, "config.json.corrupt-*"))));
    }

    [Fact]
    public void Load_accepts_older_config_without_binding_field_when_no_backup_exists()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, "{\"LastRoomName\":\"older\"}");

        LanConnectConfigData loaded = LanConnectConfigPersistence.Load(path, out bool usedBackup);

        Assert.False(usedBackup);
        Assert.Equal("older", loaded.LastRoomName);
        Assert.Empty(loaded.SaveRoomBindings);
    }

    [Fact]
    public void Save_keeps_backup_if_a_valid_primary_lost_its_room_bindings()
    {
        string path = Path.Combine(_directory, "config.json");
        LanConnectConfigPersistence.Save(path, ConfigWithBinding("recoverable"));
        LanConnectConfigPersistence.Save(path, ConfigWithBinding("current"));
        File.WriteAllText(path, "{\"SaveRoomBindings\":[]}");

        LanConnectConfigData current = LanConnectConfigPersistence.Load(path);
        current.LastRoomName = "changed-setting";
        LanConnectConfigPersistence.Save(path, current);

        Assert.Empty(LanConnectConfigPersistence.Load(path).SaveRoomBindings);
        Assert.Equal("changed-setting", LanConnectConfigPersistence.Load(path).LastRoomName);
        Assert.Equal("recoverable", Assert.Single(
            LanConnectConfigPersistence.Load(path + ".backup").SaveRoomBindings).SaveKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Save_keeps_backup_when_the_same_save_key_loses_or_changes_protocol(bool rewrittenAsCompat)
    {
        string path = Path.Combine(_directory, "config.json");
        LanConnectConfigPersistence.Save(path, ConfigWithBinding("same-save"));
        LanConnectConfigPersistence.Save(path, ConfigWithBinding("same-save"));
        LanConnectConfigData changed = ConfigWithBinding("same-save");
        LanConnectSavedRoomBinding binding = Assert.Single(changed.SaveRoomBindings);
        if (rewrittenAsCompat)
        {
            binding.ProtocolProfileV2 = "compat_4_5_v1";
            binding.ProtocolCarrier = "none";
            binding.SelectedLanProtocolVersion = 0;
        }
        else
        {
            binding.ProtocolCarrier = string.Empty;
            binding.CapabilityDigest = string.Empty;
        }
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(changed));

        LanConnectConfigData loaded = LanConnectConfigPersistence.Load(path);
        loaded.LastRoomName = "another-setting";
        LanConnectConfigPersistence.Save(path, loaded);

        LanConnectSavedRoomBinding backup = Assert.Single(
            LanConnectConfigPersistence.Load(path + ".backup").SaveRoomBindings);
        Assert.Equal("same-save", backup.SaveKey);
        Assert.Equal("tail_v1", backup.ProtocolProfileV2);
        Assert.Equal("native_bus_v1", backup.ProtocolCarrier);
    }

    [Fact]
    public void Save_does_not_replace_valid_backup_with_corrupt_current_file()
    {
        string path = Path.Combine(_directory, "config.json");
        LanConnectConfigPersistence.Save(path, ConfigWithBinding("backup"));
        LanConnectConfigPersistence.Save(path, ConfigWithBinding("current"));
        File.WriteAllText(path, "null");

        Assert.True(LanConnectConfigPersistence.Save(path, ConfigWithBinding("replacement")));

        Assert.Equal("replacement", Assert.Single(LanConnectConfigPersistence.Load(path).SaveRoomBindings).SaveKey);
        Assert.Equal("backup", Assert.Single(LanConnectConfigPersistence.Load(path + ".backup").SaveRoomBindings).SaveKey);
        Assert.Equal("null", File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "config.json.corrupt-*"))));
    }

    [Fact]
    public void Load_throws_when_both_files_are_invalid_and_preserves_them()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, "{broken primary");
        File.WriteAllText(path + ".backup", "{broken backup");

        Assert.Throws<InvalidDataException>(() => LanConnectConfigPersistence.Load(path));

        Assert.Equal("{broken primary", File.ReadAllText(path));
        Assert.Equal("{broken backup", File.ReadAllText(path + ".backup"));
    }

    [Fact]
    public void Diagnostics_report_protocol_fields_without_secrets()
    {
        LanConnectSavedRoomBinding binding = Assert.Single(ConfigWithBinding("save").SaveRoomBindings);
        binding.Password = "room-password";

        string snapshot = LanConnectSaveDiagnostics.DescribeBinding(binding);

        Assert.Contains("protocolProfile=tail_v1", snapshot);
        Assert.Contains("protocolCarrier=native_bus_v1", snapshot);
        Assert.Contains("protocolFields=complete", snapshot);
        Assert.DoesNotContain(binding.Password, snapshot);

        binding.CapabilityDigest = string.Empty;
        Assert.Contains("protocolFields=incomplete", LanConnectSaveDiagnostics.DescribeBinding(binding));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static LanConnectConfigData ConfigWithBinding(string saveKey) => new()
    {
        SaveRoomBindings =
        [
            new LanConnectSavedRoomBinding
            {
                SchemaVersion = LanConnectSavedRoomBinding.CurrentSchemaVersion,
                SaveKey = saveKey,
                RoomName = "room",
                ProtocolProfileV2 = "tail_v1",
                SelectedLanProtocolVersion = 1,
                ProtocolCarrier = "native_bus_v1",
                ProtocolMaxPlayers = 4,
                MinimumClientVersion = "0.6.2",
                ProtocolGameVersion = "0.111.0",
                CapabilityDigest = new string('a', 64)
            }
        ]
    };

    private static void AssertPrivateUnixMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(path) & (UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite));
        }
    }
}
