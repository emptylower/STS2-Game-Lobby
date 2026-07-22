using System.Reflection;

namespace Sts2LanConnect.Scripts;

internal readonly record struct LanConnectPowerDescriptionContext(
    int Amount,
    bool OnPlayer,
    bool IsMultiplayer,
    int PlayerCount,
    string OwnerName,
    string ApplierName,
    string TargetName);

internal interface ILanConnectPowerHoverTipPort
{
    bool HasSmartDescription(object model);
    object CreateSmartDescription(object model);
    void AddDecimal(object description, string name, decimal value);
    void AddBoolean(object description, string name, bool value);
    void AddString(object description, string name, string value);
    void AddDynamicVars(object model, object description);
    string Format(object description);
    string GetTitle(object model);
    object? GetIcon(object model);
    bool IsDebuff(object model, int amount);
    string GetEnergyPrefix(object model);
    LanConnectHoverTipPreviewData GetDumbHoverTip(object model, int amount);
}

internal sealed class LanConnectPowerHoverTipResolver
{
    private const string SingleStarIcon =
        "[img]res://images/packed/sprite_fonts/star_icon.png[/img]";

    private readonly ILanConnectPowerHoverTipPort _port;

    internal LanConnectPowerHoverTipResolver()
        : this(new LanConnectProductionPowerHoverTipPort())
    {
    }

