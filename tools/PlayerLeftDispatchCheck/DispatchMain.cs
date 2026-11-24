using Godot;
using GdUnit4.Core.Execution.Exceptions;
using Sts2LanConnect.GdUnitTests.Protocol;
using static GdUnit4.Assertions;

public partial class DispatchMain : Node
{
    public override void _Ready()
    {
        bool assertionThrew = false;
        try
        {
            AssertThat(false).IsTrue();
        }
        catch (TestFailedException)
        {
            assertionThrew = true;
        }
        catch (Exception exception)
        {
            GD.PrintErr("P1_DISPATCH_ASSERTION_CONTROL_FAILED " + exception);
            GetTree().Quit(2);
            return;
        }
        if (!assertionThrew)
        {
            GD.PrintErr("P1_DISPATCH_ASSERTION_CONTROL_FAILED assertion did not throw");
            GetTree().Quit(2);
            return;
        }
        GD.Print("P1_DISPATCH_ASSERTION_CONTROL_PASS");

        LanConnectPlayerLeftDispatchTests suite = new();
        (string Name, Action Run)[] cases =
        [
            ("native-mode-none", suite.Installed_native_dispatch_hook_observes_host_leave_with_actual_ENet_receive_mode),
            ("buffer-before-bootstrap", suite.Native_buffer_replay_retains_transport_context_captured_before_bootstrap),
            ("spoofed-transport", () => suite.Untrusted_native_leave_is_suppressed_and_cannot_authorize_removal(999, 100, 0, 33)),
            ("spoofed-header", () => suite.Untrusted_native_leave_is_suppressed_and_cannot_authorize_removal(100, 999, 0, 33)),
            ("wrong-channel", () => suite.Untrusted_native_leave_is_suppressed_and_cannot_authorize_removal(100, 100, 1, 33)),
            ("host-removal", () => suite.Untrusted_native_leave_is_suppressed_and_cannot_authorize_removal(100, 100, 0, 100)),
            ("unknown-duplicate", suite.Unknown_and_duplicate_native_leaves_do_not_authorize_additional_removals),
            ("running-loaded-phase", suite.Running_or_loaded_lobby_leave_reaches_native_handler_without_changing_roster_authority),
            ("bad-projection", () => suite.Invalid_projection_or_carrier_does_not_consume_verified_departure(false)),
            ("bad-carrier", () => suite.Invalid_projection_or_carrier_does_not_consume_verified_departure(true)),
            ("bad-load-lifecycle", suite.Failed_load_response_keeps_start_phase_and_departure_evidence),
            ("valid-load-lifecycle", suite.Successful_real_load_response_closes_start_phase_and_clears_departure_evidence),
            ("host-observer-lifecycle", suite.Dedicated_host_observer_marks_only_active_lobby_departure_and_generic_cleanup_does_not),
            ("unbind-cleanup", suite.Binding_termination_clears_pending_departure_proof)
        ];
        int passed = 0;
        foreach ((string name, Action run) in cases)
        {
            try
            {
                run();
                GD.Print("P1_DISPATCH_PASS " + name);
                passed++;
            }
            catch (Exception exception)
            {
                GD.PrintErr("P1_DISPATCH_FAIL " + name + " " + exception);
            }
        }
        GD.Print($"P1_DISPATCH_RESULTS passed={passed} total={cases.Length}");
        GetTree().Quit(passed == cases.Length ? 0 : 1);
    }
}
