namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectHostedRoomMetadata
{
    public string RoomName { get; init; } = string.Empty;

    public string? Password { get; init; }

    public string GameMode { get; init; } = LanConnectConstants.DefaultGameMode;

    public string PublishSource { get; init; } = "manual";

    public string? SaveKey { get; init; }

    public LobbySavedRunInfo? SavedRun { get; init; }

    public string ProtocolProfile { get; init; } = LanConnectProtocolProfiles.Extended8p;
}
