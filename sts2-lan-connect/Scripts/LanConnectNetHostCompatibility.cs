using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectNetHostCompatibility
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void DisconnectClient(object host, ulong peerId, object reason, bool now = false)
    {
        Type hostType = host.GetType();
        // 0.113 added an optional debugReason argument. Optional parameters are
        // part of the CLR signature, so even a three-argument call needs adapting.
        MethodInfo? method = hostType.GetMethod("DisconnectClient", InstanceMembers, null,
            [typeof(ulong), reason.GetType(), typeof(bool), typeof(string)], null);
        object?[] arguments;
        if (method != null)
        {
            arguments = [peerId, reason, now, null];
        }
        else
        {
            method = hostType.GetMethod("DisconnectClient", InstanceMembers, null,
                [typeof(ulong), reason.GetType(), typeof(bool)], null)
                ?? throw new MissingMethodException(hostType.FullName, "DisconnectClient");
            arguments = [peerId, reason, now];
        }

        try
        {
            method.Invoke(host, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    internal static ulong[] GetConnectedPeerIds(object host)
    {
        // The getter changed from List<NetClientData> to IReadOnlyList<...> in
        // 0.113. Enumerating its runtime value avoids binding to either return type.
        object? peers = host.GetType().GetProperty("ConnectedPeers", InstanceMembers)?.GetValue(host);
        if (peers is not IEnumerable values)
        {
            throw new MissingMemberException(host.GetType().FullName, "ConnectedPeers");
        }

        List<ulong> result = [];
        foreach (object peer in values)
        {
            object? peerId = peer.GetType().GetField("peerId", InstanceMembers)?.GetValue(peer);
            if (peerId is not ulong id)
            {
                throw new MissingFieldException(peer.GetType().FullName, "peerId");
            }

            result.Add(id);
        }

        return result.ToArray();
    }
}
