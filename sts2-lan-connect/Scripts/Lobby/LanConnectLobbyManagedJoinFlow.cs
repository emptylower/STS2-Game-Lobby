using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectLobbyManagedJoinFlow
{
    private TaskCompletionSource<InitialGameInfoMessage>? _connectCompletion;
    private TaskCompletionSource<ClientRejoinResponseMessage>? _rejoinCompletion;
    private TaskCompletionSource<ClientLoadJoinResponseMessage>? _loadJoinCompletion;
    private TaskCompletionSource<ClientLobbyJoinResponseMessage>? _joinCompletion;
    private readonly MegaCrit.Sts2.Core.Logging.Logger _logger = new("LanConnectManagedJoinFlow", LogType.Network);
    private readonly bool _relaxedCompatibility;
    private string? _protocolMismatchSummary;
    private List<string>? _detectedMissingModsOnLocal;
    private List<string>? _detectedMissingModsOnHost;
    private bool _protocolMismatchEscalated;

    public LanConnectLobbyManagedJoinFlow(string compatibilityProfile)
    {
        _relaxedCompatibility = string.Equals(
            compatibilityProfile,
            "test_relaxed",
            StringComparison.OrdinalIgnoreCase);
    }

    public NetClientGameService? NetService { get; private set; }

    public CancellationTokenSource CancelToken { get; } = new();

    public async Task<JoinResult> BeginAsync(IClientConnectionInitializer initializer, SceneTree sceneTree)
    {
        MegaCrit.Sts2.Core.Logging.Logger.logLevelTypeMap[LogType.Network] = LogLevel.Debug;
        MegaCrit.Sts2.Core.Logging.Logger.logLevelTypeMap[LogType.Actions] = LogLevel.VeryDebug;
        MegaCrit.Sts2.Core.Logging.Logger.logLevelTypeMap[LogType.GameSync] = LogLevel.VeryDebug;

        if (_connectCompletion != null)
        {
            throw new InvalidOperationException("LanConnectLobbyManagedJoinFlow can only be used once.");
        }

        _logger.Info($"Beginning managed join with initializer {initializer} relaxedCompatibility={_relaxedCompatibility}");
        NetService = new NetClientGameService();
        CancelToken.Token.Register(Cancel);

        CancellationTokenSource updateLoopCancelSource = new();
        _ = TaskHelper.RunSafely(NetServiceUpdateLoop(updateLoopCancelSource, sceneTree));

        try
        {
            NetService.RegisterMessageHandler<InitialGameInfoMessage>(HandleInitialGameInfoMessage);
            NetService.RegisterMessageHandler<ClientLobbyJoinResponseMessage>(HandleJoinResponseMessage);
            NetService.RegisterMessageHandler<ClientLoadJoinResponseMessage>(HandleLoadJoinResponseMessage);
            NetService.RegisterMessageHandler<ClientRejoinResponseMessage>(HandleRejoinResponseMessage);
            NetService.Disconnected += OnDisconnected;

            _connectCompletion = new TaskCompletionSource<InitialGameInfoMessage>();
            NetErrorInfo? connectError = await initializer.Connect(NetService, CancelToken.Token);
            if (connectError.HasValue)
            {
                _logger.Info($"Connection failed before handshake: {connectError}");
                throw new ClientConnectionFailedException("Could not connect", connectError.Value);
            }

            _logger.Info("Initializer connection completed, awaiting initial game info message.");
            InitialGameInfoMessage initialMessage = await _connectCompletion.Task;
            ValidateInitialMessage(initialMessage);

            RunSessionState sessionState = initialMessage.sessionState;
            _logger.Info(
                $"Got initial game info message. Version={initialMessage.version} Hash={initialMessage.idDatabaseHash} Mode={initialMessage.gameMode} State={sessionState}");

            return sessionState switch
            {
                RunSessionState.InLobby => new JoinResult
                {
                    gameMode = initialMessage.gameMode,
                    sessionState = sessionState,
                    joinResponse = await AttemptJoin(NetService)
                },
                RunSessionState.InLoadedLobby => new JoinResult
                {
                    gameMode = initialMessage.gameMode,
                    sessionState = sessionState,
                    loadJoinResponse = await AttemptLoadJoin(NetService)
                },
                RunSessionState.Running => new JoinResult
                {
                    gameMode = initialMessage.gameMode,
                    sessionState = sessionState,
                    rejoinResponse = await AttemptRejoin(NetService)
                },
                _ => throw new InvalidOperationException($"Received invalid state {sessionState} from connection."),
            };
        }
        catch (Exception)
        {
            if (NetService?.IsConnected == true)
            {
                NetError reason = CancelToken.IsCancellationRequested ? NetError.CancelledJoin : NetError.InternalError;
                NetService.Disconnect(reason);
            }

            throw;
        }
        finally
        {
            MegaCrit.Sts2.Core.Logging.Logger.logLevelTypeMap[LogType.Network] = LogLevel.Info;
            MegaCrit.Sts2.Core.Logging.Logger.logLevelTypeMap[LogType.Actions] = LogLevel.Info;
            MegaCrit.Sts2.Core.Logging.Logger.logLevelTypeMap[LogType.GameSync] = LogLevel.Info;

            await updateLoopCancelSource.CancelAsync();

            if (NetService != null)
            {
                NetService.UnregisterMessageHandler<InitialGameInfoMessage>(HandleInitialGameInfoMessage);
                NetService.UnregisterMessageHandler<ClientLobbyJoinResponseMessage>(HandleJoinResponseMessage);
                NetService.UnregisterMessageHandler<ClientLoadJoinResponseMessage>(HandleLoadJoinResponseMessage);
                NetService.UnregisterMessageHandler<ClientRejoinResponseMessage>(HandleRejoinResponseMessage);
                NetService.Disconnected -= OnDisconnected;
            }
        }
    }

    private void ValidateInitialMessage(InitialGameInfoMessage initialMessage)
    {
        ConnectionFailureReason? declaredCompatibilityFailure = null;
        if (initialMessage.connectionFailureReason.HasValue)
        {
            ConnectionFailureReason failureReason = initialMessage.connectionFailureReason.Value;
            if (failureReason != ConnectionFailureReason.VersionMismatch &&
                failureReason != ConnectionFailureReason.ModMismatch)
            {
                _logger.Info($"Received initial join message with failure: {failureReason}");
                throw new ClientConnectionFailedException(
                    "Got connection failure from host",
                    new NetErrorInfo(failureReason));
            }

            if (_relaxedCompatibility)
            {
                _logger.Warn($"Ignoring host-declared compatibility failure because relaxed profile is enabled: {failureReason}");
            }
            else
            {
                declaredCompatibilityFailure = failureReason;
            }
        }

        string localVersion = LanConnectBuildInfo.GetGameVersion();
        if (!string.Equals(initialMessage.version, localVersion, StringComparison.Ordinal))
        {
            if (!_relaxedCompatibility)
            {
                throw new ClientConnectionFailedException(
                    $"游戏版本不匹配。房间版本：{initialMessage.version}；当前客户端版本：{localVersion}。",
                    new NetErrorInfo(ConnectionFailureReason.VersionMismatch));
            }

            _logger.Warn($"Ignoring game version mismatch because relaxed profile is enabled. Host={initialMessage.version} Local={localVersion}");
        }

        List<string> localMods = LanConnectBuildInfo.GetModList();
        List<string> hostMods = initialMessage.mods ?? new List<string>();
        List<string> missingModsOnLocal = hostMods.Except(localMods).ToList();
        List<string> missingModsOnHost = localMods.Except(hostMods).ToList();
        ConnectionFailureExtraInfo extraInfo = new()
        {
            missingModsOnHost = missingModsOnHost,
            missingModsOnLocal = missingModsOnLocal
        };
        if (missingModsOnLocal.Count > 0 || missingModsOnHost.Count > 0)
        {
            if (!_relaxedCompatibility)
            {
                string message = LanConnectLobbyModMismatchFormatter.BuildMessage(
                    missingModsOnLocal,
                    missingModsOnHost,
                    fallbackMessage: "Mod mismatch.");
                _logger.Warn(
                    $"Mod mismatch. MissingOnLocal={string.Join(",", missingModsOnLocal)} MissingOnHost={string.Join(",", missingModsOnHost)}");
                throw new ClientConnectionFailedException(
                    message,
                    new NetErrorInfo(ConnectionFailureReason.ModMismatch, extraInfo));
            }

            _logger.Warn(
                $"Ignoring mod list mismatch because relaxed profile is enabled. MissingOnLocal={string.Join(",", missingModsOnLocal)} MissingOnHost={string.Join(",", missingModsOnHost)}");
            _detectedMissingModsOnLocal = missingModsOnLocal;
            _detectedMissingModsOnHost = missingModsOnHost;
        }

        if (initialMessage.idDatabaseHash != ModelIdSerializationCache.Hash)
        {
            if (!_relaxedCompatibility)
            {
                _logger.Warn(
                    $"ModelDb hash mismatch. Host={initialMessage.idDatabaseHash} Local={ModelIdSerializationCache.Hash}");
                throw new ClientConnectionFailedException(
                    $"ModelDb hash mismatch. Host: {initialMessage.idDatabaseHash} Ours: {ModelIdSerializationCache.Hash}",
                    new NetErrorInfo(ConnectionFailureReason.VersionMismatch, extraInfo));
            }

            _logger.Warn(
                $"Ignoring ModelDb hash mismatch because relaxed profile is enabled. Host={initialMessage.idDatabaseHash} Local={ModelIdSerializationCache.Hash}");
        }

        if (declaredCompatibilityFailure.HasValue)
        {
            throw new ClientConnectionFailedException(
                $"房主报告了连接兼容性错误：{declaredCompatibilityFailure.Value}",
                new NetErrorInfo(declaredCompatibilityFailure.Value));
        }
    }

    private async Task NetServiceUpdateLoop(CancellationTokenSource tokenSource, SceneTree sceneTree)
    {
        while (!tokenSource.IsCancellationRequested)
        {
            try
            {
                NetService?.Update();
            }
            catch (Exception ex)
            {
                CaptureJoinProtocolFailure(ex);
                if (!_protocolMismatchEscalated &&
                    !string.IsNullOrWhiteSpace(_protocolMismatchSummary) &&
                    IsJoinHandshakeStillPending())
                {
                    _protocolMismatchEscalated = true;
                    ClientConnectionFailedException protocolException = new(
                        _protocolMismatchSummary,
                        new NetErrorInfo(NetError.InternalError, selfInitiated: false));
                    TrySetException(_connectCompletion, protocolException);
                    TrySetException(_joinCompletion, protocolException);
                    TrySetException(_loadJoinCompletion, protocolException);
                    TrySetException(_rejoinCompletion, protocolException);
                    if (NetService?.IsConnected == true)
                    {
                        NetService.Disconnect(NetError.InternalError);
                    }
                }
                Log.Error(ex.ToString());
            }

            await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task<ClientLobbyJoinResponseMessage> AttemptJoin(NetClientGameService gameService)
    {
        _joinCompletion = new TaskCompletionSource<ClientLobbyJoinResponseMessage>();
        _logger.Info("Sending ClientLobbyJoinRequestMessage and waiting for response.");
        UnlockState unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
        ClientLobbyJoinRequestMessage message = new()
        {
            maxAscensionUnlocked = SaveManager.Instance.Progress.MaxMultiplayerAscension,
            unlockState = unlockState.ToSerializable()
        };
        gameService.SendMessage(message);
        ClientLobbyJoinResponseMessage response = await _joinCompletion.Task;
        _logger.Info($"Received ClientLobbyJoinResponseMessage: {response}");
        return response;
    }

    private async Task<ClientLoadJoinResponseMessage> AttemptLoadJoin(NetClientGameService gameService)
    {
        _loadJoinCompletion = new TaskCompletionSource<ClientLoadJoinResponseMessage>();
        _logger.Info("Sending ClientLoadJoinRequestMessage and waiting for response.");
        gameService.SendMessage(default(ClientLoadJoinRequestMessage));
        ClientLoadJoinResponseMessage response = await _loadJoinCompletion.Task;
        _logger.Info($"Received ClientLoadJoinResponseMessage: {response}");
        return response;
    }

    private async Task<ClientRejoinResponseMessage> AttemptRejoin(NetClientGameService gameService)
    {
        _rejoinCompletion = new TaskCompletionSource<ClientRejoinResponseMessage>();
        _logger.Info("Sending ClientRejoinRequestMessage and waiting for response.");
        gameService.SendMessage(default(ClientRejoinRequestMessage));
        ClientRejoinResponseMessage response = await _rejoinCompletion.Task;
        _logger.Info($"Received ClientRejoinResponseMessage: {response}");
        return response;
    }

    private void HandleInitialGameInfoMessage(InitialGameInfoMessage message, ulong _)
    {
        if (_connectCompletion == null || _connectCompletion.Task.IsCompleted)
        {
            _logger.Warn("Received InitialGameInfoMessage when the flow was not waiting for it.");
            return;
        }

        _connectCompletion.SetResult(message);
    }

    private void HandleRejoinResponseMessage(ClientRejoinResponseMessage message, ulong _)
    {
        if (_rejoinCompletion == null || _rejoinCompletion.Task.IsCompleted)
        {
            _logger.Warn("Received ClientRejoinResponseMessage when the flow was not waiting for it.");
            return;
        }

        _rejoinCompletion.SetResult(message);
    }

    private void HandleLoadJoinResponseMessage(ClientLoadJoinResponseMessage message, ulong _)
    {
        if (_loadJoinCompletion == null || _loadJoinCompletion.Task.IsCompleted)
        {
            _logger.Warn("Received ClientLoadJoinResponseMessage when the flow was not waiting for it.");
            return;
        }

        _loadJoinCompletion.SetResult(message);
    }

    private void HandleJoinResponseMessage(ClientLobbyJoinResponseMessage message, ulong _)
    {
        if (_joinCompletion == null || _joinCompletion.Task.IsCompleted)
        {
            _logger.Warn("Received ClientLobbyJoinResponseMessage when the flow was not waiting for it.");
            return;
        }

        _joinCompletion.SetResult(message);
    }

    private void OnDisconnected(NetErrorInfo info)
    {
        if ((_detectedMissingModsOnLocal?.Count > 0 || _detectedMissingModsOnHost?.Count > 0)
            && IsJoinHandshakeStillPending())
        {
            string modMessage = LanConnectLobbyModMismatchFormatter.BuildMessage(
                _detectedMissingModsOnLocal, _detectedMissingModsOnHost);
            _logger.Warn(
                $"Disconnect during handshake with prior mod mismatch (relaxed mode): {modMessage}");
            ClientConnectionFailedException modException = new(
                modMessage,
                new NetErrorInfo(NetError.InternalError, selfInitiated: false));
            TrySetException(_connectCompletion, modException);
            TrySetException(_joinCompletion, modException);
            TrySetException(_loadJoinCompletion, modException);
            TrySetException(_rejoinCompletion, modException);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_protocolMismatchSummary) &&
            (info.GetReason() == NetError.HandshakeTimeout ||
             info.GetReason() == NetError.Timeout ||
             info.GetReason() == NetError.InternalError))
        {
            _logger.Warn(
                $"Treating disconnect as protocol incompatibility because packet decode failed earlier: {_protocolMismatchSummary}");
            ClientConnectionFailedException protocolException = new(
                _protocolMismatchSummary,
                new NetErrorInfo(NetError.InternalError, selfInitiated: false));
            TrySetException(_connectCompletion, protocolException);
            TrySetException(_joinCompletion, protocolException);
            TrySetException(_loadJoinCompletion, protocolException);
            TrySetException(_rejoinCompletion, protocolException);
            return;
        }

        _logger.Info($"Disconnected during join flow, reason={info.GetReason()}.");
        ClientConnectionFailedException exception = new(
            $"Unexpectedly disconnected from host while joining. Reason: {info.GetReason()}",
            info);
        TrySetException(_connectCompletion, exception);
        TrySetException(_joinCompletion, exception);
        TrySetException(_loadJoinCompletion, exception);
        TrySetException(_rejoinCompletion, exception);
    }

    private void Cancel()
    {
        TrySetCanceled(_connectCompletion);
        TrySetCanceled(_joinCompletion);
        TrySetCanceled(_loadJoinCompletion);
        TrySetCanceled(_rejoinCompletion);
    }

    private static void TrySetException<T>(TaskCompletionSource<T>? completion, Exception exception)
    {
        if (completion != null && !completion.Task.IsCompleted)
        {
            completion.SetException(exception);
        }
    }

    private static void TrySetCanceled<T>(TaskCompletionSource<T>? completion)
    {
        if (completion != null && !completion.Task.IsCompleted)
        {
            completion.SetCanceled();
        }
    }

    private void CaptureJoinProtocolFailure(Exception ex)
    {
        if (!IsJoinHandshakeStillPending())
        {
            return;
        }

        string errorText = ex.ToString();
        string typeName = ex.GetType().Name;

        if (typeName.Contains("ModelNotFound", StringComparison.Ordinal) ||
            typeName.Contains("KeyNotFound", StringComparison.Ordinal))
        {
            _protocolMismatchSummary ??= BuildModEnrichedProtocolMessage(
                "联机协议不兼容：客户端缺少房间中存在的游戏内容，导致数据无法解析。");
            return;
        }

        if (errorText.Contains("no message handlers are registered for that type", StringComparison.OrdinalIgnoreCase))
        {
            _protocolMismatchSummary ??= BuildModEnrichedProtocolMessage(
                "联机协议不兼容：房主提前发送了当前客户端未注册的联机消息。通常是房主与加入方的 Mod 内容或联机流程不一致。");
            return;
        }

        bool looksLikeDeserializeFailure =
            ex is IndexOutOfRangeException ||
            ex is ArgumentOutOfRangeException ||
            ex is InvalidOperationException;
        if (!looksLikeDeserializeFailure)
        {
            return;
        }

        if (errorText.Contains("Deserialize(PacketReader", StringComparison.Ordinal) ||
            errorText.Contains("NetMessageBus.TryDeserializeMessage", StringComparison.Ordinal))
        {
            _protocolMismatchSummary ??= BuildModEnrichedProtocolMessage(
                "联机协议不兼容：客户端在握手阶段无法解析房主发来的数据包。通常是房主与加入方的 Mod 内容或底层数据协议不一致。");
        }
    }

    private string BuildModEnrichedProtocolMessage(string fallback)
    {
        if (_detectedMissingModsOnLocal?.Count > 0 || _detectedMissingModsOnHost?.Count > 0)
        {
            return LanConnectLobbyModMismatchFormatter.BuildMessage(
                _detectedMissingModsOnLocal, _detectedMissingModsOnHost);
        }

        return fallback;
    }

    private bool IsJoinHandshakeStillPending()
    {
        return (_connectCompletion != null && !_connectCompletion.Task.IsCompleted) ||
               (_joinCompletion != null && !_joinCompletion.Task.IsCompleted) ||
               (_loadJoinCompletion != null && !_loadJoinCompletion.Task.IsCompleted) ||
               (_rejoinCompletion != null && !_rejoinCompletion.Task.IsCompleted);
    }
}
