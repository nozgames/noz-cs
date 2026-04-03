//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
// TODO: Migrate popup system to UI.PopupMenu and delete this file

using System.Net;
using System.Numerics;

namespace NoZ.Editor;

internal static partial class EditorUI
{
    private static partial class ElementId
    {
        public static partial WidgetId Popup { get; }
        public static partial WidgetId PopupItem { get; }
    }

    private static bool _controlHovered = false;
    private static bool _controlSelected = false;
    private static bool _controlDisabled = false;
    private static WidgetId _popupId;
    private static WidgetId _nextPopupItemId;

    private static void SetState(bool selected, bool disabled)
    {
        _controlHovered = UI.IsHovered();
        _controlSelected = selected;
        _controlDisabled = disabled;
    }

    private static void ClearState()
    {
        _controlDisabled = false;
        _controlHovered = false;
        _controlSelected = false;
    }

    // --- Popup System ---

    public static void OpenPopup(WidgetId id) => _popupId = id;

    public static void ClosePopup() => _popupId = WidgetId.None;

    public static void TogglePopup(WidgetId id) =>
        _popupId = _popupId == id ? WidgetId.None : id;

    public static bool IsPopupOpen(WidgetId id) => _popupId == id;

    public static bool Popup(WidgetId id, Action content, PopupStyle? style = null, Vector2 offset = default)
    {
        if (_popupId != id) return false;

        _nextPopupItemId = ElementId.PopupItem;

        var anchorRect = UI.GetElementWorldRect(id).Translate(offset);
        using var _ = UI.BeginPopup(
            ElementId.Popup,
            style ?? new PopupStyle
            {
                AnchorX = Align.Min,
                AnchorY = Align.Min,
                PopupAlignX = Align.Min,
                PopupAlignY = Align.Max,
                Spacing = 2,
                ClampToScreen = true,
                AnchorRect = anchorRect,
                MinWidth = anchorRect.Width
            });

        if (UI.IsClosed())
        {
            _popupId = WidgetId.None;
            return false;
        }

        using var __ = UI.BeginColumn(EditorStyle.Popup.Root);

        content.Invoke();

        return _popupId == id;
    }

    // --- Popup Items ---

    public static bool PopupItem(string text, Action? content = null, bool selected = false, bool disabled = false) =>
        PopupItem(_nextPopupItemId++, text, content, selected, disabled);

    public static bool PopupItem(
        WidgetId id,
        string text,
        Action? content = null,
        bool selected = false,
        bool disabled = false) =>
        PopupItem(id, null, text, content, selected, disabled, showIcon: false);

    public static bool PopupItem(
        Sprite? icon,
        string text,
        Action? content = null,
        bool selected = false,
        bool disabled = false,
        bool showIcon = true) =>
        PopupItem(_nextPopupItemId++, icon, text, content, selected, disabled, showIcon);

    public static bool PopupItem(
        WidgetId id,
        Sprite? icon,
        string? text,
        Action? content = null,
        bool selected = false,
        bool disabled = false,
        bool showIcon = true,
        bool showChecked = true)
    {
        var pressed = false;
        using (UI.BeginContainer(id, EditorStyle.Popup.Item))
        {
            SetState(selected, disabled);

            if (_controlHovered)
                UI.Container(EditorStyle.Control.HoverFill);

            using (UI.BeginRow(EditorStyle.Popup.ItemContent))
            {
                if (showChecked)
                    ControlIcon(EditorStyle.Popup.CheckContent, selected ? EditorAssets.Sprites.IconCheck : null);
                if (showIcon)
                    ControlIcon(icon);
                if (text != null)
                    ControlText(text);

                content?.Invoke();
            }

            pressed = UI.WasPressed();
        }

        ClearState();

        return pressed;
    }

    // --- Control helpers (used by popup items and VFX CurveTypeButton) ---

    public static bool Control(
        WidgetId id,
        Action content,
        bool selected = false,
        bool disabled = false,
        bool toolbar = false,
        bool padding = true)
    {
        var pressed = false;
        var hovered = UI.IsHovered(id);
        using (UI.BeginContainer(id, hovered ? EditorStyle.Control.RootHovered : EditorStyle.Control.Root))
        {
            SetState(selected, disabled);

            using (UI.BeginRow())
                content.Invoke();

            pressed = !disabled && UI.WasPressed();
        }

        ClearState();

        return pressed;
    }

