using System;

namespace Sts2LanConnect.Scripts;

internal sealed class LanConnectPendingSaveBindingIntentState
{
    private BindingIntent? _current;

    public BindingIntent? Capture(
        string roomName,
        string? password,
        string gameMode,
        string? saveKey,
        LanConnectProtocolSelection frozenSelection,
        object? netService = null)
    {
        ArgumentNullException.ThrowIfNull(frozenSelection);
        if (string.IsNullOrWhiteSpace(saveKey) && netService == null)
        {
            _current = null;
            return null;
        }

        BindingIntent intent = new(roomName, password, gameMode, saveKey, frozenSelection, netService);
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
        // A keyed save notification can arrive after the room session has closed.
        // The exact key and frozen selection remain valid until a different flow starts.
    }

    public void Discard()
    {
        _current = null;
    }

    internal sealed record BindingIntent(
        string RoomName,
        string? Password,
        string GameMode,
        string? SaveKey,
        LanConnectProtocolSelection FrozenSelection,
        object? NetService);
}
