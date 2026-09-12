using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectContinueRunLobbyAutoPublisher
{
    private const string HookedMetaKey = "sts2_lan_connect_continue_run_hooks";
    private const double RetryIntervalSeconds = 5d;

    private static readonly LanConnectContinueRunPromptCoordinator PromptCoordinator = new();
    private static readonly LanConnectContinueRunPublishScreenState ScreenState =
        new(TimeSpan.FromSeconds(RetryIntervalSeconds));

    internal static void ScheduleEnsureAutoPublish(Control screen, string source)
    {
        if (!GodotObject.IsInstanceValid(screen))
        {
            return;
        }

        if (!screen.HasMeta(HookedMetaKey))
        {
            screen.SetMeta(HookedMetaKey, true);
            screen.Connect(Node.SignalName.TreeEntered, Callable.From(() => QueueTryPublish(screen, "tree_entered")));
            screen.Connect(Node.SignalName.Ready, Callable.From(() => QueueTryPublish(screen, "ready")));
            screen.Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(() => OnVisibilityChanged(screen)));
            screen.Connect(Node.SignalName.TreeExiting, Callable.From(() => ClearState(screen)));
        }

        QueueTryPublish(screen, source);
    }

    private static void OnVisibilityChanged(Control screen)
    {
        if (!GodotObject.IsInstanceValid(screen))
        {
            return;
        }

        if (screen.Visible)
        {
            QueueTryPublish(screen, "visibility_changed");
            return;
        }

        // 载入页被 Pop 时只是 Visible=false，节点仍常驻 NMainMenuSubmenuStack，
        // 所以 TreeExiting 不会触发。这里释放本次访问的终态与节流，并取消仍挂着的
        // 联机方式选择框；否则上一轮的状态会一直挡住后续的“恢复大厅房间”。
        OnScreenClosed(screen);
    }

    private static void OnScreenClosed(Control screen)
    {
        ulong instanceId = screen.GetInstanceId();
        ScreenState.NotifyScreenClosed(instanceId);
        PromptCoordinator.ClearScreen(instanceId);
        GD.Print(
            $"sts2_lan_connect continue_run_publish: screen hidden, restore stays available screen={screen.GetType().Name}");
    }

    private static void QueueTryPublish(Control screen, string source)
    {
        Callable.From(() => TryPublish(screen, source)).CallDeferred();
    }

    /// <summary>
    /// 尝试结束时，如果它属于上一次进入载入页（中途被 Pop），而玩家此刻已经重新进入，
    /// 就为当前这次进入补发一次尝试；否则这次进入不会再收到任何触发信号。
    /// </summary>
    private static void QueueRetryForNewerVisit(Control screen, LanConnectContinueRunPublishAttempt attempt)
    {
        if (!ScreenState.IsAttemptStale(attempt))
        {
            return;
        }

        if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() || !screen.Visible)
        {
            return;
        }

        QueueTryPublish(screen, "previous_attempt_finished");
    }

    private static void TryPublish(Control screen, string source)
    {
        if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() || !screen.IsNodeReady() || !screen.Visible)
        {
            return;
        }

        if (!TryResolveContext(screen, out ContinuedRunHostContext context))
        {
            return;
        }

        ulong instanceId = screen.GetInstanceId();
        if (!ScreenState.TryBeginAttempt(
                instanceId,
                DateTimeOffset.UtcNow,
                out LanConnectContinueRunPublishAttempt attempt))
        {
            return;
        }

        bool endAttemptHere = true;
        try
        {
            if (context.NetService.Type != NetGameType.Host)
            {
                ScreenState.MarkSettled(attempt);
                GD.Print($"sts2_lan_connect continue_run_publish: skip non-host screen type={context.ScreenType}, source={source}");
                return;
            }

            if (context.NetService.Platform != PlatformType.None)
            {
                ScreenState.MarkSettled(attempt);
                GD.Print($"sts2_lan_connect continue_run_publish: skip because platform={context.NetService.Platform} for screen={context.ScreenType}");
                LanConnectPopupUtil.ShowInfo("当前多人续局是 Steam 会话，无法自动发布到大厅。请使用 --force-steam=off 启动游戏后再继续该存档。");
                return;
            }

            // Resolve host channel before lobby-endpoint preflight so pure-LAN saves never
            // depend on lobby URL or show "未绑定大厅服务" when they should not publish.
            LanConnectResolvedRoomBinding earlyBinding = LanConnectMultiplayerSaveRoomBinding.Resolve(context.Run);
            if (earlyBinding.ProtocolFailure != null)
            {
                ScreenState.MarkSettled(attempt);
                LanConnectProtocolUiMessages.Present(earlyBinding.ProtocolFailure);
                return;
            }
            string? earlyPersistedChannel = earlyBinding.HostChannel;
            if (!string.IsNullOrWhiteSpace(earlyPersistedChannel) && !LanConnectHostChannels.IsValid(earlyPersistedChannel))
            {
                GD.Print(
                    $"sts2_lan_connect continue_run_publish: warning unknown hostChannel={LanConnectHostChannels.DescribePersisted(earlyPersistedChannel)}, requiring user choice");
            }

            LanConnectContinueRunPublishDecisionKind earlyDecision =
                LanConnectContinueRunPublishDecision.Decide(earlyPersistedChannel, earlyBinding.SchemaVersion);
            if (earlyDecision == LanConnectContinueRunPublishDecisionKind.SkipLanOrigin)
            {
                ScreenState.MarkSettled(attempt);
                LanConnectInviteButtonPatch.ScheduleEnsureInviteButton(screen, "continue_lan_resume");
                GD.Print(
                    $"sts2_lan_connect continue_run_publish: skip LAN-origin save screen={context.ScreenType}, saveKey={earlyBinding.SaveKey}, storedBinding={earlyBinding.HasStoredBinding}, persistedHostChannel={LanConnectHostChannels.DescribePersisted(earlyPersistedChannel)}, schemaVersion={earlyBinding.SchemaVersion}, decision=skip_lan_origin, source={source}");
                return;
            }

            if (earlyDecision == LanConnectContinueRunPublishDecisionKind.Prompt)
            {
                if (!PromptCoordinator.TryBegin(
                        instanceId,
                        earlyBinding.SaveKey,
                        out LanConnectContinueRunPromptCoordinator.PromptLease promptLease))
                {
                    ScreenState.MarkRetryable(attempt);
                    return;
                }

                endAttemptHere = false;
                TaskHelper.RunSafely(PromptThenPublishAsync(screen, context, earlyBinding, promptLease, attempt, source));
                return;
            }

            if (!HasAvailableLobbyEndpoint())
            {
                ScreenState.MarkSettled(attempt);
                GD.Print($"sts2_lan_connect continue_run_publish: skip because lobby endpoint is missing for screen={context.ScreenType}");
                LanConnectPopupUtil.ShowInfo("当前客户端尚未绑定大厅服务，无法为这个多人续局自动恢复房间。");
                return;
            }

            if (LanConnectLobbyRuntime.Instance?.IsManagingNetService(context.NetService) == true)
            {
                ScreenState.MarkSettled(attempt);
                GD.Print($"sts2_lan_connect continue_run_publish: runtime already manages this host screen={context.ScreenType}");
                return;
            }

            endAttemptHere = false;
            TaskHelper.RunSafely(PublishAsync(screen, context, attempt, source));
        }
        finally
        {
            if (endAttemptHere)
            {
                ScreenState.EndAttempt(attempt);
            }
        }
    }

    private static async System.Threading.Tasks.Task PromptThenPublishAsync(
        Control screen,
        ContinuedRunHostContext context,
        LanConnectResolvedRoomBinding binding,
        LanConnectContinueRunPromptCoordinator.PromptLease promptLease,
        LanConnectContinueRunPublishAttempt attempt,
        string source)
    {
        try
        {
            LanConnectContinueRunPromptCoordinator.PromptResolution resolution =
                await PromptCoordinator.ResolveAsync(
                promptLease,
                cancellationToken => LanConnectContinueRunHostChannelPrompt.PromptAsync(
                    screen,
                    binding.RoomName,
                    cancellationToken),
                choice => LanConnectMultiplayerSaveRoomBinding.PersistHostBinding(
                    context.Run,
                    binding.RoomName,
                    binding.Password,
                    binding.GameMode,
                    choice,
                    "continue_save_channel_prompt"));
            if (ScreenState.IsAttemptStale(attempt))
            {
                return;
            }

            string? selectedHostChannel = resolution.Choice;
            if (selectedHostChannel == null)
            {
                // 用户取消联机方式选择只是本次没有结论，必须保持可重试：
                // 载入页节点被缓存复用，一旦在这里打上终态，本次游戏进程内就再也弹不出选择框。
                ScreenState.MarkRetryable(attempt);
                GD.Print(
                    $"sts2_lan_connect continue_run_publish: channel prompt canceled saveKey={binding.SaveKey}, source={source}");
                return;
            }

            if (!resolution.Persisted)
            {
                ScreenState.MarkRetryable(attempt);
                GD.Print(
                    $"sts2_lan_connect continue_run_publish: prompted host channel was not persisted; refusing publish saveKey={binding.SaveKey}, hostChannel={selectedHostChannel}, source={source}");
                LanConnectPopupUtil.ShowInfo("未能保存本次联机方式选择。为避免误发布，已停止恢复大厅房间；请返回后重试。");
                return;
            }

            GD.Print(
                $"sts2_lan_connect continue_run_publish: saved prompted host channel saveKey={binding.SaveKey}, hostChannel={selectedHostChannel}, schemaVersion={LanConnectSavedRoomBinding.CurrentSchemaVersion}, source={source}");

            if (string.Equals(selectedHostChannel, LanConnectHostChannels.Lan, StringComparison.Ordinal))
            {
                ScreenState.MarkSettled(attempt);
                if (GodotObject.IsInstanceValid(screen))
                {
                    LanConnectInviteButtonPatch.ScheduleEnsureInviteButton(screen, "continue_lan_prompt_choice");
                }
                GD.Print(
                    $"sts2_lan_connect continue_run_publish: skip after user selected LAN saveKey={binding.SaveKey}, source={source}");
                return;
            }

            if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree())
            {
                return;
            }

            if (!HasAvailableLobbyEndpoint())
            {
                ScreenState.MarkSettled(attempt);
                GD.Print($"sts2_lan_connect continue_run_publish: skip prompted publish because lobby endpoint is missing for screen={context.ScreenType}");
                LanConnectPopupUtil.ShowInfo("当前客户端尚未绑定大厅服务，无法为这个多人续局恢复房间。");
                return;
            }

            if (LanConnectLobbyRuntime.Instance?.IsManagingNetService(context.NetService) == true)
            {
                ScreenState.MarkSettled(attempt);
                GD.Print($"sts2_lan_connect continue_run_publish: runtime already manages prompted host screen={context.ScreenType}");
                return;
            }

            await PublishAsync(screen, context, attempt, source);
        }
        finally
        {
            ScreenState.EndAttempt(attempt);
            QueueRetryForNewerVisit(screen, attempt);
        }
    }

    private static async System.Threading.Tasks.Task PublishAsync(
        Control screen,
        ContinuedRunHostContext context,
        LanConnectContinueRunPublishAttempt attempt,
        string source)
    {
        // 绑定解析和配置读取也必须在 try 内：这里抛异常时若不释放 in-flight，
        // 缓存的载入页会一直停在“正在尝试”，后续恢复全部被静默丢弃。
        try
        {
            LanConnectResolvedRoomBinding binding = LanConnectMultiplayerSaveRoomBinding.Resolve(context.Run);
            LanConnectSavedRoomBinding? storedBinding = LanConnectConfig.TryGetSaveRoomBinding(binding.SaveKey);
            Dictionary<ulong, string> storedPlayerNames = LanConnectMultiplayerSaveRoomBinding.ParsePlayerNames(storedBinding?.PlayerNames);
            LobbySavedRunInfo savedRunInfo = LanConnectMultiplayerSaveRoomBinding.BuildSavedRunInfo(context.Run, context.NetService.NetId, storedPlayerNames);

            string? persistedHostChannel = storedBinding?.HostChannel ?? binding.HostChannel;
            int schemaVersion = storedBinding?.SchemaVersion ?? binding.SchemaVersion;
            if (!string.IsNullOrWhiteSpace(persistedHostChannel) && !LanConnectHostChannels.IsValid(persistedHostChannel))
            {
                GD.Print(
                    $"sts2_lan_connect continue_run_publish: warning unknown hostChannel={LanConnectHostChannels.DescribePersisted(persistedHostChannel)}, refusing publish without user choice");
            }

            string effectiveHostChannel = LanConnectHostChannels.Resolve(persistedHostChannel);
            LanConnectContinueRunPublishDecisionKind decision = LanConnectContinueRunPublishDecision.Decide(persistedHostChannel, schemaVersion);
            GD.Print(
                $"sts2_lan_connect continue_run_publish: attempt screen={context.ScreenType}, source={source}, saveKey={binding.SaveKey}, storedBinding={binding.HasStoredBinding}, roomName='{binding.RoomName}', passwordSet={!string.IsNullOrWhiteSpace(binding.Password)}, persistedHostChannel={LanConnectHostChannels.DescribePersisted(persistedHostChannel)}, effectiveHostChannel={effectiveHostChannel}, schemaVersion={schemaVersion}, decision={LanConnectContinueRunPublishDecision.ToLogToken(decision)}");

            if (decision == LanConnectContinueRunPublishDecisionKind.SkipLanOrigin)
            {
                ScreenState.MarkSettled(attempt);
                GD.Print(
                    $"sts2_lan_connect continue_run_publish: skip screen={context.ScreenType}, saveKey={binding.SaveKey}, decision=skip_lan_origin");
                return;
            }

            if (decision == LanConnectContinueRunPublishDecisionKind.Prompt)
            {
                ScreenState.MarkRetryable(attempt);
                GD.Print(
                    $"sts2_lan_connect continue_run_publish: refuse publish without resolved user choice screen={context.ScreenType}, saveKey={binding.SaveKey}");
                return;
            }

            LanConnectHostAttemptResult published = await LanConnectHostFlow.PublishExistingHostToLobbyAsync(
                context.NetService,
                binding.RoomName,
                binding.Password,
                context.GameMode,
                publishSource: $"continue_save:{context.ScreenType}",
                boundSaveKey: binding.SaveKey,
                savedRunInfo: savedRunInfo,
                maxPlayers: LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers(),
                notifyOnFailure: false,
                persistedSelection: binding.ProtocolSelection);
            if (ScreenState.IsAttemptStale(attempt))
            {
                return;
            }

            if (!published.Succeeded)
            {
                if (published.ProtocolFailure != null)
                {
                    LanConnectProtocolUiMessages.Present(published.ProtocolFailure);
                }
                ScreenState.MarkRetryable(attempt);
                GD.Print($"sts2_lan_connect continue_run_publish: publish failed screen={context.ScreenType}, saveKey={binding.SaveKey}");
                return;
            }

            bool bindingPersisted = LanConnectMultiplayerSaveRoomBinding.PersistHostBinding(
                context.Run,
                binding.RoomName,
                binding.Password,
                binding.GameMode,
                LanConnectHostChannels.Lobby,
                "continue_save_publish");
            if (!bindingPersisted)
            {
                GD.Print(
                    $"sts2_lan_connect continue_run_publish: published room but skipped post-publish binding refresh screen={context.ScreenType}, saveKey={binding.SaveKey}");
            }
            ScreenState.MarkSettled(attempt);
            if (GodotObject.IsInstanceValid(screen))
            {
                LanConnectInviteButtonPatch.ScheduleEnsureInviteButton(screen, "continue_save_publish");
            }
            GD.Print(
                $"sts2_lan_connect continue_run_publish: publish succeeded screen={context.ScreenType}, saveKey={binding.SaveKey}, roomName='{binding.RoomName}'");
            LanConnectPopupUtil.ShowInfo($"已为当前多人存档自动恢复大厅房间：{binding.RoomName}\n队友现在可以从“游戏大厅”重新加入。");
        }
        finally
        {
            ScreenState.EndAttempt(attempt);
            QueueRetryForNewerVisit(screen, attempt);
        }
    }

    private static bool TryResolveContext(Control screen, out ContinuedRunHostContext context)
    {
        LoadRunLobby? lobby = LanConnectLoadedRunContext.TryResolve(screen, out LoadRunLobby resolvedLobby)
            ? resolvedLobby
            : null;

        if (lobby?.NetService is not NetHostGameService netService)
        {
            context = null!;
            return false;
        }

        context = new ContinuedRunHostContext(netService, lobby.Run, lobby.GameMode, screen.GetType().Name);
        return true;
    }

    private static bool HasAvailableLobbyEndpoint()
    {
        return LanConnectConfig.HasLobbyServerOverrides || LanConnectLobbyEndpointDefaults.HasBundledDefaults();
    }

    private static void ClearState(Control screen)
    {
        ulong instanceId = screen.GetInstanceId();
        ScreenState.ForgetScreen(instanceId);
        PromptCoordinator.ClearScreen(instanceId);
    }

    private sealed record ContinuedRunHostContext(NetHostGameService NetService, SerializableRun Run, GameMode GameMode, string ScreenType);
}