    public static void ControlText(string text)
    {
        UI.Text(
            text,
            style: _controlDisabled
                ? EditorStyle.Control.DisabledText
                : _controlSelected
                    ? EditorStyle.Control.SelectedText
                    : _controlHovered
                        ? EditorStyle.Control.HoveredText
                        : EditorStyle.Control.Text);
    }

    private static void ControlIcon(in ContainerStyle style, Sprite? icon)
    {
        using var _ = UI.BeginContainer(style);
        if (icon == null) return;

        UI.Image(
            icon,
            style: _controlDisabled
                ? EditorStyle.Control.DisabledIcon
                : _controlSelected
                    ? EditorStyle.Control.SelectedIcon
                    : _controlHovered
                        ? EditorStyle.Control.HoveredIcon
                        : EditorStyle.Control.Icon);
    }

    public static void ControlIcon(Sprite? icon) =>
        ControlIcon(new ContainerStyle { Width = EditorStyle.Control.Height, Height = EditorStyle.Control.Height }, icon);

    // --- Color Button ---

    public static bool ColorButton(WidgetId id, ref Color color, bool fillWidth=false)
    {
        ElementTree.BeginTree();
        ElementTree.BeginWidget(id);

        var flags = ElementTree.GetWidgetFlags();

        if (fillWidth)
            ElementTree.BeginSize(Size.Default, EditorStyle.Control.Height);
        else
            ElementTree.BeginSize(EditorStyle.Control.Height);

        if (color.A == 0)
        {
            ElementTree.BeginPadding(4);
            ElementTree.Image(EditorAssets.Sprites.IconNofill);
            ElementTree.EndPadding();
        }
        else
        {
            // HDR color?
            var maxComponent = MathF.Max(color.R, MathF.Max(color.G, color.B));
            if (maxComponent > 1f)
            {
                // HDR: gradient from base color to full intensity
                var baseColor = new Color(color.R / maxComponent, color.G / maxComponent, color.B / maxComponent);
                var brightColor = new Color(
                    MathF.Min(color.R, 1f),
                    MathF.Min(color.G, 1f),
                    MathF.Min(color.B, 1f));
                
                ElementTree.BeginRow();
                ElementTree.BeginFlex();
                ElementTree.Fill(new BackgroundStyle
                {
                    Color = baseColor,
                    GradientColor = brightColor,
                    GradientAngle = 0
                }, BorderRadius.Only(topLeft:EditorStyle.Control.BorderRadius, bottomLeft: EditorStyle.Control.BorderRadius));
                ElementTree.EndFlex();

                ElementTree.BeginFlex();
                ElementTree.BeginMargin(EdgeInsets.Left(-1));
                ElementTree.Fill(new BackgroundStyle
                {
                    Color = brightColor,
                    GradientColor = baseColor,
                    GradientAngle = 0
                }, BorderRadius.Only(bottomRight: EditorStyle.Control.BorderRadius, topRight: EditorStyle.Control.BorderRadius));
                ElementTree.EndMargin();
                ElementTree.EndFlex();
                ElementTree.EndRow();

                // Border
                ElementTree.Fill(Color.Transparent, EditorStyle.Control.BorderRadius, 4, EditorStyle.Palette.Control);
            }
            else
            {
                ElementTree.Fill(color.WithAlpha(1f), EditorStyle.Control.BorderRadius, 4, EditorStyle.Palette.Control);
            }

            ElementTree.BeginPadding(3.9f);
            ElementTree.BeginAlign(Align.Min, Align.Max);

            ElementTree.BeginSize(Size.Default, 4);
            ElementTree.Fill(Color.Black);
            ElementTree.EndSize();

            ElementTree.BeginSize(Size.Percent(color.A), 4);
            ElementTree.Fill(Color.White);
            ElementTree.EndSize();

            ElementTree.EndAlign();
            ElementTree.EndPadding();
        }

        ElementTree.EndTree();

        if (flags.HasFlag(WidgetFlags.Pressed))
            ColorPicker.Open(id, color);

        var prev = color;
        ColorPicker.Popup(id, ref color);
        UI.SetLastElement(id);

        return color != prev;
    }

