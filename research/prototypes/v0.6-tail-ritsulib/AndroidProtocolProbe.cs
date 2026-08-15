using System;
using System.IO;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2LanV06Probe;

[ModInitializer(nameof(Init))]
public static class AndroidProtocolProbe
{
    private const string Prefix = "STS2_LAN_V06_ANDROID_PROBE ";
    private const string FixtureHash = "cfc9097350801026775fd333fb19c6758becbffd4142f58bab0884f4231f5cfa";
    private static readonly byte[] FrozenContainer = Convert.FromHexString("5354534c414e303101000000001c00010001096c616e2e70726f62650001010000000101");

    public static void Init()
    {
        string phase = "unknown";
        try
        {
            string config = File.ReadAllText(Path.Combine(ProbeDirectory, "sts2_lan_v06_probe_runtime.json"));
            phase = Value(config, "mode") ?? throw new InvalidDataException("Runtime config has no mode.");
            string flowNonce = Value(config, "flowNonce") ?? throw new InvalidDataException("Runtime config has no flowNonce.");
            if (phase == "standalone") { EmitStandalone(flowNonce); return; }
            if (phase == "sidecar")
            {
                string evidencePath = Value(config, "sidecarEvidencePath") ?? throw new InvalidDataException("Sidecar mode has no evidence path.");
                if (!File.Exists(evidencePath)) throw new InvalidOperationException("BLOCKED: paired real sidecar evidence is unavailable.");
                EmitRaw(File.ReadAllText(evidencePath));
                return;
            }
            throw new InvalidDataException($"Unknown mode '{phase}'.");
        }
        catch (Exception ex)
        {
            EmitRaw("{\"phase\":\"" + Escape(phase) + "\",\"passed\":false,\"exceptionType\":\"" + Escape(ex.GetType().FullName ?? ex.GetType().Name) + "\",\"exceptionMessage\":\"" + Escape(ex.Message) + "\",\"innerExceptionType\":" + Nullable(ex.InnerException?.GetType().FullName) + "}");
        }
    }

    private static void EmitStandalone(string flowNonce)
    {
        PacketWriter writer = new();
        writer.WriteBool(true); writer.WriteBool(false); writer.WriteBool(true);
        long vanillaEnd = writer.BitPosition;
        while (writer.BitPosition % 8 != 0) writer.WriteBool(false);
        long start = writer.BitPosition;
        foreach (byte value in FrozenContainer) writer.WriteByte(value, 8);
        long end = writer.BitPosition;
        byte[] wire = writer.Buffer.AsSpan(0, checked((int)((end + 7) / 8))).ToArray();
        PacketReader reader = new();
        reader.Reset(wire);
        _ = reader.ReadBool(); _ = reader.ReadBool(); _ = reader.ReadBool();
        bool zeroPadding = true;
        while (reader.BitPosition % 8 != 0) zeroPadding &= !reader.ReadBool();
        byte[] decoded = new byte[FrozenContainer.Length];
        for (int i = 0; i < decoded.Length; i++) decoded[i] = reader.ReadByte(8);
        if (!decoded.AsSpan().SequenceEqual(FrozenContainer)) throw new InvalidDataException("Standalone container drifted.");
        EmitRaw("{\"phase\":\"standalone\",\"carrier\":\"standalone_tail_v1\",\"flowNonce\":\"" + Escape(flowNonce) + "\",\"ritsuPresent\":false,\"passed\":true,\"sts2Version\":\"unknown\",\"containerSha256\":\"" + FixtureHash + "\",\"containerLength\":36,\"vanillaBodyEndBit\":" + vanillaEnd + ",\"containerStartBit\":" + start + ",\"containerEndBit\":" + end + ",\"alignmentPaddingWasZero\":" + Bool(zeroPadding) + ",\"invalidProgram\":false}");
    }

    private static string ProbeDirectory => Path.GetDirectoryName(typeof(AndroidProtocolProbe).Assembly.Location) ?? AppContext.BaseDirectory;
    private static string? Value(string json, string key) { string marker = "\"" + key + "\":\""; int start = json.IndexOf(marker, StringComparison.Ordinal); if (start < 0) return null; start += marker.Length; int end = json.IndexOf('"', start); return end < 0 ? null : json[start..end]; }
    private static void EmitRaw(string json) => Log.Info(Prefix + json);
    private static string Bool(bool value) => value ? "true" : "false";
    private static string Nullable(string? value) => value == null ? "null" : "\"" + Escape(value) + "\"";
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}
