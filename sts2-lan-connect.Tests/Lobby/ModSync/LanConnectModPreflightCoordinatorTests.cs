using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.ModSync;

public sealed class LanConnectModPreflightCoordinatorTests
{
    [Fact]
    public void Capabilities_missing_fields_resolve_to_protocol_zero_disabled()
    {
        LanConnectModSyncCapabilityState state = LanConnectModSyncCapabilities.Resolve(new LobbyProbeCapabilities());

        Assert.Equal(0, state.ProtocolVersion);
        Assert.False(state.Enabled);
        Assert.False(state.Supported);
    }

    [Fact]
    public void Capabilities_v1_enabled_resolves_to_supported()
    {
        LanConnectModSyncCapabilityState state = LanConnectModSyncCapabilities.Resolve(new LobbyProbeCapabilities
        {
            ModSyncProtocolVersion = 1,
            ModSyncEnabled = true
        });

        Assert.True(state.Supported);
    }

    [Fact]
    public async Task JoinCoordinator_preflights_before_requesting_join_ticket()
    {
        FakePorts ports = new();
        LanConnectModPreflightCoordinator sut = new(ports);

        LanConnectModPreflightJoinResult result = await sut.JoinAsync(Request());

        Assert.Equal(LanConnectModPreflightJoinOutcome.TicketIssued, result.Outcome);
        Assert.Same(ports.JoinResponse, result.JoinResponse);
        Assert.Equal(["probe", "preflight", "ticket"], ports.Calls);
    }

    [Fact]
    public async Task JoinCoordinator_falls_back_for_v0_5_0_service()
    {
        FakePorts ports = new()
        {
            Probe = new LobbyProbeResponse { Capabilities = new LobbyProbeCapabilities() }
        };
        LanConnectModPreflightCoordinator sut = new(ports);

        LanConnectModPreflightJoinResult result = await sut.JoinAsync(Request());

        Assert.Equal(LanConnectModPreflightJoinOutcome.TicketIssued, result.Outcome);
        Assert.Equal(["probe", "ticket"], ports.Calls);
    }

    [Fact]
    public async Task JoinCoordinator_falls_back_when_feature_is_disabled()
    {
        FakePorts ports = new()
        {
            Probe = SupportedProbe(enabled: false)
        };
        LanConnectModPreflightCoordinator sut = new(ports);

        await sut.JoinAsync(Request());

        Assert.Equal(["probe", "ticket"], ports.Calls);
    }

    [Fact]
    public async Task JoinCoordinator_falls_back_when_v0_5_0_host_has_no_inventory()
    {
        FakePorts ports = new()
        {
            Preflight = CompatiblePreflight() with { HostInventoryAvailable = false }
        };
        LanConnectModPreflightCoordinator sut = new(ports);

        await sut.JoinAsync(Request());

        Assert.Equal(["probe", "preflight", "ticket"], ports.Calls);
    }

    [Fact]
    public async Task JoinCoordinator_hard_blocks_game_version_mismatch_before_mod_dialog()
    {
        FakePorts ports = new();
        LanConnectModPreflightCoordinator sut = new(ports);
        LanConnectModPreflightJoinRequest request = Request() with
        {
            Room = Room("v0.108.0"),
            GameVersion = "v0.109.0"
        };

        LanConnectModPreflightJoinResult result = await sut.JoinAsync(request);

        Assert.Equal(LanConnectModPreflightJoinOutcome.GameVersionMismatch, result.Outcome);
        Assert.Contains("游戏版本不匹配", result.Message, StringComparison.Ordinal);
        Assert.Empty(ports.Calls);
    }

    [Fact]
    public async Task JoinCoordinator_hard_blocks_server_reported_game_version_mismatch()
    {
        FakePorts ports = new()
        {
            Preflight = CompatiblePreflight() with
            {
                GameVersion = new LanConnectGameVersionComparison
                {
                    Host = "v0.108.0",
                    Local = "v0.109.0",
                    ExactMatch = false
                },
                CanContinueRelaxed = false
            }
        };
        LanConnectModPreflightCoordinator sut = new(ports);

        LanConnectModPreflightJoinResult result = await sut.JoinAsync(Request());

        Assert.Equal(LanConnectModPreflightJoinOutcome.GameVersionMismatch, result.Outcome);
        Assert.Equal(["probe", "preflight"], ports.Calls);
    }

