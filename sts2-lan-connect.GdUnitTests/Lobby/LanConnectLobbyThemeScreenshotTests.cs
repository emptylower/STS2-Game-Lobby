using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Lobby;

/// <summary>
/// Offline visual harness for the lobby overlay's three named themes
/// (<see cref="LanConnectLobbyThemes.All"/>). For each theme this renders a fully populated
/// <see cref="LanConnectLobbyOverlay"/> (five rooms mirroring the design prototypes, the first one
/// selected, a ready server chat with a few messages) into a <see cref="SubViewport"/> and saves a
/// real PNG so a human (or Claude, via image reading) can review the look. It intentionally does not
/// judge the visuals itself and does not modify <see cref="LanConnectLobbyOverlay"/> or any theme
/// definition. Until the overlay's per-theme visual refactor lands, all three PNGs will look
/// identical — that is expected of this harness.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectLobbyThemeScreenshotTests
{
    private static readonly Vector2I ViewportSize = new(1666, 944);

    // Images are written to a fixed (non-GUID) folder under the system temp directory so repeat
    // runs land at the same known path for review, and so the harness never touches the repo.
    private const string OutputDirectoryName = "sts2-lobby-theme-review";

    [TestCase]
    public async Task Every_theme_renders_lobby_overlay_to_png_for_human_review()
    {
        string outputRoot = Path.Combine(Path.GetTempPath(), OutputDirectoryName);
        Directory.CreateDirectory(outputRoot);

        try
        {
            foreach (LanConnectLobbyTheme theme in LanConnectLobbyThemes.All)
            {
                LanConnectLobbyThemes.SetForTests(theme);

                using Image image = await RenderThemedOverlayToImage();

                string pngPath = Path.Combine(outputRoot, $"{theme.Id}.png");
                AssertThat(image.SavePng(pngPath)).IsEqual(Error.Ok);
                AssertRealPng(pngPath, ViewportSize);
                GD.Print($"[LanConnectLobbyThemeScreenshotTests] {theme.DisplayName} ({theme.Id}) -> {pngPath}");
            }
        }
        finally
        {
            LanConnectLobbyThemes.SetForTests(LanConnectLobbyThemes.Default);
        }
    }

    [TestCase]
    public async Task Theme_menu_open_renders_to_png_for_human_review()
    {
        string outputRoot = Path.Combine(Path.GetTempPath(), OutputDirectoryName);
        Directory.CreateDirectory(outputRoot);

        try
        {
            foreach (LanConnectLobbyTheme theme in LanConnectLobbyThemes.All)
            {
                LanConnectLobbyThemes.SetForTests(theme);

                using Image image = await RenderThemedOverlayToImage(openThemeMenu: true);

                string pngPath = Path.Combine(outputRoot, $"{theme.Id}-menu.png");
                AssertThat(image.SavePng(pngPath)).IsEqual(Error.Ok);
                AssertRealPng(pngPath, ViewportSize);
                GD.Print($"[LanConnectLobbyThemeScreenshotTests] {theme.DisplayName} menu -> {pngPath}");
            }
        }
        finally
        {
            LanConnectLobbyThemes.SetForTests(LanConnectLobbyThemes.Default);
        }
    }

    /// <summary>
    /// Regression: a mouse click on a room card grabs focus before gui_input arrives, which used
    /// to move the selection id without rebuilding the list, so the card never highlighted until
    /// the next auto refresh. Clicking must show the SELECT badge immediately.
    /// </summary>
    [TestCase]
    public async Task Clicking_a_room_card_highlights_it_immediately()
    {
        LanConnectLobbyThemes.SetForTests(LanConnectLobbyThemes.MidnightGlass);
        try
        {
            (LanConnectLobbyOverlay overlay, ISceneRunner runner) = await BuildOverlay();
            using (runner)
            {
                AssertThat(overlay.RoomCardShowsSelectBadgeForTests("room-1")).IsTrue();
                AssertThat(overlay.RoomCardShowsSelectBadgeForTests("room-3")).IsFalse();

                overlay.ClickRoomCardForTests("room-3");
                await runner.AwaitIdleFrame();
                await runner.AwaitIdleFrame();

                AssertThat(overlay.RoomCardShowsSelectBadgeForTests("room-3")).IsTrue();
                AssertThat(overlay.RoomCardShowsSelectBadgeForTests("room-1")).IsFalse();
            }
        }
        finally
        {
            LanConnectLobbyThemes.SetForTests(LanConnectLobbyThemes.Default);
        }
    }

    /// <summary>
    /// Builds a fresh <see cref="LanConnectLobbyOverlay"/> in test mode (mirroring
    /// <c>LobbyOverlayFixture.Create</c> in <c>LanConnectLobbyServerPanelTests</c>: a ready server
    /// chat state seeded with a few messages, five rooms modelled on the design prototypes, first
    /// room selected) inside a capture-ready <see cref="SubViewport"/> (mirroring
    /// <c>LanConnectHudLegibilityScreenshotTests.RenderToImage</c>), then renders one frame to an
    /// <see cref="Image"/>.
    /// </summary>
    private static async Task<(LanConnectLobbyOverlay Overlay, ISceneRunner Runner)> BuildOverlay()
    {
        SubViewport viewport = AutoFree(new SubViewport { Size = ViewportSize, Disable3D = true })!;
        LanConnectLobbyOverlay overlay = new() { Visible = false };
        overlay.SetProcess(false);
        viewport.AddChild(overlay);
        ISceneRunner runner = ISceneRunner.Load(viewport, autoFree: true);
        await runner.AwaitIdleFrame();
        overlay.ConfigureForTests(
            ViewportSize,
            new LanConnectChatChannelState(LanConnectChatChannel.Server),
            Rooms(),
            send: _ => Task.CompletedTask,
            retry: _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        await overlay.RefreshLayoutForTests(ViewportSize);
        await runner.AwaitIdleFrame();
        return (overlay, runner);
    }

    private static async Task<Image> RenderThemedOverlayToImage(bool openThemeMenu = false)
    {
        // Let the NightSky backdrop find the repo's shipped art (the test project has its own res:// root).
        string repoAssets = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "sts2-lan-connect", "assets", "themes"));
        if (Directory.Exists(repoAssets))
        {
            System.Environment.SetEnvironmentVariable("STS2_LAN_THEME_ASSETS_DIR", repoAssets);
        }

        LanConnectChatChannelState serverState = new(LanConnectChatChannel.Server);
        serverState.Apply(new ServerChatInboundEnvelope
        {
            Type = "chat_ready",
            Channel = LanConnectChatChannel.Server,
            ServerChatVersion = 1,
            InstanceId = "theme-screenshot-tests",
            HistoryEpoch = 1,
            ChatEnabled = true,
            EnabledFeatures = new ServerChatEnabledFeatures()
        });
        serverState.SetPresentationForTests(LanConnectServerChatPresentation.Ready);
        serverState.AppendConfirmedForTests("msg-1", "Ironclad", "有人一起打今日挑战吗", 1, false);
        serverState.AppendConfirmedForTests("msg-2", "u0_a269", "我这边延迟有点高，稍等", 2, false);
        serverState.AppendConfirmedForTests("msg-3", "Silent", "好的，等你", 3, true);

        SubViewport viewport = AutoFree(new SubViewport
        {
            Size = ViewportSize,
            Size2DOverride = ViewportSize,
            Size2DOverrideStretch = true,
            Disable3D = true,
            GuiEmbedSubwindows = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
            Snap2DTransformsToPixel = true,
            Snap2DVerticesToPixel = true
        })!;

        LanConnectLobbyOverlay overlay = new() { Visible = false };
        overlay.SetProcess(false);
        viewport.AddChild(overlay);

        using ISceneRunner runner = ISceneRunner.Load(viewport, autoFree: true);
        await runner.AwaitIdleFrame();

        overlay.ConfigureForTests(
            ViewportSize,
            serverState,
            Rooms(),
            send: _ => Task.CompletedTask,
            retry: _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();
        await overlay.RefreshLayoutForTests(ViewportSize);
        await runner.AwaitIdleFrame();
        if (openThemeMenu)
        {
            overlay.OpenThemeMenuForTests();
            await runner.AwaitIdleFrame();
            await runner.AwaitIdleFrame();
        }

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
        if (image.GetWidth() != ViewportSize.X || image.GetHeight() != ViewportSize.Y)
        {
            throw new InvalidOperationException(
                $"capture size {image.GetWidth()}x{image.GetHeight()} != {ViewportSize.X}x{ViewportSize.Y}");
        }
        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }
        return image;
    }

    /// <summary>
    /// Five rooms modelled on the design prototypes: an open room, a room with a punctuation-heavy
    /// Chinese name run by a Chinese host name, and three password-locked rooms. Protocol fields
    /// follow the two shapes <c>LobbyOverlayFixture.Rooms()</c> uses in
    /// <c>LanConnectLobbyServerPanelTests</c> — the pre-0.6 compat-canonical profile with no carrier,
    /// and the 0.6.x tail-canonical profile carried over the RitsuLib sidecar.
    /// </summary>
    private static IReadOnlyList<LobbyRoomSummary> Rooms() =>
    [
        new LobbyRoomSummary
        {
            RoomId = "room-1",
            RoomName = "114514",
            HostPlayerName = "u0_a269",
            CurrentPlayers = 2,
            MaxPlayers = 8,
            GameMode = "standard",
            Version = "0.107.1",
            ModVersion = "0.5.5",
            Status = "waiting",
            RequiresPassword = false,
            ProtocolProfile = LanConnectProtocolProfiles.Extended8p,
            ProtocolProfileV2 = LanConnectProtocolProfileExtensions.CompatCanonical,
            ProtocolSelection = new LobbyProtocolSelectionDto
            {
                Profile = LanConnectProtocolProfileExtensions.CompatCanonical,
                SelectedLanProtocolVersion = 0,
                Carrier = "none",
                MinimumClientVersion = "0.3.0",
                MaxPlayers = 8,
                GameVersion = "0.107.1",
                RitsuLibPresent = false,
                CapabilityDigest = new string('1', 64)
            }
        },
        new LobbyRoomSummary
        {
            RoomId = "room-2",
            RoomName = "！！！咔咔！！！",
            HostPlayerName = "营静炉娱乐",
            CurrentPlayers = 3,
            MaxPlayers = 8,
            GameMode = "daily",
            Version = "0.111.0",
            ModVersion = "0.6.1",
            Status = "waiting",
            RequiresPassword = false,
            ProtocolProfile = LanConnectProtocolProfiles.Extended8p,
            ProtocolProfileV2 = LanConnectProtocolProfileExtensions.TailCanonical,
            ProtocolSelection = new LobbyProtocolSelectionDto
            {
                Profile = LanConnectProtocolProfileExtensions.TailCanonical,
                SelectedLanProtocolVersion = 1,
                Carrier = "ritsulib_sidecar_v1",
                MinimumClientVersion = "0.6.0-alpha.1",
                MaxPlayers = 8,
                GameVersion = "0.111.0",
                RitsuLibPresent = true,
                CapabilityDigest = new string('2', 64)
            }
        },
        new LobbyRoomSummary
        {
            RoomId = "room-3",
            RoomName = "1919810",
            HostPlayerName = "u0_a375",
            CurrentPlayers = 1,
            MaxPlayers = 8,
            GameMode = "standard",
            Version = "0.107.1",
            ModVersion = "0.5.5",
            Status = "waiting",
            RequiresPassword = true,
            ProtocolProfile = LanConnectProtocolProfiles.Extended8p,
            ProtocolProfileV2 = LanConnectProtocolProfileExtensions.CompatCanonical,
            ProtocolSelection = new LobbyProtocolSelectionDto
            {
                Profile = LanConnectProtocolProfileExtensions.CompatCanonical,
                SelectedLanProtocolVersion = 0,
                Carrier = "none",
                MinimumClientVersion = "0.3.0",
                MaxPlayers = 8,
                GameVersion = "0.107.1",
                RitsuLibPresent = false,
                CapabilityDigest = new string('3', 64)
            }
        },
        new LobbyRoomSummary
        {
            RoomId = "room-4",
            RoomName = "bwsx",
            HostPlayerName = "Lenovo",
            CurrentPlayers = 4,
            MaxPlayers = 8,
            GameMode = "daily",
            Version = "0.111.0",
            ModVersion = "0.6.1",
            Status = "waiting",
            RequiresPassword = true,
            ProtocolProfile = LanConnectProtocolProfiles.Extended8p,
            ProtocolProfileV2 = LanConnectProtocolProfileExtensions.TailCanonical,
            ProtocolSelection = new LobbyProtocolSelectionDto
            {
                Profile = LanConnectProtocolProfileExtensions.TailCanonical,
                SelectedLanProtocolVersion = 1,
                Carrier = "ritsulib_sidecar_v1",
                MinimumClientVersion = "0.6.0-alpha.1",
                MaxPlayers = 8,
                GameVersion = "0.111.0",
                RitsuLibPresent = true,
                CapabilityDigest = new string('4', 64)
            }
        },
        new LobbyRoomSummary
        {
            RoomId = "room-5",
            RoomName = "大宝",
            HostPlayerName = "u0_a361",
            CurrentPlayers = 3,
            MaxPlayers = 8,
            GameMode = "standard",
            Version = "0.107.1",
            ModVersion = "0.5.5",
            Status = "waiting",
            RequiresPassword = true,
            ProtocolProfile = LanConnectProtocolProfiles.Extended8p,
            ProtocolProfileV2 = LanConnectProtocolProfileExtensions.CompatCanonical,
            ProtocolSelection = new LobbyProtocolSelectionDto
            {
                Profile = LanConnectProtocolProfileExtensions.CompatCanonical,
                SelectedLanProtocolVersion = 0,
                Carrier = "none",
                MinimumClientVersion = "0.3.0",
                MaxPlayers = 8,
                GameVersion = "0.107.1",
                RitsuLibPresent = false,
                CapabilityDigest = new string('5', 64)
            }
        }
    ];

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
