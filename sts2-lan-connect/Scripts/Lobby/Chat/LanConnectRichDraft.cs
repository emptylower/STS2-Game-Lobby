using System;
using System.Collections.Generic;

namespace Sts2LanConnect.Scripts;

internal abstract record LanConnectDraftRun;

internal sealed record LanConnectTextRun(string Text) : LanConnectDraftRun;

internal sealed record LanConnectEmojiRun(string EmojiId) : LanConnectDraftRun;

internal sealed record LanConnectItemRun(
    string ItemType,
    string ModelId,
    int? UpgradeLevel = null) : LanConnectDraftRun;

internal readonly record struct LanConnectDraftPosition(int RunIndex, int TextOffset);

internal readonly record struct LanConnectDraftSelection(
    LanConnectDraftPosition Anchor,
    LanConnectDraftPosition Active);

internal readonly record struct LanConnectDraftMeasure(
    int TextScalars,
    int SegmentCount,
    int EntityCount,
    int WorstCaseInboundBytes,
    bool ContentValid,
    bool FeaturesSupported)
{
    internal bool WithinProtocolLimits =>
        ContentValid &&
        TextScalars <= LanConnectServerChatProtocol.MaxTextScalars &&
        SegmentCount <= LanConnectServerChatProtocol.MaxSegments &&
        EntityCount <= LanConnectServerChatProtocol.MaxEntities &&
        WorstCaseInboundBytes <= LanConnectServerChatProtocol.MaxPayloadBytes;

    internal bool CanSubmit => WithinProtocolLimits && FeaturesSupported;
}

internal sealed class LanConnectRichDraft
{
    private readonly object _sync = new();
    private readonly List<LanConnectDraftRun> _runs;
    private LanConnectDraftSelection _selection;
    private long _contentRevision;

    internal event Action<long>? ContentChanged;

    internal LanConnectRichDraft()
        : this([new LanConnectTextRun(string.Empty)])
    {
    }

    private LanConnectRichDraft(IEnumerable<LanConnectDraftRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        _runs = new List<LanConnectDraftRun>(runs);
        NormalizeRuns();
        LanConnectDraftPosition end = PositionFromOffset(DocumentLength, preferRight: false);
        _selection = new LanConnectDraftSelection(end, end);
    }

    internal IReadOnlyList<LanConnectDraftRun> Runs
    {
        get
        {
            lock (_sync)
            {
                return _runs.ToArray();
            }
        }
    }

    internal LanConnectDraftSelection Selection
    {
        get
        {
            lock (_sync)
            {
                return _selection;
            }
        }
    }

    internal bool IsEmpty
    {
        get
        {
            lock (_sync)
            {
                return IsEmptyCore;
            }
        }
    }

    internal long ContentRevision
    {
        get
        {
            lock (_sync)
            {
                return _contentRevision;
            }
        }
    }

    internal static LanConnectRichDraft FromText(string? text) =>
        new([new LanConnectTextRun(text ?? string.Empty)]);

    internal static LanConnectRichDraft FromRuns(IEnumerable<LanConnectDraftRun> runs) => new(runs);

    internal void SetCaret(LanConnectDraftPosition position)
    {
        lock (_sync)
        {
            LanConnectDraftPosition clamped = ClampPosition(position);
            _selection = new LanConnectDraftSelection(clamped, clamped);
        }
    }

    internal void SetSelection(LanConnectDraftSelection selection)
    {
        lock (_sync)
        {
            _selection = new LanConnectDraftSelection(
                ClampPosition(selection.Anchor),
                ClampPosition(selection.Active));
        }
    }

