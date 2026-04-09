//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  CPU 4x MSAA polygon rasterizer. Each pixel has 4 independent sub-samples
//  tested against paths via non-zero winding. Paths composite back-to-front
//  with Porter-Duff over; Resolve() averages the samples. Sub-samples follow
//  the D3D11 4x rotated grid so each pixel row runs as 4 sub-scanlines on
//  an active edge table. Fill/Resolve dispatch rows across Parallel.For
//  above a small-sprite threshold; workers use per-thread NativeArray<T>
//  scratch and raw pointer sample writes.
//

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Clipper2Lib;

namespace NoZ.Editor;

[InlineArray(4)]
internal struct Sample4
{
    private Color32 _element0;
}

internal static unsafe class Rasterizer
{
    // Populated by the calling thread before Parallel.For dispatch, then
    // read-only for the duration of the parallel body.
    private static NativeArray<Edge> _edgeBuffer;

    [ThreadStatic] private static NativeArray<int> _aetPool;
    [ThreadStatic] private static NativeArray<float> _xsPool;
    [ThreadStatic] private static NativeArray<int> _wsPool;

    private static readonly Comparison<Edge> EdgeYMinComparison = (a, b) => a.YMin.CompareTo(b.YMin);

    // Rows below this count stay sequential — Parallel.For scheduling
    // overhead eats the savings on tiny sprites.
    private const int ParallelRowThreshold = 128;

    // D3D11 4x rotated grid, one sample per distinct sub-row.
    private static readonly (float X, float Y)[] SampleOffsets =
    {
        (0.375f, 0.125f),
        (0.875f, 0.375f),
        (0.125f, 0.625f),
        (0.625f, 0.875f),
    };

    public static void Fill(
        PathsD paths,
        PixelData<Sample4> samples,
        RectInt targetRect,
        Vector2Int sourceOffset,
        int dpi,
        Color32 color)
    {
        if (!PrepareEdges(paths, dpi, sourceOffset)) return;

        int w = targetRect.Width;
        int h = targetRect.Height;
        bool opaque = color.A == 255;
        Color32* samplesBase = (Color32*)samples.Ptr;
        int sampleStride = samples.Width;

        if (h >= ParallelRowThreshold)
        {
            var localColor = color;
            var localOpaque = opaque;
            var localW = w;
            var localStride = sampleStride;
            Parallel.For(0, h, py =>
                FillRow(py, localW, samplesBase, localStride, localColor, localOpaque));
        }
        else
        {
            for (int py = 0; py < h; py++)
                FillRow(py, w, samplesBase, sampleStride, color, opaque);
        }
    }

    private static bool PrepareEdges(PathsD paths, int dpi, Vector2Int sourceOffset)
    {
        if (paths.Count == 0) return false;

        int maxEdges = 0;
        foreach (var path in paths)
            maxEdges += path.Count;
        if (maxEdges == 0) return false;

        EnsureCapacity(ref _edgeBuffer, maxEdges);
        _edgeBuffer.Clear();
        CollectEdges(ref _edgeBuffer, paths, dpi, sourceOffset);
        if (_edgeBuffer.Length == 0) return false;

        _edgeBuffer.AsSpan().Sort(EdgeYMinComparison);
        return true;
    }

