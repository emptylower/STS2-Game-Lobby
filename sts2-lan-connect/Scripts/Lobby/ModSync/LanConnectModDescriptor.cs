using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectModRoles
{
    public const string Gameplay = "gameplay";
    public const string Dependency = "dependency";
}

internal static class LanConnectModSources
{
    public const string SteamWorkshop = "steam_workshop";
    public const string ModsDirectory = "mods_directory";
    public const string Unknown = "unknown";
}

internal sealed class LobbyModDescriptor
{
    [JsonPropertyName("id")]
    [JsonPropertyOrder(0)]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    [JsonPropertyOrder(1)]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    [JsonPropertyOrder(2)]
    public string Role { get; set; } = LanConnectModRoles.Gameplay;

    [JsonPropertyName("source")]
    [JsonPropertyOrder(3)]
    public string Source { get; set; } = LanConnectModSources.Unknown;

    [JsonPropertyName("workshopFileId")]
    [JsonPropertyOrder(4)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkshopFileId { get; set; }

    [JsonPropertyName("dependencies")]
    [JsonPropertyOrder(5)]
    public List<string> Dependencies { get; set; } = [];
}

internal sealed class LanConnectModInventoryException : Exception
{
    public LanConnectModInventoryException(string message) : base(message)
    {
    }
}

internal static class LanConnectModInventoryValidator
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] ReservedPrefixes =
    [
        "sts2_lan_connect",
        "lan_connect.",
        "sts2-lan-connect."
    ];

    public static IReadOnlyList<LobbyModDescriptor> Canonicalize(
        IEnumerable<LobbyModDescriptor> descriptors,
        int maxPayloadBytes = LanConnectModSyncCapabilities.MaxPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        List<LobbyModDescriptor> input = descriptors.ToList();
        if (input.Count > LanConnectModSyncCapabilities.MaxDescriptors)
        {
            throw new LanConnectModInventoryException(
                $"Mod descriptor count exceeds {LanConnectModSyncCapabilities.MaxDescriptors}.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        List<LobbyModDescriptor> canonical = new(input.Count);
        foreach (LobbyModDescriptor descriptor in input)
        {
            if (descriptor == null)
            {
                throw new LanConnectModInventoryException("Mod descriptor cannot be null.");
            }

            string id = NormalizeIdentifier(descriptor.Id, "Mod identifier");
            if (!ids.Add(id))
            {
                throw new LanConnectModInventoryException($"Duplicate mod identifier: {id}");
            }

            string version = (descriptor.Version ?? string.Empty).Trim();
            if (CountScalars(version) > LanConnectModSyncCapabilities.MaxVersionCharacters)
            {
                throw new LanConnectModInventoryException(
                    $"Mod version exceeds {LanConnectModSyncCapabilities.MaxVersionCharacters} characters: {id}");
            }
            EnsureNoControlCharacters(version, $"Mod version for {id}");

            if (descriptor.Role is not (LanConnectModRoles.Gameplay or LanConnectModRoles.Dependency))
            {
                throw new LanConnectModInventoryException($"Unsupported mod role for {id}: {descriptor.Role}");
            }
            if (descriptor.Source is not (LanConnectModSources.SteamWorkshop or LanConnectModSources.ModsDirectory or LanConnectModSources.Unknown))
            {
                throw new LanConnectModInventoryException($"Unsupported mod source for {id}: {descriptor.Source}");
            }

            string? workshopFileId = NormalizeWorkshopFileId(descriptor.WorkshopFileId, id);
            List<string> dependencies = CanonicalizeDependencies(descriptor.Dependencies, id);
            canonical.Add(new LobbyModDescriptor
            {
                Id = id,
                Version = version,
                Role = descriptor.Role,
                Source = descriptor.Source,
                WorkshopFileId = workshopFileId,
                Dependencies = dependencies
            });
        }

        canonical.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        int payloadBytes = Encoding.UTF8.GetByteCount(SerializeCanonicalUnchecked(canonical));
        if (payloadBytes > maxPayloadBytes)
        {
            throw new LanConnectModInventoryException(
                $"Canonical mod inventory payload exceeds {maxPayloadBytes} bytes.");
        }

        return canonical;
    }

    public static string SerializeCanonical(IEnumerable<LobbyModDescriptor> descriptors) =>
        SerializeCanonicalUnchecked(Canonicalize(descriptors, int.MaxValue));

    public static bool IsReservedId(string id) =>
        ReservedPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal));

    private static List<string> CanonicalizeDependencies(IEnumerable<string>? values, string ownerId)
    {
        if (values == null)
        {
            throw new LanConnectModInventoryException($"Dependencies are required for {ownerId}.");
        }

        List<string> input = values.ToList();
        if (input.Count > LanConnectModSyncCapabilities.MaxDependencies)
        {
            throw new LanConnectModInventoryException(
                $"Dependency count exceeds {LanConnectModSyncCapabilities.MaxDependencies} for {ownerId}.");
        }

        return input
            .Select(value => NormalizeIdentifier(value, $"Dependency identifier for {ownerId}"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
    }

    private static string NormalizeIdentifier(string? value, string label)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new LanConnectModInventoryException($"{label} cannot be empty.");
        }
        if (CountScalars(normalized) > LanConnectModSyncCapabilities.MaxIdCharacters)
        {
            throw new LanConnectModInventoryException(
                $"{label} exceeds {LanConnectModSyncCapabilities.MaxIdCharacters} characters.");
        }
        EnsureNoControlCharacters(normalized, label);
        if (IsReservedId(normalized))
        {
            throw new LanConnectModInventoryException($"{label} uses a reserved identifier: {normalized}");
        }
        return normalized;
    }

    private static string? NormalizeWorkshopFileId(string? value, string id)
    {
        if (value == null)
        {
            return null;
        }

        string normalized = value;
        if (normalized.Length is < 1 or > 20
            || normalized.All(character => character == '0')
            || normalized.Any(character => character is < '0' or > '9'))
        {
            throw new LanConnectModInventoryException($"Invalid Workshop file identifier for {id}.");
        }
        return normalized;
    }

    private static int CountScalars(string value) => value.EnumerateRunes().Count();

    private static string SerializeCanonicalUnchecked(IEnumerable<LobbyModDescriptor> descriptors) =>
        JsonSerializer.Serialize(descriptors, CanonicalJsonOptions);

    private static void EnsureNoControlCharacters(string value, string label)
    {
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (rune.Value <= 0x1f || rune.Value is >= 0x7f and <= 0x9f)
            {
                throw new LanConnectModInventoryException($"{label} contains control characters.");
            }
        }
    }
}

internal sealed class LanConnectGameVersionComparison
{
    public string Host { get; init; } = string.Empty;
    public string Local { get; init; } = string.Empty;
    public bool ExactMatch { get; init; }
}

internal sealed class LanConnectModVersionMismatch
{
    public string Id { get; init; } = string.Empty;
    public string HostVersion { get; init; } = string.Empty;
    public string LocalVersion { get; init; } = string.Empty;
    public string? WorkshopFileId { get; init; }
}

internal sealed class LanConnectModDiff
{
    public LanConnectGameVersionComparison GameVersion { get; init; } = new();
    public List<LobbyModDescriptor> MissingWorkshopMods { get; init; } = [];
    public List<LobbyModDescriptor> MissingManualMods { get; init; } = [];
    public List<LobbyModDescriptor> ExtraGameplayMods { get; init; } = [];
    public List<LanConnectModVersionMismatch> VersionMismatches { get; init; } = [];
    public bool CanContinueRelaxed { get; init; }
}
