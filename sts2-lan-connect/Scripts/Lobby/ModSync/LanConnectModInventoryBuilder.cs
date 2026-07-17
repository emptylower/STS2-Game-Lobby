using System.Collections;
using System.Globalization;
using System.Reflection;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectRuntimeMod(
    string Id,
    string Version,
    bool AffectsGameplay,
    bool IsLoaded,
    string Source,
    string? WorkshopFileId,
    IReadOnlyList<string> Dependencies);

internal static class LanConnectModInventoryBuilder
{
    private static readonly string[] ManifestAliases = ["manifest", "Manifest"];
    private static readonly string[] StateAliases = ["state", "State", "LoadState"];
    private static readonly string[] SourceAliases = ["modSource", "ModSource", "Source"];
    private static readonly string[] PathAliases = ["path", "Path", "DirectoryPath"];
    private static readonly string[] WorkshopIdAliases = ["workshopFileId", "WorkshopFileId", "publishedFileId", "PublishedFileId"];
    private static readonly string[] IdAliases = ["id", "Id"];
    private static readonly string[] VersionAliases = ["version", "Version"];
    private static readonly string[] GameplayAliases = ["affectsGameplay", "AffectsGameplay"];
    private static readonly string[] DependencyAliases = ["dependencies", "Dependencies"];

