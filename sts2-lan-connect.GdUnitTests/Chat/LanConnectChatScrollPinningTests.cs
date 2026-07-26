using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

/// <summary>
/// Guards the invariant that history arriving after the panel has already settled
/// on an empty channel still lands the view on the newest message.
///
/// Decisive shape: the chat panel is built and laid out while the channel is still
/// empty, and the history arrives afterwards in one batch (snapshot / history epoch).
/// That is why these tests bind the panel before appending any messages instead of
/// binding once history is already present — collapsing the two steps into a single
/// bind-with-messages-already-present shape would not exercise the bug this guards
/// against and must not be used to "simplify" these tests.
///
/// Historical defect (fixed by the <c>Range.Changed</c> connection and
/// <c>OnScrollRangeChanged</c> callback): the single <c>Refresh()</c> that rendered
/// that batch used to pin the scrollbar with the ScrollContainer's not-yet-recomputed
/// <c>MaxValue</c>/<c>Page</c>, the compensating deferred re-pin ran in the same
/// message-queue flush and read the same stale values, and <c>_renderedRevision</c>
/// was then up to date so no further <c>Refresh()</c> ever ran. The list was left on
/// the oldest message while the channel state still reported <c>IsAtBottom == true</c>,
/// which was also why the "N new messages" button never appeared.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectChatScrollPinningTests
{
    [TestCase]
    public async Task History_arriving_after_the_empty_panel_settles_lands_on_the_newest_message()
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

        // The panel is following the newest message, which is why it correctly
        // never offers the "N new messages" escape hatch.
        AssertThat(state.IsAtBottom).IsTrue();
        AssertThat(state.NewMessagesBelowCount).IsEqual(0);

        // The user is looking at the newest message.
        AssertThat(bar.Value).IsEqual(BottomValue(bar));
    }

    [TestCase]
    public async Task Room_overlay_history_batch_lands_on_the_newest_message()
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
    public async Task Reopening_after_a_close_still_lands_on_the_newest_message()
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

        // Closing captures the (correct, at-bottom) view position into the channel state.
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
