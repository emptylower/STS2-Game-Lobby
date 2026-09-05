using Sts2LanConnect.Scripts;
using System.Text.Json;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectProtocolFailurePropagationTests
{
    [Fact]
    public async Task Mod_preflight_preserves_protocol_service_failure()
    {
        LanConnectProtocolFailure expected = LanConnectProtocolFailure.RitsuLibPresenceMismatch(true);
        LanConnectModPreflightCoordinator coordinator = new(new ProtocolFailurePorts(expected));
        LanConnectModPreflightJoinRequest request = new()
        {
            Room = new LobbyRoomSummary { RoomId = "room-a", Version = "0.110.1" },
            PlayerName = "player",
            ClientInstallationId = "install-a",
            GameVersion = "0.110.1",
            ModVersion = "0.6.0-alpha.1",
            ProtocolOffer = new LanConnectProtocolOffer(1, 1, "0.6.0-alpha.1", false, false)
        };

        LanConnectModPreflightJoinResult result = await coordinator.JoinAsync(request);

        Assert.Equal(LanConnectModPreflightJoinOutcome.Canceled, result.Outcome);
        Assert.NotNull(result.ProtocolFailure);
        Assert.Equal(expected.Code, result.ProtocolFailure.Code);
        Assert.Equal(expected.RequiredRitsuLibPresent, result.ProtocolFailure.RequiredRitsuLibPresent);
    }

    [Theory]
    [InlineData(11, "lan_legacy_carrier_unsupported")]
    [InlineData(12, "lan_registry_fingerprint_required")]
    [InlineData(13, "lan_registry_fingerprint_mismatch")]
    [InlineData(14, "lan_native_frame_invalid")]
    [InlineData(15, "lan_client_version_too_old")]
    [InlineData(16, "lan_type_id_mismatch")]
    [InlineData(17, "lan_extension_missing")]
    public void Native_bus_tail_rejection_codes_map_to_canonical_codes(int reasonCode, string expected)
    {
        Assert.Equal(expected, LanConnectProtocolFailureMapper.FromTail(reasonCode).Code);
    }

    [Fact]
    public void Unknown_tail_rejection_retains_raw_reason_code()
    {
        LanConnectProtocolFailure failure = LanConnectProtocolFailureMapper.FromTail(18);

        Assert.Equal("unknown_tail_rejection_18", failure.Code);
    }

    [Theory]
    [InlineData("00112233445566778899aabbccddeeff", true)]
    [InlineData("00112233445566778899AABBCCDDEEFF", false)]
    [InlineData("0011", false)]
    public void Protocol_flow_nonce_requires_exact_lowercase_hex(string nonce, bool valid)
    {
        LobbyJoinRoomResponse response = new() { ProtocolFlowNonce = nonce };

        if (valid)
        {
            Assert.Equal(16, response.GetProtocolFlowNonceBytes().Length);
        }
        else
        {
            Assert.Throws<LanConnectProtocolException>(response.GetProtocolFlowNonceBytes);
        }
    }

    [Fact]
    public void Protocol_dtos_use_the_exact_cross_runtime_field_names()
    {
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.1", false, false);
        LobbyProtocolSelectionDto selection = LobbyProtocolSelectionDto.FromValue(
            LanConnectProtocolSelection.CreateLocalCompat(8, "0.110.1", "aabb"));

        using JsonDocument offerJson = JsonDocument.Parse(JsonSerializer.Serialize(
            LobbyProtocolOfferDto.FromValue(offer),
            LanConnectJson.Options));
        using JsonDocument selectionJson = JsonDocument.Parse(JsonSerializer.Serialize(
            selection,
            LanConnectJson.Options));

        Assert.Equal("0.6.0-alpha.1", offerJson.RootElement.GetProperty("clientVersion").GetString());
        Assert.Equal("aabb", selectionJson.RootElement.GetProperty("wireCacheSignature").GetString());
        Assert.False(selectionJson.RootElement.TryGetProperty("wireCacheSignatureV1", out _));
    }

    private sealed class ProtocolFailurePorts : ILanConnectModPreflightPorts
    {
        private readonly LanConnectProtocolFailure _failure;

        public ProtocolFailurePorts(LanConnectProtocolFailure failure)
        {
            _failure = failure;
        }

        public Task<LobbyProbeResponse> GetProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new LobbyProbeResponse());

        public Task<LobbyModPreflightResponse> PreflightAsync(
            string roomId,
            LobbyModPreflightRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LanConnectModPreflightDecision> DecideAsync(
            LobbyRoomSummary room,
            LobbyModPreflightResponse response,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LobbyJoinRoomResponse> RequestJoinTicketAsync(
            string roomId,
            LobbyJoinRoomRequest request,
            CancellationToken cancellationToken) =>
            Task.FromException<LobbyJoinRoomResponse>(new LobbyServiceException(
                "protocol rejected",
                _failure.Code,
                409,
                new LobbyErrorDetails
                {
                    RequiredClientVersion = _failure.RequiredClientVersion,
                    RequiredRitsuLibPresent = _failure.RequiredRitsuLibPresent,
                    Detail = _failure.Detail
                }));
    }
}
