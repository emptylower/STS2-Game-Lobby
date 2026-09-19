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

    /// <summary>
    /// native_bus_v1 桌面 seam 把 SerializeMessage&lt;T&gt; 闭合实例化替换为全优化
    /// DynamicMethod，其内联的原始 IL 会绕过 T.Serialize 上的 compat 位宽 transpiler。
    /// 仅 compat_4_5_v1 活动时需要在消息总线边界由 prefix 显式产出 compat 字节；
    /// tail_v1（原版位宽即正确位宽）与无活动 profile 一律返回 false，走原路径。
    /// </summary>
    internal static bool ShouldForceCompatWireAtMessageBusBoundary()
    {
        LanConnectProtocolSelection? selection = LanConnectSessionProtocolState.Shared.Current.Selection;
        return selection?.Profile == LanConnectProtocolProfile.Compat4x5V1;
    }
}
