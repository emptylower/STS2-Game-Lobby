using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Lobby;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectLobbyChoiceDialogTests
{
    [TestCase]
    public async Task Dialog_and_touch_choices_stay_inside_desktop_and_android_viewports()
    {
        foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(720, 1280) })
        {
            using ChoiceDialogFixture fixture = await ChoiceDialogFixture.Create(size);
            Rect2 viewport = new(Vector2.Zero, size);
            Rect2 panel = fixture.Dialog.PanelRectForTests;

            AssertInside(panel, viewport, $"{size} panel");
            AssertThat(panel.Size.X).IsGreaterEqual(Math.Min(680f, size.X * 0.9f));
            AssertThat(fixture.Dialog.ChoiceRectsForTests.Count).IsEqual(3);
            foreach (Rect2 choice in fixture.Dialog.ChoiceRectsForTests)
            {
                AssertInside(choice, panel, $"{size} choice");
                AssertThat(choice.Size.Y).IsGreaterEqual(88f);
            }
        }
    }

    [TestCase]
    public async Task Button_pressed_signal_selects_once_and_closes_dialog()
    {
        using ChoiceDialogFixture fixture = await ChoiceDialogFixture.Create(new Vector2I(1280, 720));
        List<int> selected = [];
        fixture.Dialog.ChoiceSelected += selected.Add;

        fixture.Dialog.ActivateChoiceForTests(2);
        fixture.Dialog.ActivateChoiceForTests(2);
        await fixture.Runner.AwaitIdleFrame();

        AssertThat(selected).ContainsExactly(2);
        AssertThat(fixture.Dialog.Visible).IsFalse();
    }

    private static void AssertInside(Rect2 rect, Rect2 bounds, string context)
    {
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f ||
            rect.Position.X < bounds.Position.X - 0.5f || rect.Position.Y < bounds.Position.Y - 0.5f ||
            rect.End.X > bounds.End.X + 0.5f || rect.End.Y > bounds.End.Y + 0.5f)
        {
            throw new InvalidOperationException($"{context}: {rect} outside {bounds}");
        }
    }

    private sealed class ChoiceDialogFixture : IDisposable
    {
        private readonly ISceneRunner _runner;

        private ChoiceDialogFixture(
            ISceneRunner runner,
            LanConnectLobbyChoiceDialog dialog)
        {
            _runner = runner;
            Dialog = dialog;
        }

        internal LanConnectLobbyChoiceDialog Dialog { get; }

        internal ISceneRunner Runner => _runner;

        internal static async Task<ChoiceDialogFixture> Create(Vector2I size)
        {
            SubViewport root = AutoFree(new SubViewport
            {
                Size = size,
                Size2DOverride = size,
                Size2DOverrideStretch = true,
                Disable3D = true,
                GuiEmbedSubwindows = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always
            })!;
            FontFile font = GD.Load<FontFile>(
                "res://TestAssets/Fonts/ark-pixel-10px-proportional-zh_cn.otf") ??
                throw new InvalidOperationException("Fixed Ark Pixel screenshot font failed to load.");
            LanConnectLobbyChoiceDialog dialog = new()
            {
                Theme = new Theme { DefaultFont = font }
            };
            dialog.Configure(
                "LAN 调试建房",
                "选择要启动的游戏模式。不可用的模式会保持禁用。",
                [
                    new(0, "标准模式", "按标准规则创建 ENet LAN Host。", Primary: true),
                    new(1, "多人每日挑战", "启动多人每日挑战的 LAN Host。"),
                    new(2, "自定义模式", "进入自定义规则设置后创建 LAN Host。")
                ],
                "返回");
            root.AddChild(dialog);
            dialog.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
            dialog.Open();
            await runner.AwaitIdleFrame();
            await runner.AwaitIdleFrame();
            return new ChoiceDialogFixture(runner, dialog);
        }

        public void Dispose() => _runner.Dispose();
    }
}