    // --- Sprite Button ---

    private static WidgetId _spritePickerId;
    private static SpriteDocument? _spritePickerResult;
    private static bool _spritePickerHasResult;

    public static DocumentRef<SpriteDocument> SpriteButton(
        WidgetId id,
        DocumentRef<SpriteDocument> value)
    {
        // Render button
        ElementTree.BeginTree();
        ElementTree.BeginWidget(id);

        var flags = ElementTree.GetWidgetFlags();
        var hovered = flags.HasFlag(WidgetFlags.Hovered);

        var bg = hovered ? EditorStyle.Palette.Active : EditorStyle.Palette.Control;
        ElementTree.BeginSize(Size.Default, EditorStyle.Control.Height);
        ElementTree.BeginFill(bg, EditorStyle.Control.BorderRadius);
        ElementTree.BeginPadding(EdgeInsets.LeftRight(6));
        ElementTree.BeginRow(EditorStyle.Control.Spacing);

        var sprite = value.Value?.Sprite;
        if (sprite != null)
            ElementTree.Image(sprite, EditorStyle.Control.IconSize, ImageStretch.Uniform, Color.White, 1.0f, new Align2(Align.Center, Align.Center));
        else
            ElementTree.Image(EditorAssets.Sprites.AssetIconSprite, EditorStyle.Icon.Size, ImageStretch.Uniform, EditorStyle.Palette.SecondaryText, 1.0f, new Align2(Align.Center, Align.Center));

        ElementTree.Text(value.Name ?? "None", UI.DefaultFont, EditorStyle.Control.TextSize, EditorStyle.Palette.Content, new Align2(Align.Min, Align.Center));

        ElementTree.EndTree();

        // Open palette on press
        if (flags.HasFlag(WidgetFlags.Pressed))
        {
            _spritePickerId = id;
            _spritePickerHasResult = false;
            UI.SetHot(id, value.GetHashCode());
            AssetPalette.OpenSprites(onPicked: doc =>
            {
                _spritePickerResult = doc as SpriteDocument;
                _spritePickerHasResult = true;
            });
        }

        // Clear hot if palette closed without picking
        if (_spritePickerId == id && !_spritePickerHasResult && !AssetPalette.IsOpen)
        {
            _spritePickerId = WidgetId.None;
            UI.ClearHot();
        }

        UI.SetLastElement(id);

        // Return picked result
        if (_spritePickerHasResult && _spritePickerId == id)
        {
            _spritePickerHasResult = false;
            _spritePickerId = WidgetId.None;
            var result = new DocumentRef<SpriteDocument> { Value = _spritePickerResult, Name = _spritePickerResult?.Name };
            UI.NotifyChanged(result.GetHashCode());
            return result;
        }

        return value;
    }

    public static void PanelSeparator()
    {
        if (UI.IsRow())
            UI.Container(new ContainerStyle { Width = 1, Background = EditorStyle.Palette.Separator });
        else
            UI.Container(new ContainerStyle { Height = 1, Background = EditorStyle.Palette.Separator });
    }

    public static void SortOrderDropDown(WidgetId id, string? sortOrderId, Action<string?> onChanged)
    {
        var config = EditorApplication.Config;
        var label = "None";
        if (config != null && !string.IsNullOrEmpty(sortOrderId) && config.TryGetSortOrder(sortOrderId, out var current))
            label = current.Label;

        UI.DropDown(id, () =>
        {
            var items = new List<PopupMenuItem>();
            if (config != null)
            {
                foreach (var def in config.SortOrders)
                {
                    var defId = def.Id;
                    items.Add(new PopupMenuItem { Label = $"{def.Label} {def.SortOrderLabel}", Handler = () => onChanged(defId) });
                }
            }
            items.Add(new PopupMenuItem { Label = "None", Handler = () => onChanged(null) });
            return [.. items];
        }, label);
    }
}