    [Fact]
    public async Task JoinCoordinator_hard_blocks_game_mismatch_before_legacy_host_inventory_fallback()
    {
        FakePorts ports = new()
        {
            Preflight = CompatiblePreflight() with
            {
                HostInventoryAvailable = false,
                GameVersion = new LanConnectGameVersionComparison
                {
                    Host = "v0.108.0",
                    Local = "v0.109.0",
                    ExactMatch = false
                },
                CanContinueRelaxed = false
            }
        };
        LanConnectModPreflightCoordinator sut = new(ports);

        LanConnectModPreflightJoinResult result = await sut.JoinAsync(Request());

        Assert.Equal(LanConnectModPreflightJoinOutcome.GameVersionMismatch, result.Outcome);
        Assert.Equal(["probe", "preflight"], ports.Calls);
    }

    [Fact]
    public async Task JoinCoordinator_allows_confirmed_relaxed_continue_for_mod_differences()
    {
        FakePorts ports = new()
        {
            Preflight = DifferencePreflight(canContinueRelaxed: true),
            Decision = LanConnectModPreflightDecision.ContinueRelaxed
        };
        LanConnectModPreflightCoordinator sut = new(ports);

        LanConnectModPreflightJoinResult result = await sut.JoinAsync(Request());

        Assert.Equal(LanConnectModPreflightJoinOutcome.TicketIssued, result.Outcome);
        Assert.Equal(["probe", "preflight", "decision", "ticket"], ports.Calls);
    }

    [Fact]
    public async Task JoinCoordinator_never_defaults_relaxed_continue_as_primary_action()
    {
        FakePorts ports = new()
        {
            Preflight = DifferencePreflight(canContinueRelaxed: true)
        };
        LanConnectModPreflightCoordinator sut = new(ports);

        LanConnectModPreflightJoinResult result = await sut.JoinAsync(Request());

        Assert.Equal(LanConnectModPreflightJoinOutcome.ModSynchronizationRequired, result.Outcome);
        Assert.Equal(["probe", "preflight", "decision"], ports.Calls);
        Assert.DoesNotContain("ticket", ports.Calls);
    }

    [Fact]
    public async Task JoinCoordinator_rejects_relaxed_decision_when_service_disallows_it()
    {
        FakePorts ports = new()
        {
            Preflight = DifferencePreflight(canContinueRelaxed: false),
            Decision = LanConnectModPreflightDecision.ContinueRelaxed
        };
        LanConnectModPreflightCoordinator sut = new(ports);

        LanConnectModPreflightJoinResult result = await sut.JoinAsync(Request());

        Assert.Equal(LanConnectModPreflightJoinOutcome.ModSynchronizationRequired, result.Outcome);
        Assert.DoesNotContain("ticket", ports.Calls);
    }

    [Fact]
    public async Task JoinCoordinator_returns_restart_scheduled_without_requesting_ticket()
    {
        FakePorts ports = new()
        {
            Preflight = DifferencePreflight(canContinueRelaxed: true),
            Decision = LanConnectModPreflightDecision.RestartScheduled
        };
        LanConnectModPreflightCoordinator sut = new(ports);

        LanConnectModPreflightJoinResult result = await sut.JoinAsync(Request());

        Assert.Equal(LanConnectModPreflightJoinOutcome.RestartScheduled, result.Outcome);
        Assert.Equal(["probe", "preflight", "decision"], ports.Calls);
        Assert.DoesNotContain("ticket", ports.Calls);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("invite-password", null)]
    [InlineData("saved-password", "76561198000000001")]
    public async Task Invite_normal_join_and_saved_run_use_the_same_preflight_coordinator(
        string? password,
        string? desiredSavePlayerNetId)
    {
        FakePorts ports = new();
        LanConnectModPreflightCoordinator sut = new(ports);

        await sut.JoinAsync(Request(password, desiredSavePlayerNetId));

        Assert.Equal(["probe", "preflight", "ticket"], ports.Calls);
        Assert.Equal(password, ports.PreflightRequest?.Password);
        Assert.Equal(password, ports.JoinRequest?.Password);
        Assert.Equal(desiredSavePlayerNetId, ports.JoinRequest?.DesiredSavePlayerNetId);
    }

