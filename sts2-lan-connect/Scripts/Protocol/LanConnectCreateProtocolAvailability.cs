using System;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectCreateProtocolAvailability(
    bool TailAvailable,
    bool CompatAvailable,
    int? DefaultProtocolId,
    string? TailUnavailableReason,
    string? CompatUnavailableReason,
    string? NeitherAvailableReason)
{
    internal const int CompatProtocolId = 300;
    internal const int TailProtocolId = 301;

    internal bool HasAnyAvailable => DefaultProtocolId.HasValue;

    internal bool IsSelectable(int? protocolId) => protocolId switch
    {
        TailProtocolId => TailAvailable,
        CompatProtocolId => CompatAvailable,
        _ => false
    };

    internal static LanConnectCreateProtocolAvailability Resolve(
        LanConnectProtocolOffer offer,
        bool standaloneTailRuntimeAvailable)
    {
        ArgumentNullException.ThrowIfNull(offer);

        bool compatAvailable = !offer.RitsuLibPresent;
        bool tailOffered = offer.Supports(LanConnectConstants.TailLanProtocolVersion);
        bool tailAvailable = tailOffered && standaloneTailRuntimeAvailable;

        string? tailUnavailableReason = null;
        if (!tailAvailable)
        {
            tailUnavailableReason = !standaloneTailRuntimeAvailable
                ? "检测到 RMP 等冲突联机 Mod，当前不能使用新协议"
                : "当前游戏版本缺少 0.111+ 原生 Mod 消息通道能力，不能使用新协议";
        }

        string? compatUnavailableReason = compatAvailable
            ? null
            : "检测到已安装 RitsuLib，兼容旧版 Mod 协议不支持 RitsuLib";

        int? defaultProtocolId = tailAvailable
            ? TailProtocolId
            : compatAvailable
                ? CompatProtocolId
                : null;

        string? neitherAvailableReason = defaultProtocolId.HasValue
            ? null
            : $"当前无法创建房间：两种联机协议均不可用。新协议：{tailUnavailableReason}；兼容旧版 Mod：{compatUnavailableReason}。";

        return new LanConnectCreateProtocolAvailability(
            TailAvailable: tailAvailable,
            CompatAvailable: compatAvailable,
            DefaultProtocolId: defaultProtocolId,
            TailUnavailableReason: tailUnavailableReason,
            CompatUnavailableReason: compatUnavailableReason,
            NeitherAvailableReason: neitherAvailableReason);
    }
}
