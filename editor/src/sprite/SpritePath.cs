//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public enum SpritePathOperation : byte
{
    Normal,
    Subtract,
    Clip
}

public class SpritePath : SpriteNode
{
    public const int MaxSegmentSamples = 8;
    public const float StrokeScale = 0.005f;
    public const float MinCurve = 0.0001f;

    public List<SpritePathAnchor> Anchors { get; } = new();
    public Color32 FillColor { get; set; } = Color32.White;
    public Color32 StrokeColor { get; set; } = new(0, 0, 0, 0);
    public byte StrokeWidth { get; set; }
    public SpritePathOperation Operation { get; set; } = SpritePathOperation.Normal;
    public bool Open { get; set; }

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

    // Cached bezier segment samples per anchor (recomputed when anchors change)
    private Vector2[]? _samples;
    private bool _samplesDirty = true;

    public bool IsSubtract => Operation == SpritePathOperation.Subtract;
    public bool IsClip => Operation == SpritePathOperation.Clip;

    #region Samples

    public ReadOnlySpan<Vector2> GetSegmentSamples(int anchorIndex)
    {
        if (_samplesDirty)
            UpdateSamples();

        var offset = anchorIndex * MaxSegmentSamples;
        return new ReadOnlySpan<Vector2>(_samples, offset, MaxSegmentSamples);
    }

    public void MarkDirty() => _samplesDirty = true;

    public void UpdateSamples()
    {
        var count = Anchors.Count;
        if (count == 0)
        {
            _samples = null;
            _samplesDirty = false;
            return;
        }

        var segmentCount = Open ? count - 1 : count;
        var totalSamples = count * MaxSegmentSamples;
        if (_samples == null || _samples.Length < totalSamples)
            _samples = new Vector2[totalSamples];

        for (var i = 0; i < segmentCount; i++)
        {
            var a0 = Anchors[i];
            var a1 = Anchors[(i + 1) % count];
            var offset = i * MaxSegmentSamples;

            if (MathF.Abs(a0.Curve) < MinCurve)
            {
                for (var s = 0; s < MaxSegmentSamples; s++)
                {
                    var t = (s + 1) / (float)(MaxSegmentSamples + 1);
                    _samples[offset + s] = Vector2.Lerp(a0.Position, a1.Position, t);
                }
            }
            else
            {
                var mid = (a0.Position + a1.Position) * 0.5f;
                var dir = a1.Position - a0.Position;
                var perp = new Vector2(-dir.Y, dir.X);
                var len = perp.Length();
                if (len > 0) perp /= len;
                var control = mid + perp * a0.Curve;

                for (var s = 0; s < MaxSegmentSamples; s++)
                {
                    var t = (s + 1) / (float)(MaxSegmentSamples + 1);
                    var u = 1.0f - t;
                    _samples[offset + s] =
                        u * u * a0.Position +
                        2 * u * t * control +
                        t * t * a1.Position;
                }
            }
        }

        _samplesDirty = false;
    }

    #endregion

    #region Bounds

