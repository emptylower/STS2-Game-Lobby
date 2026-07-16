using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Exceptions;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectLocalPowerReference(
    string Title,
    string Description);

internal readonly record struct LanConnectPowerCaptureCandidate(
    string ModelId,
    int Amount,
    string RoomSessionId,
    string? OwnerPlayerNetId = null,
    string? ApplierPlayerNetId = null);

internal readonly record struct LanConnectPlayerTargetCaptureCandidate(
    string PlayerNetId,
    string RoomSessionId);

internal readonly record struct LanConnectMonsterTargetCaptureCandidate(
    object Monster,
    string RoomSessionId);

internal interface ILanConnectRoomCombatContext
{
    string ActiveRoomSessionId { get; }

    bool IsCurrentPeer(string playerNetId);

    bool TryGetCurrentPeerName(string playerNetId, out string name);

    bool TryResolveLocalPower(string modelId, out LanConnectLocalPowerReference power);
}

internal enum LanConnectResolvedCombatReferenceStatus
{
    Resolved,
    UnknownPower,
    TargetExpired
}

internal sealed record LanConnectResolvedCombatReference(
    LanConnectResolvedCombatReferenceStatus Status,
    string Label,
    string Description);

internal sealed class LanConnectRoomCombatReferenceResolver
{
    private readonly ILanConnectRoomCombatContext _context;
    private readonly LanConnectChatLocalizer _localizer;
    private readonly LanConnectMonsterTargetIdPrototype _monsterTargets = new(adapter: null);

    internal LanConnectRoomCombatReferenceResolver(
        ILanConnectRoomCombatContext context,
        LanConnectChatLocalizer? localizer = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _localizer = localizer ?? LanConnectChatUiComposition.Localizer;
    }

