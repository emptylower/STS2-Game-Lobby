namespace Sts2LanConnect.Scripts;

/// <summary>
/// Maps a chat segment to the localisation key for its verb phrase (e.g. "shared a
/// relic: " / "分享了遗物：") and resolves the finished phrase for a given locale.
/// Purely a rendering concern -- it never touches the wire format.
///
/// Scope note (phase 3, Task 2): only <see cref="LanConnectItemRefSegment"/> is mapped.
/// <see cref="LanConnectPowerStateSegment"/> and <see cref="LanConnectTargetRefSegment"/>
/// are intentionally left unmapped (<see cref="KeyFor"/> returns null): neither segment
/// carries a creature/monster identity that is reliably resolvable at render time.
/// <see cref="LanConnectPowerStateSegment"/> has no monster/creature field at all -- only
/// owner/applier *player* net ids -- and <see cref="LanConnectTargetRefSegment"/>'s
/// monster case has no working name resolution in production
/// (<c>LanConnectMonsterTargetIdPrototype</c> is wired up with <c>adapter: null</c>, and
/// <c>LanConnectRoomCombatReferenceResolver.Resolve</c> has no monster branch, always
/// falling back to the "target expired" copy). A future task can add these once a real
/// creature reference is threaded through.
/// </summary>
internal static class LanConnectChatVerbPhrase
{
    internal static string? KeyFor(LanConnectChatSegment segment) => segment switch
    {
        LanConnectItemRefSegment item => item.ItemType switch
        {
            "card" => "chat.verb.shared.card",
            "relic" => "chat.verb.shared.relic",
            "potion" => "chat.verb.shared.potion",
            _ => "chat.verb.shared.item"
        },
        _ => null
    };

    internal static string? PhraseFor(
        LanConnectChatSegment segment,
        LanConnectChatLocalizer localizer,
        string? locale)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        string? key = KeyFor(segment);
        return key == null ? null : localizer.Get(locale, key);
    }
}
