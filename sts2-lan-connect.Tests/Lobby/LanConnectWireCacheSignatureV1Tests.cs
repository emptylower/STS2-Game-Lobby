using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectWireCacheSignatureV1Tests
{
    [Fact]
    public void Known_vector_is_stable_across_processes_and_runtimes()
    {
        string signature = Compute();

        Assert.Equal("wcv1:B8vj2oR2rdCBmxvsTfJDeJCrioE9zYTa60RqvasB55o", signature);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Changing_any_table_order_changes_the_signature(int tableIndex)
    {
        string baseline = Compute();

        string changed = Compute(tableIndexToReverse: tableIndex);

        Assert.NotEqual(baseline, changed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Changing_any_bit_width_changes_the_signature(int widthIndex)
    {
        string baseline = Compute();

        string changed = Compute(widthIndexToIncrement: widthIndex);

        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void Adding_or_removing_a_table_entry_changes_the_signature()
    {
        string baseline = Compute();
        string added = Compute(extraEntry: "entry.c");
        string removed = LanConnectWireCacheSignatureV1.Compute(
            ["category.a", "category.b"],
            ["entry.a"],
            ["epoch.a", "epoch.b"],
            ["property.a", "property.b"],
            2,
            3,
            4,
            5);

        Assert.NotEqual(baseline, added);
        Assert.NotEqual(baseline, removed);
    }

    [Fact]
    public void Length_framing_distinguishes_different_element_splits()
    {
        string first = LanConnectWireCacheSignatureV1.Compute(
            ["ab", "c"], [], [], [], 1, 1, 1, 1);
        string second = LanConnectWireCacheSignatureV1.Compute(
            ["a", "bc"], [], [], [], 1, 1, 1, 1);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Stable_string_form_is_versioned_compact_base64url()
    {
        string signature = LanConnectWireCacheSignatureV1.Compute(
            ["none"], ["none"], [], ["Name"], 1, 1, 0, 0);

        Assert.Equal("wcv1:OQDRmvFjBeeel9Kp4mLLQxWaahwH2mpX90pyqpXBDos", signature);
        Assert.Matches("^wcv1:[A-Za-z0-9_-]{43}$", signature);
    }

    private static string Compute(
        int? tableIndexToReverse = null,
        int? widthIndexToIncrement = null,
        string? extraEntry = null)
    {
        string[][] tables =
        [
            ["category.a", "category.b"],
            ["entry.a", "entry.b"],
            ["epoch.a", "epoch.b"],
            ["property.a", "property.b"]
        ];
        if (tableIndexToReverse.HasValue)
        {
            Array.Reverse(tables[tableIndexToReverse.Value]);
        }
        if (extraEntry != null)
        {
            tables[1] = [.. tables[1], extraEntry];
        }

        int[] widths = [2, 3, 4, 5];
        if (widthIndexToIncrement.HasValue)
        {
            widths[widthIndexToIncrement.Value]++;
        }

        return LanConnectWireCacheSignatureV1.Compute(
            tables[0],
            tables[1],
            tables[2],
            tables[3],
            widths[0],
            widths[1],
            widths[2],
            widths[3]);
    }
}