    public void UpdateBounds()
    {
        if (Anchors.Count == 0)
        {
            Bounds = Rect.Zero;
            return;
        }

        if (_samplesDirty)
            UpdateSamples();

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var segmentCount = Open ? Anchors.Count - 1 : Anchors.Count;

        for (var i = 0; i < Anchors.Count; i++)
        {
            var pos = Anchors[i].Position;
            min = Vector2.Min(min, pos);
            max = Vector2.Max(max, pos);

            if (i < segmentCount && MathF.Abs(Anchors[i].Curve) > MinCurve)
            {
                var samples = GetSegmentSamples(i);
                for (var s = 0; s < MaxSegmentSamples; s++)
                {
                    min = Vector2.Min(min, samples[s]);
                    max = Vector2.Max(max, samples[s]);
                }
            }
        }

        var halfStroke = (StrokeColor.A > 0 && StrokeWidth > 0)
            ? StrokeWidth * StrokeScale : 0f;
        if (halfStroke > 0)
        {
            min -= new Vector2(halfStroke, halfStroke);
            max += new Vector2(halfStroke, halfStroke);
        }

        LocalBounds = Rect.FromMinMax(min, max);

        // Compute world bounds by transforming each anchor and sample individually
        // for a tight AABB (transforming LocalBounds corners over-approximates for rotations)
        if (HasTransform)
        {
            var transform = PathTransform;
            var wMin = new Vector2(float.MaxValue, float.MaxValue);
            var wMax = new Vector2(float.MinValue, float.MinValue);

            for (var i = 0; i < Anchors.Count; i++)
            {
                var tp = Vector2.Transform(Anchors[i].Position, transform);
                wMin = Vector2.Min(wMin, tp);
                wMax = Vector2.Max(wMax, tp);

                if (i < segmentCount && MathF.Abs(Anchors[i].Curve) > MinCurve)
                {
                    var samples = GetSegmentSamples(i);
                    for (var s = 0; s < MaxSegmentSamples; s++)
                    {
                        var ts = Vector2.Transform(samples[s], transform);
                        wMin = Vector2.Min(wMin, ts);
                        wMax = Vector2.Max(wMax, ts);
                    }
                }
            }

            if (halfStroke > 0)
            {
                wMin -= new Vector2(halfStroke, halfStroke);
                wMax += new Vector2(halfStroke, halfStroke);
            }

            Bounds = Rect.FromMinMax(wMin, wMax);
        }
        else
        {
            Bounds = LocalBounds;
        }
    }

    // After anchor edits, LocalBounds.Center may have shifted. Adjust PathTranslation
    // so the world-space appearance of the path is preserved with the new center.
    public void CompensateTranslationForCenterShift()
    {
        if (!HasTransform) return;

        var center = LocalBounds.Center;

        // World position of center under current transform:
        // T(-center) * S * R * T(center) * T(translation) applied to center = S*R*0 + center + translation = center + translation
        // This simplifies: the center always maps to (center + PathTranslation) regardless of S/R.
        // So if the center shifts from C_old to C_new, we need:
        //   C_old + T_old = C_new + T_new
        //   T_new = T_old + C_old - C_new
        // But we've already updated bounds, so LocalBounds.Center IS the new center.
        // We need the old center... which we don't have here.

        // Instead: recompute. The caller should capture oldCenter before UpdateBounds().
    }

    public void CompensateTranslation(Vector2 oldCenter)
    {
        if (!HasTransform) return;
        var newCenter = LocalBounds.Center;
        if (oldCenter == newCenter) return;

        // For point P, old transform: S*R*(P - oldCenter) + oldCenter + T_old
        // New transform with new center: S*R*(P - newCenter) + newCenter + T_new
        // Setting equal for unchanged P:
        //   S*R*(P - oldCenter) + oldCenter + T_old = S*R*(P - newCenter) + newCenter + T_new
        //   S*R*(-oldCenter) + oldCenter + T_old = S*R*(-newCenter) + newCenter + T_new
        //   T_new = T_old + (oldCenter - S*R*oldCenter) - (newCenter - S*R*newCenter)
        var sr = Matrix3x2.CreateScale(PathScale) * Matrix3x2.CreateRotation(PathRotation);
        var oldTerm = oldCenter - Vector2.Transform(oldCenter, sr);
        var newTerm = newCenter - Vector2.Transform(newCenter, sr);
        PathTranslation += oldTerm - newTerm;
    }

    #endregion

    #region Selection

    public bool HasSelection()
    {
        for (var i = 0; i < Anchors.Count; i++)
            if (Anchors[i].IsSelected)
                return true;
        return false;
    }

    public void ClearAnchorSelection()
    {
        for (var i = 0; i < Anchors.Count; i++)
        {
            var a = Anchors[i];
            if (a.IsSelected)
            {
                a.Flags &= ~SpritePathAnchorFlags.Selected;
                Anchors[i] = a;
            }
        }
    }

