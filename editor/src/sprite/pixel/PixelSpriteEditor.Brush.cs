//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class PixelSpriteEditor
{
    private const int MaxBrushSize = 16;

    private struct BrushRun
    {
        public Vector2 Start;
        public Vector2 End;
    }

    private static readonly BrushRun[]?[] _brushRunCache = new BrushRun[MaxBrushSize + 1][];

    public static bool IsInBrush(int dx, int dy, int brushSize)
    {
        if (brushSize <= 2) return true;
        var center = (brushSize - 1) / 2.0f;
        var distX = dx - center;
        var distY = dy - center;
        var radius = brushSize / 2.0f;
        return distX * distX + distY * distY <= radius * radius;
    }

    public void PaintBrush(Vector2Int pixel, Color32 color)
    {
        var layer = ActiveLayer;
        if (layer?.Pixels == null) return;

        var offset = (BrushSize - 1) / 2;
        for (var dy = 0; dy < BrushSize; dy++)
            for (var dx = 0; dx < BrushSize; dx++)
            {
                if (!IsInBrush(dx, dy, BrushSize)) continue;
                var px = pixel.X - offset + dx;
                var py = pixel.Y - offset + dy;
                if (!IsPixelInBounds(new Vector2Int(px, py))) continue;
                if (!IsPixelSelected(px, py)) continue;
                layer.Pixels.Set(px, py, color);
            }
        InvalidateComposite();
    }

    private static BrushRun[] GetBrushRuns(int brushSize)
    {
        var runs = _brushRunCache[brushSize];
        if (runs != null) return runs;
        runs = BuildBrushRuns(brushSize);
        _brushRunCache[brushSize] = runs;
        return runs;
    }

    private static BrushRun[] BuildBrushRuns(int brushSize)
    {
        // Collect individual edges, then merge consecutive ones into runs
        var hEdges = new List<(int y, int x0, int x1)>();
        var vEdges = new List<(int x, int y0, int y1)>();

        for (var dy = 0; dy < brushSize; dy++)
            for (var dx = 0; dx < brushSize; dx++)
            {
                if (!IsInBrush(dx, dy, brushSize)) continue;
                if (!IsInBrushSafe(dx, dy - 1, brushSize)) hEdges.Add((dy, dx, dx + 1));
                if (!IsInBrushSafe(dx, dy + 1, brushSize)) hEdges.Add((dy + 1, dx, dx + 1));
                if (!IsInBrushSafe(dx - 1, dy, brushSize)) vEdges.Add((dx, dy, dy + 1));
                if (!IsInBrushSafe(dx + 1, dy, brushSize)) vEdges.Add((dx + 1, dy, dy + 1));
            }

        var runs = new List<BrushRun>();

        // Merge horizontal edges: group by Y, sort by X, merge consecutive
        hEdges.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x0.CompareTo(b.x0));
        for (var i = 0; i < hEdges.Count;)
        {
            var (y, startX, endX) = hEdges[i];
            i++;
            while (i < hEdges.Count && hEdges[i].y == y && hEdges[i].x0 == endX)
            {
                endX = hEdges[i].x1;
                i++;
            }
            runs.Add(new BrushRun { Start = new Vector2(startX, y), End = new Vector2(endX, y) });
        }

        // Merge vertical edges: group by X, sort by Y, merge consecutive
        vEdges.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y0.CompareTo(b.y0));
        for (var i = 0; i < vEdges.Count;)
        {
            var (x, startY, endY) = vEdges[i];
            i++;
            while (i < vEdges.Count && vEdges[i].x == x && vEdges[i].y0 == endY)
            {
                endY = vEdges[i].y1;
                i++;
            }
            runs.Add(new BrushRun { Start = new Vector2(x, startY), End = new Vector2(x, endY) });
        }

        return runs.ToArray();

        static bool IsInBrushSafe(int dx, int dy, int size) =>
            dx >= 0 && dx < size && dy >= 0 && dy < size && IsInBrush(dx, dy, size);
    }

    public void DrawBrushOutline(Vector2Int pixel, Color color)
    {
        var runs = GetBrushRuns(BrushSize);
        if (runs.Length == 0) return;

        var canvas = CanvasRect;
        var cellW = canvas.Width / Document.CanvasSize.X;
        var cellH = canvas.Height / Document.CanvasSize.Y;
        var brushOffset = (BrushSize - 1) / 2;
        var originX = canvas.X + (pixel.X - brushOffset) * cellW;
        var originY = canvas.Y + (pixel.Y - brushOffset) * cellH;
        var halfWidth = EditorStyle.Workspace.DocumentBoundsLineWidth * Gizmos.ZoomRefScale;

        var vertCount = runs.Length * 4;
        var idxCount = runs.Length * 6;
        Span<MeshVertex> verts = stackalloc MeshVertex[vertCount];
        Span<ushort> indices = stackalloc ushort[idxCount];

        for (var i = 0; i < runs.Length; i++)
        {
            ref var run = ref runs[i];
            var v0 = new Vector2(originX + run.Start.X * cellW, originY + run.Start.Y * cellH);
            var v1 = new Vector2(originX + run.End.X * cellW, originY + run.End.Y * cellH);

            var delta = v1 - v0;
            var length = delta.Length();
            var dir = delta / length;
            var perp = new Vector2(-dir.Y, dir.X);
            var start = v0 - dir * halfWidth;
            var end = v1 + dir * halfWidth;

            var vi = i * 4;
            verts[vi + 0] = new MeshVertex(start.X - perp.X * halfWidth, start.Y - perp.Y * halfWidth, 0, 0, Color.White);
            verts[vi + 1] = new MeshVertex(start.X + perp.X * halfWidth, start.Y + perp.Y * halfWidth, 1, 0, Color.White);
            verts[vi + 2] = new MeshVertex(end.X + perp.X * halfWidth, end.Y + perp.Y * halfWidth, 1, 1, Color.White);
            verts[vi + 3] = new MeshVertex(end.X - perp.X * halfWidth, end.Y - perp.Y * halfWidth, 0, 1, Color.White);

            var ii = i * 6;
            indices[ii + 0] = (ushort)(vi + 0);
            indices[ii + 1] = (ushort)(vi + 1);
            indices[ii + 2] = (ushort)(vi + 2);
            indices[ii + 3] = (ushort)(vi + 2);
            indices[ii + 4] = (ushort)(vi + 3);
            indices[ii + 5] = (ushort)(vi + 0);
        }

        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(6);
            Graphics.SetColor(color);
            Graphics.Draw(verts, indices);
        }
    }
}
