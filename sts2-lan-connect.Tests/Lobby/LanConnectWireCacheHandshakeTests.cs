using System.Collections;
using System.Reflection;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectWireCacheHandshakeTests
{
    private const string SignatureA = "wcv1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string SignatureB = "wcv1:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public void Equal_signatures_are_allowed()
    {
        LanConnectWireCacheHandshakeDecision decision = Decide(
            Available(SignatureA),
            Parse(Token(SignatureA)));

        Assert.Equal(LanConnectWireCacheHandshakeDecisionKind.Match, decision.Kind);
        Assert.True(decision.IsAllowed);
        Assert.False(decision.ShouldWarn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Different_signatures_are_fatal_even_under_relaxed_compatibility(bool relaxedCompatibility)
    {
        LanConnectWireCacheHandshakeDecision decision = Decide(
            Available(SignatureA),
            Parse(Token(SignatureB, 5, 6, 7, 8)),
            relaxedCompatibility);

        Assert.Equal(LanConnectWireCacheHandshakeDecisionKind.Mismatch, decision.Kind);
        Assert.False(decision.IsAllowed);
        Assert.Contains(SignatureA, decision.Detail, StringComparison.Ordinal);
        Assert.Contains(SignatureB, decision.Detail, StringComparison.Ordinal);
        Assert.Contains("category=1, entry=2, epoch=3, property=4", decision.Detail, StringComparison.Ordinal);
        Assert.Contains("category=5, entry=6, epoch=7, property=8", decision.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("lobby-join")]
    [InlineData("load-join")]
    [InlineData("rejoin")]
    public void Host_decision_observer_only_warns_on_mismatch(string path)
    {
        LanConnectWireCacheHandshakeDecision decision = Decide(
            Available(SignatureA),
            Parse(Token(SignatureB, 5, 6, 7, 8)));
        List<string> warnings = [];

        LanConnectWireCacheHandshakeGate.ObserveHostDecision(
            decision,
            LanConnectWireCacheHandshakeTokenStatus.Valid,
            42,
            path,
            _ => throw new InvalidOperationException("mismatch must be a warning"),
            warnings.Add);

        string warning = Assert.Single(warnings);
        Assert.Contains($"path={path}", warning, StringComparison.Ordinal);
        Assert.Contains("senderId=42", warning, StringComparison.Ordinal);
        Assert.Contains($"localSignature={SignatureA}", warning, StringComparison.Ordinal);
        Assert.Contains($"remoteSignature={SignatureB}", warning, StringComparison.Ordinal);
        Assert.Contains("decision=mismatch", warning, StringComparison.Ordinal);
        Assert.Contains("localWidths=category=1, entry=2, epoch=3, property=4", warning, StringComparison.Ordinal);
        Assert.Contains("remoteWidths=category=5, entry=6, epoch=7, property=8", warning, StringComparison.Ordinal);

        // Deliberately not asserted here: that the Harmony prefixes themselves return void
        // and therefore cannot skip the original handler. Reflecting over those methods
        // resolves their game-typed message parameters and needs sts2.dll, which this
        // project does not have. sts2-lan-connect.GdUnitTests is the right home for that
        // check. What this test does prove is that the host decision observer only warns.
    }

    [Fact]
    public void Host_mismatch_logging_failure_cannot_block_the_original_handler()
    {
        LanConnectWireCacheHandshakeDecision decision = Decide(
            Available(SignatureA),
            Parse(Token(SignatureB)));

        Exception? failure = Record.Exception(() =>
            LanConnectWireCacheHandshakeGate.ObserveHostDecision(
                decision,
                LanConnectWireCacheHandshakeTokenStatus.Valid,
                42,
                "load-join",
                _ => { },
                _ => throw new InvalidOperationException("logger failed")));

        Assert.Null(failure);
    }

    [Fact]
    public void Local_unavailable_is_allowed_with_warning()
    {
        LanConnectWireCacheHandshakeDecision decision = Decide(
            LanConnectWireCacheCaptureResult.Unavailable(new InvalidOperationException("capture failed")),
            Parse(Token(SignatureA)));

        Assert.Equal(LanConnectWireCacheHandshakeDecisionKind.LocalUnavailable, decision.Kind);
        Assert.True(decision.IsAllowed);
        Assert.True(decision.ShouldWarn);
    }

    [Fact]
    public void Remote_absent_is_allowed_with_warning()
    {
        LanConnectWireCacheHandshakeDecision decision = Decide(
            Available(SignatureA),
            LanConnectWireCacheHandshakeToken.Parse(["ordinary-mod"]));

        Assert.Equal(LanConnectWireCacheHandshakeDecisionKind.RemoteAbsent, decision.Kind);
        Assert.True(decision.IsAllowed);
        Assert.True(decision.ShouldWarn);
    }

    [Fact]
    public void Allowed_join_survives_warning_logger_failure()
    {
        LanConnectWireCacheHandshakeDecision decision = Decide(
            Available(SignatureA),
            LanConnectWireCacheHandshakeToken.Parse(["ordinary-mod"]));

        bool isAllowed = LanConnectWireCacheHandshakeGate.ShouldAllowJoin(
            decision,
            "remote sentinel absent",
            _ => { },
            _ => throw new InvalidOperationException("logger failed"));

        Assert.True(isAllowed);
    }

    [Fact]
    public void Both_missing_are_allowed()
    {
        LanConnectWireCacheHandshakeDecision decision = Decide(
            LanConnectWireCacheCaptureResult.Unavailable(new InvalidOperationException("capture failed")),
            LanConnectWireCacheHandshakeToken.Parse(null));

        Assert.Equal(LanConnectWireCacheHandshakeDecisionKind.LocalUnavailable, decision.Kind);
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Parses_hash_only_and_width_bearing_sentinels()
    {
        LanConnectWireCacheHandshakeTokenParseResult hashOnly = Parse(
            LanConnectWireCacheHandshakeToken.Prefix + SignatureA);
        LanConnectWireCacheHandshakeTokenParseResult withWidths = Parse(
            Token(SignatureA, 4, 5, 6, 7));

        Assert.Equal(LanConnectWireCacheHandshakeTokenStatus.Valid, hashOnly.Status);
        Assert.False(hashOnly.Token!.HasWidths);
        Assert.Equal(LanConnectWireCacheHandshakeTokenStatus.Valid, withWidths.Status);
        Assert.Equal(4, withWidths.Token!.CategoryIdBitSize);
        Assert.Equal(7, withWidths.Token.PropertyIdBitSize);
    }

    [Theory]
    [InlineData("sts2_lan_connect_wire:v1:not-a-signature")]
    [InlineData("sts2_lan_connect_wire:v1:wcv1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:bits=1,2,3")]
    [InlineData("sts2_lan_connect_wire:v1:wcv1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:bits=1,2,3,nope")]
    public void Malformed_sentinel_is_not_treated_as_a_signature(string sentinel)
    {
        LanConnectWireCacheHandshakeTokenParseResult parse = Parse(sentinel);

        Assert.Equal(LanConnectWireCacheHandshakeTokenStatus.Malformed, parse.Status);
        Assert.Null(parse.Token);
    }

    [Fact]
    public void Duplicate_sentinels_are_ambiguous_and_not_compared()
    {
        LanConnectWireCacheHandshakeTokenParseResult parse =
            LanConnectWireCacheHandshakeToken.Parse([Token(SignatureA), Token(SignatureA)]);
        LanConnectWireCacheHandshakeDecision decision = Decide(Available(SignatureA), parse);

        Assert.Equal(LanConnectWireCacheHandshakeTokenStatus.Duplicate, parse.Status);
        Assert.Equal(LanConnectWireCacheHandshakeDecisionKind.RemoteAbsent, decision.Kind);
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void Sentinel_is_excluded_from_mod_difference_inputs()
    {
        List<string> local = LanConnectWireCacheHandshakeToken.FilterSentinels(
            ["mod-local", Token(SignatureA)]);
        List<string> remote = LanConnectWireCacheHandshakeToken.FilterSentinels(
            [Token(SignatureB), "mod-remote"]);

        Assert.Equal(["mod-local"], local);
        Assert.Equal(["mod-remote"], remote);
        Assert.DoesNotContain(local.Except(remote), LanConnectWireCacheHandshakeToken.IsSentinel);
        Assert.DoesNotContain(remote.Except(local), LanConnectWireCacheHandshakeToken.IsSentinel);
    }

    [Fact]
    public void Replacing_sentinel_never_accumulates_entries()
    {
        LanConnectWireCacheHandshakeToken first = ParsedToken(Token(SignatureA));
        LanConnectWireCacheHandshakeToken second = ParsedToken(Token(SignatureB));

        List<string>? once = LanConnectWireCacheHandshakeToken.ReplaceSentinels(
            ["ordinary-mod", Token(SignatureA)],
            first);
        List<string>? twice = LanConnectWireCacheHandshakeToken.ReplaceSentinels(once, second);

        Assert.Equal(["ordinary-mod", Token(SignatureB)], twice);
        Assert.Single(twice!, LanConnectWireCacheHandshakeToken.IsSentinel);
    }

    [Fact]
    public void Unavailable_capture_removes_stale_sentinel_without_appending_one()
    {
        List<string>? result = LanConnectWireCacheHandshakeToken.ReplaceSentinels(
            ["ordinary-mod", Token(SignatureA)],
            currentToken: null);

        Assert.Equal(["ordinary-mod"], result);
        Assert.DoesNotContain(result!, LanConnectWireCacheHandshakeToken.IsSentinel);
    }

    [Fact]
    public void Advertisement_replacement_contains_throwing_other_mods_list()
    {
        ThrowingStringList otherMods = new();

        bool replaced = LanConnectWireCacheHandshakeToken.TryReplaceSentinels(
            otherMods,
            ParsedToken(Token(SignatureA)),
            out List<string>? replacement,
            out Exception? failure);

        Assert.False(replaced);
        Assert.Null(replacement);
        Assert.IsType<InvalidOperationException>(failure);
    }

    private static LanConnectWireCacheHandshakeDecision Decide(
        LanConnectWireCacheCaptureResult local,
        LanConnectWireCacheHandshakeTokenParseResult remote,
        bool relaxedCompatibility = false) =>
        LanConnectWireCacheHandshakeDecision.Evaluate(local, remote, relaxedCompatibility);

    private static LanConnectWireCacheCaptureResult Available(string signature) =>
        LanConnectWireCacheCaptureResult.Available(new LanConnectWireCacheSnapshot(
            signature,
            CategoryIdBitSize: 1,
            EntryIdBitSize: 2,
            EpochIdBitSize: 3,
            PropertyIdBitSize: 4,
            CategoryCount: 1,
            EntryCount: 1,
            EpochCount: 1,
            PropertyCount: 1,
            VanillaHash: 42));

    private static LanConnectWireCacheHandshakeTokenParseResult Parse(string sentinel) =>
        LanConnectWireCacheHandshakeToken.Parse([sentinel]);

    private static LanConnectWireCacheHandshakeToken ParsedToken(string sentinel) =>
        Assert.IsType<LanConnectWireCacheHandshakeToken>(Parse(sentinel).Token);

    private static string Token(
        string signature,
        int categoryBits = 1,
        int entryBits = 2,
        int epochBits = 3,
        int propertyBits = 4) =>
        new LanConnectWireCacheHandshakeToken(
            signature,
            categoryBits,
            entryBits,
            epochBits,
            propertyBits).Serialize();

    // Deliberately NOT derived from List<string>: Enumerable.Where has a fast path for
    // List<T> that enumerates through the concrete type, which bypasses an explicit
    // interface implementation and would leave the throwing behaviour unreachable.
    private sealed class ThrowingStringList : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator() =>
            throw new InvalidOperationException("enumeration failed");

        IEnumerator IEnumerable.GetEnumerator() =>
            throw new InvalidOperationException("enumeration failed");
    }
}
