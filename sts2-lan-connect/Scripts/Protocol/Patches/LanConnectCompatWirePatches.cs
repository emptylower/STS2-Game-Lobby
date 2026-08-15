namespace Sts2LanConnect.Scripts;

internal static class LanConnectCompatWirePatches
{
    internal static int GetSlotIdBitWidth()
    {
        LanConnectProtocolSelection? selection = LanConnectSessionProtocolState.Shared.Current.Selection;
        return selection?.Profile switch
        {
            LanConnectProtocolProfile.Compat4x5V1 => LanConnectConstants.ExtendedSlotIdBits,
            LanConnectProtocolProfile.TailV1 => LanConnectConstants.VanillaSlotIdBits,
            _ => LanConnectProtocolProfiles.GetActiveSlotIdBitWidth()
        };
    }

    internal static int GetLobbyListBitWidth()
    {
        LanConnectProtocolSelection? selection = LanConnectSessionProtocolState.Shared.Current.Selection;
        return selection?.Profile switch
        {
            LanConnectProtocolProfile.Compat4x5V1 => LanConnectConstants.ExtendedLobbyListBits,
            LanConnectProtocolProfile.TailV1 => LanConnectConstants.VanillaLobbyListBits,
            _ => LanConnectProtocolProfiles.GetActiveLobbyListBitWidth()
        };
    }
}