    private static LanConnectModPreflightJoinRequest Request(
        string? password = null,
        string? desiredSavePlayerNetId = null) => new()
    {
        Room = Room("v0.109.0"),
        PlayerName = "Ironclad",
        Password = password,
        GameVersion = "v0.109.0",
        ModVersion = "0.5.1",
        ModList = ["gameplay.mod"],
        LocalMods =
        [
            new LobbyModDescriptor
            {
                Id = "gameplay.mod",
                Version = "1.0.0",
                Role = LanConnectModRoles.Gameplay,
                Source = LanConnectModSources.SteamWorkshop,
                WorkshopFileId = "3747497501"
            }
        ],
        DesiredSavePlayerNetId = desiredSavePlayerNetId,
        PlayerNetId = desiredSavePlayerNetId
    };

    private static LobbyRoomSummary Room(string version) => new()
    {
        RoomId = "room-1",
        RoomName = "Test Room",
        Version = version
    };

    private static LobbyProbeResponse SupportedProbe(bool enabled = true) => new()
    {
        Ok = true,
        Capabilities = new LobbyProbeCapabilities
        {
            ModSyncProtocolVersion = 1,
            ModSyncEnabled = enabled
        }
    };

    private static LobbyModPreflightResponse CompatiblePreflight() => new()
    {
        Enabled = true,
        ProtocolVersion = 1,
        HostInventoryAvailable = true,
        GameVersion = new LanConnectGameVersionComparison
        {
            Host = "v0.109.0",
            Local = "v0.109.0",
            ExactMatch = true
        },
        CanContinueRelaxed = true
    };

    private static LobbyModPreflightResponse DifferencePreflight(bool canContinueRelaxed) =>
        CompatiblePreflight() with
        {
            MissingWorkshopMods =
            [
                new LobbyModDescriptor
                {
                    Id = "missing.mod",
                    Version = "1.0.0",
                    Role = LanConnectModRoles.Gameplay,
                    Source = LanConnectModSources.SteamWorkshop,
                    WorkshopFileId = "3747497501"
                }
            ],
            CanContinueRelaxed = canContinueRelaxed
        };

    private sealed class FakePorts : ILanConnectModPreflightPorts
    {
        public List<string> Calls { get; } = [];

        public LobbyProbeResponse Probe { get; init; } = SupportedProbe();

        public LobbyModPreflightResponse Preflight { get; init; } = CompatiblePreflight();

        public LanConnectModPreflightDecision Decision { get; init; } = LanConnectModPreflightDecision.Synchronize;

        public LobbyJoinRoomResponse JoinResponse { get; } = new() { TicketId = "ticket-1" };

        public LobbyModPreflightRequest? PreflightRequest { get; private set; }

        public LobbyJoinRoomRequest? JoinRequest { get; private set; }

        public Task<LobbyProbeResponse> GetProbeAsync(CancellationToken cancellationToken)
        {
            Calls.Add("probe");
            return Task.FromResult(Probe);
        }

        public Task<LobbyModPreflightResponse> PreflightAsync(
            string roomId,
            LobbyModPreflightRequest request,
            CancellationToken cancellationToken)
        {
            Calls.Add("preflight");
            PreflightRequest = request;
            return Task.FromResult(Preflight);
        }

        public Task<LanConnectModPreflightDecision> DecideAsync(
            LobbyRoomSummary room,
            LobbyModPreflightResponse response,
            CancellationToken cancellationToken)
        {
            Calls.Add("decision");
            return Task.FromResult(Decision);
        }

        public Task<LobbyJoinRoomResponse> RequestJoinTicketAsync(
            string roomId,
            LobbyJoinRoomRequest request,
            CancellationToken cancellationToken)
        {
            Calls.Add("ticket");
            JoinRequest = request;
            return Task.FromResult(JoinResponse);
        }
    }
}
