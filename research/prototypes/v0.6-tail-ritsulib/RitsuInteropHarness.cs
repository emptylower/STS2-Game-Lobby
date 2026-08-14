using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using STS2RitsuLib.Networking.MessageExtensions;

namespace Sts2TailPrototype;

internal sealed record InteropResult(
    byte[] LanTailBytes,
    long LanTailEndBit,
    long RitsuReadStartBit,
    string Message);

internal static class InteropFixtures
{
    // A complete valid Tail v1 container, not just the magic. The byte table
    // and every offset are independently reviewed in the adjacent fixture JSON.
    internal static readonly byte[] ExpectedLanTail = FixtureFiles.ReadBytes(
        "tail-probe-complete-v1.bin");
    internal const string ExpectedMessage = "round-trip-ok";
}

internal static class FixtureFiles
{
    internal static byte[] ReadBytes(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        string? overrideDir = Environment.GetEnvironmentVariable("STS2_TAIL_FIXTURE_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            string overridePath = Path.Combine(overrideDir, fileName);
            if (File.Exists(overridePath))
            {
                return File.ReadAllBytes(overridePath);
            }
        }

        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, fileName);
            if (File.Exists(candidate))
            {
                return File.ReadAllBytes(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Fixture '{fileName}' was not found beside the test assembly or in any parent directory.",
            fileName);
    }
}

internal static class PrototypeAssemblyResolution
{
    private const string Sts2DataDirEnvironmentVariable = "STS2_DATA_DIR";
    private const string RitsuLibDirEnvironmentVariable = "RITSULIB_DIR";
    private const string MacOsDefaultSts2DataDir =
        "Library/Application Support/Steam/steamapps/common/Slay the Spire 2/" +
        "SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64";

    private static int _installed;

