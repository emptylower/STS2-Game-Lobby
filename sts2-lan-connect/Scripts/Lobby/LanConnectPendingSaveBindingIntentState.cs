using System;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectPendingSaveBindingIntentState
{
    private BindingIntent? _current;

    public BindingIntent? Capture(string roomName, string? password, string gameMode, string? saveKey)
    {
        if (string.IsNullOrWhiteSpace(saveKey))
        {
            _current = null;
            return null;
        }

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
        // B4 hardening only: retain a keyed continue-run intent if lobby-session teardown
        // narrowly wins the race with the save notification. The wider hosted-flow
        // lifetime is bounded separately by Discard().
    }

    public void Discard()
    {
        _current = null;
    }

    internal sealed record BindingIntent(string RoomName, string? Password, string GameMode, string SaveKey);
}
