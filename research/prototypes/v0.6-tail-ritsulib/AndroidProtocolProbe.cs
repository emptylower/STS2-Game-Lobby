using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2LanV06Probe;

[ModInitializer(nameof(Init))]
public static class AndroidProtocolProbe
{
    private const string MarkerPrefix = "STS2_LAN_V06_ANDROID_PROBE ";
    private const string TailExtensionsTypeName =
        "STS2RitsuLib.Networking.MessageExtensions.RitsuNetMessageTailExtensions";
    private const string RitsuLibAssemblyName = "STS2-RitsuLib";
    private const string RitsuLibModDirectoryName = "STS2-RitsuLib";
    private const string ProbeExtensionId = "lan.ritsu.probe";
    private const int ProbeExtensionVersion = 1;
    private const string RuntimeConfigFileName = "sts2_lan_v06_probe_runtime.json";

    private static readonly byte[] ProbeExtensionPayload = [0x42];

    public static void Init()
    {
        string phase = "unknown";
        bool invalidProgram = false;
        try
        {
            ProbeRuntimeConfig config = ProbeRuntimeConfig.Load();
            phase = config.Mode;
            RitsuObservation ritsu = RitsuObservation.Observe();
            switch (config.Mode)
            {
                case "encode":
                    RunEncode(config, ritsu);
                    return;
                case "decode":
                    RunDecode(config, ritsu);
                    return;
                default:
                    throw new InvalidDataException($"Unknown probe mode '{config.Mode}'.");
            }
        }
        catch (Exception ex)
        {
            invalidProgram = FlattenExceptions(ex).Any(inner => inner is InvalidProgramException);
            EmitFailureMarker(phase, ex, invalidProgram);
            // Initialization exits here without applying any production patch.
        }
    }

    private static void RunEncode(ProbeRuntimeConfig config, RitsuObservation ritsu)
    {
        PacketWriter writer = new();
        LanTailV1ProbeCodec.WriteProbeContainer(writer);
        long lanEndBit = writer.BitPosition;
        long ritsuStartBit = writer.BitPosition;
        if (ritsu.Present)
        {
            RitsuTailInvocation.WriteTail(writer);
        }

        int fixtureByteLength = checked((writer.BitPosition + 7) / 8);
        byte[] fixture = writer.Buffer.AsSpan(0, fixtureByteLength).ToArray();
        File.WriteAllBytes(config.FixturePath, fixture);

        byte[] lanTail = fixture.AsSpan(0, checked((int)(lanEndBit / 8))).ToArray();
        StringBuilder marker = BeginMarker("encode", ritsu.Present);
        AppendField(marker, "sts2Version", Sts2Version);
        AppendField(marker, "fixtureSha256", Sha256Hex(fixture));
        AppendField(marker, "fixtureLength", fixture.Length);
        AppendField(marker, "lanTailSha256", Sha256Hex(lanTail));
        AppendField(marker, "lanEndBit", lanEndBit);
        AppendField(marker, "ritsuStartBit", ritsuStartBit);
        AppendField(marker, "containsOpenGeneric", ritsu.ContainsOpenGeneric);
        AppendField(marker, "invalidProgram", false);
        AppendRitsuFields(marker, ritsu);
        EmitMarker(marker);
    }

