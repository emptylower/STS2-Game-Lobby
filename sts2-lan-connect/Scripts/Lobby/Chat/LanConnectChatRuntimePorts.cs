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

    internal void SetRoomChatEnabled(bool enabled)
    {
        RunRoomMutation(() => State.Room.SetChatEnabled(enabled));
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

    internal void ClearServerForContextChange()
    {
        AcquireSyncLease();
        try
        {
            long revisionBefore = State.Server.Revision;
            State.Server.ClearForContextChange();
            if (State.Server.Revision != revisionBefore)
            {
                StateChanged?.Invoke();
            }
        }
        finally
        {
            ReleaseSyncLease();
        }
    }

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

internal interface ILanConnectRoomLifecycle
{
    bool HasActiveRoom { get; }

    Task LeaveActiveRoomAsync(CancellationToken cancellationToken);
}

internal interface ILanConnectServerAddressStore
{
    // Called under the switch generation commit lock; implementations must be bounded and non-reentrant.
    void Persist(string baseUrl, CancellationToken cancellationToken);
}

internal interface ILanConnectServerSwitchChat
{
    Task StopAsync(CancellationToken cancellationToken);

    void ClearForContextChange();

    Task ConnectAsync(
        Uri lobbyBaseUri,
        string playerNetId,
        string playerName,
        CancellationToken cancellationToken);
}

internal sealed class LanConnectRotatingServerChatPort : ILanConnectServerSwitchChat, IAsyncDisposable
{
    private readonly Func<ILanConnectServerChatClient> _clientFactory;
    private readonly object _lifecycleLock = new();
    private LanConnectLobbyRuntimeChatCoordinator _current;
    private Task? _disposeTask;
    private bool _disposed;

    internal LanConnectRotatingServerChatPort(
        ILanConnectServerChatClient initialClient,
        Func<ILanConnectServerChatClient> clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _current = new LanConnectLobbyRuntimeChatCoordinator(
            initialClient ?? throw new ArgumentNullException(nameof(initialClient)));
        _current.StateChanged += OnCurrentStateChanged;
    }

    internal LanConnectLobbyRuntimeChatCoordinator Current
    {
        get
        {
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _current;
            }
        }
    }

    internal event Action? StateChanged;

    public Task StopAsync(CancellationToken cancellationToken) =>
        Current.StopServerAsync(cancellationToken);

    public void ClearForContextChange() => Current.ClearServerForContextChange();

    public async Task ConnectAsync(
        Uri lobbyBaseUri,
        string playerNetId,
        string playerName,
        CancellationToken cancellationToken)
    {
        LanConnectLobbyRuntimeChatCoordinator previous = Current;
        ILanConnectServerChatClient replacementClient = _clientFactory() ??
            throw new InvalidOperationException("Server chat client factory returned null.");
        LanConnectLobbyRuntimeChatCoordinator replacement;
        try
        {
            replacement = new LanConnectLobbyRuntimeChatCoordinator(replacementClient);
        }
        catch
        {
            await replacementClient.DisposeAsync();
            throw;
        }
        replacement.StateChanged += OnCurrentStateChanged;
        Exception? installError = null;
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                installError = new ObjectDisposedException(nameof(LanConnectRotatingServerChatPort));
            }
            else if (!ReferenceEquals(_current, previous))
            {
                installError = new InvalidOperationException(
                    "Server chat owner changed outside the switch coordinator.");
            }
            else
            {
                previous.StateChanged -= OnCurrentStateChanged;
                _current = replacement;
            }
        }
        if (installError != null)
        {
            replacement.StateChanged -= OnCurrentStateChanged;
            await replacement.DisposeAsync();
            throw installError;
        }

        Exception? previousDisposeError = null;
        try
        {
            await previous.DisposeAsync();
        }
        catch (Exception ex)
        {
            previousDisposeError = ex;
        }
        StateChanged?.Invoke();
        if (previousDisposeError != null)
        {
            throw previousDisposeError;
        }
        cancellationToken.ThrowIfCancellationRequested();
        await replacement.ConnectServerAsync(lobbyBaseUri, playerNetId, playerName, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        bool beginDispose = false;
        TaskCompletionSource disposeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task disposeTask;
        lock (_lifecycleLock)
        {
            if (_disposeTask == null)
            {
                _disposed = true;
                LanConnectLobbyRuntimeChatCoordinator current = _current;
                current.StateChanged -= OnCurrentStateChanged;
                _disposeTask = DisposeCoreAsync(disposeStarted.Task, current);
                beginDispose = true;
            }
            disposeTask = _disposeTask;
        }

        if (beginDispose)
        {
            disposeStarted.TrySetResult();
        }
        return new ValueTask(disposeTask);
    }

    private void OnCurrentStateChanged() => StateChanged?.Invoke();

    private static async Task DisposeCoreAsync(
        Task disposeStarted,
        LanConnectLobbyRuntimeChatCoordinator current)
    {
        await disposeStarted;
        await current.DisposeAsync();
    }
}

