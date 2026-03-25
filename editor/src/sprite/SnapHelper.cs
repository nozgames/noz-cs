//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

enum SnapType { None, Anchor, PixelGrid }

static class SnapHelper
{
    public static Vector2 Snap(
        Vector2 candidateDocLocal,
        SpriteNode root,
        HashSet<SpritePath>? excludePaths,
        out SnapType snapType)
    {
        snapType = SnapType.None;

        // Priority 1: Anchor snap via existing hit-test infrastructure
        // HitTest takes doc-local coords but returns AnchorPosition in path-local space,
        // so we need to transform back to doc-local via the target path's PathTransform.
        var hit = root.HitTest(candidateDocLocal);
        if (hit.HasValue && hit.Value.Hit.AnchorIndex >= 0
            && (excludePaths == null || !excludePaths.Contains(hit.Value.Path)))
        {
            snapType = SnapType.Anchor;
            var anchorPos = hit.Value.Hit.AnchorPosition;
            return hit.Value.Path.HasTransform
                ? Vector2.Transform(anchorPos, hit.Value.Path.PathTransform)
                : anchorPos;
        }

        // Priority 2: Pixel grid (only when zoomed in enough to see it)
        if (Grid.IsPixelGridVisible)
        {
            snapType = SnapType.PixelGrid;
            return Grid.SnapToPixelGrid(candidateDocLocal);
        }

        return candidateDocLocal;
    }
}
