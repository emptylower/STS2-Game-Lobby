namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectWireCacheCaptureCache
{
    private readonly object _sync = new();
    private readonly Func<LanConnectWireCacheSnapshot> _capture;
    private readonly Action<LanConnectWireCacheSnapshot> _logSuccess;
    private readonly Action<Exception> _logFailure;
    private LanConnectWireCacheCaptureResult? _cached;
    private bool _startupOutcomeLogged;

    internal LanConnectWireCacheCaptureCache(
        Func<LanConnectWireCacheSnapshot> capture,
        Action<LanConnectWireCacheSnapshot>? logSuccess = null,
        Action<Exception>? logFailure = null)
    {
        _capture = capture;
        _logSuccess = logSuccess ?? (static _ => { });
        _logFailure = logFailure ?? (static _ => { });
    }

    internal LanConnectWireCacheCaptureResult GetCurrentResult()
    {
        lock (_sync)
        {
            if (_cached != null)
            {
                return _cached;
            }

            try
            {
                LanConnectWireCacheSnapshot snapshot = _capture()
                    ?? throw new InvalidOperationException("Wire cache capture returned no snapshot.");
                _cached = LanConnectWireCacheCaptureResult.Available(snapshot);
                TryLog(() => _logSuccess(snapshot));
            }
            catch (Exception ex)
            {
                _cached = LanConnectWireCacheCaptureResult.Unavailable(ex);
                TryLog(() => _logFailure(ex));
            }

            return _cached;
        }
    }

    internal bool TryMarkStartupOutcomeLogged()
    {
        lock (_sync)
        {
            if (_startupOutcomeLogged)
            {
                return false;
            }

            _startupOutcomeLogged = true;
            return true;
        }
    }

    private static void TryLog(Action log)
    {
        try
        {
            log();
        }
        catch
        {
            // Diagnostics logging must not become a product failure path.
        }
    }
}
