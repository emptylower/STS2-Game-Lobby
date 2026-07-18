using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.ModSync;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectModSyncDialogTests
{
    private static readonly Vector2I[] Viewports =
    [
        new(1280, 720),
        new(1920, 1080),
        new(2560, 1440),
        new(3840, 2160),
        new(720, 1280),
        new(1280, 720)
    ];

    [TestCase]
    public async Task Dialog_stays_inside_desktop_and_android_viewports_with_64_long_rows()
    {
        foreach (Vector2I size in Viewports)
        {
            using ModSyncDialogFixture fixture = await ModSyncDialogFixture.Create(size, ExtraState(64));
            LanConnectModSyncDialogTestState state = fixture.Dialog.TestState;
            Rect2 viewport = new(Vector2.Zero, size);

            AssertInside(state.PanelRect, viewport, $"{size} panel");
            AssertInside(state.ScrollRect, state.PanelRect, $"{size} scroll");
            AssertInside(state.PrimaryButtonRect, state.PanelRect, $"{size} primary");
            AssertInside(state.CancelButtonRect, state.PanelRect, $"{size} cancel");
            AssertThat(state.RowCount).IsEqual(64);
            AssertThat(state.SelectedIds.Count).IsEqual(0);
            AssertThat(state.ScrollVisible).IsTrue();
            AssertThat(state.AllVisibleTextContained).IsTrue();
        }
    }

    [TestCase]
    public async Task Relaxed_continue_is_secondary_and_cancel_has_initial_focus_for_unchecked_extras()
    {
        using ModSyncDialogFixture fixture = await ModSyncDialogFixture.Create(new Vector2I(1280, 720), ExtraState(2));
        LanConnectModSyncDialogTestState state = fixture.Dialog.TestState;

        AssertThat(state.PrimaryAction).IsEqual(LanConnectModSyncAction.ApplyChanges);
        AssertThat(state.RelaxedButtonVisible).IsTrue();
        AssertThat(state.PrimaryButtonDisabled).IsTrue();
        AssertThat(state.FocusOwnerName).IsEqual("ModSyncCancelButton");
        AssertThat(state.RelaxedButtonAccessibilityName).Contains("可能失败");
    }

    [TestCase]
    public async Task Escape_cancels_and_selection_enables_selective_disable_action()
    {
        using ModSyncDialogFixture fixture = await ModSyncDialogFixture.Create(new Vector2I(720, 1280), ExtraState(2));
        int cancelCount = 0;
        fixture.Dialog.ActionRequested += action =>
        {
            if (action == LanConnectModSyncAction.Cancel)
            {
                cancelCount++;
            }
        };

        fixture.Dialog.SetRowSelectedForTests("extra-0", true);
        AssertThat(fixture.Dialog.TestState.PrimaryButtonDisabled).IsFalse();
        fixture.Dialog.RouteKeyForTests(Key.Escape);

        AssertThat(cancelCount).IsEqual(1);
    }

    [TestCase]
    public async Task All_nine_states_rebuild_without_stale_rows_or_primary_actions()
    {
        using ModSyncDialogFixture fixture = await ModSyncDialogFixture.Create(
            new Vector2I(1280, 720),
            LanConnectModSyncViewState.Checking());
        LanConnectModSyncViewState[] states =
        [
            LanConnectModSyncViewState.Checking(),
            StateFor(LanConnectModSyncViewKind.GameVersionMismatch),
            StateFor(LanConnectModSyncViewKind.Compatible),
            StateFor(LanConnectModSyncViewKind.AutomaticSync),
            StateFor(LanConnectModSyncViewKind.ManualAction),
            ExtraState(2),
            LanConnectModSyncViewState.Progress([]),
            LanConnectModSyncViewState.RestartRequired(),
            StateFor(LanConnectModSyncViewKind.UnsupportedPlatform)
        ];

        foreach (LanConnectModSyncViewState state in states)
        {
            fixture.Dialog.ConfigureForTests(state);
            await fixture.AwaitTwoFrames();
            AssertThat(fixture.Dialog.TestState.PrimaryAction).IsEqual(state.PrimaryAction);
            AssertThat(fixture.Dialog.TestState.SelectedIds.Count).IsEqual(0);
        }
    }

    [TestCase]
    public async Task Framebuffer_is_nonblank_and_panel_is_distinct_from_modal_veil()
    {
        foreach (Vector2I size in new[] { new Vector2I(1280, 720), new Vector2I(720, 1280) })
        {
            using ModSyncDialogFixture fixture = await ModSyncDialogFixture.Create(size, ExtraState(64));
            for (int framePair = 0; framePair < 6; framePair++)
            {
                await fixture.AwaitTwoFrames();
            }
            Image image = await fixture.CaptureImage();
            string? screenshotDirectory = System.Environment.GetEnvironmentVariable("STS2_MOD_SYNC_SCREENSHOT_DIR");
            if (!string.IsNullOrWhiteSpace(screenshotDirectory))
            {
                Directory.CreateDirectory(screenshotDirectory);
                AssertThat(image.SavePng(Path.Combine(
                    screenshotDirectory,
                    $"mod-sync-{size.X}x{size.Y}.png"))).IsEqual(Error.Ok);
            }
            Rect2 panel = fixture.Dialog.TestState.PanelRect;
            Color veil = image.GetPixel(2, 2);
            Vector2 center = panel.GetCenter();
            Color card = image.GetPixel((int)center.X, (int)center.Y);

            AssertThat(image.GetWidth()).IsEqual(size.X);
            AssertThat(image.GetHeight()).IsEqual(size.Y);
            AssertThat(veil.A).IsGreater(0.9f);
            AssertThat(card.A).IsGreater(0.9f);
            AssertThat(Math.Abs(card.R - veil.R) + Math.Abs(card.G - veil.G) + Math.Abs(card.B - veil.B))
                .IsGreater(0.2f);
        }
    }

    private static LanConnectModSyncViewState ExtraState(int count)
    {
        LobbyModPreflightResponse response = new()
        {
            Enabled = true,
            ProtocolVersion = 1,
            HostInventoryAvailable = true,
            GameVersion = new LanConnectGameVersionComparison
            {
                Host = "0.109.0",
                Local = "0.109.0",
                ExactMatch = true
            },
            CanContinueRelaxed = true,
            ExtraGameplayMods = Enumerable.Range(0, count).Select(index => new LobbyModDescriptor
            {
                Id = $"extra-{index}",
                Version = "2026.07.17-super-long-version-name",
                Role = LanConnectModRoles.Gameplay,
                Source = LanConnectModSources.ModsDirectory,
                Dependencies = [],
            }).ToList()
        };
        response.ExtraGameplayMods[^1].Id = $"extra-{count - 1}-" + new string('W', 180);
        return LanConnectModSyncViewState.FromPreflight(
            response,
            LanConnectModSyncAvailability.Available);
    }

    private static LanConnectModSyncViewState StateFor(LanConnectModSyncViewKind kind) =>
        LanConnectModSyncViewState.Checking() with
        {
            Kind = kind,
            Title = LanConnectModSyncLocalizer.Title(kind),
            Message = LanConnectModSyncLocalizer.Message(kind),
            PrimaryAction = kind switch
            {
                LanConnectModSyncViewKind.Compatible => LanConnectModSyncAction.Join,
                LanConnectModSyncViewKind.AutomaticSync => LanConnectModSyncAction.ApplyChanges,
                _ => LanConnectModSyncAction.Cancel
            }
        };

    private static void AssertInside(Rect2 rect, Rect2 bounds, string context)
    {
        if (rect.Size.X <= 0 || rect.Size.Y <= 0 ||
            rect.Position.X < bounds.Position.X - 0.5f || rect.Position.Y < bounds.Position.Y - 0.5f ||
            rect.End.X > bounds.End.X + 0.5f || rect.End.Y > bounds.End.Y + 0.5f)
        {
            throw new InvalidOperationException($"{context}: {rect} outside {bounds}");
        }
    }
}

