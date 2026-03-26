//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_KNIFE_DEBUG

using System.Diagnostics;
using System.Numerics;

namespace NoZ.Editor;

public class KnifeTool : Tool
{
    private const int MaxPoints = 128;
    private const float HoverAnchorScale = 1.5f;
    private const float IntersectionAnchorScale = 1.2f;

    private struct KnifePoint
    {
        public Vector2 Position;
        public bool Intersection;
        public bool IsFree;
    }

    private struct KnifeSegment
    {
        public int Start;
        public int End;
    }

    private readonly List<KnifePoint> _points = new(MaxPoints);
    private readonly SpriteDocument _document;
    private readonly List<SpritePath> _selectedPaths;
    private Vector2 _hoverPosition;
    private bool _hoverPositionValid;
    private bool _hoverIsClose;
    private bool _hoverIsIntersection;
    private bool _hoverIsFree;
    private int _pointCount;
    private Action? _commit;
    private Action? _cancel;

    public KnifeTool(SpriteDocument document, List<SpritePath> selectedPaths, Action? commit = null, Action? cancel = null)
    {
        _document = document;
        _selectedPaths = new List<SpritePath>(selectedPaths);
        _commit = commit;
        _cancel = cancel;
    }

    public override void Begin()
    {
        EditorCursor.SetCrosshair();
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
            if (_points.Count >= 2)
                Commit();
            else
                Cancel();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseRight))
        {
            if (_points.Count > 0)
                RemoveLastPoint();
            else
                Cancel();
            return;
        }

        UpdateHover();

        if (Input.WasButtonPressed(InputCode.MouseLeft))
            AddPoint();
    }

    public override void Cancel()
    {
        Finish();
        _cancel?.Invoke();
    }

    private bool DoesIntersectSelf(in Vector2 position)
    {
        if (_pointCount < 2)
            return false;

        var end = _points[_pointCount - 1].Position;
        for (var i = 0; i < _pointCount - 2; i++)
        {
            if (Physics.OverlapLine(end, position, _points[i].Position, _points[i + 1].Position, out _))
                return true;
        }

        return false;
    }

    private void AddPoint()
    {
        if (!_hoverPositionValid)
            return;

        if (_pointCount == 0)
            _points.Add(new KnifePoint { Position = _hoverPosition, Intersection = _hoverIsIntersection });

        _pointCount = _points.Count;

        if (_hoverIsClose)
            Commit();
    }

    private void RemoveLastPoint()
    {
    }

    private int GetKnifeSegments(Span<KnifeSegment> segments)
    {
        var pointIndex = 0;
        for (; pointIndex < _pointCount; pointIndex++)
        {
            if (_points[pointIndex].Intersection) break;
        }

        var segmentCount = 0;
        while (pointIndex < _pointCount - 1)
        {
            segments[segmentCount].Start = pointIndex;

            var free = false;
            for (pointIndex++; pointIndex < _pointCount; pointIndex++)
            {
                free |= _points[pointIndex].IsFree;
                if (_points[pointIndex].Intersection) break;
            }

            if (free) continue;

            if (pointIndex >= _pointCount) break;

            segments[segmentCount].End = pointIndex;
            segmentCount++;
        }

        return segmentCount;
    }

    private void Commit()
    {
        // Trim hover points
        while (_points.Count > _pointCount)
            _points.RemoveAt(_points.Count - 1);

        if (_points.Count < 2)
        {
            Finish();
            return;
        }

        Undo.Record(_document);

        Span<KnifeSegment> knifeSegments = stackalloc KnifeSegment[MaxPoints / 2];
        var knifeSegmentCount = GetKnifeSegments(knifeSegments);

        var allHits = new List<SpriteNode.NodeHitResult>();

        for (var ksi = 0; ksi < knifeSegmentCount; ksi++)
        {
            ref var ks = ref knifeSegments[ksi];
            var headPos = _points[ks.Start].Position;
            var tailPos = _points[ks.End].Position;

            // Find head and tail hits (restricted to selected paths)
            allHits.Clear();
            HitTestSelectedPathsAll(headPos, allHits);
            var headHits = allHits.ToArray();

            allHits.Clear();
            HitTestSelectedPathsAll(tailPos, allHits);
            var tailHits = allHits.ToArray();

            // Find a shared path between head and tail
            SpritePath? commonPath = null;
            SpritePath.HitResult headHit = default, tailHit = default;

            foreach (var hh in headHits)
            {
                foreach (var th in tailHits)
                {
                    if (hh.Path == th.Path)
                    {
                        commonPath = hh.Path;
                        headHit = hh.Hit;
                        tailHit = th.Hit;
                        break;
                    }
                }
                if (commonPath != null) break;
            }

            if (commonPath == null)
                continue;
            var commonLayer = commonPath.Parent as SpriteLayer;
            if (commonLayer == null)
                continue;

            if (headHit.SegmentIndex < 0 || tailHit.SegmentIndex < 0)
                continue;

            // Collect intermediate points
            Span<Vector2> intermediatePositions = stackalloc Vector2[ks.End - ks.Start - 1];
            for (var i = 0; i < intermediatePositions.Length; i++)
                intermediatePositions[i] = _points[ks.Start + 1 + i].Position;

            if (headHit.SegmentIndex == tailHit.SegmentIndex)
                CutNotch(commonPath, commonLayer, headHit, tailHit, intermediatePositions);
            else
                CutPath(commonPath, commonLayer, headHit, tailHit, intermediatePositions);

            commonPath.UpdateSamples();
        }

        _document.IncrementVersion();
        _document.UpdateBounds();
        Finish();
        _commit?.Invoke();
    }

    private static void CutNotch(SpritePath path, SpriteLayer layer,
        SpritePath.HitResult headHit, SpritePath.HitResult tailHit,
        ReadOnlySpan<Vector2> intermediatePoints)
    {
        var headAnchors = path.Contours[headHit.ContourIndex].Anchors;
        var tailAnchors = path.Contours[tailHit.ContourIndex].Anchors;
        var headDistToA = Vector2.DistanceSquared(headHit.SegmentPosition, headAnchors[headHit.SegmentIndex].Position);
        var tailDistToA = Vector2.DistanceSquared(tailHit.SegmentPosition, tailAnchors[tailHit.SegmentIndex].Position);
        var reversed = headDistToA > tailDistToA;

        if (reversed)
            (headHit, tailHit) = (tailHit, headHit);

        // Split segment at head
        path.SplitSegmentAtPoint(headHit.SegmentIndex, headHit.SegmentPosition);
        var headIdx = headHit.SegmentIndex + 1;

        // Split at tail (index shifted by 1 from head insert)
        path.SplitSegmentAtPoint(headIdx, tailHit.SegmentPosition);
        var tailIdx = headIdx + 1;

        // Zero out the curve on the head segment
        path.SetAnchorCurve(headIdx, 0);

        // Insert intermediate anchors
        var insertIdx = headIdx;
        Span<Vector2> positions = stackalloc Vector2[intermediatePoints.Length];
        if (reversed)
            for (var i = 0; i < intermediatePoints.Length; i++)
                positions[i] = intermediatePoints[intermediatePoints.Length - i - 1];
        else
            intermediatePoints.CopyTo(positions);

        for (var i = 0; i < positions.Length; i++)
        {
            path.InsertAnchor(insertIdx, positions[i]);
            insertIdx++;
        }

        // Create a new notch path
        var notchPath = new SpritePath { FillColor = path.FillColor, Operation = path.Operation };
        notchPath.AddAnchor(headHit.SegmentPosition);
        for (var i = 0; i < positions.Length; i++)
            notchPath.AddAnchor(positions[i]);
        notchPath.AddAnchor(tailHit.SegmentPosition);
        if (notchPath.Anchors.Count >= 3)
            layer.Add(notchPath);
    }

    private static void CutPath(SpritePath path, SpriteLayer layer,
        SpritePath.HitResult headHit, SpritePath.HitResult tailHit,
        ReadOnlySpan<Vector2> intermediatePoints)
    {
        var reverseIntermediates = headHit.SegmentIndex > tailHit.SegmentIndex;

        if (reverseIntermediates)
            (headHit, tailHit) = (tailHit, headHit);

        // Get or create anchor at head position
        int headAnchorIdx;
        if (headHit.AnchorIndex >= 0)
        {
            headAnchorIdx = headHit.AnchorIndex;
        }
        else
        {
            path.SplitSegmentAtPoint(headHit.SegmentIndex, headHit.SegmentPosition);
            headAnchorIdx = headHit.SegmentIndex + 1;
            // Adjust tail indices
            if (tailHit.AnchorIndex >= 0) tailHit.AnchorIndex++;
            tailHit.SegmentIndex++;
            path.UpdateSamples();
        }

        int tailAnchorIdx;
        if (tailHit.AnchorIndex >= 0)
        {
            tailAnchorIdx = tailHit.AnchorIndex;
        }
        else
        {
            path.SplitSegmentAtPoint(tailHit.SegmentIndex, tailHit.SegmentPosition);
            tailAnchorIdx = tailHit.SegmentIndex + 1;
        }

        Span<Vector2> positions = stackalloc Vector2[intermediatePoints.Length];
        intermediatePoints.CopyTo(positions);

        var newPath = path.SplitAtAnchors(headAnchorIdx, tailAnchorIdx, positions, reverseIntermediates);
        if (newPath != null)
            layer.Add(newPath);
    }

    private void UpdateHover()
    {
        Matrix3x2.Invert(_document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        if (_hoverPosition == mouseLocal) return;

        _hoverPosition = mouseLocal;
        _hoverPositionValid = !DoesIntersectSelf(_hoverPosition);

        var anchorHitRadius = EditorStyle.SpritePath.AnchorHitRadius;

        _hoverIsClose = _points.Count > 0 &&
            Vector2.DistanceSquared(_hoverPosition, _points[0].Position) <
            anchorHitRadius * anchorHitRadius;

        _hoverIsIntersection = false;
        _hoverIsFree = false;

        if (_hoverIsClose)
        {
            _hoverPosition = _points[0].Position;
            _hoverPositionValid = true;
        }
        else
        {
            // Check anchor/segment hits on selected paths
            foreach (var path in _selectedPaths)
            {
                var (_, anchorIdx, _, anchorPos) = path.HitTestAnchor(_hoverPosition);
                if (anchorIdx >= 0)
                {
                    _hoverPosition = anchorPos;
                    _hoverIsIntersection = true;
                    _hoverIsFree = true;
                    break;
                }

                var (_, segIdx, _, segPos) = path.HitTestSegment(_hoverPosition);
                if (segIdx >= 0)
                {
                    _hoverPosition = segPos;
                    _hoverIsIntersection = true;
                    _hoverIsFree = true;
                    break;
                }

                if (path.HitTestPath(_hoverPosition))
                    _hoverIsFree = true;
            }
        }

        if (_pointCount > 0)
            UpdateHoverIntersections(_hoverPosition, _points[_pointCount - 1].Position);
    }

    public override void Draw()
    {
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(_document.Transform);

            Gizmos.SetColor(EditorStyle.Tool.LineColor);
            for (var i = 0; i < _points.Count - 1; i++)
                Gizmos.DrawLine(_points[i].Position, _points[i + 1].Position, EditorStyle.SpritePath.SegmentLineWidth);

            if (_points.Count > 0)
            {
                Gizmos.SetColor(_hoverPositionValid
                    ? EditorStyle.Tool.LineColor
                    : EditorStyle.KnifeTool.InvalidSegmentColor);
                Gizmos.DrawLine(_points[^1].Position, _hoverPosition, EditorStyle.SpritePath.SegmentLineWidth);
            }

            for (var i = 0; i < _points.Count; i++)
                Gizmos.DrawAnchor(_points[i].Position, selected: true, scale: IntersectionAnchorScale);

            Gizmos.DrawAnchor(_hoverPosition, selected: false, scale: HoverAnchorScale);
        }
    }

    private void UpdateHoverIntersections(in Vector2 from, in Vector2 to)
    {
        // Remove hover points (keep committed points)
        while (_points.Count > _pointCount)
            _points.RemoveAt(_points.Count - 1);

        if (!_hoverPositionValid)
            return;

        _points.Add(new KnifePoint { Position = from, Intersection = _hoverIsIntersection, IsFree = !_hoverIsFree });

        // Find intersections with selected paths only
        CollectIntersections(from, to);

        // Sort by distance from 'to' (last committed point)
        var hoverCount = _points.Count - _pointCount;
        if (hoverCount <= 1) return;

        var origin = to;
        var hoverSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_points);
        hoverSpan[_pointCount..].Sort((a, b) =>
            Vector2.DistanceSquared(origin, a.Position).CompareTo(Vector2.DistanceSquared(origin, b.Position)));

        // Remove duplicates
        var duplicateThreshold = 0.5f / EditorApplication.Config.PixelsPerUnit;
        var duplicateThresholdSqr = duplicateThreshold * duplicateThreshold;
        var writeIdx = _pointCount;

        for (var readIdx = _pointCount; readIdx < _points.Count; readIdx++)
        {
            if (Vector2.DistanceSquared(_points[readIdx].Position, _points[writeIdx - 1].Position) < duplicateThresholdSqr)
            {
                if (_points[readIdx].Intersection)
                    _points[writeIdx] = _points[readIdx];
            }
            else
            {
                if (writeIdx < readIdx)
                    _points[writeIdx] = _points[readIdx];
                writeIdx++;
            }
        }

        while (_points.Count > writeIdx)
            _points.RemoveAt(_points.Count - 1);
    }

    private void CollectIntersections(Vector2 from, Vector2 to)
    {
        foreach (var path in _selectedPaths)
        {
            if (path.TotalAnchorCount < 2) continue;
            path.UpdateSamples();

            foreach (var contour in path.Contours)
            {
                var count = contour.Anchors.Count;
                if (count < 2) continue;
                var segmentCount = contour.Open ? count - 1 : count;

                for (var a = 0; a < segmentCount; a++)
                {
                    var a0 = contour.Anchors[a];
                    var a1 = contour.Anchors[(a + 1) % count];
                    var samples = contour.GetSegmentSamples(a);

                    var prev = a0.Position;
                    foreach (var sample in samples)
                    {
                        if (Physics.OverlapLine(from, to, prev, sample, out var intersection))
                            _points.Add(new KnifePoint { Position = intersection, Intersection = true });
                        prev = sample;
                    }

                    if (Physics.OverlapLine(from, to, prev, a1.Position, out var lastIntersection))
                        _points.Add(new KnifePoint { Position = lastIntersection, Intersection = true });
                }
            }
        }
    }

    private void HitTestSelectedPathsAll(Vector2 point, List<SpriteNode.NodeHitResult> results)
    {
        foreach (var path in _selectedPaths)
        {
            var hit = path.HitTest(point);
            if (hit.AnchorIndex >= 0 || hit.SegmentIndex >= 0 || hit.InPath)
            {
                // Find the parent layer for this path
                results.Add(new SpriteNode.NodeHitResult { Path = path, Hit = hit });
            }
        }
    }

    private void Finish()
    {
        Workspace.EndTool();
        Input.ConsumeButton(InputCode.MouseLeft);
        Input.ConsumeButton(InputCode.MouseRight);
    }

    [Conditional("NOZ_KNIFE_DEBUG")]
    private void LogKnife(string msg) => Log.Debug($"[KNIFE] {msg}");
}
