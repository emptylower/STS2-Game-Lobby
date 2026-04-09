using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2LanConnect.Scripts;

internal static class TreasurePatches
{
    private const int VanillaMultiplayerHolderCount = 4;
    private const float FallbackRelicHolderXStep = 220f;
    private const float MinRelicHolderXStep = 190f;
    private const float MinRelicHolderYStep = 120f;
    private const int SkipVoteIndex = -1;

    private static readonly Dictionary<NTreasureRoomRelicCollection, NChoiceSelectionSkipButton> SkipButtons = new();
    private static readonly Dictionary<string, Dictionary<string, string>> LocalizationCache = new();

    private static readonly FieldInfo? HoldersInUseField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_holdersInUse");
    private static readonly FieldInfo? MultiplayerHoldersField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_multiplayerHolders");
    private static readonly FieldInfo? RunStateField = AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_runState");
    private static readonly FieldInfo? SyncPlayerCollectionField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_playerCollection");
    private static readonly FieldInfo? SyncLocalPlayerIdField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_localPlayerId");
    private static readonly FieldInfo? SyncActionQueueField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_actionQueueSynchronizer");
    private static readonly FieldInfo? SyncCurrentRelicsField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_currentRelics");
    private static readonly FieldInfo? SyncRngField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_rng");
    private static readonly FieldInfo? SyncVotesField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes");
    private static readonly FieldInfo? SyncPredictedVoteField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_predictedVote");
    private static readonly FieldInfo? VotesChangedEventField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "VotesChanged");
    private static readonly FieldInfo? RelicsAwardedEventField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "RelicsAwarded");
    private static readonly MethodInfo? EndRelicVotingMethod = AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), "EndRelicVoting");

    private static readonly HashSet<TreasureRoomRelicSynchronizer> LocalVotePendingStates = new();
    private static readonly HashSet<TreasureRoomRelicSynchronizer> LocalSkipLockedStates = new();

    public static void Apply(Harmony harmony)
    {
        MethodInfo? initRelics = AccessTools.Method(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection.InitializeRelics));
        if (initRelics != null)
        {
            harmony.Patch(initRelics,
                prefix: new HarmonyMethod(typeof(TreasurePatches), nameof(InitializeRelicsPrefix)),
                postfix: new HarmonyMethod(typeof(TreasurePatches), nameof(InitializeRelicsPostfix)));
            harmony.Patch(initRelics,
                postfix: new HarmonyMethod(typeof(TreasurePatches), nameof(InitializeSkipPostfix)));
        }

        MethodInfo? defaultFocus = AccessTools.PropertyGetter(typeof(NTreasureRoomRelicCollection), "DefaultFocusedControl");
        if (defaultFocus != null)
        {
            harmony.Patch(defaultFocus, prefix: new HarmonyMethod(typeof(TreasurePatches), nameof(DefaultFocusPrefix)));
        }

        MethodInfo? ready = AccessTools.Method(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection._Ready));
        if (ready != null)
        {
            harmony.Patch(ready, postfix: new HarmonyMethod(typeof(TreasurePatches), nameof(ReadyPostfix)));
        }

        MethodInfo? setEnabled = AccessTools.Method(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection.SetSelectionEnabled));
        if (setEnabled != null)
        {
            harmony.Patch(setEnabled, postfix: new HarmonyMethod(typeof(TreasurePatches), nameof(SetSelectionEnabledPostfix)));
        }

        MethodInfo? exitTree = AccessTools.Method(typeof(NTreasureRoomRelicCollection), "_ExitTree");
        if (exitTree != null)
        {
            harmony.Patch(exitTree, prefix: new HarmonyMethod(typeof(TreasurePatches), nameof(ExitTreePrefix)));
        }

        MethodInfo? pickLocally = AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), "PickRelicLocally");
        if (pickLocally != null)
        {
            harmony.Patch(pickLocally, prefix: new HarmonyMethod(typeof(TreasurePatches), nameof(PickRelicLocallyPrefix)));
        }

        MethodInfo? beginPicking = AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking));
        if (beginPicking != null)
        {
            harmony.Patch(beginPicking,
                postfix: new HarmonyMethod(typeof(TreasurePatches), nameof(BeginRelicPickingPostfix)));
            harmony.Patch(beginPicking,
                postfix: new HarmonyMethod(typeof(TreasurePatches), nameof(BeginRelicPickingStrawberryPostfix)));
        }

        MethodInfo? completeNoRelics = AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), "CompleteWithNoRelics");
        if (completeNoRelics != null)
        {
            harmony.Patch(completeNoRelics, prefix: new HarmonyMethod(typeof(TreasurePatches), nameof(CompleteNoRelicsPrefix)));
        }

        MethodInfo? onPicked = AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.OnPicked));
        if (onPicked != null)
        {
            harmony.Patch(onPicked, prefix: new HarmonyMethod(typeof(TreasurePatches), nameof(OnPickedPrefix)));
        }

        Log.Info("sts2_lan_connect gameplay: treasure patches applied.");
    }

    // ReSharper disable UnusedMember.Local UnusedParameter.Local

    private static void InitializeRelicsPrefix(NTreasureRoomRelicCollection __instance)
    {
        List<NTreasureRoomRelicHolder>? holdersInUse = GetHoldersInUse(__instance);
        holdersInUse?.Clear();
        List<NTreasureRoomRelicHolder>? multiplayerHolders = GetMultiplayerHolders(__instance);
        if (multiplayerHolders != null && multiplayerHolders.Count > VanillaMultiplayerHolderCount)
        {
            for (int i = multiplayerHolders.Count - 1; i >= VanillaMultiplayerHolderCount; i--)
            {
                NTreasureRoomRelicHolder holder = multiplayerHolders[i];
                multiplayerHolders.RemoveAt(i);
                holder.QueueFree();
            }
        }

        IReadOnlyList<RelicModel>? currentRelics = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
        if (multiplayerHolders == null || currentRelics == null || currentRelics.Count <= multiplayerHolders.Count || multiplayerHolders.Count == 0)
        {
            return;
        }

        NTreasureRoomRelicHolder template = multiplayerHolders[^1];
        string scenePath = template.SceneFilePath;
        PackedScene? scene = !string.IsNullOrEmpty(scenePath) ? PreloadManager.Cache.GetScene(scenePath) : null;
        Node parent = template.GetParent();

        for (int i = multiplayerHolders.Count; i < currentRelics.Count; i++)
        {
            NTreasureRoomRelicHolder? newHolder = scene != null
                ? scene.Instantiate<NTreasureRoomRelicHolder>()
                : template.Duplicate() as NTreasureRoomRelicHolder;
            if (newHolder == null)
            {
                continue;
            }

            newHolder.Name = $"AutoHolder_{i + 1}";
            newHolder.Visible = false;
            parent.AddChild(newHolder);
            multiplayerHolders.Add(newHolder);
        }
    }

    private static void InitializeRelicsPostfix(NTreasureRoomRelicCollection __instance)
    {
        List<NTreasureRoomRelicHolder>? holdersInUse = GetHoldersInUse(__instance);
        if (holdersInUse == null || holdersInUse.Count <= VanillaMultiplayerHolderCount)
        {
            return;
        }

        float minX = float.MaxValue, maxX = float.MinValue;
        float topY = float.MaxValue, bottomY = float.MinValue;
        for (int i = 0; i < VanillaMultiplayerHolderCount; i++)
        {
            Vector2 pos = holdersInUse[i].Position;
            minX = Math.Min(minX, pos.X);
            maxX = Math.Max(maxX, pos.X);
            topY = Math.Min(topY, pos.Y);
            bottomY = Math.Max(bottomY, pos.Y);
        }

        int holderCount = holdersInUse.Count;
        int maxColumns = holderCount >= 8 ? VanillaMultiplayerHolderCount : Math.Min(VanillaMultiplayerHolderCount, holderCount);
        maxColumns = Math.Max(2, maxColumns);
        int rowCount = (int)Math.Ceiling(holderCount / (float)maxColumns);
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (topY + bottomY) * 0.5f;
        float xStep = (maxX - minX) / Math.Max(1, maxColumns - 1);
        xStep = xStep > 0f ? Math.Max(MinRelicHolderXStep, xStep) : FallbackRelicHolderXStep;
        float yStep = Math.Max(MinRelicHolderYStep, Math.Abs(bottomY - topY));

        int startIndex = 0;
        for (int i = 0; i < rowCount; i++)
        {
            int count = Math.Min(maxColumns, holderCount - startIndex);
            float y = centerY + (i - (rowCount - 1) * 0.5f) * yStep;
            LayoutRow(holdersInUse, startIndex, count, y, centerX, xStep);
            startIndex += count;
        }
    }

    private static bool DefaultFocusPrefix(NTreasureRoomRelicCollection __instance, ref Control __result)
    {
        List<NTreasureRoomRelicHolder>? holdersInUse = GetHoldersInUse(__instance);
        if (holdersInUse == null || holdersInUse.Count == 0)
        {
            return true;
        }

        IRunState? runState = GetRunState(__instance);
        int playerSlotIndex = 0;
        Player? me = runState != null ? LocalContext.GetMe(runState.Players) : null;
        if (me != null && runState != null)
        {
            playerSlotIndex = runState.GetPlayerSlotIndex(me);
        }

        playerSlotIndex = Math.Clamp(playerSlotIndex, 0, holdersInUse.Count - 1);
        __result = holdersInUse[playerSlotIndex];
        return false;
    }

    private static void ReadyPostfix(NTreasureRoomRelicCollection __instance)
    {
        EnsureSkipButton(__instance, out _);
    }

    private static void InitializeSkipPostfix(NTreasureRoomRelicCollection __instance)
    {
        if (!EnsureSkipButton(__instance, out NChoiceSelectionSkipButton? button) || button == null)
        {
            return;
        }

        button.Visible = true;
        SetSkipButtonState(button, isEnabled: !IsSkipButtonInteractionBlocked());
        UpdateSkipButtonLayout(__instance);
        button.AnimateIn();
    }

    private static void SetSelectionEnabledPostfix(NTreasureRoomRelicCollection __instance, bool isEnabled)
    {
        if (!EnsureSkipButton(__instance, out NChoiceSelectionSkipButton? button) || button == null)
        {
            return;
        }

        SetSkipButtonState(button, isEnabled && !IsSkipButtonInteractionBlocked());
    }

    private static void ExitTreePrefix(NTreasureRoomRelicCollection __instance)
    {
        SkipButtons.Remove(__instance);
    }

    private static bool PickRelicLocallyPrefix(TreasureRoomRelicSynchronizer __instance, int index)
    {
        if (index != SkipVoteIndex)
        {
            return !LocalSkipLockedStates.Contains(__instance);
        }

        if (LocalVotePendingStates.Contains(__instance))
        {
            return false;
        }

        IPlayerCollection? playerCollection = GetSyncPlayerCollection(__instance);
        ulong? localPlayerId = GetSyncLocalPlayerId(__instance);
        ActionQueueSynchronizer? actionQueue = GetSyncActionQueue(__instance);
        if (playerCollection == null || !localPlayerId.HasValue || actionQueue == null)
        {
            return false;
        }

        Player? player = playerCollection.GetPlayer(localPlayerId.Value) ?? LocalContext.GetMe(playerCollection.Players);
        if (player == null)
        {
            return false;
        }

        LocalVotePendingStates.Add(__instance);
        LocalSkipLockedStates.Add(__instance);
        SetSyncPredictedVote(__instance, SkipVoteIndex);
        actionQueue.RequestEnqueue(new LanConnectSkipRelicGameAction(player));
        InvokeVotesChanged(__instance);
        return false;
    }

    private static void BeginRelicPickingPostfix(TreasureRoomRelicSynchronizer __instance)
    {
        ClearLocalVoteState(__instance);
    }

    private static void CompleteNoRelicsPrefix(TreasureRoomRelicSynchronizer __instance)
    {
        ClearLocalVoteState(__instance);
    }

    private static bool OnPickedPrefix(TreasureRoomRelicSynchronizer __instance, Player player, int index)
    {
        List<RelicModel>? syncCurrentRelics = GetSyncCurrentRelics(__instance);
        IPlayerCollection? syncPlayerCollection = GetSyncPlayerCollection(__instance);
        List<int?>? syncVotes = GetSyncVotes(__instance);
        bool hasSkipInvolved = (syncVotes != null && syncVotes.Any(vote => vote == SkipVoteIndex)) || index == SkipVoteIndex;

        if (!hasSkipInvolved)
        {
            return true;
        }

        if (syncCurrentRelics == null || syncPlayerCollection == null || syncVotes == null)
        {
            return true;
        }

        if (index != SkipVoteIndex && (index < 0 || index >= syncCurrentRelics.Count))
        {
            return false;
        }

        int playerSlotIndex = syncPlayerCollection.GetPlayerSlotIndex(player);
        if (playerSlotIndex < 0)
        {
            if (LocalContext.IsMe(player))
            {
                LocalVotePendingStates.Remove(__instance);
                SetSyncPredictedVote(__instance, null);
                InvokeVotesChanged(__instance);
            }

            return false;
        }

        while (syncVotes.Count <= playerSlotIndex)
        {
            syncVotes.Add(null);
        }

        syncVotes[playerSlotIndex] = index;
        if (LocalContext.IsMe(player))
        {
            LocalVotePendingStates.Remove(__instance);
            SetSyncPredictedVote(__instance, null);
        }

        InvokeVotesChanged(__instance);

        int expectedCount = syncPlayerCollection.Players.Count;
        bool allVoted = syncVotes.Count >= expectedCount && syncVotes.Take(expectedCount).All(vote => vote.HasValue);
        if (allVoted)
        {
            ResolveAllVotes(__instance, syncCurrentRelics, syncPlayerCollection, syncVotes, expectedCount);
        }

        return false;
    }

    private static void BeginRelicPickingStrawberryPostfix(TreasureRoomRelicSynchronizer __instance)
    {
        List<RelicModel>? currentRelics = GetSyncCurrentRelics(__instance);
        if (currentRelics == null)
        {
            return;
        }

        bool hasChanges = false;
        for (int i = 0; i < currentRelics.Count; i++)
        {
            if (currentRelics[i] == null)
            {
                RelicModel? strawberry = ModelDb.Relic<Strawberry>();
                if (strawberry != null)
                {
                    currentRelics[i] = strawberry;
                    hasChanges = true;
                }
            }
        }

        if (hasChanges)
        {
            InvokeVotesChanged(__instance);
        }
    }

    // ReSharper restore UnusedMember.Local UnusedParameter.Local

    private static void LayoutRow(List<NTreasureRoomRelicHolder> holders, int startIndex, int count, float y, float centerX, float xStep)
    {
        float startX = centerX - (count - 1) * xStep * 0.5f;
        for (int i = 0; i < count; i++)
        {
            holders[startIndex + i].Position = new Vector2(startX + i * xStep, y);
        }
    }

    private static void OnSkipReleased(NButton button)
    {
        TreasureRoomRelicSynchronizer synchronizer = RunManager.Instance.TreasureRoomRelicSynchronizer;
        if (synchronizer.CurrentRelics == null)
        {
            return;
        }

        if (button.GetParent() is not NTreasureRoomRelicCollection collection)
        {
            return;
        }

        collection.SetSelectionEnabled(isEnabled: false);
        synchronizer.PickRelicLocally(SkipVoteIndex);
    }

    private static List<NTreasureRoomRelicHolder>? GetHoldersInUse(NTreasureRoomRelicCollection collection)
        => HoldersInUseField?.GetValue(collection) as List<NTreasureRoomRelicHolder>;

    private static List<NTreasureRoomRelicHolder>? GetMultiplayerHolders(NTreasureRoomRelicCollection collection)
        => MultiplayerHoldersField?.GetValue(collection) as List<NTreasureRoomRelicHolder>;

    private static IRunState? GetRunState(NTreasureRoomRelicCollection collection)
        => RunStateField?.GetValue(collection) as IRunState;

    private static IPlayerCollection? GetSyncPlayerCollection(TreasureRoomRelicSynchronizer sync)
        => SyncPlayerCollectionField?.GetValue(sync) as IPlayerCollection;

    private static ulong? GetSyncLocalPlayerId(TreasureRoomRelicSynchronizer sync)
        => SyncLocalPlayerIdField?.GetValue(sync) is ulong id ? id : null;

    private static ActionQueueSynchronizer? GetSyncActionQueue(TreasureRoomRelicSynchronizer sync)
        => SyncActionQueueField?.GetValue(sync) as ActionQueueSynchronizer;

    private static List<int?>? GetSyncVotes(TreasureRoomRelicSynchronizer sync)
        => SyncVotesField?.GetValue(sync) as List<int?>;

    private static List<RelicModel>? GetSyncCurrentRelics(TreasureRoomRelicSynchronizer sync)
        => SyncCurrentRelicsField?.GetValue(sync) as List<RelicModel>;

    private static Rng? GetSyncRng(TreasureRoomRelicSynchronizer sync)
        => SyncRngField?.GetValue(sync) as Rng;

    private static void SetSyncPredictedVote(TreasureRoomRelicSynchronizer sync, int? vote)
    {
        if (SyncPredictedVoteField == null)
        {
            return;
        }

        Type fieldType = SyncPredictedVoteField.FieldType;
        if (fieldType == typeof(int?))
        {
            SyncPredictedVoteField.SetValue(sync, vote);
        }
        else if (fieldType == typeof(int))
        {
            SyncPredictedVoteField.SetValue(sync, vote ?? -1);
        }
    }

    private static void ClearLocalVoteState(TreasureRoomRelicSynchronizer sync)
    {
        LocalVotePendingStates.Remove(sync);
        LocalSkipLockedStates.Remove(sync);
        SetSyncPredictedVote(sync, null);
    }

    private static bool IsSkipButtonInteractionBlocked()
    {
        TreasureRoomRelicSynchronizer sync = RunManager.Instance.TreasureRoomRelicSynchronizer;
        return sync.CurrentRelics == null
            || LocalVotePendingStates.Contains(sync)
            || LocalSkipLockedStates.Contains(sync);
    }

    private static void ResolveAllVotes(
        TreasureRoomRelicSynchronizer sync,
        List<RelicModel> relics,
        IPlayerCollection players,
        List<int?> votes,
        int expectedCount)
    {
        if (votes.Take(expectedCount).All(vote => vote == SkipVoteIndex))
        {
            sync.CompleteWithNoRelics();
            return;
        }

        Dictionary<int, List<Player>> playersByRelicIndex = new();
        for (int i = 0; i < relics.Count; i++)
        {
            playersByRelicIndex[i] = new List<Player>();
        }

        for (int i = 0; i < expectedCount; i++)
        {
            if (!votes[i].HasValue || votes[i] == SkipVoteIndex)
            {
                continue;
            }

            int value = votes[i]!.Value;
            if (value < 0 || value >= relics.Count)
            {
                Log.Warn($"sts2_lan_connect treasure: invalid vote index {value} from player slot {i}, ignoring.");
                continue;
            }

            playersByRelicIndex[value].Add(players.Players[i]);
        }

        List<RelicPickingResult> results = new();
        List<RelicModel> unclaimedRelics = new();
        RelicPickingFightMove[] fightMoves = Enum.GetValues<RelicPickingFightMove>();
        Rng? rng = GetSyncRng(sync);

        for (int i = 0; i < relics.Count; i++)
        {
            List<Player> voters = playersByRelicIndex[i];
            if (voters.Count == 0)
            {
                unclaimedRelics.Add(relics[i]);
            }
            else if (voters.Count == 1)
            {
                results.Add(new RelicPickingResult
                {
                    type = RelicPickingResultType.OnlyOnePlayerVoted,
                    player = voters[0],
                    relic = relics[i]
                });
            }
            else
            {
                results.Add(RelicPickingResult.GenerateRelicFight(voters, relics[i],
                    () => rng != null ? rng.NextItem(fightMoves) : fightMoves[0]));
            }
        }

        HashSet<int> skipVoterSlots = new();
        for (int i = 0; i < expectedCount; i++)
        {
            if (votes[i] == SkipVoteIndex)
            {
                skipVoterSlots.Add(i);
            }
        }

        List<Player> playersWithoutRelic = players.Players
            .Where((p, slotIndex) => !skipVoterSlots.Contains(slotIndex) && results.All(r => r.player != p))
            .ToList();

        if (rng != null)
        {
            unclaimedRelics.StableShuffle(rng);
        }

        for (int i = 0; i < Math.Min(unclaimedRelics.Count, playersWithoutRelic.Count); i++)
        {
            results.Add(new RelicPickingResult
            {
                type = RelicPickingResultType.ConsolationPrize,
                player = playersWithoutRelic[i],
                relic = unclaimedRelics[i]
            });
        }

        if (results.Count > 0)
        {
            InvokeRelicsAwarded(sync, results);
        }

        ClearLocalVoteState(sync);
        InvokeEndRelicVoting(sync);
    }

    private static void InvokeVotesChanged(TreasureRoomRelicSynchronizer sync)
    {
        if (VotesChangedEventField?.GetValue(sync) is Action action)
        {
            action();
        }
    }

    private static void InvokeRelicsAwarded(TreasureRoomRelicSynchronizer sync, List<RelicPickingResult> results)
    {
        if (RelicsAwardedEventField?.GetValue(sync) is Action<List<RelicPickingResult>> action)
        {
            action(results);
        }
    }

    private static void InvokeEndRelicVoting(TreasureRoomRelicSynchronizer sync)
    {
        if (EndRelicVotingMethod == null)
        {
            Log.Warn("sts2_lan_connect treasure: EndRelicVoting method not found.");
            return;
        }

        EndRelicVotingMethod.Invoke(sync, null);
    }

    private static void SetSkipButtonState(NButton button, bool isEnabled)
    {
        if (isEnabled)
        {
            button.Enable();
            button.Modulate = Colors.White;
        }
        else
        {
            button.Disable();
            button.Modulate = new Color(0.5f, 0.5f, 0.5f, 1f);
        }
    }

    private static bool EnsureSkipButton(NTreasureRoomRelicCollection collection, out NChoiceSelectionSkipButton? button)
    {
        if (SkipButtons.TryGetValue(collection, out button) && button != null)
        {
            return true;
        }

        string scenePath = SceneHelper.GetScenePath("ui/choice_selection_skip_button");
        PackedScene? scene = PreloadManager.Cache.GetScene(scenePath);
        if (scene == null)
        {
            Log.Warn($"sts2_lan_connect treasure: failed to load skip button scene: {scenePath}");
            button = null;
            return false;
        }

        button = scene.Instantiate<NChoiceSelectionSkipButton>(PackedScene.GenEditState.Disabled);
        button.Name = "TreasureSkipButton";
        button.Position = new Vector2(0f, 420f);

        MegaLabel? label = button.GetNodeOrNull<MegaLabel>("Label");
        label?.SetTextAutoSize(GetLocalizedText("TREASURE_RELIC_SKIP_BUTTON", "跳过"));

        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(OnSkipReleased));
        collection.AddChild(button);
        collection.Connect(Control.SignalName.Resized, Callable.From(() => UpdateSkipButtonLayout(collection)));
        SkipButtons[collection] = button;
        return true;
    }

    private static void UpdateSkipButtonLayout(NTreasureRoomRelicCollection collection)
    {
        if (!SkipButtons.TryGetValue(collection, out NChoiceSelectionSkipButton? button))
        {
            return;
        }

        Vector2 viewportSize = collection.GetViewportRect().Size;
        Vector2 buttonSize = button.Size;
        if (buttonSize == Vector2.Zero)
        {
            buttonSize = button.GetCombinedMinimumSize();
        }

        float marginX = 36f;
        float marginY = 110f;
        button.GlobalPosition = new Vector2(viewportSize.X - buttonSize.X - marginX, viewportSize.Y - buttonSize.Y - marginY);
    }

    private static string GetLocalizedText(string key, string fallbackText)
    {
        string languageCode = GetLanguageCode();
        if (TryGetLocValue(languageCode, key, out string value))
        {
            return value;
        }

        if (languageCode != "en_us" && TryGetLocValue("en_us", key, out value))
        {
            return value;
        }

        return fallbackText;
    }

    private static string GetLanguageCode()
    {
        string language = MegaCrit.Sts2.Core.Localization.LocManager.Instance?.Language ?? "eng";
        if (string.Equals(language, "zhs", StringComparison.OrdinalIgnoreCase))
        {
            return "zh_cn";
        }

        return "en_us";
    }

    private static bool TryGetLocValue(string languageCode, string key, out string value)
    {
        Dictionary<string, string> table = GetLocalizationTable(languageCode);
        if (table.TryGetValue(key, out string? result) && result != null)
        {
            value = result;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static Dictionary<string, string> GetLocalizationTable(string languageCode)
    {
        if (LocalizationCache.TryGetValue(languageCode, out Dictionary<string, string>? cached))
        {
            return cached;
        }

        string filePath = $"res://sts2_lan_connect/localization/{languageCode}.json";
        Dictionary<string, string> table = new();
        try
        {
            using Godot.FileAccess file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Read);
            if (file != null)
            {
                Dictionary<string, string>? parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText());
                if (parsed != null)
                {
                    table = parsed;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect treasure: failed to load localization: {filePath}. {ex.Message}");
        }

        LocalizationCache[languageCode] = table;
        return table;
    }
}
