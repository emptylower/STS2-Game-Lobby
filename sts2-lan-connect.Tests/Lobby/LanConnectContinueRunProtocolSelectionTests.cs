using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectContinueRunProtocolSelectionTests
{
    [Fact]
    public void Persisted_tail_selection_is_reused_exactly()
    {
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.4", false, false);
        LanConnectProtocolSelection selection = TailSelection(ritsuLibPresent: false);

        LanConnectCreateRoomIntent intent = LanConnectHostFlow.ResolveExistingHostPublishIntent(
            offer,
            selection,
            fallbackMaxPlayers: 4);

        Assert.Equal(LanConnectProtocolProfile.TailV1, intent.Profile);
        Assert.Equal(8, intent.MaxPlayers);
        Assert.Same(offer, intent.Offer);
    }

    [Fact]
    public void Persisted_tail_selection_wins_over_temporary_compat_host_guard()
    {
        LanConnectProtocolSelection temporaryCompat = LanConnectProtocolSelection.CreateLocalCompat(
            4,
            "0.111.0",
            "wcv1:test");
        LanConnectProtocolSelection persistedTail = TailSelection(ritsuLibPresent: true);
        LanConnectSessionProtocolSnapshot activeSnapshot = new(
            LanConnectSessionProtocolPhase.Frozen,
            LanConnectSessionProtocolRole.Host,
            "host:temporary",
            1,
            temporaryCompat);

        LanConnectProtocolSelection? required = LanConnectHostFlow.ResolveExistingHostRequiredSelection(
            activeSnapshot,
            persistedTail);

        Assert.Same(persistedTail, required);
    }

    [Fact]
    public void Official_load_host_guard_uses_persisted_tail_selection()
    {
        LanConnectProtocolSelection persistedTail = TailSelection(ritsuLibPresent: true);

        LanConnectProtocolSelection guarded = LanConnectLobbyCapacityPatches.ResolveHostGuardSelection(
            new LanConnectResolvedRoomBinding { ProtocolSelection = persistedTail },
            requestedMaxPlayers: 4,
            gameVersion: "0.111.0",
            wireCacheSignature: "wcv1:current");

        Assert.Same(persistedTail, guarded);
    }

    [Fact]
    public void Host_guard_without_saved_run_remains_compat()
    {
        LanConnectProtocolSelection guarded = LanConnectLobbyCapacityPatches.ResolveHostGuardSelection(
            savedBinding: null,
            requestedMaxPlayers: 6,
            gameVersion: "0.111.0",
            wireCacheSignature: "wcv1:current");

        Assert.Equal(LanConnectProtocolProfile.Compat4x5V1, guarded.Profile);
        Assert.Equal(6, guarded.MaxPlayers);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void Only_unbound_native_steam_saves_bypass_lan_binding_validation(
        bool isSteamPlatform,
        bool hasStoredBinding,
        bool expected)
    {
        Assert.Equal(expected, LanConnectMultiplayerSaveRoomBinding.IsUnboundNativeSteamRun(
            isSteamPlatform,
            hasStoredBinding));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void Native_steam_resume_requires_the_actual_steam_transport(
        bool steamInitialized,
        bool fastMpRequested,
        bool expected)
    {
        Assert.Equal(expected, LanConnectOfficialContinueRunPatches.CanUseNativeSteamTransport(
            steamInitialized,
            fastMpRequested));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, false)]
    public void Transport_guard_never_exempts_an_enet_resume(
        bool isSteamHost,
        bool isUnboundNativeSteamRun,
        bool hasNoSavedRun,
        bool expected)
    {
        Assert.Equal(expected, LanConnectLobbyCapacityPatches.CanBypassLanProtocolGuardForNativeSteam(
            isSteamHost,
            isUnboundNativeSteamRun,
            hasNoSavedRun));
    }

    [Fact]
    public void Missing_binding_blocks_guard_and_bound_publish_without_falling_back_to_compat()
    {
        LanConnectProtocolOffer offer = Offer();
        LanConnectProtocolSelection? restored = LanConnectMultiplayerSaveRoomBinding.TryRestoreProtocolSelection(
            binding: null,
            out LanConnectProtocolFailure? failure,
            offer);
        Assert.Null(restored);
        Assert.Equal("saved_protocol_selection_missing", failure?.Code);

        LanConnectResolvedRoomBinding resolved = new() { ProtocolSelection = restored, ProtocolFailure = failure };
        LanConnectProtocolException rejected = Assert.Throws<LanConnectProtocolException>(() =>
            LanConnectLobbyCapacityPatches.ResolveHostGuardSelection(
                resolved,
                requestedMaxPlayers: 4,
                gameVersion: "0.111.0",
                wireCacheSignature: null));
        Assert.Equal(failure, rejected.Failure);

        LanConnectProtocolFailure? publishFailure = LanConnectHostFlow.ValidateExistingHostPublishSelection(
            "save-1",
            restored,
            LanConnectSessionProtocolSnapshot.Empty);
        Assert.Equal("saved_protocol_selection_missing", publishFailure?.Code);
        Assert.Null(LanConnectMultiplayerSaveRoomBinding.ResolvePersistableHostSelection(
            LanConnectSessionProtocolSnapshot.Empty,
            frozenSelection: null,
            offer));
    }

    [Fact]
    public void Incomplete_schema_two_binding_blocks_guard_before_host_start()
    {
        LanConnectSavedRoomBinding incomplete = BindingFrom(TailSelection(false));
        incomplete.ProtocolCarrier = string.Empty;

        LanConnectProtocolSelection? restored = LanConnectMultiplayerSaveRoomBinding.TryRestoreProtocolSelection(
            incomplete,
            out LanConnectProtocolFailure? failure,
            Offer());

        Assert.Null(restored);
        Assert.Equal("saved_protocol_selection_missing", failure?.Code);
        Assert.Throws<LanConnectProtocolException>(() =>
            LanConnectLobbyCapacityPatches.ResolveHostGuardSelection(
                new LanConnectResolvedRoomBinding { ProtocolSelection = restored, ProtocolFailure = failure },
                requestedMaxPlayers: 4,
                gameVersion: "0.111.0",
                wireCacheSignature: null));
    }

    [Fact]
    public void Invalid_stored_digest_preserves_validation_failure_and_blocks_guard()
    {
        LanConnectSavedRoomBinding invalid = BindingFrom(TailSelection(false));
        invalid.CapabilityDigest = new string('0', 64);

        LanConnectProtocolSelection? restored = LanConnectMultiplayerSaveRoomBinding.TryRestoreProtocolSelection(
            invalid,
            out LanConnectProtocolFailure? failure,
            Offer());

        Assert.Null(restored);
        Assert.Equal("lan_protocol_version_mismatch", failure?.Code);
        LanConnectProtocolException rejected = Assert.Throws<LanConnectProtocolException>(() =>
            LanConnectLobbyCapacityPatches.ResolveHostGuardSelection(
                new LanConnectResolvedRoomBinding { ProtocolSelection = restored, ProtocolFailure = failure },
                requestedMaxPlayers: 4,
                gameVersion: "0.111.0",
                wireCacheSignature: null));
        Assert.Equal(failure, rejected.Failure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Pre_tail_binding_migrates_to_explicit_compat(int schemaVersion)
    {
        LanConnectSavedRoomBinding legacy = new()
        {
            SchemaVersion = schemaVersion,
            PlayerCount = 5
        };

        LanConnectProtocolSelection? restored = LanConnectMultiplayerSaveRoomBinding.TryRestoreProtocolSelection(
            legacy,
            out LanConnectProtocolFailure? failure,
            Offer(),
            legacyMaxPlayers: 4,
            legacyGameVersion: "0.111.0");

        Assert.Null(failure);
        Assert.Equal(LanConnectProtocolProfile.Compat4x5V1, restored?.Profile);
        Assert.Equal(5, restored?.MaxPlayers);
        Assert.Same(restored, LanConnectLobbyCapacityPatches.ResolveHostGuardSelection(
            new LanConnectResolvedRoomBinding { ProtocolSelection = restored },
            requestedMaxPlayers: 4,
            gameVersion: "0.111.0",
            wireCacheSignature: null));
    }

    [Fact]
    public void Legacy_schema_with_tail_fields_is_not_misread_as_compat()
    {
        LanConnectSavedRoomBinding inconsistent = BindingFrom(TailSelection(false));
        inconsistent.SchemaVersion = 1;

        Assert.Null(LanConnectMultiplayerSaveRoomBinding.TryRestoreProtocolSelection(
            inconsistent,
            out LanConnectProtocolFailure? failure,
            Offer()));
        Assert.Equal("saved_protocol_selection_missing", failure?.Code);
    }

    [Fact]
    public void Complete_schema_two_tail_and_compat_bindings_restore_exact_profiles()
    {
        LanConnectProtocolSelection tail = TailSelection(false);
        LanConnectProtocolSelection compat = LanConnectProtocolSelection.CreateLocalCompat(4, "0.111.0");

        foreach (LanConnectProtocolSelection expected in new[] { tail, compat })
        {
            LanConnectProtocolSelection? restored = LanConnectMultiplayerSaveRoomBinding.TryRestoreProtocolSelection(
                BindingFrom(expected),
                out LanConnectProtocolFailure? failure,
                Offer());

            Assert.Null(failure);
            Assert.Equal(expected, restored);
        }
    }

    [Fact]
    public void Steam_host_prefix_and_postfix_keep_the_protocol_lease()
    {
        using FileStream stream = File.OpenRead(typeof(LanConnectLobbyCapacityPatches).Assembly.Location);
        using PEReader reader = new(stream);
        MetadataReader metadata = reader.GetMetadataReader();
        TypeDefinition patchType = metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Single(type => metadata.GetString(type.Name) == nameof(LanConnectLobbyCapacityPatches));
        MethodDefinitionHandle[] methods = patchType.GetMethods().ToArray();

        AssertMethodCalls("StartSteamHostPrefix", "TryStartHostWithProtocolGuard");
        AssertMethodCalls("StartSteamHostPostfix", "TrackSteamHostStartAsync");

        void AssertMethodCalls(string sourceName, string targetName)
        {
            MethodDefinitionHandle source = methods.Single(handle =>
                metadata.GetString(metadata.GetMethodDefinition(handle).Name) == sourceName);
            MethodDefinitionHandle target = methods.Single(handle =>
                metadata.GetString(metadata.GetMethodDefinition(handle).Name) == targetName);
            MethodDefinition definition = metadata.GetMethodDefinition(source);
            byte[] il = reader.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes() ?? [];
            Assert.True(il.AsSpan().IndexOf(BitConverter.GetBytes(MetadataTokens.GetToken(target))) >= 0);
        }
    }

    [Fact]
    public void Official_resume_guards_install_before_Rmp_skips_gameplay_patches()
    {
        string root = FindRepositoryRoot();
        string startup = File.ReadAllText(Path.Combine(
            root, "sts2-lan-connect", "Scripts", "LanConnectGameplayPatches.cs"));
        string officialGuard = File.ReadAllText(Path.Combine(
            root, "sts2-lan-connect", "Scripts", "LanConnectOfficialContinueRunPatches.cs"));

        int transportGuard = startup.IndexOf("TryApplyGroup(\"HostProtocolGuard\"", StringComparison.Ordinal);
        int officialResumeGuard = startup.IndexOf("TryApplyGroup(\"OfficialContinueRun\"", StringComparison.Ordinal);
        int rmpSkip = startup.IndexOf("if (LanConnectExternalModDetection.IsRmpModLoaded)", StringComparison.Ordinal);

        Assert.True(transportGuard >= 0 && transportGuard < rmpSkip);
        Assert.True(officialResumeGuard >= 0 && officialResumeGuard < rmpSkip);
        Assert.Contains("ApplyAndroidDeferredEntry", startup, StringComparison.Ordinal);
        Assert.Contains("EnsureAndroidOfficialContinueRunGuards", officialGuard, StringComparison.Ordinal);
        Assert.Contains("nameof(StartLoadPrefix)", officialGuard, StringComparison.Ordinal);
        Assert.Contains("nameof(StartHostPrefix)", officialGuard, StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_frozen_selection_is_reused_only_for_same_host_owner()
    {
        LanConnectSessionProtocolSnapshot active = new(
            LanConnectSessionProtocolPhase.Frozen,
            LanConnectSessionProtocolRole.Host,
            "host:abc",
            1,
            TailSelection(false));

        Assert.True(LanConnectLobbyCapacityPatches.CanReuseFrozenHostSelection(active, "host:abc"));
        Assert.False(LanConnectLobbyCapacityPatches.CanReuseFrozenHostSelection(active, "host:other"));
        Assert.False(LanConnectLobbyCapacityPatches.CanReuseFrozenHostSelection(
            active with { Phase = LanConnectSessionProtocolPhase.Closing }, "host:abc"));
        Assert.False(LanConnectLobbyCapacityPatches.CanReuseFrozenHostSelection(
            active with { Role = LanConnectSessionProtocolRole.Client }, "host:abc"));
    }

    [Fact]
    public void New_lobby_host_freezes_with_net_service_owner_before_transport_start()
    {
        using FileStream stream = File.OpenRead(typeof(LanConnectHostFlow).Assembly.Location);
        using PEReader reader = new(stream);
        MetadataReader metadata = reader.GetMetadataReader();
        TypeDefinition hostFlow = metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Single(type => metadata.GetString(type.Name) == nameof(LanConnectHostFlow));
        MethodDefinitionHandle ownerMethod = hostFlow.GetMethods()
            .Single(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == "BuildHostLeaseOwner");
        TypeDefinition stateMachine = metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Single(type => metadata.GetString(type.Name).Contains("<StartLobbyHostAsync>d__", StringComparison.Ordinal));
        MethodDefinition moveNext = metadata.GetMethodDefinition(stateMachine.GetMethods()
            .Single(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == "MoveNext"));
        byte[] il = reader.GetMethodBody(moveNext.RelativeVirtualAddress).GetILBytes() ?? [];

        Assert.True(il.AsSpan().IndexOf(BitConverter.GetBytes(MetadataTokens.GetToken(ownerMethod))) >= 0);
    }

    [Fact]
    public void Binding_write_requires_frozen_host_selection_or_explicit_session_capture()
    {
        LanConnectProtocolSelection tail = TailSelection(false);
        LanConnectProtocolSelection compat = LanConnectProtocolSelection.CreateLocalCompat(4, "0.111.0");
        LanConnectSessionProtocolSnapshot activeTail = new(
            LanConnectSessionProtocolPhase.Frozen,
            LanConnectSessionProtocolRole.Host,
            "host:tail",
            1,
            tail);

        Assert.Same(tail, LanConnectMultiplayerSaveRoomBinding.ResolvePersistableHostSelection(
            activeTail, frozenSelection: null, Offer()));
        Assert.Null(LanConnectMultiplayerSaveRoomBinding.ResolvePersistableHostSelection(
            activeTail, compat, Offer()));
        Assert.Null(LanConnectMultiplayerSaveRoomBinding.ResolvePersistableHostSelection(
            LanConnectSessionProtocolSnapshot.Empty, frozenSelection: null, Offer()));
        Assert.Same(tail, LanConnectMultiplayerSaveRoomBinding.ResolvePersistableHostSelection(
            LanConnectSessionProtocolSnapshot.Empty, tail, Offer()));
    }

    [Fact]
    public void Bound_publish_requires_matching_frozen_host_even_with_valid_saved_selection()
    {
        LanConnectProtocolSelection tail = TailSelection(false);
        LanConnectSessionProtocolSnapshot activeCompat = new(
            LanConnectSessionProtocolPhase.Frozen,
            LanConnectSessionProtocolRole.Host,
            "host:compat",
            1,
            LanConnectProtocolSelection.CreateLocalCompat(4, "0.111.0"));

        Assert.Equal("protocol_selection_conflict", LanConnectHostFlow.ValidateExistingHostPublishSelection(
            "save-1", tail, activeCompat)?.Code);
        Assert.Null(LanConnectHostFlow.ValidateExistingHostPublishSelection(
            "save-1", tail, activeCompat with { Selection = tail }));
    }

    [Fact]
    public void Missing_legacy_selection_with_ritsulib_renegotiates_tail()
    {
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.4", true, true);

        LanConnectCreateRoomIntent intent = LanConnectHostFlow.ResolveExistingHostPublishIntent(
            offer,
            requiredSelection: null,
            fallbackMaxPlayers: 6);

        Assert.Equal(LanConnectProtocolProfile.TailV1, intent.Profile);
        Assert.Equal(6, intent.MaxPlayers);
    }

    [Fact]
    public void Missing_legacy_selection_without_ritsulib_renegotiates_compat()
    {
        LanConnectProtocolOffer offer = new(1, 1, "0.6.0-alpha.4", false, false);

        LanConnectCreateRoomIntent intent = LanConnectHostFlow.ResolveExistingHostPublishIntent(
            offer,
            requiredSelection: null,
            fallbackMaxPlayers: 4);

        Assert.Equal(LanConnectProtocolProfile.Compat4x5V1, intent.Profile);
        Assert.Equal(4, intent.MaxPlayers);
    }

    [Fact]
    public void Service_lowercasing_of_wire_cache_signature_is_tolerated_for_resume()
    {
        LanConnectProtocolSelection required = TailSelection(ritsuLibPresent: false) with
        {
            WireCacheSignature = "wcv1:AbC_-",
            CapabilityDigest = new string('a', 64)
        };
        LanConnectProtocolSelection server = required with
        {
            WireCacheSignature = "wcv1:abc_-",
            CapabilityDigest = new string('b', 64)
        };

        Assert.True(LanConnectHostFlow.ArePublishSelectionsEquivalent(required, server));
    }

    [Fact]
    public void Resume_still_rejects_material_protocol_changes()
    {
        LanConnectProtocolSelection required = TailSelection(ritsuLibPresent: false);

        Assert.False(LanConnectHostFlow.ArePublishSelectionsEquivalent(
            required,
            required with { MaxPlayers = 4 }));
        Assert.False(LanConnectHostFlow.ArePublishSelectionsEquivalent(
            required,
            required with { Carrier = LanConnectProtocolCarrier.LegacySidecarV1 }));
    }

    private static LanConnectProtocolSelection TailSelection(bool ritsuLibPresent)
    {
        LanConnectProtocolSelection selection = new(
            LanConnectProtocolProfile.TailV1,
            LanConnectConstants.TailLanProtocolVersion,
            LanConnectProtocolCarrier.NativeBusV1,
            "0.6.0-alpha.1",
            8,
            "0.111.0",
            null,
            ritsuLibPresent,
            string.Empty);
        return selection with { CapabilityDigest = LanConnectCapabilityDigest.Compute(selection) };
    }

    private static LanConnectProtocolOffer Offer() => new(
        LanConnectConstants.TailLanProtocolVersion,
        LanConnectConstants.TailLanProtocolVersion,
        "0.6.2",
        false,
        false);

    private static LanConnectSavedRoomBinding BindingFrom(LanConnectProtocolSelection selection) => new()
    {
        SchemaVersion = LanConnectSavedRoomBinding.CurrentSchemaVersion,
        ProtocolProfileV2 = selection.Profile.ToCanonical(),
        SelectedLanProtocolVersion = selection.SelectedLanProtocolVersion,
        ProtocolCarrier = selection.Carrier.ToWireValue(),
        ProtocolMaxPlayers = selection.MaxPlayers,
        MinimumClientVersion = selection.MinimumClientVersion,
        ProtocolGameVersion = selection.GameVersion,
        WireCacheSignatureV1 = selection.WireCacheSignature,
        RitsuLibPresent = selection.RitsuLibPresent,
        CapabilityDigest = selection.CapabilityDigest
    };

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "STS2-Game-Lobby.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
