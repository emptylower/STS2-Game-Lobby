using System;
using System.Globalization;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectClientIdentityResolution(
    ulong NetId,
    string PersistedValue,
    bool Generated);

internal static class LanConnectClientIdentity
{
    public static LanConnectClientIdentityResolution Resolve(
        string? persistedValue,
        Func<ulong> generate)
    {
        if (TryParse(persistedValue, out ulong persistedNetId))
        {
            return new LanConnectClientIdentityResolution(
                persistedNetId,
                persistedNetId.ToString(CultureInfo.InvariantCulture),
                Generated: false);
        }

        for (int attempt = 0; attempt < 16; attempt++)
        {
            ulong generatedNetId = generate();
            if (generatedNetId > 1)
            {
                return new LanConnectClientIdentityResolution(
                    generatedNetId,
                    generatedNetId.ToString(CultureInfo.InvariantCulture),
                    Generated: true);
            }
        }

        throw new InvalidOperationException("Unable to generate a valid LAN client network identity.");
    }

    public static bool TryParse(string? value, out ulong netId)
    {
        return ulong.TryParse(
                   value?.Trim(),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out netId)
               && netId > 1;
    }
}
