using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Exceptions;
using System.Security.Cryptography;
using System.Text;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectItemResolverContext(
    string Locale,
    string ModFingerprint);

internal sealed class LanConnectProductionItemResolverContextProvider
{
    private readonly Func<string> _locale;
    private readonly string _modFingerprint;

    internal LanConnectProductionItemResolverContextProvider()
        : this(
            () => TranslationServer.GetLocale(),
            () => LanConnectBuildInfo.GetModList())
    {
    }

    internal LanConnectProductionItemResolverContextProvider(
        Func<string> locale,
        Func<IReadOnlyList<string>> loadedMods)
    {
        _locale = locale ?? throw new ArgumentNullException(nameof(locale));
        ArgumentNullException.ThrowIfNull(loadedMods);
        string stableModList = string.Join(
            "\u001f",
            loadedMods()
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal));
        _modFingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(stableModList)));
    }

    internal LanConnectItemResolverContext Current => new(
        _locale() ?? string.Empty,
        _modFingerprint);
}

internal interface ILanConnectModelDbPort
{
    object DeserializeModelId(string value);

    bool TryGetCard(object id, out object model);

    bool TryGetRelic(object id, out object model);

    bool TryGetPotion(object id, out object model);

    string GetLocalizedTitle(object model);

    int GetSupportedCardUpgradeLevel(object card);

    object CreateCardPreviewCopy(object card, int upgradeLevel);

    LanConnectHoverTipPreviewData CreateRelicPreviewData(object relic);

    LanConnectHoverTipPreviewData CreatePotionPreviewData(object potion);
}

internal enum LanConnectResolvedItemStatus
{
    Resolved,
    Unknown
}

internal abstract record LanConnectItemPreviewData(string ItemType);

internal sealed record LanConnectCardPreviewData(
    object Card,
    int UpgradeLevel) : LanConnectItemPreviewData("card");

internal sealed record LanConnectHoverTipPreviewData(
    string ItemType,
    string Title,
    string Description,
    object? Visual,
    bool IsDebuff = false) : LanConnectItemPreviewData(ItemType);

internal sealed record LanConnectResolvedItem(
    LanConnectResolvedItemStatus Status,
    string ItemType,
    string LabelKey,
    string? LocalizedTitle,
    string AccessibleText,
    LanConnectItemPreviewData? Preview);

internal sealed class LanConnectItemModelResolver
{
    private readonly record struct CacheKey(
        string Locale,
        string ModFingerprint,
        string ItemType,
        string ModelId,
        int? UpgradeLevel);

    private sealed record CacheEntry(
        CacheKey Key,
        LanConnectResolvedItem Result,
        int? SupportedCardUpgradeLevel);

    internal const int MaxCacheEntries = 256;

    private readonly ILanConnectModelDbPort _port;
    private readonly LanConnectChatLocalizer _localizer;
    private readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> _cache = [];
    private readonly LinkedList<CacheEntry> _lru = [];
    private string? _locale;
    private string? _modFingerprint;

    internal LanConnectItemModelResolver()
        : this(new LanConnectProductionModelDbPort())
    {
    }

    internal LanConnectItemModelResolver(ILanConnectModelDbPort port)
        : this(port, LanConnectChatUiComposition.Localizer)
    {
    }

    internal LanConnectItemModelResolver(
        ILanConnectModelDbPort port,
        LanConnectChatLocalizer localizer)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    internal int CacheCountForTests => _cache.Count;

    internal void SetContext(string? locale, string? modFingerprint)
    {
        string normalizedLocale = locale ?? string.Empty;
        string normalizedFingerprint = modFingerprint ?? string.Empty;
        if (string.Equals(_locale, normalizedLocale, StringComparison.Ordinal) &&
            string.Equals(_modFingerprint, normalizedFingerprint, StringComparison.Ordinal))
        {
            return;
        }
        _locale = normalizedLocale;
        _modFingerprint = normalizedFingerprint;
        _cache.Clear();
        _lru.Clear();
    }

