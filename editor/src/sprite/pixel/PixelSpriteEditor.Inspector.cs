//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class PixelSpriteEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId BrushColor { get; }
        public static partial WidgetId FilterDropDown { get; }
    }

    public override void InspectorUI()
    {
        SpriteInspectorUI();
        EdgesInspectorUI();
    }

    private void SpriteInspectorUI()
    {
        using var _ = Inspector.BeginSection("SPRITE");
        if (Inspector.IsSectionCollapsed) return;

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
