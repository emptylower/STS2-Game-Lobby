using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby;

public sealed class LanConnectWireCacheSnapshotCaptureTests
{
    [Theory]
    [InlineData(0, "category")]
    [InlineData(1, "entry")]
    [InlineData(2, "epoch")]
    [InlineData(3, "property")]
    public void Forward_reverse_disagreement_returns_unavailable(int tableIndex, string tableName)
    {
        LanConnectWireCacheState state = CreateValidState();
        KeyValuePair<string, int>[] disagreeingMap = GetForwardMap(state, tableIndex).ToArray();
        disagreeingMap[0] = new KeyValuePair<string, int>(disagreeingMap[0].Key, 1);
        state = WithForwardMap(state, tableIndex, disagreeingMap);

        LanConnectWireCacheCaptureResult result = CaptureResult(() => state);

        Assert.Equal(LanConnectWireCacheCaptureStatus.Unavailable, result.Status);
        Assert.Contains(tableName, result.FailureReason, StringComparison.Ordinal);
        Assert.Contains("mapping disagrees", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_reverse_entry_returns_unavailable()
    {
        LanConnectWireCacheState state = CreateValidState() with
        {
            CategoryTable = ["category.a", "category.a"]
        };

        LanConnectWireCacheCaptureResult result = CaptureResult(() => state);

        Assert.Equal(LanConnectWireCacheCaptureStatus.Unavailable, result.Status);
        Assert.Contains("category reverse table contains duplicate ordinal name", result.FailureReason);
    }

    [Theory]
    [InlineData("table")]
    [InlineData("forward_map")]
    [InlineData("width")]
    [InlineData("hash")]
    [InlineData("initialized")]
    public void State_change_between_capture_passes_returns_unavailable(string changedComponent)
    {
        int captureCount = 0;
        LanConnectWireCacheState stable = CreateValidState();

        LanConnectWireCacheCaptureResult result = CaptureResult(() =>
        {
            captureCount++;
            return captureCount == 1 ? stable : ChangeState(stable, changedComponent);
        });

        Assert.Equal(LanConnectWireCacheCaptureStatus.Unavailable, result.Status);
        Assert.Contains("changed between two capture passes", result.FailureReason);
        Assert.Equal(2, captureCount);
    }

    [Fact]
    public void Successful_result_is_recaptured_after_tables_change()
    {
        int stateCaptureCount = 0;
        int successLogCount = 0;
        string[] categoryTable = ["category.a", "category.b"];
        LanConnectWireCacheCaptureCache cache = new(
            () => LanConnectWireCacheSnapshotCapture.Capture(() =>
            {
                stateCaptureCount++;
                return CreateValidState(categoryTable: categoryTable);
            }),
            logSuccess: _ => successLogCount++);

        LanConnectWireCacheCaptureResult first = cache.GetCurrentResult();
        categoryTable = ["category.changed", "category.b"];
        LanConnectWireCacheCaptureResult second = cache.GetCurrentResult();

        Assert.Equal(LanConnectWireCacheCaptureStatus.Available, first.Status);
        Assert.Equal(LanConnectWireCacheCaptureStatus.Available, second.Status);
        Assert.NotEqual(first.Snapshot!.Signature, second.Snapshot!.Signature);
        Assert.Equal(4, stateCaptureCount);
        Assert.Equal(1, successLogCount);
    }

    [Fact]
    public void Unavailable_validation_result_is_cached()
    {
        int stateCaptureCount = 0;
        LanConnectWireCacheState invalid = CreateValidState() with
        {
            CategoryTable = ["category.a", "category.a"]
        };
        LanConnectWireCacheCaptureCache cache = new(
            () => LanConnectWireCacheSnapshotCapture.Capture(() =>
            {
                stateCaptureCount++;
                return invalid;
            }));

        LanConnectWireCacheCaptureResult first = cache.GetCurrentResult();
        LanConnectWireCacheCaptureResult second = cache.GetCurrentResult();

        Assert.Equal(LanConnectWireCacheCaptureStatus.Unavailable, first.Status);
        Assert.Same(first, second);
        Assert.Equal(2, stateCaptureCount);
    }

    [Fact]
    public void Invalid_utf16_in_a_reverse_table_returns_unavailable()
    {
        string[] propertyTable = ["property.a", "\uD800"];
        LanConnectWireCacheState state = CreateValidState(propertyTable: propertyTable);

        LanConnectWireCacheCaptureResult result = CaptureResult(() => state);

        Assert.Equal(LanConnectWireCacheCaptureStatus.Unavailable, result.Status);
        Assert.Contains("EncoderFallbackException", result.FailureReason);
    }

    [Fact]
    public void Uninitialized_cache_returns_unavailable()
    {
        LanConnectWireCacheState state = CreateValidState() with { Initialized = false };

        LanConnectWireCacheCaptureResult result = CaptureResult(() => state);

        Assert.Equal(LanConnectWireCacheCaptureStatus.Unavailable, result.Status);
        Assert.Contains("not initialized", result.FailureReason);
    }

    private static LanConnectWireCacheCaptureResult CaptureResult(
        Func<LanConnectWireCacheState> captureState)
    {
        LanConnectWireCacheCaptureCache cache = new(
            () => LanConnectWireCacheSnapshotCapture.Capture(captureState));
        return cache.GetCurrentResult();
    }

    private static LanConnectWireCacheState CreateValidState(
        IReadOnlyList<string>? categoryTable = null,
        IReadOnlyList<string>? propertyTable = null)
    {
        categoryTable ??= ["category.a", "category.b"];
        propertyTable ??= ["property.a", "property.b"];
        string[] entryTable = ["entry.a", "entry.b"];
        string[] epochTable = ["epoch.a", "epoch.b"];

        return new LanConnectWireCacheState(
            true,
            categoryTable.ToArray(),
            entryTable,
            epochTable,
            propertyTable.ToArray(),
            CreateForwardMap(categoryTable),
            CreateForwardMap(entryTable),
            CreateForwardMap(epochTable),
            CreateForwardMap(propertyTable),
            2,
            3,
            4,
            5,
            123u);
    }

    private static KeyValuePair<string, int>[] CreateForwardMap(IReadOnlyList<string> table) =>
        table.Select(static (name, index) => new KeyValuePair<string, int>(name, index)).ToArray();

    private static IReadOnlyList<KeyValuePair<string, int>> GetForwardMap(
        LanConnectWireCacheState state,
        int tableIndex) =>
        tableIndex switch
        {
            0 => state.CategoryForwardMap,
            1 => state.EntryForwardMap,
            2 => state.EpochForwardMap,
            3 => state.PropertyForwardMap,
            _ => throw new ArgumentOutOfRangeException(nameof(tableIndex))
        };

    private static LanConnectWireCacheState WithForwardMap(
        LanConnectWireCacheState state,
        int tableIndex,
        IReadOnlyList<KeyValuePair<string, int>> forwardMap) =>
        tableIndex switch
        {
            0 => state with { CategoryForwardMap = forwardMap },
            1 => state with { EntryForwardMap = forwardMap },
            2 => state with { EpochForwardMap = forwardMap },
            3 => state with { PropertyForwardMap = forwardMap },
            _ => throw new ArgumentOutOfRangeException(nameof(tableIndex))
        };

    private static LanConnectWireCacheState ChangeState(
        LanConnectWireCacheState state,
        string changedComponent) =>
        changedComponent switch
        {
            "table" => state with { EntryTable = ["entry.changed", "entry.b"] },
            "forward_map" => state with
            {
                EpochForwardMap = [
                    new KeyValuePair<string, int>("epoch.a", 1),
                    new KeyValuePair<string, int>("epoch.b", 0)]
            },
            "width" => state with { PropertyIdBitSize = state.PropertyIdBitSize + 1 },
            "hash" => state with { VanillaHash = state.VanillaHash + 1 },
            "initialized" => state with { Initialized = false },
            _ => throw new ArgumentOutOfRangeException(nameof(changedComponent))
        };
}