    internal LanConnectPowerHoverTipResolver(ILanConnectPowerHoverTipPort port)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
    }

    internal LanConnectHoverTipPreviewData? Resolve(
        object model,
        LanConnectPowerDescriptionContext context)
    {
        ArgumentNullException.ThrowIfNull(model);
        try
        {
            if (_port.HasSmartDescription(model))
            {
                object smartDescription = _port.CreateSmartDescription(model);
                _port.AddDecimal(smartDescription, "Amount", context.Amount);
                _port.AddBoolean(smartDescription, "OnPlayer", context.OnPlayer);
                _port.AddBoolean(smartDescription, "IsMultiplayer", context.IsMultiplayer);
                _port.AddDecimal(smartDescription, "PlayerCount", context.PlayerCount);
                _port.AddString(smartDescription, "OwnerName", context.OwnerName);
                _port.AddString(smartDescription, "ApplierName", context.ApplierName);
                _port.AddString(smartDescription, "TargetName", context.TargetName);
                _port.AddString(smartDescription, "singleStarIcon", SingleStarIcon);
                _port.AddString(
                    smartDescription,
                    "energyPrefix",
                    _port.GetEnergyPrefix(model));
                _port.AddDynamicVars(model, smartDescription);
                string description = _port.Format(smartDescription);
                string title = _port.GetTitle(model);
                if (!string.IsNullOrWhiteSpace(title) &&
                    !string.IsNullOrWhiteSpace(description))
                {
                    return new LanConnectHoverTipPreviewData(
                        "power",
                        title,
                        description,
                        _port.GetIcon(model),
                        _port.IsDebuff(model, context.Amount));
                }
            }
        }
        catch
        {
            // Constrained API drift or a model-specific variable failure uses the
            // game's complete dumb hover tip instead of a partial smart string.
        }

        try
        {
            LanConnectHoverTipPreviewData fallback = _port.GetDumbHoverTip(
                model,
                context.Amount);
            return string.IsNullOrWhiteSpace(fallback.Title) ||
                   string.IsNullOrWhiteSpace(fallback.Description)
                ? null
                : fallback;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class LanConnectProductionPowerHoverTipPort : ILanConnectPowerHoverTipPort
{
    public bool HasSmartDescription(object model) =>
        ReadRequiredProperty<bool>(model, "HasSmartDescription");

    public object CreateSmartDescription(object model) =>
        ReadRequiredProperty<object>(model, "SmartDescription");

    public void AddDecimal(object description, string name, decimal value) =>
        InvokeLocStringAdd(description, name, value, typeof(decimal));

    public void AddBoolean(object description, string name, bool value) =>
        InvokeLocStringAdd(description, name, value, typeof(bool));

    public void AddString(object description, string name, string value) =>
        InvokeLocStringAdd(description, name, value, typeof(string));

    public void AddDynamicVars(object model, object description)
    {
        object dynamicVars = ReadRequiredProperty<object>(model, "DynamicVars");
        MethodInfo addTo = dynamicVars.GetType().GetMethod(
            "AddTo",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            [description.GetType()],
            modifiers: null) ?? throw new MissingMethodException(dynamicVars.GetType().FullName, "AddTo");
        addTo.Invoke(dynamicVars, [description]);
    }

    public string Format(object description) =>
        InvokeRequired(description, "GetFormattedText") as string ?? string.Empty;

    public string GetTitle(object model)
    {
        object title = ReadRequiredProperty<object>(model, "Title");
        return InvokeRequired(title, "GetFormattedText") as string ?? string.Empty;
    }

    public object? GetIcon(object model) =>
        model.GetType().GetProperty("Icon", BindingFlags.Instance | BindingFlags.Public)?
            .GetValue(model);

    public bool IsDebuff(object model, int amount)
    {
        MethodInfo? forAmount = model.GetType().GetMethod(
            "GetTypeForAmount",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            [typeof(decimal)],
            modifiers: null);
        object? type = forAmount?.Invoke(model, [(decimal)amount]) ??
                       model.GetType().GetProperty(
                           "Type",
                           BindingFlags.Instance | BindingFlags.Public)?.GetValue(model);
        return string.Equals(type?.ToString(), "Debuff", StringComparison.Ordinal);
    }

    public string GetEnergyPrefix(object model)
    {
        Type helper = model.GetType().Assembly.GetType(
            "MegaCrit.Sts2.Core.Helpers.EnergyIconHelper",
            throwOnError: true)!;
        MethodInfo getPrefix = helper.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(method =>
                method.Name == "GetPrefix" &&
                method.GetParameters() is [{ ParameterType: var parameterType }] &&
                parameterType.IsInstanceOfType(model));
        return getPrefix.Invoke(null, [model]) as string ?? string.Empty;
    }

    public LanConnectHoverTipPreviewData GetDumbHoverTip(object model, int amount)
    {
        MethodInfo? getDumb = model.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(method =>
                method.Name == "GetDumbHoverTip" &&
                method.GetParameters().Length == 1);
        object hoverTip = getDumb?.Invoke(model, [amount]) ??
                          ReadRequiredProperty<object>(model, "DumbHoverTip");
        string title = ReadOptionalProperty<string>(hoverTip, "Title") ?? GetTitle(model);
        string description = ReadRequiredProperty<string>(hoverTip, "Description");
        object? icon = ReadOptionalProperty<object>(hoverTip, "Icon");
        bool isDebuff = ReadOptionalProperty<bool?>(hoverTip, "IsDebuff") ?? IsDebuff(model, amount);
        return new LanConnectHoverTipPreviewData(
            "power",
            title,
            description,
            icon,
            isDebuff);
    }

    private static void InvokeLocStringAdd(
        object description,
        string name,
        object value,
        Type valueType)
    {
        MethodInfo add = description.GetType().GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            [typeof(string), valueType],
            modifiers: null) ?? throw new MissingMethodException(description.GetType().FullName, "Add");
        add.Invoke(description, [name, value]);
    }

    private static object? InvokeRequired(object target, string methodName) =>
        target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            Type.EmptyTypes,
            modifiers: null)?.Invoke(target, null) ??
        throw new MissingMethodException(target.GetType().FullName, methodName);

    private static T ReadRequiredProperty<T>(object target, string propertyName)
    {
        object? value = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
        return value is T typed
            ? typed
            : throw new MissingMemberException(target.GetType().FullName, propertyName);
    }

    private static T? ReadOptionalProperty<T>(object target, string propertyName)
    {
        object? value = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
        return value is T typed ? typed : default;
    }
}