    internal LanConnectResolvedItem Resolve(
        LanConnectItemRun run,
        string? locale,
        string? modFingerprint)
    {
        ArgumentNullException.ThrowIfNull(run);
        SetContext(locale, modFingerprint);
        string itemType = CanonicalItemType(run.ItemType);
        int? requestedUpgrade = itemType == "card"
            ? Math.Clamp(run.UpgradeLevel ?? 0, 0, 9)
            : null;
        CacheKey requestedKey = new(
            _locale!,
            _modFingerprint!,
            itemType,
            run.ModelId,
            requestedUpgrade);
        if (TryGetCached(requestedKey, out LanConnectResolvedItem? cached))
        {
            return cached;
        }
        if (itemType == "card" &&
            TryGetCardWithEquivalentClampedUpgrade(requestedKey, requestedUpgrade!.Value, out cached))
        {
            return cached;
        }

        try
        {
            return ResolveUncached(run.ModelId, itemType, requestedUpgrade, requestedKey);
        }
        catch
        {
            LanConnectResolvedItem unknown = Unknown(itemType);
            AddOrReplace(requestedKey, unknown, supportedCardUpgradeLevel: null);
            return unknown;
        }
    }

    private LanConnectResolvedItem ResolveUncached(
        string modelId,
        string itemType,
        int? requestedUpgrade,
        CacheKey requestedKey)
    {
        if (itemType == "item")
        {
            LanConnectResolvedItem unsupported = Unknown(itemType);
            AddOrReplace(requestedKey, unsupported, supportedCardUpgradeLevel: null);
            return unsupported;
        }

        object id = _port.DeserializeModelId(modelId);
        object model;
        bool found;
        switch (itemType)
        {
            case "card":
                found = _port.TryGetCard(id, out model!);
                break;
            case "relic":
                found = _port.TryGetRelic(id, out model!);
                break;
            case "potion":
                found = _port.TryGetPotion(id, out model!);
                break;
            default:
                found = false;
                model = null!;
                break;
        }
        if (!found)
        {
            LanConnectResolvedItem missing = Unknown(itemType);
            AddOrReplace(requestedKey, missing, supportedCardUpgradeLevel: null);
            return missing;
        }

        switch (itemType)
        {
            case "card":
                return ResolveCard(model, requestedUpgrade!.Value, requestedKey);
            case "relic":
                return ResolveHoverTip(model, itemType, requestedKey, _port.CreateRelicPreviewData);
            case "potion":
                return ResolveHoverTip(model, itemType, requestedKey, _port.CreatePotionPreviewData);
            default:
                throw new InvalidOperationException("Unsupported item type.");
        }
    }

    private LanConnectResolvedItem ResolveCard(
        object card,
        int requestedUpgrade,
        CacheKey requestedKey)
    {
        int supportedUpgrade = Math.Clamp(_port.GetSupportedCardUpgradeLevel(card), 0, 9);
        int clampedUpgrade = Math.Min(requestedUpgrade, supportedUpgrade);
        CacheKey finalKey = requestedKey with { UpgradeLevel = clampedUpgrade };
        if (TryGetCached(finalKey, out LanConnectResolvedItem? cached))
        {
            return cached;
        }
        object previewCard = _port.CreateCardPreviewCopy(card, clampedUpgrade);
        string title = RequireTitle(_port.GetLocalizedTitle(previewCard));
        LanConnectResolvedItem result = new(
            LanConnectResolvedItemStatus.Resolved,
            "card",
            "chat.card",
            title,
            title,
            new LanConnectCardPreviewData(previewCard, clampedUpgrade));
        AddOrReplace(finalKey, result, supportedUpgrade);
        return result;
    }

