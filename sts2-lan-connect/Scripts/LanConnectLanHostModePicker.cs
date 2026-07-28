using Godot;
using MegaCrit.Sts2.Core.Helpers;

namespace Sts2LanConnect.Scripts;

internal sealed partial class LanConnectLanHostModePicker : PopupMenu
{
    private Func<LanConnectLanHostModeAvailability>? _availability;
    private Func<LanConnectLanHostMode, Task>? _onSelected;
    private bool _selectionInFlight;
    private bool _exitedTree;

    public LanConnectLanHostModePicker()
    {
        Exclusive = true;
        foreach (LanConnectLanHostModeOption option in LanConnectLanHostModeSelection.Options)
        {
            AddItem(option.Label, option.Id);
        }

        IdPressed += OnIdPressed;
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

    internal void Open()
    {
        if (_availability == null || _onSelected == null ||
            _selectionInFlight || _exitedTree || !IsInsideTree())
        {
            return;
        }

        LanConnectLanHostModeAvailability availability = _availability();
        int firstAvailableIndex = -1;
        for (int index = 0; index < ItemCount; index++)
        {
            bool available = LanConnectLanHostModeSelection.TryResolve(
                GetItemId(index),
                availability,
                out _);
            SetItemDisabled(index, !available);
            if (available && firstAvailableIndex < 0)
            {
                firstAvailableIndex = index;
            }
        }

        PopupCentered(new Vector2I(420, 0));
        SetFocusedItem(firstAvailableIndex);
    }

    public override void _EnterTree()
    {
        _exitedTree = false;
    }

    public override void _ExitTree()
    {
        _exitedTree = true;
        Hide();
    }

    private void OnIdPressed(long id)
    {
        if (!Visible || _selectionInFlight || _exitedTree ||
            _availability == null || _onSelected == null ||
            !LanConnectLanHostModeSelection.TryResolve(
                id,
                _availability(),
                out LanConnectLanHostMode mode))
        {
            return;
        }

        _selectionInFlight = true;
        Hide();
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
}
