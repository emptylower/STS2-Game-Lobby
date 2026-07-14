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

internal sealed class LanConnectLobbyRuntimeChatCoordinator : IAsyncDisposable
{
    private readonly ILanConnectServerChatClient _client;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _lifecycleLock = new();
    private Task? _disposeTask;
    private bool _disposed;

    internal LanConnectLobbyRuntimeChatCoordinator(ILanConnectServerChatClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        State = new LanConnectDualChatState(client.State);
        _client.StateChanged += OnClientStateChanged;
    }

    internal LanConnectDualChatState State { get; }

    internal event Action? StateChanged;

    internal void BeginRoomPending(string clientMessageId, string senderName, string text)
    {
        ThrowIfDisposed();
        MutateRoom(() => State.Room.BeginPendingText(clientMessageId, senderName, text));
    }

    internal void ConfirmRoomSend(string clientMessageId)
    {
        ThrowIfDisposed();
        MutateRoom(() => State.Room.MarkLegacySendConfirmed(clientMessageId));
    }

    internal void FailRoomSend(string clientMessageId, string code, string message)
    {
        ThrowIfDisposed();
        MutateRoom(() => State.Room.MarkFailed(clientMessageId, code, message));
    }

    internal void AppendRoomConfirmed(
        string roomId,
        string messageId,
        string senderName,
        string text,
        bool isLocal)
    {
        ThrowIfDisposed();
        if (!string.Equals(State.ActiveRoomId, roomId, StringComparison.Ordinal))
        {
            return;
        }
        foreach (ServerChatMessageState existing in State.Room.Messages)
        {
            if (string.Equals(existing.MessageId, messageId, StringComparison.Ordinal) ||
                string.Equals(existing.ClientMessageId, messageId, StringComparison.Ordinal))
            {
                return;
            }
        }

        MutateRoom(() => State.Room.AppendConfirmedForTests(
            messageId,
            senderName,
            text,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            isLocal));
    }

    internal void EnterRoom(string roomId)
    {
        ThrowIfDisposed();
        string? activeBefore = State.ActiveRoomId;
        long roomRevisionBefore = State.Room.Revision;
        State.EnterRoom(roomId);
        RaiseIfRoomChanged(activeBefore, roomRevisionBefore);
    }

    internal void LeaveRoom()
    {
        ThrowIfDisposed();
        string? activeBefore = State.ActiveRoomId;
        long roomRevisionBefore = State.Room.Revision;
        State.LeaveRoom();
        RaiseIfRoomChanged(activeBefore, roomRevisionBefore);
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
        lock (_lifecycleLock)
        {
            _disposed = true;
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task RunClientOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await operation(cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            _client.StateChanged -= OnClientStateChanged;
            await _client.DisposeAsync();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void OnClientStateChanged()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }
        }

        StateChanged?.Invoke();
    }

    private void RaiseIfRoomChanged(string? activeRoomBefore, long roomRevisionBefore)
    {
        if (!string.Equals(activeRoomBefore, State.ActiveRoomId, StringComparison.Ordinal) ||
            roomRevisionBefore != State.Room.Revision)
        {
            StateChanged?.Invoke();
        }
    }

    private void MutateRoom(Action mutation)
    {
        long revisionBefore = State.Room.Revision;
        mutation();
        if (State.Room.Revision != revisionBefore)
        {
            StateChanged?.Invoke();
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
