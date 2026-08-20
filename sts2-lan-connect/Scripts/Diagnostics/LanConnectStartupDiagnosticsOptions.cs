namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectStartupDiagnosticsOptions
{
    public required string DiagnosticsRoot { get; init; }

    public Func<DateTimeOffset> UtcNow { get; init; } = static () => DateTimeOffset.UtcNow;

    public Func<string> SessionIdFactory { get; init; } = static () => Guid.NewGuid().ToString("N")[..12];

    public Action<string> MirrorInfo { get; init; } = static _ => { };

    public Action<string> Warn { get; init; } = static _ => { };

    public bool CaptureArtifacts { get; init; } = true;

    public bool EnableHarmonyDiagnostics { get; init; } = true;

    public int MaxSessions { get; init; } = 3;

    public long MaxTotalBytes { get; init; } = 64L * 1024L * 1024L;
}
