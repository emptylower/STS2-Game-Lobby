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

    [TestCase]
    public void Too_old_inferred_server_renders_service_too_old_badge_and_version_tooltip()
    {
        LanConnectServerSelectionDialog dialog = AutoFree(new LanConnectServerSelectionDialog())!;
        Control row = AutoFree(dialog.BuildServerRowForTests(new ServerListEntry
        {
            Address = "http://103.39.66.147:8787",
            Version = new ServerVersionInfo(ServerVersionSource.Inferred, 0, 5, "0.5.x（推断）")
        }))!;

        Label badge = Find<Label>(row, "ServiceTooOldBadge");
        AssertThat(badge.Text).IsEqual("服务端版本过旧");
        AssertThat(row.TooltipText ?? "").Contains("服务端版本：0.5.x（推断）");
        AssertThat(row.TooltipText ?? "").Contains("该服务器的 lobby-service 低于 0.6.0");
    }

    [TestCase]
    public void Current_and_unknown_servers_do_not_render_service_too_old_badge()
    {
        LanConnectServerSelectionDialog dialog = AutoFree(new LanConnectServerSelectionDialog())!;

        Control currentRow = AutoFree(dialog.BuildServerRowForTests(new ServerListEntry
        {
            Address = "http://47.97.126.98:8787",
            Version = new ServerVersionInfo(ServerVersionSource.Reported, 0, 6, "0.6.1")
        }))!;
        AssertThat(currentRow.FindChild("ServiceTooOldBadge", recursive: true, owned: false)).IsNull();
        AssertThat(currentRow.TooltipText ?? "").Contains("服务端版本：0.6.1");

        Control unknownRow = AutoFree(dialog.BuildServerRowForTests(new ServerListEntry
        {
            Address = "http://unknown.example"
        }))!;
        AssertThat(unknownRow.FindChild("ServiceTooOldBadge", recursive: true, owned: false)).IsNull();
        AssertThat(unknownRow.TooltipText ?? "").Contains("服务端版本：未知");
    }

    private static T Find<T>(Node root, string name) where T : Node =>
        (T)(root.FindChild(name, recursive: true, owned: false)
            ?? throw new InvalidOperationException($"Missing node {name}"));
}
