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
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2LanConnect.Scripts;

internal enum LobbyJoinAttemptKind
{
    Joined,
    Failed,
    Canceled
}

internal readonly record struct LobbyJoinAttemptResult(LobbyJoinAttemptKind Kind, string? FailureMessage = null)
{
    public bool Joined => Kind == LobbyJoinAttemptKind.Joined;

    public bool Canceled => Kind == LobbyJoinAttemptKind.Canceled;
}

internal static class LanConnectLobbyJoinFlow
{
    public static async Task<LobbyJoinAttemptResult> JoinAsync(
        NSubmenuStack stack,
        Control loadingOverlay,
        LobbyJoinRoomResponse joinResponse,
        string? desiredSavePlayerNetId,
        CancellationToken cancellationToken,
        Action<string>? reportProgress = null)
    {
        List<JoinAttemptCandidate> candidates = BuildCandidates(joinResponse);

        if (candidates.Count == 0)
        {
            LanConnectPopupUtil.ShowInfo("大厅服务没有返回可用的连接地址，无法加入该房间。");
            return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Failed, "大厅服务没有返回可用的连接地址。");
        }

        loadingOverlay.Visible = true;
        ClientConnectionFailedException? lastConnectionFailure = null;
        Exception? lastUnexpectedFailure = null;

        try
        {
            LanConnectProtocolProfiles.SetActiveProfile(joinResponse.Room.ProtocolProfile, joinResponse.Room.MaxPlayers, "join_room");
            ulong netId = ResolveJoinNetId(joinResponse, desiredSavePlayerNetId);
            Log.Info($"sts2_lan_connect join_flow: policy={LanConnectCompatibilityMatrix.DescribeCurrentPolicy()} roomCompatibility={LanConnectCompatibilityMatrix.DescribeRoomCompatibility(joinResponse.Room)} strategy={joinResponse.ConnectionPlan.Strategy} directCandidates={joinResponse.ConnectionPlan.DirectCandidates.Count} relayAllowed={joinResponse.ConnectionPlan.RelayAllowed}");
            for (int index = 0; index < candidates.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                JoinAttemptCandidate candidate = candidates[index];
                reportProgress?.Invoke(BuildCandidateProgressMessage(candidate, index + 1, candidates.Count));
                LanConnectLobbyManagedJoinFlow joinFlow = new(LanConnectLobbyEndpointDefaults.GetCompatibilityProfile());
                using CancellationTokenRegistration cancelRegistration = cancellationToken.Register(static state =>
                {
                    if (state is CancellationTokenSource source && !source.IsCancellationRequested)
                    {
                        source.Cancel();
                    }
                }, joinFlow.CancelToken);
                try
                {
                    ENetClientConnectionInitializer initializer = new(netId, candidate.Host, candidate.Port);
                    Log.Info($"sts2_lan_connect attempting lobby join via {candidate.Host}:{candidate.Port} ({candidate.Label}) using netId={netId}.");
                    JoinResult joinResult = await joinFlow.BeginAsync(initializer, stack.GetTree());
                    _ = TaskHelper.RunSafely(ReportConnectionEventSafeAsync(
                        joinResponse.Room.RoomId,
                        joinResponse.TicketId,
                        candidate.IsRelay ? "relay_success" : "direct_success",
                        candidate,
                        "join_completed"));
                    reportProgress?.Invoke("连接成功，正在进入联机界面");
                    if (joinFlow.NetService == null)
                    {
                        throw new InvalidOperationException("Managed JoinFlow completed without an active net service.");
                    }

                    PushJoinedScreen(stack, joinFlow.NetService, joinResult);
                    LanConnectLobbyRuntime.Instance?.AttachJoinedClient(joinFlow.NetService, joinResponse);
                    return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Joined);
                }
                catch (ClientConnectionFailedException ex)
                {
                    lastConnectionFailure = ex;
                    joinFlow.NetService?.Disconnect(ex.info.GetReason());
                    Log.Warn($"sts2_lan_connect lobby join candidate {candidate.Host}:{candidate.Port} failed: {ex.info}");
                    _ = TaskHelper.RunSafely(ReportConnectionEventSafeAsync(
                        joinResponse.Room.RoomId,
                        joinResponse.TicketId,
                        BuildFailurePhase(candidate, ex.info),
                        candidate,
                        BuildFailureDetail(ex)));

                    if (ShouldStopRetryingAfterFailure(ex))
                    {
                        break;
                    }

                    if (index + 1 < candidates.Count && !cancellationToken.IsCancellationRequested)
                    {
                        reportProgress?.Invoke($"当前连接未成功，正在尝试下一个地址 ({index + 2}/{candidates.Count})");
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Warn("sts2_lan_connect lobby join was canceled.");
                    _ = TaskHelper.RunSafely(ReportConnectionEventSafeAsync(
                        joinResponse.Room.RoomId,
                        joinResponse.TicketId,
                        candidate.IsRelay ? "relay_canceled" : "direct_canceled",
                        candidate,
                        "join_canceled"));
                    return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Canceled, "加入操作已取消。");
                }
                catch (Exception ex)
                {
                    lastUnexpectedFailure = ex;
                    joinFlow.NetService?.Disconnect(NetError.InternalError);
                    Log.Error($"sts2_lan_connect unexpected lobby join error via {candidate.Host}:{candidate.Port}: {ex}");
                    _ = TaskHelper.RunSafely(ReportConnectionEventSafeAsync(
                        joinResponse.Room.RoomId,
                        joinResponse.TicketId,
                        candidate.IsRelay ? "relay_failure" : "direct_failure",
                        candidate,
                        ex.GetType().Name));
                }
            }

