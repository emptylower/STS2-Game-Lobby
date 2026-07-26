using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

/// <summary>
/// Reproduces the "chat opens on the oldest message" defect.
///
/// Production shape: the chat panel is built and laid out while the channel is still
/// empty, and the history arrives afterwards in one batch (snapshot / history epoch).
/// The single <c>Refresh()</c> that renders that batch pins the scrollbar with the
/// ScrollContainer's not-yet-recomputed <c>MaxValue</c>/<c>Page</c>, the compensating
/// deferred re-pin runs in the same message-queue flush and reads the same stale
/// values, and <c>_renderedRevision</c> is then up to date so no further
/// <c>Refresh()</c> ever runs. The list is left on the oldest message while the
/// channel state still reports <c>IsAtBottom == true</c>, which is also why the
/// "N new messages" button never appears.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectChatScrollPinningTests
{
    [TestCase]
    public async Task History_arriving_after_the_empty_panel_settles_leaves_the_view_on_the_oldest_message()
    {
        LanConnectChatChannelState state = EnabledState();
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel
        {
            CustomMinimumSize = new Vector2(480, 300)
        })!;
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);

        // The panel is bound and laid out while the channel is still empty.
        panel.Bind(state, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

        // The history batch arrives afterwards, as it does in production.
        for (int index = 0; index < 40; index++)
        {
            state.AppendConfirmedForTests($"m-{index}", "A", $"message {index}", index + 1, false);
        }
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

        ScrollBar bar = FindNode<ScrollContainer>(panel, LanConnectConstants.ChatMessagesScrollName)
            .GetVScrollBar();

        // Non-vacuous: the list really does overflow, so there is a bottom to be at.
        AssertThat(BottomValue(bar)).IsGreater(0d);

        // The panel believes it is following the newest message, which is why it
        // never offers the "N new messages" escape hatch.
        AssertThat(state.IsAtBottom).IsTrue();
        AssertThat(state.NewMessagesBelowCount).IsEqual(0);

        // But the user is looking at the oldest message.
        AssertThat(bar.Value).IsEqual(BottomValue(bar));
    }

    [TestCase]
    public async Task Room_overlay_history_batch_leaves_the_view_on_the_oldest_message()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();

        for (int index = 0; index < 40; index++)
        {
            fixture.State.Room.AppendConfirmedForTests(
                $"room-{index}", "A", $"room message {index}", index + 1, false);
        }
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();

        ScrollBar bar = FindNode<ScrollContainer>(
            fixture.Overlay.ChatPanelForTests, LanConnectConstants.ChatMessagesScrollName).GetVScrollBar();

        AssertThat(BottomValue(bar)).IsGreater(0d);
        AssertThat(fixture.State.Room.IsAtBottom).IsTrue();
        AssertThat(bar.Value).IsEqual(BottomValue(bar));
    }

    [TestCase]
    public async Task Closing_the_overlay_from_the_stuck_view_pins_every_later_open_to_the_oldest_message()
    {
        using RoomChatFixture fixture = await RoomChatFixture.OpenWithServerSupport();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();

        for (int index = 0; index < 40; index++)
        {
            fixture.State.Room.AppendConfirmedForTests(
                $"room-{index}", "A", $"room message {index}", index + 1, false);
        }
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();

        // Closing captures the (wrong) view position into the channel state.
        await fixture.Overlay.CloseForTests();
        await fixture.Runner.AwaitIdleFrame();

        // More traffic while closed, then the user opens the panel again.
        for (int index = 40; index < 60; index++)
        {
            fixture.State.Room.AppendConfirmedForTests(
                $"room-{index}", "A", $"room message {index}", index + 1, false);
        }
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Overlay.OpenForTests();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();
        await fixture.Runner.AwaitIdleFrame();

        ScrollBar bar = FindNode<ScrollContainer>(
            fixture.Overlay.ChatPanelForTests, LanConnectConstants.ChatMessagesScrollName).GetVScrollBar();

        AssertThat(BottomValue(bar)).IsGreater(0d);
        AssertThat(bar.Value).IsEqual(BottomValue(bar));
    }

    private static LanConnectChatChannelState EnabledState()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Server);
        state.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            InstanceId = "scroll-pinning-tests",
            HistoryEpoch = 1,
            ChatEnabled = true,
            EnabledFeatures = new ServerChatEnabledFeatures()
        });
        state.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        return state;
    }

    private static double BottomValue(ScrollBar bar) =>
        Math.Max(bar.MinValue, bar.MaxValue - bar.Page);

    private static T FindNode<T>(Node root, string name) where T : Node =>
        (T)root.FindChild(name, recursive: true, owned: false);
}
