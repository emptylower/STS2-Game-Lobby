using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLobbyJoinResponseValidationTests
{
    private const string ValidNonce = "00112233445566778899aabbccddeeff";

    private static LobbyJoinRoomResponse CreateResponse(int? hostNativeBusTypeId, string? nonce = ValidNonce) => new()
    {
        ProtocolFlowNonce = nonce,
        HostNativeBusTypeId = hostNativeBusTypeId
    };

    [Fact]
    public void Compat_join_response_without_host_native_bus_type_id_passes_validation()
    {
        LobbyJoinRoomResponse response = CreateResponse(hostNativeBusTypeId: null);

        response.ValidateProtocolFields(LanConnectProtocolProfile.Compat4x5V1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(256)]
    [InlineData(-1)]
    public void Tail_join_response_with_missing_or_out_of_range_host_native_bus_type_id_is_rejected(
        int? hostNativeBusTypeId)
    {
        LobbyJoinRoomResponse response = CreateResponse(hostNativeBusTypeId);

        LanConnectProtocolException exception = Assert.Throws<LanConnectProtocolException>(
            () => response.ValidateProtocolFields(LanConnectProtocolProfile.TailV1));

        Assert.Equal("lan_type_id_mismatch", exception.Failure.Code);
    }

    [Fact]
    public void Tail_join_response_with_in_range_host_native_bus_type_id_passes_validation()
    {
        LobbyJoinRoomResponse response = CreateResponse(hostNativeBusTypeId: 201);

        response.ValidateProtocolFields(LanConnectProtocolProfile.TailV1);
    }

    [Fact]
    public void Compat_join_response_still_requires_a_valid_protocol_flow_nonce()
    {
        LobbyJoinRoomResponse response = CreateResponse(hostNativeBusTypeId: null, nonce: "NOT-A-NONCE");

        LanConnectProtocolException exception = Assert.Throws<LanConnectProtocolException>(
            () => response.ValidateProtocolFields(LanConnectProtocolProfile.Compat4x5V1));

        Assert.Equal("lan_protocol_version_mismatch", exception.Failure.Code);
    }
}