    public static IReadOnlyList<LobbyModDescriptor> BuildCurrent()
    {
        PropertyInfo modsProperty = typeof(ModManager).GetProperty(
            "Mods",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMemberException(typeof(ModManager).FullName, "Mods");
        if (modsProperty.GetIndexParameters().Length != 0 || modsProperty.GetValue(null) is not IEnumerable values)
        {
            throw new InvalidOperationException("ModManager.Mods has an unsupported runtime shape.");
        }

        return Build(values.Cast<object>().Select(ResolveRuntimeMod));
    }

    public static IReadOnlyList<LobbyModDescriptor> Build(IEnumerable<LanConnectRuntimeMod> runtimeMods)
    {
        ArgumentNullException.ThrowIfNull(runtimeMods);
        Dictionary<string, LanConnectRuntimeMod> loaded = new(StringComparer.Ordinal);
        foreach (LanConnectRuntimeMod mod in runtimeMods.Where(static mod => mod.IsLoaded))
        {
            string id = mod.Id.Trim();
            if (id.Length == 0 || LanConnectModInventoryValidator.IsReservedId(id))
            {
                continue;
            }
            if (!loaded.TryAdd(id, mod with { Id = id }))
            {
                throw new LanConnectModInventoryException($"Duplicate loaded mod identifier: {id}");
            }
        }

        HashSet<string> gameplayRoots = loaded.Values
            .Where(static mod => mod.AffectsGameplay)
            .Select(static mod => mod.Id)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> included = new(gameplayRoots, StringComparer.Ordinal);
        Stack<string> pending = new(gameplayRoots);
        while (pending.TryPop(out string? id))
        {
            if (!loaded.TryGetValue(id, out LanConnectRuntimeMod? mod))
            {
                continue;
            }
            foreach (string dependencyValue in mod.Dependencies)
            {
                string dependency = dependencyValue.Trim();
                if (loaded.ContainsKey(dependency) && included.Add(dependency))
                {
                    pending.Push(dependency);
                }
            }
        }

        IEnumerable<LobbyModDescriptor> descriptors = included.Select(id =>
        {
            LanConnectRuntimeMod mod = loaded[id];
            return new LobbyModDescriptor
            {
                Id = id,
                Version = mod.Version,
                Role = gameplayRoots.Contains(id) ? LanConnectModRoles.Gameplay : LanConnectModRoles.Dependency,
                Source = mod.Source,
                WorkshopFileId = mod.WorkshopFileId,
                Dependencies = mod.Dependencies.ToList()
            };
        });
        return LanConnectModInventoryValidator.Canonicalize(descriptors);
    }

    public static LanConnectRuntimeMod ResolveRuntimeMod(object runtimeMod)
    {
        ArgumentNullException.ThrowIfNull(runtimeMod);
        object manifest = GetRequiredMember(runtimeMod, ManifestAliases, "manifest");
        string id = Convert.ToString(GetRequiredMember(manifest, IdAliases, "manifest id"), CultureInfo.InvariantCulture) ?? string.Empty;
        string version = Convert.ToString(GetOptionalMember(manifest, VersionAliases), CultureInfo.InvariantCulture) ?? string.Empty;
        object gameplayValue = GetRequiredMember(manifest, GameplayAliases, "affects gameplay");
        if (gameplayValue is not bool affectsGameplay)
        {
            throw new InvalidOperationException("Mod manifest affects-gameplay member must be Boolean.");
        }

        object state = GetRequiredMember(runtimeMod, StateAliases, "load state");
        object sourceValue = GetRequiredMember(runtimeMod, SourceAliases, "source");
        string sourceName = sourceValue.ToString() ?? string.Empty;
        string source = sourceName switch
        {
            "SteamWorkshop" => LanConnectModSources.SteamWorkshop,
            "ModsDirectory" => LanConnectModSources.ModsDirectory,
            _ => LanConnectModSources.Unknown
        };

        object dependencyValue = GetRequiredMember(manifest, DependencyAliases, "dependencies");
        if (dependencyValue is not IEnumerable dependencyValues || dependencyValue is string)
        {
            throw new InvalidOperationException("Mod manifest dependencies member must be enumerable.");
        }
        List<string> dependencies = [];
        foreach (object? dependency in dependencyValues)
        {
            if (dependency == null)
            {
                continue;
            }
            string dependencyId = Convert.ToString(
                GetRequiredMember(dependency, IdAliases, "dependency id"),
                CultureInfo.InvariantCulture) ?? string.Empty;
            dependencies.Add(dependencyId);
        }

        string? workshopFileId = null;
        if (source == LanConnectModSources.SteamWorkshop)
        {
            workshopFileId = ConvertWorkshopId(GetOptionalMember(runtimeMod, WorkshopIdAliases));
            if (workshopFileId == null)
            {
                string? path = Convert.ToString(GetOptionalMember(runtimeMod, PathAliases), CultureInfo.InvariantCulture);
                workshopFileId = ResolveWorkshopIdFromPath(path);
            }
        }

        return new LanConnectRuntimeMod(
            id,
            version,
            affectsGameplay,
            string.Equals(state.ToString(), "Loaded", StringComparison.Ordinal),
            source,
            workshopFileId,
            dependencies);
    }

    private static object GetRequiredMember(object instance, string[] aliases, string label) =>
        GetOptionalMember(instance, aliases)
        ?? throw new MissingMemberException(instance.GetType().FullName, label);

    private static object? GetOptionalMember(object instance, string[] aliases)
    {
        Type type = instance.GetType();
        List<MemberInfo> matches = [];
        foreach (string alias in aliases)
        {
            matches.AddRange(type.GetMember(alias, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(static member => member is FieldInfo || member is PropertyInfo property && property.CanRead && property.GetIndexParameters().Length == 0));
        }
        matches = matches.Distinct().ToList();
        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Runtime type {type.FullName} exposes ambiguous members: {string.Join(", ", matches.Select(member => member.Name))}");
        }
        if (matches.Count == 0)
        {
            return null;
        }
        return matches[0] switch
        {
            FieldInfo field => field.GetValue(instance),
            PropertyInfo property => property.GetValue(instance),
            _ => null
        };
    }

    private static string? ConvertWorkshopId(object? value)
    {
        if (value == null)
        {
            return null;
        }
        if (value is string text)
        {
            return text;
        }
        if (value is byte or ushort or uint or ulong or sbyte or short or int or long)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        object? nested = GetOptionalMember(value, ["m_PublishedFileId", "Value"]);
        return nested == null ? null : Convert.ToString(nested, CultureInfo.InvariantCulture);
    }

    private static string? ResolveWorkshopIdFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        string normalized = Path.TrimEndingDirectorySeparator(path.Trim());
        string candidate = Path.GetFileName(normalized);
        return candidate.Length is >= 1 and <= 20 && candidate != "0" && candidate.All(char.IsAsciiDigit)
            ? candidate
            : null;
    }
}