    internal bool TryCreatePowerRun(
        LanConnectPowerCaptureCandidate candidate,
        out LanConnectCombatRun run)
    {
        run = null!;
        try
        {
            if (!MatchesActiveSession(candidate.RoomSessionId) ||
                !LanConnectServerChatProtocol.IsValidModelId(candidate.ModelId) ||
                candidate.Amount is < short.MinValue or > short.MaxValue ||
                !_context.TryResolveLocalPower(candidate.ModelId, out _) ||
                !OptionalPeerIsCurrent(candidate.OwnerPlayerNetId) ||
                !OptionalPeerIsCurrent(candidate.ApplierPlayerNetId))
            {
                return false;
            }

            run = new LanConnectCombatRun(new LanConnectPowerStateSegment(
                candidate.ModelId,
                (short)candidate.Amount,
                candidate.RoomSessionId,
                candidate.OwnerPlayerNetId,
                candidate.ApplierPlayerNetId));
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal bool TryCreatePlayerTargetRun(
        LanConnectPlayerTargetCaptureCandidate candidate,
        out LanConnectCombatRun run)
    {
        run = null!;
        try
        {
            if (!MatchesActiveSession(candidate.RoomSessionId) ||
                !IsPrintableAsciiId(candidate.PlayerNetId) ||
                !_context.IsCurrentPeer(candidate.PlayerNetId))
            {
                return false;
            }

            run = new LanConnectCombatRun(new LanConnectTargetRefSegment(
                "player",
                candidate.PlayerNetId,
                candidate.RoomSessionId));
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal bool TryCreateMonsterTargetRun(
        LanConnectMonsterTargetCaptureCandidate candidate,
        out LanConnectCombatRun run)
    {
        run = null!;
        try
        {
            if (!MatchesActiveSession(candidate.RoomSessionId) ||
                !_monsterTargets.TryGetStableId(candidate.Monster, out string stableId))
            {
                return false;
            }
            run = new LanConnectCombatRun(new LanConnectTargetRefSegment(
                "monster",
                stableId,
                candidate.RoomSessionId));
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal bool CanCommit(LanConnectCombatRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        try
        {
            return run.Segment switch
            {
                LanConnectPowerStateSegment power =>
                    MatchesActiveSession(power.RoomSessionId) &&
                    LanConnectServerChatProtocol.IsValidModelId(power.ModelId) &&
                    _context.TryResolveLocalPower(power.ModelId, out _) &&
                    OptionalPeerIsCurrent(power.OwnerPlayerNetId) &&
                    OptionalPeerIsCurrent(power.ApplierPlayerNetId),
                LanConnectTargetRefSegment { TargetKind: "player" } target =>
                    MatchesActiveSession(target.RoomSessionId) &&
                    IsPrintableAsciiId(target.TargetKey) &&
                    _context.IsCurrentPeer(target.TargetKey),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    internal LanConnectResolvedCombatReference Resolve(
        LanConnectCombatRun run,
        string? locale)
    {
        ArgumentNullException.ThrowIfNull(run);
        try
        {
            switch (run.Segment)
            {
                case LanConnectPowerStateSegment power when
                    CanCommit(run) &&
                    _context.TryResolveLocalPower(power.ModelId, out LanConnectLocalPowerReference localPower):
                    return ResolvePower(power, localPower, locale);
                case LanConnectPowerStateSegment:
                    return UnknownPower(locale);
                case LanConnectTargetRefSegment { TargetKind: "player" } target when
                    CanCommit(run) &&
                    _context.TryGetCurrentPeerName(target.TargetKey, out string name) &&
                    !string.IsNullOrWhiteSpace(name):
                    return new LanConnectResolvedCombatReference(
                        LanConnectResolvedCombatReferenceStatus.Resolved,
                        name,
                        name);
                case LanConnectTargetRefSegment:
                    return TargetExpired(locale);
                default:
                    return TargetExpired(locale);
            }
        }
        catch
        {
            return run.Segment is LanConnectPowerStateSegment
                ? UnknownPower(locale)
                : TargetExpired(locale);
        }
    }

    private bool MatchesActiveSession(string roomSessionId) =>
        !string.IsNullOrEmpty(roomSessionId) &&
        !string.IsNullOrEmpty(_context.ActiveRoomSessionId) &&
        string.Equals(roomSessionId, _context.ActiveRoomSessionId, StringComparison.Ordinal);

    private bool OptionalPeerIsCurrent(string? playerNetId) =>
        string.IsNullOrEmpty(playerNetId) || _context.IsCurrentPeer(playerNetId);

    private LanConnectResolvedCombatReference ResolvePower(
        LanConnectPowerStateSegment power,
        LanConnectLocalPowerReference localPower,
        string? locale)
    {
        string amount = power.Amount > 0
            ? $"+{power.Amount}"
            : power.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        List<string> descriptionLines =
        [
            localPower.Description,
            _localizer.Format(locale, "chat.power.amount", amount)
        ];
        if (!TryAppendPeerDescription(
                descriptionLines,
                power.OwnerPlayerNetId,
                locale,
                "chat.power.owner") ||
            !TryAppendPeerDescription(
                descriptionLines,
                power.ApplierPlayerNetId,
                locale,
                "chat.power.applier"))
        {
            return UnknownPower(locale);
        }

        return new LanConnectResolvedCombatReference(
            LanConnectResolvedCombatReferenceStatus.Resolved,
            _localizer.Format(locale, "chat.power.label", localPower.Title, amount),
            string.Join('\n', descriptionLines));
    }

    private bool TryAppendPeerDescription(
        List<string> descriptionLines,
        string? playerNetId,
        string? locale,
        string labelKey)
    {
        if (string.IsNullOrEmpty(playerNetId))
        {
            return true;
        }
        if (!_context.IsCurrentPeer(playerNetId) ||
            !_context.TryGetCurrentPeerName(playerNetId, out string name) ||
            string.IsNullOrWhiteSpace(name))
        {
            return false;
        }
        descriptionLines.Add(_localizer.Format(locale, labelKey, name));
        return true;
    }

    private static bool IsPrintableAsciiId(string? value) =>
        !string.IsNullOrEmpty(value) &&
        value.Length <= 128 &&
        value.All(static character => character is >= '!' and <= '~');

    private LanConnectResolvedCombatReference UnknownPower(string? locale)
    {
        string fallback = _localizer.Get(locale, "chat.unknown_power");
        return new LanConnectResolvedCombatReference(
            LanConnectResolvedCombatReferenceStatus.UnknownPower,
            fallback,
            fallback);
    }

    private LanConnectResolvedCombatReference TargetExpired(string? locale)
    {
        string fallback = _localizer.Get(locale, "chat.target_expired");
        return new LanConnectResolvedCombatReference(
            LanConnectResolvedCombatReferenceStatus.TargetExpired,
            fallback,
            fallback);
    }
}

internal static class LanConnectProductionPowerModelResolver
{
    internal static bool TryResolve(
        string modelId,
        out LanConnectLocalPowerReference power)
    {
        power = default;
        try
        {
            PowerModel model = ModelDb.GetById<PowerModel>(ModelId.Deserialize(modelId));
            string title = model.Title.GetFormattedText();
            string description = model.Description.GetFormattedText();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description))
            {
                return false;
            }
            power = new LanConnectLocalPowerReference(title, description);
            return true;
        }
        catch (ModelNotFoundException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }
}
