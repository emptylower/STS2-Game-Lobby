using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectRichDraftTests
{
    [Fact]
    public void InsertsEntitiesAtCaretAndMergesAdjacentTextRuns()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("look here");
        draft.SetCaret(new LanConnectDraftPosition(0, 5));
        draft.InsertEntity(new LanConnectEmojiRun("thumbs-up"));
        draft.InsertText("now ");

        Assert.Equal(new LanConnectDraftRun[]
        {
            new LanConnectTextRun("look "),
            new LanConnectEmojiRun("thumbs-up"),
            new LanConnectTextRun("now here")
        }, draft.Runs);
    }

    [Fact]
    public void BackspaceAtEntityBoundaryRemovesWholeEntityAndMergesText()
    {
        LanConnectRichDraft draft = Draft(
            new LanConnectTextRun("a"),
            new LanConnectItemRun("card", "MegaCrit.Strike", 1),
            new LanConnectTextRun("b"));
        draft.SetCaret(new LanConnectDraftPosition(2, 0));

        draft.Backspace();

        Assert.Equal(new LanConnectDraftRun[] { new LanConnectTextRun("ab") }, draft.Runs);
    }

    [Fact]
    public void LeftRightAndDeleteCrossOneAtomicEntity()
    {
        LanConnectRichDraft right = Draft(
            new LanConnectTextRun("a"),
            new LanConnectEmojiRun("heart"),
            new LanConnectTextRun("b"));
        right.SetCaret(new LanConnectDraftPosition(1, 0));
        right.MoveRight();
        right.Backspace();
        Assert.Equal(new LanConnectDraftRun[] { new LanConnectTextRun("ab") }, right.Runs);

        LanConnectRichDraft left = Draft(
            new LanConnectTextRun("a"),
            new LanConnectEmojiRun("heart"),
            new LanConnectTextRun("b"));
        left.SetCaret(new LanConnectDraftPosition(1, 1));
        left.MoveLeft();
        left.Delete();
        Assert.Equal(new LanConnectDraftRun[] { new LanConnectTextRun("ab") }, left.Runs);
    }

    [Fact]
    public void SelectionAcrossPartialTextAndEntitiesReplacesAsOneRange()
    {
        LanConnectRichDraft draft = Draft(
            new LanConnectTextRun("abc"),
            new LanConnectEmojiRun("heart"),
            new LanConnectItemRun("relic", "MegaCrit.Anchor", null),
            new LanConnectTextRun("def"));
        draft.SetSelection(new LanConnectDraftSelection(
            new LanConnectDraftPosition(0, 1),
            new LanConnectDraftPosition(3, 2)));

        draft.ReplaceSelectionWithText("X");

        Assert.Equal(new LanConnectDraftRun[] { new LanConnectTextRun("aXf") }, draft.Runs);
    }

    [Fact]
    public void NewlineReplacementAndEmptyRunCleanupRemainLiteralAndNormalized()
    {
        LanConnectRichDraft draft = Draft(
            new LanConnectTextRun("ab"),
            new LanConnectTextRun(string.Empty),
            new LanConnectTextRun("cd"));
        Assert.Equal(new LanConnectDraftRun[] { new LanConnectTextRun("abcd") }, draft.Runs);
        draft.SetSelection(new LanConnectDraftSelection(
            new LanConnectDraftPosition(0, 1),
            new LanConnectDraftPosition(0, 3)));

        draft.ReplaceSelectionWithText("\n");

        Assert.Equal(new LanConnectDraftRun[] { new LanConnectTextRun("a\nd") }, draft.Runs);
    }

    [Fact]
    public void SelectionCollapseUsesMovementDirectionAndExtensionCanReverse()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("abcd");
        draft.SetSelection(new LanConnectDraftSelection(
            new LanConnectDraftPosition(0, 3),
            new LanConnectDraftPosition(0, 1)));
        draft.MoveLeft();
        Assert.Equal(
            new LanConnectDraftSelection(
                new LanConnectDraftPosition(0, 1),
                new LanConnectDraftPosition(0, 1)),
            draft.Selection);

        draft.SetSelection(new LanConnectDraftSelection(
            new LanConnectDraftPosition(0, 1),
            new LanConnectDraftPosition(0, 3)));
        draft.MoveRight();
        Assert.Equal(
            new LanConnectDraftSelection(
                new LanConnectDraftPosition(0, 3),
                new LanConnectDraftPosition(0, 3)),
            draft.Selection);

        draft.SetCaret(new LanConnectDraftPosition(0, 2));
        draft.MoveLeft(extendSelection: true);
        draft.MoveLeft(extendSelection: true);
        draft.MoveRight(extendSelection: true);
        Assert.Equal(
            new LanConnectDraftSelection(
                new LanConnectDraftPosition(0, 2),
                new LanConnectDraftPosition(0, 1)),
            draft.Selection);
    }

    [Fact]
    public void CaretAndSelectionPositionsClampToDocumentAndEntityEdges()
    {
        LanConnectRichDraft draft = Draft(
            new LanConnectTextRun("ab"),
            new LanConnectEmojiRun("heart"),
            new LanConnectTextRun("cd"));
        draft.SetCaret(new LanConnectDraftPosition(-8, -3));
        draft.InsertText("L");
        draft.SetCaret(new LanConnectDraftPosition(99, 99));
        draft.InsertText("R");
        draft.SetSelection(new LanConnectDraftSelection(
            new LanConnectDraftPosition(1, -5),
            new LanConnectDraftPosition(1, 99)));

        Assert.Equal(new LanConnectDraftRun[]
        {
            new LanConnectTextRun("Lab"),
            new LanConnectEmojiRun("heart"),
            new LanConnectTextRun("cdR")
        }, draft.Runs);
        Assert.Equal(
            new LanConnectDraftSelection(
                new LanConnectDraftPosition(1, 0),
                new LanConnectDraftPosition(1, 1)),
            draft.Selection);
    }

    [Fact]
    public void DirectPositionsInsideSurrogatePairsSnapBeforeTheScalar()
    {
        LanConnectRichDraft insert = LanConnectRichDraft.FromText("A\U0001F600B");
        insert.SetCaret(new LanConnectDraftPosition(0, 2));
        Assert.Equal(new LanConnectDraftPosition(0, 1), insert.Selection.Active);
        insert.InsertText("X");
        Assert.Equal(new LanConnectDraftRun[] { new LanConnectTextRun("AX\U0001F600B") }, insert.Runs);

        LanConnectRichDraft replace = LanConnectRichDraft.FromText("A\U0001F600B");
        replace.SetSelection(new LanConnectDraftSelection(
            new LanConnectDraftPosition(0, 2),
            new LanConnectDraftPosition(0, 3)));
        replace.ReplaceSelectionWithText("Y");
        Assert.Equal(new LanConnectDraftRun[] { new LanConnectTextRun("AYB") }, replace.Runs);

        LanConnectRichDraft movement = LanConnectRichDraft.FromText("A\U0001F600B");
        movement.SetCaret(new LanConnectDraftPosition(0, 1));
        movement.MoveRight();
        Assert.Equal(new LanConnectDraftPosition(0, 3), movement.Selection.Active);
        movement.Backspace();
        Assert.Equal(new LanConnectDraftRun[] { new LanConnectTextRun("AB") }, movement.Runs);
    }

    [Fact]
    public void CopySelectionUsesOnlyGenericLabelsAndNeverLeaksStructuredIdentity()
    {
        LanConnectRichDraft draft = Draft(
            new LanConnectTextRun("a"),
            new LanConnectItemRun("card", "MegaCrit.Strike", 1),
            new LanConnectEmojiRun("heart"),
            new LanConnectItemRun("relic", "Secret.Internal.Relic", null),
            new LanConnectTextRun("b"));
        draft.SetSelection(new LanConnectDraftSelection(
            new LanConnectDraftPosition(0, 0),
            new LanConnectDraftPosition(4, 1)));

        string copied = draft.CopySelection(run => run switch
        {
            LanConnectEmojiRun => "[Emoji]",
            LanConnectItemRun { ItemType: "card" } => "[Card]",
            LanConnectItemRun { ItemType: "relic" } => "[Unknown Relic]",
            _ => "[Unknown]"
        });

        Assert.Equal("a[Card][Emoji][Unknown Relic]b", copied);
        Assert.DoesNotContain("MegaCrit.Strike", copied);
        Assert.DoesNotContain("Secret.Internal.Relic", copied);
        Assert.DoesNotContain("modelId", copied);
        Assert.DoesNotContain('{', copied);
    }

    [Fact]
    public void PasteTreatsJsonLookingInputAsOneLiteralTextRun()
    {
        LanConnectRichDraft draft = new();

        draft.Paste("{\"kind\":\"item_ref\",\"modelId\":\"MegaCrit.Strike\"}");

        LanConnectTextRun text = Assert.IsType<LanConnectTextRun>(Assert.Single(draft.Runs));
        Assert.Equal("{\"kind\":\"item_ref\",\"modelId\":\"MegaCrit.Strike\"}", text.Text);
        Assert.DoesNotContain(draft.Runs, run => run is LanConnectItemRun);
    }

    [Fact]
    public void ToContentPreservesRunOrderAndTypedFields()
    {
        LanConnectRichDraft draft = Draft(
            new LanConnectTextRun("look "),
            new LanConnectEmojiRun("thumbs-up"),
            new LanConnectItemRun("card", "MegaCrit.Strike", 1),
            new LanConnectTextRun(" now"));

        LanConnectChatContent content = draft.ToContent();

        Assert.Equal(1, content.FormatVersion);
        Assert.Equal(new LanConnectChatSegment[]
        {
            new LanConnectTextSegment("look "),
            new LanConnectEmojiSegment("thumbs-up"),
            new LanConnectItemRefSegment("card", "MegaCrit.Strike", 1),
            new LanConnectTextSegment(" now")
        }, content.Segments);
    }

    [Fact]
    public void MeasureReportsExactScalarSegmentEntityFeatureAndWireBudgets()
    {
        LanConnectRichDraft draft = Draft(
            new LanConnectTextRun("A\U00010000"),
            new LanConnectEmojiRun("heart"),
            new LanConnectItemRun("potion", "MegaCrit.FirePotion", null));
        LanConnectChatFeatureVersions enabled = new(1, 1, 1, 0);

        LanConnectDraftMeasure measure = draft.Measure(enabled, "Ironclad");
        LanConnectChatContent content = draft.ToContent();

        Assert.Equal(2, measure.TextScalars);
        Assert.Equal(3, measure.SegmentCount);
        Assert.Equal(2, measure.EntityCount);
        Assert.Equal(
            LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(content, "Ironclad"),
            measure.WorstCaseInboundBytes);
        Assert.True(measure.ContentValid);
        Assert.True(measure.FeaturesSupported);
        Assert.True(measure.WithinProtocolLimits);
        Assert.True(measure.CanSubmit);

        LanConnectRichDraft overEntities = LanConnectRichDraft.FromRuns(
            Enumerable.Range(0, 13).Select(_ =>
                (LanConnectDraftRun)new LanConnectEmojiRun("smile")));
        LanConnectDraftMeasure over = overEntities.Measure(enabled, "Ironclad");
        Assert.Equal(13, over.SegmentCount);
        Assert.Equal(13, over.EntityCount);
        Assert.False(over.ContentValid);
        Assert.False(over.FeaturesSupported);
        Assert.False(over.WithinProtocolLimits);
        Assert.False(over.CanSubmit);
    }

    [Fact]
    public void MeasureUsesFinalCanonicalNfcAndTrimmedContent()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("  e\u0301  ");
        LanConnectChatFeatureVersions callerEnabled = new(0, 0, 0, 0);
        LanConnectChatContent canonical = LanConnectServerChatProtocol.Canonicalize(
            draft.ToContent(),
            new LanConnectChatFeatureVersions(1, 1, 1, 0));

        LanConnectDraftMeasure measure = draft.Measure(callerEnabled, "Ironclad");

        Assert.Equal("é", Assert.IsType<LanConnectTextSegment>(Assert.Single(canonical.Segments)).Text);
        Assert.Equal(1, measure.TextScalars);
        Assert.Equal(1, measure.SegmentCount);
        Assert.Equal(0, measure.EntityCount);
        Assert.Equal(
            LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(canonical, "Ironclad"),
            measure.WorstCaseInboundBytes);
        Assert.True(measure.ContentValid);
        Assert.True(measure.FeaturesSupported);
    }

    [Fact]
    public void MeasureCanonicalizesCrlfBeforeCountingAndWireProjection()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("  e\u0301\r\nline  ");
        LanConnectChatContent canonical = LanConnectServerChatProtocol.Canonicalize(
            draft.ToContent(),
            new LanConnectChatFeatureVersions(1, 1, 1, 0));

        LanConnectDraftMeasure measure = draft.Measure(new(1, 1, 1, 0), "Ironclad");

        Assert.Equal("é\nline", Assert.IsType<LanConnectTextSegment>(Assert.Single(canonical.Segments)).Text);
        Assert.Equal(6, measure.TextScalars);
        Assert.Equal(
            LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(canonical, "Ironclad"),
            measure.WorstCaseInboundBytes);
        Assert.True(measure.CanSubmit);
    }

    [Fact]
    public void MeasureDropsBlankBoundaryRunsBeforeFeatureEligibility()
    {
        LanConnectRichDraft draft = Draft(
            new LanConnectTextRun("  "),
            new LanConnectEmojiRun("heart"),
            new LanConnectTextRun("x"),
            new LanConnectItemRun("card", "MegaCrit.Strike", 1),
            new LanConnectTextRun(" \n "));
        LanConnectChatContent canonical = LanConnectServerChatProtocol.Canonicalize(
            draft.ToContent(),
            new LanConnectChatFeatureVersions(1, 1, 1, 0));

        LanConnectDraftMeasure measure = draft.Measure(
            new LanConnectChatFeatureVersions(0, 0, 0, 0),
            "Ironclad");

        Assert.Equal(3, canonical.Segments.Count);
        Assert.Equal(3, measure.SegmentCount);
        Assert.Equal(1, measure.TextScalars);
        Assert.Equal(2, measure.EntityCount);
        Assert.Equal(
            LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(canonical, "Ironclad"),
            measure.WorstCaseInboundBytes);
        Assert.True(measure.ContentValid);
        Assert.False(measure.FeaturesSupported);
        Assert.False(measure.CanSubmit);
    }

    [Fact]
    public void MeasureReturnsStableInvalidStateForBlankAndMalformedEntities()
    {
        LanConnectRichDraft[] invalidDrafts =
        [
            new(),
            LanConnectRichDraft.FromText(" \r\n "),
            Draft(new LanConnectEmojiRun("unknown")),
            Draft(new LanConnectItemRun("card", "bad/model", null)),
            Draft(new LanConnectItemRun("card", "MegaCrit.Strike\n", null)),
            Draft(new LanConnectItemRun("card", "MegaCrit.Strike", 10)),
            Draft(new LanConnectItemRun("relic", "MegaCrit.Anchor", 1))
        ];

        foreach (LanConnectRichDraft draft in invalidDrafts)
        {
            LanConnectDraftMeasure measure = draft.Measure(new(1, 1, 1, 0), "Ironclad");
            Assert.False(measure.ContentValid);
            Assert.False(measure.FeaturesSupported);
            Assert.False(measure.WithinProtocolLimits);
            Assert.False(measure.CanSubmit);
        }
    }

    [Fact]
    public void MeasureReportsScalarAndEntityProtocolBoundariesWithoutThrowing()
    {
        LanConnectDraftMeasure atScalars = LanConnectRichDraft.FromText(new string('x', 300))
            .Measure(new(1, 1, 1, 0), "Ironclad");
        LanConnectDraftMeasure overScalars = LanConnectRichDraft.FromText(new string('x', 301))
            .Measure(new(1, 1, 1, 0), "Ironclad");
        LanConnectDraftMeasure atEntities = LanConnectRichDraft.FromRuns(
                Enumerable.Range(0, 12).Select(_ =>
                    (LanConnectDraftRun)new LanConnectEmojiRun("smile")))
            .Measure(new(1, 1, 1, 0), "Ironclad");
        LanConnectDraftMeasure overEntities = LanConnectRichDraft.FromRuns(
                Enumerable.Range(0, 13).Select(_ =>
                    (LanConnectDraftRun)new LanConnectEmojiRun("smile")))
            .Measure(new(1, 1, 1, 0), "Ironclad");

        Assert.Equal(300, atScalars.TextScalars);
        Assert.True(atScalars.CanSubmit);
        Assert.Equal(301, overScalars.TextScalars);
        Assert.False(overScalars.ContentValid);
        Assert.False(overScalars.CanSubmit);
        Assert.Equal(12, atEntities.EntityCount);
        Assert.True(atEntities.CanSubmit);
        Assert.Equal(13, overEntities.EntityCount);
        Assert.False(overEntities.ContentValid);
        Assert.False(overEntities.CanSubmit);
    }

    [Fact]
    public void MeasureReportsThirtyTwoAndThirtyThreeRawSegmentsWhenInvalid()
    {
        static LanConnectRichDraft Segments(int count) => LanConnectRichDraft.FromRuns(
            Enumerable.Range(0, count).Select(index => index % 2 == 0
                ? (LanConnectDraftRun)new LanConnectTextRun("x")
                : new LanConnectEmojiRun("smile")));

        LanConnectDraftMeasure atBoundary = Segments(32).Measure(new(1, 1, 1, 0), "Ironclad");
        LanConnectDraftMeasure overBoundary = Segments(33).Measure(new(1, 1, 1, 0), "Ironclad");

        Assert.Equal(32, atBoundary.SegmentCount);
        Assert.Equal(33, overBoundary.SegmentCount);
        Assert.False(atBoundary.ContentValid);
        Assert.False(overBoundary.ContentValid);
        Assert.False(atBoundary.CanSubmit);
        Assert.False(overBoundary.CanSubmit);
    }

    [Fact]
    public void MeasureTreatsExactWireBudgetAsSubmittableAndOneByteOverAsBlocked()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("hello");
        LanConnectChatContent canonical = LanConnectServerChatProtocol.Canonicalize(
            draft.ToContent(),
            new LanConnectChatFeatureVersions(1, 1, 1, 0));
        string senderAt8192 = FindExactSenderName(canonical, 8192);

        LanConnectDraftMeasure atBoundary = draft.Measure(new(1, 1, 1, 0), senderAt8192);
        LanConnectDraftMeasure overBoundary = draft.Measure(new(1, 1, 1, 0), senderAt8192 + "S");

        Assert.Equal(8192, atBoundary.WorstCaseInboundBytes);
        Assert.True(atBoundary.ContentValid);
        Assert.True(atBoundary.CanSubmit);
        Assert.Equal(8193, overBoundary.WorstCaseInboundBytes);
        Assert.True(overBoundary.ContentValid);
        Assert.True(overBoundary.FeaturesSupported);
        Assert.False(overBoundary.WithinProtocolLimits);
        Assert.False(overBoundary.CanSubmit);
    }

    [Fact]
    public void RunsAreSnapshotsAndContentEventsCarryRevisionOutsideDraftLock()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("a");
        IReadOnlyList<LanConnectDraftRun> snapshot = draft.Runs;
        List<long> revisions = [];
        draft.ContentChanged += revision =>
        {
            Task<IReadOnlyList<LanConnectDraftRun>> read = Task.Run(() => draft.Runs);
            Assert.True(read.Wait(TimeSpan.FromSeconds(2)));
            revisions.Add(revision);
        };

        draft.InsertText("b");
        draft.Backspace();

        Assert.Equal(new LanConnectDraftRun[] { new LanConnectTextRun("a") }, snapshot);
        Assert.Equal(new long[] { 1, 2 }, revisions);
        Assert.Equal(2, draft.ContentRevision);
    }

    [Fact]
    public void Throwing_content_subscriber_does_not_block_later_subscribers_or_commit()
    {
        LanConnectRichDraft draft = LanConnectRichDraft.FromText("a");
        List<long> observed = [];
        draft.ContentChanged += _ => throw new InvalidOperationException("observer failed");
        draft.ContentChanged += revision => observed.Add(revision);

        draft.InsertEntity(new LanConnectItemRun("card", "MegaCrit.Strike", 1));

        Assert.Equal(new LanConnectDraftRun[]
        {
            new LanConnectTextRun("a"),
            new LanConnectItemRun("card", "MegaCrit.Strike", 1)
        }, draft.Runs);
        Assert.Equal(1, draft.ContentRevision);
        Assert.Equal(new long[] { 1 }, observed);
    }

    private static string FindExactSenderName(LanConnectChatContent content, int target)
    {
        int baseline = LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(content, "S");
        int padding = target - baseline;
        Assert.True(padding >= 0);
        string senderName = new('S', padding + 1);
        Assert.Equal(target, LanConnectServerChatProtocol.MeasureWorstCaseInboundBytes(content, senderName));
        return senderName;
    }

    private static LanConnectRichDraft Draft(params LanConnectDraftRun[] runs) =>
        LanConnectRichDraft.FromRuns(runs);
}
