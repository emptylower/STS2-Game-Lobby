using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectChatEmojiSetTests
{
    private static readonly string[] ExpectedIds =
    [
        "smile", "laugh", "heart", "thumbs-up", "thumbs-down", "sparkles",
        "flame", "zap", "shield", "swords", "target", "crown",
        "skull", "ghost", "eye", "message-circle", "check", "x"
    ];

    [Fact]
    public void Version_one_has_exactly_eighteen_stable_ids_icons_and_label_keys()
    {
        Assert.Equal(1, LanConnectChatEmojiSet.Version);
        Assert.Equal(ExpectedIds, LanConnectChatEmojiSet.Version1.Select(emoji => emoji.Id));
        Assert.Equal(18, LanConnectChatEmojiSet.Version1.Select(emoji => emoji.LucideIcon).Distinct().Count());
        Assert.All(LanConnectChatEmojiSet.Version1, emoji =>
        {
            Assert.Equal($"chat.emoji.{emoji.Id}", emoji.LabelKey);
            Assert.False(string.IsNullOrWhiteSpace(emoji.LucideIcon));
        });
    }

    [Fact]
    public void Lookup_is_ordinal_case_sensitive_and_unknown_ids_fail()
    {
        foreach (string id in ExpectedIds)
        {
            Assert.True(LanConnectChatEmojiSet.TryGet(id, out LanConnectEmojiDescriptor? descriptor));
            Assert.Equal(id, descriptor.Id);
            Assert.False(LanConnectChatEmojiSet.TryGet(id.ToUpperInvariant(), out _));
        }

        Assert.False(LanConnectChatEmojiSet.TryGet("not-published", out _));
        Assert.False(LanConnectChatEmojiSet.TryGet(string.Empty, out _));
        Assert.False(LanConnectChatEmojiSet.TryGet(null!, out _));
    }

    [Fact]
    public void Protocol_whitelist_uses_the_catalog_order_as_its_single_csharp_source()
    {
        Assert.Equal(ExpectedIds, LanConnectServerChatProtocol.EmojiSet1);
    }

}
