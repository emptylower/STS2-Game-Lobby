using System.Text.Json.Serialization;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectPendingModSyncJoin
{
    public const int CurrentVersion = 1;
    public static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(15);

    [JsonPropertyName("version")]
    [JsonPropertyOrder(0)]
    public int Version { get; init; } = CurrentVersion;

    [JsonPropertyName("serverBaseUrl")]
    [JsonPropertyOrder(1)]
    public string ServerBaseUrl { get; init; } = string.Empty;

    [JsonPropertyName("roomId")]
    [JsonPropertyOrder(2)]
    public string RoomId { get; init; } = string.Empty;

    [JsonPropertyName("roomName")]
    [JsonPropertyOrder(3)]
    public string RoomName { get; init; } = string.Empty;

    [JsonPropertyName("desiredSavePlayerNetId")]
    [JsonPropertyOrder(4)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DesiredSavePlayerNetId { get; init; }

    [JsonPropertyName("createdAtUtc")]
    [JsonPropertyOrder(5)]
    public DateTimeOffset CreatedAtUtc { get; init; }

    [JsonPropertyName("expiresAtUtc")]
    [JsonPropertyOrder(6)]
    public DateTimeOffset ExpiresAtUtc { get; init; }
}

internal enum LanConnectPendingModSyncResumeOutcome
{
    NoPending,
    Busy,
    RoomMissing,
    PublicJoinCompleted,
    PasswordPromptCompleted,
    Canceled
}

internal interface ILanConnectPendingModSyncResumePorts
{
    Task RestoreServerAsync(string serverBaseUrl, CancellationToken cancellationToken);

    Task<IReadOnlyList<LobbyRoomSummary>> GetRoomsAsync(CancellationToken cancellationToken);

    Task<bool> RepreflightPublicJoinAsync(
        LobbyRoomSummary room,
        string? desiredSavePlayerNetId,
        CancellationToken cancellationToken);

    Task<bool> PromptPasswordAndRepreflightAsync(
        LobbyRoomSummary room,
        string? desiredSavePlayerNetId,
        CancellationToken cancellationToken);
}

internal sealed class LanConnectPendingModSyncResumeCoordinator
{
    private readonly LanConnectPendingModSyncJoinStore _store;
    private readonly ILanConnectPendingModSyncResumePorts _ports;
    private int _inFlight;

    public LanConnectPendingModSyncResumeCoordinator(
        LanConnectPendingModSyncJoinStore store,
        ILanConnectPendingModSyncResumePorts ports)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
    }

    public async Task<LanConnectPendingModSyncResumeOutcome> ResumeAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            return LanConnectPendingModSyncResumeOutcome.Busy;
        }

        try
        {
            LanConnectPendingModSyncJoin? pending = _store.Load();
            if (pending == null)
            {
                return LanConnectPendingModSyncResumeOutcome.NoPending;
            }

            await _ports.RestoreServerAsync(pending.ServerBaseUrl, cancellationToken);
            IReadOnlyList<LobbyRoomSummary> rooms = await _ports.GetRoomsAsync(cancellationToken);
            LobbyRoomSummary? room = rooms.FirstOrDefault(candidate =>
                string.Equals(candidate.RoomId, pending.RoomId, StringComparison.Ordinal));
            if (room == null)
            {
                _store.TryClear(pending);
                return LanConnectPendingModSyncResumeOutcome.RoomMissing;
            }

            bool completed = room.RequiresPassword
                ? await _ports.PromptPasswordAndRepreflightAsync(
                    room,
                    pending.DesiredSavePlayerNetId,
                    cancellationToken)
                : await _ports.RepreflightPublicJoinAsync(
                    room,
                    pending.DesiredSavePlayerNetId,
                    cancellationToken);
            _store.TryClear(pending);
            if (!completed)
            {
                return LanConnectPendingModSyncResumeOutcome.Canceled;
            }
            return room.RequiresPassword
                ? LanConnectPendingModSyncResumeOutcome.PasswordPromptCompleted
                : LanConnectPendingModSyncResumeOutcome.PublicJoinCompleted;
        }
        finally
        {
            Volatile.Write(ref _inFlight, 0);
        }
    }
}
