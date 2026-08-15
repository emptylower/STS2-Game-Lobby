namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectRosterProjectionItem<T>(
    ulong PlayerId,
    byte RealSlotId,
    int CanonicalIndex,
    T VanillaPlayer);

internal static class LanConnectRosterProjection
{
    internal static IReadOnlyList<LanConnectRosterProjectionItem<T>> Create<T>(
        IReadOnlyList<T> players,
        Func<T, ulong> getPlayerId,
        Func<T, int> getRealSlotId,
        Func<T, int, T> withEmbeddedSlot)
    {
        IReadOnlyList<(T Player, ulong Id, byte Slot)> canonical = Canonicalize(
            players,
            getPlayerId,
            getRealSlotId);
        return canonical
            .Take(4)
            .Select((value, index) => new LanConnectRosterProjectionItem<T>(
                value.Id,
                value.Slot,
                index,
                withEmbeddedSlot(value.Player, index % 4)))
            .ToArray();
    }

    internal static void Validate<T>(
        LanConnectRosterSnapshot snapshot,
        IReadOnlyList<T> vanillaProjection,
        Func<T, ulong> getPlayerId,
        Func<T, int> getEmbeddedSlotId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(vanillaProjection);
        ArgumentNullException.ThrowIfNull(getPlayerId);
        ArgumentNullException.ThrowIfNull(getEmbeddedSlotId);
        int expectedCount = Math.Min(4, snapshot.Players.Count);
        if (vanillaProjection.Count != expectedCount)
        {
            throw Invalid($"Vanilla projection count {vanillaProjection.Count} differs from {expectedCount}.");
        }

        IReadOnlyList<LanConnectRosterPlayerCarrier> canonical = snapshot.Players
            .OrderBy(static player => player.RealSlotId)
            .ThenBy(static player => player.PlayerId)
            .ToArray();
        for (int index = 0; index < expectedCount; index++)
        {
            if (getPlayerId(vanillaProjection[index]) != canonical[index].PlayerId
                || getEmbeddedSlotId(vanillaProjection[index]) != index % 4)
            {
                throw Invalid($"Vanilla projection player {index} does not match the authoritative roster.");
            }
        }
    }

    internal static IReadOnlyList<T> Restore<T>(
        LanConnectRosterSnapshot snapshot,
        Func<LanConnectRosterPlayerCarrier, (T Player, uint ConsumedBits)> deserializeCarrier,
        Func<T, ulong> getPlayerId,
        Func<T, int> getEmbeddedSlotId,
        Func<T, int, T> withRealSlot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(deserializeCarrier);
        ArgumentNullException.ThrowIfNull(getPlayerId);
        ArgumentNullException.ThrowIfNull(getEmbeddedSlotId);
        ArgumentNullException.ThrowIfNull(withRealSlot);

        IReadOnlyList<LanConnectRosterPlayerCarrier> canonical = snapshot.Players
            .OrderBy(static player => player.RealSlotId)
            .ThenBy(static player => player.PlayerId)
            .ToArray();
        List<T> restored = new(canonical.Count);
        for (int index = 0; index < canonical.Count; index++)
        {
            LanConnectRosterPlayerCarrier carrier = canonical[index];
            (T player, uint consumedBits) = deserializeCarrier(carrier);
            if (consumedBits != carrier.VanillaPlayerBitLength
                || getPlayerId(player) != carrier.PlayerId
                || getEmbeddedSlotId(player) != index % 4)
            {
                throw Invalid($"Vanilla player carrier {index} failed exact identity/slot/bit consumption.");
            }

            restored.Add(withRealSlot(player, carrier.RealSlotId));
        }

        return restored;
    }

    private static IReadOnlyList<(T Player, ulong Id, byte Slot)> Canonicalize<T>(
        IReadOnlyList<T> players,
        Func<T, ulong> getPlayerId,
        Func<T, int> getRealSlotId)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(getPlayerId);
        ArgumentNullException.ThrowIfNull(getRealSlotId);
        if (players.Count is < LanConnectConstants.ProtocolMinPlayers or > LanConnectConstants.ProtocolMaxPlayers)
        {
            throw Invalid("Projection roster must contain 2..8 players.");
        }

        HashSet<ulong> ids = [];
        HashSet<byte> slots = [];
        List<(T Player, ulong Id, byte Slot)> values = new(players.Count);
        foreach (T player in players)
        {
            ulong id = getPlayerId(player);
            int slotValue = getRealSlotId(player);
            if (slotValue is < 0 or >= LanConnectConstants.ProtocolMaxPlayers)
            {
                throw Invalid($"Real slot {slotValue} is outside 0..7.");
            }

            byte slot = checked((byte)slotValue);
            if (!ids.Add(id) || !slots.Add(slot))
            {
                throw Invalid("Projection roster contains a duplicate player ID or real slot.");
            }

            values.Add((player, id, slot));
        }

        return values
            .OrderBy(static value => value.Slot)
            .ThenBy(static value => value.Id)
            .ToArray();
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
