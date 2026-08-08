using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using Sts2LanConnect.Scripts;
using Xunit;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectSaveBindingMutationBoundaryTests
{
    [Fact]
    public void Safe_load_entry_performs_no_persisted_write()
    {
        AssertMethodsDoNotCall(
            nameof(LanConnectMultiplayerSaveCompatibility),
            nameof(LanConnectMultiplayerSaveCompatibility.StartLoadedRunAsLanHostAsync),
            [
                (nameof(LanConnectMultiplayerSaveRoomBinding), nameof(LanConnectMultiplayerSaveRoomBinding.PersistHostBinding)),
                (nameof(LanConnectConfig), nameof(LanConnectConfig.UpsertSaveRoomBinding)),
                (nameof(LanConnectConfig), nameof(LanConnectConfig.RemoveSaveRoomBinding))
            ]);
    }

    [Fact]
    public void Save_compatibility_outer_type_initialization_does_not_resolve_sts2_reflection_fields()
    {
        RuntimeHelpers.RunClassConstructor(typeof(LanConnectMultiplayerSaveCompatibility).TypeHandle);
    }

    [Fact]
    public void Repair_preserves_a_valid_binding_and_its_publish_decision()
    {
        using RepairProfileHarness profile = new();
        LanConnectSavedRoomBinding existing = new()
        {
            SchemaVersion = LanConnectSavedRoomBinding.CurrentSchemaVersion,
            SaveKey = "save-1",
            RoomName = "续局房间",
            HostChannel = LanConnectHostChannels.Lobby
        };
        Dictionary<string, LanConnectSavedRoomBinding> bindings = new()
        {
            [existing.SaveKey] = existing
        };
        LanConnectSaveRepairResult result = LanConnectMultiplayerSaveRepair.RepairCurrentProfile(
            profile.CreateContext(() => new LanConnectSaveRepairBindingInspection(
                true,
                existing.SaveKey,
                bindings.ContainsKey(existing.SaveKey))));

        AssertRepairEntryDoesNotRemoveBindings();

        Assert.True(result.Success);
        Assert.Contains($"已保留当前多人存档的房间绑定 {existing.SaveKey}", result.Message);
        Assert.Same(existing, bindings["save-1"]);
        Assert.Equal(
            LanConnectContinueRunPublishDecisionKind.Publish,
            LanConnectContinueRunPublishDecision.Decide(existing.HostChannel, existing.SchemaVersion));
    }

    [Fact]
    public void Repair_does_not_invent_a_binding_when_none_exists()
    {
        using RepairProfileHarness profile = new();
        Dictionary<string, LanConnectSavedRoomBinding> bindings = new();
        LanConnectSaveRepairResult result = LanConnectMultiplayerSaveRepair.RepairCurrentProfile(
            profile.CreateContext(() => new LanConnectSaveRepairBindingInspection(
                true,
                "save-2",
                bindings.ContainsKey("save-2"))));

        AssertRepairEntryDoesNotRemoveBindings();

        Assert.True(result.Success);
        Assert.Contains("当前多人存档没有已保存的房间绑定 save-2", result.Message);
        Assert.Empty(bindings);
    }

    [Fact]
    public async Task Hosted_restart_persists_exact_binding_before_returning_to_main_menu()
    {
        List<string> events = new();
        List<LanConnectRunBindingCoordinator<string>.BindingWrite> writes = new();
        LanConnectRunBindingCoordinator<string> coordinator = new(
            () => new(true, "save-3", string.Empty),
            run => run,
            _ => null,
            (_, write) =>
            {
                writes.Add(write);
                events.Add("persist");
            });

        await coordinator.ExecuteHostedRestartAsync(
            "save-3",
            "大厅续局",
            "secret",
            "custom",
            () => events.Add("after_persist"),
            () =>
            {
                events.Add("prepare");
                return Task.CompletedTask;
            },
            () =>
            {
                events.Add("return_to_main_menu");
                return Task.CompletedTask;
            });

        LanConnectRunBindingCoordinator<string>.BindingWrite write = Assert.Single(writes);
        Assert.Equal("save-3", write.SaveKey);
        Assert.Equal(LanConnectHostChannels.Lobby, write.HostChannel);
        Assert.Equal(LanConnectSavedRoomBinding.CurrentSchemaVersion, write.SchemaVersion);
        Assert.Equal(
            ["persist", "after_persist", "prepare", "return_to_main_menu"],
            events);
    }

    private static void AssertRepairEntryDoesNotRemoveBindings()
    {
        AssertMethodsDoNotCall(
            nameof(LanConnectMultiplayerSaveRepair),
            "RepairCurrentProfile",
            [(nameof(LanConnectConfig), nameof(LanConnectConfig.RemoveSaveRoomBinding))]);
    }

    private static void AssertMethodsDoNotCall(
        string sourceTypeName,
        string sourceMethodName,
        IReadOnlyList<(string TypeName, string MethodName)> forbiddenMethods)
    {
        using FileStream stream = File.OpenRead(typeof(LanConnectConfig).Assembly.Location);
        using PEReader peReader = new(stream);
        MetadataReader metadata = peReader.GetMetadataReader();
        TypeDefinition sourceType = FindType(metadata, sourceTypeName);
        MethodDefinitionHandle[] sourceMethods = sourceType.GetMethods()
            .Where(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == sourceMethodName)
            .ToArray();
        Assert.NotEmpty(sourceMethods);

        int[] forbiddenTokens = forbiddenMethods
            .SelectMany(forbidden => FindMethodTokens(metadata, forbidden.TypeName, forbidden.MethodName))
            .ToArray();
        Assert.NotEmpty(forbiddenTokens);

        foreach (MethodDefinitionHandle sourceHandle in sourceMethods)
        {
            MethodDefinition sourceMethod = metadata.GetMethodDefinition(sourceHandle);
            byte[] il = peReader.GetMethodBody(sourceMethod.RelativeVirtualAddress).GetILBytes() ?? [];
            Assert.All(forbiddenTokens, token =>
                Assert.True(
                    il.AsSpan().IndexOf(BitConverter.GetBytes(token)) < 0,
                    $"{sourceTypeName}.{sourceMethodName} calls forbidden metadata token 0x{token:x8}."));
        }
    }

    private static IEnumerable<int> FindMethodTokens(
        MetadataReader metadata,
        string typeName,
        string methodName)
    {
        TypeDefinition type = FindType(metadata, typeName);
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            if (metadata.GetString(metadata.GetMethodDefinition(methodHandle).Name) == methodName)
            {
                yield return MetadataTokens.GetToken(methodHandle);
            }
        }
    }

    private static TypeDefinition FindType(MetadataReader metadata, string typeName)
    {
        foreach (TypeDefinitionHandle typeHandle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
            if (metadata.GetString(type.Name) == typeName)
            {
                return type;
            }
        }

        throw new InvalidOperationException($"Type {typeName} was not found in the mod assembly.");
    }

    private sealed class RepairProfileHarness : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"lan-connect-repair-{Guid.NewGuid():N}");

        public RepairProfileHarness()
        {
            VanillaSaveDir = Path.Combine(_root, "vanilla", "saves");
            ModdedProfileDir = Path.Combine(_root, "modded");
            ModdedSaveDir = Path.Combine(ModdedProfileDir, "saves");
            BackupDir = Path.Combine(_root, "backup");
            Directory.CreateDirectory(VanillaSaveDir);
            File.WriteAllText(Path.Combine(VanillaSaveDir, "current_run_mp.save"), "fixture");
        }

        public string VanillaSaveDir { get; }

        public string ModdedProfileDir { get; }

        public string ModdedSaveDir { get; }

        public string BackupDir { get; }

        public LanConnectSaveRepairContext CreateContext(
            Func<LanConnectSaveRepairBindingInspection> inspectBinding) =>
            new(
                1,
                VanillaSaveDir,
                ModdedProfileDir,
                ModdedSaveDir,
                BackupDir,
                inspectBinding,
                () => new LanConnectSaveRepairValidation(true, "修复完成"),
                (_, _) => { });

        public void Dispose()
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
