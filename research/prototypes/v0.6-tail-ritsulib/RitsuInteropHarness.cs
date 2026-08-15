using System;
using System.IO;
using System.Linq;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2TailPrototype;

internal sealed record CarrierResult(byte[] ContainerBytes, long VanillaBodyEndBit, long ContainerStartBit, long ContainerEndBit, bool AlignmentPaddingWasZero);
internal sealed record SidecarCarrierResult(byte[] ContainerBytes, bool TrustedTicketHintBootstrappedReachability, bool SidecarReachableBeforeFirstLanFlow, bool HandlerBlockedUntilPairValidated, bool VanillaBytesMatchFixture, bool StandaloneTailPresent, bool HintClearedOnTeardown, bool ReusedPeerIdStartsUnknown);

internal static class InteropFixtures
{
    internal static readonly byte[] ExpectedContainer = FixtureFiles.ReadBytes("tail-probe-complete-v1.bin");
}

internal static class FixtureFiles
{
    internal static byte[] ReadBytes(string fileName)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, fileName);
            if (File.Exists(candidate)) return File.ReadAllBytes(candidate);
            current = current.Parent;
        }
        throw new FileNotFoundException($"Fixture '{fileName}' was not found.", fileName);
    }
}

internal static class RitsuInteropHarness
{
    internal static CarrierResult RoundTripStandalone()
    {
        PacketWriter writer = new() { WarnOnGrow = false };
        writer.WriteBool(true);
        writer.WriteBool(false);
        writer.WriteBool(true);
        long vanillaBodyEndBit = writer.BitPosition;
        long containerStartBit = StandaloneContainerCodec.Write(writer);
        long containerEndBit = writer.BitPosition;
        byte[] wire = writer.Buffer.AsSpan(0, checked((int)((writer.BitPosition + 7) / 8))).ToArray();

        PacketReader reader = new();
        reader.Reset(wire);
        _ = reader.ReadBool();
        _ = reader.ReadBool();
        _ = reader.ReadBool();
        bool paddingWasZero = StandaloneContainerCodec.Read(reader);
        byte[] container = wire.AsSpan(checked((int)(containerStartBit / 8)), InteropFixtures.ExpectedContainer.Length).ToArray();
        return new CarrierResult(container, vanillaBodyEndBit, containerStartBit, containerEndBit, paddingWasZero);
    }

    internal static System.Threading.Tasks.Task<SidecarCarrierResult> RunRealTwoProcessSidecarAsync() =>
        SidecarCarrierProbe.RunExternalTwoProcessProbeAsync();
}

internal static class StandaloneContainerCodec
{
    private const int ByteBits = 8;

    internal static long Write(PacketWriter writer)
    {
        while (writer.BitPosition % ByteBits != 0) writer.WriteBool(false);
        long startBit = writer.BitPosition;
        foreach (byte value in InteropFixtures.ExpectedContainer) writer.WriteByte(value, ByteBits);
        return startBit;
    }

    internal static bool Read(PacketReader reader)
    {
        bool paddingWasZero = true;
        while (reader.BitPosition % ByteBits != 0) paddingWasZero &= !reader.ReadBool();
        byte[] actual = new byte[InteropFixtures.ExpectedContainer.Length];
        for (int index = 0; index < actual.Length; index++) actual[index] = reader.ReadByte(ByteBits);
        if (!actual.SequenceEqual(InteropFixtures.ExpectedContainer)) throw new InvalidDataException("Standalone carrier container bytes drifted from the frozen fixture.");
        return paddingWasZero;
    }
}
