using System;
using System.Text;
using System.Text.Json;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectInvitePayload
{
    public string S { get; set; } = string.Empty;
    public string R { get; set; } = string.Empty;
    public string? P { get; set; }
    public int V { get; set; } = 1;
}

internal static class LanConnectInviteCode
{
    private const string Prefix = "STS2INV:";
    private const int CurrentVersion = 1;

    public static string Encode(string serverBaseUrl, string roomId, string? password)
    {
        LanConnectInvitePayload payload = new()
        {
            S = serverBaseUrl,
            R = roomId,
            P = string.IsNullOrWhiteSpace(password) ? null : password,
            V = CurrentVersion
        };

        string json = JsonSerializer.Serialize(payload, LanConnectJson.Options);
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return Prefix + base64;
    }

    public static bool TryDecode(string? text, out LanConnectInvitePayload? payload)
    {
        payload = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            string base64 = trimmed[Prefix.Length..];
            byte[] bytes = Convert.FromBase64String(base64);
            string json = Encoding.UTF8.GetString(bytes);
            LanConnectInvitePayload? decoded = JsonSerializer.Deserialize<LanConnectInvitePayload>(json, LanConnectJson.Options);

            if (decoded == null || decoded.V != CurrentVersion
                || string.IsNullOrWhiteSpace(decoded.S)
                || string.IsNullOrWhiteSpace(decoded.R))
            {
                return false;
            }

            payload = decoded;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
