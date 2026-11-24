using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.ProtocolPlanTests;

public sealed class LanConnectLoadJoinCanonicalizationTests
{
    private static readonly ulong[] SavedIds = [900, 7, 600, 21, 400, 13, 200, 31];
    private static readonly MethodInfo RestoreMethod = typeof(LanConnectTailMessageRuntime)
        .GetMethod("RestoreLoadJoinPlayers", BindingFlags.NonPublic | BindingFlags.Static)!
        .MakeGenericMethod(typeof(LoadRunLobbyPlayer));

    public static IEnumerable<object[]> ThreePlayerOrders()
    {
        yield return [new[] { 0, 1, 2 }];
        yield return [new[] { 0, 2, 1 }];
        yield return [new[] { 1, 0, 2 }];
        yield return [new[] { 1, 2, 0 }];
        yield return [new[] { 2, 0, 1 }];
        yield return [new[] { 2, 1, 0 }];
    }

    [Theory]
    [MemberData(nameof(ThreePlayerOrders))]
    public void Actual_send_entry_canonicalizes_every_three_player_order(int[] order)
    {
        VerifyRoundTrip(SavedIds[..3], order.Select(index => SavedIds[index]).ToArray());
    }

    [Fact]
    public void Actual_send_entry_canonicalizes_all_hosted_eight_player_online_subsets()
    {
        for (int mask = 3; mask < 1 << SavedIds.Length; mask++)
        {
            if ((mask & 1) == 0 || System.Numerics.BitOperations.PopCount((uint)mask) < 2)
            {
                continue;
            }

            ulong[] connected = SavedIds.Where((_, index) => (mask & (1 << index)) != 0).Reverse().ToArray();
            VerifyRoundTrip(SavedIds, connected);
        }
    }

    [Fact]
    public void Actual_send_entry_preserves_ready_modded_and_native_list_order()
    {
        VerifyRoundTrip(SavedIds, [SavedIds[7], SavedIds[0], SavedIds[5], SavedIds[2],
            SavedIds[6], SavedIds[1], SavedIds[4], SavedIds[3]]);
    }

    [Fact]
    public void Saved_host_can_occupy_a_nonzero_slot_in_a_sparse_online_subset()
    {
        VerifyRoundTrip(SavedIds, [SavedIds[5], SavedIds[7], SavedIds[1]], SavedIds[5]);
    }

    [Fact]
    public void Send_rejects_duplicate_connected_ids_without_committing_a_snapshot()
    {
        LanConnectRosterAuthorityState authority = new(SavedIds[0]);
        ClientLoadJoinResponseMessage message = Message(SavedIds[..3], [SavedIds[0], SavedIds[1], SavedIds[1]]);

        Assert.Throws<InvalidDataException>(() => LanConnectTailMessageRuntime.PrepareLoadJoin(
            message, Selection(), authority));

        Assert.Null(authority.Current);
    }

    [Fact]
    public void Send_rejects_connected_players_missing_from_the_save()
    {
        Assert.Throws<InvalidDataException>(() => LanConnectTailMessageRuntime.PrepareLoadJoin(
            Message(SavedIds[..3], [SavedIds[0], 999UL]), Selection(), new(SavedIds[0])));
    }

    [Fact]
    public void Send_rejects_ambiguous_duplicate_saved_ids()
    {
        Assert.Throws<InvalidDataException>(() => LanConnectTailMessageRuntime.PrepareLoadJoin(
            Message([SavedIds[0], SavedIds[1], SavedIds[1]], [SavedIds[0], SavedIds[1]]),
            Selection(), new(SavedIds[0])));
    }

