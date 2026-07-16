namespace Sts2LanConnect.Scripts;

internal interface ILanConnectMonotonicClock
{
    double NowSeconds { get; }
}

internal enum LanConnectRoomOverlayFadePhase
{
    Awake,
    Waiting,
    Fading,
    Faded
}

internal readonly record struct LanConnectRoomOverlayFadeInput(
    bool Open,
    bool Pinned,
    bool HasFocus,
    bool Hovered,
    bool Dragging,
    bool PreviewVisible,
    bool PickerOrConfirmationVisible,
    bool ModalVisible,
    bool HasDeliveryBlocker,
    int RoomUnread,
    int ServerUnread,
    int RoomNewBelow,
    int ServerNewBelow,
    long ActivityToken,
    long RoomRemoteArrivalRevision,
    long ServerRemoteArrivalRevision,
    bool ReducedMotion);

internal readonly record struct LanConnectRoomOverlayFadeCommand(
    float TargetAlpha,
    double DurationSeconds,
    bool KillTween);

internal readonly record struct LanConnectRoomOverlayFadeTick(
    LanConnectRoomOverlayFadePhase Phase,
    LanConnectRoomOverlayFadeCommand? Command);

internal sealed class LanConnectRoomOverlayFadeController
{
    internal const double IdleDelaySeconds = 5d;
    internal const double NormalFadeDurationSeconds = 0.6d;

    private readonly ILanConnectMonotonicClock _clock;
    private bool _initialized;
    private double _lastNow;
    private double _eligibleSince;
    private double _fadeStartedAt;
    private long _activityToken;
    private long _roomRemoteArrivalRevision;
    private long _serverRemoteArrivalRevision;
    private LanConnectRoomOverlayFadePhase _phase = LanConnectRoomOverlayFadePhase.Awake;

    internal LanConnectRoomOverlayFadeController(ILanConnectMonotonicClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    internal LanConnectRoomOverlayFadePhase Phase => _phase;

    internal LanConnectRoomOverlayFadeTick Tick(LanConnectRoomOverlayFadeInput input)
    {
        double now = _clock.NowSeconds;
        bool eligible = IsEligible(input);
        if (!_initialized)
        {
            _initialized = true;
            _lastNow = now;
            _activityToken = input.ActivityToken;
            _roomRemoteArrivalRevision = input.RoomRemoteArrivalRevision;
            _serverRemoteArrivalRevision = input.ServerRemoteArrivalRevision;
            if (IsValidNow(now) && eligible)
            {
                _eligibleSince = now;
                _phase = LanConnectRoomOverlayFadePhase.Waiting;
            }
            return new LanConnectRoomOverlayFadeTick(_phase, null);
        }

        bool clockRegressed = !IsValidNow(now) || !IsValidNow(_lastNow) || now < _lastNow;
        _lastNow = now;
        bool activityChanged = input.ActivityToken != _activityToken;
        bool incomingChanged =
            input.RoomRemoteArrivalRevision != _roomRemoteArrivalRevision ||
            input.ServerRemoteArrivalRevision != _serverRemoteArrivalRevision;
        _activityToken = input.ActivityToken;
        _roomRemoteArrivalRevision = input.RoomRemoteArrivalRevision;
        _serverRemoteArrivalRevision = input.ServerRemoteArrivalRevision;
        if (clockRegressed || activityChanged || incomingChanged)
        {
            return Wake(now, eligible);
        }

        if (!eligible)
        {
            return _phase == LanConnectRoomOverlayFadePhase.Awake
                ? new LanConnectRoomOverlayFadeTick(_phase, null)
                : Wake(now, eligible: false);
        }

        switch (_phase)
        {
            case LanConnectRoomOverlayFadePhase.Awake:
                _eligibleSince = now;
                _phase = LanConnectRoomOverlayFadePhase.Waiting;
                return new LanConnectRoomOverlayFadeTick(_phase, null);
            case LanConnectRoomOverlayFadePhase.Waiting:
                if (now - _eligibleSince < IdleDelaySeconds)
                {
                    return new LanConnectRoomOverlayFadeTick(_phase, null);
                }
                double duration = input.ReducedMotion ? 0d : NormalFadeDurationSeconds;
                _fadeStartedAt = now;
                _phase = duration == 0d
                    ? LanConnectRoomOverlayFadePhase.Faded
                    : LanConnectRoomOverlayFadePhase.Fading;
                return new LanConnectRoomOverlayFadeTick(
                    _phase,
                    new LanConnectRoomOverlayFadeCommand(0f, duration, KillTween: true));
            case LanConnectRoomOverlayFadePhase.Fading when input.ReducedMotion:
                _phase = LanConnectRoomOverlayFadePhase.Faded;
                return new LanConnectRoomOverlayFadeTick(
                    _phase,
                    new LanConnectRoomOverlayFadeCommand(0f, 0d, KillTween: true));
            case LanConnectRoomOverlayFadePhase.Fading:
                if (now - _fadeStartedAt >= NormalFadeDurationSeconds)
                {
                    _phase = LanConnectRoomOverlayFadePhase.Faded;
                }
                return new LanConnectRoomOverlayFadeTick(_phase, null);
            default:
                return new LanConnectRoomOverlayFadeTick(_phase, null);
        }
    }

    private LanConnectRoomOverlayFadeTick Wake(double now, bool eligible)
    {
        _phase = eligible
            ? LanConnectRoomOverlayFadePhase.Waiting
            : LanConnectRoomOverlayFadePhase.Awake;
        _eligibleSince = IsValidNow(now) ? now : 0d;
        _fadeStartedAt = 0d;
        return new LanConnectRoomOverlayFadeTick(
            _phase,
            new LanConnectRoomOverlayFadeCommand(1f, 0d, KillTween: true));
    }

    private static bool IsEligible(LanConnectRoomOverlayFadeInput input) =>
        input.Open &&
        !input.Pinned &&
        !input.HasFocus &&
        !input.Hovered &&
        !input.Dragging &&
        !input.PreviewVisible &&
        !input.PickerOrConfirmationVisible &&
        !input.ModalVisible &&
        !input.HasDeliveryBlocker &&
        input.RoomUnread <= 0 &&
        input.ServerUnread <= 0 &&
        input.RoomNewBelow <= 0 &&
        input.ServerNewBelow <= 0;

    private static bool IsValidNow(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
}
