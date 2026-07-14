using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectLobbyServerPanelTests
{
    private const string BundledDefaultServer = "https://bundled-default.example";

    [TestCase]
    public async Task Desktop_sidebar_is_room_details_above_fixed_server_chat()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);
        LanConnectLobbyOverlayTestState state = fixture.Overlay.TestState;

        float mainShare = state.RoomStageRect.Size.X /
                          (state.RoomStageRect.Size.X + state.SidebarRect.Size.X);
        AssertThat(mainShare).IsBetween(0.73f, 0.77f);
        AssertThat(state.RoomDetailRect.End.Y).IsLessEqual(state.ServerChatRect.Position.Y);
        AssertThat(state.ServerPanelVisible).IsTrue();
        AssertThat(state.CompactSidebarScrollVisible).IsFalse();
        AssertThat(state.RoomDetailMinimumHeight).IsGreater(0f);
        AssertThat(state.ServerChatMinimumHeight).IsGreater(0f);
    }

    [TestCase]
    public async Task Unsupported_server_chat_hides_panel_and_expands_details()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Unsupported);
        LanConnectLobbyOverlayTestState state = fixture.Overlay.TestState;

        AssertThat(state.ServerPanelVisible).IsFalse();
        AssertThat(state.RoomDetailRect.End.Y).IsEqualApprox(state.SidebarRect.End.Y, 2f);
    }

    [TestCase]
    public async Task Compact_sidebar_scrolls_with_details_before_chat_and_bounded_heights()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(960, 720),
            LanConnectServerChatPresentation.Ready);
        LanConnectLobbyOverlayTestState state = fixture.Overlay.TestState;

        AssertThat(state.CompactSidebarScrollVisible).IsTrue();
        AssertThat(state.RoomDetailRect.End.Y).IsLessEqual(state.ServerChatRect.Position.Y);
        AssertThat(state.RoomDetailMinimumHeight).IsGreater(0f);
        AssertThat(state.ServerChatMinimumHeight).IsGreater(0f);
    }

    [TestCase]
    public async Task Hd_window_uses_compact_sidebar_scroll_and_keeps_viewports_inside_720p()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1280, 720),
            LanConnectServerChatPresentation.Ready);
        LanConnectLobbyOverlayTestState state = fixture.Overlay.TestState;

        AssertThat(state.CompactSidebarScrollVisible).IsTrue();
        AssertThat(state.RoomStageRect.Position.Y).IsGreaterEqual(0f);
        AssertThat(state.RoomStageRect.End.Y).IsLessEqual(722f);
        AssertThat(state.CompactSidebarViewportRect.Position.Y).IsGreaterEqual(0f);
        AssertThat(state.CompactSidebarViewportRect.End.Y).IsLessEqual(722f);
        AssertThat(state.RoomDetailRect.End.Y).IsLessEqual(state.ServerChatRect.Position.Y);
        AssertThat(state.RoomDetailMinimumHeight).IsGreater(0f);
        AssertThat(state.ServerChatMinimumHeight).IsGreater(0f);
    }

    [TestCase]
    public async Task Selection_refreshes_details_without_rebuilding_server_chat_panel()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);
        GodotObject panelBefore = fixture.Overlay.ServerChatPanelForTests;

        fixture.Overlay.SelectRoomForTests("room-b");
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.TestState.SelectedRoomId).IsEqual("room-b");
        AssertThat(fixture.Overlay.TestState.SelectedRoomName).IsEqual("Beta Room");
        AssertThat(ReferenceEquals(panelBefore, fixture.Overlay.ServerChatPanelForTests)).IsTrue();
    }

    [TestCase]
    public async Task Visible_home_channel_consumes_incoming_messages_without_unread()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);

        fixture.ServerState.AppendConfirmedForTests("message", "A", "hello", 1, false);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.ServerState.UnreadCount).IsEqual(0);
        AssertThat(fixture.Overlay.ServerChatPanelForTests.TestState.MessageCount).IsEqual(1);
    }

    [TestCase]
    public async Task Server_context_rotation_rebinds_the_existing_panel_to_the_new_state()
    {
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready);
        LanConnectBasicChatPanel panel = fixture.Overlay.ServerChatPanelForTests;
        LanConnectChatChannelState replacement = ReadyState("replacement");
        replacement.AppendConfirmedForTests("new-server-message", "B", "new server", 1, false);

        fixture.Overlay.RebindServerChatForTests(replacement);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(ReferenceEquals(panel, fixture.Overlay.ServerChatPanelForTests)).IsTrue();
        AssertThat(ReferenceEquals(panel.State, replacement)).IsTrue();
        AssertThat(panel.TestState.MessageCount).IsEqual(1);
        AssertThat(fixture.ServerState.IsVisible).IsFalse();
        AssertThat(replacement.IsVisible).IsTrue();
    }

    [TestCase]
    public async Task Server_override_requires_explicit_coordinated_apply()
    {
        List<string> switches = [];
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready,
            server =>
            {
                switches.Add(server);
                return Task.FromResult(CancellationToken.None);
            },
            BundledDefaultServer);

        string requestedServer = string.Equals(
            LanConnectConfig.LobbyServerBaseUrlOverride,
            "https://phase2-gate.example",
            StringComparison.Ordinal)
            ? "https://phase2-gate-alt.example"
            : "https://phase2-gate.example";
        fixture.Overlay.SetServerOverrideDraftForTests(requestedServer);
        AssertThat(fixture.Overlay.ServerOverrideApplyEnabledForTests).IsTrue();
        fixture.Overlay.PersistSettingsForTests();
        AssertThat(switches.Count).IsEqual(0);

        await fixture.Overlay.ClearNetworkOverridesForTests();
        AssertThat(switches.Count).IsEqual(0);

        fixture.Overlay.SetServerOverrideDraftForTests(requestedServer);

        await fixture.Overlay.ApplyServerOverrideForTests();

        AssertThat(switches.Count).IsEqual(1);
        AssertThat(switches[0]).IsEqual(requestedServer);

        await fixture.Overlay.ClearNetworkOverridesForTests();

        AssertThat(switches.Count).IsEqual(2);
        AssertThat(switches[1]).IsEqual(BundledDefaultServer);
    }

    [TestCase]
    public async Task Server_switch_disables_all_switch_entrypoints_while_in_flight()
    {
        TaskCompletionSource<CancellationToken> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready,
            _ => release.Task,
            BundledDefaultServer);
        fixture.Overlay.SetServerOverrideDraftForTests("https://busy.example");

        Task apply = fixture.Overlay.ApplyServerOverrideForTests();
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(fixture.Overlay.ServerOverrideApplyEnabledForTests).IsFalse();
        AssertThat(fixture.Overlay.DirectoryServerButtonEnabledForTests).IsFalse();

        release.SetResult(CancellationToken.None);
        await apply;

        AssertThat(fixture.Overlay.DirectoryServerButtonEnabledForTests).IsTrue();
    }

    [TestCase]
    public async Task Superseded_switch_does_not_report_success_or_apply_the_override()
    {
        using CancellationTokenSource superseded = new();
        superseded.Cancel();
        int switches = 0;
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready,
            _ =>
            {
                switches++;
                return Task.FromResult(superseded.Token);
            },
            BundledDefaultServer);
        fixture.Overlay.SetServerOverrideDraftForTests("https://superseded.example");

        await fixture.Overlay.ApplyServerOverrideForTests();

        AssertThat(fixture.Overlay.LastStatusMessageForTests).Contains("取消");
        AssertThat(fixture.Overlay.ServerOverrideApplyEnabledForTests).IsTrue();
        await fixture.Overlay.ClearNetworkOverridesForTests();
        AssertThat(switches).IsEqual(1);
    }

    [TestCase]
    public async Task Persisted_then_failed_switch_remains_retryable()
    {
        const string requestedServer = "https://retry.example";
        int switches = 0;
        LobbyOverlayFixture? fixture = null;
        fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready,
            _ =>
            {
                switches++;
                if (switches == 1)
                {
                    fixture!.Overlay.SetPersistedServerOverrideForTests(requestedServer);
                    throw new InvalidOperationException("connect failed");
                }
                return Task.FromResult(CancellationToken.None);
            },
            BundledDefaultServer);
        using (fixture)
        {
            fixture.Overlay.SetServerOverrideDraftForTests(requestedServer);

            await fixture.Overlay.ApplyServerOverrideForTests();

            AssertThat(fixture.Overlay.ServerOverrideApplyEnabledForTests).IsTrue();
            AssertThat(fixture.Overlay.LastStatusMessageForTests).Contains("失败");

            await fixture.Overlay.ApplyServerOverrideForTests();

            AssertThat(switches).IsEqual(2);
            AssertThat(fixture.Overlay.LastStatusMessageForTests).Contains("已切换");
        }
    }

    [TestCase]
    public async Task Active_room_refresh_disables_every_server_switch_entrypoint()
    {
        int switches = 0;
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready,
            _ =>
            {
                switches++;
                return Task.FromResult(CancellationToken.None);
            },
            BundledDefaultServer);
        fixture.Overlay.SetServerOverrideDraftForTests("https://after-refresh.example");

        fixture.Overlay.SetRefreshInFlightForTests(true);

        AssertThat(fixture.Overlay.ServerOverrideApplyEnabledForTests).IsFalse();
        AssertThat(fixture.Overlay.ClearNetworkOverridesEnabledForTests).IsFalse();
        AssertThat(fixture.Overlay.DirectoryServerButtonEnabledForTests).IsFalse();
        await fixture.Overlay.ApplyServerOverrideForTests();
        await fixture.Overlay.ClearNetworkOverridesForTests();
        AssertThat(switches).IsEqual(0);

        fixture.Overlay.SetRefreshInFlightForTests(false);

        AssertThat(fixture.Overlay.ServerOverrideApplyEnabledForTests).IsTrue();
        AssertThat(fixture.Overlay.DirectoryServerButtonEnabledForTests).IsTrue();
    }

    [TestCase]
    public async Task Server_picker_is_serialized_and_cancel_restores_entries()
    {
        int launches = 0;
        Action? cancelPicker = null;
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready,
            switchServer: _ => Task.FromResult(CancellationToken.None),
            defaultServer: BundledDefaultServer,
            launchServerPicker: (_, onCancelled) =>
            {
                launches++;
                cancelPicker = onCancelled;
            });
        fixture.Overlay.SetServerOverrideDraftForTests("https://picker.example");

        fixture.Overlay.SetRefreshInFlightForTests(true);
        fixture.Overlay.OpenServerPickerForTests();
        AssertThat(launches).IsEqual(0);

        fixture.Overlay.SetRefreshInFlightForTests(false);
        fixture.Overlay.OpenServerPickerForTests();
        fixture.Overlay.OpenServerPickerForTests();

        AssertThat(launches).IsEqual(1);
        AssertThat(fixture.Overlay.ServerPickerOpenForTests).IsTrue();
        AssertThat(fixture.Overlay.RefreshButtonEnabledForTests).IsFalse();
        AssertThat(fixture.Overlay.ServerOverrideApplyEnabledForTests).IsFalse();
        AssertThat(fixture.Overlay.ClearNetworkOverridesEnabledForTests).IsFalse();
        AssertThat(fixture.Overlay.DirectoryServerButtonEnabledForTests).IsFalse();
        AssertThat(fixture.Overlay.CreateRoomButtonEnabledForTests).IsFalse();
        AssertThat(fixture.Overlay.JoinRoomButtonEnabledForTests).IsFalse();

        cancelPicker!();

        AssertThat(fixture.Overlay.ServerPickerOpenForTests).IsFalse();
        AssertThat(fixture.Overlay.RefreshButtonEnabledForTests).IsTrue();
        AssertThat(fixture.Overlay.DirectoryServerButtonEnabledForTests).IsTrue();
        AssertThat(fixture.Overlay.CreateRoomButtonEnabledForTests).IsTrue();
        AssertThat(fixture.Overlay.JoinRoomButtonEnabledForTests).IsTrue();
    }

    [TestCase]
    public async Task Successful_switch_with_failed_refresh_preserves_refresh_failure_status()
    {
        int switches = 0;
        int refreshes = 0;
        using LobbyOverlayFixture fixture = await LobbyOverlayFixture.Create(
            new Vector2I(1920, 1080),
            LanConnectServerChatPresentation.Ready,
            switchServer: _ =>
            {
                switches++;
                return Task.FromResult(CancellationToken.None);
            },
            defaultServer: BundledDefaultServer,
            refreshServer: _ =>
            {
                refreshes++;
                return Task.FromResult(false);
            });
        fixture.Overlay.SetServerOverrideDraftForTests("https://refresh-fails.example");

        await fixture.Overlay.ApplyServerOverrideForTests();

        AssertThat(switches).IsEqual(1);
        AssertThat(refreshes).IsEqual(1);
        AssertThat(fixture.Overlay.LastStatusMessageForTests).Contains("可能服务器拥堵");
        AssertThat(fixture.Overlay.LastStatusMessageForTests.Contains("已切换", StringComparison.Ordinal)).IsFalse();
    }

    private static LanConnectChatChannelState ReadyState(string instanceId)
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            Channel = LanConnectChatChannel.Server,
            ServerChatVersion = 1,
            InstanceId = instanceId,
            HistoryEpoch = 1,
            ChatEnabled = true,
            EnabledFeatures = new ServerChatEnabledFeatures()
        });
        state.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        return state;
    }
}

