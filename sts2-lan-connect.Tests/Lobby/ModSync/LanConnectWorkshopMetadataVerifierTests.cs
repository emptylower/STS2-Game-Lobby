using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.ModSync;

public sealed class LanConnectWorkshopMetadataVerifierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sts2-mod-sync-{Guid.NewGuid():N}");

    [Fact]
    public async Task Provider_verifies_installed_manifest_id_matches_expected_id()
    {
        WriteManifest("expected.mod", "1.0.0");
        LanConnectWorkshopMetadataVerifier sut = new();

        LanConnectWorkshopManifestVerification result = await sut.VerifyInstalledManifestAsync(
            _root,
            Descriptor("expected.mod", "1.0.0"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.RequiresRestart);
        Assert.True(result.RequiresRepreflight);
    }

    [Fact]
    public async Task Provider_rejects_installed_manifest_with_different_id()
    {
        WriteManifest("deceptive.mod", "1.0.0");
        LanConnectWorkshopMetadataVerifier sut = new();

        LanConnectWorkshopManifestVerification result = await sut.VerifyInstalledManifestAsync(
            _root,
            Descriptor("expected.mod", "1.0.0"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("installed_manifest_id_mismatch", result.ErrorCode);
        Assert.True(result.RequiresRestart);
        Assert.True(result.RequiresRepreflight);
    }

    [Fact]
    public async Task Provider_requires_repreflight_when_installed_version_differs_from_host()
    {
        WriteManifest("expected.mod", "2.0.0");
        LanConnectWorkshopMetadataVerifier sut = new();

        LanConnectWorkshopManifestVerification result = await sut.VerifyInstalledManifestAsync(
            _root,
            Descriptor("expected.mod", "1.0.0"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("installed_version_mismatch", result.ErrorCode);
        Assert.True(result.RequiresRestart);
        Assert.True(result.RequiresRepreflight);
        Assert.Equal("2.0.0", result.InstalledVersion);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteManifest(string id, string version)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "mod_manifest.json"),
            JsonSerializer.Serialize(new { id, version }));
    }

    private static LobbyModDescriptor Descriptor(string id, string version) => new()
    {
        Id = id,
        Version = version,
        Role = LanConnectModRoles.Gameplay,
        Source = LanConnectModSources.SteamWorkshop,
        WorkshopFileId = "3747497501"
    };
}
