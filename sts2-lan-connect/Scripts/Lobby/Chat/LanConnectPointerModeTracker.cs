namespace Sts2LanConnect.Scripts;

internal enum LanConnectPointerMode
{
    Touch,
    Mouse
}

/// <summary>
/// The classified summary of a raw input event that a caller (which owns the actual Godot
/// <c>InputEvent</c>) hands to <see cref="LanConnectPointerModeTracker"/>. <see cref="Mouse"/>
/// covers both a real <c>InputEventMouseButton</c> and a real <c>InputEventMouseMotion</c> with
/// a non-zero <c>Relative</c> — the tracker treats them identically, so the caller is
/// responsible for filtering out zero-motion mouse-motion noise before reporting.
/// </summary>
internal enum LanConnectPointerModeEventKind
{
    Touch,
    Mouse,
    Key
}

/// <summary>
/// The safety conditions that gate a Touch → Mouse switch (removing a touch entry point).
/// <see cref="PanelOpen"/> true means the chat panel is currently open.
/// </summary>
internal readonly record struct LanConnectPointerModeContext(
    bool PanelOpen,
    bool Dragging,
    bool TweenRunning);

/// <summary>
/// Tracks whether the player is currently driving the chat overlay with touch or with a
/// mouse/keyboard, so callers can decide whether to keep touch-only entry points (the chat
/// bubble, the send button) on screen or fall back to the minimal desktop HUD.
///
/// <para>
/// The <see cref="TouchLockoutSeconds"/> window that follows every touch event exists solely
/// to defend against Godot's <c>input_devices/pointing/emulate_mouse_from_touch</c> project
/// setting, which defaults to <b>on</b> and is not overridden anywhere in this repository.
/// Because this mod ships as a PCK loaded into Slay the Spire 2, the project settings that
/// actually govern at runtime belong to the host game, not to us — we cannot assume that
/// setting is off.
/// </para>
/// <para>
/// With <c>emulate_mouse_from_touch</c> on, every touch on Android also synthesizes an
/// <c>InputEventMouseButton</c> a few milliseconds later. A detector that flips to
/// <see cref="LanConnectPointerMode.Mouse"/> the instant it sees any mouse-shaped event would
/// therefore switch modes on the player's very first tap and delete the chat bubble out from
/// under their finger — precisely the failure this tracker exists to prevent. Do not remove
/// this lockout window or replace it with a same-event-type check: the synthetic event is
/// type-indistinguishable from a real one, so the defense has to be temporal (it always
/// arrives shortly after the touch that produced it), not type-based.
/// </para>
/// </summary>
internal sealed class LanConnectPointerModeTracker
{
    internal const double TouchLockoutSeconds = 1.0d;

    private readonly ILanConnectMonotonicClock _clock;
    private double _touchLockoutUntil;
    private bool _mouseSwitchPending;

    internal LanConnectPointerModeTracker(ILanConnectMonotonicClock clock, LanConnectPointerMode initialMode)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        InternalMode = initialMode;
        EffectiveMode = initialMode;
    }

    /// <summary>The mode implied by the most recent qualifying event, updated immediately.</summary>
    internal LanConnectPointerMode InternalMode { get; private set; }

    /// <summary>
    /// The mode callers should act on. Identical to <see cref="InternalMode"/> except that a
    /// Touch → Mouse transition (removing an entry point) is deferred until it is safe — see
    /// <see cref="NotifyContextChanged"/>.
    /// </summary>
    internal LanConnectPointerMode EffectiveMode { get; private set; }

    /// <summary>Reports a classified input event, evaluated against the current safety context.</summary>
    internal void ReportEvent(LanConnectPointerModeEventKind kind, LanConnectPointerModeContext context)
    {
        switch (kind)
        {
            case LanConnectPointerModeEventKind.Touch:
                EnterTouch();
                break;
            case LanConnectPointerModeEventKind.Mouse:
                if (!IsInsideTouchLockout())
                {
                    EnterMouse(context);
                }
                break;
            case LanConnectPointerModeEventKind.Key:
                EnterMouse(context);
                break;
        }
    }

    /// <summary>
    /// Re-checks a pending Touch → Mouse switch against a freshly changed safety context (the
    /// panel closed, a drag ended, a fade tween finished) with no new input event involved.
    /// </summary>
    internal void NotifyContextChanged(LanConnectPointerModeContext context)
    {
        if (_mouseSwitchPending && IsSafeToRemoveEntryPoint(context))
        {
            EffectiveMode = LanConnectPointerMode.Mouse;
            _mouseSwitchPending = false;
        }
    }

    private void EnterTouch()
    {
        InternalMode = LanConnectPointerMode.Touch;
        EffectiveMode = LanConnectPointerMode.Touch;
        _mouseSwitchPending = false;
        _touchLockoutUntil = SafeNow(_clock.NowSeconds) + TouchLockoutSeconds;
    }

    private void EnterMouse(LanConnectPointerModeContext context)
    {
        InternalMode = LanConnectPointerMode.Mouse;
        if (EffectiveMode == LanConnectPointerMode.Mouse)
        {
            return;
        }

        if (IsSafeToRemoveEntryPoint(context))
        {
            EffectiveMode = LanConnectPointerMode.Mouse;
            _mouseSwitchPending = false;
        }
        else
        {
            _mouseSwitchPending = true;
        }
    }

    private bool IsInsideTouchLockout() => SafeNow(_clock.NowSeconds) < _touchLockoutUntil;

    private static bool IsSafeToRemoveEntryPoint(LanConnectPointerModeContext context) =>
        !context.PanelOpen && !context.Dragging && !context.TweenRunning;

    private static double SafeNow(double value) => IsValidNow(value) ? value : 0d;

    private static bool IsValidNow(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
}
