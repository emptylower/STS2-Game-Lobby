namespace Sts2LanConnect.Scripts;

internal static class LanConnectWireCacheSnapshotCapture
{
    internal static LanConnectWireCacheSnapshot Capture(Func<LanConnectWireCacheState> captureState)
    {
        ArgumentNullException.ThrowIfNull(captureState);

        LanConnectWireCacheState first = captureState()
            ?? throw new InvalidOperationException("Wire cache state capture returned null on the first pass.");
        LanConnectWireCacheState second = captureState()
            ?? throw new InvalidOperationException("Wire cache state capture returned null on the second pass.");

        if (!StatesEqual(first, second))
        {
            throw new InvalidOperationException(
                "ModelIdSerializationCache changed between two capture passes; refusing to sign an unstable snapshot.");
        }
        if (!first.Initialized)
        {
            throw new InvalidOperationException(
                "ModelIdSerializationCache is not initialized; refusing to compute a premature wire signature.");
        }

        ValidateInverse("category", first.CategoryTable, first.CategoryForwardMap);
        ValidateInverse("entry", first.EntryTable, first.EntryForwardMap);
        ValidateInverse("epoch", first.EpochTable, first.EpochForwardMap);
        ValidateInverse("property", first.PropertyTable, first.PropertyForwardMap);

        string signature = LanConnectWireCacheSignatureV1.Compute(
            first.CategoryTable,
            first.EntryTable,
            first.EpochTable,
            first.PropertyTable,
            first.CategoryIdBitSize,
            first.EntryIdBitSize,
            first.EpochIdBitSize,
            first.PropertyIdBitSize);

        return new LanConnectWireCacheSnapshot(
            signature,
            first.CategoryIdBitSize,
            first.EntryIdBitSize,
            first.EpochIdBitSize,
            first.PropertyIdBitSize,
            first.CategoryTable.Count,
            first.EntryTable.Count,
            first.EpochTable.Count,
            first.PropertyTable.Count,
            first.VanillaHash);
    }

    private static void ValidateInverse(
        string name,
        IReadOnlyList<string> reverseTable,
        IReadOnlyList<KeyValuePair<string, int>> forwardMap)
    {
        if (forwardMap.Count != reverseTable.Count)
        {
            throw new InvalidOperationException(
                $"Wire cache {name} forward/reverse counts disagree: forward={forwardMap.Count}, reverse={reverseTable.Count}.");
        }

        Dictionary<string, int> ordinalForwardMap = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> entry in forwardMap)
        {
            if (entry.Key == null)
            {
                throw new InvalidOperationException($"Wire cache {name} forward map contains a null name.");
            }
            if (!ordinalForwardMap.TryAdd(entry.Key, entry.Value))
            {
                throw new InvalidOperationException(
                    $"Wire cache {name} forward map contains duplicate ordinal name '{entry.Key}'.");
            }
        }

        HashSet<string> reverseNames = new(StringComparer.Ordinal);
        for (int index = 0; index < reverseTable.Count; index++)
        {
            string value = reverseTable[index]
                ?? throw new InvalidOperationException(
                    $"Wire cache {name} reverse table contains null at index {index}.");
            if (!reverseNames.Add(value))
            {
                throw new InvalidOperationException(
                    $"Wire cache {name} reverse table contains duplicate ordinal name '{value}'.");
            }
            if (!ordinalForwardMap.TryGetValue(value, out int netId))
            {
                throw new InvalidOperationException(
                    $"Wire cache {name} forward map is missing reverse name '{value}' at index {index}.");
            }
            if (netId != index)
            {
                throw new InvalidOperationException(
                    $"Wire cache {name} forward/reverse mapping disagrees for '{value}': forward={netId}, reverse={index}.");
            }
        }
    }

    private static bool StatesEqual(LanConnectWireCacheState first, LanConnectWireCacheState second)
    {
        return first.Initialized == second.Initialized &&
               TablesEqual(first.CategoryTable, second.CategoryTable) &&
               TablesEqual(first.EntryTable, second.EntryTable) &&
               TablesEqual(first.EpochTable, second.EpochTable) &&
               TablesEqual(first.PropertyTable, second.PropertyTable) &&
               ForwardMapsEqual(first.CategoryForwardMap, second.CategoryForwardMap) &&
               ForwardMapsEqual(first.EntryForwardMap, second.EntryForwardMap) &&
               ForwardMapsEqual(first.EpochForwardMap, second.EpochForwardMap) &&
               ForwardMapsEqual(first.PropertyForwardMap, second.PropertyForwardMap) &&
               first.CategoryIdBitSize == second.CategoryIdBitSize &&
               first.EntryIdBitSize == second.EntryIdBitSize &&
               first.EpochIdBitSize == second.EpochIdBitSize &&
               first.PropertyIdBitSize == second.PropertyIdBitSize &&
               first.VanillaHash == second.VanillaHash;
    }

    private static bool TablesEqual(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (int index = 0; index < first.Count; index++)
        {
            if (!string.Equals(first[index], second[index], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ForwardMapsEqual(
        IReadOnlyList<KeyValuePair<string, int>> first,
        IReadOnlyList<KeyValuePair<string, int>> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        KeyValuePair<string, int>[] orderedFirst = OrderForwardMap(first);
        KeyValuePair<string, int>[] orderedSecond = OrderForwardMap(second);
        for (int index = 0; index < orderedFirst.Length; index++)
        {
            if (!string.Equals(orderedFirst[index].Key, orderedSecond[index].Key, StringComparison.Ordinal) ||
                orderedFirst[index].Value != orderedSecond[index].Value)
            {
                return false;
            }
        }
        return true;
    }

    private static KeyValuePair<string, int>[] OrderForwardMap(
        IReadOnlyList<KeyValuePair<string, int>> forwardMap)
    {
        return forwardMap
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Value)
            .ToArray();
    }
}
