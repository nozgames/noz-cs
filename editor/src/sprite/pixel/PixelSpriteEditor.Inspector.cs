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
        public static partial WidgetId ShowInSkeleton { get; }
        public static partial WidgetId ShowSkeletonOverlay { get; }
    }

    public override void InspectorUI()
    {
        SpriteInspectorUI();

        using (Inspector.BeginSection("BRUSH"))
        {
            if (Inspector.IsSectionCollapsed) return;

            using (Inspector.BeginProperty("Color"))
            {
                var color = BrushColor.ToColor();
                EditorUI.ColorButton(WidgetIds.BrushColor, ref color);
                var newColor = color.ToColor32();
                if (newColor != BrushColor)
                    BrushColor = newColor;
            }
        }
    }

    private void SpriteInspectorUI()
    {
        using var _ = Inspector.BeginSection("SPRITE");
        if (Inspector.IsSectionCollapsed) return;

        if (!Document.IsReference)
        {
            using (Inspector.BeginProperty("Export"))
            {
                var shouldExport = Document.ShouldExport;
                if (UI.Toggle(WidgetIds.ExportToggle, "", shouldExport, EditorStyle.Inspector.Toggle, EditorAssets.Sprites.IconCheck))
                {
                    shouldExport = !shouldExport;
                    Undo.Record(Document);
                    Document.ShouldExport = shouldExport;
                    AssetManifest.IsModified = true;
                }
            }
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
            var skeletonLabel = Document.Skeleton.IsResolved
                ? StringId.Get(Document.Skeleton.Value!.Name).ToString()
                : "None";

            UI.DropDown(WidgetIds.SkeletonDropDown, () =>
            {
                var skeletonItems = new List<PopupMenuItem>();

                foreach (var doc in DocumentManager.Documents)
                {
                    if (doc is not SkeletonDocument skeleton || skeleton.BoneCount == 0)
                        continue;

                    var name = StringId.Get(skeleton.Name).ToString();
                    skeletonItems.Add(new PopupMenuItem { Label = name, Handler = () => CommitSkeletonBinding(skeleton) });
                }

                skeletonItems.Add(new PopupMenuItem { Label = "None", Handler = ClearSkeletonBinding });
                return [.. skeletonItems];
            }, skeletonLabel, EditorAssets.Sprites.IconBone);
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

            if (UI.Button(WidgetIds.ShowInSkeleton, EditorAssets.Sprites.IconPreview, EditorStyle.Button.ToggleIcon, isSelected: Document.ShowInSkeleton))
            {
                Undo.Record(Document);
                Document.ShowInSkeleton = !Document.ShowInSkeleton;
                Document.Skeleton.Value?.UpdateSprites();
            }

            if (UI.Button(WidgetIds.ShowSkeletonOverlay, EditorAssets.Sprites.IconBone, EditorStyle.Button.ToggleIcon, isSelected: Document.ShowSkeletonOverlay))
            {
                Undo.Record(Document);
                Document.ShowSkeletonOverlay = !Document.ShowSkeletonOverlay;
            }
        }
    }

    private void SetConstraint(Vector2Int? size)
    {
        Undo.Record(Document);
        Document.ConstrainedSize = size;
        EditorUI.ClosePopup();
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
