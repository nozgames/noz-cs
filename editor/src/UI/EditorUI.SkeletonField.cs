//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static partial class EditorUI
{
    private static WidgetId _skeletonFieldId;
    private static SkeletonDocument? _skeletonFieldResult;
    private static bool _skeletonFieldHasResult;

    public static SkeletonDocument? SkeletonField(WidgetId id, SkeletonDocument? value)
    {
        var label = value?.Name ?? "None";

        UI.DropDown(id, () => GetSkeletonFieldItems(id), label, EditorAssets.Sprites.IconBone);

        if (_skeletonFieldHasResult && _skeletonFieldId == id)
        {
            _skeletonFieldHasResult = false;
            var result = _skeletonFieldResult;
            UI.NotifyChanged(result?.GetHashCode() ?? 0);
            return result;
        }

        return value;
    }

    private static PopupMenuItem[] GetSkeletonFieldItems(WidgetId id)
    {
        var items = new List<PopupMenuItem>();

        foreach (var doc in DocumentManager.Documents)
        {
            if (doc is not SkeletonDocument skeleton || skeleton.BoneCount == 0)
                continue;

            items.Add(new PopupMenuItem
            {
                Label = skeleton.Name,
                Handler = () =>
                {
                    _skeletonFieldId = id;
                    _skeletonFieldResult = skeleton;
                    _skeletonFieldHasResult = true;
                }
            });
        }

        items.Add(new PopupMenuItem
        {
            Label = "None",
            Handler = () =>
            {
                _skeletonFieldId = id;
                _skeletonFieldResult = null;
                _skeletonFieldHasResult = true;
            }
        });

        return [.. items];
    }
}
