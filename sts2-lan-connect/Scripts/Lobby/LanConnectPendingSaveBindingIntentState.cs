using System;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectPendingSaveBindingIntentState
{
    private BindingIntent? _current;

    public BindingIntent Capture(string roomName, string? password, string gameMode, string? saveKey)
    {
        BindingIntent intent = new(roomName, password, gameMode, saveKey);
        _current = intent;
        return intent;
    }

    public bool TryGet(out BindingIntent intent)
    {
        intent = _current!;
        return intent != null;
    }

    public bool Complete(BindingIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!ReferenceEquals(_current, intent))
        {
            return false;
        }

        _current = null;
        return true;
    }

    public void PreserveAcrossHostedSessionTeardown()
    {
        // Intentionally retained until a save consumes it or a newer hosted room replaces it.
    }

    public void Discard()
    {
        _current = null;
    }

    internal sealed record BindingIntent(string RoomName, string? Password, string GameMode, string? SaveKey);
}
