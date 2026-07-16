using System.Diagnostics;
using System.Security.Cryptography;

namespace Sts2LanConnect.Tests.Packaging;

public sealed class LanConnectInstallerContractTests
{
    private static readonly string[] RequiredPayloadFiles =
    [
        "sts2_lan_connect.dll",
        "sts2_lan_connect.pck",
        "lobby-defaults.json",
        "LICENSE",
        "THIRD_PARTY_NOTICES"
    ];

    [Fact]
    public async Task Build_install_dry_run_prints_the_shared_plan_without_any_write()
    {
        using Fixture fixture = new();
        string[] before = fixture.Snapshot();

        ProcessResult result = await fixture.RunBuildAsync("--install", "--dry-run", "--no-save-sync");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.Equal(before, fixture.Snapshot());
        Assert.False(File.Exists(fixture.ToolMarker));
        foreach (string fileName in RequiredPayloadFiles)
        {
            string destination = Path.Combine(fixture.TargetModDirectory, fileName);
            Assert.Equal(1, CountOccurrences(result.Stdout, destination));
            Assert.Contains($"DRY-RUN copy: {Path.Combine(fixture.PackageDirectory, fileName)} -> {destination}", result.Stdout);
        }
    }

    [Theory]
    [MemberData(nameof(RequiredPayloadFilesData))]
    public async Task Dry_run_fails_atomically_when_a_required_input_is_missing(string missingFile)
    {
        using Fixture fixture = new();
        File.Delete(Path.Combine(fixture.PackageDirectory, missingFile));
        string[] before = fixture.Snapshot();

        ProcessResult result = await fixture.RunBuildAsync("--install", "--dry-run");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(Path.Combine(fixture.PackageDirectory, missingFile), result.Stderr);
        Assert.Equal(before, fixture.Snapshot());
        Assert.False(Directory.Exists(fixture.TargetModDirectory));
        Assert.False(File.Exists(fixture.ToolMarker));
    }

    [Fact]
    public async Task Dry_run_requires_install_and_rejects_unknown_options_without_writes()
    {
        using Fixture fixture = new();
        string[] before = fixture.Snapshot();

        ProcessResult withoutInstall = await fixture.RunBuildAsync("--dry-run");
        Assert.NotEqual(0, withoutInstall.ExitCode);
        Assert.Contains("--dry-run requires --install", withoutInstall.Stderr);
        Assert.Equal(before, fixture.Snapshot());

        ProcessResult unknown = await fixture.RunBuildAsync("--install", "--dry-run", "--unknown-option");
        Assert.NotEqual(0, unknown.ExitCode);
        Assert.Contains("Unknown option", unknown.Stderr);
        Assert.Equal(before, fixture.Snapshot());
    }

    [Fact]
    public async Task Real_install_uses_the_exact_dry_run_destinations_and_source_bytes()
    {
        using Fixture fixture = new();
        ProcessResult planned = await fixture.RunBuildAsync("--install", "--dry-run", "--no-save-sync");
        Assert.Equal(0, planned.ExitCode);

        ProcessResult installed = await fixture.RunInstallerAsync(
            "--install",
            "--skip-codesign",
            "--no-save-sync");
        Assert.Equal(0, installed.ExitCode);

        string[] installedFiles = Directory.EnumerateFiles(fixture.TargetModDirectory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(RequiredPayloadFiles.Order(StringComparer.Ordinal), installedFiles);
        foreach (string fileName in RequiredPayloadFiles)
        {
            string source = Path.Combine(fixture.PackageDirectory, fileName);
            string destination = Path.Combine(fixture.TargetModDirectory, fileName);
            Assert.Equal(1, CountOccurrences(planned.Stdout, destination));
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(destination));
        }
        Assert.False(File.Exists(fixture.ToolMarker));
    }

