using System.Buffers.Binary;

namespace Sts2LanConnect.Scripts;

internal enum LanConnectSidecarMessageKind : byte
{
    InitialGameInfo = 1,
    LobbyJoinRequest = 2,
    LobbyJoinResponse = 3,
    LoadJoinRequest = 4,
    LoadJoinResponse = 5,
    RejoinRequest = 6,
    RejoinResponse = 7,
    ConnectionFailed = 8,
    PlayerJoined = 9,
    LobbyBeginRun = 10
}

internal sealed class LanConnectSidecarFrame
{
    private readonly byte[] _flowNonce;
    private readonly byte[] _container;

    internal LanConnectSidecarFrame(
        LanConnectSidecarMessageKind messageKind,
        ReadOnlySpan<byte> flowNonce,
        uint messageSequence,
        ReadOnlySpan<byte> container)
    {
        MessageKind = messageKind;
        _flowNonce = flowNonce.ToArray();
        MessageSequence = messageSequence;
        _container = container.ToArray();
    }

    internal LanConnectSidecarMessageKind MessageKind { get; }
    internal ReadOnlyMemory<byte> FlowNonce => _flowNonce;
    internal uint MessageSequence { get; }
    internal ReadOnlyMemory<byte> Container => _container;
}

internal static class LanConnectSidecarFrameCodec
{
    internal const int FlowNonceBytes = 16;
    private const byte CarrierVersion = 1;
    private const int HeaderBytes = 26;

    internal static byte[] Encode(LanConnectSidecarFrame frame)
    {
        Validate(frame);
        byte[] payload = new byte[checked(HeaderBytes + frame.Container.Length)];
        payload[0] = CarrierVersion;
        payload[1] = (byte)frame.MessageKind;
        frame.FlowNonce.Span.CopyTo(payload.AsSpan(2, FlowNonceBytes));
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(18, 4), frame.MessageSequence);
        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(22, 4),
            checked((uint)frame.Container.Length));
        frame.Container.Span.CopyTo(payload.AsSpan(HeaderBytes));
        return payload;
    }

    internal static LanConnectSidecarFrame Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < HeaderBytes || payload[0] != CarrierVersion)
        {
            throw Invalid("Sidecar frame is truncated or has an unsupported carrier version.");
        }

        LanConnectSidecarMessageKind kind = (LanConnectSidecarMessageKind)payload[1];
        uint sequence = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(18, 4));
        uint containerLength = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(22, 4));
        int expectedLength;
        try
        {
            expectedLength = checked(HeaderBytes + (int)containerLength);
        }
        catch (OverflowException exception)
        {
            throw Invalid("Sidecar container length overflows Int32.", exception);
        }

        if (payload.Length != expectedLength)
        {
            throw Invalid("Sidecar frame container length is truncated or has trailing bytes.");
        }

        LanConnectSidecarFrame frame = new(kind, payload.Slice(2, FlowNonceBytes), sequence, payload[HeaderBytes..]);
        Validate(frame);
        return frame;
    }

    internal static byte[] ParseFlowNonce(string value)
    {
        if (value.Length != FlowNonceBytes * 2
            || value.Any(static character => !char.IsAsciiHexDigit(character) || char.IsAsciiLetterUpper(character)))
        {
            throw Invalid("Flow nonce must be 32 lowercase hexadecimal characters.");
        }

        return Convert.FromHexString(value);
    }

    internal static string FormatFlowNonce(ReadOnlySpan<byte> value)
    {
        if (value.Length != FlowNonceBytes)
        {
            throw Invalid("Flow nonce must contain exactly 16 bytes.");
        }

        return Convert.ToHexString(value).ToLowerInvariant();
    }

    private static void Validate(LanConnectSidecarFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!Enum.IsDefined(frame.MessageKind))
        {
            throw Invalid($"Unknown sidecar message kind {(byte)frame.MessageKind}.");
        }

        if (frame.FlowNonce.Length != FlowNonceBytes || frame.MessageSequence == 0)
        {
            throw Invalid("Sidecar flow nonce or message sequence is invalid.");
        }

        if (frame.Container.Length > LanConnectTailCodec.MaxContainerBytes)
        {
            throw Invalid("Sidecar inner container exceeds 256 KiB.");
        }

        _ = LanConnectTailCodec.Decode(frame.Container.Span);
    }

    private static InvalidDataException Invalid(string message, Exception? inner = null) => new(message, inner);
}
