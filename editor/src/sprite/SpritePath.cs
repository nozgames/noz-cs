//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using Clipper2Lib;

namespace NoZ.Editor;

public enum SpritePathOperation : byte
{
    Normal,
    Subtract,
    Clip
}

public enum SpriteStrokeJoin : byte
{
    Round,
    Miter,
    Bevel
}

public class SpritePath : SpriteNode
{
    public const int MinSegmentSamples = 4;
    public const int MaxSegmentSamples = 16;
    public static float StrokeScale => EditorApplication.Config.PixelsPerUnitInv;
    public const float MinCurve = 0.0001f;

    public static int ComputeSegmentSamples(float curve, float segmentLength)
    {
        if (MathF.Abs(curve) < MinCurve) return 0;
        var ratio = MathF.Abs(curve) / MathF.Max(segmentLength, 0.001f);
        return Math.Clamp((int)(ratio * 32) + 4, MinSegmentSamples, MaxSegmentSamples);
    }

    public List<SpriteContour> Contours { get; } = new() { new SpriteContour() };

    public List<SpritePathAnchor> Anchors => Contours[0].Anchors;
    public bool Open { get => Contours[0].Open; set => Contours[0].Open = value; }

    public Color32 FillColor { get; set; } = Color32.White;
    public Color32 StrokeColor { get; set; } = new(0, 0, 0, 0);
    public byte StrokeWidth { get; set; }
    public SpritePathOperation Operation { get; set; } = SpritePathOperation.Normal;
    public SpriteStrokeJoin StrokeJoin { get; set; } = SpriteStrokeJoin.Round;

    // Per-path transform (V-mode operations modify these)
    public Vector2 PathTranslation { get; set; }
    public float PathRotation { get; set; }
    public Vector2 PathScale { get; set; } = Vector2.One;

    public bool HasTransform =>
        PathTranslation != Vector2.Zero ||
        PathRotation != 0f ||
        PathScale != Vector2.One;

    public Matrix3x2 PathTransform
    {
        get
        {
            if (!HasTransform) return Matrix3x2.Identity;
            var center = LocalBounds.Center;
            return Matrix3x2.CreateTranslation(-center)
                 * Matrix3x2.CreateScale(PathScale)
                 * Matrix3x2.CreateRotation(PathRotation)
                 * Matrix3x2.CreateTranslation(center)
                 * Matrix3x2.CreateTranslation(PathTranslation);
        }
    }

    public Rect LocalBounds { get; private set; }
    public Rect Bounds { get; private set; }

    public bool IsSubtract => Operation == SpritePathOperation.Subtract;
    public bool IsClip => Operation == SpritePathOperation.Clip;

    // Cached Clipper2 paths — invalidated when anchors or transform change
    private PathsD? _cachedClipperPaths;
    private PathsD? _cachedContractedPaths;
    private bool _contractedComputed;

    public PathsD GetClipperPaths()
    {
        if (_cachedClipperPaths == null)
            _cachedClipperPaths = SpritePathClipper.SpritePathToPaths(this);
        return _cachedClipperPaths;
    }

    public PathsD? GetContractedPaths()
    {
        if (!_contractedComputed)
        {
            _contractedComputed = true;
            var contours = GetClipperPaths();
            if (contours.Count > 0 && StrokeColor.A > 0 && StrokeWidth > 0)
            {
                _cachedContractedPaths = Clipper.InflatePaths(contours,
                    -(StrokeWidth * StrokeScale),
                    SpriteLayerProcessor.ToClipperJoinType(StrokeJoin),
                    EndType.Polygon, precision: SpriteLayerProcessor.ClipperPrecision);
            }
        }
        return _cachedContractedPaths;
    }

    public int TotalAnchorCount
    {
        get
        {
            var total = 0;
            foreach (var c in Contours)
                total += c.Anchors.Count;
            return total;
        }
    }

    #region Samples

