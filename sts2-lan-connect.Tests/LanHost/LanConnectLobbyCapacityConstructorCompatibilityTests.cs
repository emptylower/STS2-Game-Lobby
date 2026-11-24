using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.LanHost;

public sealed class LanConnectLobbyCapacityConstructorCompatibilityTests
{
    private const string GameModeName = "MegaCrit.Sts2.Core.Runs.GameMode";
    private const string NetServiceName = "MegaCrit.Sts2.Core.Multiplayer.Game.INetGameService";
    private const string ListenerName = "MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.IStartRunLobbyListener";

    [Fact]
    public void Selects_current_constructor_with_explicit_custom_flag()
    {
        AssertSignature(typeof(CurrentLobby), typeof(TestMode), typeof(bool), typeof(TestNetService), typeof(TestListener), typeof(int));
    }

    [Fact]
    public void Falls_back_to_legacy_constructor_without_custom_flag()
    {
        AssertSignature(typeof(LegacyLobby), typeof(TestMode), typeof(TestNetService), typeof(TestListener), typeof(int));
    }

    [Fact]
    public void Prefers_current_constructor_when_both_supported_shapes_exist()
    {
        AssertSignature(typeof(BothLobby), typeof(TestMode), typeof(bool), typeof(TestNetService), typeof(TestListener), typeof(int));
    }

    [Fact]
    public void Supports_nonpublic_constructor_like_the_previous_lookup()
    {
        AssertSignature(typeof(NonPublicLobby), typeof(TestMode), typeof(TestNetService), typeof(TestListener), typeof(int));
    }

    [Fact]
    public void Does_not_select_convertible_or_additional_parameter_overloads()
    {
        Assert.Null(Resolve(typeof(UnsupportedLobby)));
    }

    [Fact]
    public void Real_game_metadata_has_an_exact_supported_constructor_and_named_postfix_arguments()
    {
        using PEReader pe = new(File.OpenRead(ResolveGameAssemblyPath()));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinition lobby = reader.TypeDefinitions.Select(reader.GetTypeDefinition).Single(type =>
            reader.GetString(type.Namespace) == "MegaCrit.Sts2.Core.Multiplayer.Game.Lobby"
            && reader.GetString(type.Name) == "StartRunLobby");
        string[] current = [GameModeName, "System.Boolean", NetServiceName, ListenerName, "System.Int32"];
        string[] legacy = [GameModeName, NetServiceName, ListenerName, "System.Int32"];
        var constructors = lobby.GetMethods().Select(reader.GetMethodDefinition)
            .Where(method => reader.GetString(method.Name) == ".ctor")
            .Select(method => new
            {
                Types = ReadParameterTypes(reader, method),
                Names = method.GetParameters().Select(reader.GetParameter)
                    .Where(parameter => parameter.SequenceNumber > 0)
                    .OrderBy(parameter => parameter.SequenceNumber)
                    .Select(parameter => reader.GetString(parameter.Name)).ToArray()
            }).ToArray();
        var selected = constructors.SingleOrDefault(constructor => constructor.Types.SequenceEqual(current))
                       ?? constructors.SingleOrDefault(constructor => constructor.Types.SequenceEqual(legacy));

        Assert.NotNull(selected);
        // The production postfix uses named Harmony injection, not a positional index.
        Assert.Equal("netService", selected.Names[selected.Types.Length == 5 ? 2 : 1]);
        Assert.Equal("maxPlayers", selected.Names[^1]);
    }

    private static ConstructorInfo? Resolve(Type lobbyType) =>
        LanConnectStartRunLobbyConstructorCompatibility.Resolve(
            lobbyType, typeof(TestMode), typeof(TestNetService), typeof(TestListener));

    private static void AssertSignature(Type lobbyType, params Type[] expected)
    {
        ConstructorInfo? constructor = Resolve(lobbyType);
        Assert.NotNull(constructor);
        Assert.Equal(expected, constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private static string[] ReadParameterTypes(MetadataReader metadata, MethodDefinition method)
    {
        BlobReader signature = metadata.GetBlobReader(method.Signature);
        Assert.False(signature.ReadSignatureHeader().IsGeneric);
        int count = signature.ReadCompressedInteger();
        Assert.Equal(SignatureTypeCode.Void, signature.ReadSignatureTypeCode());
        string[] parameters = new string[count];
        for (int index = 0; index < count; index++)
        {
            SignatureTypeCode code = signature.ReadSignatureTypeCode();
            parameters[index] = code switch
            {
                SignatureTypeCode.Boolean => "System.Boolean",
                SignatureTypeCode.Int32 => "System.Int32",
                SignatureTypeCode.TypeHandle => ReadTypeName(metadata, signature.ReadTypeHandle()),
                _ => throw new InvalidDataException($"Unexpected StartRunLobby constructor parameter kind: {code}.")
            };
        }
        return parameters;
    }

    private static string ReadTypeName(MetadataReader metadata, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            TypeDefinition type = metadata.GetTypeDefinition((TypeDefinitionHandle)handle);
            return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
        }
        TypeReference reference = metadata.GetTypeReference((TypeReferenceHandle)handle);
        return metadata.GetString(reference.Namespace) + "." + metadata.GetString(reference.Name);
    }

    private static string ResolveGameAssemblyPath()
    {
        string? dataDirectory = Environment.GetEnvironmentVariable("STS2_TEST_GAME_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string root = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "Slay the Spire 2")
                : Path.Combine(home, "Library", "Application Support", "Steam", "steamapps", "common", "Slay the Spire 2");
            string architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
                == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x86_64";
            dataDirectory = OperatingSystem.IsWindows()
                ? Path.Combine(root, "data_sts2_windows_x86_64")
                : Path.Combine(root, "SlayTheSpire2.app", "Contents", "Resources", "data_sts2_macos_" + architecture);
        }
        return Path.Combine(dataDirectory, "sts2.dll");
    }

    private enum TestMode { Standard }
    private sealed class TestNetService { }
    private sealed class TestListener { }
    private sealed class LegacyLobby
    {
        public LegacyLobby(TestMode gameMode, TestNetService netService, TestListener lobbyListener, int maxPlayers) { }
    }
    private sealed class CurrentLobby
    {
        public CurrentLobby(TestMode gameMode, bool isCustom, TestNetService netService, TestListener lobbyListener, int maxPlayers) { }
    }
    private sealed class NonPublicLobby
    {
        private NonPublicLobby(TestMode gameMode, TestNetService netService, TestListener lobbyListener, int maxPlayers) { }
    }
    private sealed class BothLobby
    {
        public BothLobby(TestMode gameMode, TestNetService netService, TestListener lobbyListener, int maxPlayers) { }
        public BothLobby(TestMode gameMode, bool isCustom, TestNetService netService, TestListener lobbyListener, int maxPlayers) { }
    }
    private sealed class UnsupportedLobby
    {
        public UnsupportedLobby(TestMode gameMode, string isCustom, TestNetService netService, TestListener lobbyListener, int maxPlayers) { }
        public UnsupportedLobby(TestMode gameMode, bool isCustom, TestNetService netService, TestListener lobbyListener, long maxPlayers) { }
        public UnsupportedLobby(TestMode gameMode, TestNetService netService, object lobbyListener, int maxPlayers) { }
        public UnsupportedLobby(TestMode gameMode, bool isCustom, TestNetService netService, TestListener lobbyListener, DateTime dailyTime, int maxPlayers) { }
    }
}
