using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectClientAssemblySmokeTests
{
    [TestCase]
    public void Loads_real_chat_overlay_from_client_assembly()
    {
        LanConnectRoomChatOverlay overlay = AutoFree(new LanConnectRoomChatOverlay())!;
        AssertThat(overlay.GetType().Assembly.GetName().Name).IsEqual("sts2_lan_connect");
    }
}