internal sealed class ModSyncDialogFixture : IDisposable
{
    private readonly SubViewport _root;
    private readonly ISceneRunner _runner;

    private ModSyncDialogFixture(SubViewport root, ISceneRunner runner, LanConnectModSyncDialog dialog)
    {
        _root = root;
        _runner = runner;
        Dialog = dialog;
    }

    internal LanConnectModSyncDialog Dialog { get; }

    internal static async Task<ModSyncDialogFixture> Create(
        Vector2I size,
        LanConnectModSyncViewState state)
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
        LanConnectModSyncDialog dialog = new()
        {
            Theme = new Theme { DefaultFont = font }
        };
        root.AddChild(dialog);
        dialog.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        dialog.ConfigureForTests(state);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        return new ModSyncDialogFixture(root, runner, dialog);
    }

    internal async Task AwaitTwoFrames()
    {
        await _runner.AwaitIdleFrame();
        await _runner.AwaitIdleFrame();
    }

    internal async Task<Image> CaptureImage()
    {
        await AwaitTwoFrames();
        TaskCompletionSource frameDrawn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFramePostDraw() => frameDrawn.TrySetResult();
        RenderingServer.FramePostDraw += OnFramePostDraw;
        try
        {
            RenderingServer.ForceDraw();
            await frameDrawn.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            RenderingServer.FramePostDraw -= OnFramePostDraw;
        }
        Image image = _root.GetTexture().GetImage();
        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }
        return image;
    }

    public void Dispose() => _runner.Dispose();
}