internal sealed class LobbyOverlayFixture : IDisposable
{
    private LobbyOverlayFixture(
        LanConnectLobbyOverlay overlay,
        LanConnectChatChannelState serverState,
        ISceneRunner runner)
    {
        Overlay = overlay;
        ServerState = serverState;
        Runner = runner;
    }

    internal LanConnectLobbyOverlay Overlay { get; }

    internal LanConnectChatChannelState ServerState { get; }

    internal ISceneRunner Runner { get; }

    internal static async Task<LobbyOverlayFixture> Create(
        Vector2I viewportSize,
        LanConnectServerChatPresentation presentation,
        Func<string, Task<CancellationToken>>? switchServer = null,
        string? defaultServer = null,
        Action<Func<string, Task>, Action>? launchServerPicker = null,
        Func<CancellationToken, Task<bool>>? refreshServer = null)
    {
        LanConnectChatChannelState serverState = new(LanConnectChatChannel.Server);
        serverState.SetPresentationForTests(presentation);
        if (presentation == LanConnectServerChatPresentation.Ready)
        {
            serverState.Apply(new ServerChatInboundEnvelope
            {
                Type = "chat_ready",
                Channel = LanConnectChatChannel.Server,
                ServerChatVersion = 1,
                InstanceId = "overlay-tests",
                HistoryEpoch = 1,
                ChatEnabled = true,
                EnabledFeatures = new ServerChatEnabledFeatures()
            });
            serverState.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        }

        SubViewport sceneRoot = AutoFree(new SubViewport
        {
            Size = viewportSize,
            Disable3D = true
        })!;
        LanConnectLobbyOverlay overlay = new() { Visible = false };
        overlay.SetProcess(false);
        sceneRoot.AddChild(overlay);
        ISceneRunner runner = ISceneRunner.Load(sceneRoot, autoFree: true);
        await runner.AwaitIdleFrame();
        overlay.ConfigureForTests(
            viewportSize,
            serverState,
            Rooms(),
            send: _ => Task.CompletedTask,
            retry: _ => Task.CompletedTask,
            switchServer: switchServer,
            defaultServer: defaultServer,
            launchServerPicker: launchServerPicker,
            refreshServer: refreshServer);
        await runner.AwaitIdleFrame();
        await overlay.RefreshLayoutForTests(viewportSize);
        await runner.AwaitIdleFrame();
        return new LobbyOverlayFixture(overlay, serverState, runner);
    }

    public void Dispose() => Runner.Dispose();

    private static IReadOnlyList<LobbyRoomSummary> Rooms() =>
    [
        new LobbyRoomSummary
        {
            RoomId = "room-a",
            RoomName = "Alpha Room",
            HostPlayerName = "Ironclad",
            CurrentPlayers = 1,
            MaxPlayers = 4,
            GameMode = "standard",
            Version = "1.0",
            ModVersion = "0.4.0",
            Status = "waiting"
        },
        new LobbyRoomSummary
        {
            RoomId = "room-b",
            RoomName = "Beta Room",
            HostPlayerName = "Silent",
            CurrentPlayers = 3,
            MaxPlayers = 4,
            GameMode = "daily",
            Version = "1.0",
            ModVersion = "0.4.0",
            Status = "waiting",
            RequiresPassword = true
        }
    ];
}
