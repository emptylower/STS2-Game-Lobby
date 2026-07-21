using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectReferenceModeUiTests
{
    [TestCase]
    public async Task Reference_button_reflects_available_armed_and_accessibility_states()
    {
        bool armed = false;
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        panel.ConfigureReferenceMode(
            _ => new LanConnectReferenceModePresentation(
                Visible: true,
                Enabled: true,
                Armed: armed,
                StatusKey: armed ? "chat.reference.armed_hint" : string.Empty),
            (_, source) =>
            {
                AssertThat(source).IsEqual(LanConnectReferenceModeSource.TouchButton);
                armed = !armed;
                return true;
            });
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(EnabledRoomState(), (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        Button button = panel.ReferenceButtonForTests;
        AssertThat(button.Name.ToString()).IsEqual(LanConnectConstants.ChatReferenceButtonName);
        AssertThat(button.Visible).IsTrue();
        AssertThat(button.Disabled).IsFalse();
        AssertThat(button.AccessibilityName).IsEqual("引用");

        button.EmitSignal(Button.SignalName.Pressed);
        await panel.RefreshForTests();
        AssertThat(button.ButtonPressed).IsTrue();
        AssertThat(button.AccessibilityName).IsEqual("引用模式已开启，选择一个对象");
        AssertThat(panel.FindChild(LanConnectConstants.ChatStatusLabelName, true, false) is Label
        {
            Text: "请选择卡牌、遗物、药水、状态或玩家；引用一次后自动退出"
        }).IsTrue();
    }

    [TestCase]
    public async Task Reference_button_is_disabled_with_reason_and_enters_focus_chain_when_available()
    {
        bool available = false;
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        panel.ConfigureReferenceMode(
            _ => new LanConnectReferenceModePresentation(
                Visible: true,
                Enabled: available,
                Armed: false,
                StatusKey: available ? string.Empty : "chat.reference.no_targets"),
            (_, _) => false);
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(EnabledRoomState(), (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        AssertThat(panel.ReferenceButtonForTests.Disabled).IsTrue();
        AssertThat(panel.ReferenceButtonForTests.TooltipText).IsEqual("当前场景没有可引用对象");

        available = true;
        await panel.RefreshForTests();
        AssertThat(panel.ReferenceButtonForTests.Disabled).IsFalse();
        AssertThat(panel.TestState.FocusTargetRects
            .Any(control => control.Name == LanConnectConstants.ChatReferenceButtonName)).IsTrue();
    }

    [TestCase(1920, 1080)]
    [TestCase(1080, 1920)]
    public async Task Reference_button_stays_inside_horizontal_and_touch_portrait_input_rows(
        int width,
        int height)
    {
        using ChatUiFixture fixture = await ChatUiFixture.Create(new Vector2I(width, height), 1f);
        fixture.RichPanel.ConfigureReferenceMode(
            _ => new LanConnectReferenceModePresentation(true, true, false, string.Empty),
            (_, _) => true);
        await fixture.ShowRichMatrixOnly();
        await fixture.AwaitTwoFrames();

        Rect2 viewport = fixture.ViewportRect;
        Button reference = fixture.RichPanel.ReferenceButtonForTests;
        Button send = (Button)fixture.RichPanel.FindChild(
            LanConnectConstants.ChatSendButtonName,
            true,
            false);
        Rect2 referenceRect = reference.GetGlobalRect();
        Rect2 sendRect = send.GetGlobalRect();
        AssertThat(referenceRect.Size.X).IsGreater(0f);
        AssertThat(referenceRect.Size.Y).IsGreater(0f);
        AssertThat(viewport.Encloses(referenceRect)).IsTrue();
        AssertThat(viewport.Encloses(sendRect)).IsTrue();
        AssertThat(referenceRect.Intersects(sendRect, includeBorders: false)).IsFalse();
    }

    private static LanConnectChatChannelState EnabledRoomState()
    {
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
        state.SetEnabledRichFeatures(new LanConnectChatFeatureVersions(1, 1, 1, 1));
        state.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        return state;
    }

    private static LanConnectResolvedItem ResolveItem(LanConnectItemRun run) => new(
        LanConnectResolvedItemStatus.Resolved,
        run.ItemType,
        "chat.item." + run.ItemType,
        run.ModelId,
        run.ItemType,
        new LanConnectHoverTipPreviewData(run.ItemType, run.ItemType, "description", null));
}
