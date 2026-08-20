using System.Text;
using HarmonyLib;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectHarmonyDiagnosticScope : IDisposable
{
    private readonly Action<string> _warn;
    private readonly bool _previousDebug;
    private readonly StreamWriter? _previousWriter;
    private readonly List<string> _previousBuffer;
    private readonly bool _hadPreviousDumpTo;
    private readonly object? _previousDumpTo;
    private readonly StreamWriter? _writer;
    private readonly bool _enabled;
    private bool _disposed;

    private LanConnectHarmonyDiagnosticScope(Action<string> warn)
    {
        _warn = warn;
        _previousBuffer = [];
    }

    private LanConnectHarmonyDiagnosticScope(
        Action<string> warn,
        bool previousDebug,
        StreamWriter? previousWriter,
        List<string> previousBuffer,
        bool hadPreviousDumpTo,
        object? previousDumpTo,
        StreamWriter writer)
    {
        _warn = warn;
        _previousDebug = previousDebug;
        _previousWriter = previousWriter;
        _previousBuffer = previousBuffer;
        _hadPreviousDumpTo = hadPreviousDumpTo;
        _previousDumpTo = previousDumpTo;
        _writer = writer;
        _enabled = true;
    }

    public static LanConnectHarmonyDiagnosticScope TryEnable(string sessionDirectory, Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(warn);

        bool previousDebug = Harmony.DEBUG;
        StreamWriter? previousWriter = null;
        List<string> previousBuffer = [];
        bool hadPreviousDumpTo = false;
        object? previousDumpTo = null;
        StreamWriter? writer = null;
        bool writerInstalled = false;
        bool bufferCaptured = false;

        try
        {
            string dmdDirectory = Path.Combine(sessionDirectory, "dmd");
            Directory.CreateDirectory(dmdDirectory);
            FileStream stream = new(
                Path.Combine(sessionDirectory, "harmony.log"),
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);
            writer = new RedactingStreamWriter(stream)
            {
                AutoFlush = true
            };

            previousWriter = FileLog.LogWriter;
            previousBuffer = FileLog.GetBuffer(clear: true);
            bufferCaptured = true;
            if (!LanConnectMonoModSwitches.IsAvailable)
            {
                throw new MissingMemberException("MonoMod switch API is unavailable in the loaded Harmony assembly.");
            }
            hadPreviousDumpTo = LanConnectMonoModSwitches.TryGetValue(
                LanConnectMonoModSwitches.DmdDumpTo,
                out previousDumpTo);

            FileLog.LogWriter = writer;
            writerInstalled = true;
            Harmony.DEBUG = true;
            LanConnectMonoModSwitches.SetValue(LanConnectMonoModSwitches.DmdDumpTo, dmdDirectory);

            return new LanConnectHarmonyDiagnosticScope(
                warn,
                previousDebug,
                previousWriter,
                previousBuffer,
                hadPreviousDumpTo,
                previousDumpTo,
                writer);
        }
        catch (Exception exception)
        {
            TryRestoreAfterFailedEnable(
                previousDebug,
                previousWriter,
                previousBuffer,
                hadPreviousDumpTo,
                previousDumpTo,
                writer,
                writerInstalled,
                bufferCaptured);
            WarnSafely(warn, "harmony_scope_enable", exception);
            return new LanConnectHarmonyDiagnosticScope(warn);
        }
    }

    public void Flush()
    {
        if (!_enabled || _disposed)
        {
            return;
        }

        try
        {
            FileLog.FlushBuffer();
            _writer?.Flush();
        }
        catch (Exception exception)
        {
            WarnSafely(_warn, "harmony_scope_flush", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (!_enabled)
        {
            return;
        }

        FlushCore();
        RestoreHarmonyState();
        RestoreDumpSwitch();
        DisposeWriter();
    }

    private void FlushCore()
    {
        try
        {
            FileLog.FlushBuffer();
            _writer?.Flush();
        }
        catch (Exception exception)
        {
            WarnSafely(_warn, "harmony_scope_flush", exception);
        }
    }

    private void RestoreHarmonyState()
    {
        try
        {
            _ = FileLog.GetBuffer(clear: true);
        }
        catch (Exception exception)
        {
            WarnSafely(_warn, "harmony_buffer_clear", exception);
        }
        try
        {
            FileLog.LogWriter = _previousWriter;
        }
        catch (Exception exception)
        {
            WarnSafely(_warn, "harmony_writer_restore", exception);
        }
        try
        {
            FileLog.SetBuffer(_previousBuffer);
        }
        catch (Exception exception)
        {
            WarnSafely(_warn, "harmony_buffer_restore", exception);
        }
        try
        {
            Harmony.DEBUG = _previousDebug;
        }
        catch (Exception exception)
        {
            WarnSafely(_warn, "harmony_debug_restore", exception);
        }
    }

    private void RestoreDumpSwitch()
    {
        try
        {
            if (_hadPreviousDumpTo)
            {
                LanConnectMonoModSwitches.SetValue(LanConnectMonoModSwitches.DmdDumpTo, _previousDumpTo);
            }
            else
            {
                LanConnectMonoModSwitches.ClearValue(LanConnectMonoModSwitches.DmdDumpTo);
            }
        }
        catch (Exception exception)
        {
            WarnSafely(_warn, "dmd_dump_switch_restore", exception);
        }
    }

    private void DisposeWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (Exception exception)
        {
            WarnSafely(_warn, "harmony_writer_dispose", exception);
        }
    }

    private static void TryRestoreAfterFailedEnable(
        bool previousDebug,
        StreamWriter? previousWriter,
        List<string> previousBuffer,
        bool hadPreviousDumpTo,
        object? previousDumpTo,
        StreamWriter? writer,
        bool writerInstalled,
        bool bufferCaptured)
    {
        try
        {
            Harmony.DEBUG = previousDebug;
            if (writerInstalled)
            {
                FileLog.LogWriter = previousWriter;
            }
            if (bufferCaptured)
            {
                FileLog.SetBuffer(previousBuffer);
            }
            if (hadPreviousDumpTo)
            {
                LanConnectMonoModSwitches.SetValue(LanConnectMonoModSwitches.DmdDumpTo, previousDumpTo);
            }
            else
            {
                LanConnectMonoModSwitches.ClearValue(LanConnectMonoModSwitches.DmdDumpTo);
            }
        }
        catch
        {
        }

        try
        {
            writer?.Dispose();
        }
        catch
        {
        }
    }

    private static void WarnSafely(Action<string> warn, string operation, Exception exception)
    {
        try
        {
            LanConnectDiagnosticException description = LanConnectDiagnosticRedactor.DescribeException(exception);
            warn(
                $"sts2_lan_connect patch diagnostics warning: operation={operation} " +
                $"exception={description.Type} hresult=0x{description.HResult:X8} fingerprint={description.Fingerprint}");
        }
        catch
        {
        }
    }

    private sealed class RedactingStreamWriter(Stream stream)
        : StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
    {
        public override void WriteLine(string? value) =>
            base.WriteLine(LanConnectDiagnosticRedactor.RedactText(value));
    }
}
