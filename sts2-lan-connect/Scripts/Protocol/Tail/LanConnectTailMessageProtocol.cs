namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectTailMessagePayload(
    LanConnectSidecarMessageKind MessageKind,
    LanConnectProtocolOffer? PeerOffer,
    LanConnectCapabilitiesSelection? SessionSelection,
    LanConnectRosterSnapshot? Roster,
    LanConnectProtocolFailure? Rejection);

internal static class LanConnectTailMessageProtocol
{
    internal static byte[] EncodePeerOffer(
        LanConnectSidecarMessageKind messageKind,
        LanConnectProtocolOffer offer)
    {
        if (!IsRequest(messageKind))
        {
            throw Invalid($"Message kind {messageKind} cannot carry a peer offer.");
        }

        LanConnectTailEntry capabilities = new(
            LanConnectTailEntry.CapabilitiesId,
            1,
            true,
            LanConnectCapabilitiesCodec.EncodePeerOffer(offer));
        return LanConnectTailCodec.Encode(0, [capabilities]);
    }

    internal static byte[] EncodeSession(
        LanConnectSidecarMessageKind messageKind,
        LanConnectProtocolSelection selection,
        LanConnectRosterSnapshot? roster = null,
        LanConnectProtocolFailure? rejection = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (IsRequest(messageKind))
        {
            throw Invalid($"Request kind {messageKind} must carry a peer offer, not a session selection.");
        }

        ValidateEntryMatrix(messageKind, roster, rejection);
        List<LanConnectTailEntry> entries =
        [
            new(
                LanConnectTailEntry.CapabilitiesId,
                1,
                true,
                LanConnectCapabilitiesCodec.EncodeSessionSelection(selection))
        ];
        if (roster != null)
        {
            entries.Add(new LanConnectTailEntry(
                LanConnectTailEntry.RosterId,
                1,
                true,
                LanConnectRosterCodec.Encode(roster)));
        }

        if (rejection != null)
        {
            entries.Add(new LanConnectTailEntry(
                LanConnectTailEntry.RejectionId,
                1,
                true,
                LanConnectRejectionCodec.Encode(rejection)));
        }

        return LanConnectTailCodec.Encode(checked((ushort)selection.SelectedLanProtocolVersion), entries);
    }

    internal static LanConnectTailMessagePayload DecodeAndValidate(
        LanConnectSidecarMessageKind messageKind,
        ReadOnlySpan<byte> container,
        LanConnectProtocolSelection? frozenSelection = null,
        ulong? transportSenderPeerId = null,
        ulong? currentHostPeerId = null)
    {
        LanConnectTailEnvelope envelope = LanConnectTailCodec.Decode(container);
        LanConnectTailEntry capabilities = RequireSingle(envelope, LanConnectTailEntry.CapabilitiesId);
        LanConnectTailEntry? rosterEntry = Find(envelope, LanConnectTailEntry.RosterId);
        LanConnectTailEntry? rejectionEntry = Find(envelope, LanConnectTailEntry.RejectionId);

        if (IsRequest(messageKind))
        {
            if (rosterEntry != null || rejectionEntry != null || envelope.SessionProtocolVersion != 0)
            {
                throw Invalid("Join requests permit only a session-version-zero peer offer.");
            }

            LanConnectProtocolOffer offer = LanConnectCapabilitiesCodec.DecodePeerOffer(
                capabilities.Payload.Span,
                envelope.SessionProtocolVersion);
            return new LanConnectTailMessagePayload(messageKind, offer, null, null, null);
        }

        if (frozenSelection == null)
        {
            throw Invalid("Session messages require a frozen protocol selection.");
        }

        LanConnectCapabilitiesSelection selection = LanConnectCapabilitiesCodec.DecodeSessionSelection(
            capabilities.Payload.Span,
            envelope.SessionProtocolVersion);
        LanConnectCapabilitiesCodec.ValidateMatches(selection, frozenSelection);
        LanConnectRosterSnapshot? roster = rosterEntry == null
            ? null
            : LanConnectRosterCodec.Decode(rosterEntry.Payload.Span);
        LanConnectProtocolFailure? rejection = rejectionEntry == null
            ? null
            : LanConnectRejectionCodec.Decode(rejectionEntry.Payload.Span);
        ValidateEntryMatrix(messageKind, roster, rejection);
        if (roster != null)
        {
            if (!transportSenderPeerId.HasValue || !currentHostPeerId.HasValue)
            {
                throw Invalid("Roster messages require transport-sender and current-host authority inputs.");
            }

            LanConnectRosterCodec.ValidateAuthority(roster, transportSenderPeerId.Value, currentHostPeerId.Value);
        }

        return new LanConnectTailMessagePayload(messageKind, null, selection, roster, rejection);
    }

    private static void ValidateEntryMatrix(
        LanConnectSidecarMessageKind messageKind,
        LanConnectRosterSnapshot? roster,
        LanConnectProtocolFailure? rejection)
    {
        bool requiresRoster = messageKind is
            LanConnectSidecarMessageKind.LobbyJoinResponse or
            LanConnectSidecarMessageKind.LoadJoinResponse or
            LanConnectSidecarMessageKind.RejoinResponse or
            LanConnectSidecarMessageKind.PlayerJoined or
            LanConnectSidecarMessageKind.LobbyBeginRun;
        bool requiresRejection = messageKind == LanConnectSidecarMessageKind.ConnectionFailed;
        if ((roster != null) != requiresRoster
            || (rejection != null) != requiresRejection
            || (roster != null && rejection != null))
        {
            throw Invalid($"Message kind {messageKind} has an invalid roster/rejection entry combination.");
        }
    }

    private static bool IsRequest(LanConnectSidecarMessageKind kind) => kind is
        LanConnectSidecarMessageKind.LobbyJoinRequest or
        LanConnectSidecarMessageKind.LoadJoinRequest or
        LanConnectSidecarMessageKind.RejoinRequest;

    private static LanConnectTailEntry RequireSingle(LanConnectTailEnvelope envelope, string id) =>
        Find(envelope, id) ?? throw Invalid($"Required entry '{id}' is missing.");

    private static LanConnectTailEntry? Find(LanConnectTailEnvelope envelope, string id) =>
        envelope.Entries.SingleOrDefault(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));

    private static InvalidDataException Invalid(string message) => new(message);
}
