using System;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectPendingSaveBindingCoordinator
{
    private readonly LanConnectPendingSaveBindingIntentState _state = new();
    private readonly Func<LoadedSave?> _loadCurrentSave;
    private readonly Func<LoadedSave, PersistenceRequest, bool> _persist;
    private readonly Func<object?>? _getCurrentNetService;
    private readonly Func<string, LanConnectSavedRoomBinding?>? _readBinding;

    public LanConnectPendingSaveBindingCoordinator(
        Func<LoadedSave?> loadCurrentSave,
        Func<LoadedSave, PersistenceRequest, bool> persist,
        Func<object?>? getCurrentNetService = null,
        Func<string, LanConnectSavedRoomBinding?>? readBinding = null)
    {
        _loadCurrentSave = loadCurrentSave;
        _persist = persist;
        _getCurrentNetService = getCurrentNetService;
        _readBinding = readBinding;
    }

    public bool AttachHostedRoom(
        string roomName,
        string? password,
        string gameMode,
        string? saveKey,
        LanConnectProtocolSelection frozenSelection,
        object? netService = null)
    {
        return _state.Capture(roomName, password, gameMode, saveKey, frozenSelection, netService) != null;
    }

    public void DifferentHostedRoomWillAttach() => _state.Discard();

    public void AttachJoinedClient() => _state.Discard();

    public void HostedSessionTornDown() => _state.PreserveAcrossHostedSessionTeardown();

    public void HostedFlowEnded()
    {
        _state.Discard();
    }

    public bool CompleteActivePersist(string saveKey)
    {
        if (!_state.TryGet(out LanConnectPendingSaveBindingIntentState.BindingIntent intent)
            || (!string.IsNullOrWhiteSpace(intent.SaveKey)
                && !string.Equals(intent.SaveKey, saveKey, StringComparison.Ordinal)))
        {
            return false;
        }

        return _state.Complete(intent);
    }

    public PendingPersistResult PersistForCurrentSave(string source)
    {
        if (!_state.TryGet(out LanConnectPendingSaveBindingIntentState.BindingIntent intent))
        {
            return PendingPersistResult.NoIntent;
        }

        LoadedSave? loadedSave = _loadCurrentSave();
        if (loadedSave == null)
        {
            return PendingPersistResult.SaveUnavailable;
        }

        if (!string.IsNullOrWhiteSpace(intent.SaveKey)
            && !string.Equals(intent.SaveKey, loadedSave.SaveKey, StringComparison.Ordinal))
        {
            _state.Discard();
            return PendingPersistResult.RefusedDifferentSave;
        }

        if (string.IsNullOrWhiteSpace(intent.SaveKey))
        {
            if (intent.NetService == null
                || _getCurrentNetService == null
                || !ReferenceEquals(_getCurrentNetService(), intent.NetService))
            {
                _state.Discard();
                return PendingPersistResult.RefusedDifferentNetService;
            }

            LanConnectSavedRoomBinding? existing = _readBinding?.Invoke(loadedSave.SaveKey);
            if (existing != null
                && LanConnectContinueRunPublishDecision.Decide(
                    existing.HostChannel,
                    existing.SchemaVersion) == LanConnectContinueRunPublishDecisionKind.SkipLanOrigin)
            {
                _state.Discard();
                return PendingPersistResult.RefusedExplicitLanBinding;
            }
        }

        bool persisted = _persist(
            loadedSave,
            new PersistenceRequest(
                intent.RoomName,
                intent.Password,
                intent.GameMode,
                LanConnectHostChannels.Lobby,
                LanConnectSavedRoomBinding.CurrentSchemaVersion,
                $"{source}:pending_lobby_intent",
                intent.FrozenSelection));
        if (!persisted)
        {
            return PendingPersistResult.SkippedByPersistence;
        }

        _state.Complete(intent);
        return PendingPersistResult.Persisted;
    }

    internal sealed record LoadedSave(string SaveKey, object Value);

    internal sealed record PersistenceRequest(
        string RoomName,
        string? Password,
        string GameMode,
        string HostChannel,
        int SchemaVersion,
        string Source,
        LanConnectProtocolSelection FrozenSelection);

    internal enum PendingPersistResult
    {
        NoIntent,
        SaveUnavailable,
        SkippedByPersistence,
        Persisted,
        RefusedDifferentSave,
        RefusedDifferentNetService,
        RefusedExplicitLanBinding
    }
}
