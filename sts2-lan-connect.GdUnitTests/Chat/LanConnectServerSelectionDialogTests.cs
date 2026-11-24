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

    [TestCase]
    public async Task Blocked_name_keeps_picker_editable_and_retry_uses_corrected_name()
    {
        string originalName = LanConnectConfig.PlayerDisplayName;
        try
        {
            LanConnectConfig.PlayerDisplayName = "Before";
            LanConnectServerSelectionDialog dialog = new();
            using ISceneRunner runner = ISceneRunner.Load(dialog, autoFree: true);
            await runner.AwaitIdleFrame();
            LineEdit name = Find<LineEdit>(dialog, "PlayerDisplayNameInput");
            AssertThat(name.Text).IsEqual("Before");
            AssertThat(name.MaxLength).IsEqual(LanConnectConfig.MaxPlayerDisplayNameLength);
            List<string> attemptedNames = [];
            int picked = 0;
            int settled = 0;
            Task attempt = Task.CompletedTask;
            dialog.ServerChosen += address => attempt = LanConnectServerSelectionStartup.HandleServerPickedAsync(
                dialog,
                address,
                _ => { picked++; return Task.CompletedTask; },
                () => settled++,
                switchServerForTests: _ =>
                {
                    attemptedNames.Add(LanConnectConfig.GetEffectivePlayerDisplayName());
                    return attemptedNames.Count == 1
                        ? Task.FromException(new LobbyServiceException("arbitrary backend copy", "content_blocked", 400))
                        : Task.CompletedTask;
                });
            Button server = (Button)AutoFree(dialog.BuildServerRowForTests(new ServerListEntry
            {
                Address = "https://nickname.example"
            }))!;

            name.Text = "Blocked";
            server.EmitSignal(Button.SignalName.Pressed);
            await attempt;
            await runner.AwaitIdleFrame();

            AssertThat(dialog.Visible).IsTrue();
            AssertThat(dialog.IsQueuedForDeletion()).IsFalse();
            AssertThat(Find<Label>(dialog, "PlayerNameErrorLabel").Visible).IsTrue();
            AssertThat(name.Text).IsEqual("Blocked");
            AssertThat(name.HasFocus()).IsTrue();
            AssertThat(picked).IsEqual(0);
            AssertThat(settled).IsEqual(0);

            name.Text = "  Safe  ";
            server.EmitSignal(Button.SignalName.Pressed);
            await attempt;

            AssertThat(attemptedNames).ContainsExactly("Blocked", "Safe");
            AssertThat(LanConnectConfig.PlayerDisplayName).IsEqual("Safe");
            AssertThat(picked).IsEqual(1);
            AssertThat(settled).IsEqual(1);
            AssertThat(dialog.IsQueuedForDeletion()).IsTrue();
        }
        finally
        {
            LanConnectConfig.PlayerDisplayName = originalName;
        }
    }

    [TestCase]
    public async Task Escape_persists_focused_name_before_cancellation_callback()
    {
        string originalName = LanConnectConfig.PlayerDisplayName;
        try
        {
            LanConnectServerSelectionDialog dialog = new();
            using ISceneRunner runner = ISceneRunner.Load(dialog, autoFree: true);
            await runner.AwaitIdleFrame();
            LineEdit name = Find<LineEdit>(dialog, "PlayerDisplayNameInput");
            name.GrabFocus();
            name.Text = "  Safe  ";
            string? nameOnCancel = null;
            dialog.Cancelled += () => nameOnCancel = LanConnectConfig.PlayerDisplayName;

            dialog._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });

            AssertThat(nameOnCancel).IsEqual("Safe");
            AssertThat(dialog.IsQueuedForDeletion()).IsTrue();
        }
        finally
        {
            LanConnectConfig.PlayerDisplayName = originalName;
        }
    }

    [TestCase]
    public async Task Non_moderation_failure_closes_picker_and_settles()
    {
        LanConnectServerSelectionDialog dialog = new();
        using ISceneRunner runner = ISceneRunner.Load(dialog, autoFree: true);
        await runner.AwaitIdleFrame();
        int picked = 0;
        int settled = 0;
        string? failure = null;

        await LanConnectServerSelectionStartup.HandleServerPickedAsync(
            dialog,
            "https://nickname.example",
            _ => { picked++; return Task.CompletedTask; },
            () => settled++,
            switchServerForTests: _ => Task.FromException(new LobbyServiceException("unavailable", "http_error", 503)),
            showFailureForTests: message => failure = message);

        AssertThat(picked).IsEqual(0);
        AssertThat(settled).IsEqual(1);
        AssertThat(dialog.IsQueuedForDeletion()).IsTrue();
        AssertThat(failure ?? string.Empty).Contains("unavailable");
    }

    private static T Find<T>(Node root, string name) where T : Node =>
        (T)(root.FindChild(name, recursive: true, owned: false)
            ?? throw new InvalidOperationException($"Missing node {name}"));
}
