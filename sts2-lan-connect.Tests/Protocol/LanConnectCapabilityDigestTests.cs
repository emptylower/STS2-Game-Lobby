using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Protocol;

public sealed class LanConnectCapabilityDigestTests
{
    [Fact]
    public void Matches_cross_runtime_golden_vectors()
    {
        foreach (DigestVector vector in ReadVectors())
        {
            LanConnectProtocolProfile profile = LanConnectProtocolProfileExtensions.ParseCanonical(vector.Profile);
            bool ritsuPresent = vector.Offer.RitsuLibPresent;
            LanConnectProtocolSelection selection = new(
                profile,
                profile == LanConnectProtocolProfile.Compat4x5V1 ? 0 : 1,
                profile == LanConnectProtocolProfile.Compat4x5V1
                    ? LanConnectProtocolCarrier.None
                    : LanConnectProtocolCarrier.NativeBusV1,
                profile == LanConnectProtocolProfile.Compat4x5V1 ? "0.3.0" : "0.6.1-alpha.1",
                vector.Policy.MaxPlayers,
                vector.Policy.GameVersion,
                vector.Policy.WireCacheSignatureV1,
                ritsuPresent,
                string.Empty);

            Assert.Equal(vector.ExpectedDigest, LanConnectCapabilityDigest.Compute(selection));
        }
    }

    [Fact]
    public void Carrier_changes_the_digest()
    {
        LanConnectProtocolSelection legacy = new(
            LanConnectProtocolProfile.TailV1,
            1,
            LanConnectProtocolCarrier.LegacyTailV1,
            "0.6.1-alpha.1",
            8,
            "0.110.1",
            "aabb",
            false,
            string.Empty);
        LanConnectProtocolSelection native = legacy with { Carrier = LanConnectProtocolCarrier.NativeBusV1 };

        Assert.NotEqual(
            LanConnectCapabilityDigest.Compute(legacy),
            LanConnectCapabilityDigest.Compute(native));
    }

    [Fact]
    public void Legacy_carrier_selections_are_rejected_with_the_stable_upgrade_error()
    {
        LobbyProtocolSelectionDto legacyCarrier = CreateResponseDto(
            "tail_v1",
            1,
            "standalone_tail_v1",
            "0.6.0-alpha.1",
            new string('a', 64));
        LanConnectProtocolOffer offer = new(1, 1, "0.6.1-alpha.1", false, false);

        LanConnectProtocolException exception = Assert.Throws<LanConnectProtocolException>(
            () => legacyCarrier.ToValidatedValue(offer));
        Assert.Equal("lan_legacy_carrier_unsupported", exception.Failure.Code);
    }

    [Fact]
    public void Native_carrier_selection_validates_with_the_exact_digest()
    {
        LanConnectProtocolSelection template = new(
            LanConnectProtocolProfile.TailV1,
            1,
            LanConnectProtocolCarrier.NativeBusV1,
            "0.6.1-alpha.1",
            8,
            "v0.111.0",
            "wcv1:D5-qRxko7ywoZJWzaOM9Q49NNOWP1Jr2qXc_Nk204uU",
            false,
            string.Empty);
        string expectedDigest = LanConnectCapabilityDigest.Compute(template);
        LanConnectProtocolOffer offer = new(1, 1, "0.6.1-alpha.1", false, false);

        LobbyProtocolSelectionDto dto = CreateResponseDto(
            "tail_v1",
            1,
            "native_bus_v1",
            "0.6.1-alpha.1",
            expectedDigest);
        Assert.Equal(expectedDigest, dto.ToValidatedValue(offer).CapabilityDigest);

        // 大小写翻转的签名必须得出不同摘要并被拒绝。
        LanConnectProtocolSelection flipped = template with
        {
            WireCacheSignature = template.WireCacheSignature!.ToUpperInvariant()
        };
        string flippedDigest = LanConnectCapabilityDigest.Compute(flipped);
        Assert.NotEqual(expectedDigest, flippedDigest);
        LobbyProtocolSelectionDto mismatched = CreateResponseDto(
            "tail_v1",
            1,
            "native_bus_v1",
            "0.6.1-alpha.1",
            flippedDigest);
        Assert.Throws<LanConnectProtocolException>(() => mismatched.ToValidatedValue(offer));
    }

    private static LobbyProtocolSelectionDto CreateResponseDto(
        string profile,
        int protocolVersion,
        string carrier,
        string minimumClientVersion,
        string capabilityDigest)
    {
        string json = $$"""
        {
          "profile": "{{profile}}",
          "selectedLanProtocolVersion": {{protocolVersion}},
          "carrier": "{{carrier}}",
          "maxPlayers": 8,
          "minimumClientVersion": "{{minimumClientVersion}}",
          "gameVersion": "v0.111.0",
          "wireCacheSignature": "wcv1:D5-qRxko7ywoZJWzaOM9Q49NNOWP1Jr2qXc_Nk204uU",
          "ritsuLibPresent": false,
          "capabilityDigest": "{{capabilityDigest}}"
        }
        """;
        return JsonSerializer.Deserialize<LobbyProtocolSelectionDto>(json, LanConnectJson.Options)!;
    }

    private static IReadOnlyList<DigestVector> ReadVectors()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
        {
            directory = directory.Parent;
        }

        string path = Path.Combine(
            directory?.FullName ?? throw new DirectoryNotFoundException(),
            "test-fixtures", "protocol", "v0.6", "capability-digest-v1.json");
        return JsonSerializer.Deserialize<List<DigestVector>>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private sealed record DigestVector(
        string Name,
        string Profile,
        DigestOfferVector Offer,
        DigestPolicyVector Policy,
        string ExpectedDigest);

    private sealed record DigestOfferVector(
        int LanProtocolMin,
        int LanProtocolMax,
        string ClientVersion,
        bool RitsuLibPresent,
        bool LegacySidecarAvailable);

    private sealed record DigestPolicyVector(
        string Profile,
        int LanProtocolMin,
        int LanProtocolMax,
        int MaxPlayers,
        string GameVersion,
        string WireCacheSignatureV1);
}
