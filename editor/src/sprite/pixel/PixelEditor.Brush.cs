//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace NoZ.Editor;

public partial class PixelEditor
{
    private struct BrushRun
    {
        public Vector2 Start;
        public Vector2 End;
    }

    private static readonly BrushRun[]?[] _brushRunCache = new BrushRun[PixelDocument.MaxBrushSize + 1][];

    private PixelLayer? _softStrokeLayer;
    private PixelData<Color32>? _softStrokeOriginal;
    private PixelData<float>? _softStrokeCoverage;
    private RectInt _softStrokeRect;
    private bool _softStrokeActive;

    public bool IsSoftStrokeActive => _softStrokeActive;

    public void BeginSoftStroke()
    {
        var layer = ActiveLayer;
        if (layer?.Pixels == null) return;

        var epr = EditablePixelRect;
        _softStrokeLayer = layer;
        _softStrokeRect = epr;

        if (_softStrokeOriginal == null
            || _softStrokeOriginal.Width != epr.Width
            || _softStrokeOriginal.Height != epr.Height)
        {
            _softStrokeOriginal?.Dispose();
            _softStrokeCoverage?.Dispose();
            _softStrokeOriginal = new PixelData<Color32>(epr.Width, epr.Height);
            _softStrokeCoverage = new PixelData<float>(epr.Width, epr.Height);
        }
        else
        {
            _softStrokeCoverage!.Clear();
        }

        unsafe
        {
            var srcPtr = layer.Pixels.Ptr;
            var dstPtr = _softStrokeOriginal!.Ptr;
            var srcStride = layer.Pixels.Width;
            var dstStride = _softStrokeOriginal.Width;
            var rowBytes = (nuint)(epr.Width * sizeof(Color32));
            for (var y = 0; y < epr.Height; y++)
            {
                var srcRow = srcPtr + (epr.Y + y) * srcStride + epr.X;
                var dstRow = dstPtr + y * dstStride;
                NativeMemory.Copy(srcRow, dstRow, rowBytes);
            }
        }

        _softStrokeActive = true;
    }

    public void EndSoftStroke()
    {
        _softStrokeActive = false;
        _softStrokeLayer = null;
        // Buffers kept allocated for reuse on the next stroke.
    }

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
        var pixels = layer.Pixels;
        var mask = Document.SelectionMask;
        var alphaLock = AlphaLock;
        // Hard brush operates on whole cells: coerce to an integer ≥ 1 so the smallest
        // slider value (0.5) still paints one pixel, and so offsets match DrawBrushOutline.
        var size = Math.Max(1, (int)BrushSize);
        var offset = (size - 1) / 2;
        var discCenter = (size - 1) / 2.0f;
        var discR = size * 0.5f;
        var discR2 = discR * discR;
        var apronR = discR + ApronWidth;
        var apronR2 = apronR * apronR;
        var apronBox = (int)MathF.Ceiling(ApronWidth);

        for (var dy = -apronBox; dy < size + apronBox; dy++)
            for (var dx = -apronBox; dx < size + apronBox; dx++)
            {
                var ddx = dx - discCenter;
                var ddy = dy - discCenter;
                var d2 = ddx * ddx + ddy * ddy;
                var inBox = dx >= 0 && dx < size && dy >= 0 && dy < size;
                var inDisc = inBox && d2 <= discR2;
                if (!inDisc && d2 > apronR2) continue;

                var px = pixel.X - offset + dx;
                var py = pixel.Y - offset + dy;
                if (tiling)
                {
                    // Wrap only pixels that actually fell outside; most brush pixels sit
                    // inside the rect and don't need the modulo at all.
                    if ((uint)(px - r.X) >= (uint)r.Width)
                        px = r.X + ((px - r.X) % r.Width + r.Width) % r.Width;
                    if ((uint)(py - r.Y) >= (uint)r.Height)
                        py = r.Y + ((py - r.Y) % r.Height + r.Height) % r.Height;
                }
                else if ((uint)(px - r.X) >= (uint)r.Width || (uint)(py - r.Y) >= (uint)r.Height)
                    continue;

                if (mask != null && mask[px, py] == 0) continue;

                ref var slot = ref pixels[px, py];
                if (alphaLock && slot.A == 0) continue;

                if (inDisc)
                {
                    slot = blend && color.A < 255 ? Color32.Blend(slot, color) : color;
                    continue;
                }

                SeedApronPixel(ref slot, color);
            }

