using System.Text;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectChatDraftLimiterTests
{
    [Fact]
    public void Rejects_middle_insert_at_limit_without_dropping_tail_or_jumping_caret()
    {
        string accepted = new('a', 500);
        string candidate = accepted.Insert(250, "😀");

        LanConnectChatDraftLimitResult result = LanConnectChatDraftLimiter.Limit(
            accepted,
            candidate,
            candidateCaretUtf16Offset: 252,
            maxScalars: 500);

        Assert.Equal(accepted, result.Text);
        Assert.Equal(250, result.CaretUtf16Offset);
        Assert.Equal(500, LanConnectChatTextProtocol.CountUnicodeScalars(result.Text));
        AssertValidUtf16(result.Text);
    }

    [Fact]
    public void Accepts_only_whole_unicode_scalar_at_boundary()
    {
        string accepted = new('a', 499);
        string candidate = accepted.Insert(250, "😀x");

        LanConnectChatDraftLimitResult result = LanConnectChatDraftLimiter.Limit(
            accepted,
            candidate,
            candidateCaretUtf16Offset: 253,
            maxScalars: 500);

        Assert.Equal(accepted.Insert(250, "😀"), result.Text);
        Assert.Equal(252, result.CaretUtf16Offset);
        Assert.Equal(500, LanConnectChatTextProtocol.CountUnicodeScalars(result.Text));
        AssertValidUtf16(result.Text);
    }

    private static void AssertValidUtf16(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            Assert.NotEqual(Rune.ReplacementChar, rune);
        }
    }
}
