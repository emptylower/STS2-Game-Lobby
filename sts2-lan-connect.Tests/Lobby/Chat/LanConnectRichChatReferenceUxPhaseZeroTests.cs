using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectRichChatReferenceUxPhaseZeroTests
{
    private const string V051MixedRoomContent =
        "{\"formatVersion\":1,\"segments\":[{\"kind\":\"text\",\"text\":\"before \"},{\"kind\":\"emoji\",\"emojiId\":\"heart\"},{\"kind\":\"item_ref\",\"itemType\":\"card\",\"modelId\":\"MegaCrit.Strike\",\"upgradeLevel\":1},{\"kind\":\"item_ref\",\"itemType\":\"relic\",\"modelId\":\"MegaCrit.Anchor\"},{\"kind\":\"item_ref\",\"itemType\":\"potion\",\"modelId\":\"MegaCrit.FirePotion\"},{\"kind\":\"power_state\",\"modelId\":\"MegaCrit.Catalyst\",\"amount\":4,\"roomSessionId\":\"room-session-1\",\"ownerPlayerNetId\":\"net:ironclad\",\"applierPlayerNetId\":\"net:silent\"},{\"kind\":\"target_ref\",\"targetKind\":\"player\",\"targetKey\":\"net:ironclad\",\"roomSessionId\":\"room-session-1\"},{\"kind\":\"text\",\"text\":\" after\"}]}";

    [Fact]
    public void V051_mixed_room_wire_fixture_remains_byte_exact()
    {
        LanConnectChatContent content = JsonSerializer.Deserialize<LanConnectChatContent>(
            V051MixedRoomContent,
            LanConnectChatJson.Options) ?? throw new InvalidOperationException("Fixture returned null.");

        Assert.Equal(V051MixedRoomContent, JsonSerializer.Serialize(content, LanConnectChatJson.Options));
        Assert.Collection(
            content.Segments,
            segment => Assert.IsType<LanConnectTextSegment>(segment),
            segment => Assert.IsType<LanConnectEmojiSegment>(segment),
            segment => Assert.IsType<LanConnectItemRefSegment>(segment),
            segment => Assert.IsType<LanConnectItemRefSegment>(segment),
            segment => Assert.IsType<LanConnectItemRefSegment>(segment),
            segment => Assert.IsType<LanConnectPowerStateSegment>(segment),
            segment => Assert.IsType<LanConnectTargetRefSegment>(segment),
            segment => Assert.IsType<LanConnectTextSegment>(segment));
    }

    [Fact]
    public void Catalyst_amount_four_requires_the_complete_runtime_power_context()
    {
        string chatSources = string.Join(
            '\n',
            Directory.EnumerateFiles(ChatSourceDirectory(), "*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

        Assert.Contains("SmartDescription", chatSources, StringComparison.Ordinal);
        Assert.Contains("DynamicVars", chatSources, StringComparison.Ordinal);
        Assert.Contains("OnPlayer", chatSources, StringComparison.Ordinal);
        Assert.Contains("IsMultiplayer", chatSources, StringComparison.Ordinal);
        Assert.Contains("PlayerCount", chatSources, StringComparison.Ordinal);
        Assert.Contains("OwnerName", chatSources, StringComparison.Ordinal);
        Assert.Contains("ApplierName", chatSources, StringComparison.Ordinal);
        Assert.Contains("TargetName", chatSources, StringComparison.Ordinal);
        Assert.Contains("energyPrefix", chatSources, StringComparison.Ordinal);
    }

    private static string ChatSourceDirectory() => Path.Combine(
        FindRepositoryRoot(),
        "sts2-lan-connect",
        "Scripts",
        "Lobby",
        "Chat");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