            if (lastConnectionFailure != null)
            {
                string? customMessage = DescribeJoinFailure(lastConnectionFailure);
                if (!string.IsNullOrWhiteSpace(customMessage))
                {
                    LanConnectPopupUtil.ShowInfo(customMessage);
                    return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Failed, customMessage);
                }

                NErrorPopup? popup = NErrorPopup.Create(lastConnectionFailure.info);
                if (popup != null)
                {
                    NModalContainer.Instance?.Add(popup);
                }

                return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Failed, lastConnectionFailure.Message);
            }

            if (lastUnexpectedFailure != null)
            {
                NErrorPopup? popup = NErrorPopup.Create(new NetErrorInfo(NetError.InternalError, selfInitiated: false));
                if (popup != null)
                {
                    NModalContainer.Instance?.Add(popup);
                }

                return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Failed, lastUnexpectedFailure.Message);
            }

            return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Failed, "请查看错误弹窗或连接日志。");
        }
        catch (OperationCanceledException)
        {
            return new LobbyJoinAttemptResult(LobbyJoinAttemptKind.Canceled, "加入操作已取消。");
        }
        finally
        {
            loadingOverlay.Visible = false;
        }
    }

    private static List<JoinAttemptCandidate> BuildCandidates(LobbyJoinRoomResponse joinResponse)
    {
        List<JoinAttemptCandidate> directCandidates = joinResponse.ConnectionPlan.DirectCandidates
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.Ip) && candidate.Port > 0)
            .Select(static candidate => JoinAttemptCandidate.CreateDirect(candidate.Label, candidate.Ip, candidate.Port))
            .ToList();
        List<JoinAttemptCandidate> publicCandidates = directCandidates
            .Where(static candidate => !IsLanCandidate(candidate))
            .ToList();
        List<JoinAttemptCandidate> lanCandidates = directCandidates
            .Where(static candidate => IsLanCandidate(candidate))
            .ToList();

        JoinAttemptCandidate? relayCandidate = null;
        if (joinResponse.ConnectionPlan.RelayAllowed
            && joinResponse.ConnectionPlan.RelayEndpoint != null
            && !string.IsNullOrWhiteSpace(joinResponse.ConnectionPlan.RelayEndpoint.Host)
            && joinResponse.ConnectionPlan.RelayEndpoint.Port > 0)
        {
            relayCandidate = JoinAttemptCandidate.CreateRelay(
                joinResponse.ConnectionPlan.RelayEndpoint.Host,
                joinResponse.ConnectionPlan.RelayEndpoint.Port);
        }

        List<JoinAttemptCandidate> candidates = new();
        switch (joinResponse.ConnectionPlan.Strategy?.Trim().ToLowerInvariant())
        {
            case "relay-only":
                if (relayCandidate != null)
                {
                    candidates.Add(relayCandidate);
                }

                break;
            case "relay-first":
                if (relayCandidate != null)
                {
                    candidates.Add(relayCandidate);
                }

                candidates.AddRange(publicCandidates);
                candidates.AddRange(lanCandidates);
                break;
            default:
                candidates.AddRange(publicCandidates);
                if (relayCandidate != null)
                {
                    candidates.Add(relayCandidate);
                }

                candidates.AddRange(lanCandidates);
                break;
        }

        return candidates;
    }

    private static string BuildCandidateProgressMessage(JoinAttemptCandidate candidate, int attempt, int total)
    {
        string route = candidate.Label switch
        {
            "public" => "公网候选地址",
            "relay" => "中继候选地址",
            var label when label.StartsWith("lan_", StringComparison.OrdinalIgnoreCase) => "局域网候选地址",
            _ => "备用连接路径"
        };

        return $"正在通过{route}连接房主 ({attempt}/{total})";
    }

    private static string BuildFailurePhase(JoinAttemptCandidate candidate, NetErrorInfo info)
    {
        if (candidate.IsRelay)
        {
            return "relay_failure";
        }

        return info.GetReason() switch
        {
            NetError.Timeout => "direct_timeout",
            NetError.HandshakeTimeout => "direct_timeout",
            _ => "direct_failure"
        };
    }

    private static string BuildFailureDetail(ClientConnectionFailedException ex)
    {
        string error = ex.info.GetErrorString()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        string message = (ex.Message ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (!string.IsNullOrWhiteSpace(message) &&
            !string.Equals(message, error, StringComparison.Ordinal))
        {
            return $"reason={ex.info.GetReason()};error={error};message={message}";
        }

        return $"reason={ex.info.GetReason()};error={error}";
    }

    private static bool IsLanCandidate(JoinAttemptCandidate candidate)
    {
        return candidate.Label.StartsWith("lan_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldStopRetryingAfterFailure(ClientConnectionFailedException ex)
    {
        return !string.IsNullOrWhiteSpace(DescribeJoinFailure(ex));
    }

    private static ulong ResolveJoinNetId(LobbyJoinRoomResponse joinResponse, string? desiredSavePlayerNetId)
    {
        if (!string.IsNullOrWhiteSpace(desiredSavePlayerNetId) && ulong.TryParse(desiredSavePlayerNetId, out ulong selectedNetId))
        {
            Log.Info($"sts2_lan_connect join_flow: using selected saved-run slot netId={selectedNetId}");
            return selectedNetId;
        }

        ulong fallbackNetId = LanConnectNetUtil.GenerateClientNetId();
        Log.Info(
            $"sts2_lan_connect join_flow: no saved-run slot selected for room {joinResponse.Room.RoomId}; falling back to random netId={fallbackNetId}");
        return fallbackNetId;
    }

    private static async Task ReportConnectionEventSafeAsync(
        string roomId,
        string ticketId,
        string phase,
        JoinAttemptCandidate candidate,
        string detail)
    {
        try
        {
            using LobbyApiClient apiClient = LobbyApiClient.CreateConfigured();
            await apiClient.ReportConnectionEventAsync(roomId, new LobbyConnectionEventRequest
            {
                TicketId = ticketId,
                Phase = phase,
                CandidateLabel = candidate.Label,
                CandidateEndpoint = $"{candidate.Host}:{candidate.Port}",
                Detail = detail,
                PlayerName = LanConnectConfig.GetEffectivePlayerDisplayName()
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect failed to report connection event phase={phase} roomId={roomId}: {ex.Message}");
        }
    }

    private static void PushJoinedScreen(NSubmenuStack stack, NetClientGameService netService, JoinResult joinResult)
    {
        if (joinResult.sessionState == RunSessionState.InLobby)
        {
            switch (joinResult.gameMode)
            {
                case GameMode.Standard:
                {
                    NCharacterSelectScreen submenu = stack.GetSubmenuType<NCharacterSelectScreen>();
                    submenu.InitializeMultiplayerAsClient(netService, joinResult.joinResponse!.Value);
                    stack.Push(submenu);
                    return;
                }
                case GameMode.Daily:
                {
                    NDailyRunScreen submenu = stack.GetSubmenuType<NDailyRunScreen>();
                    submenu.InitializeMultiplayerAsClient(netService, joinResult.joinResponse!.Value);
                    stack.Push(submenu);
                    return;
                }
                case GameMode.Custom:
                {
                    NCustomRunScreen submenu = stack.GetSubmenuType<NCustomRunScreen>();
                    submenu.InitializeMultiplayerAsClient(netService, joinResult.joinResponse!.Value);
                    stack.Push(submenu);
                    return;
                }
            }
        }

        if (joinResult.sessionState == RunSessionState.InLoadedLobby)
        {
            switch (joinResult.gameMode)
            {
                case GameMode.Standard:
                {
                    NMultiplayerLoadGameScreen submenu = stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
                    submenu.InitializeAsClient(netService, joinResult.loadJoinResponse!.Value);
                    stack.Push(submenu);
                    return;
                }
                case GameMode.Daily:
                {
                    NDailyRunLoadScreen submenu = stack.GetSubmenuType<NDailyRunLoadScreen>();
                    submenu.InitializeAsClient(netService, joinResult.loadJoinResponse!.Value);
                    stack.Push(submenu);
                    return;
                }
                case GameMode.Custom:
                {
                    NCustomRunLoadScreen submenu = stack.GetSubmenuType<NCustomRunLoadScreen>();
                    submenu.InitializeAsClient(netService, joinResult.loadJoinResponse!.Value);
                    stack.Push(submenu);
                    return;
                }
            }
        }

        throw new ArgumentOutOfRangeException(nameof(joinResult.gameMode), joinResult.gameMode, "Unhandled multiplayer game mode.");
    }

    private sealed record JoinAttemptCandidate(string Label, string Host, ushort Port, bool IsRelay)
    {
        public static JoinAttemptCandidate CreateDirect(string label, string host, ushort port)
        {
            return new JoinAttemptCandidate(label, host, port, false);
        }

        public static JoinAttemptCandidate CreateRelay(string host, ushort port)
        {
            return new JoinAttemptCandidate("relay", host, port, true);
        }
    }

    private static string? DescribeJoinFailure(ClientConnectionFailedException ex)
    {
        string message = (ex.Message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        if (message.Contains("联机协议不兼容", StringComparison.Ordinal) ||
            message.Contains("MOD 不一致", StringComparison.Ordinal) ||
            message.Contains("游戏版本不匹配", StringComparison.Ordinal) ||
            message.Contains("Version mismatch", StringComparison.Ordinal) ||
            message.Contains("房主报告了连接兼容性错误", StringComparison.Ordinal))
        {
            return message;
        }

        return null;
    }
}
