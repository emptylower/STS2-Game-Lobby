using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectDirectJoinSuccess(
    JoinResult JoinResult,
    NetClientGameService NetService);

internal static class LanConnectDirectJoinFlow
{
    internal const int MaxAttempts = 2;

    public static async Task<LobbyJoinAttemptResult> JoinAsync(
        NSubmenuStack stack,
        SceneTree sceneTree,
        string ip,
        ushort port,
        ulong netId,
        string identitySource,
        CancellationToken cancellationToken)
    {
        LanConnectLobbyManagedJoinFlow? currentFlow = null;
        if (LanConnectDegradedMode.CreateBlockingFailure() is { } degradedFailure)
        {
            return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Failed, null, degradedFailure);
        }

        LanConnectProtocolFailure? localFailure = ValidateCompatOnlyPreTransport(
            LanConnectExternalCapabilityCollector.Collect().RitsuLibPresent);
        if (localFailure != null)
        {
            return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Failed, null, localFailure);
        }

        LanConnectSessionProtocolLease? protocolLease = null;
        try
        {
            LanConnectProtocolSelection selection = LanConnectProtocolSelection.CreateLocalCompat(
                LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers(),
                LanConnectBuildInfo.GetGameVersion(),
                LanConnectWireCacheDiagnostics.GetCurrentResult().Snapshot?.Signature);
            protocolLease = LanConnectSessionProtocolState.Shared.FreezeClient(
                selection,
                $"direct:{ip}:{port}:{netId}");
            LanConnectDirectJoinSuccess success = await ExecuteAttemptsAsync(
                netId,
                async (attempt, stableNetId) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    currentFlow = new LanConnectLobbyManagedJoinFlow(
                        LanConnectLobbyEndpointDefaults.GetCompatibilityProfile());
                    using CancellationTokenRegistration registration = cancellationToken.Register(
                        static state => ((CancellationTokenSource)state!).Cancel(),
                        currentFlow.CancelToken);
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    Log.Info(
                        $"sts2_lan_connect lan_direct_join: endpoint={ip}:{port}, netId={stableNetId}, identitySource={identitySource}, attempt={attempt}/{MaxAttempts}, stage=connect");
                    try
                    {
                        ENetClientConnectionInitializer initializer = new(stableNetId, ip, port);
                        JoinResult joinResult = await currentFlow.BeginAsync(initializer, sceneTree);
                        NetClientGameService netService = currentFlow.NetService
                            ?? throw new InvalidOperationException("Direct join completed without an active net service.");
                        Log.Info(
                            $"sts2_lan_connect lan_direct_join: endpoint={ip}:{port}, netId={stableNetId}, attempt={attempt}/{MaxAttempts}, result=success, elapsedMs={stopwatch.ElapsedMilliseconds}");
                        return new LanConnectDirectJoinSuccess(joinResult, netService);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(
                            $"sts2_lan_connect lan_direct_join: endpoint={ip}:{port}, netId={stableNetId}, attempt={attempt}/{MaxAttempts}, result=failure, reason={DescribeFailure(ex)}, elapsedMs={stopwatch.ElapsedMilliseconds}");
                        throw;
                    }
                },
                LanConnectJoinRetryPolicy.IsRetryable,
                async (attempt, _) =>
                {
                    bool cleaned = LanConnectNetClientCleanup.TryCleanup(currentFlow?.NetService);
                    Log.Info(
                        $"sts2_lan_connect lan_direct_join: endpoint={ip}:{port}, netId={netId}, attempt={attempt}/{MaxAttempts}, cleanup={(cleaned ? "success" : "best_effort")}, nextAttempt={attempt + 1}");
                    await Task.Delay(250, cancellationToken);
                });

            LanConnectLobbyJoinFlow.PushJoinedScreen(stack, success.NetService, success.JoinResult);
            protocolLease.Attach();
            Action<NetErrorInfo>? releaseLease = null;
            releaseLease = _ =>
            {
                success.NetService.Disconnected -= releaseLease;
                protocolLease.Dispose();
            };
            success.NetService.Disconnected += releaseLease;
            return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Joined);
        }
        catch (LanConnectProtocolException exception)
        {
            LanConnectNetClientCleanup.TryCleanup(currentFlow?.NetService);
            protocolLease?.Dispose();
            return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Failed, null, exception.Failure);
        }
        catch (OperationCanceledException)
        {
            Log.Warn($"sts2_lan_connect lan_direct_join: endpoint={ip}:{port}, netId={netId}, result=canceled");
            LanConnectNetClientCleanup.TryCleanup(currentFlow?.NetService);
            protocolLease?.Dispose();
            return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Canceled, "加入操作已取消。");
        }
        catch (ClientConnectionFailedException ex)
        {
            LanConnectNetClientCleanup.TryCleanup(currentFlow?.NetService);
            NErrorPopup? popup = NErrorPopup.Create(ex.info);
            if (popup != null)
            {
                NModalContainer.Instance?.Add(popup);
            }

            protocolLease?.Dispose();
            return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Failed, ex.Message);
        }
        catch (Exception ex)
        {
            LanConnectNetClientCleanup.TryCleanup(currentFlow?.NetService);
            Log.Error($"sts2_lan_connect lan_direct_join: unexpected failure: {ex}");
            NErrorPopup? popup = NErrorPopup.Create(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
            if (popup != null)
            {
                NModalContainer.Instance?.Add(popup);
            }

            protocolLease?.Dispose();
            return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Failed, ex.Message);
        }
    }

    internal static async Task<T> ExecuteAttemptsAsync<T>(
        ulong netId,
        Func<int, ulong, Task<T>> attempt,
        Func<Exception, bool> shouldRetry,
        Func<int, Exception, Task> beforeRetry)
    {
        for (int attemptNumber = 1; ; attemptNumber++)
        {
            try
            {
                return await attempt(attemptNumber, netId);
            }
            catch (Exception ex) when (attemptNumber < MaxAttempts && shouldRetry(ex))
            {
                await beforeRetry(attemptNumber, ex);
            }
        }
    }

    internal static bool IsRetryable(Exception exception)
    {
        return LanConnectJoinRetryPolicy.IsRetryable(exception);
    }

    internal static bool IsRetryable(NetError reason)
    {
        return IsRetryableReason(reason.ToString());
    }

    internal static bool IsRetryableReason(string? reason)
    {
        return LanConnectJoinRetryPolicy.IsRetryableReason(reason);
    }

    internal static LanConnectProtocolFailure? ValidateCompatOnlyPreTransport(bool ritsuLibPresent) =>
        ritsuLibPresent
            ? LanConnectProtocolFailure.RitsuLibNotAllowedInCompat(
                "Pure direct-IP is compat-only in the 0.6 prerelease series.")
            : null;

    private static string DescribeFailure(Exception exception)
    {
        return exception is ClientConnectionFailedException connectionFailure
            ? connectionFailure.info.GetReason().ToString()
            : exception.GetType().Name;
    }
}
