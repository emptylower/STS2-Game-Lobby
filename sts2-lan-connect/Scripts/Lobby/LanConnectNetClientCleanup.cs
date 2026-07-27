using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectNetClientCleanup
{
    private static readonly FieldInfo? ConnectionField =
        typeof(ENetClient).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? PeerField =
        typeof(ENetClient).GetField("_peer", BindingFlags.Instance | BindingFlags.NonPublic);

    public static bool TryCleanup(NetClientGameService? netService)
    {
        NetClient? netClient = netService?.NetClient;
        if (netClient == null)
        {
            return true;
        }

        try
        {
            if (netClient.IsConnected)
            {
                netClient.DisconnectFromHost(NetError.CancelledJoin, now: true);
                return true;
            }

            if (netClient is not ENetClient enetClient)
            {
                return false;
            }

            (PeerField?.GetValue(enetClient) as ENetPacketPeer)?.Reset();
            (ConnectionField?.GetValue(enetClient) as ENetConnection)?.Destroy();
            return ConnectionField != null;
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect lan_direct_join: ENet cleanup failed: {ex.Message}");
            return false;
        }
    }
}