    public static TheoryData<string> RequiredPayloadFilesData()
    {
        TheoryData<string> data = new();
        foreach (string fileName in RequiredPayloadFiles)
        {
            data.Add(fileName);
        }
        return data;
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "sts2 installer contract " + Guid.NewGuid().ToString("N"));
        private readonly string _repositoryRoot = FindRepositoryRoot();
        private readonly string _fakeTool;

        internal Fixture()
        {
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("The macOS installer contract requires a POSIX shell.");
            }
            PackageDirectory = Path.Combine(_root, "Package With Spaces [safe]");
            AppPath = Path.Combine(_root, "Fake Game With Spaces", "SlayTheSpire2.app");
            UserDataDirectory = Path.Combine(_root, "Home With Spaces", "Library", "Application Support", "SlayTheSpire2");
            ToolMarker = Path.Combine(_root, "tool-was-called");
            _fakeTool = Path.Combine(_root, "fake tool");

            Directory.CreateDirectory(PackageDirectory);
            Directory.CreateDirectory(Path.Combine(AppPath, "Contents", "MacOS"));
            Directory.CreateDirectory(UserDataDirectory);
            Directory.CreateDirectory(Path.Combine(_root, "tmp"));
            foreach (string fileName in RequiredPayloadFiles)
            {
                File.WriteAllText(Path.Combine(PackageDirectory, fileName), $"fixture:{fileName}\n");
            }
            File.WriteAllText(
                _fakeTool,
                "#!/bin/sh\nprintf called > \"$STS2_TEST_TOOL_MARKER\"\nexit 99\n");
            File.SetUnixFileMode(
                _fakeTool,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        internal string PackageDirectory { get; }
        internal string AppPath { get; }
        internal string UserDataDirectory { get; }
        internal string ToolMarker { get; }
        internal string TargetModDirectory => Path.Combine(
            AppPath,
            "Contents",
            "MacOS",
            "mods",
            "sts2_lan_connect");

        internal Task<ProcessResult> RunBuildAsync(params string[] additionalArguments) =>
            RunAsync(
                Path.Combine(_repositoryRoot, "scripts", "build-sts2-lan-connect.sh"),
                ["--mods-dir", PackageDirectory, .. additionalArguments]);

        internal Task<ProcessResult> RunInstallerAsync(params string[] additionalArguments) =>
            RunAsync(
                Path.Combine(_repositoryRoot, "scripts", "install-sts2-lan-connect-macos.sh"),
                [
                    "--app-path", AppPath,
                    "--data-dir", UserDataDirectory,
                    "--package-dir", PackageDirectory,
                    .. additionalArguments
                ]);

        internal string[] Snapshot() => Directory.EnumerateFileSystemEntries(
                _root,
                "*",
                SearchOption.AllDirectories)
            .Prepend(_root)
            .Select(path =>
            {
                string relative = Path.GetRelativePath(_root, path);
                if (File.Exists(path))
                {
                    FileInfo file = new(path);
                    return $"F|{relative}|{file.Length}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}|{file.LastWriteTimeUtc.Ticks}";
                }
                DirectoryInfo directory = new(path);
                return $"D|{relative}|{directory.LastWriteTimeUtc.Ticks}";
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        private async Task<ProcessResult> RunAsync(string script, IReadOnlyList<string> arguments)
        {
            ProcessStartInfo startInfo = new("/bin/bash")
            {
                WorkingDirectory = _repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(script);
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
            startInfo.Environment["HOME"] = Path.Combine(_root, "Home With Spaces");
            startInfo.Environment["TMPDIR"] = Path.Combine(_root, "tmp");
            startInfo.Environment["STS2_APP_PATH"] = AppPath;
            startInfo.Environment["STS2_USERDATA_DIR"] = UserDataDirectory;
            startInfo.Environment["DOTNET_BIN"] = _fakeTool;
            startInfo.Environment["GODOT_BIN"] = _fakeTool;
            startInfo.Environment["CODESIGN_BIN"] = _fakeTool;
            startInfo.Environment["STS2_TEST_TOOL_MARKER"] = ToolMarker;

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start installer contract process.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }

        public void Dispose()
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

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