    public void ClearSelection()
    {
        ClearAnchorSelection();
        IsSelected = false;
    }

    public void SetAnchorSelected(int index, bool selected)
    {
        var a = Anchors[index];
        if (selected)
            a.Flags |= SpritePathAnchorFlags.Selected;
        else
            a.Flags &= ~SpritePathAnchorFlags.Selected;
        Anchors[index] = a;
    }

    public void SelectPath() => IsSelected = true;
    public void DeselectPath() => IsSelected = false;

    public void SelectAll()
    {
        for (var i = 0; i < Anchors.Count; i++)
        {
            var a = Anchors[i];
            a.Flags |= SpritePathAnchorFlags.Selected;
            Anchors[i] = a;
        }
    }

    public new void SelectAnchorsInRect(Rect rect)
    {
        var hasXform = HasTransform;
        var xform = hasXform ? PathTransform : Matrix3x2.Identity;

        for (var i = 0; i < Anchors.Count; i++)
        {
            var pos = hasXform ? Vector2.Transform(Anchors[i].Position, xform) : Anchors[i].Position;
            if (rect.Contains(pos))
                SetAnchorSelected(i, true);
        }
    }

    public bool IsSegmentSelected(int anchorIndex)
    {
        var nextIndex = (anchorIndex + 1) % Anchors.Count;
        return Anchors[anchorIndex].IsSelected && Anchors[nextIndex].IsSelected;
    }

    #endregion

    #region Hit Testing

    public struct HitResult
    {
        public int AnchorIndex;
        public int SegmentIndex;
        public float AnchorDistSqr;
        public float SegmentDistSqr;
        public Vector2 AnchorPosition;
        public Vector2 SegmentPosition;
        public bool InPath;

        public static HitResult Empty => new()
        {
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
        // Use the larger axis scale so the radius covers both directions
        var scaleX = MathF.Sqrt(inv.M11 * inv.M11 + inv.M12 * inv.M12);
        var scaleY = MathF.Sqrt(inv.M21 * inv.M21 + inv.M22 * inv.M22);
        radius *= MathF.Max(scaleX, scaleY);
    }

    public (int Index, float DistSqr, Vector2 Position) HitTestAnchor(Vector2 point)
    {
        var radius = EditorStyle.Shape.AnchorHitRadius;
        TransformPointAndRadius(ref point, ref radius);
        var radiusSqr = radius * radius;

        var bestIndex = -1;
        var bestDistSqr = float.MaxValue;
        var bestPos = Vector2.Zero;

        for (var i = 0; i < Anchors.Count; i++)
        {
            var distSqr = Vector2.DistanceSquared(point, Anchors[i].Position);
            if (distSqr < radiusSqr && distSqr < bestDistSqr)
            {
                bestIndex = i;
                bestDistSqr = distSqr;
                bestPos = Anchors[i].Position;
            }
        }

        return (bestIndex, bestDistSqr, bestPos);
    }

