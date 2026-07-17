using System.Text.Json;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.ModSync;

public sealed class LanConnectModInventoryBuilderTests
{
    [Fact]
    public void Build_includes_gameplay_roots_and_only_their_transitive_dependencies()
    {
        LanConnectRuntimeMod[] mods =
        [
            Mod("utility.unrelated", gameplay: false),
            Mod("gameplay.root", gameplay: true, dependencies: ["dependency.one"]),
            Mod("dependency.one", gameplay: false, dependencies: ["dependency.two"]),
            Mod("dependency.two", gameplay: false),
            Mod("gameplay.disabled", gameplay: true, loaded: false)
        ];

        IReadOnlyList<LobbyModDescriptor> result = LanConnectModInventoryBuilder.Build(mods);

        Assert.Equal(["dependency.one", "dependency.two", "gameplay.root"], result.Select(mod => mod.Id));
        Assert.Equal(LanConnectModRoles.Dependency, result[0].Role);
        Assert.Equal(LanConnectModRoles.Dependency, result[1].Role);
        Assert.Equal(LanConnectModRoles.Gameplay, result[2].Role);
    }

    [Fact]
    public void Build_terminates_cycles_deduplicates_diamonds_and_promotes_gameplay_roles()
    {
        LanConnectRuntimeMod[] mods =
        [
            Mod("root.a", gameplay: true, dependencies: ["shared", "root.b"]),
            Mod("root.b", gameplay: true, dependencies: ["shared", "root.a"]),
            Mod("shared", gameplay: false)
        ];

        IReadOnlyList<LobbyModDescriptor> result = LanConnectModInventoryBuilder.Build(mods);

        Assert.Equal(["root.a", "root.b", "shared"], result.Select(mod => mod.Id));
        Assert.Equal(LanConnectModRoles.Gameplay, result[0].Role);
        Assert.Equal(LanConnectModRoles.Gameplay, result[1].Role);
        Assert.Equal(LanConnectModRoles.Dependency, result[2].Role);
    }

    [Fact]
    public void Build_excludes_lan_connect_and_reserved_ids()
    {
        LanConnectRuntimeMod[] mods =
        [
            Mod("sts2_lan_connect", gameplay: true),
            Mod("lan_connect.internal", gameplay: true),
            Mod("sts2-lan-connect.internal", gameplay: true),
            Mod("safe.gameplay", gameplay: true)
        ];

        LobbyModDescriptor descriptor = Assert.Single(LanConnectModInventoryBuilder.Build(mods));
        Assert.Equal("safe.gameplay", descriptor.Id);
    }

    [Fact]
    public void Runtime_resolver_supports_0_107_1_field_shape()
    {
        V107Mod value = new()
        {
            state = V107State.Loaded,
            modSource = V107Source.SteamWorkshop,
            path = "/steamapps/workshop/content/2868840/3747497501",
            manifest = new V107Manifest
            {
                id = "fixture.visual",
                version = "1.2.3",
                affectsGameplay = true,
                dependencies = [new V107Dependency { id = "fixture.core" }]
            }
        };

        LanConnectRuntimeMod resolved = LanConnectModInventoryBuilder.ResolveRuntimeMod(value);

        Assert.Equal("fixture.visual", resolved.Id);
        Assert.Equal("1.2.3", resolved.Version);
        Assert.True(resolved.IsLoaded);
        Assert.True(resolved.AffectsGameplay);
        Assert.Equal(LanConnectModSources.SteamWorkshop, resolved.Source);
        Assert.Equal("3747497501", resolved.WorkshopFileId);
        Assert.Equal(["fixture.core"], resolved.Dependencies);
    }

    [Fact]
    public void Runtime_resolver_treats_declared_null_dependencies_as_empty()
    {
        V107Mod value = new()
        {
            state = V107State.Loaded,
            modSource = V107Source.ModsDirectory,
            path = "/game/mods/fixture",
            manifest = new V107Manifest
            {
                id = "fixture.no_dependencies",
                version = "1.0.0",
                affectsGameplay = true,
                dependencies = null
            }
        };

        LanConnectRuntimeMod resolved = LanConnectModInventoryBuilder.ResolveRuntimeMod(value);

        Assert.Empty(resolved.Dependencies);
    }

