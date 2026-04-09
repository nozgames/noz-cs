//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class PixelSpriteEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId BrushColor { get; }
        public static partial WidgetId PixelsPerUnit { get; }
        public static partial WidgetId FilterDropDown { get; }
    }

    public override void InspectorUI()
    {
        SpriteInspectorUI();
    }

    private void SpriteInspectorUI()
    {
        using var _ = Inspector.BeginSection("SPRITE");
        if (Inspector.IsSectionCollapsed) return;

        using (Inspector.BeginProperty("Pixels Per Unit"))
        {
            var current = Document.PixelsPerUnitOverride ?? 32;
            var label = Document.PixelsPerUnitOverride.HasValue ? $"{current}" : $"{current} (Default)";
            UI.DropDown(WidgetIds.PixelsPerUnit, () =>
            [
                ..new[] { 8, 16, 32, 64, 128 }.Select(v => new PopupMenuItem
                {
                    Label = v == 32 ? "32 (Default)" : $"{v}",
                    Handler = () =>
                    {
                        Undo.Record(Document);
                        Document.PixelsPerUnitOverride = v == 32 ? null : v;
                        Document.UpdateBounds();
                        InvalidateComposite();
                    }
                })
            ], label);
        }

        using (Inspector.BeginProperty("Filter"))
        {
            var current = Document.TextureFilterOverride ?? TextureFilter.Point;
            var filterLabel = Document.TextureFilterOverride.HasValue
                ? $"{current}"
                : $"{current} (Default)";
            UI.DropDown(WidgetIds.FilterDropDown, () =>
            [
                new PopupMenuItem
                {
                    Label = "Point (Default)",
                    Handler = () =>
                    {
                        Undo.Record(Document);
                        Document.TextureFilterOverride = null;
                        Document.MarkSpriteDirty();
                        AssetManifest.IsModified = true;
                        InvalidateComposite();
                    }
                },
                new PopupMenuItem
                {
                    Label = "Linear",
                    Handler = () =>
                    {
                        Undo.Record(Document);
                        Document.TextureFilterOverride = TextureFilter.Linear;
                        Document.MarkSpriteDirty();
                        AssetManifest.IsModified = true;
                        InvalidateComposite();
                    }
                }
            ], filterLabel);
        }
    }
}