    public ReadOnlySpan<Vector2> GetSegmentSamples(int anchorIndex)
        => Contours[0].GetSegmentSamples(anchorIndex);

    public ReadOnlySpan<Vector2> GetSegmentSamples(int contourIndex, int anchorIndex)
        => Contours[contourIndex].GetSegmentSamples(anchorIndex);

    public void MarkDirty()
    {
        foreach (var c in Contours)
            c.MarkDirty();
        _cachedClipperPaths = null;
        _cachedContractedPaths = null;
        _contractedComputed = false;
    }

    public void UpdateSamples()
    {
        foreach (var c in Contours)
            c.UpdateSamples();
    }

    #endregion

    #region Bounds

    public void UpdateBounds()
    {
        var hasAnchors = false;
        foreach (var c in Contours)
        {
            if (c.Anchors.Count > 0) { hasAnchors = true; break; }
        }

        if (!hasAnchors)
        {
            Bounds = Rect.Zero;
            return;
        }

        UpdateSamples();

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);

        foreach (var contour in Contours)
        {
            var count = contour.Anchors.Count;
            if (count == 0) continue;
            var segmentCount = contour.Open ? count - 1 : count;

            for (var i = 0; i < count; i++)
            {
                var pos = contour.Anchors[i].Position;
                min = Vector2.Min(min, pos);
                max = Vector2.Max(max, pos);

                if (i < segmentCount && MathF.Abs(contour.Anchors[i].Curve) > MinCurve)
                {
                    var samples = contour.GetSegmentSamples(i);
                    foreach (var sample in samples)
                    {
                        min = Vector2.Min(min, sample);
                        max = Vector2.Max(max, sample);
                    }
                }
            }
        }

        LocalBounds = Rect.FromMinMax(min, max);

