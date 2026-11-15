using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

// Run separately for each game dependency directory; never mix versions in one process.
if (args.Length != 2)
{
    Console.Error.WriteLine("usage: JoinResponseWireCheck <mod DLL> <game data directory>");
    return 2;
}

string modPath = Path.GetFullPath(args[0]);
string gameDirectory = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string candidate = Path.Combine(gameDirectory, name.Name + ".dll");
    return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
};

try
{
    Assembly game = Assembly.LoadFrom(Path.Combine(gameDirectory, "sts2.dll"));
    Assembly mod = Assembly.LoadFrom(modPath);
    Console.WriteLine($"runtime: {RuntimeInformation.FrameworkDescription}");
    Console.WriteLine($"game: {game.Location}, MVID={game.ManifestModule.ModuleVersionId}");
    Console.WriteLine($"mod: {modPath}");
    return new WireRegression(game, mod).Run();
}
catch (Exception exception)
{
    while (exception is TargetInvocationException { InnerException: not null })
    {
        exception = exception.InnerException!;
    }

    Console.Error.WriteLine($"wire regression FAILED: {exception}");
    return 1;
}

internal sealed class WireRegression
{
    private const BindingFlags AllStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const string HarmonyOwner = "sts2_lan_connect.wire_regression";
    private readonly Assembly _mod;
    private readonly Type _response;
    private readonly Type _writer;
    private readonly Type _reader;
    private readonly Type _patches;
    private readonly MethodInfo _manualSerialize;
    private readonly MethodInfo _nativeSerialize;
    private readonly MethodInfo _nativeDeserialize;
    private readonly FieldInfo? _modeField;
    private readonly FieldInfo? _customField;
    private object? _harmony;

    internal WireRegression(Assembly game, Assembly mod)
    {
        _mod = mod;
        _response = game.GetType("MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinResponseMessage", true)!;
        _writer = game.GetType("MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter", true)!;
        _reader = game.GetType("MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader", true)!;
        _patches = mod.GetType("Sts2LanConnect.Scripts.LanConnectSerializationPatches", true)!;
        _manualSerialize = _patches.GetMethod("SerializeJoinResponseBody", AllStatic)!;
        _nativeSerialize = _response.GetMethod("Serialize")!;
        _nativeDeserialize = _response.GetMethod("Deserialize")!;
        _modeField = _response.GetField("gameMode");
        _customField = _response.GetField("isCustom");
        Check((_modeField == null) == (_customField == null), "partial mode schema");
    }

    internal int Run()
    {
        var cases = Cases().ToArray();
        Dictionary<Case, int> originalBits = [];
        int passed = 0;
        foreach (Case spec in cases)
        {
            originalBits[spec] = Verify(spec, listBits: 3);
            passed++;
        }

        // Only the production list-count-width transpilers alter the game oracle.
        // All body field ordering and decoding still come from the real game methods.
        using IDisposable lease = FreezeCompat();
        Exception? verificationFailure = null;
        try
        {
            PatchNativeListWidths();
            foreach (Case spec in cases)
            {
                int actualBits = Verify(spec, listBits: 5);
                Check(actualBits == originalBits[spec] + 2,
                    $"{spec}: compat packet changed more than its empty list count width");
                passed++;
            }
        }
        catch (Exception exception)
        {
            verificationFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                RemoveOwnPatches();
            }
            catch (Exception cleanupFailure) when (verificationFailure != null)
            {
                // Keep the original wire assertion/patch failure as the primary error.
                Console.Error.WriteLine($"patch cleanup also failed: {cleanupFailure.GetBaseException().Message}");
            }
        }

