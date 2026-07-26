using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectChatVerbPhraseTests
{
    [Theory]
    [InlineData("card", "shared a card: ", "分享了卡牌：")]
    [InlineData("relic", "shared a relic: ", "分享了遗物：")]
    [InlineData("potion", "shared a potion: ", "分享了药水：")]
    public void Known_item_types_produce_the_expected_verb_phrase_in_both_locales(
        string itemType,
        string expectedEnglish,
        string expectedChinese)
    {
        LanConnectChatLocalizer localizer = new();
        LanConnectItemRefSegment segment = new(itemType, "MegaCrit.Anchor");

        Assert.Equal(expectedEnglish, LanConnectChatVerbPhrase.PhraseFor(segment, localizer, "en-US"));
        Assert.Equal(expectedChinese, LanConnectChatVerbPhrase.PhraseFor(segment, localizer, "zh-CN"));
    }

    [Fact]
    public void Unknown_item_type_falls_back_without_throwing_or_leaking_a_bare_key()
    {
        LanConnectChatLocalizer localizer = new();
        LanConnectItemRefSegment segment = new("widget", "MegaCrit.Anchor");

        string? key = LanConnectChatVerbPhrase.KeyFor(segment);
        Assert.NotNull(key);

        string? englishPhrase = LanConnectChatVerbPhrase.PhraseFor(segment, localizer, "en-US");
        string? chinesePhrase = LanConnectChatVerbPhrase.PhraseFor(segment, localizer, "zh-CN");

        Assert.False(string.IsNullOrWhiteSpace(englishPhrase));
        Assert.False(string.IsNullOrWhiteSpace(chinesePhrase));
        Assert.NotEqual(key, englishPhrase);
        Assert.NotEqual(key, chinesePhrase);
    }

    [Theory]
    [InlineData("card")]
    [InlineData("relic")]
    [InlineData("potion")]
    [InlineData("widget")]
    public void Every_item_verb_key_this_task_introduces_is_populated_in_both_locales(string itemType)
    {
        LanConnectChatLocalizer localizer = new();
        LanConnectItemRefSegment segment = new(itemType, "MegaCrit.Anchor");
        string? key = LanConnectChatVerbPhrase.KeyFor(segment);
        Assert.NotNull(key);

        string english = localizer.Get("en-US", key!);
        string chinese = localizer.Get("zh-CN", key!);

        // LanConnectChatLocalizer.Get returns the key itself when a table entry is
        // missing, so this is what would catch a locale that only got one of the two
        // tables updated.
        Assert.NotEqual(key, english);
        Assert.NotEqual(key, chinese);
        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.False(string.IsNullOrWhiteSpace(chinese));
    }

    [Fact]
    public void Power_state_and_target_ref_segments_are_intentionally_left_unmapped()
    {
        // Step 1 finding (see task report): LanConnectPowerStateSegment never carries a
        // creature/monster identity at all (only owner/applier *player* net ids), and
        // LanConnectTargetRefSegment's monster case has no working render-time name
        // resolution in production (LanConnectMonsterTargetIdPrototype is instantiated
        // with adapter: null, and LanConnectRoomCombatReferenceResolver.Resolve has no
        // monster branch -- it always falls through to the target_expired fallback).
        // Phrasing these with a fabricated "{0}" would show players an empty or literal
        // placeholder, so this task deliberately does not map them.
        LanConnectPowerStateSegment power = new("MegaCrit.Vulnerable", 2, "room-session");
        LanConnectTargetRefSegment target = new("monster", "stable-id", "room-session");

        Assert.Null(LanConnectChatVerbPhrase.KeyFor(power));
        Assert.Null(LanConnectChatVerbPhrase.KeyFor(target));
        Assert.Null(LanConnectChatVerbPhrase.PhraseFor(power, localizer: new(), "en-US"));
        Assert.Null(LanConnectChatVerbPhrase.PhraseFor(target, localizer: new(), "en-US"));
    }
}
