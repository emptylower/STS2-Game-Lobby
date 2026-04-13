using System;
using System.Text;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectCompatibilityMatrix
{
    public static string DescribeCurrentPolicy()
    {
        int effectiveMaxPlayers = LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers();
        string compatibilityProfile = LanConnectLobbyEndpointDefaults.GetCompatibilityProfile();
        string connectionStrategy = LanConnectLobbyEndpointDefaults.GetConnectionStrategy();
        string protocolProfile = LanConnectProtocolProfiles.DetermineProfileForMaxPlayers(effectiveMaxPlayers);

        return $"compatibilityProfile={compatibilityProfile}, connectionStrategy={connectionStrategy}, effectiveMaxPlayers={effectiveMaxPlayers}, publishedProtocolProfile={protocolProfile}";
    }

    public static string BuildHumanSummary()
    {
        StringBuilder builder = new();
        builder.Append($"profile={LanConnectLobbyEndpointDefaults.GetCompatibilityProfile()}");
        builder.Append($", strategy={LanConnectLobbyEndpointDefaults.GetConnectionStrategy()}");
        builder.Append($", effectiveMaxPlayers={LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers()}");
        builder.Append($", 4pProtocol={LanConnectProtocolProfiles.Legacy4p}");
        builder.Append($", 5-8pProtocol={LanConnectProtocolProfiles.Extended8p}");
        return builder.ToString();
    }

    public static string DescribeRoomCompatibility(LobbyRoomSummary room)
    {
        string roomProtocol = LanConnectProtocolProfiles.Normalize(room.ProtocolProfile);
        string expectedProtocol = room.MaxPlayers <= LanConnectConstants.LegacyMatrixMaxPlayers
            ? LanConnectProtocolProfiles.Legacy4p
            : LanConnectProtocolProfiles.Extended8p;
        bool protocolAligned = string.Equals(roomProtocol, expectedProtocol, StringComparison.Ordinal);

        return $"roomId={room.RoomId}, maxPlayers={room.MaxPlayers}, roomProtocol={roomProtocol}, expectedProtocol={expectedProtocol}, protocolAligned={protocolAligned}, relayState={room.RelayState}, version={room.Version}, modVersion={room.ModVersion}";
    }

    public static string DescribeJoinFailureCode(string code, string fallbackMessage)
    {
        return code switch
        {
            "room_started" => "房间已经开始游戏，不能再加入。",
            "room_full" => "房间已经满员。",
            "room_closed" => "房间已经关闭。",
            "relay_host_not_ready" => "房主 relay 尚未注册完成，请稍后刷新后再试。",
            "invalid_password" => "房间密码错误。",
            "save_slot_required" => "这是续局房间，需要先选择一个可接管角色。",
            "save_slot_invalid" => "所选续局角色不存在。",
            "save_slot_unavailable" => "所选续局角色已被其他玩家接管，或当前没有可接管角色。",
            _ => fallbackMessage
        };
    }
}