    public new (int Index, float DistSqr, Vector2 Position) HitTestSegment(Vector2 point)
    {
        var radius = EditorStyle.Shape.SegmentHitRadius;
        TransformPointAndRadius(ref point, ref radius);
        var radiusSqr = radius * radius;

        if (_samplesDirty) UpdateSamples();

        var bestIndex = -1;
        var bestDistSqr = float.MaxValue;
        var bestPos = Vector2.Zero;

        var segmentCount = Open ? Anchors.Count - 1 : Anchors.Count;
        for (var i = 0; i < segmentCount; i++)
        {
            var nextIdx = (i + 1) % Anchors.Count;
            var samples = GetSegmentSamples(i);

            var segBestDistSqr = PointToSegmentDistSqr(point, Anchors[i].Position, samples[0], out var segBestClosest);
            for (var s = 0; s < MaxSegmentSamples - 1; s++)
            {
                var distSqr = PointToSegmentDistSqr(point, samples[s], samples[s + 1], out var closest);
                if (distSqr < segBestDistSqr)
                {
                    segBestDistSqr = distSqr;
                    segBestClosest = closest;
                }
            }
            var lastDistSqr = PointToSegmentDistSqr(point, samples[MaxSegmentSamples - 1], Anchors[nextIdx].Position, out var lastClosest);
            if (lastDistSqr < segBestDistSqr)
            {
                segBestDistSqr = lastDistSqr;
                segBestClosest = lastClosest;
            }

            if (segBestDistSqr < radiusSqr && segBestDistSqr < bestDistSqr)
            {
                bestIndex = i;
                bestDistSqr = segBestDistSqr;
                bestPos = segBestClosest;
            }
        }

        return (bestIndex, bestDistSqr, bestPos);
    }

    public new bool HitTestPath(Vector2 point)
    {
        if (HasTransform)
        {
            Matrix3x2.Invert(PathTransform, out var inv);
            point = Vector2.Transform(point, inv);
        }
        return ContainsPoint(point);
    }

    // Combined hit test — used by HitTestAll for click cycling
    public HitResult HitTest(Vector2 point)
    {
        var result = HitResult.Empty;

        var anchor = HitTestAnchor(point);
        if (anchor.Index >= 0)
        {
            result.AnchorIndex = anchor.Index;
            result.AnchorDistSqr = anchor.DistSqr;
            result.AnchorPosition = anchor.Position;
        }

        var segment = HitTestSegment(point);
        if (segment.Index >= 0)
        {
            result.SegmentIndex = segment.Index;
            result.SegmentDistSqr = segment.DistSqr;
            result.SegmentPosition = segment.Position;
        }

        result.InPath = HitTestPath(point);
        return result;
    }