        if (HasTransform)
        {
            var transform = PathTransform;
            var wMin = new Vector2(float.MaxValue, float.MaxValue);
            var wMax = new Vector2(float.MinValue, float.MinValue);

            foreach (var contour in Contours)
            {
                var count = contour.Anchors.Count;
                if (count == 0) continue;
                var segmentCount = contour.Open ? count - 1 : count;

                for (var i = 0; i < count; i++)
                {
                    var tp = Vector2.Transform(contour.Anchors[i].Position, transform);
                    wMin = Vector2.Min(wMin, tp);
                    wMax = Vector2.Max(wMax, tp);

                    if (i < segmentCount && MathF.Abs(contour.Anchors[i].Curve) > MinCurve)
                    {
                        var samples = contour.GetSegmentSamples(i);
                        foreach (var sample in samples)
                        {
                            var ts = Vector2.Transform(sample, transform);
                            wMin = Vector2.Min(wMin, ts);
                            wMax = Vector2.Max(wMax, ts);
                        }
                    }
                }
            }

            Bounds = Rect.FromMinMax(wMin, wMax);
        }
        else
        {
            Bounds = LocalBounds;
        }
    }

    public void CompensateTranslation(Vector2 oldCenter)
    {
        if (!HasTransform) return;
        var newCenter = LocalBounds.Center;
        if (oldCenter == newCenter) return;

        var sr = Matrix3x2.CreateScale(PathScale) * Matrix3x2.CreateRotation(PathRotation);
        var oldTerm = oldCenter - Vector2.Transform(oldCenter, sr);
        var newTerm = newCenter - Vector2.Transform(newCenter, sr);
        PathTranslation += oldTerm - newTerm;
    }

    #endregion

    #region Selection

    public bool HasSelection()
    {
        foreach (var contour in Contours)
            for (var i = 0; i < contour.Anchors.Count; i++)
                if (contour.Anchors[i].IsSelected)
                    return true;
        return false;
    }

    public void ClearAnchorSelection()
    {
        foreach (var contour in Contours)
        {
            for (var i = 0; i < contour.Anchors.Count; i++)
            {
                var a = contour.Anchors[i];
                if (a.IsSelected)
                {
                    a.Flags &= ~SpritePathAnchorFlags.Selected;
                    contour.Anchors[i] = a;
                }
            }
        }
    }

    public new void ClearSelection()
    {
        ClearAnchorSelection();
        IsSelected = false;
    }

    public void SetAnchorSelected(int index, bool selected)
        => SetAnchorSelected(0, index, selected);

    public void SetAnchorSelected(int contourIndex, int anchorIndex, bool selected)
    {
        var anchors = Contours[contourIndex].Anchors;
        var a = anchors[anchorIndex];
        if (selected)
            a.Flags |= SpritePathAnchorFlags.Selected;
        else
            a.Flags &= ~SpritePathAnchorFlags.Selected;
        anchors[anchorIndex] = a;
    }

    public void SelectPath() => IsSelected = true;
    public void DeselectPath() => IsSelected = false;

    public void SelectAll()
    {
        foreach (var contour in Contours)
        {
            for (var i = 0; i < contour.Anchors.Count; i++)
            {
                var a = contour.Anchors[i];
                a.Flags |= SpritePathAnchorFlags.Selected;
                contour.Anchors[i] = a;
            }
        }
    }

    public new void SelectAnchorsInRect(Rect rect)
    {
        var hasXform = HasTransform;
        var xform = hasXform ? PathTransform : Matrix3x2.Identity;

        foreach (var contour in Contours)
        {
            for (var i = 0; i < contour.Anchors.Count; i++)
            {
                var pos = hasXform ? Vector2.Transform(contour.Anchors[i].Position, xform) : contour.Anchors[i].Position;
                if (rect.Contains(pos))
                    SetAnchorSelected(Contours.IndexOf(contour), i, true);
            }
        }
    }

    public bool IsSegmentSelected(int anchorIndex)
        => IsSegmentSelected(0, anchorIndex);

    public bool IsSegmentSelected(int contourIndex, int anchorIndex)
    {
        var anchors = Contours[contourIndex].Anchors;
        var nextIndex = (anchorIndex + 1) % anchors.Count;
        return anchors[anchorIndex].IsSelected && anchors[nextIndex].IsSelected;
    }

    #endregion

    #region Hit Testing

    public struct HitResult
    {
        public int ContourIndex;
        public int AnchorIndex;
        public int SegmentIndex;
        public float AnchorDistSqr;
        public float SegmentDistSqr;
        public Vector2 AnchorPosition;
        public Vector2 SegmentPosition;
        public bool InPath;

        public static HitResult Empty => new()
        {
            ContourIndex = -1,
            AnchorIndex = -1,
            SegmentIndex = -1,
            AnchorDistSqr = float.MaxValue,
            SegmentDistSqr = float.MaxValue,
        };
    }

    private void TransformPointAndRadius(ref Vector2 point, ref float radius)
    {
        if (!HasTransform) return;
        Matrix3x2.Invert(PathTransform, out var inv);
        point = Vector2.Transform(point, inv);
        var scaleX = MathF.Sqrt(inv.M11 * inv.M11 + inv.M12 * inv.M12);
        var scaleY = MathF.Sqrt(inv.M21 * inv.M21 + inv.M22 * inv.M22);
        radius *= MathF.Max(scaleX, scaleY);
    }

    public (int ContourIndex, int AnchorIndex, float DistSqr, Vector2 Position) HitTestAnchor(Vector2 point)
    {
        var radius = EditorStyle.SpritePath.AnchorHitRadius;
        TransformPointAndRadius(ref point, ref radius);
        var radiusSqr = radius * radius;

        var bestContour = -1;
        var bestIndex = -1;
        var bestDistSqr = float.MaxValue;
        var bestPos = Vector2.Zero;

        for (var ci = 0; ci < Contours.Count; ci++)
        {
            var anchors = Contours[ci].Anchors;
            for (var i = 0; i < anchors.Count; i++)
            {
                var distSqr = Vector2.DistanceSquared(point, anchors[i].Position);
                if (distSqr < radiusSqr && distSqr < bestDistSqr)
                {
                    bestContour = ci;
                    bestIndex = i;
                    bestDistSqr = distSqr;
                    bestPos = anchors[i].Position;
                }
            }
        }

        return (bestContour, bestIndex, bestDistSqr, bestPos);
    }

    public (int ContourIndex, int SegmentIndex, float DistSqr, Vector2 Position) HitTestSegment(Vector2 point)
    {
        var radius = EditorStyle.SpritePath.SegmentHitRadius;
        TransformPointAndRadius(ref point, ref radius);
        var radiusSqr = radius * radius;

        var bestContour = -1;
        var bestIndex = -1;
        var bestDistSqr = float.MaxValue;
        var bestPos = Vector2.Zero;

        for (var ci = 0; ci < Contours.Count; ci++)
        {
            var contour = Contours[ci];
            contour.UpdateSamples();
            var anchors = contour.Anchors;
            var count = anchors.Count;
            var segmentCount = contour.Open ? count - 1 : count;

            for (var i = 0; i < segmentCount; i++)
            {
                var nextIdx = (i + 1) % count;
                var samples = contour.GetSegmentSamples(i);

                var prev = anchors[i].Position;
                var segBestDistSqr = float.MaxValue;
                var segBestClosest = prev;
                foreach (var sample in samples)
                {
                    var distSqr = PointToSegmentDistSqr(point, prev, sample, out var closest);
                    if (distSqr < segBestDistSqr)
                    {
                        segBestDistSqr = distSqr;
                        segBestClosest = closest;
                    }
                    prev = sample;
                }
                var lastDistSqr = PointToSegmentDistSqr(point, prev, anchors[nextIdx].Position, out var lastClosest);
                if (lastDistSqr < segBestDistSqr)
                {
                    segBestDistSqr = lastDistSqr;
                    segBestClosest = lastClosest;
                }

                if (segBestDistSqr < radiusSqr && segBestDistSqr < bestDistSqr)
                {
                    bestContour = ci;
                    bestIndex = i;
                    bestDistSqr = segBestDistSqr;
                    bestPos = segBestClosest;
                }
            }
        }

        return (bestContour, bestIndex, bestDistSqr, bestPos);
    }

    public new bool HitTestPath(Vector2 point)
    {
        // Use Clipper paths (same geometry as rendering) for containment.
        // Clipper paths have PathTransform baked in, so point must be in document space.
        var paths = GetClipperPaths();
        if (paths.Count == 0) return false;

        var p = new PointD(point.X, point.Y);
        foreach (var path in paths)
        {
            if (Clipper.PointInPolygon(p, path) != PointInPolygonResult.IsOutside)
                return true;
        }
        return false;
    }

    public HitResult HitTest(Vector2 point)
    {
        var result = HitResult.Empty;

        var anchor = HitTestAnchor(point);
        if (anchor.AnchorIndex >= 0)
        {
            result.ContourIndex = anchor.ContourIndex;
            result.AnchorIndex = anchor.AnchorIndex;
            result.AnchorDistSqr = anchor.DistSqr;
            result.AnchorPosition = anchor.Position;
        }

        var segment = HitTestSegment(point);
        if (segment.SegmentIndex >= 0)
        {
            // Use segment's contour index if no anchor hit, or if segment is closer
            if (result.ContourIndex < 0)
                result.ContourIndex = segment.ContourIndex;
            result.SegmentIndex = segment.SegmentIndex;
            result.SegmentDistSqr = segment.DistSqr;
            result.SegmentPosition = segment.Position;
        }

        result.InPath = HitTestPath(point);
        return result;
    }

    public bool ContainsPoint(Vector2 point)
    {
        // Need at least one closed contour with 3+ anchors
        var hasClosedContour = false;
        foreach (var c in Contours)
        {
            if (!c.Open && c.Anchors.Count >= 3) { hasClosedContour = true; break; }
        }
        if (!hasClosedContour) return false;
        if (!LocalBounds.Contains(point)) return false;

        // Winding number test across all contours (holes cancel via opposite winding)
        var winding = 0;
        foreach (var contour in Contours)
        {
            if (contour.Open || contour.Anchors.Count < 3) continue;
            contour.UpdateSamples();
            var count = contour.Anchors.Count;

            for (var i = 0; i < count; i++)
            {
                var nextIdx = (i + 1) % count;
                var samples = contour.GetSegmentSamples(i);

                var prev = contour.Anchors[i].Position;
                foreach (var sample in samples)
                {
                    winding += WindingSegment(point, prev, sample);
                    prev = sample;
                }
                winding += WindingSegment(point, prev, contour.Anchors[nextIdx].Position);
            }
        }

        return winding != 0;
    }

    private static int WindingSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        if (a.Y <= p.Y)
        {
            if (b.Y > p.Y && Cross(b - a, p - a) > 0)
                return 1;
        }
        else
        {
            if (b.Y <= p.Y && Cross(b - a, p - a) < 0)
                return -1;
        }
        return 0;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static float ProjectCurve(Vector2 p0, Vector2 p1, Vector2 control)
    {
        var mid = (p0 + p1) * 0.5f;
        var dir = p1 - p0;
        var perp = new Vector2(-dir.Y, dir.X);
        var len = perp.Length();
        if (len < float.Epsilon) return 0f;
        perp /= len;
        return Vector2.Dot(control - mid, perp);
    }

    private static float PointToSegmentDistSqr(Vector2 point, Vector2 a, Vector2 b, out Vector2 closest)
    {
        var ab = b - a;
        var ap = point - a;
        var t = Vector2.Dot(ap, ab);
        var lenSqr = Vector2.Dot(ab, ab);

        if (lenSqr < float.Epsilon)
        {
            closest = a;
            return Vector2.DistanceSquared(point, a);
        }

        t = Math.Clamp(t / lenSqr, 0f, 1f);
        closest = a + ab * t;
        return Vector2.DistanceSquared(point, closest);
    }

    #endregion

    #region Manipulation

    public void SnapSelectedToPixelGrid()
    {
        foreach (var contour in Contours)
        {
            for (var i = 0; i < contour.Anchors.Count; i++)
            {
                if (!contour.Anchors[i].IsSelected) continue;
                var a = contour.Anchors[i];
                a.Position = Grid.SnapToPixelGrid(a.Position);
                contour.Anchors[i] = a;
            }
        }
        MarkDirty();
    }

    public Vector2? GetSelectedCentroid()
    {
        var sum = Vector2.Zero;
        var count = 0;
        foreach (var contour in Contours)
        {
            for (var i = 0; i < contour.Anchors.Count; i++)
            {
                if (!contour.Anchors[i].IsSelected) continue;
                sum += contour.Anchors[i].Position;
                count++;
            }
        }
        return count > 0 ? sum / count : null;
    }

    public SpritePathAnchor[] SnapshotAnchors()
        => SnapshotAnchors(0);

    public SpritePathAnchor[] SnapshotAnchors(int contourIndex)
    {
        var anchors = Contours[contourIndex].Anchors;
        var snapshot = new SpritePathAnchor[anchors.Count];
        for (var i = 0; i < anchors.Count; i++)
            snapshot[i] = anchors[i];
        return snapshot;
    }

    public SpritePathAnchor[][] SnapshotAllAnchors()
    {
        var result = new SpritePathAnchor[Contours.Count][];
        for (var ci = 0; ci < Contours.Count; ci++)
            result[ci] = SnapshotAnchors(ci);
        return result;
    }

    public void SetAnchorCurve(int index, float curve)
        => SetAnchorCurve(0, index, curve);

    public void SetAnchorCurve(int contourIndex, int anchorIndex, float curve)
    {
        var anchors = Contours[contourIndex].Anchors;
        var a = anchors[anchorIndex];
        a.Curve = ClampCurve(curve);
        anchors[anchorIndex] = a;
        MarkDirty();
    }

    public void DeleteSelectedAnchors()
    {
        foreach (var contour in Contours)
        {
            for (var i = contour.Anchors.Count - 1; i >= 0; i--)
            {
                if (contour.Anchors[i].IsSelected)
                    contour.Anchors.RemoveAt(i);
            }
        }

        // Remove empty contours (but keep at least the primary one)
        for (var ci = Contours.Count - 1; ci > 0; ci--)
        {
            if (Contours[ci].Anchors.Count == 0)
                Contours.RemoveAt(ci);
        }

        MarkDirty();
    }

    public int AddAnchor(Vector2 position, float curve = 0)
    {
        // Skip duplicate positions
        if (Anchors.Count > 0)
        {
            var last = Anchors[^1].Position;
            if (Vector2.DistanceSquared(last, position) < float.Epsilon)
                return Anchors.Count - 1;
        }

        Anchors.Add(new SpritePathAnchor { Position = position, Curve = curve });
        Contours[0].MarkDirty();
        return Anchors.Count - 1;
    }

    public void InsertAnchor(int afterIndex, Vector2 position, float curve = 0)
    {
        Anchors.Insert(afterIndex + 1, new SpritePathAnchor { Position = position, Curve = curve });
        Contours[0].MarkDirty();
    }

    public void SplitSegmentAtPoint(int anchorIndex, Vector2 targetPoint)
        => SplitSegmentAtPoint(0, anchorIndex, targetPoint);

    public void SplitSegmentAtPoint(int contourIndex, int anchorIndex, Vector2 targetPoint)
    {
        var contour = Contours[contourIndex];
        contour.UpdateSamples();

        var nextIdx = (anchorIndex + 1) % contour.Anchors.Count;
        var a0 = contour.Anchors[anchorIndex];
        var a1 = contour.Anchors[nextIdx];
        var samples = contour.GetSegmentSamples(anchorIndex);

        // Find best T on the piecewise linear approximation
        var bestT = 0f;
        var bestDistSqr = float.MaxValue;
        var totalSegments = samples.Length + 1;

        var prev = a0.Position;
        for (var s = 0; s <= samples.Length; s++)
        {
            var next = s < samples.Length ? samples[s] : a1.Position;
            var distSqr = PointToSegmentDistSqr(targetPoint, prev, next, out var segClosest);
            if (distSqr < bestDistSqr)
            {
                bestDistSqr = distSqr;
                var segLenSqr = Vector2.DistanceSquared(prev, next);
                var segT = segLenSqr < float.Epsilon ? 0.5f : Vector2.Dot(segClosest - prev, next - prev) / segLenSqr;
                bestT = (s + segT) / totalSegments;
            }
            prev = next;
        }

        // Compute split point and sub-curve values
        Vector2 splitPoint;
        var leftCurve = 0f;
        var rightCurve = 0f;
        if (MathF.Abs(a0.Curve) < MinCurve)
        {
            splitPoint = Vector2.Lerp(a0.Position, a1.Position, bestT);
        }
        else
        {
            var mid = (a0.Position + a1.Position) * 0.5f;
            var dir = a1.Position - a0.Position;
            var perp = new Vector2(-dir.Y, dir.X);
            var len = perp.Length();
            if (len > 0) perp /= len;
            var control = mid + perp * a0.Curve;

            var u = 1 - bestT;
            splitPoint = u * u * a0.Position + 2 * u * bestT * control + bestT * bestT * a1.Position;

            // De Casteljau split — compute sub-control points
            var cLeft = Vector2.Lerp(a0.Position, control, bestT);
            var cRight = Vector2.Lerp(control, a1.Position, bestT);
            leftCurve = ProjectCurve(a0.Position, splitPoint, cLeft);
            rightCurve = ProjectCurve(splitPoint, a1.Position, cRight);
        }

        // Insert the new anchor with sub-curve values
        var insertIdx = anchorIndex + 1;
        if (insertIdx > contour.Anchors.Count) insertIdx = contour.Anchors.Count;
        contour.Anchors.Insert(insertIdx, new SpritePathAnchor { Position = splitPoint, Curve = rightCurve });

        var aa = contour.Anchors[anchorIndex];
        aa.Curve = leftCurve;
        contour.Anchors[anchorIndex] = aa;

        contour.MarkDirty();
    }

    #endregion

    // Split this path into two paths at the given anchor indices.
    // Returns the new second path. This path becomes the first path.
    // Operates on the primary contour only.
    public SpritePath? SplitAtAnchors(int anchor1, int anchor2, ReadOnlySpan<Vector2> intermediatePoints, bool reverseIntermediates)
    {
        if (anchor1 < 0 || anchor2 < 0 || anchor1 >= Anchors.Count || anchor2 >= Anchors.Count)
            return null;
        if (anchor1 == anchor2)
            return null;

        var path1Anchors = new List<SpritePathAnchor>();
        var path2Anchors = new List<SpritePathAnchor>();

        for (var i = anchor1; ; i = (i + 1) % Anchors.Count)
        {
            var a = Anchors[i];
            a.Flags = SpritePathAnchorFlags.None;
            path1Anchors.Add(a);
            if (i == anchor2) break;
        }

        if (intermediatePoints.Length > 0)
        {
            if (reverseIntermediates)
            {
                for (var i = intermediatePoints.Length - 1; i >= 0; i--)
                    path1Anchors.Add(new SpritePathAnchor { Position = intermediatePoints[i] });
            }
            else
            {
                for (var i = 0; i < intermediatePoints.Length; i++)
                    path1Anchors.Add(new SpritePathAnchor { Position = intermediatePoints[i] });
            }
        }

        for (var i = anchor2; ; i = (i + 1) % Anchors.Count)
        {
            var a = Anchors[i];
            a.Flags = SpritePathAnchorFlags.None;
            path2Anchors.Add(a);
            if (i == anchor1) break;
        }

        if (intermediatePoints.Length > 0)
        {
            if (reverseIntermediates)
            {
                for (var i = 0; i < intermediatePoints.Length; i++)
                    path2Anchors.Add(new SpritePathAnchor { Position = intermediatePoints[i] });
            }
            else
            {
                for (var i = intermediatePoints.Length - 1; i >= 0; i--)
                    path2Anchors.Add(new SpritePathAnchor { Position = intermediatePoints[i] });
            }
        }

        Anchors.Clear();
        Anchors.AddRange(path1Anchors);
        MarkDirty();

        if (path2Anchors.Count < 3)
            return null;

        var newPath = new SpritePath
        {
            FillColor = FillColor,
            StrokeColor = StrokeColor,
            StrokeWidth = StrokeWidth,
            Operation = Operation,
        };
        newPath.Anchors.AddRange(path2Anchors);
        newPath.MarkDirty();
        return newPath;
    }

    #region Clone

    public override SpriteNode Clone()
    {
        var clone = new SpritePath
        {
            FillColor = FillColor,
            StrokeColor = StrokeColor,
            StrokeWidth = StrokeWidth,
            Operation = Operation,
            PathTranslation = PathTranslation,
            PathRotation = PathRotation,
            PathScale = PathScale,
        };
        ClonePropertiesTo(clone);

        // Clone primary contour (already exists in clone)
        clone.Contours[0].Open = Contours[0].Open;
        clone.Contours[0].Anchors.AddRange(Contours[0].Anchors);

        // Clone additional contours
        for (var ci = 1; ci < Contours.Count; ci++)
            clone.Contours.Add(Contours[ci].Clone());

        clone.UpdateSamples();
        clone.UpdateBounds();

        return clone;
    }

    public SpritePath ClonePath()
    {
        return (SpritePath)Clone();
    }

    #endregion

    internal static float ClampCurve(float curve)
    {
        if (MathF.Abs(curve) < MinCurve)
            return 0;
        return curve;
    }
}