    private static void RunDecode(ProbeRuntimeConfig config, RitsuObservation ritsu)
    {
        List<string> results = [];
        foreach (bool senderRitsu in new[] { false, true })
        {
            string fileName = senderRitsu ? "sender-with.bin" : "sender-without.bin";
            byte[] fixture = File.ReadAllBytes(Path.Combine(config.InputDir, fileName));

            PacketReader reader = new();
            reader.Reset(fixture);
            LanTailV1ProbeCodec.ReadProbeContainer(reader);
            long lanEndBit = reader.BitPosition;
            long ritsuStartBit = reader.BitPosition;
            byte[] lanTail = fixture.AsSpan(0, checked((int)(lanEndBit / 8))).ToArray();
            string lanTailSha256 = Sha256Hex(lanTail);

            bool ritsuDispatchOk = true;
            if (ritsu.Present)
            {
                RitsuTailInvocation.LastReceivedPayload = null;
                RitsuTailInvocation.ReadTail(reader);
                if (senderRitsu)
                {
                    ritsuDispatchOk = RitsuTailInvocation.LastReceivedPayload is { } payload &&
                                      payload.SequenceEqual(ProbeExtensionPayload);
                }
            }

            bool messageOk = ritsuDispatchOk;
            results.Add("{\"senderRitsu\":" + JsonBool(senderRitsu) +
                        ",\"messageOk\":" + JsonBool(messageOk) +
                        ",\"lanTailSha256\":\"" + lanTailSha256 + "\"" +
                        ",\"lanEndBit\":" + lanEndBit +
                        ",\"ritsuStartBit\":" + ritsuStartBit + "}");
        }

        StringBuilder marker = BeginMarker("decode", ritsu.Present);
        AppendField(marker, "sts2Version", Sts2Version);
        AppendField(marker, "containsOpenGeneric", ritsu.ContainsOpenGeneric);
        AppendField(marker, "invalidProgram", false);
        AppendRitsuFields(marker, ritsu);
        marker.Append(",\"results\":[").Append(string.Join(",", results)).Append(']');
        EmitMarker(marker);
    }

    private static string Sts2Version
    {
        get
        {
            Assembly sts2 = typeof(PacketWriter).Assembly;
            string? informational = sts2
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrWhiteSpace(informational)
                ? sts2.GetName().Version?.ToString() ?? "unknown"
                : informational;
        }
    }

    private static StringBuilder BeginMarker(string phase, bool ritsuPresent)
    {
        StringBuilder marker = new();
        marker.Append("{\"phase\":\"").Append(phase).Append('\"');
        marker.Append(",\"ritsuPresent\":").Append(JsonBool(ritsuPresent));
        marker.Append(",\"passed\":true");
        return marker;
    }

    private static void AppendRitsuFields(StringBuilder marker, RitsuObservation ritsu)
    {
        AppendNullableField(marker, "ritsuManifestId", ritsu.ManifestId);
        AppendNullableField(marker, "ritsuManifestVersion", ritsu.ManifestVersion);
        AppendNullableField(marker, "ritsuSelectedAssembly", ritsu.SelectedAssembly);
        marker.Append(",\"ritsuPatchOwners\":[");
        marker.Append(string.Join(",", ritsu.PatchOwners.Select(owner => "\"" + JsonEscape(owner) + "\"")));
        marker.Append(']');
        AppendField(marker, "ritsuHarmonyOwnerTargetCount", ritsu.PatchOwners.Count);
    }

    private static void AppendField(StringBuilder marker, string name, string value) =>
        marker.Append(",\"").Append(name).Append("\":\"").Append(JsonEscape(value)).Append('\"');

    private static void AppendNullableField(StringBuilder marker, string name, string? value)
    {
        if (value == null)
        {
            marker.Append(",\"").Append(name).Append("\":null");
            return;
        }

        AppendField(marker, name, value);
    }

    private static void AppendField(StringBuilder marker, string name, long value) =>
        marker.Append(",\"").Append(name).Append("\":").Append(value);

    private static void AppendField(StringBuilder marker, string name, bool value) =>
        marker.Append(",\"").Append(name).Append("\":").Append(JsonBool(value));

    private static void EmitMarker(StringBuilder marker)
    {
        marker.Append('}');
        Log.Info(MarkerPrefix + marker);
    }

