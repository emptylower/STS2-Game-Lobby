using Godot;
using MegaCrit.Sts2.Core.Helpers;

namespace Sts2LanConnect.Scripts;

internal sealed partial class LanConnectLanHostModePicker : LanConnectLobbyChoiceDialog
{
    private Func<LanConnectLanHostModeAvailability>? _availability;
    private Func<LanConnectLanHostMode, Task>? _onSelected;
    private bool _selectionInFlight;
    private bool _exitedTree;

    public LanConnectLanHostModePicker()
    {
        Visible = false;
        ChoiceSelected += OnChoiceSelected;
    }

    internal void Initialize(
        Func<LanConnectLanHostModeAvailability> availability,
        Func<LanConnectLanHostMode, Task> onSelected)
    {
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(onSelected);
        _availability = availability;
        _onSelected = onSelected;
    }

    internal new void Open()
    {
        if (_availability == null || _onSelected == null ||
            _selectionInFlight || _exitedTree || !IsInsideTree())
        {
            return;
        }

        LanConnectLanHostModeAvailability availability = _availability();
        Configure(
            "LAN 调试建房",
            "选择要启动的游戏模式。不可用的模式会保持禁用。",
            LanConnectLanHostModeSelection.Options
                .Select(option => new LanConnectLobbyDialogChoice(
                    option.Id,
                    option.Label,
                    Describe(option.Mode),
                    IsAvailable(option.Mode, availability),
                    Primary: option.Mode == LanConnectLanHostMode.Standard))
                .ToArray(),
            "返回");
        base.Open();
    }

    public override void _EnterTree()
    {
        _exitedTree = false;
    }

    public override void _ExitTree()
    {
        _exitedTree = true;
        base._ExitTree();
    }

    private void OnChoiceSelected(int id)
    {
        if (_selectionInFlight || _exitedTree ||
            _availability == null || _onSelected == null ||
            !LanConnectLanHostModeSelection.TryResolve(
                id,
                _availability(),
                out LanConnectLanHostMode mode))
        {
            return;
        }

        _selectionInFlight = true;
        TaskHelper.RunSafely(StartSelectedModeAsync(mode));
    }

    private async Task StartSelectedModeAsync(LanConnectLanHostMode mode)
    {
        try
        {
            await _onSelected!(mode);
        }
        finally
        {
            _selectionInFlight = false;
        }
    }

    private static bool IsAvailable(
        LanConnectLanHostMode mode,
        LanConnectLanHostModeAvailability availability) => mode switch
    {
        LanConnectLanHostMode.Standard => availability.Standard,
        LanConnectLanHostMode.Daily => availability.Daily,
        LanConnectLanHostMode.Custom => availability.Custom,
        _ => false
    };

    private static string Describe(LanConnectLanHostMode mode) => mode switch
    {
        LanConnectLanHostMode.Standard => "按标准规则创建 ENet LAN Host。",
        LanConnectLanHostMode.Daily => "启动多人每日挑战的 LAN Host。",
        LanConnectLanHostMode.Custom => "进入自定义规则设置后创建 LAN Host。",
        _ => string.Empty
    };
}
