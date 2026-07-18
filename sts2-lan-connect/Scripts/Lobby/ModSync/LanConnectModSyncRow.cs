using Godot;

namespace Sts2LanConnect.Scripts;

internal sealed partial class LanConnectModSyncRow : PanelContainer
{
    private static readonly Color CardColor = new(0.99f, 0.97f, 0.93f, 1f);
    private static readonly Color SurfaceMutedColor = new(0.89f, 0.87f, 0.81f, 1f);
    private static readonly Color BorderColor = new(0.28f, 0.16f, 0.08f, 1f);
    private static readonly Color TextStrongColor = new(0.21f, 0.10f, 0.04f, 1f);
    private static readonly Color TextMutedColor = new(0.42f, 0.34f, 0.25f, 1f);

    private readonly LanConnectModSyncRowState _state;
    private CheckBox? _selector;

    internal LanConnectModSyncRow(LanConnectModSyncRowState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        Name = "ModSyncRow_" + SanitizeName(state.Descriptor.Id);
        CustomMinimumSize = new Vector2(0f, state.Job == null ? 68f : 88f);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = false;
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", PixelStyle(
            state.Selectable ? CardColor : SurfaceMutedColor,
            BorderColor,
            borderWidth: 2,
            padding: 10));
    }

    internal event Action<LanConnectModSyncRow, bool>? SelectionChanged;

    internal LobbyModDescriptor Descriptor => _state.Descriptor;

    internal bool Selected => _selector?.ButtonPressed == true;

    public override void _Ready()
    {
        HBoxContainer layout = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        layout.AddThemeConstantOverride("separation", 10);
        AddChild(layout);

        if (_state.Selectable)
        {
            _selector = new CheckBox
            {
                Name = "ModSyncSelect_" + SanitizeName(_state.Descriptor.Id),
                ButtonPressed = _state.Selected,
                FocusMode = FocusModeEnum.All,
                AccessibilityName = $"选择禁用 MOD {_state.Descriptor.Id}",
                TooltipText = $"选择后将在二次确认时禁用 {_state.Descriptor.Id}",
                CustomMinimumSize = new Vector2(42f, 42f),
                SizeFlagsVertical = SizeFlags.ShrinkCenter
            };
            _selector.Toggled += selected => SelectionChanged?.Invoke(this, selected);
            layout.AddChild(_selector);
        }

        VBoxContainer text = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        text.AddThemeConstantOverride("separation", 3);
        layout.AddChild(text);

        string visibleTitle = _state.Job?.Metadata.Title is { Length: > 0 } jobTitle
            ? jobTitle
            : _state.Metadata?.Title is { Length: > 0 } metadataTitle
                ? metadataTitle
                : _state.Descriptor.Id;
        Label title = LabelFor(
            LimitVisibleText(visibleTitle, 36),
            17,
            TextStrongColor);
        title.Name = "ModSyncRowTitle";
        title.AccessibilityName = $"MOD {_state.Descriptor.Id}";
        text.AddChild(title);

        string source = _state.Descriptor.Source == LanConnectModSources.SteamWorkshop
            ? $"Steam Workshop {_state.Descriptor.WorkshopFileId}"
            : "本地 MOD";
        string detailText = $"{_state.Descriptor.Id}  ·  {source}  ·  房主版本 {_state.Descriptor.Version}";
        LanConnectWorkshopMetadata? metadata = _state.Metadata ?? _state.Job?.Metadata;
        if (metadata != null)
        {
            detailText += $"  ·  发布者 {metadata.Publisher}";
        }
        if (_state.Job != null)
        {
            detailText += $"  ·  {DescribeJob(_state.Job)}";
        }
        Label detail = LabelFor(LimitVisibleText(detailText, 52), 13, TextMutedColor);
        detail.Name = "ModSyncRowDetail";
        detail.AccessibilityName = detailText;
        detail.TooltipText = detailText;
        text.AddChild(detail);

        if (_state.Job != null)
        {
            ProgressBar progress = new()
            {
                Name = "ModSyncRowProgress",
                MinValue = 0,
                MaxValue = Math.Max(1d, _state.Job.BytesTotal),
                Value = _state.Job.BytesDownloaded,
                ShowPercentage = _state.Job.BytesTotal > 0,
                CustomMinimumSize = new Vector2(0f, 12f),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                AccessibilityName = $"{_state.Descriptor.Id} 下载进度"
            };
            text.AddChild(progress);
        }
    }

    internal void SetSelectedForTests(bool selected)
    {
        if (_selector == null)
        {
            throw new InvalidOperationException("Row is not selectable or is not ready.");
        }
        _selector.ButtonPressed = selected;
    }

    private static Label LabelFor(string value, int size, Color color)
    {
        Label label = new()
        {
            Text = value,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.CustomMinimumSize = new Vector2(0f, size + 6f);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private static string DescribeJob(LanConnectWorkshopJobSnapshot job)
    {
        if (job.State == LanConnectWorkshopJobState.Downloading && job.BytesTotal > 0)
        {
            double percent = Math.Clamp(job.BytesDownloaded * 100d / job.BytesTotal, 0d, 100d);
            return $"下载 {percent:0}%";
        }
        return job.State switch
        {
            LanConnectWorkshopJobState.Pending => "等待中",
            LanConnectWorkshopJobState.Validating => "正在验证",
            LanConnectWorkshopJobState.Subscribing => "正在订阅",
            LanConnectWorkshopJobState.Downloading => "正在下载",
            LanConnectWorkshopJobState.WaitingInstall => "正在验证安装",
            LanConnectWorkshopJobState.Installed => "已安装",
            LanConnectWorkshopJobState.Failed => "失败，可重试",
            LanConnectWorkshopJobState.TimedOut => "已超时，可重试",
            LanConnectWorkshopJobState.Canceled => "已取消",
            _ => job.State.ToString()
        };
    }

    private static string SanitizeName(string value)
    {
        string result = new(value.Where(character => char.IsLetterOrDigit(character) || character is '_' or '-').Take(48).ToArray());
        return string.IsNullOrEmpty(result) ? "item" : result;
    }

    private static string LimitVisibleText(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters
            ? value
            : value[..Math.Max(1, maximumCharacters - 1)] + "…";

    internal static StyleBoxFlat PixelStyle(
        Color background,
        Color border,
        int borderWidth,
        int padding,
        int shadowSize = 0)
    {
        StyleBoxFlat style = new()
        {
            BgColor = background,
            BorderColor = border,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            ContentMarginLeft = padding,
            ContentMarginTop = padding,
            ContentMarginRight = padding,
            ContentMarginBottom = padding,
            ShadowColor = new Color(0.12f, 0.06f, 0.02f, 0.24f),
            ShadowSize = shadowSize
        };
        return style;
    }
}
