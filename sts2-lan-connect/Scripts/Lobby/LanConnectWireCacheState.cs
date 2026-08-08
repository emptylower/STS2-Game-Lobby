namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectWireCacheState(
    bool Initialized,
    IReadOnlyList<string> CategoryTable,
    IReadOnlyList<string> EntryTable,
    IReadOnlyList<string> EpochTable,
    IReadOnlyList<string> PropertyTable,
    IReadOnlyList<KeyValuePair<string, int>> CategoryForwardMap,
    IReadOnlyList<KeyValuePair<string, int>> EntryForwardMap,
    IReadOnlyList<KeyValuePair<string, int>> EpochForwardMap,
    IReadOnlyList<KeyValuePair<string, int>> PropertyForwardMap,
    int CategoryIdBitSize,
    int EntryIdBitSize,
    int EpochIdBitSize,
    int PropertyIdBitSize,
    uint VanillaHash);