        // Report the brush footprint (in absolute canvas pixel coords) so the compositor
        // can re-composite / re-upload only this rect. Tiling wraps pixels to the opposite
        // edge which makes the dirty region non-rectangular — fall back to full rebuild.
        if (tiling)
        {
            InvalidateCompositeFullRebuild();
        }
        else
        {
            var minX = pixel.X - offset - apronBox;
            var minY = pixel.Y - offset - apronBox;
            var sz = size + apronBox * 2;
            InvalidateComposite(new RectInt(minX, minY, sz, sz));
        }
    }

    // Apron: seed empty pixels around the disc with the brush RGB at alpha=0 so bilinear
    // filtering doesn't interpolate painted pixels against (0,0,0,0) neighbors and produce
    // a black fringe.
    private static void SeedApronPixel(ref Color32 slot, Color32 color)
    {
        if (color.A == 0) return;
        if (slot.A != 0 || (slot.R | slot.G | slot.B) != 0) return;
        slot = new Color32(color.R, color.G, color.B, 0);
    }

    public void PaintBrushSoft(Vector2 centerPixel, Color32 color, float hardness, bool erase = false)
    {
        var layer = _softStrokeLayer ?? ActiveLayer;
        if (layer?.Pixels == null) return;
        if (!_softStrokeActive || _softStrokeOriginal == null || _softStrokeCoverage == null) return;

        var h = Math.Clamp(hardness, 0f, 1f);
        var outerR = BrushSize * 0.5f;
        var fadeWidth = 1f + outerR * (1f - h);
        var effOuter = outerR + fadeWidth * 0.5f;
        var hardR = MathF.Max(0f, effOuter - fadeWidth);
        var invFade = 1f / fadeWidth;
        var effOuter2 = effOuter * effOuter;
        var hardR2 = hardR * hardR;

        const float ApronWidth = 2f;
        var apronR = effOuter + ApronWidth;
        var apronR2 = apronR * apronR;
        var box = (int)MathF.Ceiling(apronR);

        var cx0 = (int)MathF.Floor(centerPixel.X);
        var cy0 = (int)MathF.Floor(centerPixel.Y);

        var tiling = Document.ShowTiling;
        var rect = EditablePixelRect;
        var epr = _softStrokeRect;
        var cover = _softStrokeCoverage;
        var original = _softStrokeOriginal;
        var pixels = layer.Pixels;
        var mask = Document.SelectionMask;
        var alphaLock = AlphaLock;

        for (var dy = -box; dy <= box; dy++)
            for (var dx = -box; dx <= box; dx++)
            {
                // Distance is computed from the unwrapped (virtual) pixel position so that a
                // stamp near a tiling seam still counts pixels within the disc — they just get
                // wrapped to the opposite edge for the actual layer write.
                var virtPx = cx0 + dx;
                var virtPy = cy0 + dy;
                var ox = (virtPx + 0.5f) - centerPixel.X;
                var oy = (virtPy + 0.5f) - centerPixel.Y;
                var d2 = ox * ox + oy * oy;

                var px = virtPx;
                var py = virtPy;
                if (tiling)
                {
                    if ((uint)(px - rect.X) >= (uint)rect.Width)
                        px = rect.X + ((px - rect.X) % rect.Width + rect.Width) % rect.Width;
                    if ((uint)(py - rect.Y) >= (uint)rect.Height)
                        py = rect.Y + ((py - rect.Y) % rect.Height + rect.Height) % rect.Height;
                }
                else if ((uint)(px - rect.X) >= (uint)rect.Width || (uint)(py - rect.Y) >= (uint)rect.Height)
                    continue;

                if (mask != null && mask[px, py] == 0) continue;

                // Squared-space coverage: pixels in the solid core (d2 <= hardR2) and fully
                // outside (d2 >= effOuter2) skip the sqrt entirely. Only the fade ramp needs d.
                float cov;
                if (d2 >= effOuter2)
                    cov = 0f;
                else if (d2 <= hardR2)
                    cov = 1f;
                else
                    cov = (effOuter - MathF.Sqrt(d2)) * invFade;

                var bx = px - epr.X;
                var by = py - epr.Y;
                var inBuffer = (uint)bx < (uint)epr.Width && (uint)by < (uint)epr.Height;

                ref var slot = ref pixels[px, py];

                if (cov <= 0f)
                {
                    if (erase) continue;
                    if (d2 > apronR2) continue;
                    SeedApronPixel(ref slot, color);
                    continue;
                }

                if (alphaLock && slot.A == 0) continue;
                if (!inBuffer) continue;

                // Max-blend coverage accumulator: the pixel's final alpha within this stroke is
                // the maximum coverage from any stamp, not the sum — dense sub-pixel stamps
                // don't pile alpha above 1.0.
                ref var coverSlot = ref cover[bx, by];
                if (cov <= coverSlot) continue;
                coverSlot = cov;

                if (erase)
                {
                    // Soft erase: reduce the original pixel's alpha by the accumulated coverage.
                    var orig = original[bx, by];
                    var newA = (byte)(orig.A * (1f - coverSlot) + 0.5f);
                    slot = new Color32(orig.R, orig.G, orig.B, newA);
                    continue;
                }

                var srcA = (color.A / 255f) * cov;
                if (srcA <= 0f) continue;
                var src = new Color32(color.R, color.G, color.B, (byte)(srcA * 255f + 0.5f));
                if (src.A == 0) continue;

                slot = Color32.Blend(original[bx, by], src);
            }

        if (tiling)
        {
            InvalidateCompositeFullRebuild();
        }
        else
        {
            var sz = box * 2 + 1;
            InvalidateComposite(new RectInt(cx0 - box, cy0 - box, sz, sz));
        }
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

    public void DrawSoftBrushOutline(Vector2 centerPixel, Color color)
    {
        var canvas = CanvasRect;
        var epr = EditablePixelRect;
        var cellW = canvas.Width / epr.Width;
        var cellH = canvas.Height / epr.Height;
        var cx = canvas.X + (centerPixel.X - epr.X) * cellW;
        var cy = canvas.Y + (centerPixel.Y - epr.Y) * cellH;
        var radiusX = BrushSize * 0.5f * cellW;
        var radiusY = BrushSize * 0.5f * cellH;
        var halfWidth = EditorStyle.Workspace.DocumentBoundsLineWidth * Gizmos.ZoomRefScale;

        const int Segments = 48;
        var vertCount = Segments * 4;
        var idxCount = Segments * 6;
        Span<MeshVertex> verts = stackalloc MeshVertex[vertCount];
        Span<ushort> indices = stackalloc ushort[idxCount];

        var angleStep = MathF.PI * 2f / Segments;
        for (var i = 0; i < Segments; i++)
        {
            var a0 = i * angleStep;
            var a1 = (i + 1) * angleStep;
            var v0 = new Vector2(cx + MathF.Cos(a0) * radiusX, cy + MathF.Sin(a0) * radiusY);
            var v1 = new Vector2(cx + MathF.Cos(a1) * radiusX, cy + MathF.Sin(a1) * radiusY);

            var delta = v1 - v0;
            var length = delta.Length();
            if (length < 1e-6f) continue;
            var dir = delta / length;
            var perp = new Vector2(-dir.Y, dir.X);

            var vi = i * 4;
            verts[vi + 0] = new MeshVertex(v0.X - perp.X * halfWidth, v0.Y - perp.Y * halfWidth, 0, 0, Color.White);
            verts[vi + 1] = new MeshVertex(v0.X + perp.X * halfWidth, v0.Y + perp.Y * halfWidth, 1, 0, Color.White);
            verts[vi + 2] = new MeshVertex(v1.X + perp.X * halfWidth, v1.Y + perp.Y * halfWidth, 1, 1, Color.White);
            verts[vi + 3] = new MeshVertex(v1.X - perp.X * halfWidth, v1.Y - perp.Y * halfWidth, 0, 1, Color.White);

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

    public void DrawBrushOutline(Vector2Int pixel, Color color)
    {
        var size = Math.Max(1, (int)BrushSize);
        var runs = GetBrushRuns(size);
        if (runs.Length == 0) return;

        var canvas = CanvasRect;
        var epr = EditablePixelRect;
        var cellW = canvas.Width / epr.Width;
        var cellH = canvas.Height / epr.Height;
        var brushOffset = (size - 1) / 2;
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
