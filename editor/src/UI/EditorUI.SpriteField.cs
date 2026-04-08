//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal static partial class EditorUI
{
    private static WidgetId _spritePickerId;
    private static SpriteDocument? _spritePickerResult;
    private static bool _spritePickerHasResult;

    public static DocumentRef<SpriteDocument> SpriteField(
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
}
