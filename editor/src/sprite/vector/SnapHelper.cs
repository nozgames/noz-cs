//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

enum SnapType { None, Anchor, PixelGrid }

static class SnapHelper
{
    public static void DrawSnapIndicator(SnapType snapType, Vector2 snapDocLocal, Matrix3x2 docTransform)
    {
        if (snapType == SnapType.None) return;
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(docTransform);
            var size = Gizmos.GetVertexSize();
            Gizmos.SetColor(snapType == SnapType.Anchor
                ? EditorStyle.Palette.Primary
                : EditorStyle.Workspace.SelectionColor);
            Gizmos.DrawRect(snapDocLocal, snapType == SnapType.Anchor ? size * 1.3f : size);
        }
    }

    public static Vector2 Snap(
        Vector2 candidateDocLocal,
        SpriteNode root,
        HashSet<SpritePath>? excludePaths,
        out SnapType snapType)
    {
        snapType = SnapType.None;

        // Priority 1: Anchor snap — uses the unified hit test with proper radius
        var hit = root.HitTestAnchor(candidateDocLocal, exclude: excludePaths);
        if (hit.HasValue)
        {
            snapType = SnapType.Anchor;
            var anchorPos = hit.Value.Position;
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
