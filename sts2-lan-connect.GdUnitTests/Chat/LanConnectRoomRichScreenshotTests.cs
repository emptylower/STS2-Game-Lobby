using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRoomRichScreenshotTests
{
    private const int PixelChannelNoiseTolerance = 4;

    internal readonly record struct ScreenshotCase(Vector2I PhysicalSize, string Locale, float UiScale)
    {
        internal string Id =>
            $"{PhysicalSize.X}x{PhysicalSize.Y}-{Locale}-{UiScale * 100:0}";
    }

    private enum ScreenshotScenario
    {
        RichPicker,
        RichPreview,
        RoomDelivery,
        ReducedMotionFade
    }

    private sealed record PixelOwner(
        string Name,
        Rect2 LogicalBounds,
        Control Control,
        Rect2? LogicalClip = null);

    internal static readonly IReadOnlyList<ScreenshotCase> Cases = BuildCases();

    [TestCase]
    public async Task Fixed_matrix_real_pngs_pass_pixel_ownership_and_layout_bounds()
    {
        string outputRoot = Path.Combine(
            Path.GetTempPath(),
            "sts2-room-rich-png-" + Guid.NewGuid().ToString("N"));
        string previousLocale = TranslationServer.GetLocale();
        Directory.CreateDirectory(outputRoot);
        int captures = 0;
        try
        {
            foreach (ScreenshotCase testCase in Cases)
            {
                TranslationServer.SetLocale(testCase.Locale);
                using ChatUiFixture fixture = await ChatUiFixture.Create(
                    testCase.PhysicalSize,
                    testCase.UiScale);
                ScreenshotScenario scenario = await PrepareScenario(fixture, testCase);
                fixture.DisableCaretBlink();
                for (int framePair = 0; framePair < 6; framePair++)
                {
                    await fixture.AwaitTwoFrames();
                }

                AssertScenarioState(fixture, scenario, testCase.Id);
                AssertNamedPeerLayout(fixture, scenario, testCase.Id);
                AssertLongLabelsVisible(fixture, testCase.Id);

                using Image baseline = await fixture.CaptureImage();
                string pngPath = Path.Combine(outputRoot, testCase.Id + ".png");
                AssertThat(baseline.SavePng(pngPath)).IsEqual(Error.Ok);
                AssertRealPng(pngPath, testCase.PhysicalSize);
                AssertViewportAndPrimaryPanelPixels(baseline, fixture, scenario, testCase.Id);

                using Image stabilityProbe = await fixture.CaptureImage();
                AssertStableFrames(
                    baseline.GetData(),
                    stabilityProbe.GetData(),
                    testCase.PhysicalSize,
                    fixture,
                    testCase.Id + $" preview={fixture.MatrixPreview.TestState.Bounds}");
                byte[] ownershipBaseline = stabilityProbe.GetData();
                foreach (PixelOwner owner in BuildPixelOwners(fixture, scenario))
                {
                    Color original = owner.Control.Modulate;
                    owner.Control.Modulate = new Color(original.R, original.G, original.B, 0f);
                    await fixture.AwaitTwoFrames();
                    using Image hidden = await fixture.CaptureImage();
                    AssertDifferentialOwnership(
                        ownershipBaseline,
                        hidden.GetData(),
                        testCase.PhysicalSize,
                        owner,
                        fixture.UiScale,
                        testCase.Id);
                    owner.Control.Modulate = original;
                    await fixture.AwaitTwoFrames();
                    using Image restored = await fixture.CaptureImage();
                    ownershipBaseline = restored.GetData();
                }
                captures++;
            }
            AssertThat(captures).IsEqual(12);
            AssertThat(Directory.EnumerateFiles(outputRoot, "*.png").Count()).IsEqual(12);
        }
        finally
        {
            TranslationServer.SetLocale(previousLocale);
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    private static IReadOnlyList<ScreenshotCase> BuildCases()
    {
        List<ScreenshotCase> cases = [];
        foreach (Vector2I viewport in new[]
                 {
                     new Vector2I(1920, 1080),
                     new Vector2I(2560, 1440),
                     new Vector2I(3840, 2160)
                 })
        {
            foreach (string locale in new[] { "zh-CN", "en-US" })
            {
                foreach (float scale in new[] { 1f, 1.5f })
                {
                    cases.Add(new ScreenshotCase(viewport, locale, scale));
                }
            }
        }
        return cases;
    }

    private static async Task<ScreenshotScenario> PrepareScenario(
        ChatUiFixture fixture,
        ScreenshotCase testCase)
    {
        if (testCase.Locale == "zh-CN" && testCase.UiScale == 1f)
        {
            await fixture.ShowRichMatrixOnly();
            await fixture.OpenRichEmojiPicker();
            return ScreenshotScenario.RichPicker;
        }
        if (testCase.Locale == "zh-CN")
        {
            await fixture.ShowRichMatrixOnly();
            await fixture.ShowPreview("relic");
            return ScreenshotScenario.RichPreview;
        }
        if (testCase.UiScale == 1f)
        {
            await fixture.ShowRoomMatrixOnly();
            return ScreenshotScenario.RoomDelivery;
        }

        await fixture.ShowReducedMotionFadeOverRich();
        return ScreenshotScenario.ReducedMotionFade;
    }

    private static void AssertScenarioState(
        ChatUiFixture fixture,
        ScreenshotScenario scenario,
        string context)
    {
        AssertThat(fixture.PhysicalSize).IsEqual(Cases.Single(item => item.Id == context).PhysicalSize);
        AssertThat(fixture.RichMessageRuns().Count(run => run is not Label)).IsEqual(12);
        switch (scenario)
        {
            case ScreenshotScenario.RichPicker:
                LanConnectEmojiPickerTestState picker = fixture.RichPanel.EmojiPickerForTests.TestState;
                AssertThat(picker.Visible).IsTrue();
                AssertThat(picker.ButtonRects.Count).IsEqual(18);
                AssertThat(picker.Bounds.Size.X).IsGreater(0f);
                break;
            case ScreenshotScenario.RichPreview:
                AssertThat(fixture.MatrixPreview.TestState.Visible).IsTrue();
                AssertThat(fixture.MatrixPreview.TestState.Bounds.Size.X).IsGreater(0f);
                break;
            case ScreenshotScenario.RoomDelivery:
                AssertThat(fixture.Room.TestState.PanelOpen).IsTrue();
                AssertThat(fixture.Room.ChatPanelForTests.TestState.PendingCount).IsEqual(1);
                AssertThat(fixture.Room.ChatPanelForTests.TestState.FailedCount).IsEqual(1);
                AssertThat(fixture.Room.ChatPanelForTests.TestState.DeliveryUnknownCount).IsEqual(1);
                AssertThat(fixture.RoomUnreadBadgeText.Length).IsGreaterEqual(2);
                AssertThat(fixture.ServerUnreadBadgeText).IsEqual("0");
                break;
            case ScreenshotScenario.ReducedMotionFade:
                AssertThat(fixture.Room.TestState.FadePhase).IsEqual(LanConnectRoomOverlayFadePhase.Faded);
                AssertThat(fixture.Room.TestState.PanelAlpha).IsEqual(0f);
                AssertThat(fixture.Room.TestState.HintAlpha).IsEqual(1f);
                AssertThat(fixture.Room.TestState.TweenActive).IsFalse();
                break;
        }
    }

    private static void AssertNamedPeerLayout(
        ChatUiFixture fixture,
        ScreenshotScenario scenario,
        string context)
    {
        IReadOnlyList<LanConnectNamedControlRect> peers = scenario == ScreenshotScenario.RoomDelivery
            ? fixture.Room.TestState.FocusTargetRects
            : fixture.RichPanel.TestState.FocusTargetRects;
        Rect2 messages = scenario == ScreenshotScenario.RoomDelivery
            ? fixture.Room.TestState.MessagesRect
            : fixture.RichPanel.TestState.MessagesRect;
        LanConnectNamedControlRect[] visible = peers
            .Select(peer => peer.Name.StartsWith(
                    LanConnectConstants.ChatRetryButtonPrefix,
                    StringComparison.Ordinal)
                ? peer with { Rect = peer.Rect.Intersection(messages) }
                : peer)
            .Where(peer => peer.Rect.Size.X > 0f && peer.Rect.Size.Y > 0f)
            .ToArray();
        for (int first = 0; first < visible.Length; first++)
        {
            for (int second = first + 1; second < visible.Length; second++)
            {
                bool legalMessagesRetryContainment =
                    visible[first].Name == LanConnectConstants.ChatMessagesScrollName &&
                    visible[second].Name.StartsWith(LanConnectConstants.ChatRetryButtonPrefix, StringComparison.Ordinal) ||
                    visible[second].Name == LanConnectConstants.ChatMessagesScrollName &&
                    visible[first].Name.StartsWith(LanConnectConstants.ChatRetryButtonPrefix, StringComparison.Ordinal);
                if (!legalMessagesRetryContainment &&
                    visible[first].Rect.Intersects(visible[second].Rect, includeBorders: false))
                {
                    throw new InvalidOperationException(
                        $"{context} overlapping named controls: {visible[first].Name}/{visible[second].Name}");
                }
            }
        }

        if (scenario == ScreenshotScenario.RichPicker)
        {
            AssertNoRectOverlap(
                fixture.RichPanel.EmojiPickerForTests.TestState.ButtonRects,
                context + " picker");
        }
    }

    private static void AssertNoRectOverlap(
        IReadOnlyList<LanConnectNamedControlRect> controls,
        string context)
    {
        for (int first = 0; first < controls.Count; first++)
        {
            for (int second = first + 1; second < controls.Count; second++)
            {
                if (controls[first].Rect.Intersects(controls[second].Rect, includeBorders: false))
                {
                    throw new InvalidOperationException(
                        $"{context}: {controls[first].Name}/{controls[second].Name} overlap");
                }
            }
        }
    }

    private static void AssertLongLabelsVisible(ChatUiFixture fixture, string context)
    {
        foreach (Label label in fixture.LongNameLabels().Where(label =>
                     label.IsVisibleInTree() &&
                     label.GetGlobalRect().Intersects(fixture.ViewportRect, includeBorders: false)))
        {
            AssertThat(label.GetVisibleLineCount())
                .OverrideFailureMessage($"{context} clipped long label {label.GetPath()}")
                .IsEqual(label.GetLineCount());
            AssertThat(label.GetGlobalRect().Intersection(fixture.ViewportRect).Size.X).IsGreater(0f);
        }
    }

    private static IReadOnlyList<PixelOwner> BuildPixelOwners(
        ChatUiFixture fixture,
        ScreenshotScenario scenario)
    {
        List<PixelOwner> owners = [];
        if (scenario == ScreenshotScenario.RoomDelivery)
        {
            LanConnectRoomChatOverlayTestState state = fixture.Room.TestState;
            owners.Add(new PixelOwner(
                "room-panel",
                state.PanelRect,
                FindControl(fixture.Room, "RoomChatPanelFrame")));
            AddNamedOwners(
                owners,
                fixture.Room,
                state.FocusTargetRects,
                state.MessagesRect,
                "room");
            return owners;
        }

        LanConnectBasicChatPanelTestState rich = fixture.RichPanel.TestState;
        owners.Add(new PixelOwner("rich-panel", rich.PanelRect, fixture.RichPanel));
        AddNamedOwners(owners, fixture.RichPanel, rich.FocusTargetRects, rich.MessagesRect, "rich");
        if (scenario == ScreenshotScenario.RichPicker)
        {
            LanConnectEmojiPickerTestState picker = fixture.RichPanel.EmojiPickerForTests.TestState;
            owners.Add(new PixelOwner(
                "emoji-picker",
                picker.Bounds,
                FindControl(fixture.RichPanel.EmojiPickerForTests, LanConnectEmojiPicker.GridName)));
        }
        else if (scenario == ScreenshotScenario.RichPreview)
        {
            owners.Add(new PixelOwner(
                "item-preview",
                fixture.MatrixPreview.TestState.Bounds,
                FindControl(fixture.MatrixPreview, LanConnectItemPreview.SurfaceName)));
        }
        else if (scenario == ScreenshotScenario.ReducedMotionFade)
        {
            owners.Add(new PixelOwner(
                "fade-hint",
                fixture.Room.TestState.HintRect,
                FindControl(fixture.Room, "RoomChatFadeHint")));
        }
        return owners;
    }

    private static void AddNamedOwners(
        List<PixelOwner> owners,
        Node root,
        IReadOnlyList<LanConnectNamedControlRect> named,
        Rect2 messagesRect,
        string prefix)
    {
        string[] selectedNames =
        [
            LanConnectConstants.ChatMessagesScrollName,
            LanConnectConstants.ChatDraftInputName,
            LanConnectConstants.ChatSendButtonName
        ];
        foreach (LanConnectNamedControlRect item in named.Where(item => selectedNames.Contains(item.Name)))
        {
            Rect2? clip = item.Name == LanConnectConstants.ChatMessagesScrollName
                ? messagesRect
                : null;
            owners.Add(new PixelOwner(
                prefix + "-" + item.Name,
                item.Rect,
                FindControl(root, item.Name),
                clip));
        }
    }

    private static void AssertDifferentialOwnership(
        byte[] baseline,
        byte[] hidden,
        Vector2I physicalSize,
        PixelOwner owner,
        float uiScale,
        string context)
    {
        AssertThat(hidden.Length).IsEqual(baseline.Length);
        Rect2 logical = owner.LogicalClip is { } clip
            ? owner.LogicalBounds.Intersection(clip)
            : owner.LogicalBounds;
        int left = Math.Max(0, Mathf.FloorToInt(logical.Position.X * uiScale) - 1);
        int top = Math.Max(0, Mathf.FloorToInt(logical.Position.Y * uiScale) - 1);
        int right = Math.Min(physicalSize.X, Mathf.CeilToInt(logical.End.X * uiScale) + 1);
        int bottom = Math.Min(physicalSize.Y, Mathf.CeilToInt(logical.End.Y * uiScale) + 1);
        int changed = 0;
        for (int offset = 0; offset < baseline.Length; offset += 4)
        {
            if (Math.Abs(baseline[offset] - hidden[offset]) <= PixelChannelNoiseTolerance &&
                Math.Abs(baseline[offset + 1] - hidden[offset + 1]) <= PixelChannelNoiseTolerance &&
                Math.Abs(baseline[offset + 2] - hidden[offset + 2]) <= PixelChannelNoiseTolerance &&
                Math.Abs(baseline[offset + 3] - hidden[offset + 3]) <= PixelChannelNoiseTolerance)
            {
                continue;
            }
            changed++;
            int pixel = offset / 4;
            int x = pixel % physicalSize.X;
            int y = pixel / physicalSize.X;
            if (x < left || x >= right || y < top || y >= bottom)
            {
                throw new InvalidOperationException(
                    $"{context} {owner.Name} owns pixel ({x},{y}) outside " +
                    $"logical {logical} / physical [{left},{top})-({right},{bottom})");
            }
        }
        if (changed == 0)
        {
            throw new InvalidOperationException($"{context} {owner.Name} produced no owned foreground pixels");
        }
    }

    private static void AssertStableFrames(
        byte[] first,
        byte[] second,
        Vector2I physicalSize,
        ChatUiFixture fixture,
        string context)
    {
        AssertThat(second.Length).IsEqual(first.Length);
        for (int offset = 0; offset < first.Length; offset += 4)
        {
            if (Math.Abs(first[offset] - second[offset]) <= PixelChannelNoiseTolerance &&
                Math.Abs(first[offset + 1] - second[offset + 1]) <= PixelChannelNoiseTolerance &&
                Math.Abs(first[offset + 2] - second[offset + 2]) <= PixelChannelNoiseTolerance &&
                Math.Abs(first[offset + 3] - second[offset + 3]) <= PixelChannelNoiseTolerance)
            {
                continue;
            }
            int pixel = offset / 4;
            int x = pixel % physicalSize.X;
            int y = pixel / physicalSize.X;
            Vector2 logicalPoint = new(x / fixture.UiScale, y / fixture.UiScale);
            throw new InvalidOperationException(
                $"{context} consecutive frames drift at " +
                $"({x},{y}) logical={logicalPoint} controls=" +
                $"[{string.Join(",", fixture.VisibleControlsAt(logicalPoint))}] " +
                $"rgba=({first[offset]},{first[offset + 1]},{first[offset + 2]},{first[offset + 3]})" +
                $"->({second[offset]},{second[offset + 1]},{second[offset + 2]},{second[offset + 3]})");
        }
    }

    private static void AssertViewportAndPrimaryPanelPixels(
        Image image,
        ChatUiFixture fixture,
        ScreenshotScenario scenario,
        string context)
    {
        Rect2I used = image.GetUsedRect();
        AssertThat(used.Position.X).IsGreaterEqual(0);
        AssertThat(used.Position.Y).IsGreaterEqual(0);
        AssertThat(used.End.X).IsLessEqual(image.GetWidth());
        AssertThat(used.End.Y).IsLessEqual(image.GetHeight());

        Rect2 logicalPanel = scenario == ScreenshotScenario.RoomDelivery
            ? fixture.Room.TestState.PanelRect
            : fixture.RichPanel.TestState.PanelRect;
        int left = Mathf.Clamp(Mathf.FloorToInt(logicalPanel.Position.X * fixture.UiScale), 0, image.GetWidth() - 1);
        int top = Mathf.Clamp(Mathf.FloorToInt(logicalPanel.Position.Y * fixture.UiScale), 0, image.GetHeight() - 1);
        int right = Mathf.Clamp(Mathf.CeilToInt(logicalPanel.End.X * fixture.UiScale), left + 1, image.GetWidth());
        int bottom = Mathf.Clamp(Mathf.CeilToInt(logicalPanel.End.Y * fixture.UiScale), top + 1, image.GetHeight());
        byte[] data = image.GetData();
        HashSet<uint> colors = [];
        int opaque = 0;
        for (int y = top; y < bottom; y += 2)
        {
            for (int x = left; x < right; x += 2)
            {
                int offset = (y * image.GetWidth() + x) * 4;
                if (data[offset + 3] > 16) opaque++;
                colors.Add(
                    (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]));
            }
        }
        AssertThat(opaque)
            .OverrideFailureMessage($"{context} primary panel is blank or transparent")
            .IsGreater(100);
        AssertThat(colors.Count)
            .OverrideFailureMessage($"{context} primary panel has no foreground variation")
            .IsGreater(5);
    }

    private static void AssertRealPng(string path, Vector2I expectedSize)
    {
        byte[] bytes = File.ReadAllBytes(path);
        AssertThat(bytes.Length).IsGreater(1024);
        AssertThat(bytes.Take(8).ToArray()).IsEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        int width = ReadBigEndianInt32(bytes, 16);
        int height = ReadBigEndianInt32(bytes, 20);
        AssertThat(width).IsEqual(expectedSize.X);
        AssertThat(height).IsEqual(expectedSize.Y);
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        bytes[offset] << 24 |
        bytes[offset + 1] << 16 |
        bytes[offset + 2] << 8 |
        bytes[offset + 3];

    private static Control FindControl(Node root, string name) =>
        root.FindChild(name, recursive: true, owned: false) as Control ??
        throw new InvalidOperationException($"Named screenshot control not found: {name}");
}