    private static void EmitFailureMarker(string phase, Exception exception, bool invalidProgram)
    {
        bool ritsuPresent = false;
        try
        {
            ritsuPresent = RitsuObservation.IsTailApiLoaded();
        }
        catch
        {
            // Best effort only; the failure marker must still go out.
        }

        StringBuilder marker = new();
        marker.Append("{\"phase\":\"").Append(JsonEscape(phase)).Append('\"');
        marker.Append(",\"ritsuPresent\":").Append(JsonBool(ritsuPresent));
        marker.Append(",\"passed\":false");
        AppendField(marker, "sts2Version", Sts2Version);
        AppendField(marker, "exceptionType", exception.GetType().FullName ?? exception.GetType().Name);
        AppendField(marker, "exceptionMessage", exception.Message);
        AppendNullableField(marker, "innerExceptionType", exception.InnerException?.GetType().FullName);
        AppendField(marker, "containsOpenGeneric", false);
        AppendField(marker, "invalidProgram", invalidProgram);
        marker.Append(",\"ritsuPatchOwners\":[]");
        EmitMarker(marker);
    }

    private static IEnumerable<Exception> FlattenExceptions(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions.SelectMany(FlattenExceptions))
                {
                    yield return inner;
                }
            }
        }
    }

    private static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static string JsonBool(bool value) => value ? "true" : "false";

    private static string JsonEscape(string value)
    {
        StringBuilder escaped = new(value.Length);
        foreach (char character in value)
        {
            switch (character)
            {
                case '\\': escaped.Append("\\\\"); break;
                case '"': escaped.Append("\\\""); break;
                case '\n': escaped.Append("\\n"); break;
                case '\r': escaped.Append("\\r"); break;
                case '\t': escaped.Append("\\t"); break;
                default:
                    if (char.IsControl(character))
                    {
                        escaped.Append("\\u").Append(((int)character).ToString("x4"));
                    }
                    else
                    {
                        escaped.Append(character);
                    }

                    break;
            }
        }

        return escaped.ToString();
    }

    private sealed record ProbeRuntimeConfig(string Mode, string FixturePath, string InputDir)
    {
        internal static ProbeRuntimeConfig Load()
        {
            string probeDirectory = ResolveProbeDirectory();
            string configPath = Path.Combine(probeDirectory, RuntimeConfigFileName);
            string json = File.ReadAllText(configPath);
            string mode = ExtractJsonString(json, "mode")
                ?? throw new InvalidDataException($"Runtime config missing 'mode': {configPath}");
            string fixturePath = ExtractJsonString(json, "fixturePath")
                ?? throw new InvalidDataException($"Runtime config missing 'fixturePath': {configPath}");
            string inputDir = ExtractJsonString(json, "inputDir")
                ?? throw new InvalidDataException($"Runtime config missing 'inputDir': {configPath}");
            return new ProbeRuntimeConfig(mode, fixturePath, inputDir);
        }

        internal static string ResolveProbeDirectory()
        {
            string? assemblyLocation = typeof(AndroidProtocolProbe).Assembly.Location;
            string? assemblyDirectory = string.IsNullOrWhiteSpace(assemblyLocation)
                ? null
                : Path.GetDirectoryName(assemblyLocation);
            if (!string.IsNullOrWhiteSpace(assemblyDirectory) && Directory.Exists(assemblyDirectory))
            {
                return assemblyDirectory;
            }

            return AppContext.BaseDirectory;
        }

        internal static string? ExtractJsonString(string json, string key)
        {
            string needle = "\"" + key + "\":\"";
            int start = json.IndexOf(needle, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += needle.Length;
            int end = json.IndexOf('"', start);
            return end < 0 ? null : json[start..end];
        }
    }

    private sealed record RitsuObservation(
        bool Present,
        string? ManifestId,
        string? ManifestVersion,
        string? SelectedAssembly,
        IReadOnlyList<string> PatchOwners,
        bool ContainsOpenGeneric)
    {
        internal static RitsuObservation Observe()
        {
            Assembly? ritsuAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == RitsuLibAssemblyName);
            bool present = ritsuAssembly != null && IsTailApiLoaded();

            string probeDirectory = ProbeRuntimeConfig.ResolveProbeDirectory();
            string ritsuModDirectory = Path.Combine(probeDirectory, RitsuLibModDirectoryName);
            string? manifestId = null;
            string? manifestVersion = null;
            string manifestPath = Path.Combine(ritsuModDirectory, "mod_manifest.json");
            if (File.Exists(manifestPath))
            {
                string manifestJson = File.ReadAllText(manifestPath);
                manifestId = ProbeRuntimeConfig.ExtractJsonString(manifestJson, "id");
                manifestVersion = ProbeRuntimeConfig.ExtractJsonString(manifestJson, "version");
            }

            string? selectedAssembly = ResolveSelectedAssembly(ritsuAssembly, ritsuModDirectory);
            (List<string> patchOwners, bool containsOpenGeneric) = CollectPatchOwners(ritsuAssembly);
            return new RitsuObservation(
                present,
                manifestId,
                manifestVersion,
                selectedAssembly,
                patchOwners,
                containsOpenGeneric);
        }

        internal static bool IsTailApiLoaded() => AccessTools.TypeByName(TailExtensionsTypeName) != null;

        private static string? ResolveSelectedAssembly(Assembly? ritsuAssembly, string ritsuModDirectory)
        {
            try
            {
                string? location = ritsuAssembly?.Location;
                if (!string.IsNullOrWhiteSpace(location))
                {
                    string fullRoot = Path.GetFullPath(ritsuModDirectory);
                    string fullLocation = Path.GetFullPath(location);
                    if (fullLocation.StartsWith(fullRoot, StringComparison.Ordinal))
                    {
                        return Path.GetRelativePath(fullRoot, fullLocation).Replace('\\', '/');
                    }
                }

                if (Directory.Exists(ritsuModDirectory))
                {
                    string[] candidates = Directory
                        .GetFiles(ritsuModDirectory, RitsuLibAssemblyName + ".dll", SearchOption.AllDirectories)
                        .OrderBy(path => path, StringComparer.Ordinal)
                        .ToArray();
                    if (candidates.Length > 0)
                    {
                        return Path.GetRelativePath(ritsuModDirectory, candidates[0]).Replace('\\', '/');
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return null;
            }

            return null;
        }

        private static (List<string> Owners, bool ContainsOpenGeneric) CollectPatchOwners(Assembly? ritsuAssembly)
        {
            SortedSet<string> owners = new(StringComparer.Ordinal);
            bool containsOpenGeneric = false;
            if (ritsuAssembly == null)
            {
                return ([], false);
            }

            foreach (MethodBase target in Harmony.GetAllPatchedMethods())
            {
                HarmonyLib.Patches? info = Harmony.GetPatchInfo(target);
                if (info == null)
                {
                    continue;
                }

                foreach (Patch patch in info.Prefixes
                    .Concat(info.Postfixes)
                    .Concat(info.Transpilers)
                    .Concat(info.Finalizers))
                {
                    if (patch.PatchMethod.DeclaringType?.Assembly != ritsuAssembly)
                    {
                        continue;
                    }

                    bool isGenericMethod = target is MethodInfo targetMethod && targetMethod.IsGenericMethod;
                    bool isGenericMethodDefinition = target is MethodInfo targetMethodDefinition &&
                                                     targetMethodDefinition.IsGenericMethodDefinition;
                    bool hasGenericParameters = target.ContainsGenericParameters ||
                                                patch.PatchMethod.ContainsGenericParameters;
                    owners.Add(
                        patch.owner + " -> " + target.FullDescription() +
                        " [IsGenericMethod=" + isGenericMethod +
                        ", IsGenericMethodDefinition=" + isGenericMethodDefinition +
                        ", ContainsGenericParameters=" + hasGenericParameters + "]");
                    if (isGenericMethod || isGenericMethodDefinition)
                    {
                        if (hasGenericParameters || isGenericMethodDefinition)
                        {
                            containsOpenGeneric = true;
                        }
                    }

                    if (hasGenericParameters)
                    {
                        containsOpenGeneric = true;
                    }
                }
            }

            return ([.. owners], containsOpenGeneric);
        }
    }

    public sealed class ProbeMessage;

    private static class RitsuTailInvocation
    {
        internal static byte[]? LastReceivedPayload;

        internal static void WriteTail(PacketWriter writer)
        {
            Type extensionsType = RegisterProbeExtension();
            MethodInfo write = SingleGenericPublicStatic(extensionsType, "Write");
            write.MakeGenericMethod(typeof(ProbeMessage)).Invoke(null, [writer, new ProbeMessage()]);
        }

        internal static void ReadTail(PacketReader reader)
        {
            Type extensionsType = RegisterProbeExtension();
            MethodInfo read = SingleGenericPublicStatic(extensionsType, "Read");
            read.MakeGenericMethod(typeof(ProbeMessage)).Invoke(null, [reader]);
        }

        private static Type RegisterProbeExtension()
        {
            Type extensionsType = AccessTools.TypeByName(TailExtensionsTypeName)
                ?? throw new TypeLoadException($"RitsuLib tail API type is unavailable: {TailExtensionsTypeName}");
            MethodInfo registerBytes = SingleGenericPublicStatic(extensionsType, "RegisterBytes");
            Func<ProbeMessage, byte[]?> writePayload = static _ => ProbeExtensionPayload;
            Action<int, ReadOnlyMemory<byte>> readPayload = static (_, payload) =>
                LastReceivedPayload = payload.ToArray();
            registerBytes.MakeGenericMethod(typeof(ProbeMessage)).Invoke(
                null,
                [ProbeExtensionId, ProbeExtensionVersion, writePayload, readPayload]);
            return extensionsType;
        }

        private static MethodInfo SingleGenericPublicStatic(Type declaringType, string name)
        {
            return declaringType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == name && method.IsGenericMethodDefinition);
        }
    }

    private static class LanTailV1ProbeCodec
    {
        private const int ByteBits = 8;
        private const byte ContainerVersion = 1;
        private const byte ContainerFlags = 0;
        private const ushort ProbeSessionProtocolVersion = 1;
        private const ushort ProbeEntryVersion = 1;
        private const byte ProbeEntryFlags = 1;
        private static readonly byte[] Magic = "STSLAN01"u8.ToArray();
        private static readonly byte[] ProbeEntryId = "lan.probe"u8.ToArray();
        private static readonly byte[] ProbePayload = [0x01];

        internal static void WriteProbeContainer(PacketWriter writer)
        {
            while (writer.BitPosition % ByteBits != 0)
            {
                writer.WriteBool(false);
            }

            foreach (byte value in Magic.Concat(BuildContainerBody()))
            {
                writer.WriteByte(value, ByteBits);
            }
        }

        internal static void ReadProbeContainer(PacketReader reader)
        {
            while (reader.BitPosition % ByteBits != 0)
            {
                reader.ReadBool();
            }

            long containerStartBit = reader.BitPosition;
            foreach (byte expected in Magic)
            {
                if (reader.ReadByte(ByteBits) != expected)
                {
                    throw new InvalidDataException("LAN tail magic mismatch.");
                }
            }

            if (reader.ReadByte(ByteBits) != ContainerVersion)
            {
                throw new InvalidDataException("Unsupported LAN tail container version.");
            }

            if (reader.ReadByte(ByteBits) != ContainerFlags)
            {
                throw new InvalidDataException("Unsupported LAN tail container flags.");
            }

            uint containerByteLength = ReadUInt32BigEndian(reader);
            long containerEndBit = containerStartBit + ((long)Magic.Length + containerByteLength) * ByteBits;
            if (containerEndBit > (long)reader.Buffer.Length * ByteBits)
            {
                throw new InvalidDataException("LAN tail container exceeds the packet buffer.");
            }

            if (ReadUInt16BigEndian(reader) != ProbeSessionProtocolVersion)
            {
                throw new InvalidDataException("Unsupported LAN session protocol.");
            }

            ushort entryCount = ReadUInt16BigEndian(reader);
            HashSet<string> seenIds = new(StringComparer.Ordinal);
            for (int index = 0; index < entryCount; index++)
            {
                byte idByteLength = reader.ReadByte(ByteBits);
                byte[] idBytes = new byte[idByteLength];
                reader.ReadBytes(idBytes, idByteLength);
                string id = Encoding.UTF8.GetString(idBytes);
                if (!seenIds.Add(id))
                {
                    throw new InvalidDataException($"Duplicate LAN tail entry id '{id}'.");
                }

                ushort entryVersion = ReadUInt16BigEndian(reader);
                byte entryFlags = reader.ReadByte(ByteBits);
                if ((entryFlags & ~1) != 0)
                {
                    throw new InvalidDataException($"Unknown LAN tail entry flags {entryFlags} for '{id}'.");
                }

                uint payloadByteLength = ReadUInt32BigEndian(reader);
                if (payloadByteLength > int.MaxValue)
                {
                    throw new InvalidDataException("LAN tail payload length is out of range.");
                }

                byte[] payload = new byte[payloadByteLength];
                reader.ReadBytes(payload, (int)payloadByteLength);
                if (id == "lan.probe" && entryVersion != ProbeEntryVersion)
                {
                    throw new InvalidDataException($"Unsupported lan.probe entry version {entryVersion}.");
                }
            }

            if (reader.BitPosition != containerEndBit)
            {
                throw new InvalidDataException(
                    $"LAN tail container was not consumed exactly: cursor={reader.BitPosition}, expected={containerEndBit}.");
            }
        }

        private static byte[] BuildContainerBody()
        {
            List<byte> body = [ContainerVersion, ContainerFlags];
            uint containerByteLength =
                (uint)(1 + 1 + 4 + 2 + 2 + 1 + ProbeEntryId.Length + 2 + 1 + 4 + ProbePayload.Length);
            AppendUInt32BigEndian(body, containerByteLength);
            AppendUInt16BigEndian(body, ProbeSessionProtocolVersion);
            AppendUInt16BigEndian(body, 1);
            body.Add((byte)ProbeEntryId.Length);
            body.AddRange(ProbeEntryId);
            AppendUInt16BigEndian(body, ProbeEntryVersion);
            body.Add(ProbeEntryFlags);
            AppendUInt32BigEndian(body, (uint)ProbePayload.Length);
            body.AddRange(ProbePayload);
            if (body.Count != containerByteLength)
            {
                throw new InvalidOperationException("LAN tail body length drifted from its declared length.");
            }

            return [.. body];
        }

        private static void AppendUInt16BigEndian(List<byte> body, ushort value)
        {
            body.Add((byte)(value >> ByteBits));
            body.Add((byte)(value & 0xFF));
        }

        private static void AppendUInt32BigEndian(List<byte> body, uint value)
        {
            body.Add((byte)(value >> 24));
            body.Add((byte)((value >> 16) & 0xFF));
            body.Add((byte)((value >> ByteBits) & 0xFF));
            body.Add((byte)(value & 0xFF));
        }

        private static ushort ReadUInt16BigEndian(PacketReader reader) =>
            (ushort)((reader.ReadByte(ByteBits) << ByteBits) | reader.ReadByte(ByteBits));

        private static uint ReadUInt32BigEndian(PacketReader reader) =>
            ((uint)reader.ReadByte(ByteBits) << 24) |
            ((uint)reader.ReadByte(ByteBits) << 16) |
            ((uint)reader.ReadByte(ByteBits) << ByteBits) |
            reader.ReadByte(ByteBits);
    }
}
