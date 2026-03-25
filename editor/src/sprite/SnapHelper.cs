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

    private const float SnapScreenRadius = 0.8f;

    public static Vector2 Snap(
        Vector2 candidateDocLocal,
        SpriteNode root,
        HashSet<SpritePath>? excludePaths,
        out SnapType snapType)
    {
        snapType = SnapType.None;

        // Priority 1: Anchor snap with a generous screen-space radius
        var snapRadius = SnapScreenRadius / Workspace.Zoom;
        var snapRadiusSqr = snapRadius * snapRadius;
        var bestDistSqr = float.MaxValue;
        var bestPos = Vector2.Zero;

        FindNearestAnchor(root, candidateDocLocal, snapRadiusSqr, excludePaths, ref bestDistSqr, ref bestPos);

        if (bestDistSqr < snapRadiusSqr)
        {
            snapType = SnapType.Anchor;
            return bestPos;
        }

        // Priority 2: Pixel grid (only when zoomed in enough to see it)
        if (Grid.IsPixelGridVisible)
        {
            snapType = SnapType.PixelGrid;
            return Grid.SnapToPixelGrid(candidateDocLocal);
        }

        return candidateDocLocal;
    }

    private static void FindNearestAnchor(
        SpriteNode node,
        Vector2 docLocal,
        float radiusSqr,
        HashSet<SpritePath>? exclude,
        ref float bestDistSqr,
        ref Vector2 bestPos)
    {
        if (!node.Visible) return;

        if (node is SpritePath path)
        {
            if (exclude != null && exclude.Contains(path)) return;

            for (var i = 0; i < path.Anchors.Count; i++)
            {
                // Transform anchor to doc-local space
                var pos = path.HasTransform
                    ? Vector2.Transform(path.Anchors[i].Position, path.PathTransform)
                    : path.Anchors[i].Position;

                var distSqr = Vector2.DistanceSquared(docLocal, pos);
                if (distSqr < radiusSqr && distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    bestPos = pos;
                }
            }
            return;
        }

        for (var i = 0; i < node.Children.Count; i++)
            FindNearestAnchor(node.Children[i], docLocal, radiusSqr, exclude, ref bestDistSqr, ref bestPos);
    }
}
