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
        Assert.True(measure.FeaturesSupported);
        Assert.True(measure.WithinProtocolLimits);

        LanConnectRichDraft overEntities = LanConnectRichDraft.FromRuns(
            Enumerable.Range(0, 13).Select(_ =>
                (LanConnectDraftRun)new LanConnectEmojiRun("smile")));
        LanConnectDraftMeasure over = overEntities.Measure(enabled, "Ironclad");
        Assert.Equal(13, over.SegmentCount);
        Assert.Equal(13, over.EntityCount);
        Assert.False(over.WithinProtocolLimits);
    }

    private static LanConnectRichDraft Draft(params LanConnectDraftRun[] runs) =>
        LanConnectRichDraft.FromRuns(runs);
}