internal enum LanConnectServerSwitchCoordinatorCheckpoint
{
    BeforeGenerationRegistration,
    AfterGenerationRegistration
}

internal static class LanConnectLobbyServerAddress
{
    internal static Uri NormalizeUri(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? parsed) ||
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Lobby server must be an absolute HTTP or HTTPS URL.",
                parameterName);
        }

        return new Uri(parsed.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
    }

    internal static string NormalizeAuthority(string value, string parameterName) =>
        NormalizeUri(value, parameterName).GetLeftPart(UriPartial.Authority);
}

internal sealed class LanConnectServerContextLease : IDisposable
{
    private LanConnectServerContextHolder? _owner;

    internal LanConnectServerContextLease(LanConnectServerContextHolder owner, CancellationToken token)
    {
        _owner = owner;
        Token = token;
    }

    internal CancellationToken Token { get; }

    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseLease();
}

internal sealed class LanConnectServerContextHolder
{
    private readonly object _lifecycleLock = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _token;
    private int _referenceCount = 1;
    private bool _acceptingLeases = true;
    private bool _disposed;

    internal LanConnectServerContextHolder()
    {
        _token = _cancellation.Token;
    }

    internal int ReferenceCount
    {
        get
        {
            lock (_lifecycleLock)
            {
                return _referenceCount;
            }
        }
    }

    internal bool IsDisposed
    {
        get
        {
            lock (_lifecycleLock)
            {
                return _disposed;
            }
        }
    }

    internal LanConnectServerContextLease AcquireLease()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(!_acceptingLeases || _disposed, this);
            _referenceCount++;
            return new LanConnectServerContextLease(this, _token);
        }
    }

    internal void Cancel() => _cancellation.Cancel();

    internal void ReleaseOwner()
    {
        lock (_lifecycleLock)
        {
            if (!_acceptingLeases)
            {
                throw new InvalidOperationException("Server context owner was already released.");
            }
            _acceptingLeases = false;
        }
        ReleaseReference();
    }

    internal void ReleaseLease() => ReleaseReference();

    private void ReleaseReference()
    {
        bool dispose = false;
        lock (_lifecycleLock)
        {
            if (_referenceCount <= 0)
            {
                throw new InvalidOperationException("Server context reference count underflowed.");
            }
            _referenceCount--;
            if (_referenceCount == 0)
            {
                _disposed = true;
                dispose = true;
            }
        }
        if (dispose)
        {
            _cancellation.Dispose();
        }
    }
}

internal sealed class LanConnectServerSwitchCoordinator : IAsyncDisposable
{
    private readonly ILanConnectRoomLifecycle _room;
    private readonly ILanConnectServerSwitchChat _chat;
    private readonly ILanConnectServerAddressStore _addressStore;
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private readonly object _generationLock = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly TaskCompletionSource _lifetimeCancellationCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<LanConnectServerSwitchCoordinatorCheckpoint>? _checkpoint;
    private SwitchGeneration? _activeSwitch;
    private LanConnectServerContextHolder _currentServerContext;
    private Task? _disposeTask;
    private TaskCompletionSource? _switchLeasesDrained;
    private Exception? _lifetimeCancellationError;
    private Exception? _contextCancellationError;
    private int _switchLeaseCount;
    private bool _disposed;

    internal LanConnectServerSwitchCoordinator(
        ILanConnectRoomLifecycle room,
        ILanConnectServerSwitchChat chat,
        ILanConnectServerAddressStore addressStore,
        Action<LanConnectServerSwitchCoordinatorCheckpoint>? checkpoint = null)
    {
        _room = room ?? throw new ArgumentNullException(nameof(room));
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _addressStore = addressStore ?? throw new ArgumentNullException(nameof(addressStore));
        _checkpoint = checkpoint;
        _lifetimeToken = _lifetimeCancellation.Token;
        _currentServerContext = new LanConnectServerContextHolder();
    }

