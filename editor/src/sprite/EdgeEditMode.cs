//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public enum EdgeSide
{
    None,
    Top,
    Left,
    Bottom,
    Right,
}

public class EdgeEditMode : EditorMode<SpriteEditor>
{
    private const float HitPixelTolerance = 6f;

    private EdgeSide _hoverSide = EdgeSide.None;
    private EdgeSide _dragSide = EdgeSide.None;
    private EdgeInsets _dragStartEdges;
    private Vector2 _dragStartLocal;

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
        {
            Editor.ToggleEdgeEditMode();
            return;
        }

        var doc = Editor.Document;
        Matrix3x2.Invert(doc.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        // Start drag: hit-test at the drag-start position, not current mouse.
        // By the time DragStarted fires the mouse has already moved DragMinDistance
        // away from the initial click, so the current mouse may no longer be on
        // the edge even though the click was.
        if (_dragSide == EdgeSide.None && Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
        {
            var dragLocal = Vector2.Transform(Workspace.DragWorldPosition, invTransform);
            _dragSide = HitTestEdges(dragLocal);
            if (_dragSide != EdgeSide.None)
            {
                _dragStartEdges = doc.Edges;
                _dragStartLocal = dragLocal;
                Undo.Record(doc);
            }
        }

        if (_dragSide != EdgeSide.None)
        {
            UpdateDrag(mouseLocal);
            UpdateCursor(_dragSide);
            if (Input.WasButtonReleased(InputCode.MouseLeft, InputScope.All))
                _dragSide = EdgeSide.None;
            return;
        }

        _hoverSide = HitTestEdges(mouseLocal);
        UpdateCursor(_hoverSide);
    }

    private static void UpdateCursor(EdgeSide side)
    {
        switch (side)
        {
            case EdgeSide.Top:
            case EdgeSide.Bottom:
                EditorCursor.SetSystem(SystemCursor.ResizeNS);
                break;
            case EdgeSide.Left:
            case EdgeSide.Right:
                EditorCursor.SetSystem(SystemCursor.ResizeEW);
                break;
            default:
                EditorCursor.SetArrow();
                break;
        }
    }

    private EdgeSide HitTestEdges(Vector2 local)
    {
        var bounds = Editor.Document.Bounds;
        var edges = Editor.Document.Edges;
        var ppu = Editor.Document.PixelsPerUnit;
        var tol = HitPixelTolerance / (ppu * Workspace.Zoom);

        var topY = bounds.Top + edges.T;
        var botY = bounds.Bottom - edges.B;
        var leftX = bounds.Left + edges.L;
        var rightX = bounds.Right - edges.R;

        var inX = local.X >= bounds.Left - tol && local.X <= bounds.Right + tol;
        var inY = local.Y >= bounds.Top - tol && local.Y <= bounds.Bottom + tol;

        var best = EdgeSide.None;
        var bestDist = tol;

        if (inX)
        {
            var dt = MathF.Abs(local.Y - topY);
            if (dt < bestDist) { bestDist = dt; best = EdgeSide.Top; }

            var db = MathF.Abs(local.Y - botY);
            if (db < bestDist) { bestDist = db; best = EdgeSide.Bottom; }
        }

        if (inY)
        {
            var dl = MathF.Abs(local.X - leftX);
            if (dl < bestDist) { bestDist = dl; best = EdgeSide.Left; }

            var dr = MathF.Abs(local.X - rightX);
            if (dr < bestDist) { best = EdgeSide.Right; }
        }

        return best;
    }

    private void UpdateDrag(Vector2 mouseLocal)
    {
        var doc = Editor.Document;
        var bounds = doc.Bounds;
        var ppu = doc.PixelsPerUnit;
        var delta = mouseLocal - _dragStartLocal;

        var t = _dragStartEdges.T;
        var l = _dragStartEdges.L;
        var b = _dragStartEdges.B;
        var r = _dragStartEdges.R;

        switch (_dragSide)
        {
            case EdgeSide.Top:
                t = Math.Clamp(t + delta.Y, 0f, bounds.Height - b);
                t = MathF.Round(t * ppu) / ppu;
                break;
            case EdgeSide.Bottom:
                b = Math.Clamp(b - delta.Y, 0f, bounds.Height - t);
                b = MathF.Round(b * ppu) / ppu;
                break;
            case EdgeSide.Left:
                l = Math.Clamp(l + delta.X, 0f, bounds.Width - r);
                l = MathF.Round(l * ppu) / ppu;
                break;
            case EdgeSide.Right:
                r = Math.Clamp(r - delta.X, 0f, bounds.Width - l);
                r = MathF.Round(r * ppu) / ppu;
                break;
        }

        doc.Edges = new EdgeInsets(t, l, b, r);
    }

    public override void Draw()
    {
        var doc = Editor.Document;
        var bounds = doc.Bounds;
        var edges = doc.Edges;

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(doc.Transform);
            Graphics.SetSortGroup(7);

            var baseWidth = EditorStyle.Workspace.DocumentBoundsLineWidth * 1.5f;
            var activeWidth = baseWidth * 1.6f;
            var baseColor = new Color(0.2f, 0.9f, 0.2f, 0.9f);
            var activeColor = new Color(1f, 0.85f, 0.2f, 1f);

            DrawEdgeLine(EdgeSide.Top, bounds, edges, baseColor, activeColor, baseWidth, activeWidth);
            DrawEdgeLine(EdgeSide.Bottom, bounds, edges, baseColor, activeColor, baseWidth, activeWidth);
            DrawEdgeLine(EdgeSide.Left, bounds, edges, baseColor, activeColor, baseWidth, activeWidth);
            DrawEdgeLine(EdgeSide.Right, bounds, edges, baseColor, activeColor, baseWidth, activeWidth);
        }
    }

    private void DrawEdgeLine(EdgeSide side, Rect bounds, EdgeInsets edges, Color baseColor, Color activeColor, float baseWidth, float activeWidth)
    {
        var isActive = side == _dragSide || (_dragSide == EdgeSide.None && side == _hoverSide);
        Gizmos.SetColor(isActive ? activeColor : baseColor);
        var w = isActive ? activeWidth : baseWidth;

        Vector2 a, b;
        switch (side)
        {
            case EdgeSide.Top:
                var yt = bounds.Top + edges.T;
                a = new Vector2(bounds.Left, yt);
                b = new Vector2(bounds.Right, yt);
                break;
            case EdgeSide.Bottom:
                var yb = bounds.Bottom - edges.B;
                a = new Vector2(bounds.Left, yb);
                b = new Vector2(bounds.Right, yb);
                break;
            case EdgeSide.Left:
                var xl = bounds.Left + edges.L;
                a = new Vector2(xl, bounds.Top);
                b = new Vector2(xl, bounds.Bottom);
                break;
            case EdgeSide.Right:
                var xr = bounds.Right - edges.R;
                a = new Vector2(xr, bounds.Top);
                b = new Vector2(xr, bounds.Bottom);
                break;
            default: return;
        }

        Gizmos.DrawLine(a, b, w);
    }
}
