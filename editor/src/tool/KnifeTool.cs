//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

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

    private static Vector2 ClosestPointOnLine(Vector2 a, Vector2 b, Vector2 p)
    {
        var ab = b - a;
        var lenSq = Vector2.Dot(ab, ab);
        if (lenSq < 0.0001f)
            return a;

        var t = MathF.Max(0, MathF.Min(1, Vector2.Dot(p - a, ab) / lenSq));
        return a + ab * t;
    }

    private void AddPoint()
    {
        if (_pointCount >= MaxPoints)
            return;

        if (_pointCount > 0)
        {
            var lastPos = _points[_pointCount - 1].Position;
            if (Vector2.Distance(_hoverPosition, lastPos) < 0.001f)
                return;
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
                _resolvedPathIndex = _hoverCandidates[0].PathIndex;
        }
        else if (_hoverCandidateCount > 1 && _resolvedPathIndex != ushort.MaxValue)
        {
            // Try to match resolved path
            for (var i = 0; i < _hoverCandidateCount; i++)
            {
                if (_hoverCandidates[i].PathIndex == _resolvedPathIndex)
                {
                    resolvedIndex = i;
                    break;
                }
            }
        }

        _points[_pointCount++] = new KnifePoint
        {
            Position = _hoverPosition,
            Type = _hoverType,
            Candidates = candidates,
            CandidateCount = _hoverCandidateCount,
            ResolvedIndex = resolvedIndex,
        };

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
            FindIntersectionsForSegment(p0, p1);
        }

        // Intersections can help resolve ambiguity
        if (_resolvedPathIndex == ushort.MaxValue && _intersections.Count > 0)
        {
            _resolvedPathIndex = _intersections[0].PathIndex;
            TryResolveAmbiguousPoints();
        }
    }

    private void FindIntersectionsForSegment(Vector2 knifeStart, Vector2 knifeEnd)
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
                FindSegmentIntersections(knifeStart, knifeEnd, p, segmentIndex);
            }
        }
    }

    private void FindSegmentIntersections(Vector2 knifeStart, Vector2 knifeEnd, ushort pathIndex, ushort segmentIndex)
    {
        var samples = _shape.GetSegmentSamples(segmentIndex);
        ref readonly var a0 = ref _shape.GetAnchor(segmentIndex);
        ref readonly var a1 = ref _shape.GetNextAnchor(segmentIndex);

        var prev = a0.Position;
        var tBase = 0f;
        var tStep = 1f / (Shape.MaxSegmentSamples + 1);

        for (var i = 0; i < Shape.MaxSegmentSamples; i++)
        {
            if (LineSegmentIntersect(knifeStart, knifeEnd, prev, samples[i], out var hit, out var localT))
            {
                var globalT = tBase + localT * tStep;
                if (!IsDuplicateIntersection(segmentIndex, globalT))
                {
                    _intersections.Add(new SegmentIntersection
                    {
                        PathIndex = pathIndex,
                        SegmentIndex = segmentIndex,
                        Position = hit,
                        T = globalT
                    });
                }
            }
            prev = samples[i];
            tBase += tStep;
        }

        if (LineSegmentIntersect(knifeStart, knifeEnd, prev, a1.Position, out var hitLast, out var localTLast))
        {
            var globalT = tBase + localTLast * tStep;
            if (!IsDuplicateIntersection(segmentIndex, globalT))
            {
                _intersections.Add(new SegmentIntersection
                {
                    PathIndex = pathIndex,
                    SegmentIndex = segmentIndex,
                    Position = hitLast,
                    T = globalT
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
        out float t)
    {
        var d1 = p2 - p1;
        var d2 = p4 - p3;
        var cross = d1.X * d2.Y - d1.Y * d2.X;

        if (MathF.Abs(cross) < 0.0001f)
        {
            intersection = default;
            t = 0;
            return false;
        }

        var d3 = p1 - p3;
        var t1 = (d2.X * d3.Y - d2.Y * d3.X) / cross;
        t = (d1.X * d3.Y - d1.Y * d3.X) / cross;

        if (t1 >= 0 && t1 <= 1 && t >= 0 && t <= 1)
        {
            intersection = p1 + d1 * t1;
            return true;
        }

        intersection = default;
        return false;
    }

    private void Commit()
    {
        if (_pointCount < 2 && _intersections.Count == 0)
        {
            Finish();
            return;
        }

        // Final disambiguation: resolve any remaining ambiguous points to first candidate
        for (var i = 0; i < _pointCount; i++)
        {
            if (_points[i].ResolvedIndex < 0 && _points[i].CandidateCount > 0)
                _points[i].ResolvedIndex = 0;
        }

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
            if (LineSegmentIntersect(knifeStart, knifeEnd, prev, samples[i], out var hit, out _))
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

        if (LineSegmentIntersect(knifeStart, knifeEnd, prev, a1.Position, out var hitLast, out _))
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
}
