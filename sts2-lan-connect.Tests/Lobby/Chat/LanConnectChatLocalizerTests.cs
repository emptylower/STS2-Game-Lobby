using Sts2LanConnect.Scripts;
using System.Text.RegularExpressions;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectChatLocalizerTests
{
    private const string RequiredLucideNotice = """
        Lucide Icons
        ------------
        Copyright (c) 2025 Lucide Contributors
        Copyright (c) 2013-2022 Cole Bemis

        ISC License

        Permission to use, copy, modify, and/or distribute this software for any
        purpose with or without fee is hereby granted, provided that the above
        copyright notice and this permission notice appear in all copies.

        THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
        WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
        MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
        ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
        WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
        ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
        OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

        Lucide is derived from Feather Icons (https://feathericons.com/).
        """;

    private static readonly string[] ExpectedKeys =
    [
        "chat.emoji.button",
        "chat.emoji.picker_title",
        "chat.emoji.smile",
        "chat.emoji.laugh",
        "chat.emoji.heart",
        "chat.emoji.thumbs-up",
        "chat.emoji.thumbs-down",
        "chat.emoji.sparkles",
        "chat.emoji.flame",
        "chat.emoji.zap",
        "chat.emoji.shield",
        "chat.emoji.swords",
        "chat.emoji.target",
        "chat.emoji.crown",
        "chat.emoji.skull",
        "chat.emoji.ghost",
        "chat.emoji.eye",
        "chat.emoji.message-circle",
        "chat.emoji.check",
        "chat.emoji.x",
        "chat.item.card",
        "chat.item.relic",
        "chat.item.potion",
        "chat.item.power",
        "chat.item.player",
        "chat.item.entity",
        "chat.unknown_card",
        "chat.unknown_relic",
        "chat.unknown_potion",
        "chat.unknown_item",
        "chat.unknown_emoji",
        "chat.unknown_content",
        "chat.unknown_player",
        "chat.unknown_power",
        "chat.target_expired",
        "chat.power.label",
        "chat.power.amount",
        "chat.power.owner",
        "chat.power.applier",
        "chat.preview_unavailable",
        "chat.rich_disabled",
        "chat.emoji_disabled",
        "chat.item_disabled",
        "chat.combat_disabled",
        "chat.combat.room_only",
        "chat.budget.empty",
        "chat.budget.text_limit",
        "chat.budget.segment_limit",
        "chat.budget.entity_limit",
        "chat.budget.wire_limit",
        "chat.budget.invalid",
        "chat.budget.summary",
        "chat.copy.emoji",
        "chat.copy.card",
        "chat.copy.relic",
        "chat.copy.potion",
        "chat.copy.power",
        "chat.copy.player",
        "chat.copy.entity",
        "chat.capture.blocked",
        "chat.tooltip.emoji_picker",
        "chat.tooltip.item_preview",
        "chat.accessibility.message_budget",
        "chat.accessibility.message_text",
        "chat.title.server",
        "chat.title.room",
        "chat.action.send",
        "chat.action.retry",
        "chat.action.resend",
        "chat.action.cancel",
        "chat.empty",
        "chat.confirm.title",
        "chat.confirm.delivery_unknown",
        "chat.new_messages",
        "chat.fade.unread_hint",
        "chat.operation.unavailable",
        "chat.operation.submitted",
        "chat.operation.send_failed",
        "chat.operation.retried",
        "chat.operation.retry_failed",
        "chat.operation.sent_as_new",
        "chat.operation.resend_failed",
        "chat.delivery.pending",
        "chat.delivery.failed",
        "chat.delivery.unknown",
        "chat.delivery.disconnected_unknown",
        "chat.presentation.unsupported",
        "chat.presentation.connecting",
        "chat.presentation.reconnecting",
        "chat.presentation.room_ready",
        "chat.presentation.room_disabled",
        "chat.presentation.server_ready",
        "chat.presentation.chat_disabled",
        "chat.presentation.server_disabled",
        "chat.presentation.transport_failure",
        "chat.presentation.transport_failure_detail"
    ];

    [Fact]
    public void Chinese_and_English_tables_are_exact_complete_and_nonblank()
    {
        LanConnectChatLocalizer localizer = new();

        Assert.Equal(ExpectedKeys.Order(StringComparer.Ordinal), localizer.Keys.Order(StringComparer.Ordinal));
        foreach (string key in ExpectedKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(localizer.Get("zh-CN", key)), key + " zh-CN");
            Assert.False(string.IsNullOrWhiteSpace(localizer.Get("en", key)), key + " en");
        }
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hans-CN")]
    [InlineData("zh-CN-x-test")]
    [InlineData(" ZH_cn ")]
    [InlineData("zh_hans")]
    public void Simplified_Chinese_locales_use_Chinese(string locale)
    {
        LanConnectChatLocalizer localizer = new();

        Assert.Equal("未知遗物", localizer.Get(locale, "chat.unknown_relic"));
        Assert.Equal("未知能力", localizer.Get(locale, "chat.unknown_power"));
        Assert.Equal("目标已不可用", localizer.Get(locale, "chat.target_expired"));
        Assert.Equal("战斗状态只能分享到房间聊天", localizer.Get(locale, "chat.combat.room_only"));
        Assert.Equal("消息文字超过 300 字符", localizer.Get(locale, "chat.budget.text_limit"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData(" EN_us ")]
    public void English_locales_use_English(string locale)
    {
        LanConnectChatLocalizer localizer = new();

        Assert.Equal("Unknown power", localizer.Get(locale, "chat.unknown_power"));
        Assert.Equal("Chat · {0} unread", localizer.Get(locale, "chat.fade.unread_hint"));
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    [InlineData("unknown")]
    [InlineData("zh-CNN")]
    [InlineData("zh-Hansfoo")]
    [InlineData("zh-TW")]
    [InlineData("zh-Hant")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zh-CN-")]
    [InlineData("zh-Hans--CN")]
    [InlineData("zh__CN")]
    [InlineData("-zh-CN")]
    [InlineData("zh-CN-\u0001")]
    [InlineData(null)]
    public void Unsupported_or_null_locales_fall_back_to_English(string? locale)
    {
        LanConnectChatLocalizer localizer = new();

        Assert.Equal("Unknown relic", localizer.Get(locale, "chat.unknown_relic"));
        Assert.Equal("Unknown power", localizer.Get(locale, "chat.unknown_power"));
        Assert.Equal("Target is no longer available", localizer.Get(locale, "chat.target_expired"));
        Assert.Equal("Combat state can only be shared in room chat", localizer.Get(locale, "chat.combat.room_only"));
        Assert.Equal("Message text exceeds 300 characters", localizer.Get(locale, "chat.budget.text_limit"));
    }

    [Fact]
    public void Phase_four_governance_combat_and_fade_copy_is_exact_in_both_languages()
    {
        LanConnectChatLocalizer localizer = new();
        Dictionary<string, (string English, string Chinese)> expected = new()
        {
            ["chat.unknown_power"] = ("Unknown power", "未知能力"),
            ["chat.target_expired"] = ("Target is no longer available", "目标已不可用"),
            ["chat.combat.room_only"] = ("Combat state can only be shared in room chat", "战斗状态只能分享到房间聊天"),
            ["chat.fade.unread_hint"] = ("Chat · {0} unread", "聊天 · {0} 条未读"),
            ["chat.rich_disabled"] = ("Rich content is unavailable in this channel", "当前频道不支持草稿中的富内容"),
            ["chat.emoji_disabled"] = ("Emoji are unavailable in this channel", "当前频道不支持表情"),
            ["chat.item_disabled"] = ("Item links are unavailable in this channel", "当前频道不支持物品链接"),
            ["chat.combat_disabled"] = ("Combat references are unavailable in this channel", "当前频道不支持战斗引用"),
            ["chat.presentation.room_disabled"] = ("Room chat unavailable", "房间聊天暂不可用"),
            ["chat.presentation.chat_disabled"] = ("Chat unavailable", "聊天暂不可用"),
            ["chat.presentation.server_disabled"] = ("Server chat disabled by the server", "频道已由服务器停用")
        };

        foreach ((string key, (string english, string chinese)) in expected)
        {
            Assert.Equal(english, localizer.Get("en-US", key));
            Assert.Equal(chinese, localizer.Get("zh-Hans-CN", key));
        }
    }

    [Fact]
    public void English_and_Chinese_format_placeholders_match_for_every_key()
    {
        LanConnectChatLocalizer localizer = new();
        foreach (string key in ExpectedKeys)
        {
            int[] english = PlaceholderIndexes(localizer.Get("en", key));
            int[] chinese = PlaceholderIndexes(localizer.Get("zh-CN", key));
            Assert.Equal(english, chinese);

            if (english.Length > 0)
            {
                object[] args = Enumerable.Range(0, english.Max() + 1)
                    .Select(index => (object)index)
                    .ToArray();
                Assert.False(string.IsNullOrWhiteSpace(localizer.Format("en", key, args)));
                Assert.False(string.IsNullOrWhiteSpace(localizer.Format("zh-CN", key, args)));
            }
        }
    }

    [Fact]
    public void Format_uses_the_selected_language_without_localizing_ModelDb_titles()
    {
        LanConnectChatLocalizer localizer = new();

        Assert.Equal(
            "Preview 本地药水",
            localizer.Format("en", "chat.tooltip.item_preview", "本地药水"));
        Assert.DoesNotContain("本地药水", localizer.Keys);
    }

    [Fact]
    public void Missing_keys_and_format_argument_mismatches_are_deterministic()
    {
        LanConnectChatLocalizer localizer = new();

        Assert.Equal("chat.missing", localizer.Get("en", "chat.missing"));
        Assert.Equal("chat.missing", localizer.Format("en", "chat.missing", 1));
        Assert.Throws<FormatException>(() => localizer.Format("en", "chat.tooltip.item_preview"));
    }

    [Fact]
    public void Emoji_descriptors_reference_the_exact_18_localized_keys()
    {
        Assert.Equal(
            ExpectedKeys.Where(key => key.StartsWith("chat.emoji.", StringComparison.Ordinal))
                .Except(["chat.emoji.button", "chat.emoji.picker_title"], StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
            LanConnectChatEmojiSet.Version1.Select(emoji => emoji.LabelKey)
                .Order(StringComparer.Ordinal));
        Assert.Equal(18, LanConnectChatEmojiSet.Version1.Count);
    }

    [Fact]
    public void Production_composition_owns_one_shared_localizer_instance()
    {
        Assert.Same(LanConnectChatUiComposition.Localizer, LanConnectChatUiComposition.Localizer);
    }

    [Fact]
    public void Localized_copy_labels_do_not_change_fixed_English_wire_fallback()
    {
        LanConnectChatLocalizer localizer = new();
        Assert.Equal("[表情]", localizer.Get("zh-CN", "chat.copy.emoji"));
        LanConnectChatContent content = new(1,
        [
            new LanConnectEmojiSegment("heart"),
            new LanConnectItemRefSegment("relic", "MegaCrit.Anchor")
        ]);

        Assert.Equal("[Emoji][Relic]", LanConnectServerChatProtocol.RenderGenericFallback(content));
    }

    [Fact]
    public void Notice_contains_the_exact_Lucide_Feather_ISC_section_once()
    {
        string path = Path.Combine(FindRepositoryRoot(), "THIRD_PARTY_NOTICES");
        Assert.True(File.Exists(path), "Missing root THIRD_PARTY_NOTICES.");
        string notice = File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd();

        Assert.Equal(1, CountOccurrences(notice, RequiredLucideNotice));
        foreach (string required in new[]
        {
            "Lucide Icons",
            "Copyright (c) 2025 Lucide Contributors",
            "Copyright (c) 2013-2022 Cole Bemis",
            "ISC License",
            "Permission to use, copy, modify, and/or distribute this software for any\npurpose with or without fee is hereby granted, provided that the above\ncopyright notice and this permission notice appear in all copies.",
            "THE SOFTWARE IS PROVIDED \"AS IS\" AND THE AUTHOR DISCLAIMS ALL WARRANTIES\nWITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF\nMERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR\nANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES\nWHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN\nACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF\nOR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.",
            "Lucide is derived from Feather Icons (https://feathericons.com/)."
        })
        {
            Assert.Equal(1, CountOccurrences(notice, required));
        }
        Assert.DoesNotContain("sts2_typing", notice, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static int[] PlaceholderIndexes(string value) =>
        Regex.Matches(value, @"\{(\d+)(?:[^}]*)\}")
            .Select(match => int.Parse(match.Groups[1].Value))
            .Distinct()
            .Order()
            .ToArray();

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
