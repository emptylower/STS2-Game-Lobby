using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal enum LanConnectModPreflightDecision
{
    Synchronize,
    Cancel,
    ContinueRelaxed,
    RestartScheduled
}

internal enum LanConnectModPreflightJoinOutcome
{
    TicketIssued,
    GameVersionMismatch,
    ModSynchronizationRequired,
    Canceled,
    RestartScheduled
}

internal sealed record LanConnectModPreflightJoinRequest
{
    public LobbyRoomSummary Room { get; init; } = new();
    public string PlayerName { get; init; } = string.Empty;
    public string? Password { get; init; }
    public string GameVersion { get; init; } = string.Empty;
    public string ModVersion { get; init; } = string.Empty;
    public List<string> ModList { get; init; } = [];
    public List<LobbyModDescriptor> LocalMods { get; init; } = [];
    public string? DesiredSavePlayerNetId { get; init; }
    public string? PlayerNetId { get; init; }

    public static LanConnectModPreflightJoinRequest CreateCurrent(
        LobbyRoomSummary room,
        string? password,
        string? desiredSavePlayerNetId = null,
        string? playerNetId = null) => new()
    {
        Room = room,
        PlayerName = LanConnectConfig.GetEffectivePlayerDisplayName(),
        Password = string.IsNullOrWhiteSpace(password) ? null : password,
        GameVersion = LanConnectBuildInfo.GetGameVersion(),
        ModVersion = LanConnectBuildInfo.GetModVersion(),
        ModList = LanConnectBuildInfo.GetModList(),
        LocalMods = LanConnectBuildInfo.GetModInventory(),
        DesiredSavePlayerNetId = string.IsNullOrWhiteSpace(desiredSavePlayerNetId) ? null : desiredSavePlayerNetId,
        PlayerNetId = string.IsNullOrWhiteSpace(playerNetId) ? null : playerNetId
    };
}

internal sealed record LanConnectModPreflightJoinResult
{
    public LanConnectModPreflightJoinOutcome Outcome { get; init; }
    public LobbyJoinRoomResponse? JoinResponse { get; init; }
    public LobbyModPreflightResponse? Preflight { get; init; }
    public string? Message { get; init; }
}

internal interface ILanConnectModPreflightPorts
{
    Task<LobbyProbeResponse> GetProbeAsync(CancellationToken cancellationToken);

    Task<LobbyModPreflightResponse> PreflightAsync(
        string roomId,
        LobbyModPreflightRequest request,
        CancellationToken cancellationToken);

    Task<LanConnectModPreflightDecision> DecideAsync(
        LobbyRoomSummary room,
        LobbyModPreflightResponse response,
        CancellationToken cancellationToken);

    Task<LobbyJoinRoomResponse> RequestJoinTicketAsync(
        string roomId,
        LobbyJoinRoomRequest request,
        CancellationToken cancellationToken);
}

internal sealed class LanConnectModPreflightCoordinator
{
    private readonly ILanConnectModPreflightPorts _ports;

    public LanConnectModPreflightCoordinator(ILanConnectModPreflightPorts ports)
    {
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
    }

