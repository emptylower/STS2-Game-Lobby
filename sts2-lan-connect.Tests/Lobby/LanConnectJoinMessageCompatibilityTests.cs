using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectJoinMessageCompatibilityTests
{
    [Theory]
    [InlineData(TestMode.Standard, false)]
    [InlineData(TestMode.Daily, false)]
    [InlineData(TestMode.Custom, true)]
    public void Reads_legacy_mode_from_initial_message(TestMode mode, bool custom)
    {
        var result = LanConnectLobbyHandshakeCompatibility.ReadRunMode(
            new LegacyInitialMessage { gameMode = mode }, new object());

        Assert.Equal(mode.ToString(), result.GameMode);
        Assert.Equal(custom, result.IsCustom);
    }

    [Theory]
    [InlineData(TestMode.Standard, true)]
    [InlineData(TestMode.Blitz, false)]
    [InlineData(TestMode.Dread, false)]
    public void Reads_0_113_mode_and_custom_flag_from_join_response(TestMode mode, bool custom)
    {
        var result = LanConnectLobbyHandshakeCompatibility.ReadRunMode(
            new object(), new CurrentJoinResponse { gameMode = mode, isCustom = custom });

        Assert.Equal(mode.ToString(), result.GameMode);
        Assert.Equal(custom, result.IsCustom);
    }

    [Fact]
    public void Reads_0_113_loaded_or_running_mode_from_serialized_run()
    {
        var result = LanConnectLobbyHandshakeCompatibility.ReadRunMode(
            new object(), new SavedJoinResponse
            {
                serializableRun = new SavedRun { GameMode = TestMode.Standard, IsCustomGame = true }
            });

        Assert.Equal("Standard", result.GameMode);
        Assert.True(result.IsCustom);
    }

    [Fact]
    public void Rejects_missing_mode_instead_of_opening_the_wrong_lobby()
    {
        Assert.Throws<MissingFieldException>(() =>
            LanConnectLobbyHandshakeCompatibility.ReadRunMode(new object(), new object()));
    }

    [Fact]
    public void Populates_legacy_ascension_limit_without_needing_blitz_progress()
    {
        LegacyJoinRequest result = LanConnectLobbyHandshakeCompatibility.AttachAscensionLimits(
            new LegacyJoinRequest { marker = 42 }, new LegacyProgress());

        Assert.Equal(7, result.maxAscensionUnlocked);
        Assert.Equal(42, result.marker);
    }

    [Fact]
    public void Populates_both_0_113_ascension_limits()
    {
        CurrentJoinRequest result = LanConnectLobbyHandshakeCompatibility.AttachAscensionLimits(
            new CurrentJoinRequest { marker = 42 }, new CurrentProgress());

        Assert.Equal(7, result.maxStandardAscension);
        Assert.Equal(3, result.maxBlitzAscension);
        Assert.Equal(42, result.marker);
    }

    [Theory]
    [InlineData("Custom", false, false, "CustomRun.NCustomRunScreen")]
    [InlineData("Standard", true, false, "CustomRun.NCustomRunScreen")]
    [InlineData("Standard", true, true, "CustomRun.NCustomRunLoadScreen")]
    [InlineData("Standard", false, false, "CharacterSelect.NCharacterSelectScreen")]
    [InlineData("Daily", false, true, "DailyRun.NDailyRunLoadScreen")]
    [InlineData("Blitz", false, false, "Blitz.NBlitzScreen")]
    [InlineData("Dread", false, false, "Dread.NDreadScreen")]
    [InlineData("Blitz", false, true, "CharacterSelect.NMultiplayerLoadGameScreen")]
    [InlineData("Dread", false, true, "CharacterSelect.NMultiplayerLoadGameScreen")]
    public void Routes_legacy_and_0_113_lobbies_to_the_native_screen(
        string mode, bool custom, bool loaded, string screen)
    {
        Assert.Equal("MegaCrit.Sts2.Core.Nodes.Screens." + screen,
            LanConnectLobbyJoinFlow.GetJoinedScreenTypeName(mode, custom, loaded));
    }

    [Fact]
    public void Mod_has_no_direct_member_references_to_changed_join_fields()
    {
        using PEReader pe = new(File.OpenRead(typeof(Entry).Assembly.Location));
        MetadataReader reader = pe.GetMetadataReader();
        List<string> violations = [];
        foreach (MemberReferenceHandle handle in reader.MemberReferences)
        {
            MemberReference member = reader.GetMemberReference(handle);
            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            TypeReference type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
            string typeName = reader.GetString(type.Name);
            string memberName = reader.GetString(member.Name);
            if (typeName == "JoinResult"
                || (typeName == "InitialGameInfoMessage" && memberName == "gameMode")
                || (typeName == "ClientLobbyJoinRequestMessage" && memberName is
                    "maxAscensionUnlocked" or "maxStandardAscension" or "maxBlitzAscension"))
            {
                violations.Add($"{typeName}.{memberName}");
            }
        }

        Assert.Empty(violations);
    }

    public enum TestMode { Standard, Daily, Custom, Blitz, Dread }

#pragma warning disable CS0649
    private struct LegacyInitialMessage { public TestMode gameMode; }
    private struct CurrentJoinResponse { public TestMode gameMode; public bool isCustom; }
    private struct SavedJoinResponse { public SavedRun serializableRun; }
    private sealed class SavedRun { public TestMode GameMode { get; init; } public bool IsCustomGame { get; init; } }
    private struct LegacyJoinRequest { public int maxAscensionUnlocked; public int marker; }
    private struct CurrentJoinRequest { public int maxStandardAscension; public int maxBlitzAscension; public int marker; }
    private class LegacyProgress { public int MaxMultiplayerAscension => 7; }
    private sealed class CurrentProgress : LegacyProgress { public int MaxBlitzAscension => 3; }
#pragma warning restore CS0649
}
