using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectServerSelectionDialogTests
{
    [TestCase]
    public void Featured_mod_sync_server_renders_pin_and_capability_badges()
    {
        LanConnectServerSelectionDialog dialog = AutoFree(new LanConnectServerSelectionDialog())!;
        Control row = AutoFree(dialog.BuildServerRowForTests(new ServerListEntry
        {
            Address = LanConnectServerListBootstrap.FeaturedServerAddress,
            DisplayName = "测试节点",
            IsPinned = true,
            SupportsModSyncV051Plus = true,
            PingMs = 64,
            Rooms = 2
        }))!;

        Label pin = Find<Label>(row, "PinnedServerBadge");
        Label capability = Find<Label>(row, "ModSyncSupportBadge");
        AssertThat(pin.Text).IsEqual("置顶测试服");
        AssertThat(capability.Text).IsEqual("支持 0.5.1+ MOD 同步");
        AssertThat(row.CustomMinimumSize.Y).IsGreaterEqual(88f);
    }

    [TestCase]
    public void Legacy_server_does_not_render_mod_sync_capability_badge()
    {
        LanConnectServerSelectionDialog dialog = AutoFree(new LanConnectServerSelectionDialog())!;
        Control row = AutoFree(dialog.BuildServerRowForTests(new ServerListEntry
        {
            Address = "https://legacy.example",
            SupportsModSyncV051Plus = false
        }))!;

        AssertThat(row.FindChild("ModSyncSupportBadge", recursive: true, owned: false)).IsNull();
        AssertThat(row.FindChild("PinnedServerBadge", recursive: true, owned: false)).IsNull();
    }

    private static T Find<T>(Node root, string name) where T : Node =>
        (T)(root.FindChild(name, recursive: true, owned: false)
            ?? throw new InvalidOperationException($"Missing node {name}"));
}
