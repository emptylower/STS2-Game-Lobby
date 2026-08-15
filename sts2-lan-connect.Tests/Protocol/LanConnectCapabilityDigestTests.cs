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
