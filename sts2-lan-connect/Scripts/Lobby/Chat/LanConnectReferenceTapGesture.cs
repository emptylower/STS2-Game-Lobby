using Godot;

namespace Sts2LanConnect.Scripts;

// A reference tap must finish before opening a pinned preview. Godot can emit
// both touch events and MetaClicked for the same gesture.
internal sealed class LanConnectReferenceTapGesture
{
    private const float MaxTravelSquared = 16f * 16f;
    private const ulong MetaGuardMilliseconds = 600;
    private int? _touchIndex;
    private Vector2 _start;
    private bool _cancelled;
    private bool _releasedAsTap;
    private bool _activated;
    private bool _hasTouch;
    private ulong _lastTouchTimestamp;

    internal bool TouchInProgress => _touchIndex.HasValue;

    internal void Press(int index, Vector2 position, ulong timestamp)
    {
        if (_touchIndex.HasValue)
        {
            // A second finger is not an item activation gesture.
            _cancelled = true;
            return;
        }
        _touchIndex = index;
        _start = position;
        _cancelled = false;
        _releasedAsTap = false;
        _activated = false;
        ObserveTouch(timestamp);
    }

    internal void Drag(int index, Vector2 position, ulong timestamp)
    {
        if (_touchIndex != index)
        {
            return;
        }
        _cancelled |= position.DistanceSquaredTo(_start) > MaxTravelSquared;
        ObserveTouch(timestamp);
    }

    internal bool Release(int index, Vector2 position, ulong timestamp, bool cancelled)
    {
        if (_touchIndex != index)
        {
            return false;
        }
        _touchIndex = null;
        _releasedAsTap = !_cancelled && !cancelled &&
                         position.DistanceSquaredTo(_start) <= MaxTravelSquared;
        ObserveTouch(timestamp);
        return _releasedAsTap;
    }

    internal bool TryActivateReference(ulong timestamp)
    {
        if (TouchInProgress)
        {
            return false;
        }
        if (!IsRecentTouch(timestamp))
        {
            return true;
        }
        if (!_releasedAsTap || _activated)
        {
            return false;
        }
        _activated = true;
        return true;
    }

    internal bool IgnoreHover(ulong timestamp) => TouchInProgress || IsRecentTouch(timestamp);

    internal void UseMouse()
    {
        _touchIndex = null;
        _hasTouch = false;
    }

    private void ObserveTouch(ulong timestamp)
    {
        _hasTouch = true;
        _lastTouchTimestamp = timestamp;
    }

    private bool IsRecentTouch(ulong timestamp) => _hasTouch &&
        timestamp >= _lastTouchTimestamp &&
        timestamp - _lastTouchTimestamp <= MetaGuardMilliseconds;
}
