using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Godot;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectLucideRenderKey(
    string IconName,
    int PixelSize,
    Color Color);

internal interface ILanConnectLucideResources
{
    bool TryReadSvg(string iconName, out string svg);
}

internal interface ILanConnectLucideTextureDecoder
{
    bool TryDecode(string svg, int pixelSize, out Texture2D texture);

    Texture2D CreateFallback(int pixelSize, Color color);
}

internal sealed class LanConnectLucideIconLoader
{
    internal const int MaximumCacheEntries = 128;

    private static readonly Regex IconNamePattern = new(
        "\\A[a-z0-9-]+\\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex CurrentColorToken = new(
        "(?<![A-Za-z0-9_-])currentColor(?![A-Za-z0-9_-])",
        RegexOptions.CultureInvariant);

    private readonly object _gate = new();
    private readonly ILanConnectLucideResources _resources;
    private readonly ILanConnectLucideTextureDecoder _decoder;
    private readonly Dictionary<LanConnectLucideRenderKey, Texture2D> _cache = [];
    private readonly Queue<LanConnectLucideRenderKey> _oldest = [];

    internal LanConnectLucideIconLoader(
        ILanConnectLucideResources? resources = null,
        ILanConnectLucideTextureDecoder? decoder = null)
    {
        _resources = resources ?? LanConnectEmbeddedLucideResources.Instance;
        _decoder = decoder ?? new LanConnectGodotLucideTextureDecoder();
    }

    internal int CachedCount
    {
        get
        {
            lock (_gate)
            {
                return _cache.Count;
            }
        }
    }

    internal Texture2D Get(string iconName, int pixelSize, Color color)
    {
        ArgumentNullException.ThrowIfNull(iconName);
        if (pixelSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelSize));
        }

        LanConnectLucideRenderKey key = new(iconName, pixelSize, color);
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out Texture2D? cached))
            {
                return cached;
            }

            Texture2D texture = Render(iconName, pixelSize, color);
            if (_cache.Count == MaximumCacheEntries)
            {
                LanConnectLucideRenderKey oldest = _oldest.Dequeue();
                _cache.Remove(oldest);
            }
            _cache.Add(key, texture);
            _oldest.Enqueue(key);
            return texture;
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _cache.Clear();
            _oldest.Clear();
        }
    }

    private Texture2D Render(string iconName, int pixelSize, Color color)
    {
        if (!IconNamePattern.IsMatch(iconName) ||
            !_resources.TryReadSvg(iconName, out string svg) ||
            !TryPrepareSvg(svg, color, out string prepared) ||
            !_decoder.TryDecode(prepared, pixelSize, out Texture2D texture) ||
            texture == null)
        {
            return _decoder.CreateFallback(pixelSize, color);
        }
        return texture;
    }

    private static bool TryPrepareSvg(string svg, Color color, out string prepared)
    {
        try
        {
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using StringReader text = new(svg);
            using XmlReader reader = XmlReader.Create(text, settings);
            XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            if (document.Root?.Name.LocalName != "svg")
            {
                prepared = string.Empty;
                return false;
            }

            // Godot's SVG rasterizer accepts six-digit colors reliably, while
            // eight-digit #RRGGBBAA strokes can decode to a fully transparent image.
            string html = "#" + color.ToHtml(false);
            foreach (XAttribute attribute in document.Descendants().Attributes())
            {
                attribute.Value = CurrentColorToken.Replace(attribute.Value, html);
            }
            prepared = document.ToString(SaveOptions.DisableFormatting);
            return true;
        }
        catch (XmlException)
        {
            prepared = string.Empty;
            return false;
        }
    }
}

internal sealed class LanConnectGodotLucideTextureDecoder : ILanConnectLucideTextureDecoder
{
    public bool TryDecode(string svg, int pixelSize, out Texture2D texture)
    {
        Image image = new();
        Error error = image.LoadSvgFromString(svg, pixelSize / 24f);
        if (error != Error.Ok || image.GetWidth() <= 0 || image.GetHeight() <= 0)
        {
            texture = null!;
            return false;
        }
        texture = ImageTexture.CreateFromImage(image);
        return GodotObject.IsInstanceValid(texture);
    }

    public Texture2D CreateFallback(int pixelSize, Color color)
    {
        Image image = Image.CreateEmpty(pixelSize, pixelSize, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        Color mark = new(1f, 0.2f, 0.4f, 1f);
        int last = pixelSize - 1;
        for (int pixel = 0; pixel < pixelSize; pixel++)
        {
            image.SetPixel(pixel, 0, mark);
            image.SetPixel(pixel, last, mark);
            image.SetPixel(0, pixel, mark);
            image.SetPixel(last, pixel, mark);
            image.SetPixel(pixel, pixel, mark);
            image.SetPixel(last - pixel, pixel, mark);
        }
        return ImageTexture.CreateFromImage(image);
    }
}

internal static class LanConnectChatUiComposition
{
    internal static LanConnectLucideIconLoader Icons { get; } = new();

    internal static LanConnectChatLocalizer Localizer { get; } = new();
}

internal sealed class LanConnectEmbeddedLucideResources : ILanConnectLucideResources
{
    internal static LanConnectEmbeddedLucideResources Instance { get; } = new();