    [Fact]
    public void Runtime_resolver_supports_0_108_0_property_shape()
    {
        V108Mod value = new()
        {
            State = V108State.Loaded,
            Source = V108Source.ModsDirectory,
            Path = "/game/mods/fixture",
            Manifest = new V108Manifest
            {
                Id = "fixture.manual",
                Version = "2.0.0",
                AffectsGameplay = true,
                Dependencies = [new V108Dependency { Id = "fixture.core" }]
            }
        };

        LanConnectRuntimeMod resolved = LanConnectModInventoryBuilder.ResolveRuntimeMod(value);

        Assert.Equal("fixture.manual", resolved.Id);
        Assert.Equal(LanConnectModSources.ModsDirectory, resolved.Source);
        Assert.Null(resolved.WorkshopFileId);
    }

    [Fact]
    public void Runtime_resolver_supports_0_109_0_workshop_id_field_shape()
    {
        V109Mod value = new()
        {
            state = V109State.Loaded,
            modSource = V109Source.SteamWorkshop,
            path = "/steamapps/workshop/content/2868840/non-numeric-folder",
            workshopId = 3762348066UL,
            manifest = new V107Manifest
            {
                id = "fixture.current",
                version = "3.0.0",
                affectsGameplay = true,
                dependencies = []
            }
        };

        LanConnectRuntimeMod resolved = LanConnectModInventoryBuilder.ResolveRuntimeMod(value);

        Assert.True(resolved.IsLoaded);
        Assert.Equal("3762348066", resolved.WorkshopFileId);
    }

    [Fact]
    public void Runtime_metadata_matches_the_supported_version_inventory_contract()
    {
        string assemblyPath = ResolveGameAssemblyPath();
        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader peReader = new(stream);
        MetadataReader metadata = peReader.GetMetadataReader();

        AssertFields(metadata, "MegaCrit.Sts2.Core.Modding", "ModManifest",
            "id", "version", "affectsGameplay", "dependencies");
        AssertFields(metadata, "MegaCrit.Sts2.Core.Modding", "Mod",
            "manifest", "state", "modSource", "path");

        HashSet<string> modFields = ReadFields(metadata, "MegaCrit.Sts2.Core.Modding", "Mod");
        switch (ResolveGameVersion(assemblyPath))
        {
            case "v0.107.1":
                Assert.DoesNotContain("workshopId", modFields);
                break;
            case "v0.109.0":
                Assert.Contains("workshopId", modFields);
                break;
            default:
                throw new InvalidOperationException("The runtime metadata contract has not been locked for this game version.");
        }
    }

