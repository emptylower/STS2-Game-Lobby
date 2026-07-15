using System;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectChatFeatureResolver
{
    private static readonly LanConnectChatFeatureVersions AllDeclared = new(1, 1, 1, 1);

    internal static LanConnectChatFeatureVersions Resolve(LanConnectChatFeatureInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.ChannelEnabled ||
            (input.Channel == LanConnectChatChannel.Room && !input.RoomV2Enabled))
        {
            return new LanConnectChatFeatureVersions();
        }

        LanConnectChatFeatureVersions sender = input.Sender ?? AllDeclared;
        LanConnectChatFeatureVersions receiver = input.Receiver ?? AllDeclared;
        int rich = Enabled(
            input.Compiled.RichContentVersion,
            input.Admin?.RichContentVersion ?? input.Configured.RichContentVersion,
            sender.RichContentVersion,
            receiver.RichContentVersion);
        if (rich == 0)
        {
            return new LanConnectChatFeatureVersions();
        }

        return new LanConnectChatFeatureVersions(
            rich,
            Enabled(
                input.Compiled.EmojiSetVersion,
                input.Admin?.EmojiSetVersion ?? input.Configured.EmojiSetVersion,
                sender.EmojiSetVersion,
                receiver.EmojiSetVersion),
            Enabled(
                input.Compiled.ItemRefVersion,
                input.Admin?.ItemRefVersion ?? input.Configured.ItemRefVersion,
                sender.ItemRefVersion,
                receiver.ItemRefVersion),
            input.Channel == LanConnectChatChannel.Room
                ? Enabled(
                    input.Compiled.CombatRefVersion,
                    input.Admin?.CombatRefVersion ?? input.Configured.CombatRefVersion,
                    sender.CombatRefVersion,
                    receiver.CombatRefVersion)
                : 0);
    }

    internal static bool SupportsContent(
        LanConnectChatContent content,
        LanConnectChatFeatureVersions enabled)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(enabled);
        if (content.Segments == null)
        {
            return false;
        }

        foreach (LanConnectChatSegment segment in content.Segments)
        {
            switch (segment)
            {
                case LanConnectTextSegment:
                    break;
                case LanConnectEmojiSegment when
                    enabled.RichContentVersion == 1 && enabled.EmojiSetVersion == 1:
                    break;
                case LanConnectItemRefSegment when
                    enabled.RichContentVersion == 1 && enabled.ItemRefVersion == 1:
                    break;
                case LanConnectPowerStateSegment when
                    enabled.RichContentVersion == 1 && enabled.CombatRefVersion == 1:
                    break;
                case LanConnectTargetRefSegment { TargetKind: "player" } when
                    enabled.RichContentVersion == 1 && enabled.CombatRefVersion == 1:
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    private static int Enabled(int compiled, int configured, int sender, int receiver) =>
        compiled == 1 && configured == 1 && sender == 1 && receiver == 1 ? 1 : 0;
}