    [ModuleInitializer]
    internal static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromGameDirectories;
    }

    private static Assembly? ResolveFromGameDirectories(object? sender, ResolveEventArgs args)
    {
        AssemblyName requested = new(args.Name);
        foreach (string directory in CandidateDirectories())
        {
            string candidate = Path.Combine(directory, requested.Name + ".dll");
            if (File.Exists(candidate))
            {
                try
                {
                    return Assembly.LoadFrom(candidate);
                }
                catch (FileLoadException)
                {
                    // Fall through to the next candidate directory.
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        string? sts2DataDir = Environment.GetEnvironmentVariable(Sts2DataDirEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(sts2DataDir))
        {
            yield return sts2DataDir;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, MacOsDefaultSts2DataDir);

        string? ritsuLibDir = Environment.GetEnvironmentVariable(RitsuLibDirEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(ritsuLibDir))
        {
            yield return ritsuLibDir;
        }
    }
}

internal static class RitsuInteropHarness
{
    private const string ProbeExtensionId = "lan.ritsu.probe";
    private const int ProbeExtensionVersion = 1;
    private static readonly byte[] ProbeExtensionPayload = [0x42];

    private static readonly ConcurrentDictionary<Type, byte[]> ReceivedPayloads = new();
    private static readonly ConcurrentDictionary<Type, bool> RegisteredMessageTypes = new();

    internal static InteropResult RoundTrip(bool senderHasRitsu, bool receiverHasRitsu)
    {
        // RitsuLib registrations are process-wide and cannot be removed, so each of the
        // four install combinations gets its own message key type.
        return (senderHasRitsu, receiverHasRitsu) switch
        {
            (false, false) => RoundTripCore<CombinationNeitherMessage>(senderHasRitsu, receiverHasRitsu),
            (true, false) => RoundTripCore<CombinationSenderOnlyMessage>(senderHasRitsu, receiverHasRitsu),
            (false, true) => RoundTripCore<CombinationReceiverOnlyMessage>(senderHasRitsu, receiverHasRitsu),
            (true, true) => RoundTripCore<CombinationBothMessage>(senderHasRitsu, receiverHasRitsu),
        };
    }

    private static InteropResult RoundTripCore<TMessage>(bool senderHasRitsu, bool receiverHasRitsu)
        where TMessage : class, new()
    {
        Type messageType = typeof(TMessage);
        EnsureRegistered<TMessage>();

        PacketWriter writer = new() { WarnOnGrow = false };
        LanTailV1Codec.WriteProbeContainer(writer);
        long lanWriteEndBit = writer.BitPosition;
        if (senderHasRitsu)
        {
            RitsuNetMessageTailExtensions.Write(writer, new TMessage());
        }

        int wireByteLength = checked((writer.BitPosition + 7) / 8);
        byte[] wire = writer.Buffer.AsSpan(0, wireByteLength).ToArray();
        byte[] lanTailBytes = wire.AsSpan(0, checked((int)(lanWriteEndBit / 8))).ToArray();

        PacketReader reader = new();
        reader.Reset(wire);
        LanTailV1Codec.ReadProbeContainer(reader);
        long lanTailEndBit = reader.BitPosition;
        long ritsuReadStartBit = reader.BitPosition;
        if (receiverHasRitsu)
        {
            RitsuNetMessageTailExtensions.Read<TMessage>(reader);
        }

        if (senderHasRitsu && receiverHasRitsu &&
            (!ReceivedPayloads.TryGetValue(messageType, out byte[]? payload) ||
             payload == null ||
             !payload.SequenceEqual(ProbeExtensionPayload)))
        {
            throw new InvalidOperationException(
                $"Ritsu tail extension payload was not dispatched for {messageType.Name}.");
        }

        return new InteropResult(lanTailBytes, lanTailEndBit, ritsuReadStartBit, InteropFixtures.ExpectedMessage);
    }

    private static void EnsureRegistered<TMessage>()
        where TMessage : class
    {
        if (RegisteredMessageTypes.ContainsKey(typeof(TMessage)))
        {
            return;
        }

        RitsuNetMessageTailExtensions.RegisterBytes<TMessage>(
            ProbeExtensionId,
            ProbeExtensionVersion,
            static _ => ProbeExtensionPayload,
            (version, payload) => ReceivedPayloads[typeof(TMessage)] = payload.ToArray());
        RegisteredMessageTypes[typeof(TMessage)] = true;
    }

    private sealed class CombinationNeitherMessage;

    private sealed class CombinationSenderOnlyMessage;

    private sealed class CombinationReceiverOnlyMessage;

    private sealed class CombinationBothMessage;
}

internal static class LanTailV1Codec
{
    private const int ByteBits = 8;
    private const byte ContainerVersion = 1;
    private const byte ContainerFlags = 0;
    private const ushort ProbeSessionProtocolVersion = 1;
    private const ushort ProbeEntryVersion = 1;
    private const byte ProbeEntryFlags = 1;
    private static readonly byte[] Magic = "STSLAN01"u8.ToArray();
    private static readonly byte[] ProbeEntryId = "lan.probe"u8.ToArray();
    private static readonly byte[] ProbePayload = [0x01];

    internal static void WriteProbeContainer(PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        AlignWriterToByteBoundary(writer);
        long containerStartBit = writer.BitPosition;

        byte[] body = BuildContainerBody();
        foreach (byte value in Magic)
        {
            writer.WriteByte(value, ByteBits);
        }

        foreach (byte value in body)
        {
            writer.WriteByte(value, ByteBits);
        }

        long containerBits = writer.BitPosition - containerStartBit;
        if (containerBits != (Magic.Length + body.Length) * (long)ByteBits)
        {
            throw new InvalidOperationException($"LAN tail container bit count drifted: {containerBits}.");
        }
    }

    internal static void ReadProbeContainer(PacketReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        AlignReaderToByteBoundary(reader);
        long containerStartBit = reader.BitPosition;

        byte[] magic = ReadRawBytes(reader, Magic.Length);
        if (!magic.SequenceEqual(Magic))
        {
            throw new InvalidDataException("LAN tail magic mismatch.");
        }

        byte containerVersion = reader.ReadByte(ByteBits);
        if (containerVersion != ContainerVersion)
        {
            throw new InvalidDataException($"Unsupported LAN tail container version {containerVersion}.");
        }

        byte containerFlags = reader.ReadByte(ByteBits);
        if (containerFlags != ContainerFlags)
        {
            throw new InvalidDataException($"Unsupported LAN tail container flags {containerFlags}.");
        }

        uint containerByteLength = ReadUInt32BigEndian(reader);
        long containerEndBit = containerStartBit + ((long)Magic.Length + containerByteLength) * ByteBits;
        if (containerEndBit > (long)reader.Buffer.Length * ByteBits)
        {
            throw new InvalidDataException("LAN tail container exceeds the packet buffer.");
        }

        ushort sessionProtocolVersion = ReadUInt16BigEndian(reader);
        if (sessionProtocolVersion != ProbeSessionProtocolVersion)
        {
            throw new InvalidDataException($"Unsupported LAN session protocol {sessionProtocolVersion}.");
        }

        ushort entryCount = ReadUInt16BigEndian(reader);
        HashSet<string> seenIds = new(StringComparer.Ordinal);
        for (int index = 0; index < entryCount; index++)
        {
            byte idByteLength = reader.ReadByte(ByteBits);
            string id = Encoding.UTF8.GetString(ReadRawBytes(reader, idByteLength));
            if (!seenIds.Add(id))
            {
                throw new InvalidDataException($"Duplicate LAN tail entry id '{id}'.");
            }

            ushort entryVersion = ReadUInt16BigEndian(reader);
            byte entryFlags = reader.ReadByte(ByteBits);
            if ((entryFlags & ~1) != 0)
            {
                throw new InvalidDataException($"Unknown LAN tail entry flags {entryFlags} for '{id}'.");
            }

            uint payloadByteLength = ReadUInt32BigEndian(reader);
            _ = ReadRawBytes(reader, checked((int)payloadByteLength));
            if (id == "lan.probe" && entryVersion != ProbeEntryVersion)
            {
                throw new InvalidDataException($"Unsupported lan.probe entry version {entryVersion}.");
            }
        }

        if (reader.BitPosition != containerEndBit)
        {
            throw new InvalidDataException(
                $"LAN tail container was not consumed exactly: cursor={reader.BitPosition}, expected={containerEndBit}.");
        }
    }

    private static byte[] BuildContainerBody()
    {
        List<byte> body =
        [
            ContainerVersion,
            ContainerFlags,
        ];

        // containerByteLength counts every byte after the magic, including this field.
        uint containerByteLength = (uint)(1 + 1 + 4 + 2 + 2 + 1 + ProbeEntryId.Length + 2 + 1 + 4 + ProbePayload.Length);
        AppendUInt32BigEndian(body, containerByteLength);
        AppendUInt16BigEndian(body, ProbeSessionProtocolVersion);
        AppendUInt16BigEndian(body, 1);
        body.Add((byte)ProbeEntryId.Length);
        body.AddRange(ProbeEntryId);
        AppendUInt16BigEndian(body, ProbeEntryVersion);
        body.Add(ProbeEntryFlags);
        AppendUInt32BigEndian(body, (uint)ProbePayload.Length);
        body.AddRange(ProbePayload);
        if (body.Count != containerByteLength)
        {
            throw new InvalidOperationException(
                $"LAN tail body length {body.Count} disagrees with declared {containerByteLength}.");
        }

        return [.. body];
    }

    private static void AlignWriterToByteBoundary(PacketWriter writer)
    {
        while (writer.BitPosition % ByteBits != 0)
        {
            writer.WriteBool(false);
        }
    }

    private static void AlignReaderToByteBoundary(PacketReader reader)
    {
        while (reader.BitPosition % ByteBits != 0)
        {
            reader.ReadBool();
        }
    }

    private static void AppendUInt16BigEndian(List<byte> body, ushort value)
    {
        body.Add((byte)(value >> ByteBits));
        body.Add((byte)(value & 0xFF));
    }

    private static void AppendUInt32BigEndian(List<byte> body, uint value)
    {
        body.Add((byte)(value >> 24));
        body.Add((byte)((value >> 16) & 0xFF));
        body.Add((byte)((value >> ByteBits) & 0xFF));
        body.Add((byte)(value & 0xFF));
    }

    private static ushort ReadUInt16BigEndian(PacketReader reader)
    {
        return (ushort)((reader.ReadByte(ByteBits) << ByteBits) | reader.ReadByte(ByteBits));
    }

    private static uint ReadUInt32BigEndian(PacketReader reader)
    {
        return ((uint)reader.ReadByte(ByteBits) << 24) |
               ((uint)reader.ReadByte(ByteBits) << 16) |
               ((uint)reader.ReadByte(ByteBits) << ByteBits) |
               reader.ReadByte(ByteBits);
    }

    private static byte[] ReadRawBytes(PacketReader reader, int byteCount)
    {
        if (byteCount < 0 || reader.BitPosition + (long)byteCount * ByteBits > (long)reader.Buffer.Length * ByteBits)
        {
            throw new InvalidDataException("LAN tail read exceeds the packet buffer.");
        }

        byte[] data = new byte[byteCount];
        for (int index = 0; index < byteCount; index++)
        {
            data[index] = reader.ReadByte(ByteBits);
        }

        return data;
    }
}
