using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectSkipRelicGameAction : GameAction
{
    private readonly Player _player;

    public LanConnectSkipRelicGameAction(Player player)
    {
        _player = player;
    }

    public override ulong OwnerId => _player.NetId;

    public override GameActionType ActionType => GameActionType.NonCombat;

    protected override Task ExecuteAction()
    {
        RunManager.Instance.TreasureRoomRelicSynchronizer.OnPicked(_player, -1);
        return Task.CompletedTask;
    }

    public override INetAction ToNetAction() => new LanConnectSkipRelicNetAction();

    public override string ToString() => $"LanConnectSkipRelicAction for player {_player.NetId}";
}

public struct LanConnectSkipRelicNetAction : INetAction, IPacketSerializable
{
    public readonly GameAction ToGameAction(Player player) => new LanConnectSkipRelicGameAction(player);

    public readonly void Serialize(PacketWriter writer)
    {
    }

    public void Deserialize(PacketReader reader)
    {
    }

    public override readonly string ToString() => nameof(LanConnectSkipRelicNetAction);
}
