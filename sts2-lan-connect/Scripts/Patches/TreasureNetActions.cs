using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2LanConnect.Scripts;

internal static class SkipRelicActionFactory
{
    private static readonly ConstructorInfo? PickRelicCtor =
        typeof(NetPickRelicAction).GetConstructor(new[] { typeof(Player), typeof(int) });

    internal static GameAction CreateSkipAction(Player player)
    {
        if (PickRelicCtor != null)
        {
            return (GameAction)PickRelicCtor.Invoke(new object[] { player, -1 });
        }

        throw new InvalidOperationException("Cannot create NetPickRelicAction via reflection.");
    }
}

public struct LanConnectSkipRelicNetAction : INetAction, IPacketSerializable
{
    public readonly GameAction ToGameAction(Player player) => SkipRelicActionFactory.CreateSkipAction(player);

    public readonly void Serialize(PacketWriter writer)
    {
    }

    public void Deserialize(PacketReader reader)
    {
    }

    public override readonly string ToString() => nameof(LanConnectSkipRelicNetAction);
}
