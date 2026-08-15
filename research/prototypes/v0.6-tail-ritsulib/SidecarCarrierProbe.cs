using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using STS2RitsuLib.Networking.Sidecar;

namespace Sts2TailPrototype;

internal sealed record SidecarCarrierFrame(string TicketId, string FlowNonce, ulong Sequence, string VanillaKind, byte[] Container);

internal static class SidecarCarrierProbe
{
    internal const string ModuleId = "sts2-lan-connect.probe";
    internal const string MessageKey = "tail-v1-carrier";

    internal static readonly RitsuLibSidecarMessageDescriptor<SidecarCarrierFrame> Descriptor = new(
        ModuleId,
        MessageKey,
        static frame => JsonSerializer.SerializeToUtf8Bytes(frame),
        static payload => JsonSerializer.Deserialize<SidecarCarrierFrame>(payload) ?? throw new InvalidDataException("Sidecar carrier frame was empty."),
        RitsuLibSidecarDeliverySemantics.StableSync,
        Required: true);

    internal static ulong Register() => RitsuLibSidecarTypedMessageRegistry.Register(Descriptor);

    internal static IDisposable Subscribe(Action<RitsuLibSidecarTypedDispatchContext<SidecarCarrierFrame>> handler) =>
        RitsuLibSidecarTypedMessageRegistry.Subscribe(Descriptor, handler);

    internal static void BootstrapTrustedTicket(INetGameService service, ulong peerNetId, string ticketId, string flowNonce)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(flowNonce);
        RitsuLibSidecarSessionManager.ObserveNetService(service);
        RitsuLibSidecarSessionManager.SetPeerReachabilityHint(peerNetId, RitsuLibSidecarPeerReachability.Supported);
        if (!RitsuLibSidecarSessionManager.CanSendToPeer(peerNetId))
        {
            throw new InvalidOperationException("Trusted-ticket sidecar hint did not establish reachability.");
        }
    }

    internal static void ClearHint(ulong peerNetId) =>
        RitsuLibSidecarSessionManager.SetPeerReachabilityHint(peerNetId, RitsuLibSidecarPeerReachability.Unknown);

    internal static bool SendToHost(INetGameService service, SidecarCarrierFrame frame) =>
        RitsuLibSidecarTypedMessageRegistry.SendToHost(service, Descriptor, frame);

    internal static bool SendToPeer(INetGameService service, ulong peerNetId, SidecarCarrierFrame frame) =>
        RitsuLibSidecarTypedMessageRegistry.SendToPeer(service, peerNetId, Descriptor, frame);

    internal static async Task<SidecarCarrierResult> RunExternalTwoProcessProbeAsync()
    {
        string? command = Environment.GetEnvironmentVariable("STS2_RITSU_SIDECAR_PROBE_COMMAND");
        string? evidencePath = Environment.GetEnvironmentVariable("STS2_RITSU_SIDECAR_PROBE_EVIDENCE");
        if (string.IsNullOrWhiteSpace(command) || !File.Exists(command) || string.IsNullOrWhiteSpace(evidencePath))
        {
            throw new InvalidOperationException("BLOCKED: set executable STS2_RITSU_SIDECAR_PROBE_COMMAND and writable STS2_RITSU_SIDECAR_PROBE_EVIDENCE to launch the two real game-process probe.");
        }

        string fullEvidencePath = Path.GetFullPath(evidencePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullEvidencePath) ?? throw new InvalidOperationException("Evidence path has no directory."));
        if (File.Exists(fullEvidencePath)) File.Delete(fullEvidencePath);
        string flowNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        ProcessStartInfo startInfo = new(command)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--flow-nonce");
        startInfo.ArgumentList.Add(flowNonce);
        startInfo.ArgumentList.Add("--evidence");
        startInfo.ArgumentList.Add(fullEvidencePath);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the real sidecar probe launcher.");
        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Real sidecar probe launcher failed ({process.ExitCode}): {standardError}\n{standardOutput}");
        if (!File.Exists(fullEvidencePath)) throw new InvalidOperationException("Real sidecar probe launcher produced no evidence file.");

        SidecarEvidence? evidence = JsonSerializer.Deserialize<SidecarEvidence>(await File.ReadAllTextAsync(fullEvidencePath));
        if (evidence == null || evidence.FlowNonce != flowNonce) throw new InvalidOperationException("Real sidecar probe evidence is missing or has the wrong flow nonce.");
        if (!evidence.Container.SequenceEqual(InteropFixtures.ExpectedContainer)) throw new InvalidOperationException("Real sidecar probe inner container differs from frozen fixture.");
        return new SidecarCarrierResult(evidence.Container, evidence.TrustedTicketHintBootstrappedReachability, evidence.SidecarReachableBeforeFirstLanFlow, evidence.HandlerBlockedUntilPairValidated, evidence.VanillaBytesMatchFixture, evidence.StandaloneTailPresent, evidence.HintClearedOnTeardown, evidence.ReusedPeerIdStartsUnknown);
    }

    private sealed record SidecarEvidence(string FlowNonce, byte[] Container, bool TrustedTicketHintBootstrappedReachability, bool SidecarReachableBeforeFirstLanFlow, bool HandlerBlockedUntilPairValidated, bool VanillaBytesMatchFixture, bool StandaloneTailPresent, bool HintClearedOnTeardown, bool ReusedPeerIdStartsUnknown);
}
