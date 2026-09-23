using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectConfigPersistence
{
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static LanConnectConfigData Load(string path)
    {
        return Load(path, out _);
    }

    public static LanConnectConfigData Load(string path, out bool recoveredFromBackup)
    {
        recoveredFromBackup = false;
        try
        {
            LanConnectConfigData primary = ReadConfig(path, out bool hasBindingField);
            if (!hasBindingField && BackupHasStoredBindings(path + ".backup"))
            {
                throw new InvalidDataException(
                    "Current config omitted SaveRoomBindings while its backup contains saved rooms.");
            }

            return primary;
        }
        catch (Exception primaryError) when (primaryError is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            LanConnectConfigData backup;
            try
            {
                backup = ReadConfig(path + ".backup");
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                throw new InvalidDataException(
                    "Neither the current config nor its backup could be loaded.",
                    new AggregateException(primaryError, backupError));
            }

            string temporaryPath = TemporaryPath(path);
            try
            {
                SetPrivateUnixMode(path + ".backup");
                CopyPrivateFile(path + ".backup", temporaryPath);
                if (File.Exists(path))
                {
                    SetPrivateUnixMode(path);
                    PreserveCorruptConfig(path);
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                File.Delete(temporaryPath);
            }

            recoveredFromBackup = true;
            return backup;
        }
    }

    public static bool Save(string path, LanConnectConfigData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.SaveRoomBindings == null)
        {
            throw new InvalidDataException("Config SaveRoomBindings list cannot be null.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        string temporaryPath = TemporaryPath(path);
        try
        {
            using (FileStream stream = CreatePrivateFile(temporaryPath))
            {
                stream.Write(Encoding.UTF8.GetBytes(json));
                stream.Flush(flushToDisk: true);
            }

            if (!File.Exists(path))
            {
                File.Move(temporaryPath, path);
                return false;
            }

            SetPrivateUnixMode(path);
            try
            {
                _ = ReadConfig(path, out bool hasBindingField);
                if (!hasBindingField && BackupHasStoredBindings(path + ".backup"))
                {
                    throw new InvalidDataException(
                        "Current config omitted SaveRoomBindings while its backup contains saved rooms.");
                }
            }
            catch (Exception error) when (error is JsonException or InvalidDataException)
            {
                PreserveCorruptConfig(path);
                File.Replace(temporaryPath, path, null);
                return true;
            }

            // Keep the only remaining copy of a room's frozen protocol if the current
            // config lost that room or rewrote its protocol under the same save key.
            if (BackupContainsProtocolHistoryAtRisk(path + ".backup", data.SaveRoomBindings))
            {
                File.Replace(temporaryPath, path, null);
                return false;
            }

            // File.Replace atomically installs the new file and keeps the last valid one.
            File.Replace(temporaryPath, path, path + ".backup");
            return false;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static LanConnectConfigData ReadConfig(string path) => ReadConfig(path, out _);

    private static LanConnectConfigData ReadConfig(string path, out bool hasBindingField)
    {
        string json = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(json);
        hasBindingField = document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty(nameof(LanConnectConfigData.SaveRoomBindings), out _);
        LanConnectConfigData? data = JsonSerializer.Deserialize<LanConnectConfigData>(json);
        return data?.SaveRoomBindings == null
            ? throw new InvalidDataException("Config JSON is missing its root object or SaveRoomBindings list.")
            : data;
    }

    private static bool BackupHasStoredBindings(string backupPath)
    {
        try
        {
            return ReadConfig(backupPath).SaveRoomBindings.Count > 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return false;
        }
    }

    private static bool BackupContainsProtocolHistoryAtRisk(
        string backupPath,
        System.Collections.Generic.List<LanConnectSavedRoomBinding> currentBindings)
    {
        try
        {
            LanConnectConfigData backup = ReadConfig(backupPath);
            return backup.SaveRoomBindings.Any(saved =>
            {
                LanConnectSavedRoomBinding? current = currentBindings.FirstOrDefault(candidate =>
                    string.Equals(candidate.SaveKey, saved.SaveKey, StringComparison.Ordinal));
                return current == null || !SameFrozenProtocol(saved, current);
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return false;
        }
    }

    private static bool SameFrozenProtocol(
        LanConnectSavedRoomBinding left,
        LanConnectSavedRoomBinding right) =>
        left.SchemaVersion == right.SchemaVersion
        && string.Equals(left.ProtocolProfileV2, right.ProtocolProfileV2, StringComparison.Ordinal)
        && left.SelectedLanProtocolVersion == right.SelectedLanProtocolVersion
        && string.Equals(left.ProtocolCarrier, right.ProtocolCarrier, StringComparison.Ordinal)
        && left.ProtocolMaxPlayers == right.ProtocolMaxPlayers
        && string.Equals(left.MinimumClientVersion, right.MinimumClientVersion, StringComparison.Ordinal)
        && string.Equals(left.ProtocolGameVersion, right.ProtocolGameVersion, StringComparison.Ordinal)
        && string.Equals(left.WireCacheSignatureV1, right.WireCacheSignatureV1, StringComparison.Ordinal)
        && left.RitsuLibPresent == right.RitsuLibPresent
        && string.Equals(left.CapabilityDigest, right.CapabilityDigest, StringComparison.Ordinal);

    private static void PreserveCorruptConfig(string path)
    {
        CopyPrivateFile(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
    }

    private static void CopyPrivateFile(string source, string destination)
    {
        using FileStream input = File.OpenRead(source);
        using FileStream output = CreatePrivateFile(destination);
        input.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private static FileStream CreatePrivateFile(string path)
    {
        FileStreamOptions options = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = PrivateFileMode;
        }

        return new FileStream(path, options);
    }

    private static void SetPrivateUnixMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, PrivateFileMode);
        }
    }

    private static string TemporaryPath(string path)
    {
        return path + ".tmp-" + Guid.NewGuid().ToString("N");
    }
}
