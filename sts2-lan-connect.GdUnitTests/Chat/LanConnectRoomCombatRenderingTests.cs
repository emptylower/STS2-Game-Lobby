using System.Collections;
using System.Reflection;
using Godot;
using GdUnit4;
using Sts2LanConnect.Scripts;
using static GdUnit4.Assertions;

namespace Sts2LanConnect.GdUnitTests.Chat;

[TestSuite]
[RequireGodotRuntime]
public sealed class LanConnectRoomCombatRenderingTests
{
    [TestCase]
    public async Task Overlay_composition_resolves_live_runs_preserves_order_and_invalidates_stably()
    {
        CombatContext live = ReadyContext();
        LanConnectRoomCombatReferenceResolver resolver = new(live, new LanConnectChatLocalizer());
        LanConnectRoomCombatRenderContext renderContext = RenderContext(resolver);
        LanConnectDualChatState state = StateWithMessage();
        SubViewport root = AutoFree(new SubViewport
        {
            Size = new Vector2I(1920, 1080),
            Disable3D = true
        })!;
        LanConnectRoomChatOverlay overlay = new();
        root.AddChild(overlay);
        using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        await runner.AwaitIdleFrame();
        overlay.ConfigureCombatRenderingForTests(() => renderContext);
        overlay.ConfigureForTests(
            state,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);
        await overlay.OpenForTests();
        await runner.AwaitIdleFrame();

        LanConnectBasicChatPanel panel = overlay.ChatPanelForTests;
        AssertRunMatrix(panel, "before ", "Strength +3", " middle ", "Silent", " after ");
        Control firstPower = Run(panel, 1);
        string staticItemText = RunText(Run(panel, 4));
        bool staticItemResolved = Run(panel, 4).HasMeta("lan_connect_resolved_item");
        ulong stablePowerInstance = firstPower.GetInstanceId();
        await runner.AwaitIdleFrame();
        await runner.AwaitIdleFrame();
        AssertThat(Run(panel, 1).GetInstanceId()).IsEqual(stablePowerInstance);

        live.Peers.Remove("3");
        renderContext = renderContext with { PeerTargetDirectoryFingerprint = "1:1:4:Host" };
        await runner.AwaitIdleFrame();
        AssertRunMatrix(
            panel,
            "before ",
            "Strength +3",
            " middle ",
            "Target is no longer available",
            " after ");
        AssertThat(Run(panel, 3).HasMeta("lan_connect_resolved_combat")).IsFalse();

        live.ActiveRoomSessionId = "generation-b";
        renderContext = renderContext with
        {
            RoomSessionId = "generation-b",
            PeerTargetDirectoryFingerprint = "1:1:4:Host\u001e1:3:6:Silent"
        };
        await runner.AwaitIdleFrame();
        AssertThat(RunText(Run(panel, 1))).IsEqual("Unknown power");
        AssertThat(RunText(Run(panel, 3))).IsEqual("Target is no longer available");
        AssertThat(RunText(Run(panel, 0))).IsEqual("before ");
        AssertThat(RunText(Run(panel, 2))).IsEqual(" middle ");
        AssertThat(RunText(Run(panel, 5))).IsEqual(" after ");
        AssertThat(RunText(Run(panel, 4))).IsEqual(staticItemText);
        AssertThat(Run(panel, 4).HasMeta("lan_connect_resolved_item")).IsEqual(staticItemResolved);

        renderContext = renderContext with { Locale = "zh-CN" };
        await runner.AwaitIdleFrame();
        AssertThat(RunText(Run(panel, 1))).IsEqual("未知能力");
        AssertThat(RunText(Run(panel, 3))).IsEqual("目标已不可用");

        renderContext = renderContext with { ModFingerprint = "mods-b" };
        await runner.AwaitIdleFrame();
        AssertThat(RunText(Run(panel, 4))).IsEqual(staticItemText);
        AssertThat(Run(panel, 4).HasMeta("lan_connect_resolved_item")).IsEqual(staticItemResolved);
    }

