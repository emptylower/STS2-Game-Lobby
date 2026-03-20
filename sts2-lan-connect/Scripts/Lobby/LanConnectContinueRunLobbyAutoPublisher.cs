using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectContinueRunLobbyAutoPublisher
{
    private const string HookedMetaKey = "sts2_lan_connect_continue_run_hooks";
    private const double RetryIntervalSeconds = 5d;

    private static readonly FieldInfo? MultiplayerLoadLobbyField = typeof(NMultiplayerLoadGameScreen).GetField("_runLobby", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CustomLoadLobbyField = typeof(NCustomRunLoadScreen).GetField("_lobby", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? DailyLoadLobbyField = typeof(NDailyRunLoadScreen).GetField("_lobby", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly HashSet<ulong> InFlightScreens = new();
    private static readonly HashSet<ulong> CompletedScreens = new();
    private static readonly Dictionary<ulong, DateTimeOffset> LastAttemptAt = new();

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
            screen.Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(() => QueueTryPublish(screen, "visibility_changed")));
            screen.Connect(Node.SignalName.TreeExiting, Callable.From(() => ClearState(screen)));
        }

        QueueTryPublish(screen, source);
    }

    private static void QueueTryPublish(Control screen, string source)
    {
        Callable.From(() => TryPublish(screen, source)).CallDeferred();
    }

    private static void TryPublish(Control screen, string source)
    {
        if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree() || !screen.IsNodeReady() || !screen.Visible)
        {
            return;
        }

        ulong instanceId = screen.GetInstanceId();
        if (CompletedScreens.Contains(instanceId) || InFlightScreens.Contains(instanceId))
        {
            return;
        }

        if (LastAttemptAt.TryGetValue(instanceId, out DateTimeOffset lastAttempt)
            && (DateTimeOffset.UtcNow - lastAttempt).TotalSeconds < RetryIntervalSeconds)
        {
            return;
        }

        if (!TryResolveContext(screen, out ContinuedRunHostContext context))
        {
            return;
        }

        if (context.NetService.Type != NetGameType.Host)
        {
            CompletedScreens.Add(instanceId);
            GD.Print($"sts2_lan_connect continue_run_publish: skip non-host screen type={context.ScreenType}, source={source}");
            return;
        }

        if (context.NetService.Platform != PlatformType.None)
        {
            CompletedScreens.Add(instanceId);
            GD.Print($"sts2_lan_connect continue_run_publish: skip because platform={context.NetService.Platform} for screen={context.ScreenType}");
            LanConnectPopupUtil.ShowInfo("当前多人续局是 Steam 会话，无法自动发布到大厅。请使用 --force-steam=off 启动游戏后再继续该存档。");
            return;
        }

        if (!HasAvailableLobbyEndpoint())
        {
            CompletedScreens.Add(instanceId);
            GD.Print($"sts2_lan_connect continue_run_publish: skip because lobby endpoint is missing for screen={context.ScreenType}");
            LanConnectPopupUtil.ShowInfo("当前客户端尚未绑定大厅服务，无法为这个多人续局自动恢复房间。");
            return;
        }

        if (LanConnectLobbyRuntime.Instance?.IsManagingNetService(context.NetService) == true)
        {
            CompletedScreens.Add(instanceId);
            GD.Print($"sts2_lan_connect continue_run_publish: runtime already manages this host screen={context.ScreenType}");
            return;
        }

        LastAttemptAt[instanceId] = DateTimeOffset.UtcNow;
        InFlightScreens.Add(instanceId);
        TaskHelper.RunSafely(PublishAsync(screen, context, source));
    }

    private static async System.Threading.Tasks.Task PublishAsync(Control screen, ContinuedRunHostContext context, string source)
    {
        LanConnectResolvedRoomBinding binding = LanConnectMultiplayerSaveRoomBinding.Resolve(context.Run);
        LobbySavedRunInfo savedRunInfo = LanConnectMultiplayerSaveRoomBinding.BuildSavedRunInfo(context.Run, context.NetService.NetId);
        GD.Print(
            $"sts2_lan_connect continue_run_publish: attempt screen={context.ScreenType}, source={source}, saveKey={binding.SaveKey}, storedBinding={binding.HasStoredBinding}, roomName='{binding.RoomName}', passwordSet={!string.IsNullOrWhiteSpace(binding.Password)}");

        try
        {
            bool published = await LanConnectHostFlow.PublishExistingHostToLobbyAsync(
                context.NetService,
                binding.RoomName,
                binding.Password,
                context.GameMode,
                publishSource: $"continue_save:{context.ScreenType}",
                boundSaveKey: binding.SaveKey,
                savedRunInfo: savedRunInfo,
                maxPlayers: LanConnectMultiplayerCompatibility.GetEffectiveMaxPlayers(),
                notifyOnFailure: false);
            if (!published)
            {
                GD.Print($"sts2_lan_connect continue_run_publish: publish failed screen={context.ScreenType}, saveKey={binding.SaveKey}");
                return;
            }

            LanConnectMultiplayerSaveRoomBinding.PersistBinding(context.Run, binding.RoomName, binding.Password, binding.GameMode, "continue_save_publish");
            CompletedScreens.Add(screen.GetInstanceId());
            GD.Print(
                $"sts2_lan_connect continue_run_publish: publish succeeded screen={context.ScreenType}, saveKey={binding.SaveKey}, roomName='{binding.RoomName}'");
            LanConnectPopupUtil.ShowInfo($"已为当前多人存档自动恢复大厅房间：{binding.RoomName}\n队友现在可以从“游戏大厅”重新加入。");
        }
        finally
        {
            InFlightScreens.Remove(screen.GetInstanceId());
        }
    }

    private static bool TryResolveContext(Control screen, out ContinuedRunHostContext context)
    {
        LoadRunLobby? lobby = screen switch
        {
            NMultiplayerLoadGameScreen multiplayerLoadScreen => GetLobby(screen, MultiplayerLoadLobbyField, multiplayerLoadScreen),
            NCustomRunLoadScreen customRunLoadScreen => GetLobby(screen, CustomLoadLobbyField, customRunLoadScreen),
            NDailyRunLoadScreen dailyRunLoadScreen => GetLobby(screen, DailyLoadLobbyField, dailyRunLoadScreen),
            _ => null
        };

        if (lobby?.NetService is not NetHostGameService netService)
        {
            context = null!;
            return false;
        }

        context = new ContinuedRunHostContext(netService, lobby.Run, lobby.GameMode, screen.GetType().Name);
        return true;
    }

    private static LoadRunLobby? GetLobby(Control screen, FieldInfo? field, object instance)
    {
        if (field == null)
        {
            GD.Print($"sts2_lan_connect continue_run_publish: missing reflection field for {screen.GetType().Name}");
            return null;
        }

        return field.GetValue(instance) as LoadRunLobby;
    }

    private static bool HasAvailableLobbyEndpoint()
    {
        return LanConnectConfig.HasLobbyServerOverrides || LanConnectLobbyEndpointDefaults.HasBundledDefaults();
    }

    private static void ClearState(Control screen)
    {
        ulong instanceId = screen.GetInstanceId();
        InFlightScreens.Remove(instanceId);
        CompletedScreens.Remove(instanceId);
        LastAttemptAt.Remove(instanceId);
    }

    private sealed record ContinuedRunHostContext(NetHostGameService NetService, SerializableRun Run, GameMode GameMode, string ScreenType);
}
