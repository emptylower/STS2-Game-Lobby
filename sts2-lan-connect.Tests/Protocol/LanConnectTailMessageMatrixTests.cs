using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectTailMessageMatrixTests
{
    [Theory]
    [InlineData((int)LanConnectSidecarMessageKind.LobbyJoinRequest)]
    [InlineData((int)LanConnectSidecarMessageKind.LoadJoinRequest)]
    [InlineData((int)LanConnectSidecarMessageKind.RejoinRequest)]
    public void Requests_carry_only_a_session_zero_peer_offer(int kindValue)
    {
        LanConnectSidecarMessageKind kind = (LanConnectSidecarMessageKind)kindValue;
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.1", false, false);
        byte[] container = LanConnectTailMessageProtocol.EncodePeerOffer(kind, offer);
        LanConnectTailMessagePayload decoded = LanConnectTailMessageProtocol.DecodeAndValidate(kind, container);

        Assert.Equal(offer, decoded.PeerOffer);
        Assert.Null(decoded.SessionSelection);
        Assert.Null(decoded.Roster);
        Assert.Null(decoded.Rejection);
    }

    [Theory]
    [InlineData((int)LanConnectSidecarMessageKind.LobbyJoinResponse)]
    [InlineData((int)LanConnectSidecarMessageKind.LoadJoinResponse)]
    [InlineData((int)LanConnectSidecarMessageKind.RejoinResponse)]
    [InlineData((int)LanConnectSidecarMessageKind.PlayerJoined)]
    [InlineData((int)LanConnectSidecarMessageKind.LobbyBeginRun)]
    public void Success_and_mutation_messages_require_selection_plus_authoritative_roster(
        int kindValue)
    {
        LanConnectSidecarMessageKind kind = (LanConnectSidecarMessageKind)kindValue;
        LanConnectProtocolSelection selection = TailSelection();
        LanConnectRosterSnapshot roster = Roster();
        byte[] container = LanConnectTailMessageProtocol.EncodeSession(kind, selection, roster);
        LanConnectTailMessagePayload decoded = LanConnectTailMessageProtocol.DecodeAndValidate(
            kind, container, selection, 100, 100);

        Assert.NotNull(decoded.SessionSelection);
        Assert.Equal(roster.RosterRevision, decoded.Roster!.RosterRevision);
        Assert.Null(decoded.Rejection);
    }

    [Fact]
    public void Initial_info_requires_selection_only_and_failure_requires_rejection_only()
    {
        LanConnectProtocolSelection selection = TailSelection();
        byte[] initial = LanConnectTailMessageProtocol.EncodeSession(
            LanConnectSidecarMessageKind.InitialGameInfo,
            selection);
        Assert.NotNull(LanConnectTailMessageProtocol.DecodeAndValidate(
            LanConnectSidecarMessageKind.InitialGameInfo, initial, selection).SessionSelection);

        LanConnectProtocolFailure failure = LanConnectProtocolFailure.RitsuLibPresenceMismatch(false);
        byte[] rejection = LanConnectTailMessageProtocol.EncodeSession(
            LanConnectSidecarMessageKind.ConnectionFailed,
            selection,
            rejection: failure);
        Assert.Equal(failure, LanConnectTailMessageProtocol.DecodeAndValidate(
            LanConnectSidecarMessageKind.ConnectionFailed, rejection, selection).Rejection);
    }

    [Fact]
    public void Matrix_rejects_forbidden_entries_wrong_authority_and_selection_drift()
    {
        LanConnectProtocolSelection selection = TailSelection();
        Assert.Throws<InvalidDataException>(() => LanConnectTailMessageProtocol.EncodeSession(
            LanConnectSidecarMessageKind.LobbyJoinResponse,
            selection));
        Assert.Throws<InvalidDataException>(() => LanConnectTailMessageProtocol.EncodeSession(
            LanConnectSidecarMessageKind.InitialGameInfo,
            selection,
            Roster()));

        byte[] response = LanConnectTailMessageProtocol.EncodeSession(
            LanConnectSidecarMessageKind.LobbyJoinResponse, selection, Roster());
        Assert.Throws<InvalidDataException>(() => LanConnectTailMessageProtocol.DecodeAndValidate(
            LanConnectSidecarMessageKind.LobbyJoinResponse, response, selection, 200, 100));

        LanConnectProtocolSelection drift = selection with
        {
            Carrier = LanConnectProtocolCarrier.RitsuLibSidecarV1,
            RitsuLibPresent = true
        };
        Assert.Throws<InvalidDataException>(() => LanConnectTailMessageProtocol.DecodeAndValidate(
            LanConnectSidecarMessageKind.LobbyJoinResponse, response, drift, 100, 100));
    }

    private static LanConnectRosterSnapshot Roster() => new(
        100,
        1,
        [new(11, 0, 3, [0x05]), new(22, 7, 8, [0xaa])]);

    private static LanConnectProtocolSelection TailSelection()
    {
        LanConnectProtocolSelection selection = new(
            LanConnectProtocolProfile.TailV1,
            1,
            LanConnectProtocolCarrier.StandaloneTailV1,
            "0.6.0-alpha.1",
            8,
            "0.110.1",
            null,
            false,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }
}