    [TestCase]
    public async Task Hover_resolves_again_and_contains_description_exception_to_one_segment()
    {
        CombatContext live = ReadyContext();
        LanConnectRoomCombatReferenceResolver resolver = new(live, new LanConnectChatLocalizer());
        LanConnectRoomCombatRenderContext renderContext = RenderContext(resolver);
        LanConnectChatChannelState state = new(LanConnectChatChannel.Room);
        state.SetEnabledRichFeatures(new LanConnectChatFeatureVersions(1, 1, 1, 1));
        state.AppendConfirmedContentForTests(
            "hover",
            "Silent",
            new LanConnectChatContent(1,
            [
                new LanConnectTextSegment("left "),
                new LanConnectPowerStateSegment("MegaCrit.Strength", -4, "generation-a", "1", "3"),
                new LanConnectTextSegment(" right"),
                new LanConnectItemRefSegment("relic", "MegaCrit.Anchor")
            ]),
            1,
            false);
        LanConnectBasicChatPanel panel = AutoFree(new LanConnectBasicChatPanel(
            LanConnectChatUiComposition.Icons,
            ResolveItem))!;
        panel.ConfigureCombatRendering(() => renderContext);
        using ISceneRunner runner = ISceneRunner.Load(panel, autoFree: true);
        panel.BindStructured(state, (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await runner.AwaitIdleFrame();

        Control power = Run(panel, 1);
        AssertThat(RunText(power)).IsEqual("Strength -4");
        AssertThat(power.TooltipText).Contains("Amount: -4");
        AssertThat(power.TooltipText).Contains("Owner: Host");
        AssertThat(power.TooltipText).Contains("Applied by: Silent");

        live.ThrowOnPower = true;
        power.EmitSignal(Control.SignalName.MouseEntered);
        await runner.AwaitIdleFrame();
        AssertThat(RunText(power)).IsEqual("Unknown power");
        AssertThat(power.HasMeta("lan_connect_resolved_combat")).IsFalse();
        AssertThat(power.MouseFilter).IsEqual(Control.MouseFilterEnum.Ignore);
        AssertThat(RunText(Run(panel, 0))).IsEqual("left ");
        AssertThat(RunText(Run(panel, 2))).IsEqual(" right");
        AssertThat(Run(panel, 3).HasMeta("lan_connect_resolved_item")).IsTrue();
    }

    [TestCase]
    public async Task Production_control_orchestration_rejects_superseded_mutation_send_and_cleanup()
    {
        LanConnectRoomSessionAuthority authority = new();
        LanConnectLegacyControlEnvelopeOrchestrator orchestrator = new(authority);
        object candidate = new();
        object activeSession = candidate;
        bool closing = false;
        int broadcasts = 0;
        const string roomId = "gdunit-authority-room";
        try
        {
            LanConnectLobbyPlayerNameDirectory.BeginRoom(roomId);
            LanConnectLobbyPlayerNameDirectory.Upsert(roomId, 1, "Host");
            authority.AfterOptimisticCheckForTests = () => authority.RunExclusive(
                () => activeSession = new object());

            bool applied = await orchestrator.ApplyHostedPlayerNameSyncAsync(
                () => activeSession,
                candidate,
                () => closing,
                () => LanConnectLobbyPlayerNameDirectory.Upsert(roomId, 2, "Stale"),
                () =>
                {
                    broadcasts++;
                    return Task.CompletedTask;
                });

            AssertThat(applied).IsFalse();
            AssertThat(LanConnectLobbyPlayerNameDirectory.TryGetPlayerName(1)).IsEqual("Host");
            AssertThat(LanConnectLobbyPlayerNameDirectory.TryGetPlayerName(2)).IsNull();
            AssertThat(broadcasts).IsEqual(0);

            object oldSession = new();
            activeSession = oldSession;
            LanConnectLobbyPlayerNameDirectory.BeginRoom(roomId);
            LanConnectLobbyPlayerNameDirectory.Upsert(roomId, 3, "OldGen");
            object newSession = new();
            authority.RunExclusive(() =>
            {
                activeSession = newSession;
                LanConnectLobbyPlayerNameDirectory.BeginRoom(roomId);
                LanConnectLobbyPlayerNameDirectory.Upsert(roomId, 9, "NewGen");
            });

            bool cleared = orchestrator.CleanupCurrentGeneration(
                () => activeSession,
                oldSession,
                () => LanConnectLobbyPlayerNameDirectory.ClearRoom(roomId));

            AssertThat(cleared).IsFalse();
            AssertThat(LanConnectLobbyPlayerNameDirectory.TryGetPlayerName(9)).IsEqual("NewGen");
            AssertThat(LanConnectLobbyPlayerNameDirectory.TryGetPlayerName(3)).IsNull();
        }
        finally
        {
            LanConnectLobbyPlayerNameDirectory.ClearRoom(roomId);
        }
    }

    [TestCase]
    public void Replace_snapshot_removes_departed_peer_from_directory_and_null_platform_projection()
    {
        const string roomId = "replace-snapshot-room";
        try
        {
            LanConnectLobbyPlayerNameDirectory.ReplaceSnapshot(roomId,
            [
                PlayerName("1", "Host"),
                PlayerName("2", "Defect"),
                PlayerName("3", "Silent")
            ]);
            LanConnectLobbyPlayerNameDirectory.ReplaceSnapshot(roomId,
            [
                PlayerName("1", "Host2"),
                PlayerName("3", "Silent2")
            ]);

            List<LobbyPlayerNameEntry> snapshot = LanConnectLobbyPlayerNameDirectory.BuildSnapshot(roomId);
            AssertThat(snapshot.Select(static entry => entry.PlayerNetId).ToArray())
                .ContainsExactly("1", "3");
            AssertThat(snapshot.Select(static entry => entry.PlayerName).ToArray())
                .ContainsExactly("Host2", "Silent2");
            AssertThat(LanConnectLobbyPlayerNameDirectory.TryGetPlayerName(2)).IsNull();
            AssertThat(NullPlatformProjectedPeerIds()).ContainsExactly(1UL, 3UL);
        }
        finally
        {
            LanConnectLobbyPlayerNameDirectory.ClearRoom(roomId);
        }
    }

    [TestCase]
    public void Replace_snapshot_exception_leaves_directory_lookup_and_projection_unchanged()
    {
        const string roomId = "replace-snapshot-exception-room";
        int enumerations = 0;
        IEnumerable<LobbyPlayerNameEntry> ThrowingSnapshot()
        {
            enumerations++;
            yield return PlayerName("2", "Partial");
            throw new InvalidOperationException("snapshot enumeration failed");
        }

        try
        {
            LanConnectLobbyPlayerNameDirectory.ReplaceSnapshot(roomId,
            [
                PlayerName("1", "Host"),
                PlayerName("3", "Silent")
            ]);
            string beforeDirectory = DirectoryFingerprint(roomId);
            string beforeProjection = NullPlatformProjectionFingerprint();
            Exception? observed = null;
            try
            {
                LanConnectLobbyPlayerNameDirectory.ReplaceSnapshot(roomId, ThrowingSnapshot());
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            AssertThat(observed is InvalidOperationException).IsTrue();
            AssertThat(enumerations).IsEqual(1);
            AssertThat(DirectoryFingerprint(roomId)).IsEqual(beforeDirectory);
            AssertThat(LanConnectLobbyPlayerNameDirectory.TryGetPlayerName(1)).IsEqual("Host");
            AssertThat(LanConnectLobbyPlayerNameDirectory.TryGetPlayerName(2)).IsNull();
            AssertThat(LanConnectLobbyPlayerNameDirectory.TryGetPlayerName(3)).IsEqual("Silent");
            AssertThat(NullPlatformProjectionFingerprint()).IsEqual(beforeProjection);
        }
        finally
        {
            LanConnectLobbyPlayerNameDirectory.ClearRoom(roomId);
        }
    }

    [TestCase]
    public void Replace_snapshot_enumerates_source_once_and_commits_complete_replacement()
    {
        const string roomId = "replace-snapshot-single-enumeration-room";
        int enumerations = 0;
        IEnumerable<LobbyPlayerNameEntry> SingleUseSnapshot()
        {
            enumerations++;
            if (enumerations > 1)
            {
                throw new InvalidOperationException("snapshot was enumerated twice");
            }
            yield return PlayerName("4", "Watcher");
            yield return PlayerName("5", "Defect");
        }

        try
        {
            LanConnectLobbyPlayerNameDirectory.ReplaceSnapshot(roomId, SingleUseSnapshot());

            AssertThat(enumerations).IsEqual(1);
            AssertThat(DirectoryFingerprint(roomId)).IsEqual("4:Watcher|5:Defect");
            AssertThat(NullPlatformProjectionFingerprint()).IsEqual("4:Watcher|5:Defect");
        }
        finally
        {
            LanConnectLobbyPlayerNameDirectory.ClearRoom(roomId);
        }
    }

    [TestCase]
    public void Joined_snapshot_prepare_exception_cannot_partially_commit_peer_or_name_directory()
    {
        const string roomId = "joined-prepare-exception-room";
        LanConnectRoomSessionAuthority authority = new();
        LanConnectLegacyControlEnvelopeOrchestrator orchestrator = new(authority);
        object candidate = new();
        object activeSession = candidate;
        bool closing = false;
        IReadOnlySet<ulong> peerSet = new HashSet<ulong> { 1, 3 };
        IEnumerable<LobbyPlayerNameEntry> ThrowingSnapshot()
        {
            yield return PlayerName("2", "Partial");
            throw new InvalidOperationException("joined snapshot preparation failed");
        }

        try
        {
            LanConnectLobbyPlayerNameDirectory.ReplaceSnapshot(roomId,
            [
                PlayerName("1", "Host"),
                PlayerName("3", "Silent")
            ]);
            string beforeDirectory = DirectoryFingerprint(roomId);
            string beforeProjection = NullPlatformProjectionFingerprint();
            Exception? observed = null;
            try
            {
                orchestrator.ApplyJoinedPlayerNameSnapshot(
                    () => activeSession,
                    candidate,
                    () => closing,
                    () => LanConnectLobbyRuntime.PrepareJoinedPlayerNameSnapshot(ThrowingSnapshot()),
                    snapshot =>
                    {
                        LanConnectLobbyPlayerNameDirectory.ReplaceSnapshot(roomId, snapshot.Entries);
                        peerSet = snapshot.PeerIds;
                    });
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            AssertThat(observed is InvalidOperationException).IsTrue();
            AssertThat(peerSet.Order().ToArray()).ContainsExactly(1UL, 3UL);
            AssertThat(DirectoryFingerprint(roomId)).IsEqual(beforeDirectory);
            AssertThat(NullPlatformProjectionFingerprint()).IsEqual(beforeProjection);
        }
        finally
        {
            LanConnectLobbyPlayerNameDirectory.ClearRoom(roomId);
        }
    }

    private static LanConnectDualChatState StateWithMessage()
    {
        LanConnectChatChannelState server = new(LanConnectChatChannel.Server);
        server.SetPresentationForTests(LanConnectServerChatPresentation.Unsupported);
        LanConnectDualChatState state = new(server);
        state.EnterRoom("room-a");
        state.Room.SetEnabledRichFeatures(new LanConnectChatFeatureVersions(1, 1, 1, 1));
        state.Room.AppendConfirmedContentForTests(
            "combat-matrix",
            "Silent",
            new LanConnectChatContent(1,
            [
                new LanConnectTextSegment("before "),
                new LanConnectPowerStateSegment("MegaCrit.Strength", 3, "generation-a"),
                new LanConnectTextSegment(" middle "),
                new LanConnectTargetRefSegment("player", "3", "generation-a"),
                new LanConnectItemRefSegment("relic", "MegaCrit.Anchor"),
                new LanConnectTextSegment(" after ")
            ]),
            1,
            false);
        return state;
    }

    private static LobbyPlayerNameEntry PlayerName(string playerNetId, string playerName) => new()
    {
        PlayerNetId = playerNetId,
        PlayerName = playerName
    };

    private static ulong[] NullPlatformProjectedPeerIds()
        => NullPlatformProjectedNames().Keys.Order().ToArray();

    private static string DirectoryFingerprint(string roomId) => string.Join(
        '|',
        LanConnectLobbyPlayerNameDirectory.BuildSnapshot(roomId)
            .Select(static entry => $"{entry.PlayerNetId}:{entry.PlayerName}"));

    private static string NullPlatformProjectionFingerprint() => string.Join(
        '|',
        NullPlatformProjectedNames()
            .OrderBy(static pair => pair.Key)
            .Select(static pair => $"{pair.Key}:{pair.Value}"));

    private static Dictionary<ulong, string> NullPlatformProjectedNames()
    {
        Type platformUtil = typeof(MegaCrit.Sts2.Core.Platform.PlatformUtil);
        Type nullStrategy = typeof(MegaCrit.Sts2.Core.Platform.Null.NullPlatformUtilStrategy);
        object? instance = platformUtil
            .GetField("_null", BindingFlags.Static | BindingFlags.NonPublic)?
            .GetValue(null);
        IEnumerable? names = nullStrategy
            .GetField("_mpNames", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(instance) as IEnumerable;
        if (names == null)
        {
            return [];
        }

        Dictionary<ulong, string> projected = [];
        foreach (object name in names)
        {
            object? netIdValue = name.GetType()
                .GetField("netId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(name);
            object? nameValue = name.GetType()
                .GetField("name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                .GetValue(name);
            if (netIdValue is ulong peerId && nameValue is string playerName)
            {
                projected[peerId] = playerName;
            }
        }
        return projected;
    }

    private static CombatContext ReadyContext() => new()
    {
        ActiveRoomSessionId = "generation-a",
        Peers = { "1", "3" },
        Names = { ["1"] = "Host", ["3"] = "Silent" },
        Powers = { ["MegaCrit.Strength"] = new("Strength", "Gain attack damage.") }
    };

    private static LanConnectRoomCombatRenderContext RenderContext(
        LanConnectRoomCombatReferenceResolver resolver) => new(
        resolver,
        "en-US",
        "mods-a",
        "generation-a",
        "1:1:4:Host\u001e1:3:6:Silent",
        FreshReady: true);

    private static void AssertRunMatrix(
        Node panel,
        string first,
        string power,
        string middle,
        string target,
        string after)
    {
        AssertThat(RunText(Run(panel, 0))).IsEqual(first);
        AssertThat(RunText(Run(panel, 1))).IsEqual(power);
        AssertThat(RunText(Run(panel, 2))).IsEqual(middle);
        AssertThat(RunText(Run(panel, 3))).IsEqual(target);
        AssertThat(Run(panel, 4)).IsNotNull();
        AssertThat(RunText(Run(panel, 5))).IsEqual(after);
    }

    private static Control Run(Node panel, int index) =>
        (Control)panel.FindChild($"ChatMessageRun{index}", recursive: true, owned: false);

    private static string RunText(Control run) => run switch
    {
        Label label => label.Text,
        _ => run.FindChildren("*", "Label", true, false)
            .OfType<Label>()
            .Select(label => label.Text)
            .FirstOrDefault() ?? string.Empty
    };

    private static LanConnectResolvedItem ResolveItem(LanConnectItemRun run) => new(
        LanConnectResolvedItemStatus.Resolved,
        run.ItemType,
        "chat.relic",
        "Anchor",
        "Anchor",
        new LanConnectHoverTipPreviewData("relic", "Anchor", "Description", null));

    private sealed class CombatContext : ILanConnectRoomCombatContext
    {
        public string ActiveRoomSessionId { get; set; } = string.Empty;

        internal HashSet<string> Peers { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, LanConnectLocalPowerReference> Powers { get; } = new(StringComparer.Ordinal);

        internal bool ThrowOnPower { get; set; }

        public bool IsCurrentPeer(string playerNetId) => Peers.Contains(playerNetId);

        public bool TryGetCurrentPeerName(string playerNetId, out string name) =>
            Names.TryGetValue(playerNetId, out name!);

        public bool TryResolveLocalPower(string modelId, out LanConnectLocalPowerReference power)
        {
            if (ThrowOnPower)
            {
                throw new InvalidOperationException("power description failed");
            }
            return Powers.TryGetValue(modelId, out power);
        }
    }
}
