using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectWireCacheSignatureV1
{
    private const string Domain = "sts2_lan_connect_wire:v1";
    private const string TokenPrefix = "wcv1:";
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static string Compute(
        IReadOnlyList<string> categoryTable,
        IReadOnlyList<string> entryTable,
        IReadOnlyList<string> epochTable,
        IReadOnlyList<string> propertyTable,
        int categoryIdBitSize,
        int entryIdBitSize,
        int epochIdBitSize,
        int propertyIdBitSize)
    {
        ArgumentNullException.ThrowIfNull(categoryTable);
        ArgumentNullException.ThrowIfNull(entryTable);
        ArgumentNullException.ThrowIfNull(epochTable);
        ArgumentNullException.ThrowIfNull(propertyTable);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendStringFrame(hash, Domain);
        AppendTable(hash, "category", categoryTable);
        AppendTable(hash, "entry", entryTable);
        AppendTable(hash, "epoch", epochTable);
        AppendTable(hash, "property", propertyTable);
        AppendStringFrame(hash, "bit_widths");
        AppendInt32Frame(hash, categoryIdBitSize);
        AppendInt32Frame(hash, entryIdBitSize);
        AppendInt32Frame(hash, epochIdBitSize);
        AppendInt32Frame(hash, propertyIdBitSize);

        string digest = Convert.ToBase64String(hash.GetHashAndReset())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return TokenPrefix + digest;
    }

    private static void AppendTable(
        IncrementalHash hash,
        string name,
        IReadOnlyList<string> table)
    {
        AppendStringFrame(hash, name);
        AppendInt32Frame(hash, table.Count);
        for (int index = 0; index < table.Count; index++)
        {
            string value = table[index]
                ?? throw new InvalidOperationException($"Wire cache table {name} contains null at index {index}.");
            AppendStringFrame(hash, value);
        }
    }

    private static void AppendStringFrame(IncrementalHash hash, string value)
    {
        byte[] bytes = StrictUtf8.GetBytes(value);
        AppendFrameLength(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32Frame(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        AppendFrameLength(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendFrameLength(IncrementalHash hash, int length)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, length);
        hash.AppendData(bytes);
    }
}