        Console.WriteLine($"wire regression OK: {passed} cases; schema={(_modeField == null ? "legacy" : "mode/custom")}; list widths=3,5");
        Console.WriteLine("coverage: native bytes/bits and native Deserialize; all modes present in the response schema; seed/dailyTime branches; ascension 0/3/20.");
        Console.WriteLine("limitation: empty player/modifier lists isolate message-body schema; this does not replace nonempty roster, message-bus, transport or paired-game integration tests.");
        return 0;
    }

    private IEnumerable<Case> Cases()
    {
        (string? Mode, bool Custom)[] modes = _modeField == null
            ? [(null, false)]
            : [("Standard", false), ("Daily", false), ("Standard", true), ("Blitz", false), ("Dread", false)];
        foreach (var mode in modes)
        foreach (string? seed in new string?[] { null, "", "JOIN-SEED-123" })
        foreach (bool daily in new[] { false, true })
        foreach (int ascension in new[] { 0, 3, 20 })
        {
            yield return new Case(mode.Mode, mode.Custom, seed, daily, ascension);
        }
    }

    private object NewResponse(Case spec)
    {
        object value = Activator.CreateInstance(_response)!;
        foreach (string fieldName in new[] { "playersInLobby", "modifiers" })
        {
            FieldInfo field = _response.GetField(fieldName)!;
            field.SetValue(value, Activator.CreateInstance(field.FieldType));
        }

        _response.GetField("seed")!.SetValue(value, spec.Seed);
        _response.GetField("ascension")!.SetValue(value, spec.Ascension);
        if (spec.Daily)
        {
            FieldInfo daily = _response.GetField("dailyTime")!;
            Type dailyType = Nullable.GetUnderlyingType(daily.FieldType)!;
            object time = Activator.CreateInstance(dailyType)!;
            dailyType.GetField("serverTime")!.SetValue(time, DateTimeOffset.FromUnixTimeSeconds(1794614400));
            dailyType.GetField("localReceivedTime")!.SetValue(time, DateTimeOffset.FromUnixTimeSeconds(1794614402));
            daily.SetValue(value, time);
        }

        if (_modeField != null)
        {
            _modeField.SetValue(value, Enum.Parse(_modeField.FieldType, spec.Mode!));
            _customField!.SetValue(value, spec.Custom);
        }

        return value;
    }

    private int Verify(Case spec, int listBits)
    {
        object expected = NewResponse(spec);
        object nativeWriter = Activator.CreateInstance(_writer)!;
        object manualWriter = Activator.CreateInstance(_writer)!;
        _nativeSerialize.Invoke(expected, [nativeWriter]);
        _manualSerialize.Invoke(null, [manualWriter, expected, listBits]);
        int nativeBits = Bits(nativeWriter);
        Check(Bits(manualWriter) == nativeBits, $"{spec} width={listBits}: bit count mismatch");
        byte[] nativeBytes = Bytes(nativeWriter, nativeBits);
        byte[] manualBytes = Bytes(manualWriter, nativeBits);
        Check(nativeBytes.SequenceEqual(manualBytes), $"{spec} width={listBits}: native/manual bytes differ");

        object reader = Activator.CreateInstance(_reader)!;
        _reader.GetMethod("Reset")!.Invoke(reader, [manualBytes]);
        object decoded = Activator.CreateInstance(_response)!;
        _nativeDeserialize.Invoke(decoded, [reader]);
        Check((int)_reader.GetProperty("BitPosition")!.GetValue(reader)! == nativeBits,
            $"{spec} width={listBits}: native reader did not consume the exact body");
        foreach (FieldInfo field in _response.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            object? wanted = field.GetValue(expected);
            object? actual = field.GetValue(decoded);
            if (wanted is ICollection collection)
            {
                Check(collection.Count == 0 && actual is ICollection { Count: 0 },
                    $"{spec} width={listBits}: {field.Name} count mismatch");
            }
            else
            {
                Check(Equals(wanted, actual), $"{spec} width={listBits}: {field.Name} mismatch ({wanted} != {actual})");
            }
        }

        return nativeBits;
    }

    private IDisposable FreezeCompat()
    {
        Type selectionType = _mod.GetType("Sts2LanConnect.Scripts.LanConnectProtocolSelection", true)!;
        object selection = selectionType.GetMethod("CreateLocalCompat", AllStatic)!.Invoke(null, [8, "wire-regression", null])!;
        Type stateType = _mod.GetType("Sts2LanConnect.Scripts.LanConnectSessionProtocolState", true)!;
        object state = stateType.GetProperty("Shared", AllStatic)!.GetValue(null)!;
        return (IDisposable)stateType.GetMethod("FreezeHost")!.Invoke(state, [selection, "wire-regression"])!;
    }

    private void PatchNativeListWidths()
    {
        // Production transpilers have injectable sinks so this console test never
        // enters Godot's native GD logging or starts a game instance.
        Type utils = _mod.GetType("Sts2LanConnect.Scripts.LanConnectTranspilerUtils", true)!;
        foreach (string name in new[] { "LogInfoSink", "LogWarnSink" })
        {
            utils.GetField(name, AllStatic)!.SetValue(null, (Action<string>)Console.WriteLine);
        }

        Assembly harmonyAssembly = Assembly.Load("0Harmony");
        Type harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", true)!;
        Type harmonyMethodType = harmonyAssembly.GetType("HarmonyLib.HarmonyMethod", true)!;
        _harmony = Activator.CreateInstance(harmonyType, HarmonyOwner)!;
        MethodInfo patch = harmonyType.GetMethods().Single(method => method.Name == "Patch" && method.GetParameters().Length == 5);
        foreach (var target in new[]
        {
            (_nativeSerialize, "TranspileJoinResponseSerialize"),
            (_nativeDeserialize, "TranspileJoinResponseDeserialize")
        })
        {
            MethodInfo transpiler = _patches.GetMethod(target.Item2, AllStatic)!;
            object hook = Activator.CreateInstance(harmonyMethodType, transpiler)!;
            patch.Invoke(_harmony, [target.Item1, null, null, hook, null]);
        }
    }

    private void RemoveOwnPatches()
    {
        if (_harmony == null)
        {
            return;
        }

        Type harmonyType = _harmony.GetType();
        MethodInfo? unpatchAll = harmonyType.GetMethod("UnpatchAll",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static,
            binder: null, types: [typeof(string)], modifiers: null);
        if (unpatchAll != null)
        {
            unpatchAll.Invoke(unpatchAll.IsStatic ? null : _harmony, [HarmonyOwner]);
            return;
        }

        MethodInfo unpatchSelf = harmonyType.GetMethod("UnpatchSelf", Type.EmptyTypes)
            ?? throw new MissingMethodException(harmonyType.FullName, "UnpatchAll(string)/UnpatchSelf()");
        unpatchSelf.Invoke(_harmony, null);
    }

    private int Bits(object writer) => (int)_writer.GetProperty("BitPosition")!.GetValue(writer)!;
    private byte[] Bytes(object writer, int bits) =>
        ((byte[])_writer.GetProperty("Buffer")!.GetValue(writer)!).AsSpan(0, (bits + 7) / 8).ToArray();

    private static void Check(bool valid, string message)
    {
        if (!valid)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record Case(string? Mode, bool Custom, string? Seed, bool Daily, int Ascension);
}
