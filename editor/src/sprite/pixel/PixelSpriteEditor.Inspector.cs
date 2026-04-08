//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class PixelSpriteEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId BrushColor { get; }
        public static partial WidgetId ExportToggle { get; }
        public static partial WidgetId ConstraintDropDown { get; }
        public static partial WidgetId SortOrder { get; }
        public static partial WidgetId SkeletonDropDown { get; }
        public static partial WidgetId BoneDropDown { get; }
        public static partial WidgetId PixelsPerUnit { get; }
    }

    public override void InspectorUI()
    {
        SpriteInspectorUI();
    }

    private void SpriteInspectorUI()
    {
        using var _ = Inspector.BeginSection("SPRITE");
        if (Inspector.IsSectionCollapsed) return;

        using (Inspector.BeginProperty("Export"))
        {
            var shouldExport = Document.ShouldExport;
            if (UI.Toggle(WidgetIds.ExportToggle, shouldExport, EditorStyle.Inspector.Toggle))
            {
                shouldExport = !shouldExport;
                Undo.Record(Document);
                Document.ShouldExport = shouldExport;
                AssetManifest.IsModified = true;
            }
        }

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

        using (Inspector.BeginProperty("Size"))
        {
            var sizes = EditorApplication.Config.SpriteSizes;
            var constraintLabel = "Auto";
            if (Document.ConstrainedSize.HasValue)
                for (int i = 0; i < sizes.Length; i++)
                    if (Document.ConstrainedSize.Value == sizes[i].Size)
                    {
                        constraintLabel = sizes[i].Label;
                        break;
                    }

            UI.DropDown(WidgetIds.ConstraintDropDown, () => [
                ..EditorApplication.Config.SpriteSizes.Select(s =>
                new PopupMenuItem { Label = s.Label, Handler = () => SetConstraint(s.Size) }
            ),
            new PopupMenuItem { Label = "Auto", Handler = () => SetConstraint(null)}
            ], constraintLabel, EditorAssets.Sprites.IconConstraint);
        }

        using (Inspector.BeginProperty("Sort Order"))
        {
            EditorUI.SortOrderDropDown(WidgetIds.SortOrder, Document.SortOrderId, id =>
            {
                Undo.Record(Document);
                Document.SortOrderId = id;
            });
        }

        using (Inspector.BeginProperty("Skeleton"))
        {
            var skeleton = EditorUI.SkeletonField(WidgetIds.SkeletonDropDown, Document.Skeleton.Value);
            if (UI.WasChanged())
            {
                if (skeleton != null)
                    CommitSkeletonBinding(skeleton);
                else
                    ClearSkeletonBinding();
            }
        }

        if (Document.Skeleton.IsResolved)
        {
            using (Inspector.BeginProperty("Bone"))
            {
                var skeleton = Document.Skeleton.Value!;
                var boneLabel = Document.BoneName ?? "None";

                UI.DropDown(WidgetIds.BoneDropDown, () =>
                {
                    var boneItems = new List<PopupMenuItem>();

                    for (var i = 0; i < skeleton.BoneCount; i++)
                    {
                        var boneName = skeleton.Bones[i].Name;
                        boneItems.Add(new PopupMenuItem { Label = boneName, Handler = () => CommitBoneBinding(boneName) });
                    }

                    boneItems.Add(new PopupMenuItem { Label = "None", Handler = () => CommitBoneBinding(null) });
                    return [.. boneItems];
                }, boneLabel, EditorAssets.Sprites.IconBone);
            }


        }
    }

    private void SetConstraint(Vector2Int? size)
    {
        Undo.Record(Document);
        Document.ConstrainedSize = size;

        var maxWork = GetMaxWorkingSize();
        var newCanvas = new Vector2Int(
            Math.Max(Document.CanvasSize.X, maxWork),
            Math.Max(Document.CanvasSize.Y, maxWork));
        ResizeCanvas(newCanvas);

        Document.UpdateBounds();
        InvalidateComposite();
    }

    private void CommitSkeletonBinding(SkeletonDocument skeleton)
    {
        Undo.Record(Document);
        Document.Skeleton = skeleton;
        skeleton.UpdateSprites();
    }

    private void ClearSkeletonBinding()
    {
        if (!Document.Skeleton.IsResolved)
            return;

        var skeleton = Document.Skeleton.Value;
        Undo.Record(Document);
        Document.Skeleton.Clear();
        Document.BoneName = null;
        skeleton?.UpdateSprites();
    }

    private void CommitBoneBinding(string? boneName)
    {
        Undo.Record(Document);
        Document.BoneName = boneName;
        Document.ResolveBone();
        Document.Skeleton.Value?.UpdateSprites();
    }
}
