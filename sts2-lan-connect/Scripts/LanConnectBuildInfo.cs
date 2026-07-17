using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectBuildInfo
{
    private static readonly string[] ManifestFileNames =
    {
        "sts2_lan_connect.json",
        "mod_manifest.json"
    };

    private static string? _cachedModVersion;
    private static List<string>? _cachedModList;
    private static List<LobbyModDescriptor>? _cachedModInventory;

    public static string GetGameVersion()
    {
        return ReleaseInfoManager.Instance.ReleaseInfo?.Version
               ?? GitHelper.ShortCommitId
               ?? typeof(NMultiplayerSubmenu).Assembly.GetName().Version?.ToString()
               ?? "UNKNOWN";
    }

    public static string GetModVersion()
    {
        if (!string.IsNullOrWhiteSpace(_cachedModVersion))
        {
            return _cachedModVersion;
        }

        try
        {
            string modDirectory = ResolveModDirectory();
            foreach (string manifestFileName in ManifestFileNames)
            {
                string manifestPath = Path.Combine(modDirectory, manifestFileName);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (document.RootElement.TryGetProperty("version", out JsonElement versionElement))
                {
                    string? version = versionElement.GetString();
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        _cachedModVersion = version;
                        return version;
                    }
                }
            }
        }
        catch
        {
        }

        _cachedModVersion = typeof(LanConnectBuildInfo).Assembly.GetName().Version?.ToString() ?? "unknown";
        return _cachedModVersion;
    }

    public static List<string> GetModList()
    {
        if (_cachedModList != null)
        {
            return new List<string>(_cachedModList);
        }

        List<string> mods = ModManager.GetGameplayRelevantModNameList()?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToList()
            ?? new List<string>();
        _cachedModList = mods;
        return new List<string>(mods);
    }

    public static List<LobbyModDescriptor> GetModInventory()
    {
        if (_cachedModInventory != null)
        {
            return _cachedModInventory.Select(CloneDescriptor).ToList();
        }

        _cachedModInventory = LanConnectModInventoryBuilder.BuildCurrent()
            .Select(CloneDescriptor)
            .ToList();
        return _cachedModInventory.Select(CloneDescriptor).ToList();
    }

    private static LobbyModDescriptor CloneDescriptor(LobbyModDescriptor descriptor) => new()
    {
        Id = descriptor.Id,
        Version = descriptor.Version,
        Role = descriptor.Role,
        Source = descriptor.Source,
        WorkshopFileId = descriptor.WorkshopFileId,
        Dependencies = descriptor.Dependencies.ToList()
    };

    private static string ResolveModDirectory()
    {
        string? assemblyLocation = Assembly.GetExecutingAssembly().Location;
        string? assemblyDirectory = string.IsNullOrWhiteSpace(assemblyLocation) ? null : Path.GetDirectoryName(assemblyLocation);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory) && Directory.Exists(assemblyDirectory))
        {
            return assemblyDirectory;
        }

        return Path.Combine(AppContext.BaseDirectory, "mods", "sts2_lan_connect");
    }
}
