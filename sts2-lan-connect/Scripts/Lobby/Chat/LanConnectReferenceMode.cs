namespace Sts2LanConnect.Scripts;

internal enum LanConnectReferenceModeState
{
    Idle,
    Armed
}

internal enum LanConnectReferenceModeSource
{
    TouchButton,
    KeyboardShortcut,
    DirectAltClick
}

internal enum LanConnectReferenceModeExitReason
{
    Captured,
    Canceled,
    ChannelChanged,
    OverlayClosed,
    RoomChanged,
    CapabilityLost
}

[Flags]
internal enum LanConnectReferenceTargetKind
{
    None = 0,
    Item = 1,
    Combat = 2
}

internal sealed record LanConnectReferenceModeContext(
    bool HasChatTarget,
    LanConnectChatChannel Channel,
    LanConnectChatChannelState ChannelState,
    string? RoomId,
    string? RoomSessionId,
    LanConnectReferenceTargetKind AllowedTargets);

internal readonly record struct LanConnectReferenceModePresentation(
    bool Visible,
    bool Enabled,
    bool Armed,
    string StatusKey);

internal sealed class LanConnectReferenceMode
{
    private long _nextArmedGeneration;
    private LanConnectReferenceModeContext? _armedContext;

    internal LanConnectReferenceModeState State { get; private set; }

    internal LanConnectReferenceModeSource? Source { get; private set; }

    internal LanConnectReferenceModeExitReason? LastExitReason { get; private set; }

    internal long ArmedGeneration { get; private set; }

    internal LanConnectChatChannelState? ArmedChannelState => _armedContext?.ChannelState;

    internal bool Toggle(
        LanConnectReferenceModeContext context,
        LanConnectReferenceModeSource source)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (State == LanConnectReferenceModeState.Armed)
        {
            return Exit(LanConnectReferenceModeExitReason.Canceled);
        }
        return Arm(context, source);
    }

    internal bool ArmDirect(LanConnectReferenceModeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return State == LanConnectReferenceModeState.Idle &&
               Arm(context, LanConnectReferenceModeSource.DirectAltClick);
    }

    internal bool CanCapture(
        long armedGeneration,
        LanConnectReferenceModeContext context,
        LanConnectReferenceTargetKind targetKind)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (State != LanConnectReferenceModeState.Armed ||
            armedGeneration <= 0 ||
            armedGeneration != ArmedGeneration ||
            !SameContextIdentity(context) ||
            targetKind is not (LanConnectReferenceTargetKind.Item or LanConnectReferenceTargetKind.Combat))
        {
            return false;
        }
        return EffectiveTargets(context).HasFlag(targetKind);
    }

    internal bool CaptureSucceeded(
        long armedGeneration,
        LanConnectReferenceModeContext context,
        LanConnectReferenceTargetKind targetKind)
    {
        if (!CanCapture(armedGeneration, context, targetKind))
        {
            return false;
        }
        return Exit(LanConnectReferenceModeExitReason.Captured);
    }

    internal bool CaptureFailed(
        long armedGeneration,
        LanConnectReferenceModeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (State == LanConnectReferenceModeState.Armed && armedGeneration == ArmedGeneration)
        {
            Synchronize(context);
        }
        return false;
    }

    internal bool Synchronize(LanConnectReferenceModeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (State != LanConnectReferenceModeState.Armed || _armedContext == null)
        {
            return false;
        }
        if (_armedContext.Channel != context.Channel ||
            !ReferenceEquals(_armedContext.ChannelState, context.ChannelState))
        {
            return Exit(LanConnectReferenceModeExitReason.ChannelChanged);
        }
        if (!string.Equals(_armedContext.RoomId, context.RoomId, StringComparison.Ordinal) ||
            !string.Equals(_armedContext.RoomSessionId, context.RoomSessionId, StringComparison.Ordinal))
        {
            return Exit(LanConnectReferenceModeExitReason.RoomChanged);
        }
        if (!context.HasChatTarget || EffectiveTargets(context) == LanConnectReferenceTargetKind.None)
        {
            return Exit(LanConnectReferenceModeExitReason.CapabilityLost);
        }
        return false;
    }

    internal bool Exit(LanConnectReferenceModeExitReason reason)
    {
        if (State != LanConnectReferenceModeState.Armed)
        {
            return false;
        }
        State = LanConnectReferenceModeState.Idle;
        Source = null;
        ArmedGeneration = 0;
        _armedContext = null;
        LastExitReason = reason;
        return true;
    }

    private bool Arm(
        LanConnectReferenceModeContext context,
        LanConnectReferenceModeSource source)
    {
        if (!CanArm(context))
        {
            return false;
        }
        if (_nextArmedGeneration == long.MaxValue)
        {
            throw new InvalidOperationException("The reference-mode generation is exhausted.");
        }
        State = LanConnectReferenceModeState.Armed;
        Source = source;
        LastExitReason = null;
        ArmedGeneration = ++_nextArmedGeneration;
        _armedContext = context;
        return true;
    }

    private static bool CanArm(LanConnectReferenceModeContext context) =>
        context.HasChatTarget &&
        context.ChannelState.Channel == context.Channel &&
        EffectiveTargets(context) != LanConnectReferenceTargetKind.None &&
        (context.Channel != LanConnectChatChannel.Room ||
         !string.IsNullOrEmpty(context.RoomId) && !string.IsNullOrEmpty(context.RoomSessionId));

    private bool SameContextIdentity(LanConnectReferenceModeContext context) =>
        _armedContext != null &&
        context.HasChatTarget &&
        _armedContext.Channel == context.Channel &&
        ReferenceEquals(_armedContext.ChannelState, context.ChannelState) &&
        string.Equals(_armedContext.RoomId, context.RoomId, StringComparison.Ordinal) &&
        string.Equals(_armedContext.RoomSessionId, context.RoomSessionId, StringComparison.Ordinal);

    private static LanConnectReferenceTargetKind EffectiveTargets(
        LanConnectReferenceModeContext context) => context.Channel switch
        {
            LanConnectChatChannel.Server =>
                context.AllowedTargets & LanConnectReferenceTargetKind.Item,
            LanConnectChatChannel.Room =>
                context.AllowedTargets &
                (LanConnectReferenceTargetKind.Item | LanConnectReferenceTargetKind.Combat),
            _ => LanConnectReferenceTargetKind.None
        };
}
