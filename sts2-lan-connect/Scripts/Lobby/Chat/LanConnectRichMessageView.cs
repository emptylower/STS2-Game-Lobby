using System.Text;
using Godot;
using MegaCrit.Sts2.addons.mega_text;

namespace Sts2LanConnect.Scripts;

internal sealed record LanConnectRichMessageReference(
    LanConnectChatSegment Segment,
    LanConnectResolvedItem? Item,
    LanConnectResolvedCombatReference? Combat);

internal readonly record struct LanConnectRichMessageSpan(
    string DisplayText,
    string CopyText,
    string AccessibleText,
    Color Color,
    Texture2D? Texture,
    LanConnectRichMessageReference? Reference);

internal sealed partial class LanConnectRichMessageView : MarginContainer
{
    internal const string ViewName = "ChatRichMessageView";
    internal const string LabelName = "ChatRichMessageText";

    private readonly Dictionary<string, LanConnectRichMessageReference> _references = new(StringComparer.Ordinal);
    private readonly MegaRichTextLabel _label;
    private readonly string _copyText;
    private readonly IReadOnlyList<LanConnectRichMessageSpan> _spans;
    private bool _rendered;

    internal LanConnectRichMessageView(
        IReadOnlyList<LanConnectRichMessageSpan> spans,
        int fontSize,
        Font? font = null)
    {
        Name = ViewName;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Pass;

        _spans = spans.ToArray();
        _copyText = string.Concat(_spans.Select(span => span.CopyText));
        _label = new MegaRichTextLabel
        {
            Name = LabelName,
            BbcodeEnabled = false,
            AutoSizeEnabled = false,
            FitContent = true,
            ScrollActive = false,
            SelectionEnabled = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(0f, 22f)
        };
        _label.AddThemeFontOverride("normal_font", font ?? ThemeDB.FallbackFont);
        _label.AddThemeFontSizeOverride("normal_font_size", fontSize);
        AddChild(_label);

        StringBuilder accessible = new();
        List<string> referenceKeys = new();
        for (int index = 0; index < _spans.Count; index++)
        {
            LanConnectRichMessageSpan span = _spans[index];
            if (!string.IsNullOrEmpty(span.AccessibleText))
            {
                accessible.Append(span.AccessibleText);
            }

            string? referenceKey = null;
            if (span.Reference != null)
            {
                referenceKey = $"ref-{index}";
                _references[referenceKey] = span.Reference;
                referenceKeys.Add(referenceKey);
            }
        }

        _label.AccessibilityName = accessible.ToString();
        SetMeta("lan_connect_copy_text", _copyText);
        SetMeta("lan_connect_reference_keys", string.Join(',', referenceKeys));
        SetMeta("lan_connect_reference_count", referenceKeys.Count);

        _label.MetaHoverStarted += OnMetaHoverStarted;
        _label.MetaHoverEnded += OnMetaHoverEnded;
        _label.MetaClicked += OnMetaClicked;
        _label.GuiInput += OnGuiInput;
    }

    internal event Action<Control, LanConnectRichMessageReference>? ReferenceHoverStarted;
    internal event Action? ReferenceHoverEnded;
    internal event Action<Control, LanConnectRichMessageReference>? ReferencePressed;

    internal string CopyText => _copyText;

    public override void _Ready()
    {
        if (_rendered)
        {
            return;
        }

        _rendered = true;
        _label.Clear();
        for (int index = 0; index < _spans.Count; index++)
        {
            LanConnectRichMessageSpan span = _spans[index];
            bool hasReference = span.Reference != null;
            if (hasReference)
            {
                _label.PushMeta($"ref-{index}");
            }

            _label.PushColor(span.Color);
            if (span.Texture != null)
            {
                _label.AddImage(span.Texture, 20, 20, span.Color);
            }
            else
            {
                _label.AddText(span.DisplayText);
            }
            _label.Pop();

            if (hasReference)
            {
                _label.Pop();
            }
        }
    }

    private void OnMetaHoverStarted(Variant meta)
    {
        if (TryGetReference(meta, out LanConnectRichMessageReference reference))
        {
            ReferenceHoverStarted?.Invoke(_label, reference);
        }
    }

    private void OnMetaHoverEnded(Variant meta)
    {
        if (TryGetReference(meta, out _))
        {
            ReferenceHoverEnded?.Invoke();
        }
    }

    private void OnMetaClicked(Variant meta)
    {
        if (TryGetReference(meta, out LanConnectRichMessageReference reference))
        {
            ReferencePressed?.Invoke(_label, reference);
        }
    }

    private void OnGuiInput(InputEvent input)
    {
        bool openSingleReference = input switch
        {
            InputEventScreenTouch { Pressed: true } => true,
            InputEventKey
            {
                Pressed: true,
                Echo: false,
                Keycode: Key.Enter or Key.KpEnter
            } => true,
            InputEventJoypadButton
            {
                Pressed: true,
                ButtonIndex: JoyButton.A
            } => true,
            _ => false
        };
        if (openSingleReference && _references.Count == 1)
        {
            ReferencePressed?.Invoke(_label, _references.Values.Single());
            GetViewport().SetInputAsHandled();
            return;
        }

        if (input is not InputEventKey key ||
            !key.Pressed || key.Echo ||
            key.Keycode != Key.C ||
            (!key.CtrlPressed && !key.MetaPressed))
        {
            return;
        }

        DisplayServer.ClipboardSet(_copyText);
        GetViewport().SetInputAsHandled();
    }

    private bool TryGetReference(Variant meta, out LanConnectRichMessageReference reference)
    {
        string key = meta.AsString();
        if (_references.TryGetValue(key, out LanConnectRichMessageReference? found))
        {
            reference = found;
            return true;
        }

        reference = null!;
        return false;
    }
}
