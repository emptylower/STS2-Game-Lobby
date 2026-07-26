using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

/// <summary>
/// Offline visual harness for the two provisional values in <see cref="LanConnectHudLegibility"/>
/// that docs/superpowers/specs/2026-07-26-room-chat-hud-redesign-design.md §9 items 1 and 2 flag as
/// requiring a human look before they are locked in: <c>OutlineSize</c> (currently 3) and
/// <c>RestPlateColor</c>'s alpha (currently 0.35). This suite renders two comparison grids to real
/// PNGs via a Godot <see cref="SubViewport"/> and asserts only that the exported files are non-empty
/// real PNGs of the expected pixel size. It intentionally does not judge legibility — a human reviews
/// the exported images and decides whether to keep or adjust the values. It does not modify
/// <see cref="LanConnectHudLegibility"/> in any way.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectHudLegibilityScreenshotTests
{
    private const string ChineseSample = "房间 频道 引用 固定 收起";
    private const string EnglishSample = "Room Channel Ref Pin Close";
    private const int SampleFontSize = 13;
    private const int AnnotationFontSize = 18;

    // Images are written to a fixed (non-GUID) folder under the system temp directory so repeat
    // runs land at the same known path for review, and so the harness never touches the repo.
    private const string OutputDirectoryName = "sts2-hud-legibility-review";

    // The HUD/room chat style's normal message text colour (LanConnectBasicChatPanel.DarkTextStrongColor,
    // used when UsesLobbyStyle is false) — the light text these values must keep readable.
    private static readonly Color PanelTextColor = new(0.94f, 0.91f, 0.84f, 1f);

    private readonly record struct OutlineVariant(int Size, string HeaderText, bool UseProductionPath);

    private readonly record struct Band(Color Fill, string HeaderText);

    private readonly record struct LanguageSample(string Text, string HeaderText);

    private readonly record struct PlateAlphaVariant(float Alpha, string HeaderText);

    private readonly record struct OutlineRow(bool WithOutline, string HeaderText);

    [TestCase]
    public async Task Outline_width_grid_renders_to_png_for_human_review()
    {
        (Control root, Vector2I size) = BuildOutlineWidthGrid();
        using Image image = await RenderToImage(root, size);

        string outputRoot = Path.Combine(Path.GetTempPath(), OutputDirectoryName);
        Directory.CreateDirectory(outputRoot);
        string pngPath = Path.Combine(outputRoot, "outline-width-grid.png");
        AssertThat(image.SavePng(pngPath)).IsEqual(Error.Ok);
        AssertRealPng(pngPath, size);
        GD.Print($"[LanConnectHudLegibilityScreenshotTests] outline width grid -> {pngPath}");
    }

    [TestCase]
    public async Task Rest_plate_alpha_grid_renders_to_png_for_human_review()
    {
        (Control root, Vector2I size) = BuildRestPlateAlphaGrid();
        using Image image = await RenderToImage(root, size);

        string outputRoot = Path.Combine(Path.GetTempPath(), OutputDirectoryName);
        Directory.CreateDirectory(outputRoot);
        string pngPath = Path.Combine(outputRoot, "rest-plate-alpha-grid.png");
        AssertThat(image.SavePng(pngPath)).IsEqual(Error.Ok);
        AssertRealPng(pngPath, size);
        GD.Print($"[LanConnectHudLegibilityScreenshotTests] rest plate alpha grid -> {pngPath}");
    }

    /// <summary>
    /// Image A: three flat background bands (dark cave floor, mid torch-lit, bright parchment map),
    /// each showing the Chinese and English 13px samples at outline_size 0, 2 and the current
    /// production <see cref="LanConnectHudLegibility.OutlineSize"/> (3 today). Only the last column
    /// goes through <see cref="LanConnectHudLegibility.ApplyTextOutline"/>; the 0 and 2 columns set
    /// the theme overrides directly and are therefore synthetic, not production-path.
    /// </summary>
    private static (Control Root, Vector2I Size) BuildOutlineWidthGrid()
    {
        const int bgNameColumnWidth = 130;
        const int languageColumnWidth = 90;
        const int labelColumnWidth = bgNameColumnWidth + languageColumnWidth;
        const int headerRowHeight = 60;
        const int outlineColumnWidth = 340;
        const int languageRowHeight = 80;
        const int bandHeight = languageRowHeight * 2;
        Vector2I size = new(labelColumnWidth + outlineColumnWidth * 3, headerRowHeight + bandHeight * 3);

        Color gutterColor = HexColor(0x20, 0x20, 0x24);
        Color gutterText = Colors.White;

        Control root = new() { Name = "OutlineWidthGridRoot", Theme = LoadFixedZhFont() };
        root.Position = Vector2.Zero;
        root.Size = size;
        root.AddChild(MakeFill(Vector2.Zero, size, gutterColor));

        OutlineVariant[] outlineVariants =
        {
            new(0, "outline 0\n(theme override)", UseProductionPath: false),
            new(2, "outline 2\n(theme override)", UseProductionPath: false),
            new(
                LanConnectHudLegibility.OutlineSize,
                $"outline {LanConnectHudLegibility.OutlineSize}\n(ApplyTextOutline)",
                UseProductionPath: true)
        };

        for (int column = 0; column < outlineVariants.Length; column++)
        {
            Label header = CreateLabel(outlineVariants[column].HeaderText, AnnotationFontSize, gutterText);
            header.Position = new Vector2(labelColumnWidth + column * outlineColumnWidth, 0);
            header.Size = new Vector2(outlineColumnWidth, headerRowHeight);
            header.HorizontalAlignment = HorizontalAlignment.Center;
            header.VerticalAlignment = VerticalAlignment.Center;
            root.AddChild(header);
        }

        Band[] bands =
        {
            new(HexColor(0x1e, 0x1b, 0x26), "dark\n#1e1b26"),
            new(HexColor(0x8a, 0x7f, 0x63), "mid\n#8a7f63"),
            new(HexColor(0xd9, 0xcb, 0xa8), "bright\n#d9cba8")
        };

        LanguageSample[] samples =
        {
            new(ChineseSample, "中文"),
            new(EnglishSample, "EN")
        };

        for (int band = 0; band < bands.Length; band++)
        {
            float bandY = headerRowHeight + band * bandHeight;
            root.AddChild(MakeFill(
                new Vector2(labelColumnWidth, bandY),
                new Vector2(size.X - labelColumnWidth, bandHeight),
                bands[band].Fill));

            Label bandLabel = CreateLabel(bands[band].HeaderText, AnnotationFontSize, gutterText);
            bandLabel.Position = new Vector2(0, bandY);
            bandLabel.Size = new Vector2(bgNameColumnWidth, bandHeight);
            bandLabel.HorizontalAlignment = HorizontalAlignment.Center;
            bandLabel.VerticalAlignment = VerticalAlignment.Center;
            root.AddChild(bandLabel);

            for (int row = 0; row < samples.Length; row++)
            {
                float rowY = bandY + row * languageRowHeight;

                Label languageLabel = CreateLabel(samples[row].HeaderText, AnnotationFontSize, gutterText);
                languageLabel.Position = new Vector2(bgNameColumnWidth, rowY);
                languageLabel.Size = new Vector2(languageColumnWidth, languageRowHeight);
                languageLabel.HorizontalAlignment = HorizontalAlignment.Center;
                languageLabel.VerticalAlignment = VerticalAlignment.Center;
                root.AddChild(languageLabel);

                for (int column = 0; column < outlineVariants.Length; column++)
                {
                    Label sample = CreateLabel(samples[row].Text, SampleFontSize, PanelTextColor);
                    sample.Position = new Vector2(labelColumnWidth + column * outlineColumnWidth + 16, rowY);
                    sample.Size = new Vector2(outlineColumnWidth - 32, languageRowHeight);
                    sample.VerticalAlignment = VerticalAlignment.Center;

                    if (outlineVariants[column].UseProductionPath)
                    {
                        LanConnectHudLegibility.ApplyTextOutline(sample);
                    }
                    else
                    {
                        sample.AddThemeColorOverride("font_outline_color", LanConnectHudLegibility.OutlineColor);
                        sample.AddThemeConstantOverride("outline_size", outlineVariants[column].Size);
                    }

                    root.AddChild(sample);
                }
            }
        }

        return (root, size);
    }

    /// <summary>
    /// Image B: on the bright parchment-map band, the 13px Chinese sample on a rounded rest plate at
    /// alpha 0.35 (the current <see cref="LanConnectHudLegibility.RestPlateColor"/>) and a synthetic
    /// 0.45 copy, each with and without the text outline
    /// (<see cref="LanConnectHudLegibility.ApplyTextOutline"/> for the "with outline" row). The plate's
    /// rounded-rect shape (5px corner radius, 10/6px content margins) mirrors
    /// <see cref="LanConnectHudLegibility"/>'s private <c>Plate()</c> helper, which this harness cannot
    /// call directly, so the stylebox here is a harness-only recreation of that shape, not the
    /// production code path.
    /// </summary>
    private static (Control Root, Vector2I Size) BuildRestPlateAlphaGrid()
    {
        const int rowHeaderWidth = 170;
        const int colHeaderHeight = 56;
        const int cellWidth = 340;
        const int cellHeight = 150;
        const int platePadding = 20;
        Vector2I size = new(rowHeaderWidth + cellWidth * 2, colHeaderHeight + cellHeight * 2);

        Color gutterColor = HexColor(0x20, 0x20, 0x24);
        Color gutterText = Colors.White;
        Color brightBand = HexColor(0xd9, 0xcb, 0xa8);

        Control root = new() { Name = "RestPlateAlphaGridRoot", Theme = LoadFixedZhFont() };
        root.Position = Vector2.Zero;
        root.Size = size;
        root.AddChild(MakeFill(Vector2.Zero, size, gutterColor));
        root.AddChild(MakeFill(
            new Vector2(rowHeaderWidth, colHeaderHeight),
            new Vector2(size.X - rowHeaderWidth, size.Y - colHeaderHeight),
            brightBand));

        PlateAlphaVariant[] alphaVariants =
        {
            new(LanConnectHudLegibility.RestPlateColor.A, $"alpha {LanConnectHudLegibility.RestPlateColor.A:0.00}\n(current)"),
            new(0.45f, "alpha 0.45\n(candidate)")
        };

        for (int column = 0; column < alphaVariants.Length; column++)
        {
            Label header = CreateLabel(alphaVariants[column].HeaderText, AnnotationFontSize, gutterText);
            header.Position = new Vector2(rowHeaderWidth + column * cellWidth, 0);
            header.Size = new Vector2(cellWidth, colHeaderHeight);
            header.HorizontalAlignment = HorizontalAlignment.Center;
            header.VerticalAlignment = VerticalAlignment.Center;
            root.AddChild(header);
        }

        OutlineRow[] outlineRows =
        {
            new(WithOutline: false, "no outline"),
            new(WithOutline: true, "with outline\n(ApplyTextOutline)")
        };

        for (int row = 0; row < outlineRows.Length; row++)
        {
            float rowY = colHeaderHeight + row * cellHeight;

            Label rowLabel = CreateLabel(outlineRows[row].HeaderText, AnnotationFontSize, gutterText);
            rowLabel.Position = new Vector2(0, rowY);
            rowLabel.Size = new Vector2(rowHeaderWidth, cellHeight);
            rowLabel.HorizontalAlignment = HorizontalAlignment.Center;
            rowLabel.VerticalAlignment = VerticalAlignment.Center;
            root.AddChild(rowLabel);

            for (int column = 0; column < alphaVariants.Length; column++)
            {
                float cellX = rowHeaderWidth + column * cellWidth;
                Color plateColor = new(
                    LanConnectHudLegibility.RestPlateColor.R,
                    LanConnectHudLegibility.RestPlateColor.G,
                    LanConnectHudLegibility.RestPlateColor.B,
                    alphaVariants[column].Alpha);

                Panel plate = new()
                {
                    Position = new Vector2(cellX + platePadding, rowY + platePadding),
                    Size = new Vector2(cellWidth - 2 * platePadding, cellHeight - 2 * platePadding),
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                plate.AddThemeStyleboxOverride("panel", CreateRestPlateStyle(plateColor));
                root.AddChild(plate);

                Label text = CreateLabel(ChineseSample, SampleFontSize, PanelTextColor);
                text.Position = Vector2.Zero;
                text.Size = plate.Size;
                text.HorizontalAlignment = HorizontalAlignment.Center;
                text.VerticalAlignment = VerticalAlignment.Center;
                if (outlineRows[row].WithOutline)
                {
                    LanConnectHudLegibility.ApplyTextOutline(text);
                }
                plate.AddChild(text);
            }
        }

        return (root, size);
    }

    private static StyleBoxFlat CreateRestPlateStyle(Color background) => new()
    {
        BgColor = background,
        BorderColor = Colors.Transparent,
        BorderWidthLeft = 0,
        BorderWidthTop = 0,
        BorderWidthRight = 0,
        BorderWidthBottom = 0,
        CornerRadiusTopLeft = 5,
        CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5,
        CornerRadiusBottomRight = 5,
        ContentMarginLeft = 10,
        ContentMarginTop = 6,
        ContentMarginRight = 10,
        ContentMarginBottom = 6
    };

    private static ColorRect MakeFill(Vector2 position, Vector2 size, Color color) => new()
    {
        Position = position,
        Size = size,
        Color = color,
        MouseFilter = Control.MouseFilterEnum.Ignore
    };

    private static Label CreateLabel(string text, int fontSize, Color color)
    {
        Label label = new() { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Color HexColor(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f, 1f);

    /// <summary>
    /// Same fixed CJK-capable test font used by <c>LanConnectChatResolutionTests.LoadFixedTestTheme</c>
    /// (Ark Pixel, res://TestAssets/Fonts) so this harness's Chinese glyphs render through a real font
    /// with CJK coverage rather than the editor's default (which lacks it in headless test runs). This
    /// mirrors how the production overlay obtains its font: <c>LanConnectBasicChatPanel</c> calls
    /// <c>GetThemeDefaultFont()</c>, i.e. whatever font a Theme higher up the tree supplies.
    /// </summary>
    private static Theme LoadFixedZhFont()
    {
        FontFile font = GD.Load<FontFile>(
            "res://TestAssets/Fonts/ark-pixel-10px-proportional-zh_cn.otf") ??
            throw new InvalidOperationException("Fixed Ark Pixel screenshot font failed to load.");
        return new Theme { DefaultFont = font };
    }

    private static async Task<Image> RenderToImage(Control root, Vector2I size)
    {
        SubViewport viewport = AutoFree(new SubViewport
        {
            Size = size,
            Size2DOverride = size,
            Size2DOverrideStretch = true,
            Disable3D = true,
            GuiEmbedSubwindows = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
            Snap2DTransformsToPixel = true,
            Snap2DVerticesToPixel = true
        })!;
        viewport.AddChild(root);

        using ISceneRunner runner = ISceneRunner.Load(viewport, autoFree: true);
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();

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

        Image image = viewport.GetTexture().GetImage();
        if (image.GetWidth() != size.X || image.GetHeight() != size.Y)
        {
            throw new InvalidOperationException(
                $"capture size {image.GetWidth()}x{image.GetHeight()} != {size.X}x{size.Y}");
        }
        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }
        return image;
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
}
