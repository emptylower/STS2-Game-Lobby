using System.Collections.ObjectModel;
using System.Globalization;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectChatLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> English = ReadOnly(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chat.emoji.button"] = "Emoji",
            ["chat.emoji.picker_title"] = "Choose emoji",
            ["chat.emoji.smile"] = "Smile",
            ["chat.emoji.laugh"] = "Laugh",
            ["chat.emoji.heart"] = "Heart",
            ["chat.emoji.thumbs-up"] = "Thumbs up",
            ["chat.emoji.thumbs-down"] = "Thumbs down",
            ["chat.emoji.sparkles"] = "Sparkles",
            ["chat.emoji.flame"] = "Flame",
            ["chat.emoji.zap"] = "Lightning",
            ["chat.emoji.shield"] = "Shield",
            ["chat.emoji.swords"] = "Swords",
            ["chat.emoji.target"] = "Target",
            ["chat.emoji.crown"] = "Crown",
            ["chat.emoji.skull"] = "Skull",
            ["chat.emoji.ghost"] = "Ghost",
            ["chat.emoji.eye"] = "Eye",
            ["chat.emoji.message-circle"] = "Message",
            ["chat.emoji.check"] = "Confirm",
            ["chat.emoji.x"] = "Cancel",
            ["chat.item.card"] = "Card",
            ["chat.item.relic"] = "Relic",
            ["chat.item.potion"] = "Potion",
            ["chat.item.power"] = "Power",
            ["chat.item.player"] = "Player",
            ["chat.item.entity"] = "Entity",
            ["chat.unknown_card"] = "Unknown card",
            ["chat.unknown_relic"] = "Unknown relic",
            ["chat.unknown_potion"] = "Unknown potion",
            ["chat.unknown_item"] = "Unknown item",
            ["chat.unknown_emoji"] = "Unknown emoji",
            ["chat.unknown_content"] = "Unknown content",
            ["chat.unknown_player"] = "Unknown player",
            ["chat.unknown_power"] = "Unknown power",
            ["chat.target_expired"] = "Target is no longer available",
            ["chat.power.label"] = "{0} {1}",
            ["chat.power.amount"] = "Amount: {0}",
            ["chat.power.owner"] = "Owner: {0}",
            ["chat.power.applier"] = "Applied by: {0}",
            ["chat.preview.close"] = "Close reference preview",
            ["chat.preview_unavailable"] = "Preview unavailable",
            ["chat.rich_disabled"] = "Rich content is unavailable in this channel",
            ["chat.emoji_disabled"] = "Emoji are unavailable in this channel",
            ["chat.item_disabled"] = "Item links are unavailable in this channel",
            ["chat.combat_disabled"] = "Combat references are unavailable in this channel",
            ["chat.combat.room_only"] = "Combat state can only be shared in room chat",
            ["chat.reference.action"] = "Reference",
            ["chat.reference.tooltip"] = "Reference a game object",
            ["chat.reference.armed_accessibility"] = "Reference mode is on; select an object",
            ["chat.reference.armed_hint"] = "Select a card, relic, potion, power, or player; reference mode exits after one capture",
            ["chat.reference.no_targets"] = "No referenceable objects are available in this scene",
            ["chat.reference.unsupported_target"] = "That object cannot be referenced; reference mode is still on",
            ["chat.budget.empty"] = "Enter a message",
            ["chat.input.placeholder"] = "Type a message...",
            ["chat.budget.text_limit"] = "Message text exceeds 300 characters",
            ["chat.budget.segment_limit"] = "Message exceeds 32 segments",
            ["chat.budget.entity_limit"] = "Message exceeds 12 entities",
            ["chat.budget.wire_limit"] = "Message payload exceeds 8192 bytes",
            ["chat.budget.invalid"] = "Message content is invalid",
            ["chat.budget.summary"] = "Characters {0}/300 · segments {1}/32 · entities {2}/12 · bytes {3}/8192",
            ["chat.copy.emoji"] = "[Emoji]",
            ["chat.copy.card"] = "[Card]",
            ["chat.copy.relic"] = "[Relic]",
            ["chat.copy.potion"] = "[Potion]",
            ["chat.copy.power"] = "[Power]",
            ["chat.copy.player"] = "[Player]",
            ["chat.copy.entity"] = "[Entity]",
            ["chat.capture.blocked"] = "Item link capture is currently blocked",
            ["chat.tooltip.emoji_picker"] = "Open emoji picker",
            ["chat.tooltip.item_preview"] = "Preview {0}",
            ["chat.accessibility.message_budget"] = "Message budget",
            ["chat.accessibility.message_text"] = "Message text",
            ["chat.title.server"] = "Server chat",
            ["chat.title.room"] = "Room chat",
            ["chat.action.send"] = "Send",
            ["chat.action.retry"] = "Retry",
            ["chat.action.resend"] = "Resend",
            ["chat.action.cancel"] = "Cancel",
            ["chat.empty"] = "No messages yet",
            ["chat.confirm.title"] = "Confirm resend",
            ["chat.confirm.delivery_unknown"] = "This message may already have been sent. Send it again as a new message?",
            ["chat.new_messages"] = "{0} new messages",
            ["chat.fade.unread_hint"] = "Chat · {0} unread",
            ["chat.operation.unavailable"] = "Chat is unavailable",
            ["chat.operation.submitted"] = "Submitted",
            ["chat.operation.send_failed"] = "Send failed: {0}",
            ["chat.operation.retried"] = "Retried",
            ["chat.operation.retry_failed"] = "Retry failed: {0}",
            ["chat.operation.sent_as_new"] = "Sent as a new message",
            ["chat.operation.resend_failed"] = "Resend failed: {0}",
            ["chat.delivery.pending"] = "Sending",
            ["chat.delivery.failed"] = "Send failed: {0}",
            ["chat.delivery.unknown"] = "Delivery status unknown",
            ["chat.delivery.disconnected_unknown"] = "Possibly sent; confirm before resending",
            ["chat.presentation.unsupported"] = "This server does not support server chat",
            ["chat.presentation.connecting"] = "Connecting to server chat...",
            ["chat.presentation.reconnecting"] = "Server chat disconnected; reconnecting...",
            ["chat.presentation.room_ready"] = "Room chat available",
            ["chat.presentation.room_disabled"] = "Room chat unavailable",
            ["chat.presentation.server_ready"] = "Server chat available",
            ["chat.presentation.chat_disabled"] = "Chat unavailable",
            ["chat.presentation.server_disabled"] = "Server chat disabled by the server",
            ["chat.presentation.transport_failure"] = "Server chat connection failed",
            ["chat.presentation.transport_failure_detail"] = "Server chat connection failed: {0}"
        });

    private static readonly IReadOnlyDictionary<string, string> SimplifiedChinese = ReadOnly(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chat.emoji.button"] = "表情",
            ["chat.emoji.picker_title"] = "选择表情",
            ["chat.emoji.smile"] = "微笑",
            ["chat.emoji.laugh"] = "大笑",
            ["chat.emoji.heart"] = "爱心",
            ["chat.emoji.thumbs-up"] = "赞同",
            ["chat.emoji.thumbs-down"] = "反对",
            ["chat.emoji.sparkles"] = "闪光",
            ["chat.emoji.flame"] = "火焰",
            ["chat.emoji.zap"] = "闪电",
            ["chat.emoji.shield"] = "护盾",
            ["chat.emoji.swords"] = "双剑",
            ["chat.emoji.target"] = "目标",
            ["chat.emoji.crown"] = "皇冠",
            ["chat.emoji.skull"] = "骷髅",
            ["chat.emoji.ghost"] = "幽灵",
            ["chat.emoji.eye"] = "眼睛",
            ["chat.emoji.message-circle"] = "消息",
            ["chat.emoji.check"] = "确认",
            ["chat.emoji.x"] = "取消",
            ["chat.item.card"] = "卡牌",
            ["chat.item.relic"] = "遗物",
            ["chat.item.potion"] = "药水",
            ["chat.item.power"] = "能力",
            ["chat.item.player"] = "玩家",
            ["chat.item.entity"] = "实体",
            ["chat.unknown_card"] = "未知卡牌",
            ["chat.unknown_relic"] = "未知遗物",
            ["chat.unknown_potion"] = "未知药水",
            ["chat.unknown_item"] = "未知物品",
            ["chat.unknown_emoji"] = "未知表情",
            ["chat.unknown_content"] = "未知内容",
            ["chat.unknown_player"] = "未知玩家",
            ["chat.unknown_power"] = "未知能力",
            ["chat.target_expired"] = "目标已不可用",
            ["chat.power.label"] = "{0} {1}",
            ["chat.power.amount"] = "层数：{0}",
            ["chat.power.owner"] = "持有者：{0}",
            ["chat.power.applier"] = "施加者：{0}",
            ["chat.preview.close"] = "关闭引用预览",
            ["chat.preview_unavailable"] = "预览不可用",
            ["chat.rich_disabled"] = "当前频道不支持草稿中的富内容",
            ["chat.emoji_disabled"] = "当前频道不支持表情",
            ["chat.item_disabled"] = "当前频道不支持物品链接",
            ["chat.combat_disabled"] = "当前频道不支持战斗引用",
            ["chat.combat.room_only"] = "战斗状态只能分享到房间聊天",
            ["chat.reference.action"] = "引用",
            ["chat.reference.tooltip"] = "引用游戏对象",
            ["chat.reference.armed_accessibility"] = "引用模式已开启，选择一个对象",
            ["chat.reference.armed_hint"] = "请选择卡牌、遗物、药水、状态或玩家；引用一次后自动退出",
            ["chat.reference.no_targets"] = "当前场景没有可引用对象",
            ["chat.reference.unsupported_target"] = "该对象不支持引用；引用模式仍保持开启",
            ["chat.budget.empty"] = "请输入消息",
            ["chat.input.placeholder"] = "输入消息...",
            ["chat.budget.text_limit"] = "消息文字超过 300 字符",
            ["chat.budget.segment_limit"] = "消息分段超过 32 段",
            ["chat.budget.entity_limit"] = "消息实体超过 12 个",
            ["chat.budget.wire_limit"] = "消息传输大小超过 8192 字节",
            ["chat.budget.invalid"] = "消息内容无效",
            ["chat.budget.summary"] = "字符 {0}/300 · 分段 {1}/32 · 实体 {2}/12 · 字节 {3}/8192",
            ["chat.copy.emoji"] = "[表情]",
            ["chat.copy.card"] = "[卡牌]",
            ["chat.copy.relic"] = "[遗物]",
            ["chat.copy.potion"] = "[药水]",
            ["chat.copy.power"] = "[能力]",
            ["chat.copy.player"] = "[玩家]",
            ["chat.copy.entity"] = "[实体]",
            ["chat.capture.blocked"] = "当前无法捕获物品链接",
            ["chat.tooltip.emoji_picker"] = "打开表情选择器",
            ["chat.tooltip.item_preview"] = "预览 {0}",
            ["chat.accessibility.message_budget"] = "消息预算",
            ["chat.accessibility.message_text"] = "消息文字",
            ["chat.title.server"] = "频道聊天",
            ["chat.title.room"] = "房间聊天",
            ["chat.action.send"] = "发送",
            ["chat.action.retry"] = "重试",
            ["chat.action.resend"] = "重新发送",
            ["chat.action.cancel"] = "取消",
            ["chat.empty"] = "还没有消息",
            ["chat.confirm.title"] = "确认重新发送",
            ["chat.confirm.delivery_unknown"] = "这条消息可能已经发送。是否以新消息重新发送？",
            ["chat.new_messages"] = "有 {0} 条新消息",
            ["chat.fade.unread_hint"] = "聊天 · {0} 条未读",
            ["chat.operation.unavailable"] = "聊天暂不可用",
            ["chat.operation.submitted"] = "已提交",
            ["chat.operation.send_failed"] = "发送失败：{0}",
            ["chat.operation.retried"] = "已重试",
            ["chat.operation.retry_failed"] = "重试失败：{0}",
            ["chat.operation.sent_as_new"] = "已作为新消息发送",
            ["chat.operation.resend_failed"] = "重发失败：{0}",
            ["chat.delivery.pending"] = "发送中",
            ["chat.delivery.failed"] = "发送失败：{0}",
            ["chat.delivery.unknown"] = "投递状态未知",
            ["chat.delivery.disconnected_unknown"] = "可能已发送，确认后重发",
            ["chat.presentation.unsupported"] = "当前服务器不支持频道聊天",
            ["chat.presentation.connecting"] = "正在连接频道...",
            ["chat.presentation.reconnecting"] = "频道连接中断，正在重连...",
            ["chat.presentation.room_ready"] = "房间聊天可用",
            ["chat.presentation.room_disabled"] = "房间聊天暂不可用",
            ["chat.presentation.server_ready"] = "频道可用",
            ["chat.presentation.chat_disabled"] = "聊天暂不可用",
            ["chat.presentation.server_disabled"] = "频道已由服务器停用",
            ["chat.presentation.transport_failure"] = "频道连接失败",
            ["chat.presentation.transport_failure_detail"] = "频道连接失败：{0}"
        });

    static LanConnectChatLocalizer()
    {
        if (!English.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(SimplifiedChinese.Keys) ||
            English.Values.Any(string.IsNullOrWhiteSpace) ||
            SimplifiedChinese.Values.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Chat localization tables must have the same complete nonblank key set.");
        }
    }

    internal IReadOnlyCollection<string> Keys => English.Keys.ToArray();

    internal string Get(string? locale, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        IReadOnlyDictionary<string, string> table = IsSimplifiedChinese(locale)
            ? SimplifiedChinese
            : English;
        return table.TryGetValue(key, out string? value) ? value : key;
    }

    internal string Format(string? locale, string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(locale, key), args ?? Array.Empty<object>());

    private static bool IsSimplifiedChinese(string? locale) =>
        NormalizeLocale(locale) is string normalized &&
        (IsLocaleOrChild(normalized, "zh-CN") ||
         IsLocaleOrChild(normalized, "zh-Hans"));

    private static bool IsLocaleOrChild(string locale, string expected) =>
        string.Equals(locale, expected, StringComparison.OrdinalIgnoreCase) ||
        (locale.Length > expected.Length &&
         locale[expected.Length] == '-' &&
         locale.StartsWith(expected, StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeLocale(string? locale)
    {
        string? normalized = locale?.Trim().Replace('_', '-');
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        string[] subtags = normalized.Split('-');
        if (subtags.Length == 0 || subtags[0].Length is < 2 or > 8 ||
            !subtags[0].All(IsAsciiLetter))
        {
            return null;
        }

        foreach (string subtag in subtags)
        {
            if (subtag.Length is < 1 or > 8 || !subtag.All(IsAsciiLetterOrDigit))
            {
                return null;
            }
        }

        return normalized;
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsAsciiLetterOrDigit(char value) =>
        IsAsciiLetter(value) || value is >= '0' and <= '9';

    private static IReadOnlyDictionary<string, string> ReadOnly(Dictionary<string, string> source) =>
        new ReadOnlyDictionary<string, string>(source);
}
