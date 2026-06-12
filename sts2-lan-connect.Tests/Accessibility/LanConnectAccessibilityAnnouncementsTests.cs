using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Accessibility;

public sealed class LanConnectAccessibilityAnnouncementsTests
{
    [Fact]
    public void BuildRoomCardAnnouncement_includes_core_room_state_without_password()
    {
        string announcement = LanConnectAccessibilityAnnouncements.BuildRoomCardAnnouncement(
            roomName: "盲测房间",
            hostName: "房主A",
            currentPlayers: 2,
            maxPlayers: 4,
            requiresPassword: true,
            canJoin: false,
            joinDisabledReason: "房间正在开始",
            isSelected: true,
            gameModeLabel: "标准模式");

        Assert.Contains("已选中", announcement);
        Assert.Contains("盲测房间", announcement);
        Assert.Contains("房主A", announcement);
        Assert.Contains("2/4", announcement);
        Assert.Contains("需要密码", announcement);
        Assert.Contains("房间正在开始", announcement);
        Assert.DoesNotContain("密码是", announcement);
    }
}
