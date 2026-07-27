using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectLanResumeCodeTests
{
    [Fact]
    public void RoundTripPreservesSavedSlotIdentity()
    {
        string code = LanConnectLanResumeCode.Encode("save-key", 11797420990750824289UL, "铁甲战士", "朋友");

        Assert.True(LanConnectLanResumeCode.TryDecode(code, out LanConnectLanResumePayload payload, out string error), error);
        Assert.Equal("save-key", payload.SaveKey);
        Assert.Equal(11797420990750824289UL, payload.PlayerNetId);
        Assert.Equal("铁甲战士", payload.CharacterName);
        Assert.Equal("朋友", payload.PlayerName);
    }

    [Fact]
    public void DecoderAcceptsCodeInsideLabeledLine()
    {
        string code = LanConnectLanResumeCode.Encode("save-key", 42UL, "Silent", "P2");

        Assert.True(LanConnectLanResumeCode.TryDecode($"Silent / P2: {code}\n说明", out LanConnectLanResumePayload payload, out _));
        Assert.Equal(42UL, payload.PlayerNetId);
    }

    [Fact]
    public void DecoderRejectsMultipleSlotCodesInsteadOfGuessing()
    {
        string first = LanConnectLanResumeCode.Encode("save-key", 42UL, "Silent", "P2");
        string second = LanConnectLanResumeCode.Encode("save-key", 43UL, "Ironclad", "P3");

        Assert.False(LanConnectLanResumeCode.TryDecode($"P2: {first}\nP3: {second}", out _, out string error));
        Assert.Contains("多条", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("STS2LANRESUME:")]
    [InlineData("STS2LANRESUME:not-base64")]
    [InlineData("unrelated")]
    public void DecoderRejectsInvalidCodes(string input)
    {
        Assert.False(LanConnectLanResumeCode.TryDecode(input, out _, out string error));
        Assert.NotEmpty(error);
    }
}