    public async Task<LanConnectModPreflightJoinResult> JoinAsync(
        LanConnectModPreflightJoinRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        string? roomVersionMismatch = LanConnectLobbyManagedJoinFlow.GetGameVersionMismatchMessage(
            request.Room.Version,
            request.GameVersion);
        if (roomVersionMismatch != null)
        {
            return new LanConnectModPreflightJoinResult
            {
                Outcome = LanConnectModPreflightJoinOutcome.GameVersionMismatch,
                Message = roomVersionMismatch
            };
        }

        LobbyProbeResponse probe = await _ports.GetProbeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        LanConnectModSyncCapabilityState capability = LanConnectModSyncCapabilities.Resolve(probe.Capabilities);
        if (!capability.Supported)
        {
            return await RequestTicketAsync(request, preflight: null, cancellationToken);
        }

        LobbyModPreflightResponse preflight = await _ports.PreflightAsync(
            request.Room.RoomId,
            new LobbyModPreflightRequest
            {
                PlayerName = request.PlayerName,
                Password = request.Password,
                GameVersion = request.GameVersion,
                ModSyncProtocolVersion = LanConnectModSyncCapabilities.ProtocolVersion,
                LocalMods = request.LocalMods
            },
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!preflight.Enabled ||
            preflight.ProtocolVersion != LanConnectModSyncCapabilities.ProtocolVersion)
        {
            return await RequestTicketAsync(request, preflight, cancellationToken);
        }

        if (!preflight.GameVersion.ExactMatch)
        {
            string message = LanConnectLobbyManagedJoinFlow.GetGameVersionMismatchMessage(
                preflight.GameVersion.Host,
                preflight.GameVersion.Local)
                ?? "游戏版本不匹配，无法加入该房间。";
            return new LanConnectModPreflightJoinResult
            {
                Outcome = LanConnectModPreflightJoinOutcome.GameVersionMismatch,
                Preflight = preflight,
                Message = message
            };
        }

        if (!preflight.HostInventoryAvailable)
        {
            return await RequestTicketAsync(request, preflight, cancellationToken);
        }

        if (!preflight.HasModDifferences)
        {
            return await RequestTicketAsync(request, preflight, cancellationToken);
        }

        LanConnectModPreflightDecision decision = await _ports.DecideAsync(
            request.Room,
            preflight,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (decision == LanConnectModPreflightDecision.ContinueRelaxed && preflight.CanContinueRelaxed)
        {
            return await RequestTicketAsync(request, preflight, cancellationToken);
        }

        return new LanConnectModPreflightJoinResult
        {
            Outcome = decision switch
            {
                LanConnectModPreflightDecision.Cancel => LanConnectModPreflightJoinOutcome.Canceled,
                LanConnectModPreflightDecision.RestartScheduled => LanConnectModPreflightJoinOutcome.RestartScheduled,
                _ => LanConnectModPreflightJoinOutcome.ModSynchronizationRequired
            },
            Preflight = preflight
        };
    }

    private async Task<LanConnectModPreflightJoinResult> RequestTicketAsync(
        LanConnectModPreflightJoinRequest request,
        LobbyModPreflightResponse? preflight,
        CancellationToken cancellationToken)
    {
        LobbyJoinRoomResponse joinResponse = await _ports.RequestJoinTicketAsync(
            request.Room.RoomId,
            new LobbyJoinRoomRequest
            {
                PlayerName = request.PlayerName,
                Password = request.Password,
                Version = request.GameVersion,
                ModVersion = request.ModVersion,
                ModList = request.ModList,
                DesiredSavePlayerNetId = request.DesiredSavePlayerNetId,
                PlayerNetId = request.PlayerNetId
            },
            cancellationToken);
        return new LanConnectModPreflightJoinResult
        {
            Outcome = LanConnectModPreflightJoinOutcome.TicketIssued,
            JoinResponse = joinResponse,
            Preflight = preflight
        };
    }
}

internal sealed class LanConnectModPreflightApiPorts : ILanConnectModPreflightPorts
{
    private readonly LobbyApiClient _apiClient;
    private readonly Func<LobbyRoomSummary, LobbyModPreflightResponse, CancellationToken, Task<LanConnectModPreflightDecision>> _decision;

    public LanConnectModPreflightApiPorts(
        LobbyApiClient apiClient,
        Func<LobbyRoomSummary, LobbyModPreflightResponse, CancellationToken, Task<LanConnectModPreflightDecision>> decision)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _decision = decision ?? throw new ArgumentNullException(nameof(decision));
    }

    public Task<LobbyProbeResponse> GetProbeAsync(CancellationToken cancellationToken) =>
        _apiClient.GetProbeAsync(cancellationToken);

    public Task<LobbyModPreflightResponse> PreflightAsync(
        string roomId,
        LobbyModPreflightRequest request,
        CancellationToken cancellationToken) =>
        _apiClient.ModPreflightAsync(roomId, request, cancellationToken);

    public Task<LanConnectModPreflightDecision> DecideAsync(
        LobbyRoomSummary room,
        LobbyModPreflightResponse response,
        CancellationToken cancellationToken) =>
        _decision(room, response, cancellationToken);

    public Task<LobbyJoinRoomResponse> RequestJoinTicketAsync(
        string roomId,
        LobbyJoinRoomRequest request,
        CancellationToken cancellationToken) =>
        _apiClient.JoinRoomAsync(roomId, request, cancellationToken);
}
