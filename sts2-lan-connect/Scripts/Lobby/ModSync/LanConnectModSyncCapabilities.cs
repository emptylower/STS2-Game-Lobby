namespace Sts2LanConnect.Scripts;

internal static class LanConnectModSyncCapabilities
{
    public const int ProtocolVersion = 1;
    public const int MaxDescriptors = 64;
    public const int MaxIdCharacters = 128;
    public const int MaxVersionCharacters = 64;
    public const int MaxDependencies = 16;
    public const int MaxPayloadBytes = 65_536;
    public const uint SteamAppId = 2_868_840;

    public static LanConnectModSyncCapabilityState Resolve(LobbyProbeCapabilities? capabilities)
    {
        int protocolVersion = capabilities?.ModSyncProtocolVersion ?? 0;
        bool enabled = capabilities?.ModSyncEnabled ?? false;
        return new LanConnectModSyncCapabilityState(
            protocolVersion,
            enabled,
            protocolVersion == ProtocolVersion && enabled);
    }
}

internal readonly record struct LanConnectModSyncCapabilityState(
    int ProtocolVersion,
    bool Enabled,
    bool Supported);