    private LanConnectResolvedItem ResolveHoverTip(
        object model,
        string itemType,
        CacheKey key,
        Func<object, LanConnectHoverTipPreviewData> createPreview)
    {
        string title = RequireTitle(_port.GetLocalizedTitle(model));
        LanConnectHoverTipPreviewData preview = createPreview(model);
        if (!string.Equals(preview.ItemType, itemType, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(preview.Title) ||
            string.IsNullOrWhiteSpace(preview.Description))
        {
            throw new InvalidOperationException("The local preview data is incomplete.");
        }
        LanConnectResolvedItem result = new(
            LanConnectResolvedItemStatus.Resolved,
            itemType,
            "chat." + itemType,
            title,
            title,
            preview);
        AddOrReplace(key, result, supportedCardUpgradeLevel: null);
        return result;
    }

    private bool TryGetCardWithEquivalentClampedUpgrade(
        CacheKey requestedKey,
        int requestedUpgrade,
        out LanConnectResolvedItem result)
    {
        foreach (LinkedListNode<CacheEntry> node in _cache.Values.ToArray())
        {
            CacheEntry entry = node.Value;
            if (entry.SupportedCardUpgradeLevel is not { } supported ||
                !string.Equals(entry.Key.Locale, requestedKey.Locale, StringComparison.Ordinal) ||
                !string.Equals(entry.Key.ModFingerprint, requestedKey.ModFingerprint, StringComparison.Ordinal) ||
                !string.Equals(entry.Key.ItemType, "card", StringComparison.Ordinal) ||
                !string.Equals(entry.Key.ModelId, requestedKey.ModelId, StringComparison.Ordinal) ||
                entry.Key.UpgradeLevel != Math.Min(requestedUpgrade, supported))
            {
                continue;
            }
            Touch(node);
            result = entry.Result;
            return true;
        }
        result = null!;
        return false;
    }

    private bool TryGetCached(CacheKey key, out LanConnectResolvedItem result)
    {
        if (_cache.TryGetValue(key, out LinkedListNode<CacheEntry>? node))
        {
            Touch(node);
            result = node.Value.Result;
            return true;
        }
        result = null!;
        return false;
    }

    private void AddOrReplace(
        CacheKey key,
        LanConnectResolvedItem result,
        int? supportedCardUpgradeLevel)
    {
        if (_cache.Remove(key, out LinkedListNode<CacheEntry>? existing))
        {
            _lru.Remove(existing);
        }
        LinkedListNode<CacheEntry> node = _lru.AddFirst(
            new CacheEntry(key, result, supportedCardUpgradeLevel));
        _cache.Add(key, node);
        while (_cache.Count > MaxCacheEntries && _lru.Last is { } oldest)
        {
            _lru.RemoveLast();
            _cache.Remove(oldest.Value.Key);
        }
    }

    private void Touch(LinkedListNode<CacheEntry> node)
    {
        _lru.Remove(node);
        _lru.AddFirst(node);
    }

    private static string RequireTitle(string title) =>
        !string.IsNullOrWhiteSpace(title)
            ? title
            : throw new InvalidOperationException("The local item title is blank.");

    private static string CanonicalItemType(string itemType) => itemType switch
    {
        "card" => "card",
        "relic" => "relic",
        "potion" => "potion",
        _ => "item"
    };

    private LanConnectResolvedItem Unknown(string itemType)
    {
        string labelKey = itemType switch
        {
            "card" => "chat.unknown_card",
            "relic" => "chat.unknown_relic",
            "potion" => "chat.unknown_potion",
            _ => "chat.unknown_item"
        };
        return new LanConnectResolvedItem(
            LanConnectResolvedItemStatus.Unknown,
            itemType,
            labelKey,
            LocalizedTitle: null,
            AccessibleText: _localizer.Get(_locale, labelKey),
            Preview: null);
    }
}

internal sealed class LanConnectProductionModelDbPort : ILanConnectModelDbPort
{
    public object DeserializeModelId(string value) => ModelId.Deserialize(value);

    public bool TryGetCard(object id, out object model) =>
        TryGet<CardModel>(id, out model);

    public bool TryGetRelic(object id, out object model) =>
        TryGet<RelicModel>(id, out model);

    public bool TryGetPotion(object id, out object model) =>
        TryGet<PotionModel>(id, out model);

    public string GetLocalizedTitle(object model) => model switch
    {
        CardModel card => card.Title,
        RelicModel relic => relic.Title.GetFormattedText(),
        PotionModel potion => potion.Title.GetFormattedText(),
        _ => throw new InvalidOperationException("Unsupported local model type.")
    };

    public int GetSupportedCardUpgradeLevel(object card) =>
        ((CardModel)card).MaxUpgradeLevel;

    public object CreateCardPreviewCopy(object card, int upgradeLevel)
    {
        CardModel copy = ((CardModel)card).ToMutable();
        for (int level = 0; level < upgradeLevel; level++)
        {
            copy.UpgradeInternal();
            copy.FinalizeUpgradeInternal();
        }
        return copy;
    }

    public LanConnectHoverTipPreviewData CreateRelicPreviewData(object relic)
    {
        RelicModel model = (RelicModel)relic;
        HoverTip hoverTip = model.HoverTip;
        return new LanConnectHoverTipPreviewData(
            "relic",
            hoverTip.Title ?? model.Title.GetFormattedText(),
            hoverTip.Description,
            model.Icon);
    }

    public LanConnectHoverTipPreviewData CreatePotionPreviewData(object potion)
    {
        PotionModel model = (PotionModel)potion;
        HoverTip hoverTip = model.HoverTip;
        return new LanConnectHoverTipPreviewData(
            "potion",
            hoverTip.Title ?? model.Title.GetFormattedText(),
            hoverTip.Description,
            model.Image);
    }

    private static bool TryGet<T>(object id, out object model) where T : AbstractModel
    {
        try
        {
            model = ModelDb.GetById<T>((ModelId)id);
            return true;
        }
        catch (ModelNotFoundException)
        {
            model = null!;
            return false;
        }
    }
}
