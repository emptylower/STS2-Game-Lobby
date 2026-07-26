using System.Text;
using Godot;

namespace Sts2LanConnect.Scripts;

/// <summary>
/// Maps a chat sender name to a stable, readable colour so that in a room with several
/// players you can tell who is who at a glance. Consumed by the HUD-style message row
/// (a later task); this unit is pure logic and holds no Godot node references.
/// </summary>
internal static class LanConnectChatNameColor
{
    // .NET randomises string.GetHashCode() per process on purpose (hash-flood mitigation),
    // so the same player name would land on a different colour every session if we used it.
    // FNV-1a over the UTF-8 bytes gives a hash that is identical across processes and
    // platforms, which is what "stable" actually requires here. Do not "simplify" this
    // back to string.GetHashCode() - it would silently reintroduce the per-session drift.
    private const uint FnvOffsetBasis = 0x811C9DC5;
    private const uint FnvPrime = 0x01000193;

    // Saturation/decorrelation constant (2^32 / phi, Knuth's multiplicative hashing constant).
    // Used to derive saturation from a value that doesn't move in lockstep with the hue bucket.
    private const uint SaturationMixMultiplier = 2654435761u;

    private const int HueBucketCount = 16;

    // Coprime with HueBucketCount: multiplying the bucket index by this and wrapping is a
    // bijection over the same 16 evenly spaced hues, so it keeps the "any two distinct
    // buckets are >= 1/16 apart" guarantee while stopping adjacent hashes (e.g. bucket 3 vs 4)
    // from landing on adjacent, hard-to-tell-apart hues.
    private const uint HuePermutationMultiplier = 9;

    private const float SaturationMin = 0.45f;
    private const float SaturationMax = 0.75f;

    // AccentColor already means "local player / emphasis" throughout this UI
    // (see LanConnectBasicChatPanel.DarkAccentColor). Any sender hue landing within this
    // distance of it gets pushed out to the nearest edge of the excluded band.
    private static readonly Color AccentColor = new(0.88f, 0.58f, 0.17f);
    private const float AccentExclusionHalfWidth = 0.04f;

    // Chosen so that Value = RemoteLightnessTarget / (1 - saturation / 2) never reaches 1.0
    // for any saturation in [SaturationMin, SaturationMax) - the highest saturation we ever
    // produce needs Value ~= 0.992, comfortably under the ceiling. That headroom is what
    // guarantees a local player is always strictly brighter than the same name remote below.
    private const float RemoteLightnessTarget = 0.62f;

    internal static Color ForSender(string? senderName, bool isLocal)
    {
        // Null and empty both hash to the same FNV offset basis (an empty byte span makes
        // the loop below a no-op), so they already share one stable fallback colour.
        uint hash = Fnv1aHash(senderName ?? string.Empty);
        float hue = HueForHash(hash);
        float saturation = SaturationForHash(hash);
        float value = isLocal ? 1f : RemoteLightnessTarget / (1f - (saturation / 2f));
        value = Mathf.Clamp(value, 0f, 1f);

        return Color.FromHsv(hue, saturation, value);
    }

    private static uint Fnv1aHash(string value)
    {
        uint hash = FnvOffsetBasis;
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        return hash;
    }

    private static float HueForHash(uint hash)
    {
        uint rawBucket = hash % HueBucketCount;
        uint spreadBucket = (rawBucket * HuePermutationMultiplier) % HueBucketCount;
        float hue = spreadBucket / (float)HueBucketCount;
        return PushOutsideAccentBand(hue);
    }

    private static float SaturationForHash(uint hash)
    {
        uint mixed = hash * SaturationMixMultiplier;
        float frac = (mixed % 10000u) / 10000f;
        return SaturationMin + ((SaturationMax - SaturationMin) * frac);
    }

    private static float PushOutsideAccentBand(float hue)
    {
        float accentHue = AccentColor.H;
        float signedDistance = WrapSigned(hue - accentHue);
        if (Mathf.Abs(signedDistance) >= AccentExclusionHalfWidth)
        {
            return hue;
        }

        float pushedHue = signedDistance >= 0f
            ? accentHue + AccentExclusionHalfWidth
            : accentHue - AccentExclusionHalfWidth;
        return Wrap01(pushedHue);
    }

    /// <summary>Wraps a hue delta into [-0.5, 0.5), i.e. the shortest signed distance around the wheel.</summary>
    private static float WrapSigned(float delta) => delta - Mathf.Floor(delta + 0.5f);

    private static float Wrap01(float value) => value - Mathf.Floor(value);
}
