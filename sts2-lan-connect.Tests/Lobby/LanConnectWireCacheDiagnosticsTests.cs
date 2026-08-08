using System.Text;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectWireCacheDiagnosticsTests
{
    [Fact]
    public void Capture_failure_does_not_throw_from_startup_logging()
    {
        LanConnectWireCacheCaptureCache cache = new(
            static () => throw new MissingMemberException("renamed cache member"));

        Exception? exception = Record.Exception(() =>
            LanConnectWireCacheDiagnostics.LogStartupSnapshot(
                cache,
                static _ => throw new Xunit.Sdk.XunitException("success logger should not run"),
                static _ => throw new InvalidOperationException("logger also failed")));

        Assert.Null(exception);
    }

    [Fact]
    public void Debug_report_keeps_other_sections_and_marks_signature_unavailable()
    {
        // Build depends on the sts2 game assembly and cannot run end-to-end in this project.
        // This does not cover "Build must never propagate a capture failure"; that belongs in
        // sts2-lan-connect.GdUnitTests if the coverage is added later.
        LanConnectWireCacheCaptureCache cache = new(
            static () => throw new MissingMemberException("required cache table was renamed"));
        StringBuilder builder = new();
        builder.AppendLine("STS2 LAN Connect Client Debug Report");

        LanConnectDebugReport.AppendWireCacheDiagnostics(builder, cache.GetCurrentResult());
        builder.AppendLine("loaded_mod_inventory_count: 2");
        builder.AppendLine("Recent Relevant Client Log Lines");

        string report = builder.ToString();
        Assert.Contains("wire_cache_signature_v1: <unavailable>", report);
        Assert.Contains(
            "wire_cache_signature_v1_unavailable_reason: MissingMemberException: required cache table was renamed",
            report);
        Assert.Contains("loaded_mod_inventory_count: 2", report);
        Assert.Contains("Recent Relevant Client Log Lines", report);
    }

    [Fact]
    public void Capture_failure_is_cached_and_logged_once()
    {
        int captureCount = 0;
        int failureLogCount = 0;
        LanConnectWireCacheCaptureCache cache = new(
            () =>
            {
                captureCount++;
                throw new InvalidOperationException("persistent failure");
            },
            logFailure: _ => failureLogCount++);

        LanConnectWireCacheCaptureResult first = cache.GetCurrentResult();
        LanConnectWireCacheCaptureResult second = cache.GetCurrentResult();

        Assert.Same(first, second);
        Assert.Equal(LanConnectWireCacheCaptureStatus.Unavailable, first.Status);
        Assert.Equal(1, captureCount);
        Assert.Equal(1, failureLogCount);
    }

    [Fact]
    public void Comparison_distinguishes_unavailable_from_mismatch()
    {
        LanConnectWireCacheCaptureResult unavailable = LanConnectWireCacheCaptureResult.Unavailable(
            new InvalidOperationException("cache not initialized"));
        LanConnectWireCacheCaptureResult available = LanConnectWireCacheCaptureResult.Available(
            CreateSnapshot("wcv1:actual"));

        LanConnectWireCacheSignatureComparison unavailableComparison =
            unavailable.CompareSignature("wcv1:expected");
        LanConnectWireCacheSignatureComparison mismatchComparison =
            available.CompareSignature("wcv1:expected");

        Assert.Equal(
            LanConnectWireCacheSignatureComparisonStatus.Unavailable,
            unavailableComparison.Status);
        Assert.Equal(
            LanConnectWireCacheSignatureComparisonStatus.Mismatch,
            mismatchComparison.Status);
        Assert.Throws<LanConnectWireCacheUnavailableException>(unavailable.GetRequiredSnapshot);
        Assert.Same(available.Snapshot, available.GetRequiredSnapshot());
    }

    private static LanConnectWireCacheSnapshot CreateSnapshot(string signature) =>
        new(signature, 2, 3, 4, 5, 10, 20, 30, 40, 123u);
}