    [Fact]
    public void Runtime_resolver_rejects_ambiguous_manifest_members()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LanConnectModInventoryBuilder.ResolveRuntimeMod(new AmbiguousMod()));
    }

    [Fact]
    public void Canonicalize_trims_deduplicates_and_ordinal_sorts()
    {
        LobbyModDescriptor[] descriptors =
        [
            Descriptor(" z.mod ", dependencies: [" dep.b ", "dep.a", "dep.a"]),
            Descriptor("A.mod")
        ];

        IReadOnlyList<LobbyModDescriptor> result = LanConnectModInventoryValidator.Canonicalize(descriptors);

        Assert.Equal(["A.mod", "z.mod"], result.Select(mod => mod.Id));
        Assert.Equal(["dep.a", "dep.b"], result[1].Dependencies);
    }

    [Theory]
    [InlineData(65, "descriptor")]
    [InlineData(1, "identifier")]
    public void Validate_rejects_descriptor_and_identifier_limits(int mode, string expected)
    {
        IReadOnlyList<LobbyModDescriptor> descriptors = mode == 65
            ? Enumerable.Range(0, mode).Select(index => Descriptor($"mod.{index}")).ToArray()
            : [Descriptor(new string('x', LanConnectModSyncCapabilities.MaxIdCharacters + 1))];

        LanConnectModInventoryException error = Assert.Throws<LanConnectModInventoryException>(() =>
            LanConnectModInventoryValidator.Canonicalize(descriptors));
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("123456789012345678901")]
    public void Validate_rejects_invalid_workshop_ids(string workshopFileId)
    {
        Assert.Throws<LanConnectModInventoryException>(() =>
            LanConnectModInventoryValidator.Canonicalize([
                Descriptor("mod", source: LanConnectModSources.SteamWorkshop, workshopFileId: workshopFileId)
            ]));
    }

    [Fact]
    public void Validate_rejects_duplicate_control_and_reserved_ids()
    {
        Assert.Throws<LanConnectModInventoryException>(() =>
            LanConnectModInventoryValidator.Canonicalize([Descriptor("same"), Descriptor(" same ")]));
        Assert.Throws<LanConnectModInventoryException>(() =>
            LanConnectModInventoryValidator.Canonicalize([Descriptor("bad\u0001id")]));
        Assert.Throws<LanConnectModInventoryException>(() =>
            LanConnectModInventoryValidator.Canonicalize([Descriptor("lan_connect.private")]));
    }

    [Fact]
    public void Validate_rejects_dependency_and_payload_limits()
    {
        string[] dependencies = Enumerable.Range(0, 17).Select(index => $"dep.{index}").ToArray();
        Assert.Throws<LanConnectModInventoryException>(() =>
            LanConnectModInventoryValidator.Canonicalize([Descriptor("root", dependencies: dependencies)]));

        string longVersion = new('v', LanConnectModSyncCapabilities.MaxVersionCharacters);
        LobbyModDescriptor[] descriptors = Enumerable.Range(0, 64)
            .Select(index => Descriptor($"mod.{index:D2}.{new string('x', 110)}", version: longVersion))
            .ToArray();
        Assert.Throws<LanConnectModInventoryException>(() =>
            LanConnectModInventoryValidator.Canonicalize(descriptors, maxPayloadBytes: 1024));
    }

    [Fact]
    public void Canonical_json_uses_cross_runtime_property_order_and_utf8_text()
    {
        string json = LanConnectModInventoryValidator.SerializeCanonical([
            Descriptor("mod.测试", version: "版本一")
        ]);

        Assert.Equal(
            "[{\"id\":\"mod.测试\",\"version\":\"版本一\",\"role\":\"gameplay\",\"source\":\"mods_directory\",\"dependencies\":[]}]",
            json);
    }

    [Fact]
    public void Diff_matches_shared_canonical_fixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestFixtures", "ModSync", "canonical-diff-v1.json");
        SharedFixture fixture = JsonSerializer.Deserialize<SharedFixture>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        LanConnectModDiff result = LanConnectModDiffResolver.Resolve(
            fixture.HostMods,
            fixture.LocalMods,
            fixture.GameVersion.Host,
            fixture.GameVersion.Local);

        Assert.True(result.GameVersion.ExactMatch);
        Assert.Equal(fixture.Expected.MissingWorkshopModIds, result.MissingWorkshopMods.Select(mod => mod.Id));
        Assert.Equal(fixture.Expected.MissingManualModIds, result.MissingManualMods.Select(mod => mod.Id));
        Assert.Equal(fixture.Expected.ExtraGameplayModIds, result.ExtraGameplayMods.Select(mod => mod.Id));
        Assert.Equal(fixture.Expected.VersionMismatchModIds, result.VersionMismatches.Select(mod => mod.Id));
        Assert.Equal(fixture.Expected.CanContinueRelaxed, result.CanContinueRelaxed);
    }

    [Fact]
    public void Diff_blocks_cross_game_version_before_mod_comparison()
    {
        LanConnectModDiff result = LanConnectModDiffResolver.Resolve(
            [Descriptor("host.only")],
            [Descriptor("local.only")],
            "v0.108.0",
            "v0.109.0");

        Assert.False(result.GameVersion.ExactMatch);
        Assert.False(result.CanContinueRelaxed);
        Assert.Empty(result.MissingWorkshopMods);
        Assert.Empty(result.MissingManualMods);
        Assert.Empty(result.ExtraGameplayMods);
        Assert.Empty(result.VersionMismatches);
    }

    [Fact]
    public void Diff_ignores_dependency_only_local_extras()
    {
        LanConnectModDiff result = LanConnectModDiffResolver.Resolve(
            [],
            [Descriptor("extra.dependency", role: LanConnectModRoles.Dependency)],
            "v0.109.0",
            "v0.109.0");

        Assert.Empty(result.ExtraGameplayMods);
    }

    private static LanConnectRuntimeMod Mod(
        string id,
        bool gameplay,
        bool loaded = true,
        IReadOnlyList<string>? dependencies = null) =>
        new(id, "1.0.0", gameplay, loaded, LanConnectModSources.ModsDirectory, null, dependencies ?? []);

    private static LobbyModDescriptor Descriptor(
        string id,
        string version = "1.0.0",
        string role = LanConnectModRoles.Gameplay,
        string source = LanConnectModSources.ModsDirectory,
        string? workshopFileId = null,
        IReadOnlyList<string>? dependencies = null) =>
        new()
        {
            Id = id,
            Version = version,
            Role = role,
            Source = source,
            WorkshopFileId = workshopFileId,
            Dependencies = dependencies?.ToList() ?? []
        };

    private static string ResolveGameAssemblyPath()
    {
        string? overrideDirectory = Environment.GetEnvironmentVariable("STS2_TEST_GAME_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return Path.Combine(overrideDirectory, "sts2.dll");
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string root = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam", "steamapps", "common", "Slay the Spire 2")
            : Path.Combine(home, "Library", "Application Support", "Steam", "steamapps", "common", "Slay the Spire 2");
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(root, "data_sts2_windows_x86_64", "sts2.dll");
        }

        string architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
            == System.Runtime.InteropServices.Architecture.Arm64
            ? "data_sts2_macos_arm64"
            : "data_sts2_macos_x86_64";
        return Path.Combine(root, "SlayTheSpire2.app", "Contents", "Resources", architecture, "sts2.dll");
    }

    private static string ResolveGameVersion(string assemblyPath)
    {
        string dataDirectory = Path.GetDirectoryName(assemblyPath)
            ?? throw new InvalidOperationException("The game assembly path has no parent directory.");
        string[] candidates =
        [
            Path.Combine(dataDirectory, "release_info.json"),
            Path.GetFullPath(Path.Combine(dataDirectory, "..", "release_info.json")),
            Path.GetFullPath(Path.Combine(dataDirectory, "..", "..", "release_info.json"))
        ];
        string releaseInfoPath = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Could not locate release_info.json for the game assembly.");
        using JsonDocument releaseInfo = JsonDocument.Parse(File.ReadAllText(releaseInfoPath));
        return releaseInfo.RootElement.GetProperty("version").GetString()
            ?? throw new InvalidDataException("release_info.json has no version value.");
    }

    private static void AssertFields(
        MetadataReader metadata,
        string typeNamespace,
        string typeName,
        params string[] expectedFields)
    {
        HashSet<string> fields = ReadFields(metadata, typeNamespace, typeName);

        foreach (string expected in expectedFields)
        {
            Assert.Contains(expected, fields);
        }
    }

    private static HashSet<string> ReadFields(
        MetadataReader metadata,
        string typeNamespace,
        string typeName)
    {
        TypeDefinition definition = metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Single(type => metadata.GetString(type.Namespace) == typeNamespace
                && metadata.GetString(type.Name) == typeName);
        return definition.GetFields()
            .Select(handle => metadata.GetString(metadata.GetFieldDefinition(handle).Name))
            .ToHashSet(StringComparer.Ordinal);
    }

    private sealed class SharedFixture
    {
        public SharedGameVersion GameVersion { get; init; } = new();
        public List<LobbyModDescriptor> HostMods { get; init; } = [];
        public List<LobbyModDescriptor> LocalMods { get; init; } = [];
        public SharedExpected Expected { get; init; } = new();
    }

    private sealed class SharedGameVersion
    {
        public string Host { get; init; } = string.Empty;
        public string Local { get; init; } = string.Empty;
    }

    private sealed class SharedExpected
    {
        public List<string> MissingWorkshopModIds { get; init; } = [];
        public List<string> MissingManualModIds { get; init; } = [];
        public List<string> ExtraGameplayModIds { get; init; } = [];
        public List<string> VersionMismatchModIds { get; init; } = [];
        public bool CanContinueRelaxed { get; init; }
    }

    private enum V107State { None, Loaded }
    private enum V107Source { ModsDirectory, SteamWorkshop }
    private sealed class V107Dependency { public string id = string.Empty; }
    private sealed class V107Manifest
    {
        public string id = string.Empty;
        public string version = string.Empty;
        public bool affectsGameplay;
        public List<V107Dependency>? dependencies = [];
    }
    private sealed class V107Mod
    {
        public V107State state;
        public V107Source modSource;
        public string path = string.Empty;
        public V107Manifest manifest = new();
    }

    private enum V108State { None, Loaded }
    private enum V108Source { ModsDirectory, SteamWorkshop }
    private sealed class V108Dependency { public string Id { get; init; } = string.Empty; }
    private sealed class V108Manifest
    {
        public string Id { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public bool AffectsGameplay { get; init; }
        public List<V108Dependency> Dependencies { get; init; } = [];
    }
    private sealed class V108Mod
    {
        public V108State State { get; init; }
        public V108Source Source { get; init; }
        public string Path { get; init; } = string.Empty;
        public V108Manifest Manifest { get; init; } = new();
    }

    private enum V109State { None, Loaded }
    private enum V109Source { ModsDirectory, SteamWorkshop }
    private sealed class V109Mod
    {
        public V109State state;
        public V109Source modSource;
        public string path = string.Empty;
        public ulong? workshopId;
        public V107Manifest manifest = new();
    }

    private sealed class AmbiguousMod
    {
        public object manifest = new();
        public object Manifest { get; } = new();
    }
}
