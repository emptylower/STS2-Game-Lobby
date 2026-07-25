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

    public static async Task<bool> JoinAsync(
        NSubmenuStack stack,
        SceneTree sceneTree,
        string ip,
        ushort port,
        ulong netId,
        string identitySource,
        CancellationToken cancellationToken)
    {
        LanConnectLobbyManagedJoinFlow? currentFlow = null;
        try
        {
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
                IsRetryable,
                async (attempt, _) =>
                {
                    bool cleaned = LanConnectNetClientCleanup.TryCleanup(currentFlow?.NetService);
                    Log.Info(
                        $"sts2_lan_connect lan_direct_join: endpoint={ip}:{port}, netId={netId}, attempt={attempt}/{MaxAttempts}, cleanup={(cleaned ? "success" : "best_effort")}, nextAttempt={attempt + 1}");
                    await Task.Delay(250, cancellationToken);
                });

            LanConnectLobbyJoinFlow.PushJoinedScreen(stack, success.NetService, success.JoinResult);
            return true;
        }
        catch (OperationCanceledException)
        {
            Log.Warn($"sts2_lan_connect lan_direct_join: endpoint={ip}:{port}, netId={netId}, result=canceled");
            LanConnectNetClientCleanup.TryCleanup(currentFlow?.NetService);
            return false;
        }
        catch (ClientConnectionFailedException ex)
        {
            LanConnectNetClientCleanup.TryCleanup(currentFlow?.NetService);
            NErrorPopup? popup = NErrorPopup.Create(ex.info);
            if (popup != null)
            {
                NModalContainer.Instance?.Add(popup);
            }

            return false;
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

            return false;
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
        return exception is ClientConnectionFailedException connectionFailure
               && IsRetryable(connectionFailure.info.GetReason());
    }

    internal static bool IsRetryable(NetError reason)
    {
        return IsRetryableReason(reason.ToString());
    }

    internal static bool IsRetryableReason(string? reason)
    {
        return string.Equals(reason, "Timeout", StringComparison.Ordinal)
               || string.Equals(reason, "UnknownNetworkError", StringComparison.Ordinal);
    }

    private static string DescribeFailure(Exception exception)
    {
        return exception is ClientConnectionFailedException connectionFailure
            ? connectionFailure.info.GetReason().ToString()
            : exception.GetType().Name;
    }
}
