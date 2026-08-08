using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectInstallationCredentialTests
{
    [Fact]
    public void Generated_credential_has_256_bits_of_entropy_and_cannot_equal_a_game_net_id()
    {
        byte[] entropy = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();

        LanConnectInstallationCredentialResolution result =
            LanConnectInstallationCredential.Resolve(string.Empty, () => entropy);

        Assert.True(result.Generated);
        Assert.StartsWith("lci_", result.Credential, StringComparison.Ordinal);
        Assert.NotEqual("4053194744260183570", result.Credential);
        Assert.True(LanConnectInstallationCredential.TryNormalize(result.Credential, out string normalized));
        Assert.Equal(result.Credential, normalized);
    }

    [Fact]
    public void Valid_persisted_credential_is_reused_without_generating()
    {
        string persisted = LanConnectInstallationCredential.Resolve(
            null,
            () => Enumerable.Repeat((byte)0x5a, 32).ToArray()).Credential;
        int generated = 0;

        LanConnectInstallationCredentialResolution result =
            LanConnectInstallationCredential.Resolve($"  {persisted}  ", () =>
            {
                generated++;
                return new byte[32];
            });

        Assert.False(result.Generated);
        Assert.Equal(persisted, result.Credential);
        Assert.Equal(0, generated);
    }

    [Fact]
    public void Config_persistence_round_trip_preserves_installation_credential_separately_from_net_id()
    {
        const string netId = "4053194744260183570";
        string credential = LanConnectInstallationCredential.Resolve(
            null,
            () => Enumerable.Repeat((byte)0xa5, 32).ToArray()).Credential;
        string directory = Path.Combine(Path.GetTempPath(), $"lan-connect-installation-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "config.json");

        try
        {
            LanConnectConfigPersistence.Save(path, new LanConnectConfigData
            {
                LanClientNetId = netId,
                ClientInstallationId = credential
            });

            LanConnectConfigData loaded = LanConnectConfigPersistence.Load(path);
            Assert.Equal(netId, loaded.LanClientNetId);
            Assert.Equal(credential, loaded.ClientInstallationId);
            Assert.NotEqual(loaded.LanClientNetId, loaded.ClientInstallationId);
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(credential, json.RootElement.GetProperty("ClientInstallationId").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
