using System;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectCurrentSaveBindingWriter
{
    private readonly Func<LoadResult> _loadCurrentSave;
    private readonly Func<object?> _getCurrentNetService;
    private readonly Func<LoadedSave, PersistenceRequest, bool> _persist;

    public LanConnectCurrentSaveBindingWriter(
        Func<LoadResult> loadCurrentSave,
        Func<object?> getCurrentNetService,
        Func<LoadedSave, PersistenceRequest, bool> persist)
    {
        _loadCurrentSave = loadCurrentSave;
        _getCurrentNetService = getCurrentNetService;
        _persist = persist;
    }

    public PersistOutcome Persist(BindingTarget target, string source)
    {
        if (target.IsClosing)
        {
            return new PersistOutcome(PersistResult.RefusedClosing, null, string.Empty);
        }

        LoadResult loaded = _loadCurrentSave();
        if (!loaded.Success || loaded.Value == null || string.IsNullOrWhiteSpace(loaded.SaveKey))
        {
            return new PersistOutcome(PersistResult.SaveUnavailable, null, loaded.FailureReason);
        }

        if (!string.IsNullOrWhiteSpace(target.ExpectedSaveKey))
        {
            if (!string.Equals(target.ExpectedSaveKey, loaded.SaveKey, StringComparison.Ordinal))
            {
                return new PersistOutcome(PersistResult.RefusedDifferentSave, loaded.SaveKey, string.Empty);
            }
        }
        else if (!ReferenceEquals(_getCurrentNetService(), target.NetService))
        {
            return new PersistOutcome(PersistResult.RefusedDifferentNetService, loaded.SaveKey, string.Empty);
        }

        LoadedSave save = new(loaded.SaveKey, loaded.Value);
        bool persisted = _persist(
            save,
            new PersistenceRequest(
                target.RoomName,
                target.Password,
                target.GameMode,
                target.HostChannel,
                source));
        return new PersistOutcome(
            persisted ? PersistResult.Persisted : PersistResult.SkippedByPersistence,
            loaded.SaveKey,
            string.Empty);
    }

    internal sealed record LoadResult(
        bool Success,
        string? SaveKey,
        object? Value,
        string FailureReason);

    internal sealed record LoadedSave(string SaveKey, object Value);

    internal sealed record BindingTarget(
        object NetService,
        bool IsClosing,
        string? ExpectedSaveKey,
        string RoomName,
        string? Password,
        string GameMode,
        string HostChannel);

    internal sealed record PersistenceRequest(
        string RoomName,
        string? Password,
        string GameMode,
        string HostChannel,
        string Source);

    internal sealed record PersistOutcome(
        PersistResult Result,
        string? SaveKey,
        string FailureReason);

    internal enum PersistResult
    {
        Persisted,
        SaveUnavailable,
        SkippedByPersistence,
        RefusedClosing,
        RefusedDifferentSave,
        RefusedDifferentNetService
    }
}
