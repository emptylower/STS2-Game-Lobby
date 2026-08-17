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
                    : ritsuPresent
                        ? LanConnectProtocolCarrier.RitsuLibSidecarV1
                        : LanConnectProtocolCarrier.StandaloneTailV1,
                profile == LanConnectProtocolProfile.Compat4x5V1 ? "0.3.0" : "0.6.0-alpha.1",
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
        LanConnectProtocolSelection standalone = new(
            LanConnectProtocolProfile.TailV1,
            1,
            LanConnectProtocolCarrier.StandaloneTailV1,
            "0.6.0-alpha.1",
            8,
            "0.110.1",
            "aabb",
            false,
            string.Empty);
        LanConnectProtocolSelection sidecar = standalone with
        {
            Carrier = LanConnectProtocolCarrier.RitsuLibSidecarV1,
            RitsuLibPresent = true
        };

        Assert.NotEqual(
            LanConnectCapabilityDigest.Compute(standalone),
            LanConnectCapabilityDigest.Compute(sidecar));
    }

    [Theory]
    [InlineData(
        "compat_4_5_v1",
        0,
        "none",
        "0.3.0",
        "64d24fb637bf6e55ec8f486c4dc9ebaec554defe68f54be0da5a73ffd71bf8cd")]
    [InlineData(
        "tail_v1",
        1,
        "standalone_tail_v1",
        "0.6.0-alpha.1",
        "0acaf09bc69874c88a01bac7c5cdb7b464331da7ebad5b78f516a2d83036a0fd")]
    public void Accepts_alpha4_legacy_lowercase_digest_response(
        string profile,
        int protocolVersion,
        string carrier,
        string minimumClientVersion,
        string capabilityDigest)
    {
        LobbyProtocolSelectionDto dto = CreateResponseDto(
            profile,
            protocolVersion,
            carrier,
            minimumClientVersion,
            capabilityDigest);
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.5", false, false);

        LanConnectProtocolSelection selection = dto.ToValidatedValue(offer);

        Assert.Equal(profile, selection.Profile.ToCanonical());
        Assert.Equal(capabilityDigest, selection.CapabilityDigest);
    }

    [Theory]
    [InlineData(
        "compat_4_5_v1",
        0,
        "none",
        "0.3.0",
        "9aceddb15255e3b925ea37d438614a71317d9d86c5dd66df2ffada45355643b3")]
    [InlineData(
        "tail_v1",
        1,
        "standalone_tail_v1",
        "0.6.0-alpha.1",
        "9c6b6fdb3aebd6ddb8b27ddb2fe69106291ed4e7efbe4acb1fb71930b0002789")]
    public void Validates_case_sensitive_create_response(
        string profile,
        int protocolVersion,
        string carrier,
        string minimumClientVersion,
        string capabilityDigest)
    {
        LobbyProtocolSelectionDto dto = CreateResponseDto(
            profile,
            protocolVersion,
            carrier,
            minimumClientVersion,
            capabilityDigest);
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.5", false, false);

        LanConnectProtocolSelection selection = dto.ToValidatedValue(offer);

        Assert.Equal(profile, selection.Profile.ToCanonical());
        Assert.Equal(capabilityDigest, selection.CapabilityDigest);
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
        string path = Path.Combine(FindRepositoryRoot(), "test-fixtures", "protocol", "v0.6", "capability-digest-v1.json");
        return JsonSerializer.Deserialize<List<DigestVector>>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record DigestVector(
        string Name,
        string Profile,
        DigestOffer Offer,
        DigestPolicy Policy,
        string ExpectedDigest);

    private sealed record DigestOffer(bool RitsuLibPresent);

    private sealed record DigestPolicy(int MaxPlayers, string GameVersion, string? WireCacheSignatureV1);
}