    internal void InsertText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        long? revision;
        lock (_sync)
        {
            revision = ReplaceSelectionCore([new LanConnectTextRun(text)]);
        }
        NotifyContentChanged(revision);
    }

    internal void InsertEntity(LanConnectDraftRun entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity is not (LanConnectEmojiRun or LanConnectItemRun))
        {
            throw new ArgumentException("Only Emoji and item runs are atomic draft entities.", nameof(entity));
        }
        long? revision;
        lock (_sync)
        {
            revision = ReplaceSelectionCore([entity]);
        }
        NotifyContentChanged(revision);
    }

    internal void MoveLeft(bool extendSelection = false)
    {
        lock (_sync)
        {
            int anchor = OffsetFromPosition(_selection.Anchor);
            int active = OffsetFromPosition(_selection.Active);
            if (!extendSelection && anchor != active)
            {
                SetCollapsedOffset(Math.Min(anchor, active), preferRight: true);
                return;
            }

            int next = PreviousLogicalOffset(active);
            if (extendSelection)
            {
                _selection = new LanConnectDraftSelection(
                    _selection.Anchor,
                    PositionFromOffset(next, preferRight: true));
            }
            else
            {
                SetCollapsedOffset(next, preferRight: true);
            }
        }
    }

    internal void MoveRight(bool extendSelection = false)
    {
        lock (_sync)
        {
            int anchor = OffsetFromPosition(_selection.Anchor);
            int active = OffsetFromPosition(_selection.Active);
            if (!extendSelection && anchor != active)
            {
                SetCollapsedOffset(Math.Max(anchor, active), preferRight: false);
                return;
            }

            int next = NextLogicalOffset(active);
            if (extendSelection)
            {
                _selection = new LanConnectDraftSelection(
                    _selection.Anchor,
                    PositionFromOffset(next, preferRight: false));
            }
            else
            {
                SetCollapsedOffset(next, preferRight: false);
            }
        }
    }

    internal void Backspace()
    {
        long? revision = null;
        lock (_sync)
        {
            (int start, int end) = OrderedSelectionOffsets();
            if (start != end)
            {
                revision = ReplaceRangeCore(start, end, []);
            }
            else
            {
                int previous = PreviousLogicalOffset(start);
                if (previous != start)
                {
                    revision = ReplaceRangeCore(previous, start, []);
                }
            }
        }
        NotifyContentChanged(revision);
    }

    internal void Delete()
    {
        long? revision = null;
        lock (_sync)
        {
            (int start, int end) = OrderedSelectionOffsets();
            if (start != end)
            {
                revision = ReplaceRangeCore(start, end, []);
            }
            else
            {
                int next = NextLogicalOffset(end);
                if (next != end)
                {
                    revision = ReplaceRangeCore(end, next, []);
                }
            }
        }
        NotifyContentChanged(revision);
    }

    internal void ReplaceSelectionWithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        long? revision;
        lock (_sync)
        {
            revision = ReplaceSelectionCore([new LanConnectTextRun(text)]);
        }
        NotifyContentChanged(revision);
    }

    internal void ReplaceAllWithText(string? text)
    {
        string next = text ?? string.Empty;
        long? revision = null;
        lock (_sync)
        {
            if (!IsExactlyTextCore(next))
            {
                _runs.Clear();
                _runs.Add(new LanConnectTextRun(next));
                NormalizeRuns();
                SetCollapsedOffset(DocumentLength, preferRight: false);
                revision = NextContentRevision();
            }
        }
        NotifyContentChanged(revision);
    }

    internal string CopySelection(Func<LanConnectDraftRun, string> genericLabel)
    {
        ArgumentNullException.ThrowIfNull(genericLabel);
        lock (_sync)
        {
            (int start, int end) = OrderedSelectionOffsets();
            if (start == end)
            {
                return string.Empty;
            }

            System.Text.StringBuilder builder = new();
            foreach (LanConnectDraftRun run in Slice(start, end))
            {
                if (run is LanConnectTextRun text)
                {
                    builder.Append(text.Text);
                }
                else
                {
                    builder.Append(genericLabel(run) ?? string.Empty);
                }
            }
            return builder.ToString();
        }
    }

    internal void Paste(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ReplaceSelectionWithText(text);
    }

    internal LanConnectChatContent ToContent()
    {
        lock (_sync)
        {
            return ToContentCore();
        }
    }

    private LanConnectChatContent ToContentCore()
    {
        List<LanConnectChatSegment> segments = [];
        foreach (LanConnectDraftRun run in _runs)
        {
            switch (run)
            {
                case LanConnectTextRun { Text.Length: > 0 } text:
                    if (segments.Count > 0 && segments[^1] is LanConnectTextSegment previous)
                    {
                        segments[^1] = new LanConnectTextSegment(previous.Text + text.Text);
                    }
                    else
                    {
                        segments.Add(new LanConnectTextSegment(text.Text));
                    }
                    break;
                case LanConnectTextRun:
                    break;
                case LanConnectEmojiRun emoji:
                    segments.Add(new LanConnectEmojiSegment(emoji.EmojiId));
                    break;
                case LanConnectItemRun item:
                    segments.Add(new LanConnectItemRefSegment(
                        item.ItemType,
                        item.ModelId,
                        item.UpgradeLevel));
                    break;
                default:
                    throw new InvalidOperationException("Unsupported draft run type.");
            }
        }
        return new LanConnectChatContent(1, segments);
    }

    internal LanConnectDraftMeasure Measure(
        LanConnectChatFeatureVersions enabled,
        string senderName)
    {
        ArgumentNullException.ThrowIfNull(enabled);
        ArgumentNullException.ThrowIfNull(senderName);
        LanConnectChatContent rawContent = ToContent();
        LanConnectChatContent content = rawContent;
        bool contentValid = false;
        try
        {
            content = LanConnectServerChatProtocol.Canonicalize(
                rawContent,
                new LanConnectChatFeatureVersions(1, 1, 1, 0));
            contentValid = true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            // The editor still needs stable counters for invalid drafts.
        }
        int scalars = 0;
        int entities = 0;
        foreach (LanConnectChatSegment segment in content.Segments)
        {
            if (segment is LanConnectTextSegment text)
            {
                try
                {
                    scalars += LanConnectServerChatProtocol.CountUnicodeScalars(text.Text);
                }
                catch (InvalidOperationException)
                {
                    scalars += text.Text.Length;
                }
            }
            else
            {
                entities++;
            }
        }
        int wireBytes;
        try
        {
            wireBytes = LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(content, senderName);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            wireBytes = int.MaxValue;
        }
        return new LanConnectDraftMeasure(
            scalars,
            content.Segments.Count,
            entities,
            wireBytes,
            contentValid,
            contentValid && LanConnectChatFeatureResolver.SupportsContent(content, enabled));
    }

    internal long Clear()
    {
        long? revision = null;
        long clearedThroughRevision;
        lock (_sync)
        {
            if (IsEmptyCore)
            {
                SetCollapsedOffset(0, preferRight: true);
            }
            else
            {
                _runs.Clear();
                _runs.Add(new LanConnectTextRun(string.Empty));
                SetCollapsedOffset(0, preferRight: true);
                revision = NextContentRevision();
            }
            clearedThroughRevision = _contentRevision;
        }
        NotifyContentChanged(revision);
        return clearedThroughRevision;
    }

    internal string ToCompatibilityText()
    {
        lock (_sync)
        {
            System.Text.StringBuilder builder = new();
            foreach (LanConnectDraftRun run in _runs)
            {
                switch (run)
                {
                    case LanConnectTextRun text:
                        builder.Append(text.Text);
                        break;
                    case LanConnectEmojiRun:
                        builder.Append("[Emoji]");
                        break;
                    case LanConnectItemRun { ItemType: "card" }:
                        builder.Append("[Card]");
                        break;
                    case LanConnectItemRun { ItemType: "relic" }:
                        builder.Append("[Relic]");
                        break;
                    case LanConnectItemRun { ItemType: "potion" }:
                        builder.Append("[Potion]");
                        break;
                    case LanConnectItemRun:
                        builder.Append("[Item]");
                        break;
                }
            }
            return builder.ToString();
        }
    }

    internal bool IsExactlyText(string text)
    {
        lock (_sync)
        {
            return IsExactlyTextCore(text);
        }
    }

    private bool IsExactlyTextCore(string text) =>
        _runs.Count == 1 && _runs[0] is LanConnectTextRun current &&
        string.Equals(current.Text, text, StringComparison.Ordinal);

    private bool IsEmptyCore =>
        _runs.Count == 1 && _runs[0] is LanConnectTextRun { Text.Length: 0 };

    private int DocumentLength
    {
        get
        {
            int length = 0;
            foreach (LanConnectDraftRun run in _runs)
            {
                length += RunLength(run);
            }
            return length;
        }
    }

    private long? ReplaceSelectionCore(IReadOnlyList<LanConnectDraftRun> replacement)
    {
        (int start, int end) = OrderedSelectionOffsets();
        return ReplaceRangeCore(start, end, replacement);
    }

    private long? ReplaceRangeCore(
        int start,
        int end,
        IReadOnlyList<LanConnectDraftRun> replacement)
    {
        LanConnectDraftRun[] before = _runs.ToArray();
        List<LanConnectDraftRun> left = Slice(0, start);
        List<LanConnectDraftRun> right = Slice(end, DocumentLength);
        int caretOffset = LogicalLength(left) + LogicalLength(replacement);
        _runs.Clear();
        _runs.AddRange(left);
        _runs.AddRange(replacement);
        _runs.AddRange(right);
        NormalizeRuns();
        SetCollapsedOffset(caretOffset, preferRight: false);
        if (!RunsEqual(before, _runs))
        {
            return NextContentRevision();
        }
        return null;
    }

    private long NextContentRevision() => ++_contentRevision;

    private void NotifyContentChanged(long? revision)
    {
        if (revision.HasValue)
        {
            ContentChanged?.Invoke(revision.Value);
        }
    }

    private List<LanConnectDraftRun> Slice(int start, int end)
    {
        List<LanConnectDraftRun> result = [];
        int offset = 0;
        foreach (LanConnectDraftRun run in _runs)
        {
            int runLength = RunLength(run);
            int runEnd = offset + runLength;
            if (run is LanConnectTextRun text)
            {
                int localStart = Math.Max(0, start - offset);
                int localEnd = Math.Min(text.Text.Length, end - offset);
                if (localStart < localEnd)
                {
                    result.Add(new LanConnectTextRun(text.Text[localStart..localEnd]));
                }
            }
            else if (start <= offset && end >= runEnd)
            {
                result.Add(run);
            }
            offset = runEnd;
        }
        return result;
    }

    private void NormalizeRuns()
    {
        List<LanConnectDraftRun> normalized = [];
        foreach (LanConnectDraftRun? run in _runs)
        {
            switch (run)
            {
                case LanConnectTextRun { Text: null }:
                    throw new ArgumentException("Text runs cannot contain null.");
                case LanConnectTextRun { Text.Length: 0 }:
                    continue;
                case LanConnectTextRun text when normalized.Count > 0 && normalized[^1] is LanConnectTextRun previous:
                    normalized[^1] = new LanConnectTextRun(previous.Text + text.Text);
                    break;
                case LanConnectTextRun text:
                    normalized.Add(text);
                    break;
                case LanConnectEmojiRun or LanConnectItemRun:
                    normalized.Add(run);
                    break;
                case null:
                    throw new ArgumentException("Draft runs cannot contain null.");
                default:
                    throw new ArgumentException("Unsupported draft run type.");
            }
        }
        if (normalized.Count == 0)
        {
            normalized.Add(new LanConnectTextRun(string.Empty));
        }
        _runs.Clear();
        _runs.AddRange(normalized);
    }

    private LanConnectDraftPosition ClampPosition(LanConnectDraftPosition position)
    {
        if (position.RunIndex < 0)
        {
            return PositionAtRunEdge(0, before: true);
        }
        if (position.RunIndex >= _runs.Count)
        {
            return PositionAtRunEdge(_runs.Count - 1, before: false);
        }

        LanConnectDraftRun run = _runs[position.RunIndex];
        int offset;
        if (run is LanConnectTextRun text)
        {
            offset = Math.Clamp(position.TextOffset, 0, text.Text.Length);
            if (offset > 0 && offset < text.Text.Length &&
                char.IsHighSurrogate(text.Text[offset - 1]) &&
                char.IsLowSurrogate(text.Text[offset]))
            {
                offset--;
            }
        }
        else
        {
            offset = position.TextOffset <= 0 ? 0 : 1;
        }
        return new LanConnectDraftPosition(position.RunIndex, offset);
    }

    private LanConnectDraftPosition PositionAtRunEdge(int runIndex, bool before)
    {
        LanConnectDraftRun run = _runs[runIndex];
        int offset = before ? 0 : RunLength(run);
        return new LanConnectDraftPosition(runIndex, offset);
    }

    private int OffsetFromPosition(LanConnectDraftPosition position)
    {
        LanConnectDraftPosition clamped = ClampPosition(position);
        int offset = 0;
        for (int index = 0; index < clamped.RunIndex; index++)
        {
            offset += RunLength(_runs[index]);
        }
        return offset + clamped.TextOffset;
    }

    private LanConnectDraftPosition PositionFromOffset(int requestedOffset, bool preferRight)
    {
        int target = Math.Clamp(requestedOffset, 0, DocumentLength);
        int offset = 0;
        for (int index = 0; index < _runs.Count; index++)
        {
            LanConnectDraftRun run = _runs[index];
            int runLength = RunLength(run);
            int end = offset + runLength;
            if (target < end)
            {
                return new LanConnectDraftPosition(index, target - offset);
            }
            if (target == end && (!preferRight || index == _runs.Count - 1))
            {
                return new LanConnectDraftPosition(index, runLength);
            }
            offset = end;
        }
        return PositionAtRunEdge(_runs.Count - 1, before: false);
    }

    private (int Start, int End) OrderedSelectionOffsets()
    {
        int anchor = OffsetFromPosition(_selection.Anchor);
        int active = OffsetFromPosition(_selection.Active);
        return (Math.Min(anchor, active), Math.Max(anchor, active));
    }

    private int PreviousLogicalOffset(int offset)
    {
        if (offset <= 0)
        {
            return 0;
        }
        int runStart = 0;
        foreach (LanConnectDraftRun run in _runs)
        {
            int runEnd = runStart + RunLength(run);
            if (offset > runStart && offset <= runEnd)
            {
                if (run is LanConnectTextRun text)
                {
                    int local = offset - runStart;
                    if (local >= 2 &&
                        char.IsLowSurrogate(text.Text[local - 1]) &&
                        char.IsHighSurrogate(text.Text[local - 2]))
                    {
                        return offset - 2;
                    }
                }
                return offset - 1;
            }
            runStart = runEnd;
        }
        return Math.Max(0, offset - 1);
    }

    private int NextLogicalOffset(int offset)
    {
        int documentLength = DocumentLength;
        if (offset >= documentLength)
        {
            return documentLength;
        }
        int runStart = 0;
        foreach (LanConnectDraftRun run in _runs)
        {
            int runEnd = runStart + RunLength(run);
            if (offset >= runStart && offset < runEnd)
            {
                if (run is LanConnectTextRun text)
                {
                    int local = offset - runStart;
                    if (local + 1 < text.Text.Length &&
                        char.IsHighSurrogate(text.Text[local]) &&
                        char.IsLowSurrogate(text.Text[local + 1]))
                    {
                        return offset + 2;
                    }
                }
                return offset + 1;
            }
            runStart = runEnd;
        }
        return Math.Min(documentLength, offset + 1);
    }

    private void SetCollapsedOffset(int offset, bool preferRight)
    {
        LanConnectDraftPosition position = PositionFromOffset(offset, preferRight);
        _selection = new LanConnectDraftSelection(position, position);
    }

    private static int LogicalLength(IEnumerable<LanConnectDraftRun> runs)
    {
        int length = 0;
        foreach (LanConnectDraftRun run in runs)
        {
            length += RunLength(run);
        }
        return length;
    }

    private static int RunLength(LanConnectDraftRun run) =>
        run is LanConnectTextRun text ? text.Text.Length : 1;

    private static bool RunsEqual(
        IReadOnlyList<LanConnectDraftRun> left,
        IReadOnlyList<LanConnectDraftRun> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }
        for (int index = 0; index < left.Count; index++)
        {
            if (!Equals(left[index], right[index]))
            {
                return false;
            }
        }
        return true;
    }
}
