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
    public async Task Permanent_abandon_choice_is_fully_visible_with_the_production_copy()
    {
        foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(720, 1280) })
        {
            using ChoiceDialogFixture fixture = await ChoiceDialogFixture.Create(
                size,
                "确认永久放弃多人存档",
                "此操作会结束当前多人进度，并删除游戏使用的 current_run_mp.save。\n\n" +
                "LAN Connect 会先在 user://sts2_lan_connect/save-backups/ 创建可恢复备份；如果读取或备份失败，删除会自动取消。",
                [
                    new LanConnectLobbyDialogChoice(
                        1,
                        "备份并永久放弃",
                        "先创建可恢复备份，再结束当前多人进度。",
                        Danger: true)
                ],
                "保留存档");

            Rect2 choice = fixture.Dialog.ChoiceRectsForTests.Single();
            Rect2 visibleChoice = choice.Intersection(fixture.Dialog.ChoiceViewportRectForTests);
            AssertThat(visibleChoice.Size.Y).IsGreaterEqual(88f);
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
            return await Create(
                size,
                "LAN 调试建房",
                "选择要启动的游戏模式。不可用的模式会保持禁用。",
                [
                    new(0, "标准模式", "按标准规则创建 ENet LAN Host。", Primary: true),
                    new(1, "多人每日挑战", "启动多人每日挑战的 LAN Host。"),
                    new(2, "自定义模式", "进入自定义规则设置后创建 LAN Host。")
                ],
                "返回");
        }

        internal static async Task<ChoiceDialogFixture> Create(
            Vector2I size,
            string title,
            string message,
            IReadOnlyList<LanConnectLobbyDialogChoice> choices,
            string cancelText)
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
                title,
                message,
                choices,
                cancelText);
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
