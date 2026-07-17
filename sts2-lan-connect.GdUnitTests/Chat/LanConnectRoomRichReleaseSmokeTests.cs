using System.Security.Cryptography;
using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRoomRichReleaseSmokeTests
{
    private const string FixedFontSha256 =
        "2F39C30EBB8559D9A9DBD0AFE7CC8445680E1003073CC8FD685ECEF58F0ED336";

    [TestCase]
    public async Task Release_fixture_locks_font_entities_combat_delivery_and_matrix()
    {
        string previousLocale = TranslationServer.GetLocale();
        try
        {
            TranslationServer.SetLocale("zh-CN");
            using ChatUiFixture fixture = await ChatUiFixture.Create(new Vector2I(1920, 1080), 1f);
            await fixture.ShowRichMatrixOnly();

            AssertThat(LanConnectRoomRichScreenshotTests.Cases.Count).IsEqual(12);
            AssertThat(fixture.PhysicalSize).IsEqual(new Vector2I(1920, 1080));
            AssertThat(fixture.LogicalViewportSize).IsEqual(new Vector2I(1920, 1080));
            AssertThat(fixture.RichMessageRuns().Length).IsEqual(13);
            AssertThat(fixture.RichMessageRuns().Count(run => run is not Label)).IsEqual(12);
            AssertThat(fixture.RichMessageRuns().Count(run => run.HasMeta("lan_connect_resolved_combat")))
                .IsEqual(2);
            AssertThat(fixture.RichMessageRuns().Count(run => run.HasMeta("lan_connect_combat_fallback")))
                .IsEqual(1);
            AssertThat(fixture.RichVisibleText).Contains("Watcher");
            AssertThat(fixture.RichVisibleText.Contains("prototype:stale", StringComparison.Ordinal)).IsFalse();

            AssertThat(fixture.RoomServerState.Messages.Count(message =>
                message.Delivery == ServerChatDeliveryState.Pending)).IsEqual(1);
            AssertThat(fixture.RoomServerState.Messages.Count(message =>
                message.Delivery == ServerChatDeliveryState.Failed)).IsEqual(1);
            AssertThat(fixture.RoomServerState.Messages.Count(message =>
                message.Delivery == ServerChatDeliveryState.DeliveryUnknown)).IsEqual(1);
            AssertThat(fixture.RoomUnreadBadgeText.Length).IsGreaterEqual(2);
            AssertThat(fixture.ServerUnreadBadgeText).IsEqual("0");

            string fontPath = ProjectSettings.GlobalizePath(
                "res://TestAssets/Fonts/ark-pixel-10px-proportional-zh_cn.otf");
            string licensePath = ProjectSettings.GlobalizePath(
                "res://TestAssets/Fonts/ARK_PIXEL_FONT_OFL.txt");
            AssertThat(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fontPath))))
                .IsEqual(FixedFontSha256);
            AssertThat(File.ReadAllText(licensePath)).Contains("SIL OPEN FONT LICENSE Version 1.1");
            AssertThat(fixture.RichPanel.GetThemeDefaultFont()).IsNotNull();
        }
        finally
        {
            TranslationServer.SetLocale(previousLocale);
        }
    }
}
