//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#define NOZ_KNIFE_DEBUG

using System.Diagnostics;
using System.Numerics;

namespace NoZ.Editor;

public class KnifeTool : Tool
{
    private const float HitTolerance = 0.25f;
    private const int MaxPoints = 64;
    private const int MaxCandidates = 8;

    private enum KnifePointType
    {
        Free,
        Anchor,
        Segment,
    }

    private struct KnifeCandidate
    {
        public ushort PathIndex;
        public ushort AnchorIndex;
        public ushort SegmentIndex;
        public Vector2 Position;
    }

    private struct KnifePoint
    {
        public Vector2 Position;
        public KnifePointType Type;
        public KnifeCandidate[] Candidates;
        public int CandidateCount;
        public int ResolvedIndex;
    }

    private struct SegmentIntersection
    {
        public ushort PathIndex;
        public ushort SegmentIndex;
        public Vector2 Position;
        public float T;
        public int KnifeSegmentIndex; // which knife segment (point[i] to point[i+1]) caused this
        public float KnifeT; // position along the knife segment (0-1)
    }

    private struct SegmentLocation
    {
        public ushort PathIndex;
        public ushort SegmentIndex;
        public float T;
        public Vector2 Position;
    }

    private readonly SpriteEditor _editor;
    private readonly Shape _shape;

    private readonly KnifePoint[] _points = new KnifePoint[MaxPoints];
    private int _pointCount;

    private ushort _resolvedPathIndex = ushort.MaxValue;

    private readonly List<SegmentIntersection> _intersections = new();

    private KnifePointType _hoverType;
    private readonly KnifeCandidate[] _hoverCandidates = new KnifeCandidate[MaxCandidates];
    private int _hoverCandidateCount;
    private Vector2 _hoverPosition;

    public KnifeTool(SpriteEditor editor, Shape shape)
    {
        _editor = editor;
        _shape = shape;
    }

