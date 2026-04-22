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

    public void PaintBrush(Vector2Int pixel, Color32 color, bool blend = true)
    {
        var layer = ActiveLayer;
        if (layer?.Pixels == null) return;

        const float ApronWidth = 2f;
        var tiling = Document.ShowTiling;
        var r = EditablePixelRect;
        var offset = (BrushSize - 1) / 2;
        var discCenter = (BrushSize - 1) / 2.0f;
        var apronR = BrushSize / 2.0f + ApronWidth;
        var apronR2 = apronR * apronR;
        var apronBox = (int)MathF.Ceiling(ApronWidth);

        for (var dy = -apronBox; dy < BrushSize + apronBox; dy++)
            for (var dx = -apronBox; dx < BrushSize + apronBox; dx++)
            {
                var inDisc = dx >= 0 && dx < BrushSize && dy >= 0 && dy < BrushSize
                             && IsInBrush(dx, dy, BrushSize);
                if (!inDisc)
                {
                    var ddx = dx - discCenter;
                    var ddy = dy - discCenter;
                    if (ddx * ddx + ddy * ddy > apronR2) continue;
                }

                var px = pixel.X - offset + dx;
                var py = pixel.Y - offset + dy;
                if (tiling)
                {
                    px = r.X + ((px - r.X) % r.Width + r.Width) % r.Width;
                    py = r.Y + ((py - r.Y) % r.Height + r.Height) % r.Height;
                }
                if (!IsPixelInConstraint(new Vector2Int(px, py))) continue;
                if (!IsPixelSelected(px, py)) continue;
                if (AlphaLock && layer.Pixels[px, py].A == 0) continue;

                if (inDisc)
                {
                    var dst = layer.Pixels[px, py];
                    layer.Pixels.Set(px, py, blend && color.A < 255 ? Color32.Blend(dst, color) : color);
                    continue;
                }

                // Apron: seed empty pixels around the disc with the brush RGB at alpha=0 so
                // bilinear filtering doesn't interpolate painted pixels against (0,0,0,0)
                // neighbors and produce a black fringe.
                if (color.A == 0) continue;
                var empty = layer.Pixels[px, py];
                if (empty.A != 0 || (empty.R | empty.G | empty.B) != 0) continue;
                layer.Pixels.Set(px, py, new Color32(color.R, color.G, color.B, 0));
            }
        InvalidateComposite();
    }

    public void PaintBrushSoft(Vector2Int pixel, Color32 color, float hardness)
    {
        var layer = ActiveLayer;
        if (layer?.Pixels == null) return;

        // Brush tip is a disc mask rasterized via 4×4 super-sampling per pixel. Same approach
        // as Photoshop — a 1-px brush at full hardness covers the center pixel at ~π/4 ≈ 78%
        // rather than snapping to a solid single pixel (that's what the Pencil tool is for).
        //
        // At hardness=1 the brush is a hard disc of radius nominalR; super-sampling alone
        // produces the AA rim. At hardness<1 the fade zone opens symmetrically — hardR
        // retreats from nominalR inward and outerR extends past it — for Photoshop-style
        // "soft edge spills outside the preview circle" behavior.
        var h = Math.Clamp(hardness, 0f, 1f);
        var nominalR = BrushSize * 0.5f;
        // Floor on the fade extension so tiny brushes get a visible soft footprint at low
        // hardness; above ~size 6 this floor stops mattering and scaling is proportional.
        const float MinFade = 3f;
        var fadeExtension = MathF.Max(nominalR, MinFade) * (1f - h);
        var outerR = nominalR + fadeExtension;
        var hardR = nominalR - fadeExtension;          // may be negative for tiny soft brushes
        var fadeWidth = outerR - hardR;
        var isHardMask = fadeWidth < 1e-4f;

        var centerOffset = (BrushSize & 1) == 0 ? 0.5f : 0f;
        var centerX = pixel.X + centerOffset;
        var centerY = pixel.Y + centerOffset;
        const float ApronWidth = 2f;
        var box = (int)MathF.Ceiling(outerR + ApronWidth);

        const int SS = 4;
        const float SSStep = 1f / SS;
        const float SSBase = -0.5f + SSStep * 0.5f;
        const float SSRecip = 1f / (SS * SS);

        var tiling = Document.ShowTiling;
        var rect = EditablePixelRect;

        for (var dy = -box; dy <= box; dy++)
            for (var dx = -box; dx <= box; dx++)
            {
                var px = pixel.X + dx;
                var py = pixel.Y + dy;
                if (tiling)
                {
                    px = rect.X + ((px - rect.X) % rect.Width + rect.Width) % rect.Width;
                    py = rect.Y + ((py - rect.Y) % rect.Height + rect.Height) % rect.Height;
                }
                if (!IsPixelInConstraint(new Vector2Int(px, py))) continue;
                if (!IsPixelSelected(px, py)) continue;
                if (AlphaLock && layer.Pixels[px, py].A == 0) continue;

                var sum = 0f;
                for (var sy = 0; sy < SS; sy++)
                {
                    var distY = (pixel.Y + dy + SSBase + sy * SSStep) - centerY;
                    var dy2 = distY * distY;
                    for (var sx = 0; sx < SS; sx++)
                    {
                        var distX = (pixel.X + dx + SSBase + sx * SSStep) - centerX;
                        var d = MathF.Sqrt(distX * distX + dy2);
                        if (d >= outerR) continue;
                        if (isHardMask || d <= hardR) { sum += 1f; continue; }
                        var t = (outerR - d) / fadeWidth;
                        sum += t * t * (3f - 2f * t);
                    }
                }
                if (sum <= 0f)
                {
                    // Apron: seed fully-empty pixels within ApronWidth px of the brush footprint
                    // with the brush RGB at alpha=0, so bilinear filtering doesn't interpolate
                    // painted pixels against (0,0,0,0) neighbors and produce a black fringe.
                    if (color.A == 0) continue;
                    var empty = layer.Pixels[px, py];
                    if (empty.A != 0 || (empty.R | empty.G | empty.B) != 0) continue;
                    var dcx = (pixel.X + dx) - centerX;
                    var dcy = (pixel.Y + dy) - centerY;
                    var apronR = outerR + ApronWidth;
                    if (dcx * dcx + dcy * dcy > apronR * apronR) continue;
                    layer.Pixels.Set(px, py, new Color32(color.R, color.G, color.B, 0));
                    continue;
                }
                var coverage = sum * SSRecip;

                var srcA = (color.A / 255f) * coverage;
                if (srcA <= 0f) continue;
                var src = new Color32(color.R, color.G, color.B, (byte)(srcA * 255f + 0.5f));
                if (src.A == 0) continue;

                var dst = layer.Pixels[px, py];
                layer.Pixels.Set(px, py, Color32.Blend(dst, src));
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
        var epr = EditablePixelRect;
        var cellW = canvas.Width / epr.Width;
        var cellH = canvas.Height / epr.Height;
        var brushOffset = (BrushSize - 1) / 2;
        var originX = canvas.X + (pixel.X - epr.X - brushOffset) * cellW;
        var originY = canvas.Y + (pixel.Y - epr.Y - brushOffset) * cellH;
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
