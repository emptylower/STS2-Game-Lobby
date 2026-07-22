using Sts2LanConnect.Scripts;

namespace Sts2LanConnect.Tests.Lobby.Chat;

public sealed class LanConnectPowerHoverTipResolverTests
{
    [Fact]
    public void Smart_description_receives_the_complete_runtime_context_and_dynamic_vars()
    {
        FakePort port = new();
        LanConnectPowerHoverTipResolver resolver = new(port);

        LanConnectHoverTipPreviewData? preview = resolver.Resolve(
            port.Model,
            new LanConnectPowerDescriptionContext(
                Amount: 4,
                OnPlayer: true,
                IsMultiplayer: true,
                PlayerCount: 2,
                OwnerName: "Ironclad",
                ApplierName: "Silent",
                TargetName: "Hexaghost"));

        Assert.NotNull(preview);
        Assert.Equal("power", preview.ItemType);
        Assert.Equal("Catalyst", preview.Title);
        Assert.Contains("Amount=4", preview.Description, StringComparison.Ordinal);
        Assert.Contains("OnPlayer=True", preview.Description, StringComparison.Ordinal);
        Assert.Contains("IsMultiplayer=True", preview.Description, StringComparison.Ordinal);
        Assert.Contains("PlayerCount=2", preview.Description, StringComparison.Ordinal);
        Assert.Contains("OwnerName=Ironclad", preview.Description, StringComparison.Ordinal);
        Assert.Contains("ApplierName=Silent", preview.Description, StringComparison.Ordinal);
        Assert.Contains("TargetName=Hexaghost", preview.Description, StringComparison.Ordinal);
        Assert.Contains("energyPrefix=green", preview.Description, StringComparison.Ordinal);
        Assert.Contains("singleStarIcon=[img]res://images/packed/sprite_fonts/star_icon.png[/img]",
            preview.Description,
            StringComparison.Ordinal);
        Assert.True(port.DynamicVarsAdded);
        Assert.Equal(0, port.DumbCalls);
    }

    [Fact]
    public void Missing_or_throwing_smart_members_fall_back_to_complete_dumb_hover_tip()
    {
        FakePort port = new() { ThrowOnDynamicVars = true };
        LanConnectPowerHoverTipResolver resolver = new(port);

        LanConnectHoverTipPreviewData? preview = resolver.Resolve(
            port.Model,
            new LanConnectPowerDescriptionContext(4, true, true, 2, "Ironclad", "Silent", ""));

        Assert.NotNull(preview);
        Assert.Equal("Catalyst", preview.Title);
        Assert.Equal("Dumb Catalyst amount 4", preview.Description);
        Assert.Equal(1, port.DumbCalls);
    }

    private sealed class FakePort : ILanConnectPowerHoverTipPort
    {
        internal object Model { get; } = new();
        internal bool ThrowOnDynamicVars { get; init; }
        internal bool DynamicVarsAdded { get; private set; }
        internal int DumbCalls { get; private set; }

        public bool HasSmartDescription(object model) => true;
        public object CreateSmartDescription(object model) => new Dictionary<string, object>();

        public void AddDecimal(object description, string name, decimal value) =>
            ((Dictionary<string, object>)description)[name] = value;

        public void AddBoolean(object description, string name, bool value) =>
            ((Dictionary<string, object>)description)[name] = value;

        public void AddString(object description, string name, string value) =>
            ((Dictionary<string, object>)description)[name] = value;

        public void AddDynamicVars(object model, object description)
        {
            if (ThrowOnDynamicVars)
            {
                throw new MissingMemberException("DynamicVars");
            }
            DynamicVarsAdded = true;
        }

        public string Format(object description) => string.Join(
            ';',
            ((Dictionary<string, object>)description)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));

        public string GetTitle(object model) => "Catalyst";
        public object? GetIcon(object model) => null;
        public bool IsDebuff(object model, int amount) => true;
        public string GetEnergyPrefix(object model) => "green";

        public LanConnectHoverTipPreviewData GetDumbHoverTip(object model, int amount)
        {
            DumbCalls++;
            return new LanConnectHoverTipPreviewData(
                "power",
                "Catalyst",
                $"Dumb Catalyst amount {amount}",
                null,
                IsDebuff: true);
        }
    }
}
