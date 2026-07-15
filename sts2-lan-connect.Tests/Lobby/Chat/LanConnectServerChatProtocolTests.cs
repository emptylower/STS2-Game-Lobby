using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectServerChatProtocolTests
{
    private static readonly LanConnectChatFeatureVersions RoomFeatures = new(1, 1, 1, 1);
    private static readonly HashSet<string> Peers =
        ["net:owner", "net:applier", "net:target"];

    [Fact]
    public void RoomCombatCanonicalizationIsStrictAndServerStillRejectsIt()
    {
        LanConnectChatContent input = new(1,
        [
            new LanConnectPowerStateSegment(
                "MegaCrit.Strength", short.MinValue, "session-1", "net:owner", "net:applier"),
            new LanConnectTargetRefSegment("player", "net:target", "session-1"),
            new LanConnectTargetRefSegment("monster", "monster-1", "session-1")
        ]);

        LanConnectChatContent canonical = LanConnectServerChatProtocol.CanonicalizeRoom(
            input, RoomFeatures, "session-1", "session-1", Peers);

        Assert.Equal("[Power][Player][Monster]",
            LanConnectServerChatProtocol.RenderGenericFallback(canonical));
        Assert.Equal(
            "{\"formatVersion\":1,\"segments\":[{\"kind\":\"power_state\",\"modelId\":\"MegaCrit.Strength\",\"amount\":-32768,\"roomSessionId\":\"session-1\",\"ownerPlayerNetId\":\"net:owner\",\"applierPlayerNetId\":\"net:applier\"},{\"kind\":\"target_ref\",\"targetKind\":\"player\",\"targetKey\":\"net:target\",\"roomSessionId\":\"session-1\"},{\"kind\":\"target_ref\",\"targetKind\":\"monster\",\"targetKey\":\"monster-1\",\"roomSessionId\":\"session-1\"}]}",
            LanConnectServerChatProtocol.DeterministicContentJson(canonical));
        Assert.Throws<InvalidOperationException>(() =>
            LanConnectServerChatProtocol.Canonicalize(input, RoomFeatures));
        Assert.Throws<InvalidOperationException>(() =>
            LanConnectServerChatProtocol.CanonicalizeRoom(
                input, RoomFeatures with { CombatRefVersion = 0 },
                "session-1", "session-1", Peers));
        Assert.Throws<InvalidOperationException>(() =>
            LanConnectServerChatProtocol.CanonicalizeRoom(
                input, RoomFeatures, "session-1", "session-2", Peers));
    }

    [Fact]
    public void RoomCombatRejectsWrongSessionSchemaAsciiAndPeerAuthority()
    {
        foreach (LanConnectChatSegment segment in new LanConnectChatSegment[]
        {
            new LanConnectPowerStateSegment("bad/model", 1, "session-1"),
            new LanConnectPowerStateSegment("MegaCrit.Strength", 1, "session-2"),
            new LanConnectPowerStateSegment("MegaCrit.Strength", 1, "session-1", "net:gone"),
            new LanConnectPowerStateSegment("MegaCrit.Strength", 1, "session-1", null, "net:gone"),
            new LanConnectTargetRefSegment("other", "net:target", "session-1"),
            new LanConnectTargetRefSegment("player", "net:gone", "session-1"),
            new LanConnectTargetRefSegment("monster", "bad\nkey", "session-1"),
            new LanConnectTargetRefSegment("monster", new string('m', 129), "session-1")
        })
        {
            Assert.Throws<InvalidOperationException>(() =>
                LanConnectServerChatProtocol.CanonicalizeRoom(
                    new(1, [segment]), RoomFeatures, "session-1", "session-1", Peers));
        }

        const string unknownField =
            "{\"formatVersion\":1,\"segments\":[{\"kind\":\"power_state\",\"modelId\":\"MegaCrit.Strength\",\"amount\":1,\"roomSessionId\":\"session-1\",\"label\":\"leak\"}]}";
        const string amountOverflow =
            "{\"formatVersion\":1,\"segments\":[{\"kind\":\"power_state\",\"modelId\":\"MegaCrit.Strength\",\"amount\":32768,\"roomSessionId\":\"session-1\"}]}";
        Assert.Throws<JsonException>(() => Deserialize(unknownField));
        Assert.Throws<JsonException>(() => Deserialize(amountOverflow));
        foreach (string amount in new[] { "1.0", "1e0", "32767.0" })
        {
            LanConnectChatContent parsed = Deserialize(
                $"{{\"formatVersion\":1,\"segments\":[{{\"kind\":\"power_state\",\"modelId\":\"Power\",\"amount\":{amount},\"roomSessionId\":\"session-1\"}}]}}");
            _ = LanConnectServerChatProtocol.CanonicalizeRoom(
                parsed, RoomFeatures, "session-1", "session-1", Peers);
        }
        foreach (string amount in new[] { "1.5", "32768.0", "null", "\"1\"" })
        {
            Assert.Throws<JsonException>(() => Deserialize(
                $"{{\"formatVersion\":1,\"segments\":[{{\"kind\":\"power_state\",\"modelId\":\"Power\",\"amount\":{amount},\"roomSessionId\":\"session-1\"}}]}}"));
        }

        HashSet<string> caseInsensitivePeers = new(StringComparer.OrdinalIgnoreCase)
            { "NET:OWNER" };
        Assert.Throws<InvalidOperationException>(() =>
            LanConnectServerChatProtocol.CanonicalizeRoom(
                new(1, [new LanConnectPowerStateSegment(
                    "Power", 1, "session-1", "net:owner")]),
                RoomFeatures, "session-1", "session-1", caseInsensitivePeers));

        _ = LanConnectServerChatProtocol.CanonicalizeRoom(
            new(1, Enumerable.Range(0, 12).Select(_ =>
                (LanConnectChatSegment)new LanConnectPowerStateSegment(
                    "MegaCrit.Strength", 1, "session-1")).ToArray()),
            RoomFeatures, "session-1", "session-1", Peers);
        Assert.Throws<InvalidOperationException>(() => LanConnectServerChatProtocol.CanonicalizeRoom(
            new(1, Enumerable.Range(0, 13).Select(_ =>
                (LanConnectChatSegment)new LanConnectPowerStateSegment(
                    "MegaCrit.Strength", 1, "session-1")).ToArray()),
            RoomFeatures, "session-1", "session-1", Peers));
    }

    [Fact]
    public void LegacyRoomFallbackUsesTextThenWholeEntityTokens()
    {
        LanConnectChatSegment card = new LanConnectItemRefSegment("card", "Secret.Card");
        LanConnectChatSegment monster = new LanConnectTargetRefSegment(
            "monster", "secret", "session-1");

        Assert.Equal("[Emoji]" + new string('x', 53), Render(
            new LanConnectEmojiSegment("heart"), new LanConnectTextSegment(new string('x', 53))));
        Assert.Equal(new string('x', 53), Render(
            monster, new LanConnectTextSegment(new string('x', 53))));
        Assert.Equal(new string('x', 54) + "[Card]", Render(
            monster, new LanConnectTextSegment(new string('x', 54)), card));
        Assert.Equal(new string('x', 60), Render(new LanConnectTextSegment(new string('x', 60))));
        Assert.Equal(new string('x', 60), Render(new LanConnectTextSegment(new string('x', 61))));
        Assert.Equal(new string('x', 59) + "z", Render(
            new LanConnectTextSegment(new string('x', 59)),
            new LanConnectTextSegment("😀"),
            new LanConnectTextSegment("z")));
        Assert.Equal("[Emoji][Card][Relic][Potion][Power][Player][Monster]", Render(
            new LanConnectEmojiSegment("heart"),
            card,
            new LanConnectItemRefSegment("relic", "Secret.Relic"),
            new LanConnectItemRefSegment("potion", "Secret.Potion"),
            new LanConnectPowerStateSegment("Secret.Power", 1, "session-1"),
            new LanConnectTargetRefSegment("player", "net:target", "session-1"),
            monster));
    }

    [Fact]
    public void RoomWireProjectionMatchesTypeScriptLiteralAndUses128ByteSenderId()
    {
        LanConnectChatContent content = LanConnectServerChatProtocol.CanonicalizeRoom(
            new(1, [new LanConnectPowerStateSegment("MegaCrit.Strength", 1, "session-1")]),
            RoomFeatures, "session-1", "session-1", Peers);

        Assert.Equal(577, LanConnectServerChatProtocol.MeasureWorstCaseRoomBytes(
            content, "room-1", "session-1", "Ironclad"));
        LanConnectServerChatProtocol.AssertRoomBudget(
            content, "room-1", "session-1", "Ironclad");

        string sessionId = new('S', 128);
        string playerNetId = new('P', 128);
        HashSet<string> boundaryPeers = [playerNetId];
        LanConnectChatContent Boundary(short firstAmount)
        {
            List<LanConnectChatSegment> segments =
                [new LanConnectTextSegment(new string('T', 38))];
            segments.AddRange(Enumerable.Range(0, 11).Select(index =>
                (LanConnectChatSegment)new LanConnectPowerStateSegment(
                    new string('M', 160),
                    index == 0 ? firstAmount : short.MinValue,
                    sessionId,
                    playerNetId,
                    playerNetId)));
            return LanConnectServerChatProtocol.CanonicalizeRoom(
                new(1, segments), RoomFeatures, sessionId, sessionId, boundaryPeers);
        }
        LanConnectChatContent exact8192 = Boundary(short.MaxValue);
        LanConnectChatContent exact8193 = Boundary(short.MinValue);
        Assert.Equal(8192, LanConnectServerChatProtocol.MeasureWorstCaseRoomBytes(
            exact8192, new string('R', 128), sessionId, new string('N', 32)));
        Assert.Equal(8193, LanConnectServerChatProtocol.MeasureWorstCaseRoomBytes(
            exact8193, new string('R', 128), sessionId, new string('N', 32)));
        LanConnectServerChatProtocol.AssertRoomBudget(
            exact8192, new string('R', 128), sessionId, new string('N', 32));
        Assert.Throws<InvalidOperationException>(() =>
            LanConnectServerChatProtocol.AssertRoomBudget(
                exact8193, new string('R', 128), sessionId, new string('N', 32)));
    }

    private static string Render(params LanConnectChatSegment[] segments) =>
        LanConnectServerChatProtocol.RenderLegacyRoomFallback(new(1, segments));

    private static LanConnectChatContent Deserialize(string json) =>
        JsonSerializer.Deserialize<LanConnectChatContent>(json, LanConnectChatJson.Options) ??
        throw new InvalidOperationException("Fixture returned null.");
}
