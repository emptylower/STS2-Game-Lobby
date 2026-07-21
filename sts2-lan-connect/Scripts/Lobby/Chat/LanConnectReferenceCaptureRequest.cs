using Godot;

namespace Sts2LanConnect.Scripts;

internal enum LanConnectReferencePointerKind
{
    Mouse,
    Touch
}

internal enum LanConnectReferenceCaptureStatus
{
    Captured,
    Unsupported,
    Blocked
}

internal readonly record struct LanConnectReferenceCaptureRequest(
    LanConnectReferenceModeSource Source,
    LanConnectReferencePointerKind PointerKind,
    bool Pressed,
    Vector2 ScreenPosition,
    object? StartingControl,
    long ArmedGeneration);

internal readonly record struct LanConnectReferenceCaptureResult(
    LanConnectReferenceCaptureStatus Status,
    LanConnectReferenceTargetKind TargetKind = LanConnectReferenceTargetKind.None)
{
    internal bool Consumed => Status == LanConnectReferenceCaptureStatus.Captured;
}

internal sealed class LanConnectReferenceTouchMouseDeduplicator
{
    private readonly ulong _maxAgeMilliseconds;
    private readonly float _maxDistanceSquared;
    private Vector2 _lastTouchPosition;
    private ulong _lastTouchTimestamp;
    private bool _pendingTouch;

    internal bool ConsumeSyntheticMouse { get; private set; }

    internal LanConnectReferenceTouchMouseDeduplicator(
        ulong maxAgeMilliseconds = 600,
        float maxDistance = 24f)
    {
        if (maxAgeMilliseconds == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAgeMilliseconds));
        }
        if (!float.IsFinite(maxDistance) || maxDistance < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistance));
        }
        _maxAgeMilliseconds = maxAgeMilliseconds;
        _maxDistanceSquared = maxDistance * maxDistance;
    }

    internal void ObserveTouch(
        Vector2 position,
        ulong timestampMilliseconds,
        bool consumeSyntheticMouse = true)
    {
        _lastTouchPosition = position;
        _lastTouchTimestamp = timestampMilliseconds;
        _pendingTouch = true;
        ConsumeSyntheticMouse = consumeSyntheticMouse;
    }

    internal bool IsSyntheticMouse(Vector2 position, ulong timestampMilliseconds)
    {
        if (!_pendingTouch)
        {
            ConsumeSyntheticMouse = false;
            return false;
        }
        _pendingTouch = false;
        if (timestampMilliseconds < _lastTouchTimestamp ||
            timestampMilliseconds - _lastTouchTimestamp > _maxAgeMilliseconds)
        {
            ConsumeSyntheticMouse = false;
            return false;
        }
        bool matches = position.DistanceSquaredTo(_lastTouchPosition) <= _maxDistanceSquared;
        if (!matches)
        {
            ConsumeSyntheticMouse = false;
        }
        return matches;
    }
}
