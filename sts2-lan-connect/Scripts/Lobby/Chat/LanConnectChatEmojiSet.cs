using System.Collections.ObjectModel;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectEmojiDescriptor(
    string Id,
    string LucideIcon,
    string LabelKey);

internal static class LanConnectChatEmojiSet
{
    internal const int Version = 1;

    private static readonly ReadOnlyCollection<LanConnectEmojiDescriptor> Published = Array.AsReadOnly(
    [
        Emoji("smile"),
        Emoji("laugh"),
        Emoji("heart"),
        Emoji("thumbs-up"),
        Emoji("thumbs-down"),
        Emoji("sparkles"),
        Emoji("flame"),
        Emoji("zap"),
        Emoji("shield"),
        Emoji("swords"),
        Emoji("target"),
        Emoji("crown"),
        Emoji("skull"),
        Emoji("ghost"),
        Emoji("eye"),
        Emoji("message-circle"),
        Emoji("check"),
        Emoji("x")
    ]);

    private static readonly IReadOnlyDictionary<string, LanConnectEmojiDescriptor> ById =
        new ReadOnlyDictionary<string, LanConnectEmojiDescriptor>(
            Published.ToDictionary(emoji => emoji.Id, StringComparer.Ordinal));

    internal static IReadOnlyList<LanConnectEmojiDescriptor> Version1 => Published;

    internal static bool TryGet(string? id, out LanConnectEmojiDescriptor descriptor)
    {
        if (id != null && ById.TryGetValue(id, out LanConnectEmojiDescriptor? found))
        {
            descriptor = found;
            return true;
        }
        descriptor = null!;
        return false;
    }

    private static LanConnectEmojiDescriptor Emoji(string id) =>
        new(id, id, $"chat.emoji.{id}");
}