    internal LanConnectServerContextLease AcquireCurrentServerContext()
    {
        lock (_generationLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _currentServerContext.AcquireLease();
        }
    }

    internal bool IsSwitchInProgress
    {
        get
        {
            lock (_generationLock)
            {
                return _activeSwitch != null;
            }
        }
    }

    internal async Task SwitchAsync(
        string baseUrl,
        string playerNetId,
        string playerName,
        CancellationToken cancellationToken = default)
    {
        using LanConnectServerContextLease context = await SwitchWithContextAsync(
            baseUrl,
            playerNetId,
            playerName,
            cancellationToken);
    }

    internal Task<LanConnectServerContextLease> SwitchWithContextAsync(
        string baseUrl,
        string playerNetId,
        string playerName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Uri normalized = LanConnectLobbyServerAddress.NormalizeUri(baseUrl, nameof(baseUrl));
        return SwitchCoreAsync(normalized, playerNetId, playerName, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        bool beginCancellation = false;
        Task disposeTask;
        LanConnectServerContextHolder? contextToRetire = null;
        lock (_generationLock)
        {
            if (_disposeTask == null)
            {
                _disposed = true;
                Task switchDrain = GetSwitchLeaseDrainTaskLocked();
                _disposeTask = DisposeCoreAsync(_lifetimeCancellationCompleted.Task, switchDrain);
                contextToRetire = _currentServerContext;
                beginCancellation = true;
            }
            disposeTask = _disposeTask;
        }

        if (beginCancellation)
        {
            RetireServerContext(contextToRetire!);
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

    private async Task<LanConnectServerContextLease> SwitchCoreAsync(
        Uri normalized,
        string playerNetId,
        string playerName,
        CancellationToken callerCancellation)
    {
        SwitchGeneration ownGeneration;
        SwitchGeneration? superseded;
        IDisposable? supersededCancellationLease;
        LanConnectServerContextHolder previousServerContext;
        _checkpoint?.Invoke(LanConnectServerSwitchCoordinatorCheckpoint.BeforeGenerationRegistration);
        lock (_generationLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            LanConnectServerContextHolder ownServerContext = new();
            ownGeneration = new SwitchGeneration(
                callerCancellation,
                _lifetimeToken,
                ownServerContext);
            superseded = _activeSwitch;
            supersededCancellationLease = superseded?.AcquireCancellationLease();
            previousServerContext = _currentServerContext;
            _currentServerContext = ownServerContext;
            _activeSwitch = ownGeneration;
            _switchLeaseCount++;
        }

        CancellationToken ownToken = ownGeneration.Token;
        bool gateAcquired = false;
        try
        {
            RetireServerContext(previousServerContext);
            _checkpoint?.Invoke(LanConnectServerSwitchCoordinatorCheckpoint.AfterGenerationRegistration);
            CancelSuperseded(superseded, supersededCancellationLease);
            supersededCancellationLease = null;
            await _switchGate.WaitAsync(ownToken);
            gateAcquired = true;
            ownToken.ThrowIfCancellationRequested();

            if (_room.HasActiveRoom)
            {
                await _room.LeaveActiveRoomAsync(ownToken);
            }

            ownToken.ThrowIfCancellationRequested();
            await _chat.StopAsync(ownToken);
            ownToken.ThrowIfCancellationRequested();
            _chat.ClearForContextChange();
            ownToken.ThrowIfCancellationRequested();

            lock (_generationLock)
            {
                ThrowIfSupersededLocked(ownGeneration);
                _addressStore.Persist(normalized.GetLeftPart(UriPartial.Authority), ownToken);
            }

            ownToken.ThrowIfCancellationRequested();
            await _chat.ConnectAsync(normalized, playerNetId, playerName, ownToken);
        }
        catch (OperationCanceledException) when (
            !callerCancellation.IsCancellationRequested && ownToken.IsCancellationRequested)
        {
            // A newer switch owns the user-visible result.
        }
        catch
        {
            CancelServerContext(ownGeneration.ServerContext);
            ownGeneration.ResultContextLease.Dispose();
            throw;
        }
        finally
        {
            supersededCancellationLease?.Dispose();
            if (gateAcquired)
            {
                _switchGate.Release();
            }

            lock (_generationLock)
            {
                if (ReferenceEquals(_activeSwitch, ownGeneration))
                {
                    _activeSwitch = null;
                }
            }
            ownGeneration.Complete();
            ReleaseSwitchLease();
        }

        return ownGeneration.ResultContextLease;
    }

    private async Task DisposeCoreAsync(Task cancellationCompleted, Task switchDrain)
    {
        await cancellationCompleted;
        await switchDrain;
        _lifetimeCancellation.Dispose();
        _switchGate.Dispose();
        if (_lifetimeCancellationError != null && _contextCancellationError != null)
        {
            throw new AggregateException(_contextCancellationError, _lifetimeCancellationError);
        }
        if (_contextCancellationError != null)
        {
            throw _contextCancellationError;
        }
        if (_lifetimeCancellationError != null)
        {
            throw _lifetimeCancellationError;
        }
    }

    private void RetireServerContext(LanConnectServerContextHolder context)
    {
        try
        {
            context.Cancel();
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref _contextCancellationError, ex, null);
        }
        finally
        {
            context.ReleaseOwner();
        }
    }

    private void CancelServerContext(LanConnectServerContextHolder context)
    {
        try
        {
            context.Cancel();
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref _contextCancellationError, ex, null);
        }
    }

    private Task GetSwitchLeaseDrainTaskLocked()
    {
        if (_switchLeaseCount == 0)
        {
            return Task.CompletedTask;
        }

        _switchLeasesDrained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _switchLeasesDrained.Task;
    }

    private void ReleaseSwitchLease()
    {
        TaskCompletionSource? drained = null;
        lock (_generationLock)
        {
            _switchLeaseCount--;
            if (_switchLeaseCount == 0 && _switchLeasesDrained != null)
            {
                drained = _switchLeasesDrained;
                _switchLeasesDrained = null;
            }
        }

        drained?.TrySetResult();
    }

    private void ThrowIfSupersededLocked(SwitchGeneration generation)
    {
        if (!ReferenceEquals(_activeSwitch, generation))
        {
            throw new OperationCanceledException(generation.Token);
        }
        generation.Token.ThrowIfCancellationRequested();
    }

    private static void CancelSuperseded(
        SwitchGeneration? superseded,
        IDisposable? cancellationLease)
    {
        try
        {
            superseded?.Cancel();
        }
        catch (AggregateException)
        {
            // Cancellation callback failures belong to the superseded operation.
        }
        finally
        {
            cancellationLease?.Dispose();
        }
    }

    private sealed class SwitchGeneration
    {
        private readonly object _lifecycleLock = new();
        private readonly CancellationTokenSource _cancellation;
        private int _cancellationLeaseCount;
        private bool _completed;

        internal SwitchGeneration(
            CancellationToken callerCancellation,
            CancellationToken ownerCancellation,
            LanConnectServerContextHolder serverContext)
        {
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                callerCancellation,
                ownerCancellation);
            Token = _cancellation.Token;
            ServerContext = serverContext;
            ResultContextLease = serverContext.AcquireLease();
        }

        internal CancellationToken Token { get; }

        internal LanConnectServerContextHolder ServerContext { get; }

        internal LanConnectServerContextLease ResultContextLease { get; }

        internal IDisposable AcquireCancellationLease()
        {
            lock (_lifecycleLock)
            {
                if (_completed)
                {
                    throw new InvalidOperationException("Cannot cancel a completed server switch generation.");
                }
                _cancellationLeaseCount++;
            }
            return new CancellationLease(this);
        }

        internal void Cancel() => _cancellation.Cancel();

        internal void Complete()
        {
            bool dispose;
            lock (_lifecycleLock)
            {
                _completed = true;
                dispose = _cancellationLeaseCount == 0;
            }
            if (dispose)
            {
                _cancellation.Dispose();
            }
        }

        private void ReleaseCancellationLease()
        {
            bool dispose;
            lock (_lifecycleLock)
            {
                _cancellationLeaseCount--;
                dispose = _completed && _cancellationLeaseCount == 0;
            }
            if (dispose)
            {
                _cancellation.Dispose();
            }
        }

        private sealed class CancellationLease(SwitchGeneration owner) : IDisposable
        {
            private SwitchGeneration? _owner = owner;

            public void Dispose()
            {
                Interlocked.Exchange(ref _owner, null)?.ReleaseCancellationLease();
            }
        }
    }
}
