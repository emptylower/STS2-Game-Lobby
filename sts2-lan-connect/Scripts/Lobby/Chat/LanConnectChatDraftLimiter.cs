using System.Text;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectChatDraftLimitResult(
    string Text,
    int CaretUtf16Offset);

internal static class LanConnectChatDraftLimiter
{
    internal static LanConnectChatDraftLimitResult Limit(
        string acceptedText,
        string candidateText,
        int candidateCaretUtf16Offset,
        int maxScalars)
    {
        ArgumentNullException.ThrowIfNull(acceptedText);
        ArgumentNullException.ThrowIfNull(candidateText);
        ArgumentOutOfRangeException.ThrowIfNegative(maxScalars);

        List<Rune> accepted = ToRunes(acceptedText);
        List<Rune> candidate = ToRunes(candidateText);
        int candidateCaretRune = RuneIndexAtUtf16Offset(candidateText, candidateCaretUtf16Offset);
        if (candidate.Count <= maxScalars)
        {
            string sanitized = FromRunes(candidate);
            return new LanConnectChatDraftLimitResult(
                sanitized,
                Utf16OffsetAtRuneIndex(candidate, candidateCaretRune));
        }

        int prefix = CommonPrefixLength(accepted, candidate);
        int suffix = CommonSuffixLength(accepted, candidate, prefix);
        int removedCount = accepted.Count - prefix - suffix;
        int insertedCount = candidate.Count - prefix - suffix;
        int available = Math.Max(0, maxScalars - (accepted.Count - removedCount));
        int acceptedInsertCount = Math.Min(insertedCount, available);

        List<Rune> result = new(Math.Min(maxScalars, accepted.Count + acceptedInsertCount));
        result.AddRange(accepted.GetRange(0, prefix));
        result.AddRange(candidate.GetRange(prefix, acceptedInsertCount));
        result.AddRange(accepted.GetRange(accepted.Count - suffix, suffix));
        if (result.Count > maxScalars)
        {
            result.RemoveRange(maxScalars, result.Count - maxScalars);
        }

        int resultCaretRune;
        if (candidateCaretRune <= prefix)
        {
            resultCaretRune = candidateCaretRune;
        }
        else if (candidateCaretRune <= prefix + insertedCount)
        {
            resultCaretRune = prefix + Math.Min(candidateCaretRune - prefix, acceptedInsertCount);
        }
        else
        {
            resultCaretRune = prefix + acceptedInsertCount + candidateCaretRune - prefix - insertedCount;
        }
        resultCaretRune = Math.Clamp(resultCaretRune, 0, result.Count);
        return new LanConnectChatDraftLimitResult(
            FromRunes(result),
            Utf16OffsetAtRuneIndex(result, resultCaretRune));
    }

    private static int CommonPrefixLength(IReadOnlyList<Rune> left, IReadOnlyList<Rune> right)
    {
        int count = Math.Min(left.Count, right.Count);
        int prefix = 0;
        while (prefix < count && left[prefix].Value == right[prefix].Value)
        {
            prefix++;
        }
        return prefix;
    }

    private static int CommonSuffixLength(
        IReadOnlyList<Rune> left,
        IReadOnlyList<Rune> right,
        int prefix)
    {
        int max = Math.Min(left.Count, right.Count) - prefix;
        int suffix = 0;
        while (suffix < max &&
               left[left.Count - 1 - suffix].Value == right[right.Count - 1 - suffix].Value)
        {
            suffix++;
        }
        return suffix;
    }

    private static List<Rune> ToRunes(string text)
    {
        List<Rune> runes = new();
        foreach (Rune rune in text.EnumerateRunes())
        {
            runes.Add(rune);
        }
        return runes;
    }

    private static string FromRunes(IReadOnlyList<Rune> runes)
    {
        StringBuilder builder = new();
        foreach (Rune rune in runes)
        {
            builder.Append(rune.ToString());
        }
        return builder.ToString();
    }

    private static int RuneIndexAtUtf16Offset(string text, int utf16Offset)
    {
        int clamped = Math.Clamp(utf16Offset, 0, text.Length);
        int consumed = 0;
        int runes = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int next = consumed + rune.Utf16SequenceLength;
            if (next > clamped)
            {
                break;
            }
            consumed = next;
            runes++;
        }
        return runes;
    }

    private static int Utf16OffsetAtRuneIndex(IReadOnlyList<Rune> runes, int runeIndex)
    {
        int count = Math.Clamp(runeIndex, 0, runes.Count);
        int offset = 0;
        for (int index = 0; index < count; index++)
        {
            offset += runes[index].Utf16SequenceLength;
        }
        return offset;
    }
}