    private static void FillRow(
        int py,
        int w,
        Color32* samplesBase,
        int sampleStride,
        Color32 color,
        bool opaque)
    {
        if (!BuildActiveEdgeList(py, out int aetCount, out Edge* edgePtr, out int* aetPtr))
            return;

        // Sample4 is [InlineArray(4)] Color32, so each pixel is 4 contiguous slots.
        Color32* rowBase = samplesBase + (long)py * sampleStride * 4;

        for (int s = 0; s < 4; s++)
        {
            if (!ComputeCrossings(s, py, edgePtr, aetPtr, aetCount, out int n, out float* xs, out int* ws))
                continue;

            Color32* subRow = rowBase + s;
            float subX = SampleOffsets[s].X;
            int ci = 0;
            int winding = 0;

            if (opaque)
            {
                for (int px = 0; px < w; px++)
                {
                    float sampleX = px + subX;
                    while (ci < n && xs[ci] <= sampleX)
                    {
                        winding += ws[ci];
                        ci++;
                    }
                    if (winding != 0)
                        subRow[px * 4] = color;
                }
            }
            else
            {
                for (int px = 0; px < w; px++)
                {
                    float sampleX = px + subX;
                    while (ci < n && xs[ci] <= sampleX)
                    {
                        winding += ws[ci];
                        ci++;
                    }
                    if (winding != 0)
                    {
                        Color32* slot = subRow + px * 4;
                        if (slot->A == 0)
                            *slot = color;
                        else
                            *slot = Color32.Blend(*slot, color);
                    }
                }
            }
        }
    }

    private static bool BuildActiveEdgeList(
        int py,
        out int aetCount,
        out Edge* edgePtr,
        out int* aetPtr)
    {
        var edges = _edgeBuffer;
        int edgeCount = edges.Length;
        edgePtr = edges.Ptr;

        if (!_aetPool.IsCreated)
            _aetPool = new NativeArray<int>(128);
        _aetPool.Clear();

        if (!_xsPool.IsCreated)
            _xsPool = new NativeArray<float>(64);
        if (!_wsPool.IsCreated)
            _wsPool = new NativeArray<int>(64);

        float rowBot = py + 1;
        for (int i = 0; i < edgeCount; i++)
        {
            Edge* e = edgePtr + i;
            if (e->YMin >= rowBot) break;
            if (e->YMax > py)
            {
                if (_aetPool.Length >= _aetPool.Capacity)
                    EnsureCapacity(ref _aetPool, _aetPool.Capacity * 2);
                _aetPool.Add(i);
            }
        }
        if (_aetPool.Length == 0)
        {
            aetCount = 0;
            aetPtr = null;
            return false;
        }

        aetCount = _aetPool.Length;
        aetPtr = _aetPool.Ptr;
        return true;
    }

    private static bool ComputeCrossings(
        int s,
        int py,
        Edge* edgePtr,
        int* aetPtr,
        int aetCount,
        out int n,
        out float* xs,
        out int* ws)
    {
        float subY = py + SampleOffsets[s].Y;

        if (_xsPool.Capacity < aetCount)
            EnsureCapacity(ref _xsPool, aetCount);
        if (_wsPool.Capacity < aetCount)
            EnsureCapacity(ref _wsPool, aetCount);
        xs = _xsPool.Ptr;
        ws = _wsPool.Ptr;

        n = 0;
        for (int k = 0; k < aetCount; k++)
        {
            Edge* e = edgePtr + aetPtr[k];
            // Half-open [YMin, YMax) so edges touching a sub-row exactly
            // aren't double-counted.
            if (subY < e->YMin || subY >= e->YMax) continue;

            float t = (subY - e->Y0) / (e->Y1 - e->Y0);
            xs[n] = e->X0 + t * (e->X1 - e->X0);
            ws[n] = (int)e->Direction;
            n++;
        }

        if (n < 2) return false;

        for (int i = 1; i < n; i++)
        {
            float kx = xs[i];
            int kw = ws[i];
            int j = i - 1;
            while (j >= 0 && xs[j] > kx)
            {
                xs[j + 1] = xs[j];
                ws[j + 1] = ws[j];
                j--;
            }
            xs[j + 1] = kx;
            ws[j + 1] = kw;
        }
        return true;
    }

    public static void Resolve(
        PixelData<Color32> target,
        PixelData<Sample4> samples,
        RectInt targetRect)
    {
        int w = targetRect.Width;
        int h = targetRect.Height;
        int sampleStride = samples.Width;
        int targetStride = target.Width;

        Color32* samplesBase = (Color32*)samples.Ptr;
        Color32* targetBase = target.Ptr;

        if (h >= ParallelRowThreshold)
        {
            var localRect = targetRect;
            var localW = w;
            var localSampleStride = sampleStride;
            var localTargetStride = targetStride;
            Parallel.For(0, h, py =>
                ResolveRow(py, localW, samplesBase, targetBase,
                    localSampleStride, localTargetStride, localRect));
        }
        else
        {
            for (int py = 0; py < h; py++)
                ResolveRow(py, w, samplesBase, targetBase,
                    sampleStride, targetStride, targetRect);
        }
    }