    // Lucide icon geometry is distributed under the ISC license.
    private static readonly IReadOnlyDictionary<string, string> SvgByName =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["smile"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10" /><path d="M8 14s1.5 2 4 2 4-2 4-2" /><line x1="9" x2="9.01" y1="9" y2="9" /><line x1="15" x2="15.01" y1="9" y2="9" /></svg>
                """,
            ["laugh"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10" /><path d="M18 13a6 6 0 0 1-6 5 6 6 0 0 1-6-5h12Z" /><line x1="9" x2="9.01" y1="9" y2="9" /><line x1="15" x2="15.01" y1="9" y2="9" /></svg>
                """,
            ["heart"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2 9.5a5.5 5.5 0 0 1 9.591-3.676.56.56 0 0 0 .818 0A5.49 5.49 0 0 1 22 9.5c0 2.29-1.5 4-3 5.5l-5.492 5.313a2 2 0 0 1-3 .019L5 15c-1.5-1.5-3-3.2-3-5.5" /></svg>
                """,
            ["thumbs-up"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 5.88 14 10h5.83a2 2 0 0 1 1.92 2.56l-2.33 8A2 2 0 0 1 17.5 22H4a2 2 0 0 1-2-2v-8a2 2 0 0 1 2-2h2.76a2 2 0 0 0 1.79-1.11L12 2a3.13 3.13 0 0 1 3 3.88Z" /><path d="M7 10v12" /></svg>
                """,
            ["thumbs-down"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18.12 10 14H4.17a2 2 0 0 1-1.92-2.56l2.33-8A2 2 0 0 1 6.5 2H20a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2h-2.76a2 2 0 0 0-1.79 1.11L12 22a3.13 3.13 0 0 1-3-3.88Z" /><path d="M17 14V2" /></svg>
                """,
            ["sparkles"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M11.017 2.814a1 1 0 0 1 1.966 0l1.051 5.558a2 2 0 0 0 1.594 1.594l5.558 1.051a1 1 0 0 1 0 1.966l-5.558 1.051a2 2 0 0 0-1.594 1.594l-1.051 5.558a1 1 0 0 1-1.966 0l-1.051-5.558a2 2 0 0 0-1.594-1.594l-5.558-1.051a1 1 0 0 1 0-1.966l5.558-1.051a2 2 0 0 0 1.594-1.594z" /><path d="M20 2v4" /><path d="M22 4h-4" /><circle cx="4" cy="20" r="2" /></svg>
                """,
            ["flame"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3q1 4 4 6.5t3 5.5a1 1 0 0 1-14 0 5 5 0 0 1 1-3 1 1 0 0 0 5 0c0-2-1.5-3-1.5-5q0-2 2.5-4" /></svg>
                """,
            ["zap"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z" /></svg>
                """,
            ["shield"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z" /></svg>
                """,
            ["swords"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="14.5 17.5 3 6 3 3 6 3 17.5 14.5" /><line x1="13" x2="19" y1="19" y2="13" /><line x1="16" x2="20" y1="16" y2="20" /><line x1="19" x2="21" y1="21" y2="19" /><polyline points="14.5 6.5 18 3 21 3 21 6 17.5 9.5" /><line x1="5" x2="9" y1="14" y2="18" /><line x1="7" x2="4" y1="17" y2="20" /><line x1="3" x2="5" y1="19" y2="21" /></svg>
                """,
            ["target"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10" /><circle cx="12" cy="12" r="6" /><circle cx="12" cy="12" r="2" /></svg>
                """,
            ["crown"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M11.562 3.266a.5.5 0 0 1 .876 0L15.39 8.87a1 1 0 0 0 1.516.294L21.183 5.5a.5.5 0 0 1 .798.519l-2.834 10.246a1 1 0 0 1-.956.734H5.81a1 1 0 0 1-.957-.734L2.02 6.02a.5.5 0 0 1 .798-.519l4.276 3.664a1 1 0 0 0 1.516-.294z" /><path d="M5 21h14" /></svg>
                """,
            ["skull"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m12.5 17-.5-1-.5 1h1z" /><path d="M15 22a1 1 0 0 0 1-1v-1a2 2 0 0 0 1.56-3.25 8 8 0 1 0-11.12 0A2 2 0 0 0 8 20v1a1 1 0 0 0 1 1z" /><circle cx="15" cy="12" r="1" /><circle cx="9" cy="12" r="1" /></svg>
                """,
            ["ghost"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 10h.01" /><path d="M15 10h.01" /><path d="M12 2a8 8 0 0 0-8 8v12l3-3 2.5 2.5L12 19l2.5 2.5L17 19l3 3V10a8 8 0 0 0-8-8z" /></svg>
                """,
            ["eye"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0" /><circle cx="12" cy="12" r="3" /></svg>
                """,
            ["message-circle"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2.992 16.342a2 2 0 0 1 .094 1.167l-1.065 3.29a1 1 0 0 0 1.236 1.168l3.413-.998a2 2 0 0 1 1.099.092 10 10 0 1 0-4.777-4.719" /></svg>
                """,
            ["check"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5" /></svg>
                """,
            ["x"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6 6 18" /><path d="m6 6 12 12" /></svg>
                """,
            ["send"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z" /><path d="m21.854 2.147-10.94 10.939" /></svg>
                """,
            ["link-2"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 17H7A5 5 0 0 1 7 7h2" /><path d="M15 7h2a5 5 0 1 1 0 10h-2" /><line x1="8" x2="16" y1="12" y2="12" /></svg>
                """,
            ["refresh-cw"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8" /><path d="M21 3v5h-5" /><path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16" /><path d="M8 16H3v5" /></svg>
                """,
            ["pin"] = """
                <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 17v5" /><path d="M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z" /></svg>
                """
        });

    public bool TryReadSvg(string iconName, out string svg) =>
        SvgByName.TryGetValue(iconName, out svg!);
}