    public override void Begin()
    {
        Cursor.SetCrosshair();
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            Cancel();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter))
        {
            if (_pointCount >= 2)
                Commit();
            else
                Cancel();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseRight))
        {
            if (_pointCount > 0)
                RemoveLastPoint();
            else
                Cancel();
            return;
        }

        UpdateHover();

        if (Input.WasButtonPressed(InputCode.MouseLeft))
            AddPoint();
    }

    private void UpdateHover()
    {
        var hitRadius = HitTolerance / Workspace.Zoom;
        var hitRadiusSqr = hitRadius * hitRadius;

        Matrix3x2.Invert(_editor.Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        _hoverType = KnifePointType.Free;
        _hoverCandidateCount = 0;
        _hoverPosition = mouseLocal;

        // Find ALL anchors within hit radius
        for (ushort p = 0; p < _shape.PathCount; p++)
        {
            ref readonly var path = ref _shape.GetPath(p);
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                ref readonly var anchor = ref _shape.GetAnchor(anchorIdx);
                var distSqr = Vector2.DistanceSquared(mouseLocal, anchor.Position);

                if (distSqr < hitRadiusSqr && _hoverCandidateCount < MaxCandidates)
                {
                    _hoverCandidates[_hoverCandidateCount++] = new KnifeCandidate
                    {
                        PathIndex = p,
                        AnchorIndex = anchorIdx,
                        SegmentIndex = ushort.MaxValue,
                        Position = anchor.Position
                    };
                }
            }
        }

        if (_hoverCandidateCount > 0)
        {
            _hoverType = KnifePointType.Anchor;
            _hoverPosition = _hoverCandidates[0].Position;
            return;
        }

        // Find ALL segments within hit radius
        for (ushort p = 0; p < _shape.PathCount; p++)
        {
            ref readonly var path = ref _shape.GetPath(p);
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var segmentIdx = (ushort)(path.AnchorStart + a);
                var closestPoint = GetClosestPointOnSegment(segmentIdx, mouseLocal);
                var distSqr = Vector2.DistanceSquared(mouseLocal, closestPoint);

                if (distSqr < hitRadiusSqr && _hoverCandidateCount < MaxCandidates)
                {
                    _hoverCandidates[_hoverCandidateCount++] = new KnifeCandidate
                    {
                        PathIndex = p,
                        AnchorIndex = ushort.MaxValue,
                        SegmentIndex = segmentIdx,
                        Position = closestPoint
                    };
                }
            }
        }

        if (_hoverCandidateCount > 0)
        {
            _hoverType = KnifePointType.Segment;
            _hoverPosition = _hoverCandidates[0].Position;
        }
    }

    private Vector2 GetClosestPointOnSegment(ushort anchorIndex, Vector2 point)
    {
        ref readonly var a0 = ref _shape.GetAnchor(anchorIndex);
        ref readonly var a1 = ref _shape.GetNextAnchor(anchorIndex);

        var samples = _shape.GetSegmentSamples(anchorIndex);
        var bestDist = float.MaxValue;
        var bestPoint = (a0.Position + a1.Position) * 0.5f;

        var prev = a0.Position;
        for (var i = 0; i < Shape.MaxSegmentSamples; i++)
        {
            var closestOnSeg = ClosestPointOnLine(prev, samples[i], point);
            var dist = Vector2.DistanceSquared(closestOnSeg, point);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPoint = closestOnSeg;
            }
            prev = samples[i];
        }

        var closestLast = ClosestPointOnLine(prev, a1.Position, point);
        var distLast = Vector2.DistanceSquared(closestLast, point);
        if (distLast < bestDist)
            bestPoint = closestLast;

        return bestPoint;
    }

    private (Vector2 point, float t) GetClosestPointOnSegmentWithT(ushort anchorIndex, Vector2 point)
    {
        ref readonly var a0 = ref _shape.GetAnchor(anchorIndex);
        ref readonly var a1 = ref _shape.GetNextAnchor(anchorIndex);

        var samples = _shape.GetSegmentSamples(anchorIndex);
        var bestDist = float.MaxValue;
        var bestPoint = (a0.Position + a1.Position) * 0.5f;
        var bestT = 0.5f;

        var tStep = 1f / (Shape.MaxSegmentSamples + 1);
        var tBase = 0f;

        var prev = a0.Position;
        for (var i = 0; i < Shape.MaxSegmentSamples; i++)
        {
            var (closestOnSeg, localT) = ClosestPointOnLineWithT(prev, samples[i], point);
            var dist = Vector2.DistanceSquared(closestOnSeg, point);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPoint = closestOnSeg;
                bestT = tBase + localT * tStep;
            }
            prev = samples[i];
            tBase += tStep;
        }

        var (closestLast, localTLast) = ClosestPointOnLineWithT(prev, a1.Position, point);
        var distLast = Vector2.DistanceSquared(closestLast, point);
        if (distLast < bestDist)
        {
            bestPoint = closestLast;
            bestT = tBase + localTLast * tStep;
        }

        return (bestPoint, bestT);
    }

    private static (Vector2 point, float t) ClosestPointOnLineWithT(Vector2 a, Vector2 b, Vector2 p)
    {
        var ab = b - a;
        var lenSq = Vector2.Dot(ab, ab);
        if (lenSq < 0.0001f)
            return (a, 0f);

        var t = MathF.Max(0, MathF.Min(1, Vector2.Dot(p - a, ab) / lenSq));
        return (a + ab * t, t);
    }

    private static Vector2 ClosestPointOnLine(Vector2 a, Vector2 b, Vector2 p)
    {
        var ab = b - a;
        var lenSq = Vector2.Dot(ab, ab);
        if (lenSq < 0.0001f)
            return a;

        var t = MathF.Max(0, MathF.Min(1, Vector2.Dot(p - a, ab) / lenSq));
        return a + ab * t;
    }

    private SegmentLocation FindSegmentContainingPoint(Vector2 point, float tolerance = 0.01f)
    {
        var toleranceSq = tolerance * tolerance;

        for (ushort p = 0; p < _shape.PathCount; p++)
        {
            ref readonly var path = ref _shape.GetPath(p);
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var segmentIdx = (ushort)(path.AnchorStart + a);
                var (closest, t) = GetClosestPointOnSegmentWithT(segmentIdx, point);

                if (Vector2.DistanceSquared(closest, point) < toleranceSq)
                {
                    return new SegmentLocation
                    {
                        PathIndex = p,
                        SegmentIndex = segmentIdx,
                        T = t,
                        Position = point
                    };
                }
            }
        }

        return new SegmentLocation { PathIndex = ushort.MaxValue };
    }

    private void AddPoint()
    {
        LogKnife($"AddPoint: HoverType={_hoverType} HoverCandidates={_hoverCandidateCount} ResolvedPath={_resolvedPathIndex}");

        if (_pointCount >= MaxPoints)
            return;

        if (_pointCount > 0)
        {
            var lastPos = _points[_pointCount - 1].Position;
            if (Vector2.Distance(_hoverPosition, lastPos) < 0.001f)
                return;
        }

        for (var i = 0; i < _hoverCandidateCount; i++)
        {
            ref var c = ref _hoverCandidates[i];
            LogKnife($"  Candidate[{i}]: Path={c.PathIndex} Anchor={c.AnchorIndex} Segment={c.SegmentIndex} Pos={c.Position}");
        }

        var candidates = new KnifeCandidate[MaxCandidates];
        for (var i = 0; i < _hoverCandidateCount; i++)
            candidates[i] = _hoverCandidates[i];

        var resolvedIndex = -1;

        // Try to resolve immediately
        if (_hoverCandidateCount == 1)
        {
            resolvedIndex = 0;
            if (_resolvedPathIndex == ushort.MaxValue)
            {
                _resolvedPathIndex = _hoverCandidates[0].PathIndex;
                LogKnife($"  Single candidate, setting ResolvedPath={_resolvedPathIndex}");
            }
        }
        else if (_hoverCandidateCount > 1 && _resolvedPathIndex != ushort.MaxValue)
        {
            LogKnife($"  Multiple candidates, looking for path {_resolvedPathIndex}");
            // Try to match resolved path
            for (var i = 0; i < _hoverCandidateCount; i++)
            {
                if (_hoverCandidates[i].PathIndex == _resolvedPathIndex)
                {
                    resolvedIndex = i;
                    LogKnife($"  Found matching candidate at index {i}");
                    break;
                }
            }
            if (resolvedIndex < 0)
                LogKnife($"  No matching candidate found!");
        }

        _points[_pointCount++] = new KnifePoint
        {
            Position = _hoverPosition,
            Type = _hoverType,
            Candidates = candidates,
            CandidateCount = _hoverCandidateCount,
            ResolvedIndex = resolvedIndex,
        };

        LogKnife($"  Added point {_pointCount - 1}: Type={_hoverType} ResolvedIndex={resolvedIndex}");

        // If we just resolved something, try to resolve earlier ambiguous points
        if (resolvedIndex >= 0 && _resolvedPathIndex != ushort.MaxValue)
            TryResolveAmbiguousPoints();

        RecalculateIntersections();
    }

    private void TryResolveAmbiguousPoints()
    {
        for (var i = 0; i < _pointCount; i++)
        {
            if (_points[i].ResolvedIndex >= 0)
                continue;

            for (var c = 0; c < _points[i].CandidateCount; c++)
            {
                if (_points[i].Candidates[c].PathIndex == _resolvedPathIndex)
                {
                    _points[i].ResolvedIndex = c;
                    break;
                }
            }
        }
    }

    private void RemoveLastPoint()
    {
        if (_pointCount > 0)
        {
            _pointCount--;

            // Recalculate resolved path if needed
            _resolvedPathIndex = ushort.MaxValue;
            for (var i = 0; i < _pointCount; i++)
            {
                if (_points[i].ResolvedIndex >= 0)
                {
                    _resolvedPathIndex = _points[i].Candidates[_points[i].ResolvedIndex].PathIndex;
                    break;
                }
            }

            RecalculateIntersections();
        }
    }

    private void RecalculateIntersections()
    {
        _intersections.Clear();

        for (var i = 0; i < _pointCount - 1; i++)
        {
            var p0 = _points[i].Position;
            var p1 = _points[i + 1].Position;
            FindIntersectionsForSegment(p0, p1, i);
        }

        // Intersections can help resolve ambiguity
        if (_resolvedPathIndex == ushort.MaxValue && _intersections.Count > 0)
        {
            _resolvedPathIndex = _intersections[0].PathIndex;
            TryResolveAmbiguousPoints();
        }
    }

    private void FindIntersectionsForSegment(Vector2 knifeStart, Vector2 knifeEnd, int knifeSegmentIndex)
    {
        for (ushort p = 0; p < _shape.PathCount; p++)
        {
            // Only consider resolved path if we have one
            if (_resolvedPathIndex != ushort.MaxValue && p != _resolvedPathIndex)
                continue;

            ref readonly var path = ref _shape.GetPath(p);
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var segmentIndex = (ushort)(path.AnchorStart + a);
                FindSegmentIntersections(knifeStart, knifeEnd, p, segmentIndex, knifeSegmentIndex);
            }
        }
    }

    private void FindSegmentIntersections(Vector2 knifeStart, Vector2 knifeEnd, ushort pathIndex, ushort segmentIndex, int knifeSegmentIndex)
    {
        var samples = _shape.GetSegmentSamples(segmentIndex);
        ref readonly var a0 = ref _shape.GetAnchor(segmentIndex);
        ref readonly var a1 = ref _shape.GetNextAnchor(segmentIndex);

        var prev = a0.Position;
        var tBase = 0f;
        var tStep = 1f / (Shape.MaxSegmentSamples + 1);

        for (var i = 0; i < Shape.MaxSegmentSamples; i++)
        {
            if (LineSegmentIntersect(knifeStart, knifeEnd, prev, samples[i], out var hit, out var localT, out var knifeT))
            {
                var globalT = tBase + localT * tStep;
                if (!IsDuplicateIntersection(segmentIndex, globalT))
                {
                    _intersections.Add(new SegmentIntersection
                    {
                        PathIndex = pathIndex,
                        SegmentIndex = segmentIndex,
                        Position = hit,
                        T = globalT,
                        KnifeSegmentIndex = knifeSegmentIndex,
                        KnifeT = knifeT
                    });
                }
            }
            prev = samples[i];
            tBase += tStep;
        }

        if (LineSegmentIntersect(knifeStart, knifeEnd, prev, a1.Position, out var hitLast, out var localTLast, out var knifeTLast))
        {
            var globalT = tBase + localTLast * tStep;
            if (!IsDuplicateIntersection(segmentIndex, globalT))
            {
                _intersections.Add(new SegmentIntersection
                {
                    PathIndex = pathIndex,
                    SegmentIndex = segmentIndex,
                    Position = hitLast,
                    T = globalT,
                    KnifeSegmentIndex = knifeSegmentIndex,
                    KnifeT = knifeTLast
                });
            }
        }
    }

    private bool IsDuplicateIntersection(ushort segmentIndex, float t)
    {
        foreach (var existing in _intersections)
        {
            if (existing.SegmentIndex == segmentIndex && MathF.Abs(existing.T - t) < 0.01f)
                return true;
        }
        return false;
    }

    private static bool LineSegmentIntersect(
        Vector2 p1, Vector2 p2,
        Vector2 p3, Vector2 p4,
        out Vector2 intersection,
        out float t,
        out float knifeT)
    {
        var d1 = p2 - p1;
        var d2 = p4 - p3;
        var cross = d1.X * d2.Y - d1.Y * d2.X;

        if (MathF.Abs(cross) < 0.0001f)
        {
            intersection = default;
            t = 0;
            knifeT = 0;
            return false;
        }

        var d3 = p1 - p3;
        knifeT = (d2.X * d3.Y - d2.Y * d3.X) / cross;
        t = (d1.X * d3.Y - d1.Y * d3.X) / cross;

        if (knifeT >= 0 && knifeT <= 1 && t >= 0 && t <= 1)
        {
            intersection = p1 + d1 * knifeT;
            return true;
        }

        intersection = default;
        return false;
    }

    private void Commit()
    {
        LogKnife($"Commit: PointCount={_pointCount} ResolvedPath={_resolvedPathIndex}");

        if (_pointCount < 2)
        {
            LogKnife("  Not enough points, canceling");
            Finish();
            return;
        }

        // Log all points before disambiguation
        for (var i = 0; i < _pointCount; i++)
        {
            ref var p = ref _points[i];
            LogKnife($"  Point[{i}]: Type={p.Type} CandidateCount={p.CandidateCount} ResolvedIndex={p.ResolvedIndex}");
            if (p.ResolvedIndex >= 0 && p.CandidateCount > 0)
            {
                ref var c = ref p.Candidates[p.ResolvedIndex];
                LogKnife($"    Resolved: Path={c.PathIndex} Anchor={c.AnchorIndex}");
            }
        }

        // Final disambiguation: resolve any remaining ambiguous points to first candidate
        for (var i = 0; i < _pointCount; i++)
        {
            if (_points[i].ResolvedIndex < 0 && _points[i].CandidateCount > 0)
            {
                _points[i].ResolvedIndex = 0;
                LogKnife($"  Force-resolved point {i} to candidate 0");
            }
        }

        // Get first and last knife points
        ref var firstPoint = ref _points[0];
        ref var lastPoint = ref _points[_pointCount - 1];

        LogKnife($"  First: Type={firstPoint.Type} ResolvedIdx={firstPoint.ResolvedIndex}");
        LogKnife($"  Last: Type={lastPoint.Type} ResolvedIdx={lastPoint.ResolvedIndex}");

        // Check if we have an anchor-to-anchor cut on the same path
        if (firstPoint.Type == KnifePointType.Anchor &&
            lastPoint.Type == KnifePointType.Anchor &&
            firstPoint.ResolvedIndex >= 0 &&
            lastPoint.ResolvedIndex >= 0)
        {
            var firstPathIndex = firstPoint.Candidates[firstPoint.ResolvedIndex].PathIndex;
            var lastPathIndex = lastPoint.Candidates[lastPoint.ResolvedIndex].PathIndex;

            LogKnife($"  Anchor-to-Anchor: FirstPath={firstPathIndex} LastPath={lastPathIndex}");

            if (firstPathIndex == lastPathIndex)
            {
                CommitPathSplit(firstPathIndex);
                return;
            }
            else
            {
                LogKnife($"  Paths don't match! Cannot split.");
            }
        }

        // For all other cases (segment clicks, free clicks with intersections),
        // use the iterative cut algorithm that processes intersection pairs
        if (_intersections.Count >= 2)
        {
            LogKnife($"  Using iterative cuts for {_intersections.Count} intersections");
            CommitIterativeCuts();
            return;
        }

        // Handle segment-to-segment or segment-to-anchor when we have direct segment snaps
        // Add these as synthetic intersections and process iteratively
        if ((firstPoint.Type == KnifePointType.Segment || lastPoint.Type == KnifePointType.Segment) &&
            firstPoint.ResolvedIndex >= 0 && lastPoint.ResolvedIndex >= 0)
        {
            // Create synthetic intersections from segment snap points
            _intersections.Clear();

            if (firstPoint.Type == KnifePointType.Segment)
            {
                _intersections.Add(new SegmentIntersection
                {
                    PathIndex = firstPoint.Candidates[firstPoint.ResolvedIndex].PathIndex,
                    SegmentIndex = firstPoint.Candidates[firstPoint.ResolvedIndex].SegmentIndex,
                    Position = firstPoint.Position,
                    T = 0.5f,
                    KnifeSegmentIndex = 0,
                    KnifeT = 0f
                });
            }
            else if (firstPoint.Type == KnifePointType.Anchor)
            {
                _intersections.Add(new SegmentIntersection
                {
                    PathIndex = firstPoint.Candidates[firstPoint.ResolvedIndex].PathIndex,
                    SegmentIndex = firstPoint.Candidates[firstPoint.ResolvedIndex].AnchorIndex,
                    Position = firstPoint.Position,
                    T = 0f,
                    KnifeSegmentIndex = 0,
                    KnifeT = 0f
                });
            }

            if (lastPoint.Type == KnifePointType.Segment)
            {
                _intersections.Add(new SegmentIntersection
                {
                    PathIndex = lastPoint.Candidates[lastPoint.ResolvedIndex].PathIndex,
                    SegmentIndex = lastPoint.Candidates[lastPoint.ResolvedIndex].SegmentIndex,
                    Position = lastPoint.Position,
                    T = 0.5f,
                    KnifeSegmentIndex = _pointCount - 2,
                    KnifeT = 1f
                });
            }
            else if (lastPoint.Type == KnifePointType.Anchor)
            {
                _intersections.Add(new SegmentIntersection
                {
                    PathIndex = lastPoint.Candidates[lastPoint.ResolvedIndex].PathIndex,
                    SegmentIndex = lastPoint.Candidates[lastPoint.ResolvedIndex].AnchorIndex,
                    Position = lastPoint.Position,
                    T = 0f,
                    KnifeSegmentIndex = _pointCount - 2,
                    KnifeT = 1f
                });
            }

            if (_intersections.Count >= 2)
            {
                LogKnife($"  Using iterative cuts for {_intersections.Count} synthetic intersections");
                CommitIterativeCuts();
                return;
            }
        }

        // No valid cut pattern found - cancel
        LogKnife("  No valid cut pattern found, canceling");
        Finish();
    }

    private void CommitPathSplit(ushort pathIndex)
    {
        LogKnife($"CommitPathSplit: PathIndex={pathIndex}");

        ref var firstPoint = ref _points[0];
        ref var lastPoint = ref _points[_pointCount - 1];

        var firstAnchorIdx = firstPoint.Candidates[firstPoint.ResolvedIndex].AnchorIndex;
        var lastAnchorIdx = lastPoint.Candidates[lastPoint.ResolvedIndex].AnchorIndex;

        LogKnife($"  FirstAnchor={firstAnchorIdx} LastAnchor={lastAnchorIdx}");

        if (firstAnchorIdx == lastAnchorIdx)
        {
            LogKnife("  Same anchor, canceling");
            Finish();
            return;
        }

        Undo.Record(_editor.Document);

        ref readonly var path = ref _shape.GetPath(pathIndex);
        var pathStart = path.AnchorStart;
        var pathAnchorCount = path.AnchorCount;
        var fillColor = path.FillColor;

        LogKnife($"  Path: Start={pathStart} Count={pathAnchorCount} FillColor={fillColor}");

        // Convert to local indices within the path
        var firstLocal = (ushort)(firstAnchorIdx - pathStart);
        var lastLocal = (ushort)(lastAnchorIdx - pathStart);

        LogKnife($"  Local indices: First={firstLocal} Last={lastLocal}");

        // Ensure firstLocal < lastLocal for consistent ordering
        if (firstLocal > lastLocal)
        {
            (firstLocal, lastLocal) = (lastLocal, firstLocal);
            LogKnife($"  Swapped to: First={firstLocal} Last={lastLocal}");
        }

        // Collect anchor data for both new paths
        var anchorsPath1 = new List<(Vector2 pos, float curve)>();
        var anchorsPath2 = new List<(Vector2 pos, float curve)>();

        // Path 1: from firstLocal to lastLocal (inclusive)
        for (var i = firstLocal; i <= lastLocal; i++)
        {
            var anchorIdx = (ushort)(pathStart + i);
            ref readonly var anchor = ref _shape.GetAnchor(anchorIdx);
            var curve = (i == lastLocal) ? 0f : anchor.Curve;
            anchorsPath1.Add((anchor.Position, curve));
        }

        // Path 2: from lastLocal to firstLocal (wrapping around, inclusive)
        for (var i = lastLocal; i != firstLocal; i = (ushort)((i + 1) % pathAnchorCount))
        {
            var anchorIdx = (ushort)(pathStart + i);
            ref readonly var anchor = ref _shape.GetAnchor(anchorIdx);
            anchorsPath2.Add((anchor.Position, anchor.Curve));
        }
        // Add the first anchor to close path 2
        {
            var anchorIdx = (ushort)(pathStart + firstLocal);
            ref readonly var anchor = ref _shape.GetAnchor(anchorIdx);
            anchorsPath2.Add((anchor.Position, 0f));
        }

        LogKnife($"  Path1 anchors: {anchorsPath1.Count}, Path2 anchors: {anchorsPath2.Count}");

        // Only proceed if both paths have at least 3 anchors
        if (anchorsPath1.Count < 3 || anchorsPath2.Count < 3)
        {
            LogKnife("  Not enough anchors for split, canceling");
            Finish();
            return;
        }

        // Delete the original path by selecting all its anchors and deleting
        for (var i = 0; i < pathAnchorCount; i++)
        {
            var anchorIdx = (ushort)(pathStart + i);
            _shape.SetAnchorSelected(anchorIdx, true);
        }
        _shape.DeleteAnchors();

        LogKnife("  Deleted original path");

        // Create the two new paths
        var newPath1 = _shape.AddPath(fillColor);
        if (newPath1 != ushort.MaxValue)
        {
            foreach (var (pos, curve) in anchorsPath1)
                _shape.AddAnchor(newPath1, pos, curve);
            LogKnife($"  Created Path1 at index {newPath1}");
        }

        var newPath2 = _shape.AddPath(fillColor);
        if (newPath2 != ushort.MaxValue)
        {
            foreach (var (pos, curve) in anchorsPath2)
                _shape.AddAnchor(newPath2, pos, curve);
            LogKnife($"  Created Path2 at index {newPath2}");
        }

        _shape.ClearSelection();
        _shape.UpdateSamples();
        _shape.UpdateBounds();

        _editor.Document.MarkModified();
        _editor.Document.UpdateBounds();
        _editor.MarkRasterDirty();

        LogKnife("  Split complete");
        Finish();
    }

    private void CommitIntersectionsOnly()
    {
        LogKnife($"CommitIntersectionsOnly: IntersectionCount={_intersections.Count}");

        if (_intersections.Count == 0)
        {
            LogKnife("  No intersections, canceling");
            Finish();
            return;
        }

        // Group intersections by path
        var intersectionsByPath = _intersections
            .GroupBy(i => i.PathIndex)
            .ToDictionary(g => g.Key, g => g.ToList());

        LogKnife($"  Paths with intersections: {intersectionsByPath.Count}");

        // If we have exactly 2 intersections on a single path, we can do a through-cut split
        if (intersectionsByPath.Count == 1)
        {
            var pathIndex = intersectionsByPath.Keys.First();
            var pathIntersections = intersectionsByPath[pathIndex];

            LogKnife($"  Single path {pathIndex} with {pathIntersections.Count} intersections");

            if (pathIntersections.Count == 2)
            {
                CommitThroughCutSplit(pathIndex, pathIntersections);
                return;
            }
        }

        // Fallback: just insert anchors at intersections and select
        LogKnife("  Fallback: inserting anchors only");

        Undo.Record(_editor.Document);

        var sortedIntersections = _intersections
            .OrderByDescending(i => i.SegmentIndex)
            .ThenByDescending(i => i.T)
            .ToList();

        var newAnchorIndices = new List<ushort>();

        foreach (var intersection in sortedIntersections)
        {
            _shape.SplitSegmentAtPoint(intersection.SegmentIndex, intersection.Position);
            var newIndex = (ushort)(intersection.SegmentIndex + 1);
            newAnchorIndices.Add(newIndex);
        }

        _shape.ClearSelection();

        for (var i = 0; i < _pointCount; i++)
        {
            if (_points[i].Type == KnifePointType.Anchor && _points[i].ResolvedIndex >= 0)
            {
                var anchorIdx = _points[i].Candidates[_points[i].ResolvedIndex].AnchorIndex;
                if (anchorIdx != ushort.MaxValue)
                    _shape.SetAnchorSelected(anchorIdx, true);
            }
        }

        foreach (var idx in newAnchorIndices)
        {
            if (idx < _shape.AnchorCount)
                _shape.SetAnchorSelected(idx, true);
        }

        _shape.UpdateSamples();
        _shape.UpdateBounds();

        _editor.Document.MarkModified();
        _editor.Document.UpdateBounds();
        _editor.MarkRasterDirty();

        Finish();
    }

    private void CommitThroughCutSplit(ushort pathIndex, List<SegmentIntersection> intersections, bool includeKnifeMiddlePoints = true)
    {
        LogKnife($"CommitThroughCutSplit: PathIndex={pathIndex} IncludeKnifeMiddle={includeKnifeMiddlePoints}");
        LogShape("BEFORE SPLIT");

        ref readonly var path = ref _shape.GetPath(pathIndex);
        var pathStart = path.AnchorStart;
        var pathAnchorCount = path.AnchorCount;
        var fillColor = path.FillColor;

        // Sort intersections by their position along the path (shape position)
        var sorted = intersections
            .OrderBy(i => i.SegmentIndex)
            .ThenBy(i => i.T)
            .ToList();

        var int1 = sorted[0];
        var int2 = sorted[1];

        LogKnife($"  Intersection1: Segment={int1.SegmentIndex} T={int1.T} KnifeSeg={int1.KnifeSegmentIndex} KnifeT={int1.KnifeT:F3} Pos={int1.Position}");
        LogKnife($"  Intersection2: Segment={int2.SegmentIndex} T={int2.T} KnifeSeg={int2.KnifeSegmentIndex} KnifeT={int2.KnifeT:F3} Pos={int2.Position}");

        // Convert to local segment indices
        var seg1Local = (ushort)(int1.SegmentIndex - pathStart);
        var seg2Local = (ushort)(int2.SegmentIndex - pathStart);

        // Special case: both intersections on the same segment
        // This creates a "notch" or "slit" - no enclosed area to cut out
        // Just insert both intersection points as new anchors
        if (seg1Local == seg2Local)
        {
            LogKnife("  Same segment - inserting anchors only (no split)");
            CommitSameSegmentInsert(int1, int2);
            return;
        }

        LogKnife($"  Local segments: {seg1Local}, {seg2Local}");
        LogKnife($"  Path has {pathAnchorCount} anchors");

        // Log original anchors
        for (var i = 0; i < pathAnchorCount; i++)
        {
            var anchorIdx = (ushort)(pathStart + i);
            ref readonly var anchor = ref _shape.GetAnchor(anchorIdx);
            LogKnife($"  Original anchor {i}: {anchor.Position}");
        }

        // Collect knife middle points that are BETWEEN the two intersections along the knife path
        // Only do this for Segment/Anchor based cuts, not for intersection-based (Free-to-Free) cuts
        var knifeMiddlePoints = new List<Vector2>();
        var knifeForward = int1.KnifeSegmentIndex < int2.KnifeSegmentIndex ||
                           (int1.KnifeSegmentIndex == int2.KnifeSegmentIndex && int1.KnifeT < int2.KnifeT);

        // Only collect knife middle points for Segment/Anchor-based cuts, not for intersection-based cuts
        if (includeKnifeMiddlePoints)
        {
            int knifeStart, knifeEnd;

            if (knifeForward)
            {
                knifeStart = int1.KnifeSegmentIndex + 1;
                knifeEnd = int2.KnifeSegmentIndex;
            }
            else
            {
                knifeStart = int2.KnifeSegmentIndex + 1;
                knifeEnd = int1.KnifeSegmentIndex;
            }

            LogKnife($"  Knife direction: {(knifeForward ? "forward" : "reversed")}");
            LogKnife($"  Knife points between intersections: indices {knifeStart} to {knifeEnd}");

            for (var i = knifeStart; i <= knifeEnd; i++)
            {
                knifeMiddlePoints.Add(_points[i].Position);
                LogKnife($"  Knife middle point {i}: {_points[i].Position}");
            }
        }
        else
        {
            LogKnife("  Skipping knife middle points (intersection-based cut)");
        }

        Undo.Record(_editor.Document);

        // Collect anchor data for both new paths
        // Path 1 (cutout): from int1 along path to int2, then knife middle points back to int1
        // Path 2 (main): from int2 wrapping around to int1, then knife points forward to int2
        var anchorsPath1 = new List<(Vector2 pos, float curve)>();
        var anchorsPath2 = new List<(Vector2 pos, float curve)>();

        // Path 1: Start with intersection point 1
        anchorsPath1.Add((int1.Position, 0f));
        LogKnife($"  Path1: Added int1 at {int1.Position}");

        // Add anchors from after segment1 to segment2 (going forward)
        // Calculate how many anchors to add (handles same-segment case where count is 0)
        var path1AnchorCount = (seg2Local - seg1Local + pathAnchorCount) % pathAnchorCount;
        LogKnife($"  Path1: Will add {path1AnchorCount} original anchors");
        for (var c = 0; c < path1AnchorCount; c++)
        {
            var i = (seg1Local + 1 + c) % pathAnchorCount;
            var anchorIdx = (ushort)(pathStart + i);
            ref readonly var anchor = ref _shape.GetAnchor(anchorIdx);
            anchorsPath1.Add((anchor.Position, anchor.Curve));
            LogKnife($"  Path1: Added anchor {i} at {anchor.Position}");
        }

        // Add intersection point 2
        anchorsPath1.Add((int2.Position, 0f));
        LogKnife($"  Path1: Added int2 at {int2.Position}");

        // Add knife middle points to close Path 1 (from int2 back to int1)
        // If knifeForward: knife goes int1→points→int2, so reverse for int2→int1
        // If !knifeForward: knife goes int2→points→int1, so forward for int2→int1
        if (knifeForward)
        {
            for (var i = knifeMiddlePoints.Count - 1; i >= 0; i--)
            {
                anchorsPath1.Add((knifeMiddlePoints[i], 0f));
                LogKnife($"  Path1: Added knife point at {knifeMiddlePoints[i]}");
            }
        }
        else
        {
            for (var i = 0; i < knifeMiddlePoints.Count; i++)
            {
                anchorsPath1.Add((knifeMiddlePoints[i], 0f));
                LogKnife($"  Path1: Added knife point at {knifeMiddlePoints[i]}");
            }
        }

        LogKnife($"  Path1 has {anchorsPath1.Count} anchors (including {knifeMiddlePoints.Count} knife points)");

        // Path 2: Start with intersection point 2
        anchorsPath2.Add((int2.Position, 0f));
        LogKnife($"  Path2: Added int2 at {int2.Position}");

        // Add anchors from after segment2 to segment1 (going forward, wrapping around)
        // When both intersections are on the same segment, this wraps around ALL anchors
        var path2AnchorCount = (seg1Local - seg2Local + pathAnchorCount) % pathAnchorCount;
        if (path2AnchorCount == 0 && seg1Local == seg2Local)
            path2AnchorCount = pathAnchorCount; // Same segment: wrap around all anchors
        LogKnife($"  Path2: Will add {path2AnchorCount} original anchors");
        for (var c = 0; c < path2AnchorCount; c++)
        {
            var i = (seg2Local + 1 + c) % pathAnchorCount;
            var anchorIdx = (ushort)(pathStart + i);
            ref readonly var anchor = ref _shape.GetAnchor(anchorIdx);
            anchorsPath2.Add((anchor.Position, anchor.Curve));
            LogKnife($"  Path2: Added anchor {i} at {anchor.Position}");
        }

        // Add intersection point 1
        anchorsPath2.Add((int1.Position, 0f));
        LogKnife($"  Path2: Added int1 at {int1.Position}");

        // Add knife middle points to close Path 2 (from int1 to int2)
        // If knifeForward: knife goes int1→points→int2, so forward for int1→int2
        // If !knifeForward: knife goes int2→points→int1, so reverse for int1→int2
        if (knifeForward)
        {
            for (var i = 0; i < knifeMiddlePoints.Count; i++)
            {
                anchorsPath2.Add((knifeMiddlePoints[i], 0f));
                LogKnife($"  Path2: Added knife point at {knifeMiddlePoints[i]}");
            }
        }
        else
        {
            for (var i = knifeMiddlePoints.Count - 1; i >= 0; i--)
            {
                anchorsPath2.Add((knifeMiddlePoints[i], 0f));
                LogKnife($"  Path2: Added knife point at {knifeMiddlePoints[i]}");
            }
        }

        LogKnife($"  Path2 has {anchorsPath2.Count} anchors (including {knifeMiddlePoints.Count} knife points)");

        // Only proceed if both paths have at least 3 anchors
        if (anchorsPath1.Count < 3 || anchorsPath2.Count < 3)
        {
            LogKnife("  Not enough anchors for split, canceling");
            Finish();
            return;
        }

        // Delete the original path
        for (var i = 0; i < pathAnchorCount; i++)
        {
            var anchorIdx = (ushort)(pathStart + i);
            _shape.SetAnchorSelected(anchorIdx, true);
        }
        _shape.DeleteAnchors();

        LogKnife("  Deleted original path");

        // Create the two new paths
        var newPath1 = _shape.AddPath(fillColor);
        if (newPath1 != ushort.MaxValue)
        {
            foreach (var (pos, curve) in anchorsPath1)
                _shape.AddAnchor(newPath1, pos, curve);
            LogKnife($"  Created Path1 at index {newPath1}");
        }

        var newPath2 = _shape.AddPath(fillColor);
        if (newPath2 != ushort.MaxValue)
        {
            foreach (var (pos, curve) in anchorsPath2)
                _shape.AddAnchor(newPath2, pos, curve);
            LogKnife($"  Created Path2 at index {newPath2}");
        }

        _shape.ClearSelection();
        _shape.UpdateSamples();
        _shape.UpdateBounds();

        LogShape("AFTER SPLIT");

        _editor.Document.MarkModified();
        _editor.Document.UpdateBounds();
        _editor.MarkRasterDirty();

        LogKnife("  Through-cut split complete");
        Finish();
    }

    private void CommitSameSegmentInsert(SegmentIntersection int1, SegmentIntersection int2)
    {
        LogKnife($"CommitSameSegmentInsert: Segment={int1.SegmentIndex}");

        Undo.Record(_editor.Document);

        // Insert both points on the same segment
        // Insert in reverse T order so indices stay valid
        if (int1.T > int2.T)
            (int1, int2) = (int2, int1);

        // Insert int2 first (higher T), then int1
        _shape.SplitSegmentAtPoint(int2.SegmentIndex, int2.Position);
        _shape.SplitSegmentAtPoint(int1.SegmentIndex, int1.Position);

        // Select the two new anchors
        _shape.ClearSelection();
        var newAnchor1 = (ushort)(int1.SegmentIndex + 1);
        var newAnchor2 = (ushort)(int2.SegmentIndex + 2); // +2 because int1 was inserted before
        _shape.SetAnchorSelected(newAnchor1, true);
        _shape.SetAnchorSelected(newAnchor2, true);

        _shape.UpdateSamples();
        _shape.UpdateBounds();

        LogShape("AFTER INSERT");

        _editor.Document.MarkModified();
        _editor.Document.UpdateBounds();
        _editor.MarkRasterDirty();

        LogKnife("  Same-segment insert complete");
        Finish();
    }

    private void CommitIterativeCuts()
    {
        LogKnife($"CommitIterativeCuts: Processing {_intersections.Count} intersections");

        if (_intersections.Count < 2)
        {
            LogKnife("  Not enough intersections for any cuts");
            Finish();
            return;
        }

        // Group intersections by path, then sort each group by knife position
        var intersectionsByPath = _intersections
            .GroupBy(i => i.PathIndex)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(i => i.KnifeSegmentIndex).ThenBy(i => i.KnifeT).ToList()
            );

        LogKnife($"  Intersections grouped into {intersectionsByPath.Count} path(s)");

        // Count valid cuts (paths with 2+ intersections)
        var totalCuts = intersectionsByPath.Values.Sum(list => list.Count / 2);
        if (totalCuts == 0)
        {
            LogKnife("  No paths have enough intersections for cuts");
            Finish();
            return;
        }

        Undo.Record(_editor.Document);

        // Process each path's intersections as pairs
        var cutIndex = 0;
        foreach (var (pathIndex, pathIntersections) in intersectionsByPath)
        {
            LogKnife($"  Path {pathIndex}: {pathIntersections.Count} intersections = {pathIntersections.Count / 2} cut(s)");

            // Process pairs within this path
            for (var i = 0; i + 1 < pathIntersections.Count; i += 2)
            {
                var entry = pathIntersections[i];
                var exit = pathIntersections[i + 1];

                LogKnife($"    Cut {cutIndex}: Entry={entry.Position} (KnifeSeg={entry.KnifeSegmentIndex}), Exit={exit.Position} (KnifeSeg={exit.KnifeSegmentIndex})");

                // Find which path currently contains these points (may have changed from prior cuts)
                var entryLoc = FindSegmentContainingPoint(entry.Position);
                var exitLoc = FindSegmentContainingPoint(exit.Position);

                LogKnife($"      Entry on path {entryLoc.PathIndex} segment {entryLoc.SegmentIndex}");
                LogKnife($"      Exit on path {exitLoc.PathIndex} segment {exitLoc.SegmentIndex}");

                if (entryLoc.PathIndex == ushort.MaxValue || exitLoc.PathIndex == ushort.MaxValue)
                {
                    LogKnife("      Skipping - couldn't find path for intersection point");
                    continue;
                }

                if (entryLoc.PathIndex != exitLoc.PathIndex)
                {
                    LogKnife("      Skipping - entry and exit on different paths (shape changed)");
                    continue;
                }

                PerformSingleCut(entryLoc, exitLoc);
                cutIndex++;
            }
        }

        _shape.ClearSelection();
        _shape.UpdateSamples();
        _shape.UpdateBounds();

        LogShape("AFTER ALL CUTS");

        _editor.Document.MarkModified();
        _editor.Document.UpdateBounds();
        _editor.MarkRasterDirty();

        LogKnife("  Iterative cuts complete");
        Finish();
    }

    private void PerformSingleCut(SegmentLocation entry, SegmentLocation exit)
    {
        LogKnife($"    PerformSingleCut: path={entry.PathIndex}");

        ref readonly var path = ref _shape.GetPath(entry.PathIndex);
        var pathStart = path.AnchorStart;
        var pathAnchorCount = path.AnchorCount;
        var fillColor = path.FillColor;

        // Convert to local segment indices within the path
        var seg1Local = (ushort)(entry.SegmentIndex - pathStart);
        var seg2Local = (ushort)(exit.SegmentIndex - pathStart);

        LogKnife($"    Local segments: {seg1Local}, {seg2Local} (path has {pathAnchorCount} anchors)");

        // Handle same-segment case (notch/slit) - just insert anchors, no split
        if (seg1Local == seg2Local)
        {
            LogKnife("    Same segment - inserting anchors only");
            InsertTwoAnchorsOnSameSegment(entry, exit);
            return;
        }

        // Collect anchors for both new paths
        var anchorsPath1 = new List<(Vector2 pos, float curve)>();
        var anchorsPath2 = new List<(Vector2 pos, float curve)>();

        // Path 1: entry → forward along path → exit
        anchorsPath1.Add((entry.Position, 0f));

        var path1Count = (seg2Local - seg1Local + pathAnchorCount) % pathAnchorCount;
        for (var c = 0; c < path1Count; c++)
        {
            var idx = (seg1Local + 1 + c) % pathAnchorCount;
            var anchorIdx = (ushort)(pathStart + idx);
            ref readonly var anchor = ref _shape.GetAnchor(anchorIdx);
            anchorsPath1.Add((anchor.Position, anchor.Curve));
        }
        anchorsPath1.Add((exit.Position, 0f));

        // Path 2: exit → wrap around path → entry
        anchorsPath2.Add((exit.Position, 0f));

        var path2Count = (seg1Local - seg2Local + pathAnchorCount) % pathAnchorCount;
        for (var c = 0; c < path2Count; c++)
        {
            var idx = (seg2Local + 1 + c) % pathAnchorCount;
            var anchorIdx = (ushort)(pathStart + idx);
            ref readonly var anchor = ref _shape.GetAnchor(anchorIdx);
            anchorsPath2.Add((anchor.Position, anchor.Curve));
        }
        anchorsPath2.Add((entry.Position, 0f));

        LogKnife($"    Path1: {anchorsPath1.Count} anchors, Path2: {anchorsPath2.Count} anchors");

        // Validate both paths have >= 3 anchors for valid polygons
        if (anchorsPath1.Count < 3 || anchorsPath2.Count < 3)
        {
            LogKnife("    Not enough anchors for valid split, skipping");
            return;
        }

        // Delete original path
        for (var i = 0; i < pathAnchorCount; i++)
            _shape.SetAnchorSelected((ushort)(pathStart + i), true);
        _shape.DeleteAnchors();

        // Create two new paths
        var newPath1 = _shape.AddPath(fillColor);
        if (newPath1 != ushort.MaxValue)
        {
            foreach (var (pos, curve) in anchorsPath1)
                _shape.AddAnchor(newPath1, pos, curve);
        }

        var newPath2 = _shape.AddPath(fillColor);
        if (newPath2 != ushort.MaxValue)
        {
            foreach (var (pos, curve) in anchorsPath2)
                _shape.AddAnchor(newPath2, pos, curve);
        }

        LogKnife($"    Split complete: created paths {newPath1} and {newPath2}");
    }

    private void InsertTwoAnchorsOnSameSegment(SegmentLocation loc1, SegmentLocation loc2)
    {
        // Ensure loc1 has smaller T (closer to start of segment)
        if (loc1.T > loc2.T)
            (loc1, loc2) = (loc2, loc1);

        // Insert loc2 first (higher T), then loc1 - so indices stay valid
        _shape.SplitSegmentAtPoint(loc1.SegmentIndex, loc2.Position);
        _shape.SplitSegmentAtPoint(loc1.SegmentIndex, loc1.Position);

        LogKnife($"    Inserted 2 anchors on segment {loc1.SegmentIndex}");
    }

    public override void Draw()
    {
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(_editor.Document.Transform);

            // Draw knife path lines
            Gizmos.SetColor(EditorStyle.KnifeTool.SegmentColor);
            for (var i = 0; i < _pointCount - 1; i++)
                Gizmos.DrawLine(_points[i].Position, _points[i + 1].Position, EditorStyle.Shape.SegmentLineWidth);

            // Draw preview line to mouse
            if (_pointCount > 0)
                Gizmos.DrawLine(_points[_pointCount - 1].Position, _hoverPosition, EditorStyle.Shape.SegmentLineWidth);

            // Draw knife points (resolved vs ambiguous)
            for (var i = 0; i < _pointCount; i++)
            {
                var isAmbiguous = _points[i].CandidateCount > 1 && _points[i].ResolvedIndex < 0;
                Gizmos.SetColor(isAmbiguous ? EditorStyle.KnifeTool.IntersectionColor : EditorStyle.KnifeTool.AnchorColor);
                Gizmos.DrawRect(_points[i].Position, EditorStyle.Shape.AnchorSize);
            }

            // Draw intersection markers
            Gizmos.SetColor(EditorStyle.KnifeTool.IntersectionColor);
            foreach (var intersection in _intersections)
                Gizmos.DrawRect(intersection.Position, EditorStyle.Shape.AnchorSize * EditorStyle.KnifeTool.IntersectionAnchorScale);

            // Draw preview intersections
            if (_pointCount > 0)
            {
                var tempIntersections = new List<SegmentIntersection>();
                FindPreviewIntersections(_points[_pointCount - 1].Position, _hoverPosition, tempIntersections);
                foreach (var intersection in tempIntersections)
                    Gizmos.DrawRect(intersection.Position, EditorStyle.Shape.AnchorSize * EditorStyle.KnifeTool.IntersectionAnchorScale);
            }

            // Draw hover indicator
            var isMultipleHover = _hoverCandidateCount > 1;
            if (_hoverType == KnifePointType.Anchor || _hoverType == KnifePointType.Segment)
            {
                Gizmos.SetColor(isMultipleHover ? EditorStyle.KnifeTool.IntersectionColor : EditorStyle.SelectionColor);
                Gizmos.DrawRect(_hoverPosition, EditorStyle.Shape.AnchorSize * 1.3f);
            }
            else
            {
                Gizmos.SetColor(EditorStyle.Shape.AnchorColor.WithAlpha(0.5f));
                Gizmos.DrawRect(_hoverPosition, EditorStyle.Shape.AnchorSize * 0.8f);
            }
        }
    }

    private void FindPreviewIntersections(Vector2 knifeStart, Vector2 knifeEnd, List<SegmentIntersection> results)
    {
        for (ushort p = 0; p < _shape.PathCount; p++)
        {
            // Only consider resolved path if we have one
            if (_resolvedPathIndex != ushort.MaxValue && p != _resolvedPathIndex)
                continue;

            ref readonly var path = ref _shape.GetPath(p);
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var segmentIndex = (ushort)(path.AnchorStart + a);
                FindPreviewSegmentIntersections(knifeStart, knifeEnd, p, segmentIndex, results);
            }
        }
    }

    private void FindPreviewSegmentIntersections(Vector2 knifeStart, Vector2 knifeEnd, ushort pathIndex, ushort segmentIndex, List<SegmentIntersection> results)
    {
        var samples = _shape.GetSegmentSamples(segmentIndex);
        ref readonly var a0 = ref _shape.GetAnchor(segmentIndex);
        ref readonly var a1 = ref _shape.GetNextAnchor(segmentIndex);

        var prev = a0.Position;
        var tBase = 0f;
        var tStep = 1f / (Shape.MaxSegmentSamples + 1);

        for (var i = 0; i < Shape.MaxSegmentSamples; i++)
        {
            if (LineSegmentIntersect(knifeStart, knifeEnd, prev, samples[i], out var hit, out _, out _))
            {
                results.Add(new SegmentIntersection
                {
                    PathIndex = pathIndex,
                    SegmentIndex = segmentIndex,
                    Position = hit,
                    T = tBase
                });
            }
            prev = samples[i];
            tBase += tStep;
        }

        if (LineSegmentIntersect(knifeStart, knifeEnd, prev, a1.Position, out var hitLast, out _, out _))
        {
            results.Add(new SegmentIntersection
            {
                PathIndex = pathIndex,
                SegmentIndex = segmentIndex,
                Position = hitLast,
                T = tBase
            });
        }
    }

    private void Finish()
    {
        Workspace.EndTool();
        Input.ConsumeButton(InputCode.MouseLeft);
        Input.ConsumeButton(InputCode.MouseRight);
    }

    public override void Cancel()
    {
        Finish();
    }

    public override void End()
    {
        Cursor.SetDefault();
    }

    [Conditional("NOZ_KNIFE_DEBUG")]
    private void LogKnife(string msg)
    {
        Log.Debug($"[KNIFE] {msg}");
    }

    [Conditional("NOZ_KNIFE_DEBUG")]
    private void LogShape(string label)
    {
        Log.Debug($"[KNIFE] === {label} ===");
        Log.Debug($"[KNIFE] Shape: {_shape.PathCount} paths, {_shape.AnchorCount} anchors");
        for (ushort p = 0; p < _shape.PathCount; p++)
        {
            ref readonly var path = ref _shape.GetPath(p);
            Log.Debug($"[KNIFE]   Path {p}: FillColor={path.FillColor} AnchorStart={path.AnchorStart} AnchorCount={path.AnchorCount}");
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchorIdx = (ushort)(path.AnchorStart + a);
                ref readonly var anchor = ref _shape.GetAnchor(anchorIdx);
                Log.Debug($"[KNIFE]     Anchor {a} (idx {anchorIdx}): Pos={anchor.Position} Curve={anchor.Curve}");
            }
        }
        Log.Debug($"[KNIFE] === End {label} ===");
    }
}
