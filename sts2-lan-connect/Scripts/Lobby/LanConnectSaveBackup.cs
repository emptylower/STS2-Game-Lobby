using System;
using System.Globalization;
using System.IO;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectSaveBackup
{
    internal static bool TryCreate(
        string sourcePath,
        string backupRoot,
        int profileId,
        DateTimeOffset timestamp,
        out string backupPath,
        out string error)
    {
        backupPath = string.Empty;
        error = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "当前多人存档文件不存在。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(backupRoot))
            {
                error = "备份目录不可用。";
                return false;
            }

            string profileDirectory = Path.Combine(
                backupRoot,
                $"profile-{profileId.ToString(CultureInfo.InvariantCulture)}");
            Directory.CreateDirectory(profileDirectory);
            string timestampToken = timestamp.UtcDateTime.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            string baseName = $"{timestampToken}-current_run_mp.save";
            backupPath = Path.Combine(profileDirectory, baseName);
            int collision = 1;
            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(
                    profileDirectory,
                    $"{timestampToken}-{collision.ToString(CultureInfo.InvariantCulture)}-current_run_mp.save");
                collision++;
            }

            File.Copy(sourcePath, backupPath, overwrite: false);
            return true;
        }
        catch (Exception ex)
        {
            backupPath = string.Empty;
            error = ex.Message;
            return false;
        }
    }
}
