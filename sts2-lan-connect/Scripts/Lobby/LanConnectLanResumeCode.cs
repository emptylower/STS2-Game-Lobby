using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectLanResumePayload(
    int Version,
    string SaveKey,
    ulong PlayerNetId,
    string CharacterName,
    string PlayerName);

internal static class LanConnectLanResumeCode
{
    internal const string Prefix = "STS2LANRESUME:";
    private const int CurrentVersion = 1;
    private const int MaxEncodedLength = 2048;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    internal static string Encode(string saveKey, ulong playerNetId, string? characterName, string? playerName)
    {
        Validate(saveKey, playerNetId);
        LanConnectLanResumePayload payload = new(
            CurrentVersion,
            saveKey.Trim(),
            playerNetId,
            characterName?.Trim() ?? string.Empty,
            playerName?.Trim() ?? string.Empty);
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        string token = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return Prefix + token;
    }

    internal static bool TryDecode(string? input, out LanConnectLanResumePayload payload, out string error)
    {
        payload = null!;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "续局身份码为空。";
            return false;
        }

        int prefixIndex = input.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase);
        if (prefixIndex < 0)
        {
            error = $"续局身份码必须以 {Prefix} 开头。";
            return false;
        }

        if (input.IndexOf(Prefix, prefixIndex + Prefix.Length, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            error = "检测到多条续局身份码。请只粘贴与你的角色/玩家名对应的那一条。";
            return false;
        }

        string encoded = input[(prefixIndex + Prefix.Length)..].Trim();
        int separator = encoded.IndexOfAny([' ', '\t', '\r', '\n']);
        if (separator >= 0)
        {
            encoded = encoded[..separator];
        }

        if (encoded.Length == 0 || encoded.Length > MaxEncodedLength)
        {
            error = "续局身份码长度无效。";
            return false;
        }

        try
        {
            string padded = encoded.Replace('-', '+').Replace('_', '/');
            padded += (padded.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                0 => string.Empty,
                _ => throw new FormatException("invalid base64url length")
            };
            byte[] bytes = Convert.FromBase64String(padded);
            LanConnectLanResumePayload? decoded = JsonSerializer.Deserialize<LanConnectLanResumePayload>(bytes, JsonOptions);
            if (decoded == null)
            {
                error = "续局身份码内容为空。";
                return false;
            }

            if (decoded.Version != CurrentVersion)
            {
                error = $"续局身份码版本不受支持（{decoded.Version}）。";
                return false;
            }

            Validate(decoded.SaveKey, decoded.PlayerNetId);
            payload = decoded with
            {
                SaveKey = decoded.SaveKey.Trim(),
                CharacterName = decoded.CharacterName?.Trim() ?? string.Empty,
                PlayerName = decoded.PlayerName?.Trim() ?? string.Empty
            };
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            error = "续局身份码损坏或格式不正确。";
            return false;
        }
    }

    private static void Validate(string? saveKey, ulong playerNetId)
    {
        if (string.IsNullOrWhiteSpace(saveKey) || saveKey.Trim().Length > 128)
        {
            throw new ArgumentException("save key is invalid", nameof(saveKey));
        }

        if (playerNetId <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(playerNetId), "player net id must be greater than 1");
        }
    }
}
