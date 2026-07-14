using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sts2LanConnect.Scripts;

internal interface ILanConnectServerChatClient : IAsyncDisposable
{
    LanConnectChatChannelState State { get; }

    event Action? StateChanged;

    Task ConnectAsync(
        Uri lobbyBaseUri,
        string playerNetId,
        string playerName,
        CancellationToken cancellationToken = default);

    Task SendTextAsync(string text, CancellationToken cancellationToken = default);

    Task RetryAsync(string clientMessageId, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

internal enum LanConnectLobbyRuntimeChatCoordinatorCheckpoint
{
    BeginAfterSyncLease,
    EnterBeforeRoomMutationLock,
    AppendAfterContextValidation
}

internal sealed class LanConnectLobbyRuntimeChatCoordinator : IAsyncDisposable
{
    private const int LegacyRoomConfirmedMessageLimit = 60;
    private readonly ILanConnectServerChatClient _client;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _lifecycleLock = new();
    private readonly object _roomMutationLock = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly TaskCompletionSource _lifetimeCancellationCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<LanConnectLobbyRuntimeChatCoordinatorCheckpoint>? _checkpoint;
    private Task? _disposeTask;
    private TaskCompletionSource? _syncLeasesDrained;
    private Exception? _lifetimeCancellationError;
    private int _syncLeaseCount;
    private bool _disposed;

    internal LanConnectLobbyRuntimeChatCoordinator(
        ILanConnectServerChatClient client,
        Action<LanConnectLobbyRuntimeChatCoordinatorCheckpoint>? checkpoint = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _lifetimeToken = _lifetimeCancellation.Token;
        _checkpoint = checkpoint;
        State = new LanConnectDualChatState(client.State);
        _client.StateChanged += OnClientStateChanged;
    }

    internal LanConnectDualChatState State { get; }

    internal event Action? StateChanged;

    internal void BeginRoomPending(
        string clientMessageId,
        string senderName,
        string? senderNetId,
        string text,
        DateTimeOffset sentAt)
    {
        RunRoomMutation(
            () => State.Room.BeginPendingText(clientMessageId, senderName, text, senderNetId, sentAt),
            LanConnectLobbyRuntimeChatCoordinatorCheckpoint.BeginAfterSyncLease);
    }

    internal void ConfirmRoomSend(string clientMessageId)
    {
        RunRoomMutation(() => State.Room.MarkLegacySendConfirmed(clientMessageId, LegacyRoomConfirmedMessageLimit));
    }

    internal void FailRoomSend(string clientMessageId, string code, string message)
    {
        RunRoomMutation(() => State.Room.MarkFailed(clientMessageId, code, message));
    }

    internal void AppendRoomConfirmed(
        string roomId,
        string messageId,
        string senderName,
        string? senderNetId,
        string text,
        DateTimeOffset sentAt,
        bool isLocal)
    {
        RunRoomMutation(() =>
        {
            if (!string.Equals(State.ActiveRoomId, roomId, StringComparison.Ordinal))
            {
                return;
            }
            _checkpoint?.Invoke(LanConnectLobbyRuntimeChatCoordinatorCheckpoint.AppendAfterContextValidation);
            foreach (ServerChatMessageState existing in State.Room.Messages)
            {
                if (string.Equals(existing.MessageId, messageId, StringComparison.Ordinal) ||
                    string.Equals(existing.ClientMessageId, messageId, StringComparison.Ordinal))
                {
                    return;
                }
            }

            State.Room.AppendLegacyConfirmed(
                messageId,
                senderName,
                senderNetId,
                text,
                sentAt,
                isLocal,
                LegacyRoomConfirmedMessageLimit);
        });
    }

    internal void EnterRoom(string roomId)
    {
        RunRoomMutation(
            () => State.EnterRoom(roomId),
            LanConnectLobbyRuntimeChatCoordinatorCheckpoint.EnterBeforeRoomMutationLock);
    }

    internal void LeaveRoom()
    {
        RunRoomMutation(State.LeaveRoom);
    }

    internal Task ConnectServerAsync(
        Uri lobbyBaseUri,
        string playerNetId,
        string playerName,
        CancellationToken cancellationToken = default) =>
        RunClientOperationAsync(
            token => _client.ConnectAsync(lobbyBaseUri, playerNetId, playerName, token),
            cancellationToken);

    internal Task SendServerAsync(string text, CancellationToken cancellationToken = default) =>
        RunClientOperationAsync(token => _client.SendTextAsync(text, token), cancellationToken);

    internal Task RetryServerAsync(string clientMessageId, CancellationToken cancellationToken = default) =>
        RunClientOperationAsync(token => _client.RetryAsync(clientMessageId, token), cancellationToken);

    internal Task StopServerAsync(CancellationToken cancellationToken = default) =>
        RunClientOperationAsync(token => _client.StopAsync(token), cancellationToken);

    public ValueTask DisposeAsync()
    {
        bool beginCancellation = false;
        Task disposeTask;
        lock (_lifecycleLock)
        {
            if (_disposeTask == null)
            {
                _disposed = true;
                Task syncDrain = GetSyncLeaseDrainTaskLocked();
                _disposeTask = DisposeCoreAsync(_lifetimeCancellationCompleted.Task, syncDrain);
                beginCancellation = true;
            }
            disposeTask = _disposeTask;
        }

        if (beginCancellation)
        {
            try
            {
                _lifetimeCancellation.Cancel();
            }
            catch (Exception ex)
            {
                _lifetimeCancellationError = ex;
            }
            finally
            {
                _lifetimeCancellationCompleted.TrySetResult();
            }
        }

        return new ValueTask(disposeTask);
    }

    private async Task RunClientOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using CancellationTokenSource operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        await _operationGate.WaitAsync(operationCancellation.Token);
        try
        {
            ThrowIfDisposed();
            await operation(operationCancellation.Token);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task DisposeCoreAsync(Task cancellationCompleted, Task syncDrain)
    {
        await cancellationCompleted;
        await syncDrain;
        await _operationGate.WaitAsync();
        try
        {
            _client.StateChanged -= OnClientStateChanged;
            await _client.DisposeAsync();
        }
        finally
        {
            _lifetimeCancellation.Dispose();
            _operationGate.Release();
        }

        if (_lifetimeCancellationError != null)
        {
            throw _lifetimeCancellationError;
        }
    }

    private void OnClientStateChanged()
    {
        if (!TryAcquireSyncLease())
        {
            return;
        }

        try
        {
            StateChanged?.Invoke();
        }
        finally
        {
            ReleaseSyncLease();
        }
    }

    private void RunRoomMutation(
        Action mutation,
        LanConnectLobbyRuntimeChatCoordinatorCheckpoint? checkpointBeforeRoomLock = null)
    {
        AcquireSyncLease();
        try
        {
            if (checkpointBeforeRoomLock.HasValue)
            {
                _checkpoint?.Invoke(checkpointBeforeRoomLock.Value);
            }

            bool changed;
            lock (_roomMutationLock)
            {
                string? activeRoomBefore = State.ActiveRoomId;
                long roomRevisionBefore = State.Room.Revision;
                mutation();
                changed = !string.Equals(activeRoomBefore, State.ActiveRoomId, StringComparison.Ordinal) ||
                          roomRevisionBefore != State.Room.Revision;
            }

            if (changed)
            {
                StateChanged?.Invoke();
            }
        }
        finally
        {
            ReleaseSyncLease();
        }
    }

    private void AcquireSyncLease()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _syncLeaseCount++;
        }
    }

    private bool TryAcquireSyncLease()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return false;
            }

            _syncLeaseCount++;
            return true;
        }
    }

    private void ReleaseSyncLease()
    {
        TaskCompletionSource? drained = null;
        lock (_lifecycleLock)
        {
            _syncLeaseCount--;
            if (_syncLeaseCount == 0 && _syncLeasesDrained != null)
            {
                drained = _syncLeasesDrained;
                _syncLeasesDrained = null;
            }
        }

        drained?.TrySetResult();
    }

    private Task GetSyncLeaseDrainTaskLocked()
    {
        if (_syncLeaseCount == 0)
        {
            return Task.CompletedTask;
        }

        _syncLeasesDrained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _syncLeasesDrained.Task;
    }

    private void ThrowIfDisposed()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
