namespace Sts2LanConnect.Scripts;

internal static class LanConnectHostOriginProtocolLeaseHandoff
{
    internal static LanConnectSessionProtocolLease? PreserveExistingOwner(
        LanConnectSessionProtocolLease? existingLease,
        LanConnectSessionProtocolLease? replacementLease)
    {
        if (existingLease == null)
        {
            return replacementLease;
        }

        if (!ReferenceEquals(existingLease, replacementLease))
        {
            replacementLease?.Dispose();
        }
        return existingLease;
    }
}
