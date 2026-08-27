using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
    private readonly LanConnectProtocolOffer? _protocolOffer;
    private readonly LanConnectProtocolSelection? _protocolSelection;
    private readonly byte[]? _protocolFlowNonce;
    private string? _protocolMismatchSummary;
    private List<string>? _detectedMissingModsOnLocal;
    private List<string>? _detectedMissingModsOnHost;
    private bool _protocolMismatchEscalated;

    public LanConnectLobbyManagedJoinFlow(string compatibilityProfile)
        : this(compatibilityProfile, null, null, null)
    {
    }

    internal LanConnectLobbyManagedJoinFlow(
        string compatibilityProfile,
        LanConnectProtocolOffer? protocolOffer,
        LanConnectProtocolSelection? protocolSelection,
        byte[]? protocolFlowNonce)
    {
        _relaxedCompatibility = string.Equals(
            compatibilityProfile,
            "test_relaxed",
            StringComparison.OrdinalIgnoreCase);
        _protocolOffer = protocolOffer;
        _protocolSelection = protocolSelection;
        _protocolFlowNonce = protocolFlowNonce?.ToArray();
    }

    public NetClientGameService? NetService { get; private set; }

    public CancellationTokenSource CancelToken { get; } = new();

    public async Task<JoinResult> BeginAsync(object initializer, SceneTree sceneTree)
    {
        MegaCrit.Sts2.Core.Logging.Logger.logLevelTypeMap[LogType.Network] = LogLevel.Debug;
        MegaCrit.Sts2.Core.Logging.Logger.logLevelTypeMap[LogType.Actions] = LogLevel.VeryDebug;
        MegaCrit.Sts2.Core.Logging.Logger.logLevelTypeMap[LogType.GameSync] = LogLevel.VeryDebug;

        if (_connectCompletion != null)
        {
            throw new InvalidOperationException("LanConnectLobbyManagedJoinFlow can only be used once.");
        }

        _logger.Info($"Beginning managed join with initializer {initializer} relaxedCompatibility={_relaxedCompatibility}");
        NetService = LanConnectNetGameServiceFactory.CreateClient();
        if (_protocolSelection?.Profile == LanConnectProtocolProfile.TailV1)
        {
            LanConnectTailMessageRuntime.Shared.BindClient(
                NetService,
                _protocolOffer ?? throw new InvalidOperationException("Tail join has no frozen local offer."),
                _protocolSelection,
                _protocolFlowNonce ?? throw new InvalidOperationException("Tail join has no protocol flow nonce."));
        }
        LanConnectRitsuLibLobbyCompatibility.TrackLobbyNetService(NetService);
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
            NetErrorInfo? connectError = await ConnectAsync(initializer, NetService, CancelToken.Token);
            if (connectError.HasValue)
            {
                _logger.Info($"Connection failed before handshake: {connectError}");
                throw new ClientConnectionFailedException("Could not connect", connectError.Value);
            }

            _logger.Info("Initializer connection completed, awaiting initial game info message.");
            InitialGameInfoMessage initialMessage = await _connectCompletion.Task;
            LanConnectLobbyHandshakeCompatibility.PeerVersionSnapshot hostInfo =
                LanConnectLobbyHandshakeCompatibility.ReadInitialGameInfo(initialMessage);
            ValidateInitialMessage(initialMessage, hostInfo);

            RunSessionState sessionState = initialMessage.sessionState;
            string peerMetadata = hostInfo.HasVersionMetadata
                ? $"Version={hostInfo.Version} Hash={hostInfo.IdDatabaseHash}"
                : "VersionMetadata=unavailable";
            _logger.Info($"Got initial game info message. {peerMetadata} Mode={initialMessage.gameMode} State={sessionState}");

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

            if (NetService != null)
            {
                LanConnectTailMessageRuntime.Shared.Unbind(NetService);
                LanConnectRitsuLibLobbyCompatibility.ReleaseLobbyNetService(NetService);
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

    private static async Task<NetErrorInfo?> ConnectAsync(
        object initializer,
        NetClientGameService netService,
        CancellationToken cancellationToken)
    {
        MethodInfo connectMethod;
        try
        {
            connectMethod = ResolveCompatibleConnectMethod(
                initializer.GetType(),
                netService.GetType());
        }
        catch (MissingMethodException)
        {
            // GetMethods(Public|Instance) 找不到显式接口实现，
            // 仅当初始化器仍以游戏接口形式暴露 Connect 时才回退到接口解析。
            connectMethod = ResolveCompatibleConnectMethod(
                typeof(IClientConnectionInitializer),
                netService.GetType());
        }

        object? connectTask;
        try
        {
            connectTask = connectMethod.Invoke(initializer, [netService, cancellationToken]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        if (connectTask is not Task task)
        {
            throw new InvalidOperationException(
                $"Connection initializer returned an unsupported result from {connectMethod}.");
        }

        await task;
        object? result = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(task);
        return result switch
        {
            null => null,
            NetErrorInfo error => error,
            _ => throw new InvalidOperationException(
                $"Connection initializer returned unsupported error data of type {result.GetType().FullName}.")
        };
    }

    internal static MethodInfo ResolveCompatibleConnectMethod(Type initializerContractType, Type netServiceType)
    {
        MethodInfo? connectMethod = initializerContractType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(static method => string.Equals(method.Name, "Connect", StringComparison.Ordinal))
            .Where(static method => typeof(Task).IsAssignableFrom(method.ReturnType))
            .SingleOrDefault(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType.IsAssignableFrom(netServiceType)
                    && parameters[1].ParameterType == typeof(CancellationToken);
            });

        return connectMethod
            ?? throw new MissingMethodException(
                initializerContractType.FullName,
                $"Connect({netServiceType.FullName}, {typeof(CancellationToken).FullName})");
    }

    private void ValidateInitialMessage(
        InitialGameInfoMessage initialMessage,
        LanConnectLobbyHandshakeCompatibility.PeerVersionSnapshot hostInfo)
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

        if (!hostInfo.HasVersionMetadata)
        {
            _logger.Info(
                "InitialGameInfoMessage has no native peer version metadata; " +
                "using the game transport handshake plus the frozen LAN protocol selection.");
            if (declaredCompatibilityFailure.HasValue)
            {
                throw new ClientConnectionFailedException(
                    $"房主报告了连接兼容性错误：{declaredCompatibilityFailure.Value}",
                    new NetErrorInfo(declaredCompatibilityFailure.Value));
            }

            return;
        }

        ValidateWireCacheCompatibility(hostInfo);

        string localVersion = LanConnectBuildInfo.GetGameVersion();
        ValidateGameVersion(hostInfo.Version, localVersion);

        List<string> localMods = LanConnectWireCacheHandshakeToken.FilterSentinels(
            LanConnectBuildInfo.GetModList());
        List<string> hostMods = LanConnectWireCacheHandshakeToken.FilterSentinels(
            hostInfo.GameplayAffectingMods);
        List<string> missingModsOnLocal = hostMods.Except(localMods).ToList();
        List<string> missingModsOnHost = localMods.Except(hostMods).ToList();
        ConnectionFailureExtraInfo extraInfo = LanConnectLobbyHandshakeCompatibility.PopulateFailureExtraInfo(
            new ConnectionFailureExtraInfo(),
            hostInfo,
            missingModsOnLocal,
            missingModsOnHost);
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

        if (hostInfo.IdDatabaseHash != ModelIdSerializationCache.Hash)
        {
            if (!_relaxedCompatibility)
            {
                _logger.Warn(
                    $"ModelDb hash mismatch. Host={hostInfo.IdDatabaseHash} Local={ModelIdSerializationCache.Hash}");
                throw new ClientConnectionFailedException(
                    $"ModelDb hash mismatch. Host: {hostInfo.IdDatabaseHash} Ours: {ModelIdSerializationCache.Hash}",
                    new NetErrorInfo(ConnectionFailureReason.VersionMismatch, extraInfo));
            }

            _logger.Warn(
                $"Ignoring ModelDb hash mismatch because relaxed profile is enabled. Host={hostInfo.IdDatabaseHash} Local={ModelIdSerializationCache.Hash}");
        }

        if (declaredCompatibilityFailure.HasValue)
        {
            throw new ClientConnectionFailedException(
                $"房主报告了连接兼容性错误：{declaredCompatibilityFailure.Value}",
                new NetErrorInfo(declaredCompatibilityFailure.Value));
        }
    }

    private void ValidateWireCacheCompatibility(
        LanConnectLobbyHandshakeCompatibility.PeerVersionSnapshot hostInfo)
    {
        LanConnectWireCacheCaptureResult localCapture =
            LanConnectWireCacheDiagnostics.GetCurrentResult();
        LanConnectWireCacheHandshakeDecision decision =
            LanConnectWireCacheHandshakeDecision.Evaluate(
                localCapture,
                hostInfo.WireCacheToken,
                _relaxedCompatibility);

        string decisionName = decision.Kind switch
        {
            LanConnectWireCacheHandshakeDecisionKind.Match => "match",
            LanConnectWireCacheHandshakeDecisionKind.Mismatch => "mismatch",
            LanConnectWireCacheHandshakeDecisionKind.LocalUnavailable => "local-unavailable",
            LanConnectWireCacheHandshakeDecisionKind.RemoteAbsent => "remote-absent",
            _ => decision.Kind.ToString()
        };
        string localSignature = decision.LocalToken?.Signature ?? "unavailable";
        string remoteSignature = decision.RemoteToken?.Signature ?? "absent";
        string diagnostic =
            $"sts2_lan_connect wire_handshake join: localSignature={localSignature}, " +
            $"remoteSignature={remoteSignature}, decision={decisionName}, " +
            $"localWidths={decision.LocalToken?.FormatWidths() ?? "unavailable"}, " +
            $"remoteWidths={decision.RemoteToken?.FormatWidths() ?? "unavailable"}, " +
            $"remoteSentinelStatus={hostInfo.WireCacheToken.Status}, detail={decision.Detail}";

        bool isAllowed = LanConnectWireCacheHandshakeGate.ShouldAllowJoin(
            decision,
            diagnostic,
            message => _logger.Info(message),
            message => _logger.Warn(message));
        if (!isAllowed)
        {
            throw new ClientConnectionFailedException(
                decision.Detail,
                new NetErrorInfo(ConnectionFailureReason.ModMismatch));
        }
    }

    internal static void ValidateGameVersion(string hostVersion, string localVersion)
    {
        string? mismatchMessage = GetGameVersionMismatchMessage(hostVersion, localVersion);
        if (mismatchMessage == null)
        {
            return;
        }

        throw new ClientConnectionFailedException(
            mismatchMessage,
            new NetErrorInfo(ConnectionFailureReason.VersionMismatch));
    }

    internal static string? GetGameVersionMismatchMessage(string hostVersion, string localVersion)
    {
        return string.Equals(
                NormalizeGameVersion(hostVersion),
                NormalizeGameVersion(localVersion),
                StringComparison.Ordinal)
            ? null
            : $"游戏版本不匹配，无法加入房间。房主版本：{hostVersion}；当前版本：{localVersion}。请让所有玩家使用完全相同的游戏版本后重试。";
    }

    private static string NormalizeGameVersion(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.StartsWith('v') || normalized.StartsWith('V')
            ? normalized[1..]
            : normalized;
    }

    private async Task NetServiceUpdateLoop(CancellationTokenSource tokenSource, SceneTree sceneTree)
    {
        while (!tokenSource.IsCancellationRequested)
        {
            try
            {
                NetService?.Update();
                LanConnectRitsuLibLobbyCompatibility.Tick(NetService);
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
        message = LanConnectLobbyHandshakeCompatibility.AttachLocalVersionInfo(message);
        gameService.SendMessage(message);
        ClientLobbyJoinResponseMessage response = await _joinCompletion.Task;
        _logger.Info($"Received ClientLobbyJoinResponseMessage: {response}");
        return response;
    }

    private async Task<ClientLoadJoinResponseMessage> AttemptLoadJoin(NetClientGameService gameService)
    {
        _loadJoinCompletion = new TaskCompletionSource<ClientLoadJoinResponseMessage>();
        _logger.Info("Sending ClientLoadJoinRequestMessage and waiting for response.");
        ClientLoadJoinRequestMessage message = LanConnectLobbyHandshakeCompatibility.AttachLocalVersionInfo(
            default(ClientLoadJoinRequestMessage));
        gameService.SendMessage(message);
        ClientLoadJoinResponseMessage response = await _loadJoinCompletion.Task;
        _logger.Info($"Received ClientLoadJoinResponseMessage: {response}");
        return response;
    }

    private async Task<ClientRejoinResponseMessage> AttemptRejoin(NetClientGameService gameService)
    {
        _rejoinCompletion = new TaskCompletionSource<ClientRejoinResponseMessage>();
        _logger.Info("Sending ClientRejoinRequestMessage and waiting for response.");
        ClientRejoinRequestMessage message = LanConnectLobbyHandshakeCompatibility.AttachLocalVersionInfo(
            default(ClientRejoinRequestMessage));
        gameService.SendMessage(message);
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
        if (TryTakeTailRejection(out LanConnectProtocolFailure? failure)
            && failure != null)
        {
            _logger.Warn($"Disconnect carried validated LAN protocol rejection: {failure.Code}");
            LanConnectProtocolException rejectionException = new(failure);
            TrySetException(_connectCompletion, rejectionException);
            TrySetException(_joinCompletion, rejectionException);
            TrySetException(_loadJoinCompletion, rejectionException);
            TrySetException(_rejoinCompletion, rejectionException);
            return;
        }

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

    // 无法确认 0.107.1 是否有 NetClientGameService.HostNetId（v0.5.6 从未引用过 get_HostNetId），
    // 因此对 TailMessageRuntime 与 HostNetId 的访问必须隔离在本方法体内；
    // OnDisconnected 只在 tail 会话调用它，compat 会话（0.107.1 唯一可能的会话形态）下永远不会 JIT 本方法。
    private bool TryTakeTailRejection(out LanConnectProtocolFailure? failure)
    {
        failure = null;
        if (_protocolSelection?.Profile != LanConnectProtocolProfile.TailV1 || NetService == null)
        {
            return false;
        }

        return LanConnectTailMessageRuntime.Shared.TryTakeValidatedRejection(
            NetService,
            NetService.HostNetId,
            out failure);
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
