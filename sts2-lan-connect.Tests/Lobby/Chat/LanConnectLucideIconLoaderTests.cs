using Godot;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectLucideIconLoaderTests
{
    private const string ValidSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" stroke="currentColor">
          <title>currentColor</title>
          <path d="M2 2h20v20H2z" style="fill:currentColor" data-note="currentColor-suffix" />
        </svg>
        """;

    [Fact]
    public void Same_render_key_reads_and_decodes_once_while_distinct_keys_cache_separately()
    {
        FakeResources resources = new(ValidSvg);
        FakeDecoder decoder = new();
        LanConnectLucideIconLoader loader = new(resources, decoder);

        Texture2D first = loader.Get("thumbs-up", 20, Colors.White);
        Texture2D second = loader.Get("thumbs-up", 20, Colors.White);
        Texture2D larger = loader.Get("thumbs-up", 24, Colors.White);
        Texture2D red = loader.Get("thumbs-up", 20, Colors.Red);

        Assert.Same(first, second);
        Assert.NotSame(first, larger);
        Assert.NotSame(first, red);
        Assert.Equal(3, resources.ReadCount("thumbs-up"));
        Assert.Equal(3, decoder.DecodeCount);
        Assert.Equal(3, loader.CachedCount);
    }

    [Fact]
    public void Recolors_only_explicit_current_color_tokens_in_svg_attributes()
    {
        FakeResources resources = new(ValidSvg);
        FakeDecoder decoder = new();
        LanConnectLucideIconLoader loader = new(resources, decoder);
        Color color = new(0.2f, 0.4f, 0.6f, 0.8f);

        loader.Get("smile", 20, color);

        string decoded = Assert.Single(decoder.DecodedSvg);
        string html = "#" + color.ToHtml();
        Assert.Contains($"stroke=\"{html}\"", decoded, StringComparison.Ordinal);
        Assert.Contains($"fill:{html}", decoded, StringComparison.Ordinal);
        Assert.Contains(">currentColor</", decoded, StringComparison.Ordinal);
        Assert.Contains("currentColor-suffix", decoded, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Pin")]
    [InlineData("pin.svg")]
    [InlineData("../pin")]
    [InlineData("pin icon")]
    [InlineData("pin\n")]
    [InlineData("pin\r\n")]
    [InlineData("pin\r")]
    public void Invalid_icon_names_never_reach_the_resource_reader(string iconName)
    {
        FakeResources resources = new(ValidSvg);
        FakeDecoder decoder = new();
        LanConnectLucideIconLoader loader = new(resources, decoder);

        Texture2D fallback = loader.Get(iconName, 20, Colors.White);

        Assert.Equal(0, resources.TotalReads);
        Assert.Equal(0, decoder.DecodeCount);
        Assert.Equal(1, decoder.FallbackCount);
        Assert.NotNull(fallback);
        Assert.Equal((20, Colors.White), Assert.Single(decoder.FallbackRequests));
        Assert.Equal(1, loader.CachedCount);
        Assert.Same(fallback, loader.Get(iconName, 20, Colors.White));
        Assert.Equal(1, decoder.FallbackCount);
    }

    [Fact]
    public void Missing_and_malformed_svg_use_cached_deterministic_visible_fallbacks()
    {
        FakeResources resources = new();
        resources.Add("malformed", "<svg><path></svg>");
        FakeDecoder decoder = new();
        LanConnectLucideIconLoader loader = new(resources, decoder);

        Texture2D missing = loader.Get("missing", 18, Colors.Yellow);
        Texture2D missingAgain = loader.Get("missing", 18, Colors.Yellow);
        Texture2D malformed = loader.Get("malformed", 18, Colors.Yellow);
        Texture2D malformedAgain = loader.Get("malformed", 18, Colors.Yellow);

        Assert.Same(missing, missingAgain);
        Assert.Same(malformed, malformedAgain);
        Assert.Equal(2, decoder.FallbackCount);
        Assert.Equal(0, decoder.DecodeCount);
        Assert.All(decoder.FallbackRequests, request => Assert.True(request.PixelSize > 0));
    }

    [Fact]
    public void Cache_evicts_the_oldest_entry_at_128_and_clear_forces_a_reload()
    {
        FakeResources resources = new(ValidSvg);
        FakeDecoder decoder = new();
        LanConnectLucideIconLoader loader = new(resources, decoder);
        for (int index = 0; index < 129; index++)
        {
            loader.Get($"icon-{index}", 20, Colors.White);
        }

        Assert.Equal(128, loader.CachedCount);
        Assert.Equal(1, resources.ReadCount("icon-0"));
        loader.Get("icon-0", 20, Colors.White);
        Assert.Equal(2, resources.ReadCount("icon-0"));

        loader.Clear();
        Assert.Equal(0, loader.CachedCount);
        loader.Get("icon-128", 20, Colors.White);
        Assert.Equal(2, resources.ReadCount("icon-128"));
    }

    [Fact]
    public void Embedded_resources_resolve_all_version_one_emoji_and_chat_icons()
    {
        FakeDecoder decoder = new();
        LanConnectLucideIconLoader loader = new(decoder: decoder);
        string[] chatIcons = ["send", "refresh-cw", "pin"];

        foreach (string iconName in LanConnectChatEmojiSet.Version1
                     .Select(emoji => emoji.LucideIcon)
                     .Concat(chatIcons))
        {
            loader.Get(iconName, 20, Colors.White);
        }

        Assert.Equal(21, decoder.DecodeCount);
        Assert.Equal(0, decoder.FallbackCount);
    }

    private sealed class FakeResources : ILanConnectLucideResources
    {
        private readonly Dictionary<string, string> _svg = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _reads = new(StringComparer.Ordinal);
        private readonly string? _defaultSvg;

        internal FakeResources(string? defaultSvg = null) => _defaultSvg = defaultSvg;

        internal int TotalReads => _reads.Values.Sum();

        internal void Add(string name, string svg) => _svg[name] = svg;

        internal int ReadCount(string name) => _reads.GetValueOrDefault(name);

        public bool TryReadSvg(string iconName, out string svg)
        {
            _reads[iconName] = ReadCount(iconName) + 1;
            if (_svg.TryGetValue(iconName, out string? exact))
            {
                svg = exact;
                return true;
            }
            if (_defaultSvg != null)
            {
                svg = _defaultSvg;
                return true;
            }
            svg = string.Empty;
            return false;
        }
    }

    private sealed class FakeDecoder : ILanConnectLucideTextureDecoder
    {
        internal List<string> DecodedSvg { get; } = [];
        internal List<(int PixelSize, Color Color)> FallbackRequests { get; } = [];
        internal int DecodeCount { get; private set; }
        internal int FallbackCount { get; private set; }

        public bool TryDecode(string svg, int pixelSize, out Texture2D texture)
        {
            DecodeCount++;
            DecodedSvg.Add(svg);
            texture = Texture(pixelSize, Colors.White);
            return true;
        }

        public Texture2D CreateFallback(int pixelSize, Color color)
        {
            FallbackCount++;
            FallbackRequests.Add((pixelSize, color));
            return Texture(pixelSize, color);
        }

        private static Texture2D Texture(int pixelSize, Color color) =>
            (ImageTexture)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(ImageTexture));
    }
}