    private static void ResolveRow(
        int py,
        int w,
        Color32* samplesBase,
        Color32* targetBase,
        int sampleStride,
        int targetStride,
        RectInt targetRect)
    {
        Color32* sRow = samplesBase + (long)py * sampleStride * 4;

        int ty = targetRect.Y + py;
        int tx0 = targetRect.X;
        Color32* tRow = targetBase + (long)ty * targetStride + tx0;

        for (int px = 0; px < w; px++)
        {
            Color32* s = sRow + px * 4;

            var c0 = s[0];
            var c1 = s[1];
            var c2 = s[2];
            var c3 = s[3];

            int sumA = c0.A + c1.A + c2.A + c3.A;
            if (sumA == 0) continue;

            // Weight RGB by per-sample alpha so transparent slots don't
            // drag the color toward black — produces straight-alpha output.
            int sumR = c0.R * c0.A + c1.R * c1.A + c2.R * c2.A + c3.R * c3.A;
            int sumG = c0.G * c0.A + c1.G * c1.A + c2.G * c2.A + c3.G * c3.A;
            int sumB = c0.B * c0.A + c1.B * c1.A + c2.B * c2.A + c3.B * c3.A;

            int halfA = sumA >> 1;
            var avg = new Color32(
                (byte)((sumR + halfA) / sumA),
                (byte)((sumG + halfA) / sumA),
                (byte)((sumB + halfA) / sumA),
                (byte)((sumA + 2) >> 2));

            Color32* dstPtr = tRow + px;
            *dstPtr = dstPtr->A == 0 ? avg : Color32.Blend(*dstPtr, avg);
        }
    }

    private static void EnsureCapacity<T>(ref NativeArray<T> array, int minCapacity)
        where T : unmanaged
    {
        if (array.IsCreated && array.Capacity >= minCapacity) return;

        int oldLength = array.IsCreated ? array.Length : 0;
        int newCapacity = Math.Max(minCapacity, array.IsCreated ? array.Capacity * 2 : 64);
        var newArray = new NativeArray<T>(newCapacity, oldLength);

        if (array.IsCreated)
        {
            if (oldLength > 0)
            {
                NativeMemory.Copy(
                    array.Ptr,
                    newArray.Ptr,
                    (nuint)(oldLength * (uint)Unsafe.SizeOf<T>()));
            }
            array.Dispose();
        }

        array = newArray;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Edge
    {
        public float X0, Y0;
        public float X1, Y1;
        public float YMin, YMax;
        public float Direction; // +1 downward, -1 upward
    }

    private static void CollectEdges(
        ref NativeArray<Edge> edges,
        PathsD paths,
        int dpi,
        Vector2Int sourceOffset)
    {
        foreach (var path in paths)
        {
            int count = path.Count;
            if (count < 3) continue;

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                float x0 = (float)(path[i].x * dpi) + sourceOffset.X;
                float y0 = (float)(path[i].y * dpi) + sourceOffset.Y;
                float x1 = (float)(path[j].x * dpi) + sourceOffset.X;
                float y1 = (float)(path[j].y * dpi) + sourceOffset.Y;

                float dy = y1 - y0;
                if (MathF.Abs(dy) < 1e-6f) continue;

                edges.Add(new Edge
                {
                    X0 = x0, Y0 = y0,
                    X1 = x1, Y1 = y1,
                    YMin = MathF.Min(y0, y1),
                    YMax = MathF.Max(y0, y1),
                    Direction = dy > 0 ? 1f : -1f,
                });
            }
        }
    }
}
