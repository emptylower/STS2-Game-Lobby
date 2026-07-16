namespace Sts2LanConnect.Scripts;

internal interface ILanConnectStableMonsterIdAdapter
{
    string PublicNetworkIdMember { get; }

    bool IsNetworkReplicated { get; }

    bool TryGetNetworkId(object monster, out string stableId);

    bool TryResolveNetworkId(string stableId, out object monster);
}

internal sealed class LanConnectMonsterTargetIdPrototype
{
    internal const string MissingProofReason = "No verified STS2 network monster ID API";

    private readonly ILanConnectStableMonsterIdAdapter? _adapter;

    internal LanConnectMonsterTargetIdPrototype(
        ILanConnectStableMonsterIdAdapter? adapter = null)
    {
        _adapter = adapter;
    }

    internal bool IsEnabled
    {
        get
        {
            try
            {
                return _adapter != null &&
                       _adapter.IsNetworkReplicated &&
                       !string.IsNullOrWhiteSpace(_adapter.PublicNetworkIdMember);
            }
            catch
            {
                return false;
            }
        }
    }

    internal bool TryGetStableId(object monster, out string stableId)
    {
        stableId = string.Empty;
        if (monster == null || !IsEnabled)
        {
            return false;
        }
        try
        {
            if (!_adapter!.TryGetNetworkId(monster, out string candidate) ||
                !IsValidStableId(candidate) ||
                !_adapter.TryResolveNetworkId(candidate, out object resolved) ||
                !ReferenceEquals(resolved, monster))
            {
                return false;
            }
            stableId = candidate;
            return true;
        }
        catch
        {
            stableId = string.Empty;
            return false;
        }
    }

    internal bool CanResolveStableId(string stableId)
    {
        if (!IsEnabled || !IsValidStableId(stableId))
        {
            return false;
        }
        try
        {
            return _adapter!.TryResolveNetworkId(stableId, out object monster) &&
                   monster != null;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsValidStableId(string? stableId) =>
        !string.IsNullOrEmpty(stableId) &&
        stableId.Length <= 128 &&
        stableId.All(static character => character is >= ' ' and <= '~');
}