    public bool ContainsPoint(Vector2 point)
    {
        if (Open || Anchors.Count < 3) return false;
        if (!LocalBounds.Contains(point)) return false;

        // Winding number test using sampled segments
        var winding = 0;
        var segmentCount = Anchors.Count;

        for (var i = 0; i < segmentCount; i++)
        {
            var nextIdx = (i + 1) % Anchors.Count;
            var samples = GetSegmentSamples(i);

            // Test anchor→first sample, then sample chain, then last sample→next anchor
            winding += WindingSegment(point, Anchors[i].Position, samples[0]);
            for (var s = 0; s < MaxSegmentSamples - 1; s++)
                winding += WindingSegment(point, samples[s], samples[s + 1]);
            winding += WindingSegment(point, samples[MaxSegmentSamples - 1], Anchors[nextIdx].Position);
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
        for (var i = 0; i < Anchors.Count; i++)
        {
            if (!Anchors[i].IsSelected) continue;
            var a = Anchors[i];
            a.Position = Grid.SnapToPixelGrid(a.Position);
            Anchors[i] = a;
        }
        MarkDirty();
    }

    public Vector2? GetSelectedCentroid()
    {
        var sum = Vector2.Zero;
        var count = 0;
        for (var i = 0; i < Anchors.Count; i++)
        {
            if (!Anchors[i].IsSelected) continue;
            sum += Anchors[i].Position;
            count++;
        }
        return count > 0 ? sum / count : null;
    }

    public SpritePathAnchor[] SnapshotAnchors()
    {
        var snapshot = new SpritePathAnchor[Anchors.Count];
        for (var i = 0; i < Anchors.Count; i++)
            snapshot[i] = Anchors[i];
        return snapshot;
    }

    public void SetAnchorCurve(int index, float curve)
    {
        var a = Anchors[index];
        a.Curve = ClampCurve(curve);
        Anchors[index] = a;
        MarkDirty();
    }

    public void DeleteSelectedAnchors()
    {
        for (var i = Anchors.Count - 1; i >= 0; i--)
        {
            if (Anchors[i].IsSelected)
                Anchors.RemoveAt(i);
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
        MarkDirty();
        return Anchors.Count - 1;
    }

    public void InsertAnchor(int afterIndex, Vector2 position, float curve = 0)
    {
        Anchors.Insert(afterIndex + 1, new SpritePathAnchor { Position = position, Curve = curve });
        MarkDirty();
    }

    public void SplitSegmentAtPoint(int anchorIndex, Vector2 targetPoint)
    {
        if (_samplesDirty) UpdateSamples();

        var nextIdx = (anchorIndex + 1) % Anchors.Count;
        var a0 = Anchors[anchorIndex];
        var a1 = Anchors[nextIdx];
        var samples = GetSegmentSamples(anchorIndex);

        // Find best T on the piecewise linear approximation
        var bestT = 0f;
        var bestDistSqr = float.MaxValue;
        var totalSegments = MaxSegmentSamples + 1;

        var prev = a0.Position;
        for (var s = 0; s <= MaxSegmentSamples; s++)
        {
            var next = s < MaxSegmentSamples ? samples[s] : a1.Position;
            var distSqr = PointToSegmentDistSqr(targetPoint, prev, next, out _);
            if (distSqr < bestDistSqr)
            {
                bestDistSqr = distSqr;
                bestT = (s + 0.5f) / totalSegments;
            }
            prev = next;
        }

        // Compute split point on the quadratic bezier
        Vector2 splitPoint;
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
        }

        // Insert the new anchor and zero out curves on split segments
        var insertIdx = anchorIndex + 1;
        if (insertIdx > Anchors.Count) insertIdx = Anchors.Count;
        Anchors.Insert(insertIdx, new SpritePathAnchor { Position = splitPoint });

        // Zero out the original segment curve (crude but functional)
        var aa = Anchors[anchorIndex];
        aa.Curve = 0;
        Anchors[anchorIndex] = aa;

        MarkDirty();
    }

    #endregion

    // Split this path into two paths at the given anchor indices.
    // Returns the new second path. This path becomes the first path.
    // anchor1 must come before anchor2 in the anchor list.
    public SpritePath? SplitAtAnchors(int anchor1, int anchor2, ReadOnlySpan<Vector2> intermediatePoints, bool reverseIntermediates)
    {
        if (anchor1 < 0 || anchor2 < 0 || anchor1 >= Anchors.Count || anchor2 >= Anchors.Count)
            return null;
        if (anchor1 == anchor2)
            return null;

        // Path 1: anchor1 → anchor2, plus reversed intermediates
        // Path 2: anchor2 → wrap → anchor1, plus intermediates
        var path1Anchors = new List<SpritePathAnchor>();
        var path2Anchors = new List<SpritePathAnchor>();

        // Collect path1: anchor1 to anchor2 (forward)
        for (var i = anchor1; ; i = (i + 1) % Anchors.Count)
        {
            var a = Anchors[i];
            a.Flags = SpritePathAnchorFlags.None;
            path1Anchors.Add(a);
            if (i == anchor2) break;
        }

        // Add reversed intermediates to path1 (knife line, curve=0)
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

        // Collect path2: anchor2 to anchor1 (forward, wrapping)
        for (var i = anchor2; ; i = (i + 1) % Anchors.Count)
        {
            var a = Anchors[i];
            a.Flags = SpritePathAnchorFlags.None;
            path2Anchors.Add(a);
            if (i == anchor1) break;
        }

        // Add intermediates to path2
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

        // Update this path to be path1
        Anchors.Clear();
        Anchors.AddRange(path1Anchors);
        MarkDirty();

        // Create path2
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
            Open = Open,
            PathTranslation = PathTranslation,
            PathRotation = PathRotation,
            PathScale = PathScale,
        };
        ClonePropertiesTo(clone);
        clone.Anchors.AddRange(Anchors);
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
