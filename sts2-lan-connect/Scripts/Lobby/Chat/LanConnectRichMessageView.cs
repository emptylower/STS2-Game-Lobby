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
    private readonly LanConnectReferenceTapGesture _tapGesture = new();
    private readonly LanConnectReferenceTouchMouseDeduplicator _touchMouseDeduplicator = new();
    private LanConnectRichMessageReference? _touchReference;
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
        _label.AddThemeFontOverride("normal_font", LanConnectLobbyFonts.WithChineseFallback(font ?? ThemeDB.FallbackFont));
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

        _label.SetMeta("lan_connect_reference_view_id", LanConnectReferenceDiagnostics.Identity(this));
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
        LanConnectReferenceDiagnostics.Shared.Write("render", "view_ready",
            ("view_id", LanConnectReferenceDiagnostics.Identity(this)), ("spans", _spans.Count),
            ("references", _references.Count), ("in_tree", IsInsideTree()), ("visible", IsVisibleInTree()),
            ("bounds", _label.GetGlobalRect()));
    }

    private void OnMetaHoverStarted(Variant meta)
    {
        TraceInput("meta_hover_started");
        if (!_tapGesture.IgnoreHover(Time.GetTicksMsec()) &&
            TryGetReference(meta, out LanConnectRichMessageReference reference))
        {
            ReferenceHoverStarted?.Invoke(_label, reference);
        }
    }

    private void OnMetaHoverEnded(Variant meta)
    {
        TraceInput("meta_hover_ended");
        if (TryGetReference(meta, out _))
        {
            ReferenceHoverEnded?.Invoke();
        }
    }

    private void OnMetaClicked(Variant meta)
    {
        TraceInput("meta_clicked");
        if (TryGetReference(meta, out LanConnectRichMessageReference reference))
        {
            if (_tapGesture.TouchInProgress)
            {
                _touchReference = reference;
                TraceInput("meta_waits_for_touch_release");
            }
            else if (_tapGesture.TryActivateReference(Time.GetTicksMsec()))
            {
                RaiseReferencePressed("meta", reference);
            }
            else
            {
                TraceInput("meta_suppressed");
            }
        }
        else
        {
            TraceInput("meta_without_reference");
        }
    }

    private void OnGuiInput(InputEvent input)
    {
        ulong now = Time.GetTicksMsec();
        if (input is InputEventScreenTouch touch)
        {
            LanConnectReferenceDiagnostics.Shared.Write("input", touch.Pressed ? "touch_press" : "touch_release",
                ("view_id", LanConnectReferenceDiagnostics.Identity(this)), ("references", _references.Count),
                ("device", touch.Device), ("index", touch.Index), ("position", touch.Position),
                ("cancelled", touch.Canceled), ("in_tree", IsInsideTree()),
                ("visible", IsVisibleInTree()), ("bounds", _label.GetGlobalRect()));
            _touchMouseDeduplicator.ObserveTouch(touch.Position, now);
            if (touch.Pressed)
            {
                _touchReference = null;
                _tapGesture.Press(touch.Index, touch.Position, now);
                ReferenceHoverEnded?.Invoke();
            }
            else
            {
                bool tapped = _tapGesture.Release(touch.Index, touch.Position, now, touch.Canceled);
                LanConnectRichMessageReference? reference = _references.Count == 1
                    ? _references.Values.Single()
                    : _touchReference;
                _touchReference = null;
                if (tapped && reference != null && _tapGesture.TryActivateReference(now))
                {
                    RaiseReferencePressed("touch_release", reference);
                    GetViewport().SetInputAsHandled();
                }
            }
            // Leave presses/drags available to the enclosing chat scroll container.
            return;
        }
        if (input is InputEventScreenDrag drag)
        {
            TraceInput("touch_drag");
            _tapGesture.Drag(drag.Index, drag.Position, now);
            return;
        }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } pointer)
        {
            LanConnectReferenceDiagnostics.Shared.Write("input", pointer.Pressed ? "mouse_press" : "mouse_release",
                ("view_id", LanConnectReferenceDiagnostics.Identity(this)), ("references", _references.Count),
                ("device", pointer.Device), ("position", pointer.Position),
                ("in_tree", IsInsideTree()), ("visible", IsVisibleInTree()), ("bounds", _label.GetGlobalRect()));
        }
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse &&
            !_touchMouseDeduplicator.IsSyntheticMouse(mouse.Position, now))
        {
            _tapGesture.UseMouse();
        }
        bool openSingleReference = input switch
        {
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
            RaiseReferencePressed(input.GetType().Name, _references.Values.Single());
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

    private void TraceInput(string reason) => LanConnectReferenceDiagnostics.Shared.Write("input", reason,
        ("view_id", LanConnectReferenceDiagnostics.Identity(this)), ("references", _references.Count),
        ("in_tree", IsInsideTree()), ("visible", IsVisibleInTree()), ("bounds", _label.GetGlobalRect()));

    private void RaiseReferencePressed(string source, LanConnectRichMessageReference reference)
    {
        LanConnectReferenceDiagnostics.Shared.Write("activate", "view_reference_pressed",
            ("view_id", LanConnectReferenceDiagnostics.Identity(this)), ("source", source),
            ("reference_id", reference.Item == null ? 0 : LanConnectReferenceDiagnostics.Identity(reference.Item)),
            ("model_id", (reference.Segment as LanConnectItemRefSegment)?.ModelId));
        ReferencePressed?.Invoke(_label, reference);
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
