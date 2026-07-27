using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectSaveBackupTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "sts2-lan-connect-backup-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreatesProfileScopedNonOverwritingBackup()
    {
        Directory.CreateDirectory(_tempDirectory);
        string source = Path.Combine(_tempDirectory, "current_run_mp.save");
        File.WriteAllText(source, "save-content");
        string backupRoot = Path.Combine(_tempDirectory, "backups");
        DateTimeOffset timestamp = new(2026, 7, 25, 1, 2, 3, 456, TimeSpan.Zero);

        Assert.True(LanConnectSaveBackup.TryCreate(source, backupRoot, 7, timestamp, out string first, out string firstError), firstError);
        Assert.True(LanConnectSaveBackup.TryCreate(source, backupRoot, 7, timestamp, out string second, out string secondError), secondError);

        Assert.NotEqual(first, second);
        Assert.Equal("save-content", File.ReadAllText(first));
        Assert.Equal("save-content", File.ReadAllText(second));
        Assert.Contains("profile-7", first);
    }

    [Fact]
    public void MissingSourceFailsWithoutCreatingBackup()
    {
        Assert.False(LanConnectSaveBackup.TryCreate(
            Path.Combine(_tempDirectory, "missing.save"),
            Path.Combine(_tempDirectory, "backups"),
            1,
            DateTimeOffset.UtcNow,
            out string backupPath,
            out string error));

        Assert.Empty(backupPath);
        Assert.NotEmpty(error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
