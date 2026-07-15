using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectItemModelResolverTests
{
    [Fact]
    public void Missing_model_returns_noninteractive_type_placeholder_without_id()
    {
        FakeModelDbPort port = new();
        LanConnectItemModelResolver resolver = new(port);

        LanConnectResolvedItem result = resolver.Resolve(
            new LanConnectItemRun("relic", "Missing.ModRelic"),
            "zh-CN",
            "mods-a");

        Assert.Equal(LanConnectResolvedItemStatus.Unknown, result.Status);
        Assert.Equal("relic", result.ItemType);
        Assert.Equal("chat.unknown_relic", result.LabelKey);
        Assert.Null(result.LocalizedTitle);
        Assert.Null(result.Preview);
        Assert.DoesNotContain("Missing.ModRelic", result.AccessibleText, StringComparison.Ordinal);
        Assert.DoesNotContain("Missing.ModRelic", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("card", "Strike", "Strike+2")]
    [InlineData("relic", "Anchor", "Anchor")]
    [InlineData("potion", "Fire Potion", "Fire Potion")]
    public void Known_local_models_resolve_type_title_accessibility_and_preview(
        string itemType,
        string title,
        string expectedTitle)
    {
        FakeModelDbPort port = new();
        port.Add(itemType, "MegaCrit.Item", title, maxUpgrade: 2);
        LanConnectItemModelResolver resolver = new(port);

        LanConnectResolvedItem result = resolver.Resolve(
            new LanConnectItemRun(itemType, "MegaCrit.Item", itemType == "card" ? 2 : null),
            "en",
            "mods-a");

        Assert.Equal(LanConnectResolvedItemStatus.Resolved, result.Status);
        Assert.Equal("chat." + itemType, result.LabelKey);
        Assert.Equal(expectedTitle, result.LocalizedTitle);
        Assert.Equal(expectedTitle, result.AccessibleText);
        Assert.NotNull(result.Preview);
        Assert.Equal(itemType, result.Preview!.ItemType);
    }

    [Fact]
    public void Card_upgrade_is_clamped_to_received_local_support_and_protocol_limit_on_local_copy()
    {
        FakeModelDbPort port = new();
        port.Add("card", "MegaCrit.MultiUpgrade", "Multi", maxUpgrade: 2);
        LanConnectItemModelResolver resolver = new(port);

        LanConnectResolvedItem first = resolver.Resolve(
            new LanConnectItemRun("card", "MegaCrit.MultiUpgrade", 99),
            "en",
            "mods-a");
        LanConnectResolvedItem sameClampedKey = resolver.Resolve(
            new LanConnectItemRun("card", "MegaCrit.MultiUpgrade", 9),
            "en",
            "mods-a");

        LanConnectCardPreviewData preview = Assert.IsType<LanConnectCardPreviewData>(first.Preview);
        Assert.Equal(2, preview.UpgradeLevel);
        Assert.Equal(2, Assert.IsType<FakeCardCopy>(preview.Card).UpgradeLevel);
        Assert.Equal("Multi+2", first.LocalizedTitle);
        Assert.Same(first, sameClampedKey);
        Assert.Equal(1, port.LookupCalls);
        Assert.Equal(1, port.CardCopyCalls);
    }

    [Theory]
    [InlineData("relic")]
    [InlineData("potion")]
    public void Non_card_models_never_receive_upgrade_work(string itemType)
    {
        FakeModelDbPort port = new();
        port.Add(itemType, "MegaCrit.Item", "Item");
        LanConnectItemModelResolver resolver = new(port);

        LanConnectResolvedItem result = resolver.Resolve(
            new LanConnectItemRun(itemType, "MegaCrit.Item", 7),
            "en",
            "mods-a");

        Assert.Equal(LanConnectResolvedItemStatus.Resolved, result.Status);
        Assert.Equal(0, port.SupportedUpgradeCalls);
        Assert.Equal(0, port.CardCopyCalls);
    }

    [Theory]
    [InlineData(FailurePoint.Deserialize)]
    [InlineData(FailurePoint.Lookup)]
    [InlineData(FailurePoint.Title)]
    [InlineData(FailurePoint.SupportedUpgrade)]
    [InlineData(FailurePoint.CardCopy)]
    [InlineData(FailurePoint.Preview)]
    public void Every_local_failure_degrades_one_segment_without_id_leak(FailurePoint point)
    {
        FakeModelDbPort port = new() { ThrowAt = point };
        port.Add("card", "PrivateMod.SecretCard", "Secret", maxUpgrade: 1);
        port.Add("relic", "PrivateMod.SecretRelic", "Secret relic");
        LanConnectItemModelResolver resolver = new(port);
        bool previewFailure = point == FailurePoint.Preview;
        string modelId = previewFailure ? "PrivateMod.SecretRelic" : "PrivateMod.SecretCard";
        string itemType = previewFailure ? "relic" : "card";

        LanConnectResolvedItem result = resolver.Resolve(
            new LanConnectItemRun(itemType, modelId, previewFailure ? null : 1),
            "en",
            "mods-a");

        Assert.Equal(LanConnectResolvedItemStatus.Unknown, result.Status);
        Assert.Equal("chat.unknown_" + itemType, result.LabelKey);
        Assert.Null(result.Preview);
        Assert.DoesNotContain(modelId, result.AccessibleText, StringComparison.Ordinal);
        Assert.DoesNotContain(modelId, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_generic_model_type_is_unknown_and_does_not_escape()
    {
        FakeModelDbPort port = new();
        port.Add("relic", "MegaCrit.Anchor", "Anchor");
        LanConnectItemModelResolver resolver = new(port);

        LanConnectResolvedItem result = resolver.Resolve(
            new LanConnectItemRun("card", "MegaCrit.Anchor", 0),
            "en",
            "mods-a");

        Assert.Equal(LanConnectResolvedItemStatus.Unknown, result.Status);
        Assert.Null(result.Preview);
    }

    [Fact]
    public void One_segment_exception_does_not_affect_subsequent_resolution()
    {
        FakeModelDbPort port = new();
        port.Add("relic", "MegaCrit.Anchor", "Anchor");
        port.Add("potion", "MegaCrit.FirePotion", "Fire Potion");
        port.ThrowForModelId = "MegaCrit.Anchor";
        LanConnectItemModelResolver resolver = new(port);

        LanConnectResolvedItem failed = resolver.Resolve(
            new LanConnectItemRun("relic", "MegaCrit.Anchor"), "en", "mods-a");
        LanConnectResolvedItem next = resolver.Resolve(
            new LanConnectItemRun("potion", "MegaCrit.FirePotion"), "en", "mods-a");

        Assert.Equal(LanConnectResolvedItemStatus.Unknown, failed.Status);
        Assert.Equal(LanConnectResolvedItemStatus.Resolved, next.Status);
        Assert.Equal("Fire Potion", next.LocalizedTitle);
    }

    [Fact]
    public void Repeated_resolution_uses_one_locale_mod_type_id_upgrade_cache_entry()
    {
        FakeModelDbPort port = new();
        port.Add("card", "MegaCrit.Strike", "Strike", maxUpgrade: 1);
        LanConnectItemModelResolver resolver = new(port);
        LanConnectItemRun run = new("card", "MegaCrit.Strike", 1);

        LanConnectResolvedItem first = resolver.Resolve(run, "en", "mods-a");
        LanConnectResolvedItem second = resolver.Resolve(run, "en", "mods-a");

        Assert.Same(first, second);
        Assert.Equal(1, port.DeserializeCalls);
        Assert.Equal(1, port.LookupCalls);
        Assert.Equal(1, resolver.CacheCountForTests);
    }

    [Fact]
    public void Locale_or_mod_fingerprint_change_clears_positive_cache()
    {
        FakeModelDbPort port = new();
        port.Add("relic", "MegaCrit.Anchor", "Anchor");
        LanConnectItemModelResolver resolver = new(port);
        LanConnectItemRun run = new("relic", "MegaCrit.Anchor");

        resolver.Resolve(run, "en", "mods-a");
        resolver.Resolve(run, "zh-CN", "mods-a");
        resolver.Resolve(run, "zh-CN", "mods-b");

        Assert.Equal(3, port.LookupCalls);
        Assert.Equal(1, resolver.CacheCountForTests);
    }

    [Fact]
    public void Negative_results_are_cached_only_in_current_context()
    {
        FakeModelDbPort port = new();
        LanConnectItemModelResolver resolver = new(port);
        LanConnectItemRun run = new("potion", "Missing.SecretPotion");

        LanConnectResolvedItem first = resolver.Resolve(run, "en", "mods-a");
        LanConnectResolvedItem second = resolver.Resolve(run, "en", "mods-a");
        resolver.Resolve(run, "en", "mods-b");

        Assert.Same(first, second);
        Assert.Equal(2, port.LookupCalls);
        Assert.Equal(1, resolver.CacheCountForTests);
    }

    [Fact]
    public void Cache_is_lru_bounded_to_256_and_257th_entry_evicts_first()
    {
        FakeModelDbPort port = new() { AutoCreateModels = true };
        LanConnectItemModelResolver resolver = new(port);
        for (int index = 0; index < 257; index++)
        {
            resolver.Resolve(
                new LanConnectItemRun("relic", $"MegaCrit.Relic{index}"),
                "en",
                "mods-a");
        }
        Assert.Equal(256, resolver.CacheCountForTests);
        Assert.Equal(257, port.LookupCalls);

        resolver.Resolve(new LanConnectItemRun("relic", "MegaCrit.Relic0"), "en", "mods-a");

        Assert.Equal(258, port.LookupCalls);
        Assert.Equal(256, resolver.CacheCountForTests);
    }

    public enum FailurePoint
    {
        None,
        Deserialize,
        Lookup,
        Title,
        SupportedUpgrade,
        CardCopy,
        Preview
    }

    private sealed record FakeId(string Value);

    private sealed record FakeModel(string Type, string Id, string Title, int MaxUpgrade);

    private sealed record FakeCardCopy(string Title, int UpgradeLevel);

    private sealed class FakeModelDbPort : ILanConnectModelDbPort
    {
        private readonly Dictionary<(string Type, string Id), FakeModel> _models = [];

        internal FailurePoint ThrowAt { get; init; }

        internal string? ThrowForModelId { get; set; }

        internal bool AutoCreateModels { get; init; }

        internal int DeserializeCalls { get; private set; }

        internal int LookupCalls { get; private set; }

        internal int SupportedUpgradeCalls { get; private set; }

        internal int CardCopyCalls { get; private set; }

        internal void Add(string type, string id, string title, int maxUpgrade = 0) =>
            _models[(type, id)] = new FakeModel(type, id, title, maxUpgrade);

        public object DeserializeModelId(string value)
        {
            DeserializeCalls++;
            ThrowIf(FailurePoint.Deserialize, value);
            return new FakeId(value);
        }

        public bool TryGetCard(object id, out object model) => TryGet("card", id, out model);

        public bool TryGetRelic(object id, out object model) => TryGet("relic", id, out model);

        public bool TryGetPotion(object id, out object model) => TryGet("potion", id, out model);

        public string GetLocalizedTitle(object model)
        {
            string id = model switch
            {
                FakeModel value => value.Id,
                FakeCardCopy => string.Empty,
                _ => throw new InvalidOperationException("wrong fake model")
            };
            ThrowIf(FailurePoint.Title, id);
            return model switch
            {
                FakeModel value => value.Title,
                FakeCardCopy copy => copy.Title + "+" + copy.UpgradeLevel,
                _ => throw new InvalidOperationException("wrong fake model")
            };
        }

        public int GetSupportedCardUpgradeLevel(object card)
        {
            FakeModel model = Assert.IsType<FakeModel>(card);
            SupportedUpgradeCalls++;
            ThrowIf(FailurePoint.SupportedUpgrade, model.Id);
            return model.MaxUpgrade;
        }

        public object CreateCardPreviewCopy(object card, int upgradeLevel)
        {
            FakeModel model = Assert.IsType<FakeModel>(card);
            CardCopyCalls++;
            ThrowIf(FailurePoint.CardCopy, model.Id);
            return new FakeCardCopy(model.Title, upgradeLevel);
        }

        public LanConnectHoverTipPreviewData CreateRelicPreviewData(object relic) =>
            CreateHoverTip("relic", relic);

        public LanConnectHoverTipPreviewData CreatePotionPreviewData(object potion) =>
            CreateHoverTip("potion", potion);

        private bool TryGet(string type, object id, out object model)
        {
            string value = Assert.IsType<FakeId>(id).Value;
            LookupCalls++;
            ThrowIf(FailurePoint.Lookup, value);
            if (_models.TryGetValue((type, value), out FakeModel? found))
            {
                model = found;
                return true;
            }
            if (AutoCreateModels)
            {
                model = new FakeModel(type, value, type + " title", 0);
                return true;
            }
            model = null!;
            return false;
        }

        private LanConnectHoverTipPreviewData CreateHoverTip(string type, object value)
        {
            FakeModel model = Assert.IsType<FakeModel>(value);
            ThrowIf(FailurePoint.Preview, model.Id);
            return new LanConnectHoverTipPreviewData(type, model.Title, "Description", Visual: null);
        }

        private void ThrowIf(FailurePoint point, string modelId)
        {
            if (ThrowAt == point || string.Equals(ThrowForModelId, modelId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("local model failure");
            }
        }
    }
}