    [Fact]
    public void Receive_still_rejects_an_unmodified_noncanonical_old_host_body()
    {
        ClientLoadJoinResponseMessage source = Message(SavedIds[..3], [SavedIds[0], SavedIds[2], SavedIds[1]]);
        LanConnectRosterAuthorityState authority = new(SavedIds[0]);
        _ = LanConnectTailMessageRuntime.PrepareLoadJoin(source, Selection(), authority);

        Assert.Throws<InvalidDataException>(() => Restore(
            authority.Current!, source.serializableRun, source.playersAlreadyConnected));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Receive_still_rejects_duplicate_or_missing_native_members(bool duplicate)
    {
        (ClientLoadJoinResponseMessage message, LanConnectRosterSnapshot snapshot) = Prepared();
        List<LoadRunLobbyPlayer> body = message.playersAlreadyConnected.ToList();
        if (duplicate)
        {
            body[1] = body[0];
        }
        else
        {
            body.RemoveAt(body.Count - 1);
        }

        Assert.Throws<InvalidDataException>(() => Restore(snapshot, message.serializableRun, body));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Receive_rejects_native_ready_or_modded_state_that_disagrees_with_the_carrier(bool ready)
    {
        (ClientLoadJoinResponseMessage message, LanConnectRosterSnapshot snapshot) = Prepared();
        List<LoadRunLobbyPlayer> body = message.playersAlreadyConnected.ToList();
        LoadRunLobbyPlayer changed = body[0];
        if (ready)
        {
            changed.isReady = !changed.isReady;
        }
        else
        {
            changed.isModded = !changed.isModded;
        }
        body[0] = changed;

        Assert.Throws<InvalidDataException>(() => Restore(snapshot, message.serializableRun, body));
    }

    [Fact]
    public void Receive_still_rejects_a_carrier_bound_to_the_wrong_saved_slot()
    {
        (ClientLoadJoinResponseMessage message, LanConnectRosterSnapshot snapshot) = Prepared();
        LanConnectRosterPlayerCarrier first = snapshot.Players[0];
        LanConnectRosterSnapshot changed = snapshot with
        {
            Players = [new(first.PlayerId, 7, first.VanillaPlayerBitLength, first.VanillaPlayerBytes.Span),
                .. snapshot.Players.Skip(1)]
        };

        Assert.Throws<InvalidDataException>(() => Restore(changed, message.serializableRun, message.playersAlreadyConnected));
    }

    private static void VerifyRoundTrip(ulong[] savedIds, ulong[] connectedIds, ulong hostId = 900)
    {
        ClientLoadJoinResponseMessage source = Message(savedIds, connectedIds);
        List<LoadRunLobbyPlayer> original = source.playersAlreadyConnected;
        (ulong, bool, bool)[] originalValues = original.Select(Value).ToArray();
        LanConnectProtocolSelection selection = Selection();
        LanConnectPreparedTailMessage prepared = LanConnectTailMessageRuntime.PrepareLoadJoin(
            source, selection, new(hostId));
        ClientLoadJoinResponseMessage body = Assert.IsType<ClientLoadJoinResponseMessage>(prepared.Message);
        LanConnectRosterSnapshot snapshot = LanConnectTailMessagePatches.DecodeAndValidate(
            LanConnectSidecarMessageKind.LoadJoinResponse, prepared.Container,
            selection, hostId, hostId).Roster!;
        ulong[] expected = savedIds.Where(connectedIds.Contains).ToArray();

        Assert.NotSame(original, body.playersAlreadyConnected);
        Assert.Same(original, source.playersAlreadyConnected);
        Assert.Equal(originalValues, original.Select(Value));
        Assert.Equal(expected, body.playersAlreadyConnected.Select(static player => player.id));
        Assert.Equal(expected, snapshot.Players.Select(static player => player.PlayerId));
        List<LoadRunLobbyPlayer> restored = Restore(snapshot, source.serializableRun, body.playersAlreadyConnected);
        Assert.Equal(expected, restored.Select(static player => player.id));
        foreach (LoadRunLobbyPlayer player in restored)
        {
            Assert.Equal(Value(original.Single(value => value.id == player.id)), Value(player));
        }
    }

    private static (ClientLoadJoinResponseMessage, LanConnectRosterSnapshot) Prepared()
    {
        LanConnectRosterAuthorityState authority = new(SavedIds[0]);
        LanConnectPreparedTailMessage prepared = LanConnectTailMessageRuntime.PrepareLoadJoin(
            Message(SavedIds[..3], SavedIds[..3]), Selection(), authority);
        return (Assert.IsType<ClientLoadJoinResponseMessage>(prepared.Message), authority.Current!);
    }

    private static ClientLoadJoinResponseMessage Message(ulong[] savedIds, ulong[] connectedIds)
    {
        // These helpers touch NetId and the managed lobby DTO only; constructing
        // the full save model would unnecessarily initialize game resources.
        SerializableRun run = (SerializableRun)RuntimeHelpers.GetUninitializedObject(typeof(SerializableRun));
        run.Players = savedIds.Select(id =>
        {
            SerializablePlayer player = (SerializablePlayer)RuntimeHelpers.GetUninitializedObject(typeof(SerializablePlayer));
            FieldInfo? field = typeof(SerializablePlayer).GetField(nameof(SerializablePlayer.NetId));
            if (field != null)
            {
                field.SetValue(player, id);
            }
            else
            {
                typeof(SerializablePlayer).GetProperty(nameof(SerializablePlayer.NetId))!.SetValue(player, id);
            }
            return player;
        }).ToList();
        return new ClientLoadJoinResponseMessage
        {
            serializableRun = run,
            playersAlreadyConnected = connectedIds.Select(id => new LoadRunLobbyPlayer
            {
                id = id,
                isReady = id % 2 == 0,
                isModded = id % 3 == 0
            }).ToList()
        };
    }

    private static (ulong, bool, bool) Value(LoadRunLobbyPlayer player) => (player.id, player.isReady, player.isModded);

    private static List<LoadRunLobbyPlayer> Restore(
        LanConnectRosterSnapshot snapshot, SerializableRun run, IReadOnlyList<LoadRunLobbyPlayer> body)
    {
        try
        {
            return (List<LoadRunLobbyPlayer>)RestoreMethod.Invoke(null, [snapshot, run.Players, body])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static LanConnectProtocolSelection Selection()
    {
        LanConnectProtocolSelection selection = new(LanConnectProtocolProfile.TailV1, 1,
            LanConnectProtocolCarrier.NativeBusV1, "0.6.2-alpha.1", 8, "0.113.1", "aabb", false, string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }
}
