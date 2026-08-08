using System;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectPendingSaveBindingCoordinator
{
    private readonly LanConnectPendingSaveBindingIntentState _state = new();
    private readonly Func<LoadedSave?> _loadCurrentSave;
    private readonly Func<LoadedSave, PersistenceRequest, bool> _persist;

    public LanConnectPendingSaveBindingCoordinator(
        Func<LoadedSave?> loadCurrentSave,
        Func<LoadedSave, PersistenceRequest, bool> persist)
    {
        _loadCurrentSave = loadCurrentSave;
        _persist = persist;
    }

    public bool AttachHostedRoom(string roomName, string? password, string gameMode, string? saveKey)
    {
        return _state.Capture(roomName, password, gameMode, saveKey) != null;
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
            || string.IsNullOrWhiteSpace(intent.SaveKey)
            || !string.Equals(intent.SaveKey, saveKey, StringComparison.Ordinal))
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

        if (string.IsNullOrWhiteSpace(intent.SaveKey))
        {
            _state.Discard();
            return PendingPersistResult.RefusedMissingKey;
        }

        LoadedSave? loadedSave = _loadCurrentSave();
        if (loadedSave == null)
        {
            return PendingPersistResult.SaveUnavailable;
        }

        if (!string.Equals(intent.SaveKey, loadedSave.SaveKey, StringComparison.Ordinal))
        {
            _state.Discard();
            return PendingPersistResult.RefusedDifferentSave;
        }

        bool persisted = _persist(
            loadedSave,
            new PersistenceRequest(
                intent.RoomName,
                intent.Password,
                intent.GameMode,
                LanConnectHostChannels.Lobby,
                LanConnectSavedRoomBinding.CurrentSchemaVersion,
                $"{source}:pending_lobby_intent"));
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
        string Source);

    internal enum PendingPersistResult
    {
        NoIntent,
        SaveUnavailable,
        SkippedByPersistence,
        Persisted,
        RefusedMissingKey,
        RefusedDifferentSave
    }
}
