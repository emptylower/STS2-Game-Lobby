using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectAtomicPatchDispatcherTests
{
    [Fact]
    public void Compat_and_tail_profiles_choose_fixed_4_5_and_vanilla_2_3_widths()
    {
        Assert.Equal(LanConnectConstants.ExtendedSlotIdBits, 4);
        Assert.Equal(LanConnectConstants.ExtendedLobbyListBits, 5);
        Assert.Equal(LanConnectConstants.VanillaSlotIdBits, 2);
        Assert.Equal(LanConnectConstants.VanillaLobbyListBits, 3);
    }

    [Fact]
    public void Dispatcher_preserves_the_existing_RMP_full_patch_skip_guard()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "sts2-lan-connect",
            "Scripts",
            "Protocol",
            "Patches",
            "LanConnectProtocolPatchDispatcher.cs"));

        Assert.Contains("LanConnectExternalModDetection.IsRmpModLoaded", source, StringComparison.Ordinal);
        Assert.Contains("skipping all LAN protocol patches", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

}
