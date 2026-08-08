using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Sts2LanConnect.Scripts;

internal enum LanConnectWireCacheHandshakeTokenStatus
{
    Absent,
    Valid,
    Malformed,
    Duplicate
}

internal sealed record LanConnectWireCacheHandshakeToken(
    string Signature,
    int? CategoryIdBitSize,
    int? EntryIdBitSize,
    int? EpochIdBitSize,
    int? PropertyIdBitSize)
{
    internal const string Prefix = "sts2_lan_connect_wire:v1:";
    private const string WidthMarker = ":bits=";

    internal bool HasWidths =>
        CategoryIdBitSize.HasValue &&
        EntryIdBitSize.HasValue &&
        EpochIdBitSize.HasValue &&
        PropertyIdBitSize.HasValue;

    internal string Serialize()
    {
        if (!HasWidths)
        {
            return Prefix + Signature;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}{Signature}{WidthMarker}{CategoryIdBitSize},{EntryIdBitSize},{EpochIdBitSize},{PropertyIdBitSize}");
    }

    internal string FormatWidths() => HasWidths
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"category={CategoryIdBitSize}, entry={EntryIdBitSize}, epoch={EpochIdBitSize}, property={PropertyIdBitSize}")
        : "category=未知, entry=未知, epoch=未知, property=未知";

    internal static LanConnectWireCacheHandshakeToken FromSnapshot(LanConnectWireCacheSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new LanConnectWireCacheHandshakeToken(
            snapshot.Signature,
            snapshot.CategoryIdBitSize,
            snapshot.EntryIdBitSize,
            snapshot.EpochIdBitSize,
            snapshot.PropertyIdBitSize);
    }

    internal static LanConnectWireCacheHandshakeTokenParseResult Parse(IEnumerable<string>? otherMods)
    {
        List<string> sentinels = otherMods?
            .Where(IsSentinel)
            .ToList() ?? [];
        if (sentinels.Count == 0)
        {
            return new LanConnectWireCacheHandshakeTokenParseResult(
                LanConnectWireCacheHandshakeTokenStatus.Absent,
                null);
        }
        if (sentinels.Count != 1)
        {
            return new LanConnectWireCacheHandshakeTokenParseResult(
                LanConnectWireCacheHandshakeTokenStatus.Duplicate,
                null);
        }

        return TryParse(sentinels[0], out LanConnectWireCacheHandshakeToken? token)
            ? new LanConnectWireCacheHandshakeTokenParseResult(
                LanConnectWireCacheHandshakeTokenStatus.Valid,
                token)
            : new LanConnectWireCacheHandshakeTokenParseResult(
                LanConnectWireCacheHandshakeTokenStatus.Malformed,
                null);
    }

    internal static List<string> FilterSentinels(IEnumerable<string>? mods) => mods?
        .Where(static mod => !IsSentinel(mod))
        .Where(static mod => !string.IsNullOrWhiteSpace(mod))
        .Select(static mod => mod.Trim())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(static mod => mod, StringComparer.Ordinal)
        .ToList() ?? [];

    internal static List<string>? ReplaceSentinels(
        IEnumerable<string>? otherMods,
        LanConnectWireCacheHandshakeToken? currentToken)
    {
        List<string> filtered = otherMods?
            .Where(static mod => !IsSentinel(mod))
            .ToList() ?? [];
        if (currentToken != null)
        {
            filtered.Add(currentToken.Serialize());
        }

        return otherMods == null && currentToken == null ? null : filtered;
    }

    internal static bool IsSentinel(string? value) =>
        value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    private static bool TryParse(
        string sentinel,
        out LanConnectWireCacheHandshakeToken? token)
    {
        token = null;
        string payload = sentinel[Prefix.Length..];
        int widthMarkerIndex = payload.IndexOf(WidthMarker, StringComparison.Ordinal);
        string signature = widthMarkerIndex < 0
            ? payload
            : payload[..widthMarkerIndex];
        if (!IsValidSignature(signature))
        {
            return false;
        }

        if (widthMarkerIndex < 0)
        {
            token = new LanConnectWireCacheHandshakeToken(signature, null, null, null, null);
            return true;
        }

        string[] widthParts = payload[(widthMarkerIndex + WidthMarker.Length)..].Split(',');
        if (widthParts.Length != 4 ||
            widthParts.Any(static value =>
                !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int width) ||
                width is < 0 or > 32))
        {
            return false;
        }

        token = new LanConnectWireCacheHandshakeToken(
            signature,
            int.Parse(widthParts[0], CultureInfo.InvariantCulture),
            int.Parse(widthParts[1], CultureInfo.InvariantCulture),
            int.Parse(widthParts[2], CultureInfo.InvariantCulture),
            int.Parse(widthParts[3], CultureInfo.InvariantCulture));
        return true;
    }

    private static bool IsValidSignature(string signature)
    {
        const string signaturePrefix = "wcv1:";
        const int digestLength = 43;
        return signature.StartsWith(signaturePrefix, StringComparison.Ordinal) &&
               signature.Length == signaturePrefix.Length + digestLength &&
               signature[signaturePrefix.Length..].All(static character =>
                   char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }
}

internal sealed record LanConnectWireCacheHandshakeTokenParseResult(
    LanConnectWireCacheHandshakeTokenStatus Status,
    LanConnectWireCacheHandshakeToken? Token);
