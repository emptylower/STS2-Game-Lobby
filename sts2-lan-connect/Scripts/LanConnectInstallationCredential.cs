using System;
using System.Security.Cryptography;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectInstallationCredentialResolution(
    string Credential,
    bool Generated);

internal static class LanConnectInstallationCredential
{
    private const string Prefix = "lci_";
    private const int EntropyBytes = 32;

    public static LanConnectInstallationCredentialResolution Resolve(
        string? persistedValue,
        Func<byte[]> generateEntropy)
    {
        if (TryNormalize(persistedValue, out string persistedCredential))
        {
            return new LanConnectInstallationCredentialResolution(
                persistedCredential,
                Generated: false);
        }

        for (int attempt = 0; attempt < 16; attempt++)
        {
            byte[] entropy = generateEntropy();
            if (entropy.Length != EntropyBytes)
            {
                continue;
            }

            return new LanConnectInstallationCredentialResolution(
                Prefix + Convert.ToBase64String(entropy)
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_'),
                Generated: true);
        }

        throw new InvalidOperationException("Unable to generate a valid lobby installation credential.");
    }

    public static LanConnectInstallationCredentialResolution Resolve(string? persistedValue) =>
        Resolve(persistedValue, () => RandomNumberGenerator.GetBytes(EntropyBytes));

    public static bool TryNormalize(string? value, out string credential)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (!candidate.StartsWith(Prefix, StringComparison.Ordinal))
        {
            credential = string.Empty;
            return false;
        }

        string encoded = candidate[Prefix.Length..];
        try
        {
            string base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            if (Convert.FromBase64String(base64).Length != EntropyBytes)
            {
                credential = string.Empty;
                return false;
            }
        }
        catch (FormatException)
        {
            credential = string.Empty;
            return false;
        }

        credential = candidate;
        return true;
    }
}
