//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PixelLassoSelectMode : EditorMode<PixelEditor>
{
    private List<Vector2Int>? _points;
    private readonly List<int> _intersections = new();

    public override void Update()
    {
        EditorCursor.SetCrosshair();

        var mouseWorld = Workspace.MouseWorldPosition;
        var pixel = Editor.WorldToPixelSnapped(mouseWorld);

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
        {
            Editor.ClearSelection();
            return;
        }

        if (_points == null)
        {
            if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
            {
                _points = new List<Vector2Int> { pixel };
            }
        }
        else
        {
            if (pixel != _points[^1])
                AddBresenhamLine(_points[^1], pixel);

            if (!Input.IsButtonDown(InputCode.MouseLeft, InputScope.All))
            {
                CommitSelection();
                _points = null;
            }
        }
    }

    private void AddBresenhamLine(Vector2Int from, Vector2Int to)
    {
        var dx = Math.Abs(to.X - from.X);
        var dy = Math.Abs(to.Y - from.Y);
        var sx = from.X < to.X ? 1 : -1;
        var sy = from.Y < to.Y ? 1 : -1;
        var err = dx - dy;

        var x = from.X;
        var y = from.Y;

        while (true)
        {
            if (x == to.X && y == to.Y) break;

            var e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }

            _points!.Add(new Vector2Int(x, y));
        }
    }

    private void CommitSelection()
    {
        if (_points == null || _points.Count < 3)
        {
            Editor.ClearSelection();
            return;
        }

        SelectionOp op;
        if (Input.IsShiftDown(InputScope.All))
            op = SelectionOp.Add;
        else if (Input.IsAltDown(InputScope.All))
            op = SelectionOp.Subtract;
        else
            op = SelectionOp.Replace;

        var w = Editor.Document.CanvasSize.X;
        var h = Editor.Document.CanvasSize.Y;

        if (op == SelectionOp.Replace)
        {
            Editor.Document.SelectionMask?.Dispose();
            Editor.Document.SelectionMask = new PixelData<byte>(w, h);
            FillPolygon(Editor.Document.SelectionMask, 255, w, h);
        }
        else if (op == SelectionOp.Add)
        {
            Editor.Document.SelectionMask ??= new PixelData<byte>(w, h);
            FillPolygon(Editor.Document.SelectionMask, 255, w, h);
        }
        else if (op == SelectionOp.Subtract)
        {
            if (Editor.Document.SelectionMask == null) return;
            FillPolygon(Editor.Document.SelectionMask, 0, w, h);
        }

        // Shrink selection to only non-transparent pixels
        if (op != SelectionOp.Subtract)
            MaskToContent(Editor.Document.SelectionMask!, w, h);
    }

    private void MaskToContent(PixelData<byte> mask, int w, int h)
    {
        var pixels = Editor.ActiveLayer?.Pixels;
        if (pixels == null) return;

        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (mask[x, y] > 0 && pixels[x, y].A == 0)
                    mask[x, y] = 0;
    }

    private void FillPolygon(PixelData<byte> mask, byte value, int w, int h)
    {
        var n = _points!.Count;
        if (n < 3) return;

        var minY = int.MaxValue;
        var maxY = int.MinValue;
        for (var i = 0; i < n; i++)
        {
            if (_points[i].Y < minY) minY = _points[i].Y;
            if (_points[i].Y > maxY) maxY = _points[i].Y;
        }

        minY = Math.Max(0, minY);
        maxY = Math.Min(h - 1, maxY);

        for (var y = minY; y <= maxY; y++)
        {
            _intersections.Clear();

            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                var y0 = _points[i].Y;
                var y1 = _points[j].Y;

                if ((y0 <= y && y < y1) || (y1 <= y && y < y0))
                {
                    var x0 = _points[i].X;
                    var x1 = _points[j].X;
                    var ix = x0 + (y - y0) * (x1 - x0) / (y1 - y0);
                    _intersections.Add(ix);
                }
            }

            _intersections.Sort();

            for (var k = 0; k + 1 < _intersections.Count; k += 2)
            {
                var xStart = Math.Max(0, _intersections[k]);
                var xEnd = Math.Min(w - 1, _intersections[k + 1]);
                for (var x = xStart; x <= xEnd; x++)
                    mask[x, y] = value;
            }
        }
    }

    public override void Draw()
    {
        if (_points == null || _points.Count < 2) return;

        var bounds = Editor.CanvasRect;
        var epr = Editor.EditablePixelRect;
        var cellW = bounds.Width / epr.Width;
        var cellH = bounds.Height / epr.Height;

        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(Editor.Document.Transform);

            // Draw path twice for contrast (black shadow + white foreground)
            for (var pass = 0; pass < 2; pass++)
            {
                if (pass == 0)
                {
                    Graphics.SetColor(new Color(0f, 0f, 0f, 0.6f));
                }
                else
                {
                    Graphics.SetColor(new Color(1f, 1f, 1f, 0.8f));
                }

                var thickness = pass == 0
                    ? EditorStyle.Workspace.DocumentBoundsLineWidth * 2f
                    : EditorStyle.Workspace.DocumentBoundsLineWidth;

                for (var i = 0; i < _points.Count; i++)
                {
                    var j = (i + 1) % _points.Count;
                    var p0 = _points[i];
                    var p1 = _points[j];

                    var w0 = new Vector2(
                        bounds.X + (p0.X - epr.X + 0.5f) * cellW,
                        bounds.Y + (p0.Y - epr.Y + 0.5f) * cellH);
                    var w1 = new Vector2(
                        bounds.X + (p1.X - epr.X + 0.5f) * cellW,
                        bounds.Y + (p1.Y - epr.Y + 0.5f) * cellH);

                    Gizmos.DrawLine(w0, w1, thickness);
                }
            }
        }
    }
}
