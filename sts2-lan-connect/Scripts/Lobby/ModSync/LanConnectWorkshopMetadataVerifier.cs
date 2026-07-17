using System.Text.Json;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectWorkshopMetadataValidation(
    bool Success,
    string? ErrorCode,
    string? Message)
{
    public static LanConnectWorkshopMetadataValidation Valid { get; } = new(true, null, null);

    public static LanConnectWorkshopMetadataValidation Invalid(string code, string message) =>
        new(false, code, message);
}

internal sealed record LanConnectWorkshopManifestVerification
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public string? InstalledVersion { get; init; }
    public bool RequiresRestart { get; init; }
    public bool RequiresRepreflight { get; init; }

    public static LanConnectWorkshopManifestVerification Verified(string installedVersion) => new()
    {
        Success = true,
        InstalledVersion = installedVersion,
        RequiresRestart = true,
        RequiresRepreflight = true
    };

    public static LanConnectWorkshopManifestVerification Failed(
        string code,
        string message,
        string? installedVersion = null,
        bool requiresRestart = false,
        bool requiresRepreflight = false) => new()
    {
        ErrorCode = code,
        Message = message,
        InstalledVersion = installedVersion,
        RequiresRestart = requiresRestart,
        RequiresRepreflight = requiresRepreflight
    };
}

internal interface ILanConnectWorkshopManifestVerifier
{
    Task<LanConnectWorkshopManifestVerification> VerifyInstalledManifestAsync(
        string installFolder,
        LobbyModDescriptor expected,
        CancellationToken cancellationToken);
}

internal sealed class LanConnectWorkshopMetadataVerifier : ILanConnectWorkshopManifestVerifier
{
    private const int MaxManifestFiles = 128;
    private const long MaxManifestBytes = 1_048_576;

    public LanConnectWorkshopMetadataValidation VerifyMetadata(
        LobbyModDescriptor expected,
        LanConnectWorkshopMetadata metadata)
    {
        if (expected.Source != LanConnectModSources.SteamWorkshop)
        {
            return LanConnectWorkshopMetadataValidation.Invalid(
                "workshop_source_required",
                "Only Steam Workshop descriptors can be synchronized automatically.");
        }
        if (expected.Role is not (LanConnectModRoles.Gameplay or LanConnectModRoles.Dependency))
        {
            return LanConnectWorkshopMetadataValidation.Invalid(
                "workshop_role_invalid",
                "Only gameplay MODs and their required dependencies can be synchronized.");
        }
        if (!ulong.TryParse(expected.WorkshopFileId, out ulong expectedId) || expectedId == 0)
        {
            return LanConnectWorkshopMetadataValidation.Invalid(
                "workshop_invalid_id",
                "Workshop file ID is invalid.");
        }
        if (!ulong.TryParse(metadata.WorkshopFileId, out ulong actualId) || actualId != expectedId)
        {
            return LanConnectWorkshopMetadataValidation.Invalid(
                "workshop_id_mismatch",
                "Steam returned metadata for a different Workshop item.");
        }
        if (metadata.ConsumerAppId != LanConnectModSyncCapabilities.SteamAppId)
        {
            return LanConnectWorkshopMetadataValidation.Invalid(
                "workshop_wrong_app",
                $"Workshop item belongs to AppID {metadata.ConsumerAppId}, not {LanConnectModSyncCapabilities.SteamAppId}.");
        }
        if (string.IsNullOrWhiteSpace(metadata.Title) || string.IsNullOrWhiteSpace(metadata.Publisher))
        {
            return LanConnectWorkshopMetadataValidation.Invalid(
                "workshop_metadata_incomplete",
                "Steam did not return a title and publisher for this item.");
        }
        return LanConnectWorkshopMetadataValidation.Valid;
    }

    public Task<LanConnectWorkshopManifestVerification> VerifyInstalledManifestAsync(
        string installFolder,
        LobbyModDescriptor expected,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(installFolder) || !Directory.Exists(installFolder))
        {
            return Task.FromResult(LanConnectWorkshopManifestVerification.Failed(
                "installed_folder_missing",
                "Steam reported the item as installed but its folder is unavailable."));
        }

        try
        {
            bool sawDifferentManifestId = false;
            int inspected = 0;
            EnumerationOptions options = new()
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MaxRecursionDepth = 8
            };
            foreach (string path in Directory.EnumerateFiles(installFolder, "*.json", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++inspected > MaxManifestFiles)
                {
                    return Task.FromResult(LanConnectWorkshopManifestVerification.Failed(
                        "installed_manifest_limit",
                        "Workshop item contains too many manifest candidates."));
                }
                FileInfo info = new(path);
                if (info.Length > MaxManifestBytes)
                {
                    continue;
                }
                JsonDocument document;
                try
                {
                    using FileStream stream = File.OpenRead(path);
                    document = JsonDocument.Parse(stream);
                }
                catch (JsonException)
                {
                    continue;
                }
                using (document)
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object ||
                        !document.RootElement.TryGetProperty("id", out JsonElement idElement) ||
                        idElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }
                    string? id = idElement.GetString();
                    if (!string.Equals(id, expected.Id, StringComparison.Ordinal))
                    {
                        sawDifferentManifestId = true;
                        continue;
                    }
                    string installedVersion = document.RootElement.TryGetProperty("version", out JsonElement versionElement) &&
                                              versionElement.ValueKind == JsonValueKind.String
                        ? versionElement.GetString() ?? string.Empty
                        : string.Empty;
                    if (!string.Equals(installedVersion, expected.Version, StringComparison.Ordinal))
                    {
                        return Task.FromResult(LanConnectWorkshopManifestVerification.Failed(
                            "installed_version_mismatch",
                            "Workshop current version differs from the host inventory; restart and re-run preflight for manual handling.",
                            installedVersion,
                            requiresRestart: true,
                            requiresRepreflight: true));
                    }
                    return Task.FromResult(LanConnectWorkshopManifestVerification.Verified(installedVersion));
                }
            }

            return Task.FromResult(LanConnectWorkshopManifestVerification.Failed(
                sawDifferentManifestId ? "installed_manifest_id_mismatch" : "installed_manifest_missing",
                sawDifferentManifestId
                    ? "Installed Workshop manifests do not match the expected MOD ID."
                    : "No readable MOD manifest was found in the installed Workshop item.",
                requiresRestart: true,
                requiresRepreflight: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Task.FromResult(LanConnectWorkshopManifestVerification.Failed(
                "installed_manifest_invalid",
                ex.Message));
        }
    }
}
