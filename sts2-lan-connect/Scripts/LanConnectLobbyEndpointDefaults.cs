using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectBundledLobbyDefaults
{
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("wsUrl")]
    public string WsUrl { get; set; } = string.Empty;

    [JsonPropertyName("registryBaseUrl")]
    public string RegistryBaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("compatibilityProfile")]
    public string CompatibilityProfile { get; set; } = string.Empty;

    [JsonPropertyName("connectionStrategy")]
    public string ConnectionStrategy { get; set; } = string.Empty;
}

internal static class LanConnectLobbyEndpointDefaults
{
    private const string DefaultsFileName = "lobby-defaults.json";

    private static readonly object Sync = new();

    private static bool _loaded;
    private static string _defaultBaseUrl = string.Empty;
    private static string _defaultWsUrl = string.Empty;
    private static string _registryBaseUrl = string.Empty;
    private static string _compatibilityProfile = LanConnectConstants.DefaultCompatibilityProfile;
    private static string _connectionStrategy = LanConnectConstants.DefaultConnectionStrategy;

    public static string GetDefaultBaseUrl()
    {
        EnsureLoaded();
        return _defaultBaseUrl;
    }

    public static string GetDefaultWsUrl()
    {
        EnsureLoaded();
        return _defaultWsUrl;
    }

    public static string GetRegistryBaseUrl()
    {
        EnsureLoaded();
        return _registryBaseUrl;
    }

    public static bool HasBundledDefaults()
    {
        EnsureLoaded();
        return !string.IsNullOrWhiteSpace(_defaultBaseUrl);
    }

    public static string GetCompatibilityProfile()
    {
        EnsureLoaded();
        return _compatibilityProfile;
    }

    public static string GetConnectionStrategy()
    {
        EnsureLoaded();
        return _connectionStrategy;
    }

    public static bool MatchesBundledDefaultBaseUrl(string? value)
    {
        string bundled = GetDefaultBaseUrl();
        if (string.IsNullOrWhiteSpace(bundled))
        {
            return false;
        }

        return string.Equals(NormalizeBaseUrl(value), bundled, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesBundledDefaultWsUrl(string? value)
    {
        string bundled = GetDefaultWsUrl();
        if (string.IsNullOrWhiteSpace(bundled))
        {
            return false;
        }

        return string.Equals(NormalizeWsUrl(value), bundled, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLegacyLocalhostBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string normalized = NormalizeBaseUrl(value);
        return string.Equals(normalized, "http://127.0.0.1:8787", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "http://localhost:8787", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLegacyLocalhostWsUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string normalized = NormalizeWsUrl(value);
        return string.Equals(normalized, "ws://127.0.0.1:8787/control", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "ws://localhost:8787/control", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureLoaded()
    {
        lock (Sync)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            LoadUnsafe();
        }
    }

    private static void LoadUnsafe()
    {
        string path = Path.Combine(LanConnectPaths.ResolveModDirectory(), DefaultsFileName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            LanConnectBundledLobbyDefaults? defaults = JsonSerializer.Deserialize<LanConnectBundledLobbyDefaults>(json);
            string baseUrl = NormalizeBaseUrl(defaults?.BaseUrl);
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return;
            }

            _defaultBaseUrl = baseUrl;
            _defaultWsUrl = NormalizeWsUrl(defaults?.WsUrl, baseUrl);
            _registryBaseUrl = NormalizeBaseUrl(defaults?.RegistryBaseUrl);
            if (string.IsNullOrWhiteSpace(_registryBaseUrl))
            {
                _registryBaseUrl = baseUrl;
            }
            _compatibilityProfile = NormalizeCompatibilityProfile(defaults?.CompatibilityProfile);
            _connectionStrategy = NormalizeConnectionStrategy(defaults?.ConnectionStrategy);
        }
        catch (Exception ex)
        {
            Log.Warn($"sts2_lan_connect failed to read bundled lobby defaults: {ex.Message}");
            _defaultBaseUrl = string.Empty;
            _defaultWsUrl = string.Empty;
            _registryBaseUrl = string.Empty;
            _compatibilityProfile = LanConnectConstants.DefaultCompatibilityProfile;
            _connectionStrategy = LanConnectConstants.DefaultConnectionStrategy;
        }
    }

    private static string NormalizeBaseUrl(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().TrimEnd('/');
    }

    private static string NormalizeWsUrl(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().TrimEnd('/');
    }

    private static string NormalizeWsUrl(string? value, string baseUrl)
    {
        string normalized = NormalizeWsUrl(value);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        Uri httpUri = new(baseUrl, UriKind.Absolute);
        string scheme = httpUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        return $"{scheme}://{httpUri.Authority}/control";
    }

    private static string NormalizeCompatibilityProfile(string? value)
    {
        return string.Equals(value?.Trim(), "test_relaxed", StringComparison.OrdinalIgnoreCase)
            ? "test_relaxed"
            : LanConnectConstants.DefaultCompatibilityProfile;
    }

    private static string NormalizeConnectionStrategy(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "relay-first" => "relay-first",
            "relay-only" => "relay-only",
            _ => LanConnectConstants.DefaultConnectionStrategy
        };
    }
}
