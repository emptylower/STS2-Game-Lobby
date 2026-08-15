using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectSidecarPairingCacheTests
{
    [Fact]
    public void Sidecar_frame_codec_round_trips_exact_inner_container()
    {
        byte[] container = ReadFixture("tail-envelope-capabilities-v1.bin");
        LanConnectSidecarFrame frame = Frame(1, LanConnectSidecarMessageKind.InitialGameInfo, container);

        LanConnectSidecarFrame decoded = LanConnectSidecarFrameCodec.Decode(
            LanConnectSidecarFrameCodec.Encode(frame));

        Assert.Equal(frame.MessageKind, decoded.MessageKind);
        Assert.Equal(frame.MessageSequence, decoded.MessageSequence);
        Assert.Equal(container, decoded.Container.ToArray());
    }

    [Fact]
    public void Releases_handler_only_after_frame_and_vanilla_pair_in_either_cross_stream_order()
    {
        byte[] nonce = new byte[16];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        LanConnectSidecarPairingCache frameFirst = Bound(nonce);
        Assert.Null(frameFirst.SubmitFrame(1, 2, Frame(1), now));
        LanConnectPairedSidecarMessage paired = Assert.IsType<LanConnectPairedSidecarMessage>(
            frameFirst.SubmitVanilla(1, 2, nonce, LanConnectSidecarMessageKind.InitialGameInfo, new object(), now));
        Assert.Equal(1u, paired.Sequence);

        LanConnectSidecarPairingCache vanillaFirst = Bound(nonce);
        Assert.Null(vanillaFirst.SubmitVanilla(
            1, 2, nonce, LanConnectSidecarMessageKind.InitialGameInfo, new object(), now));
        Assert.NotNull(vanillaFirst.SubmitFrame(1, 2, Frame(1), now));
    }

    [Fact]
    public void Preserves_order_inside_each_stream_and_pairs_by_next_ordinal_not_kind()
    {
        byte[] nonce = new byte[16];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        LanConnectSidecarPairingCache cache = Bound(nonce);
        Assert.Null(cache.SubmitFrame(1, 2, Frame(1, LanConnectSidecarMessageKind.PlayerJoined), now));
        Assert.Null(cache.SubmitFrame(1, 2, Frame(2, LanConnectSidecarMessageKind.PlayerJoined), now));

        Assert.NotNull(cache.SubmitVanilla(
            1, 2, nonce, LanConnectSidecarMessageKind.PlayerJoined, "first", now));
        Assert.NotNull(cache.SubmitVanilla(
            1, 2, nonce, LanConnectSidecarMessageKind.PlayerJoined, "second", now));
        Assert.Throws<InvalidDataException>(() => cache.SubmitFrame(1, 2, Frame(2), now));
    }

    [Fact]
    public void Rejects_wrong_flow_kind_timeout_cache_overflow_and_sequence_exhaustion()
    {
        byte[] nonce = new byte[16];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        LanConnectSidecarPairingCache cache = Bound(nonce);
        Assert.Throws<InvalidDataException>(() => cache.SubmitFrame(9, 2, Frame(1), now));

        Assert.Null(cache.SubmitFrame(1, 2, Frame(1, LanConnectSidecarMessageKind.InitialGameInfo), now));
        Assert.Throws<InvalidDataException>(() => cache.SubmitVanilla(
            1, 2, nonce, LanConnectSidecarMessageKind.PlayerJoined, new object(), now));

        LanConnectSidecarPairingCache timed = Bound(nonce);
        Assert.Null(timed.SubmitFrame(1, 2, Frame(1), now));
        Assert.Throws<InvalidDataException>(() => timed.SubmitVanilla(
            1, 2, nonce, LanConnectSidecarMessageKind.InitialGameInfo, new object(), now.AddSeconds(6)));

        LanConnectSidecarPairingCache full = Bound(nonce);
        for (uint sequence = 1; sequence <= 16; sequence++)
        {
            Assert.Null(full.SubmitFrame(1, 2, Frame(sequence), now));
        }
        Assert.Throws<InvalidDataException>(() => full.SubmitFrame(1, 2, Frame(17), now));

        LanConnectSidecarPairingCache exhausted = new();
        exhausted.BindFlow(1, 2, nonce, uint.MaxValue);
        Assert.Null(exhausted.SubmitFrame(1, 2, Frame(uint.MaxValue), now));
        Assert.Throws<InvalidDataException>(() => exhausted.SubmitFrame(1, 2, Frame(uint.MaxValue), now));
    }

    [Fact]
    public void Teardown_clears_peer_bindings_so_reused_ids_start_unbound()
    {
        byte[] nonce = new byte[16];
        LanConnectSidecarPairingCache cache = Bound(nonce);
        cache.ClearPeer(1);

        Assert.Throws<InvalidDataException>(() =>
            cache.SubmitFrame(1, 2, Frame(1), DateTimeOffset.UtcNow));
    }

    private static LanConnectSidecarPairingCache Bound(byte[] nonce)
    {
        LanConnectSidecarPairingCache cache = new();
        cache.BindFlow(1, 2, nonce);
        return cache;
    }

    private static LanConnectSidecarFrame Frame(
        uint sequence,
        LanConnectSidecarMessageKind kind = LanConnectSidecarMessageKind.InitialGameInfo,
        byte[]? container = null) => new(
            kind,
            new byte[16],
            sequence,
            container ?? ReadFixture("tail-envelope-capabilities-v1.bin"));

    private static byte[] ReadFixture(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
        {
            directory = directory.Parent;
        }

        return File.ReadAllBytes(Path.Combine(
            directory?.FullName ?? throw new DirectoryNotFoundException(),
            "test-fixtures", "protocol", "v0.6", name));
    }
}
