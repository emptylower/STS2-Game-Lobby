using Godot;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectChatNameColorTests
{
    [Theory]
    [InlineData("Toadpole")]
    [InlineData("狂战士")]
    [InlineData("a")]
    [InlineData("")]
    public void Same_sender_always_gets_the_same_colour(string sender)
    {
        Color first = LanConnectChatNameColor.ForSender(sender, isLocal: false);
        Color second = LanConnectChatNameColor.ForSender(sender, isLocal: false);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("Toadpole")]
    [InlineData("狂战士")]
    [InlineData("")]
    [InlineData("a-very-long-display-name-that-someone-actually-used")]
    public void Every_colour_stays_inside_the_readable_band(string sender)
    {
        Color colour = LanConnectChatNameColor.ForSender(sender, isLocal: false);

        float lightness = colour.V * (1f - (colour.S / 2f));
        Assert.InRange(lightness, 0.60f, 0.84f);
        Assert.InRange(colour.S, 0.40f, 0.80f);
    }

    [Fact]
    public void Local_player_is_brighter_than_the_same_name_remote()
    {
        Color remote = LanConnectChatNameColor.ForSender("Toadpole", isLocal: false);
        Color local = LanConnectChatNameColor.ForSender("Toadpole", isLocal: true);

        Assert.NotEqual(remote, local);
        Assert.True(local.V > remote.V);
    }

    [Fact]
    public void Distinct_senders_are_visually_separable()
    {
        string[] senders = ["Alice", "Bob", "Carol", "Dave", "Erin", "Frank"];
        List<Color> colours = senders.Select(s => LanConnectChatNameColor.ForSender(s, isLocal: false)).ToList();

        for (int i = 0; i < colours.Count; i++)
        {
            for (int j = i + 1; j < colours.Count; j++)
            {
                Assert.True(
                    HueDistance(colours[i].H, colours[j].H) > 0.04f,
                    $"{senders[i]} and {senders[j]} are too close in hue");
            }
        }
    }

    [Fact]
    public void Null_sender_returns_a_stable_fallback_colour_without_throwing()
    {
        Color first = LanConnectChatNameColor.ForSender(null, isLocal: false);
        Color second = LanConnectChatNameColor.ForSender(null, isLocal: false);

        Assert.Equal(first, second);
    }

    private static float HueDistance(float a, float b)
    {
        float raw = Math.Abs(a - b);
        return Math.Min(raw, 1f - raw);
    }
}
