using System;

namespace Sts2LanConnect.Scripts;

internal sealed class LobbyDirectoryServerEntry
{
    public string Id { get; set; } = string.Empty;

    public string SourceType { get; set; } = "community";

    public string DisplayName { get; set; } = string.Empty;

    public string RegionLabel { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string WsUrl { get; set; } = string.Empty;

    public string? BandwidthProbeUrl { get; set; }

    public string ListingState { get; set; } = "approved";

    public string RuntimeState { get; set; } = "offline";

    public string QualityGrade { get; set; } = "unknown";

    public DateTimeOffset? LastProbeAt { get; set; }

    public double? LastProbeRttMs { get; set; }

    public double? LastBandwidthMbps { get; set; }

    public string? FailureReason { get; set; }

    public string? OperatorName { get; set; }

    public string? Contact { get; set; }

    public string? Notes { get; set; }
}

internal sealed class LobbyDirectorySubmissionRequest
{
    public string DisplayName { get; set; } = string.Empty;

    public string RegionLabel { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string? WsUrl { get; set; }

    public string? BandwidthProbeUrl { get; set; }

    public string OperatorName { get; set; } = string.Empty;

    public string Contact { get; set; } = string.Empty;

    public string? Notes { get; set; }
}
