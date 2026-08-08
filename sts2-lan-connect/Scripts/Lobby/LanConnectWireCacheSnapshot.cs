namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectWireCacheSnapshot(
    string Signature,
    int CategoryIdBitSize,
    int EntryIdBitSize,
    int EpochIdBitSize,
    int PropertyIdBitSize,
    int CategoryCount,
    int EntryCount,
    int EpochCount,
    int PropertyCount,
    uint VanillaHash);
