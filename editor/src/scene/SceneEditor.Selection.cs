//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class SceneEditor
{
    private readonly List<SceneNode> _selectedNodes = [];
    private Rect _selectionLocalBounds;
    private float _selectionRotation;

    public IReadOnlyList<SceneNode> SelectedNodes => _selectedNodes;
    public float SelectionRotation => _selectionRotation;
    public Rect SelectionLocalBounds => _selectionLocalBounds;

    internal void RebuildSelection()
    {
        _selectedNodes.Clear();
        Document.Root.ForEach(n =>
        {
            if (n.IsSelected)
                _selectedNodes.Add(n);
        });
        UpdateSelectionBounds();
    }

    internal void ClearSelection()
    {
        Document.Root.ClearSelection();
        _selectedNodes.Clear();
        _selectionLocalBounds = default;
    }

    internal void SelectAll()
    {
        Document.Root.ClearSelection();
        foreach (var child in Document.Root.Children)
            child.IsSelected = true;
        RebuildSelection();
    }

    private void SelectOnly(SceneNode node)
    {
        Document.Root.ClearSelection();
        node.IsSelected = true;
        node.ExpandAncestors();
        RebuildSelection();
    }

    private void ToggleSelected(SceneNode node)
    {
        node.IsSelected = !node.IsSelected;
        RebuildSelection();
    }

    private static bool IsAncestorOfAnySelected(SceneNode node)
    {
        // To avoid double-selecting (parent and child), prefer top-level selected ancestor
        var p = node.Parent;
        while (p != null)
        {
            if (p.IsSelected)
                return true;
            p = p.Parent;
        }
        return false;
    }

    internal Rect SelectionWorldBounds()
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        var any = false;
        foreach (var node in _selectedNodes)
        {
            var b = NodeWorldAABB(node);
            if (b.Width <= 0 && b.Height <= 0) continue;
            min = Vector2.Min(min, b.Min);
            max = Vector2.Max(max, b.Max);
            any = true;
        }
        return any ? Rect.FromMinMax(min, max) : default;
    }

    private static Rect NodeWorldAABB(SceneNode node)
    {
        if (node is SceneSprite ss && ss.Sprite.Value is { } doc)
            return TransformAABB(doc.Bounds, node.WorldTransform);

        if (node is SceneGroup g)
        {
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);
            var any = false;
            CollectGroupAABB(g, ref min, ref max, ref any);
            return any ? Rect.FromMinMax(min, max) : default;
        }

        return default;
    }

    private static void CollectGroupAABB(SceneNode node, ref Vector2 min, ref Vector2 max, ref bool any)
    {
        foreach (var child in node.Children)
        {
            if (child is SceneSprite ss && ss.Sprite.Value is { } doc)
            {
                var b = TransformAABB(doc.Bounds, child.WorldTransform);
                min = Vector2.Min(min, b.Min);
                max = Vector2.Max(max, b.Max);
                any = true;
            }
            else if (child is SceneGroup)
            {
                CollectGroupAABB(child, ref min, ref max, ref any);
            }
        }
    }

    private static Rect TransformAABB(Rect r, Matrix3x2 m)
    {
        Span<Vector2> corners = stackalloc Vector2[]
        {
            Vector2.Transform(new Vector2(r.X, r.Y), m),
            Vector2.Transform(new Vector2(r.Right, r.Y), m),
            Vector2.Transform(new Vector2(r.Right, r.Bottom), m),
            Vector2.Transform(new Vector2(r.X, r.Bottom), m),
        };
        var min = corners[0];
        var max = corners[0];
        for (var i = 1; i < 4; i++)
        {
            min = Vector2.Min(min, corners[i]);
            max = Vector2.Max(max, corners[i]);
        }
        return Rect.FromMinMax(min, max);
    }

    internal void UpdateSelectionBounds()
    {
        if (_selectedNodes.Count == 0)
        {
            _selectionLocalBounds = default;
            _selectionRotation = 0f;
            return;
        }

        // If a single node selected, use its local sprite bounds rotated by its world rotation
        if (_selectedNodes.Count == 1)
        {
            var node = _selectedNodes[0];
            var rotation = ExtractRotation(node.WorldTransform);
            _selectionRotation = rotation;
            var aabb = NodeWorldAABB(node);
            // Convert AABB to selection-local (rotate by -rotation around origin)
            var inv = Matrix3x2.CreateRotation(-rotation);
            var corners = new[]
            {
                Vector2.Transform(new Vector2(aabb.X, aabb.Y), inv),
                Vector2.Transform(new Vector2(aabb.Right, aabb.Y), inv),
                Vector2.Transform(new Vector2(aabb.Right, aabb.Bottom), inv),
                Vector2.Transform(new Vector2(aabb.X, aabb.Bottom), inv),
            };
            var min = corners[0];
            var max = corners[0];
            for (var i = 1; i < 4; i++)
            {
                min = Vector2.Min(min, corners[i]);
                max = Vector2.Max(max, corners[i]);
            }
            _selectionLocalBounds = Rect.FromMinMax(min, max);
            return;
        }

        // Multi-select: axis-aligned bounding box
        _selectionRotation = 0f;
        _selectionLocalBounds = SelectionWorldBounds();
    }

    private static float ExtractRotation(Matrix3x2 m)
    {
        return MathF.Atan2(m.M12, m.M11);
    }

    internal SceneNode? HitTestNodes(Vector2 worldPos)
    {
        // Test in reverse draw order: last-drawn (top of children list at deepest level) wins
        return HitRecursive(Document.Root, worldPos);
    }

    private static SceneNode? HitRecursive(SceneNode node, Vector2 worldPos)
    {
        if (node.Locked) return null;

        // Check children in reverse order (later children render on top)
        for (var i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            if (!child.Visible) continue;
            var hit = HitRecursive(child, worldPos);
            if (hit != null) return hit;
        }

        if (node is SceneSprite ss && ss.Sprite.Value is { } doc)
        {
            var aabb = NodeWorldAABB(node);
            if (aabb.Contains(worldPos))
                return node;
        }
        return null;
    }

    private void DrawSelectionOverlay()
    {
        if (_selectedNodes.Count == 0) return;

        UpdateSelectionBounds();

        var bounds = _selectionLocalBounds;
        if (bounds.Width <= 0 && bounds.Height <= 0) return;

        using var _ = Gizmos.PushState(EditorLayer.DocumentEditor);

        var selToDoc = Matrix3x2.CreateRotation(_selectionRotation);
        Graphics.SetTransform(selToDoc * Document.Transform);

        Gizmos.SetColor(EditorStyle.Palette.Primary);
        var lineWidth = EditorStyle.SpritePath.SegmentLineWidth;
        var tl = new Vector2(bounds.X, bounds.Y);
        var tr = new Vector2(bounds.Right, bounds.Y);
        var br = new Vector2(bounds.Right, bounds.Bottom);
        var bl = new Vector2(bounds.X, bounds.Bottom);
        Gizmos.DrawLine(tl, tr, lineWidth, order: 2);
        Gizmos.DrawLine(tr, br, lineWidth, order: 2);
        Gizmos.DrawLine(br, bl, lineWidth, order: 2);
        Gizmos.DrawLine(bl, tl, lineWidth, order: 2);

        var midX = bounds.X + bounds.Width * 0.5f;
        var midY = bounds.Y + bounds.Height * 0.5f;

        var h = _hoverHandle;
        Gizmos.DrawAnchor(tl, selected: h is SpritePathHandle.ScaleTopLeft, order: 6);
        Gizmos.DrawAnchor(tr, selected: h is SpritePathHandle.ScaleTopRight, order: 6);
        Gizmos.DrawAnchor(br, selected: h is SpritePathHandle.ScaleBottomRight, order: 6);
        Gizmos.DrawAnchor(bl, selected: h is SpritePathHandle.ScaleBottomLeft, order: 6);
        Gizmos.DrawAnchor(new Vector2(midX, bounds.Y), selected: h is SpritePathHandle.ScaleTop, order: 6);
        Gizmos.DrawAnchor(new Vector2(midX, bounds.Bottom), selected: h is SpritePathHandle.ScaleBottom, order: 6);
        Gizmos.DrawAnchor(new Vector2(bounds.X, midY), selected: h is SpritePathHandle.ScaleLeft, order: 6);
        Gizmos.DrawAnchor(new Vector2(bounds.Right, midY), selected: h is SpritePathHandle.ScaleRight, order: 6);

        var center = new Vector2(midX, midY);
        var rotScale = EditorStyle.SpritePath.RotateHandleScale;
        Gizmos.DrawAnchor(GetRotateHandleOffset(tl, center), selected: h is SpritePathHandle.RotateTopLeft, scale: rotScale, order: 6);
        Gizmos.DrawAnchor(GetRotateHandleOffset(tr, center), selected: h is SpritePathHandle.RotateTopRight, scale: rotScale, order: 6);
        Gizmos.DrawAnchor(GetRotateHandleOffset(br, center), selected: h is SpritePathHandle.RotateBottomRight, scale: rotScale, order: 6);
        Gizmos.DrawAnchor(GetRotateHandleOffset(bl, center), selected: h is SpritePathHandle.RotateBottomLeft, scale: rotScale, order: 6);

        // Snap-leader origin marker — the first effective selection's origin is what move-snap aligns to
        var leader = EffectiveSelection().FirstOrDefault();
        if (leader != null)
        {
            using (Graphics.PushState())
            {
                Graphics.SetTransform(leader.WorldTransform * Document.Transform);
                Gizmos.DrawOrigin(EditorStyle.Workspace.OriginColor, order: 7);
            }
        }
    }

    private SpritePathHandle _hoverHandle = SpritePathHandle.None;
    internal SpritePathHandle HoverHandle => _hoverHandle;

    internal void SetHoverHandle(SpritePathHandle handle)
    {
        _hoverHandle = handle;
    }

    private static Vector2 GetRotateHandleOffset(Vector2 corner, Vector2 boundsCenter)
    {
        var dir = Vector2.Normalize(corner - boundsCenter);
        var offset = EditorStyle.SpritePath.RotateHandleOffset * Gizmos.ZoomRefScale;
        return corner + dir * offset;
    }

    internal SpritePathHandle HitTestHandles(Vector2 docLocalPos)
    {
        var bounds = _selectionLocalBounds;
        if (bounds.Width <= 0 && bounds.Height <= 0) return SpritePathHandle.None;

        var selPos = Vector2.Transform(docLocalPos, Matrix3x2.CreateRotation(-_selectionRotation));

        var hitRadius = EditorStyle.SpritePath.AnchorHitRadius;
        var hitRadiusSqr = hitRadius * hitRadius;

        var midX = bounds.X + bounds.Width * 0.5f;
        var midY = bounds.Y + bounds.Height * 0.5f;

        Span<Vector2> corners = stackalloc Vector2[4];
        corners[0] = new Vector2(bounds.X, bounds.Y);
        corners[1] = new Vector2(bounds.Right, bounds.Y);
        corners[2] = new Vector2(bounds.Right, bounds.Bottom);
        corners[3] = new Vector2(bounds.X, bounds.Bottom);

        var boundsCenter = new Vector2(midX, midY);
        var rotateHitRadius = hitRadius * EditorStyle.SpritePath.RotateHandleScale;
        var rotateHitRadiusSqr = rotateHitRadius * rotateHitRadius;
        for (var i = 0; i < 4; i++)
        {
            var rotPos = GetRotateHandleOffset(corners[i], boundsCenter);
            if (Vector2.DistanceSquared(selPos, rotPos) <= rotateHitRadiusSqr)
                return SpritePathHandle.RotateTopLeft + i;
        }

        for (var i = 0; i < 4; i++)
        {
            if (Vector2.DistanceSquared(selPos, corners[i]) <= hitRadiusSqr)
                return SpritePathHandle.ScaleTopLeft + i * 2;
        }

        Span<Vector2> edges = stackalloc Vector2[4];
        edges[0] = new Vector2(midX, bounds.Y);
        edges[1] = new Vector2(bounds.Right, midY);
        edges[2] = new Vector2(midX, bounds.Bottom);
        edges[3] = new Vector2(bounds.X, midY);

        Span<SpritePathHandle> edgeHits = stackalloc SpritePathHandle[4];
        edgeHits[0] = SpritePathHandle.ScaleTop;
        edgeHits[1] = SpritePathHandle.ScaleRight;
        edgeHits[2] = SpritePathHandle.ScaleBottom;
        edgeHits[3] = SpritePathHandle.ScaleLeft;

        for (var i = 0; i < 4; i++)
        {
            if (Vector2.DistanceSquared(selPos, edges[i]) <= hitRadiusSqr)
                return edgeHits[i];
        }

        if (bounds.Contains(selPos))
            return SpritePathHandle.Move;

        return SpritePathHandle.None;
    }

    internal Vector2 GetOppositePivotInSelSpace(SpritePathHandle hit)
    {
        var b = _selectionLocalBounds;
        var midX = b.X + b.Width * 0.5f;
        var midY = b.Y + b.Height * 0.5f;

        return hit switch
        {
            SpritePathHandle.ScaleTopLeft => new Vector2(b.Right, b.Bottom),
            SpritePathHandle.ScaleTop => new Vector2(midX, b.Bottom),
            SpritePathHandle.ScaleTopRight => new Vector2(b.X, b.Bottom),
            SpritePathHandle.ScaleRight => new Vector2(b.X, midY),
            SpritePathHandle.ScaleBottomRight => new Vector2(b.X, b.Y),
            SpritePathHandle.ScaleBottom => new Vector2(midX, b.Y),
            SpritePathHandle.ScaleBottomLeft => new Vector2(b.Right, b.Y),
            SpritePathHandle.ScaleLeft => new Vector2(b.Right, midY),
            _ => b.Center,
        };
    }

    internal void HandleClick(Vector2 worldPos, bool shift)
    {
        var hit = HitTestNodes(worldPos);

        if (hit == null)
        {
            if (!shift)
                ClearSelection();
            return;
        }

        if (shift)
        {
            ToggleSelected(hit);
        }
        else
        {
            SelectOnly(hit);
        }
    }

    internal void CommitBoxSelect(Rect selectionBounds)
    {
        // selectionBounds is in world space; node AABBs are in document-local space
        var boxLocal = selectionBounds.Translate(-Document.Position);

        Document.Root.ClearSelection();
        Document.Root.ForEach(n =>
        {
            if (!n.Visible) return;
            if (IsLockedSelf(n)) return;
            if (n is not SceneSprite) return;
            var aabb = NodeWorldAABB(n);
            if (aabb.Width <= 0 && aabb.Height <= 0) return;
            if (boxLocal.Intersects(aabb))
                n.IsSelected = true;
        });
        RebuildSelection();
    }

    private static bool IsLockedSelf(SceneNode node)
    {
        var n = node;
        while (n != null)
        {
            if (n.Locked) return true;
            n = n.Parent;
        }
        return false;
    }

    internal IEnumerable<SceneNode> EffectiveSelection()
    {
        // Skip nodes whose ancestor is also selected (avoids double-transform)
        foreach (var n in _selectedNodes)
            if (!IsAncestorOfAnySelected(n))
                yield return n;
    }
}
