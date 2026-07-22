using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sts2LanConnect.Tests.Packaging;

public sealed class LanConnectPackageContentTests
{
    private static readonly string[] ExpectedFiles =
    [
        "LICENSE",
        "README.md",
        "STS2_LAN_CONNECT_USER_GUIDE_ZH.md",
        "THIRD_PARTY_NOTICES",
        "install-sts2-lan-connect-macos.command",
        "install-sts2-lan-connect-macos.sh",
        "install-sts2-lan-connect-windows.bat",
        "install-sts2-lan-connect-windows.ps1",
        "lobby-defaults.json",
        "sts2_lan_connect.dll",
        "sts2_lan_connect.json",
        "sts2_lan_connect.pck"
    ];

    [Fact]
    public async Task Client_package_uses_exact_allowlist_source_legal_bytes_and_deterministic_output()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Fixture fixture = new();
        string[] historicalBefore = fixture.SnapshotHistorical();
        ProcessResult first = await fixture.RunAsync(
            fixture.TempRoot,
            "--output-dir",
            Path.Combine("relative output", "with spaces"));

        Assert.Equal(0, first.ExitCode);
        string firstOutput = Path.Combine(fixture.TempRoot, "relative output", "with spaces");
        string packageDirectory = Path.Combine(firstOutput, "sts2_lan_connect");
        string firstZip = Path.Combine(firstOutput, "sts2_lan_connect-release.zip");
        Assert.Equal(ExpectedFiles, Fixture.ListFiles(packageDirectory));
        Assert.Equal(ExpectedFiles, Fixture.ListZipFiles(firstZip));

        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(packageDirectory, "sts2_lan_connect.json")));
        Assert.Equal("0.5.2", manifest.RootElement.GetProperty("version").GetString());
        FileVersionInfo assemblyVersion = FileVersionInfo.GetVersionInfo(
            Path.Combine(packageDirectory, "sts2_lan_connect.dll"));
        Assert.Equal("0.5.2.0", assemblyVersion.FileVersion);
        Assert.StartsWith("0.5.2", assemblyVersion.ProductVersion, StringComparison.Ordinal);

        foreach (string packagePath in ExpectedFiles)
        {
            Assert.Equal(
                File.ReadAllBytes(fixture.SourcePath(packagePath)),
                File.ReadAllBytes(Path.Combine(packageDirectory, packagePath)));
        }
        AssertCleanManifest(ExpectedFiles);

        string absoluteOutput = Path.Combine(fixture.TempRoot, "absolute output [safe]");
        ProcessResult second = await fixture.RunAsync(
            fixture.RepositoryRoot,
            "--skip-build",
            "--output-dir",
            absoluteOutput);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(
            SHA256.HashData(File.ReadAllBytes(firstZip)),
            SHA256.HashData(File.ReadAllBytes(Path.Combine(absoluteOutput, "sts2_lan_connect-release.zip"))));
        Assert.Equal(historicalBefore, fixture.SnapshotHistorical());
    }

    [Fact]
    public void Client_v052_candidate_documents_the_new_reference_paths_without_requiring_a_service_upgrade()
    {
        using Fixture fixture = new();
        string changelog = File.ReadAllText(Path.Combine(fixture.RepositoryRoot, "CHANGELOG.md"));
        string releaseNotes = File.ReadAllText(Path.Combine(
            fixture.RepositoryRoot,
            "docs",
            "RELEASE_NOTES_V0.5.2_ZH.md"));
        string clientReadme = File.ReadAllText(Path.Combine(
            fixture.RepositoryRoot,
            "docs",
            "CLIENT_RELEASE_README_ZH.md"));
        string userGuide = File.ReadAllText(Path.Combine(
            fixture.RepositoryRoot,
            "docs",
            "STS2_LAN_CONNECT_USER_GUIDE_ZH.md"));
        string workshop = File.ReadAllText(Path.Combine(
            fixture.RepositoryRoot,
            "docs",
            "STEAM_WORKSHOP_DESCRIPTION_ZH.txt"));

        foreach (string text in new[] { changelog, releaseNotes, clientReadme, userGuide, workshop })
        {
            Assert.Contains("0.5.2", text, StringComparison.Ordinal);
            Assert.Contains("一次性引用", text, StringComparison.Ordinal);
        }
        Assert.Contains("Alt+R", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Alt+左键", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Android", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("点击", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("lobby-service 0.5.1", releaseNotes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_package_rejects_missing_repeated_protected_traversal_and_symlink_outputs()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Fixture fixture = new();
        string[] historicalBefore = fixture.SnapshotHistorical();
        string[][] rejectedArguments =
        [
            ["--output-dir"],
            ["--output-dir", Path.Combine(fixture.TempRoot, "one"), "--output-dir", Path.Combine(fixture.TempRoot, "two")],
            ["--output-dir", Path.Combine(fixture.RepositoryRoot, "releases")],
            ["--output-dir", Path.Combine(fixture.RepositoryRoot, "releases", "nested")],
            ["--output-dir", Path.Combine(fixture.RepositoryRoot, "sts2-lan-connect", "release", "nested")],
            ["--output-dir", Path.Combine(fixture.RepositoryRoot, "lobby-service", "release", "nested")],
            ["--output-dir", Path.Combine(fixture.RepositoryRoot, "scripts", "package output")],
            ["--output-dir", Path.Combine(fixture.TempRoot, "safe", "..", "escape")]
        ];

        foreach (string[] arguments in rejectedArguments)
        {
            ProcessResult result = await fixture.RunAsync(fixture.RepositoryRoot, arguments);
            Assert.NotEqual(0, result.ExitCode);
        }

        string link = Path.Combine(fixture.TempRoot, "release-link");
        Directory.CreateSymbolicLink(link, Path.Combine(fixture.RepositoryRoot, "releases"));
        ProcessResult symlink = await fixture.RunAsync(
            fixture.RepositoryRoot,
            "--output-dir",
            Path.Combine(link, "nested"));
        Assert.NotEqual(0, symlink.ExitCode);
        Assert.Contains("protected release output path", symlink.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(historicalBefore, fixture.SnapshotHistorical());
    }

    private static void AssertCleanManifest(IEnumerable<string> files)
    {
        string[] forbiddenBinaryNames =
        [
            "sts2.dll",
            "Steamworks.NET.dll",
            "0Harmony.dll",
            "GodotSharp.dll",
            "GodotSharpEditor.dll"
        ];
        foreach (string file in files)
        {
            string lower = file.ToLowerInvariant();
            Assert.DoesNotContain(
                forbiddenBinaryNames,
                forbidden => string.Equals(Path.GetFileName(file), forbidden, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("typing.dll", lower, StringComparison.Ordinal);
            Assert.DoesNotContain("/.env", "/" + lower, StringComparison.Ordinal);
            Assert.DoesNotContain("/.git/", "/" + lower + "/", StringComparison.Ordinal);
            Assert.DoesNotContain("/test/", "/" + lower + "/", StringComparison.Ordinal);
            Assert.DoesNotContain("/tests/", "/" + lower + "/", StringComparison.Ordinal);
            Assert.DoesNotContain(".test.", lower, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", lower, StringComparison.Ordinal);
            Assert.DoesNotContain("admin-state", lower, StringComparison.Ordinal);
            if (!string.Equals(file, "sts2_lan_connect.pck", StringComparison.Ordinal))
            {
                Assert.False(lower.EndsWith(".pck", StringComparison.Ordinal));
            }
            Assert.False(lower.EndsWith(".png", StringComparison.Ordinal));
            Assert.False(lower.EndsWith(".jpg", StringComparison.Ordinal));
            Assert.False(lower.EndsWith(".jpeg", StringComparison.Ordinal));
            Assert.False(lower.EndsWith(".ttf", StringComparison.Ordinal));
            Assert.False(lower.EndsWith(".otf", StringComparison.Ordinal));
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal Fixture()
        {
            RepositoryRoot = FindRepositoryRoot();
            TempRoot = Path.Combine(Path.GetTempPath(), "sts2 package contract " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempRoot);
        }

        internal string RepositoryRoot { get; }
        internal string TempRoot { get; }

        internal async Task<ProcessResult> RunAsync(string workingDirectory, params string[] arguments)
        {
            ProcessStartInfo startInfo = new(Path.Combine(RepositoryRoot, "scripts", "package-sts2-lan-connect.sh"))
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            string? dotnet = ResolveTool(
                "DOTNET_BIN",
                Environment.ProcessPath,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet"));
            string? godot = ResolveTool(
                "GODOT_BIN",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "Godot_mono.app", "Contents", "MacOS", "Godot"),
                "/Applications/Godot_mono.app/Contents/MacOS/Godot",
                "/Applications/Godot.app/Contents/MacOS/Godot");
            if (dotnet != null) startInfo.Environment["DOTNET_BIN"] = dotnet;
            if (godot != null) startInfo.Environment["GODOT_BIN"] = godot;

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start client package process.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }

        internal string[] SnapshotHistorical() =>
        [
            SnapshotTree(Path.Combine(RepositoryRoot, "releases")),
            SnapshotTree(Path.Combine(RepositoryRoot, "sts2-lan-connect", "release")),
            SnapshotTree(Path.Combine(RepositoryRoot, "lobby-service", "release"))
        ];

        internal string SourcePath(string packagePath) => packagePath switch
        {
            "LICENSE" or "THIRD_PARTY_NOTICES" => Path.Combine(RepositoryRoot, packagePath),
            "README.md" => Path.Combine(RepositoryRoot, "docs", "CLIENT_RELEASE_README_ZH.md"),
            "STS2_LAN_CONNECT_USER_GUIDE_ZH.md" => Path.Combine(RepositoryRoot, "docs", packagePath),
            "install-sts2-lan-connect-macos.sh" or
            "install-sts2-lan-connect-macos.command" or
            "install-sts2-lan-connect-windows.ps1" or
            "install-sts2-lan-connect-windows.bat" => Path.Combine(RepositoryRoot, "scripts", packagePath),
            "lobby-defaults.json" or "sts2_lan_connect.json" => Path.Combine(RepositoryRoot, "sts2-lan-connect", packagePath),
            "sts2_lan_connect.dll" => Path.Combine(RepositoryRoot, "sts2-lan-connect", ".godot", "mono", "temp", "bin", "Debug", packagePath),
            "sts2_lan_connect.pck" => Path.Combine(RepositoryRoot, "sts2-lan-connect", "build", packagePath),
            _ => throw new InvalidOperationException($"Unknown client package entry: {packagePath}")
        };

        internal static string[] ListFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        internal static string[] ListZipFiles(string zipPath)
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            string[] entries = archive.Entries.Select(entry => entry.FullName).ToArray();
            Assert.Equal(entries.Length, entries.Distinct(StringComparer.Ordinal).Count());
            foreach (string entry in entries)
            {
                Assert.False(entry.StartsWith("/", StringComparison.Ordinal));
                Assert.DoesNotContain('\\', entry);
                Assert.DoesNotContain("..", entry.Split('/'));
                Assert.True(entry == "sts2_lan_connect" || entry.StartsWith("sts2_lan_connect/", StringComparison.Ordinal));
            }
            return entries
                .Where(entry => entry.StartsWith("sts2_lan_connect/", StringComparison.Ordinal) && !entry.EndsWith('/'))
                .Select(entry => entry["sts2_lan_connect/".Length..])
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        public void Dispose() => Directory.Delete(TempRoot, recursive: true);

        private static string SnapshotTree(string root)
        {
            if (!Directory.Exists(root)) return "MISSING";
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                         .Prepend(root)
                         .Order(StringComparer.Ordinal))
            {
                FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                string record = $"{Path.GetRelativePath(root, path)}|{info.Attributes}|{info.LastWriteTimeUtc.Ticks}|";
                hash.AppendData(System.Text.Encoding.UTF8.GetBytes(record));
                if (File.Exists(path))
                {
                    hash.AppendData(File.ReadAllBytes(path));
                }
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        private static string? ResolveTool(string environmentKey, params string?[] candidates)
        {
            string? configured = Environment.GetEnvironmentVariable(environmentKey);
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
            return candidates.FirstOrDefault(candidate =>
                !string.IsNullOrWhiteSpace(candidate) &&
                File.Exists(candidate) &&
                string.Equals(Path.GetFileName(candidate), environmentKey == "DOTNET_BIN" ? "dotnet" : "Godot", StringComparison.OrdinalIgnoreCase));
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
