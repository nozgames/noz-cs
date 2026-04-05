//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class BevelTool : Tool
{
    private readonly SpriteEditor _editor;
    private readonly SpritePath _path;
    private readonly int _contourIndex;
    private readonly int _anchorIndex;
    private readonly SpritePathAnchor[] _saved;
    private readonly Matrix3x2 _transform;
    private readonly Matrix3x2 _invTransform;

    private readonly Vector2 _savedBoundsCenter;
    private readonly Vector2 _savedTranslation;

    // Original corner geometry
    private readonly Vector2 _originalPos;
    private readonly Vector2 _prevPos;
    private readonly Vector2 _nextPos;
    private readonly float _prevCurve; // curve on prev anchor's outgoing segment (prevIdx -> anchorIndex)
    private readonly float _origCurve; // curve on original anchor's outgoing segment (anchorIndex -> nextIdx)
    private readonly int _prevIdx;
    private readonly int _nextIdx;
    private readonly float _maxDist;

    private Vector2 _startMouseLocal;

    // Current bevel state for drawing
    private Vector2 _p1, _p2;
    private int _p1Idx, _p2Idx;

    public bool CommitOnRelease { get; set; }

    public BevelTool(SpriteEditor editor, SpritePath path, Matrix3x2 transform,
        SpritePathAnchor[] saved, int anchorIndex, int contourIndex = 0)
    {
        _editor = editor;
        _path = path;
        _contourIndex = contourIndex;
        _anchorIndex = anchorIndex;
        _saved = saved;
        _transform = path.HasTransform ? path.PathTransform * transform : transform;
        _savedBoundsCenter = path.LocalBounds.Center;
        _savedTranslation = path.PathTranslation;

        Matrix3x2.Invert(_transform, out _invTransform);

        var anchors = saved;
        var count = anchors.Length;
        _originalPos = anchors[anchorIndex].Position;
        _prevIdx = (anchorIndex - 1 + count) % count;
        _nextIdx = (anchorIndex + 1) % count;
        _prevPos = anchors[_prevIdx].Position;
        _nextPos = anchors[_nextIdx].Position;
        _prevCurve = anchors[_prevIdx].Curve;
        _origCurve = anchors[anchorIndex].Curve;

        var distPrev = Vector2.Distance(_originalPos, _prevPos);
        var distNext = Vector2.Distance(_originalPos, _nextPos);
        _maxDist = MathF.Min(distPrev, distNext);

        _p1 = _originalPos;
        _p2 = _originalPos;
    }

    public override void Begin()
    {
        base.Begin();
        _startMouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape, Scope) || Input.WasButtonPressed(InputCode.MouseRight, Scope))
        {
            Workspace.CancelTool();
            return;
        }

        var commitInput = CommitOnRelease
            ? Input.WasButtonReleased(InputCode.MouseLeft, Scope)
            : Input.WasButtonPressed(InputCode.MouseLeft, Scope) || Input.WasButtonPressed(InputCode.KeyEnter, Scope);

        if (commitInput)
        {
            _path.UpdateSamples();
            _path.UpdateBounds();
            _path.PathTranslation = _savedTranslation;
            _path.CompensateTranslation(_savedBoundsCenter);
            _editor.MarkDirty();
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
        var dragDist = Vector2.Distance(mouseLocal, _startMouseLocal);

        if (_maxDist < 0.001f)
            return;

        var t = Math.Clamp(dragDist / _maxDist, 0f, 0.95f);

        // Restore anchors from snapshot
        var contour = _path.Contours[_contourIndex];
        contour.Anchors.Clear();
        for (var i = 0; i < _saved.Length; i++)
            contour.Anchors.Add(_saved[i]);

        if (t < 0.001f)
        {
            _p1 = _originalPos;
            _p2 = _originalPos;
            _path.MarkDirty();
            _path.UpdateSamples();
            _path.UpdateBounds();
            _path.PathTranslation = _savedTranslation;
            _path.CompensateTranslation(_savedBoundsCenter);
            _editor.MarkDirty();
            return;
        }

        // Compute P1: point on segment prev->original, at fraction t from original toward prev
        float leftCurve;
        if (MathF.Abs(_prevCurve) < SpritePath.MinCurve)
        {
            _p1 = Vector2.Lerp(_originalPos, _prevPos, t);
            leftCurve = 0f;
        }
        else
        {
            // Evaluate quadratic bezier on prev segment at (1 - t) from prev end
            // Segment goes from prevPos to originalPos with curve = _prevCurve
            var splitT = 1f - t;
            var mid = (_prevPos + _originalPos) * 0.5f;
            var dir = _originalPos - _prevPos;
            var perp = new Vector2(-dir.Y, dir.X);
            var len = perp.Length();
            if (len > 0) perp /= len;
            var control = mid + perp * _prevCurve;

            var u = 1f - splitT;
            _p1 = u * u * _prevPos + 2 * u * splitT * control + splitT * splitT * _originalPos;

            // De Casteljau split: compute left sub-curve (prevPos -> P1)
            var cLeft = Vector2.Lerp(_prevPos, control, splitT);
            leftCurve = SpritePath.ProjectCurve(_prevPos, _p1, cLeft);
        }

        // Compute P2: point on segment original->next, at fraction t from original toward next
        float rightCurve;
        if (MathF.Abs(_origCurve) < SpritePath.MinCurve)
        {
            _p2 = Vector2.Lerp(_originalPos, _nextPos, t);
            rightCurve = 0f;
        }
        else
        {
            // Evaluate quadratic bezier on orig segment at t from original end
            // Segment goes from originalPos to nextPos with curve = _origCurve
            var mid = (_originalPos + _nextPos) * 0.5f;
            var dir = _nextPos - _originalPos;
            var perp = new Vector2(-dir.Y, dir.X);
            var len = perp.Length();
            if (len > 0) perp /= len;
            var control = mid + perp * _origCurve;

            var u = 1f - t;
            _p2 = u * u * _originalPos + 2 * u * t * control + t * t * _nextPos;

            // De Casteljau split: compute right sub-curve (P2 -> nextPos)
            var cRight = Vector2.Lerp(control, _nextPos, t);
            rightCurve = SpritePath.ProjectCurve(_p2, _nextPos, cRight);
        }

        // Compute bevel segment curve
        float bevelCurve;
        if (Input.IsCtrlDown(Scope))
        {
            // Smooth bevel: project original position onto bevel chord for optimal arc
            bevelCurve = SpritePath.ProjectCurve(_p1, _p2, _originalPos);
        }
        else
        {
            bevelCurve = 0f;
        }

        // Rebuild the anchor list with the bevel applied
        contour.Anchors.Clear();
        for (var i = 0; i < _saved.Length; i++)
        {
            if (i == _prevIdx)
            {
                // Previous anchor: update its curve to leftCurve (truncated segment)
                var a = _saved[i];
                a.Curve = SpritePath.ClampCurve(leftCurve);
                contour.Anchors.Add(a);
            }
            else if (i == _anchorIndex)
            {
                // Replace original anchor with two new anchors
                contour.Anchors.Add(new SpritePathAnchor
                {
                    Position = _p1,
                    Curve = SpritePath.ClampCurve(bevelCurve),
                    Flags = SpritePathAnchorFlags.Selected,
                });
                contour.Anchors.Add(new SpritePathAnchor
                {
                    Position = _p2,
                    Curve = SpritePath.ClampCurve(rightCurve),
                    Flags = SpritePathAnchorFlags.Selected,
                });
            }
            else
            {
                contour.Anchors.Add(_saved[i]);
            }
        }

        // Track indices for drawing
        _p1Idx = _anchorIndex;
        _p2Idx = _anchorIndex + 1;

        _path.MarkDirty();
        _path.UpdateSamples();
        _path.UpdateBounds();
        _path.PathTranslation = _savedTranslation;
        _path.CompensateTranslation(_savedBoundsCenter);
        _editor.MarkDirty();
    }

    public override void Draw()
    {
        using var _ = Gizmos.PushState(EditorLayer.Tool);

        Graphics.SetTransform(_transform);

        var contour = _path.Contours[_contourIndex];
        var anchors = contour.Anchors;

        if (anchors.Count <= _anchorIndex)
            return;

        // Draw the bevel segment highlighted
        Gizmos.SetColor(EditorStyle.Palette.Primary);

        // Draw segment from prev -> P1
        DrawSegment(contour, _prevIdx < anchors.Count ? _prevIdx : 0);

        // Draw bevel segment P1 -> P2
        if (_p1Idx < anchors.Count)
            DrawSegment(contour, _p1Idx);

        // Draw segment P2 -> next
        if (_p2Idx < anchors.Count)
            DrawSegment(contour, _p2Idx);

        // Draw dashed lines showing original corner
        Gizmos.SetColor(EditorStyle.Workspace.SelectionColor.WithAlpha(0.5f));
        Gizmos.DrawDashedLine(_p1, _originalPos);
        Gizmos.DrawDashedLine(_originalPos, _p2);
    }

    private void DrawSegment(SpriteContour contour, int segIdx)
    {
        var anchors = contour.Anchors;
        if (segIdx >= anchors.Count) return;
        var samples = contour.GetSegmentSamples(segIdx);
        var prev = anchors[segIdx].Position;
        foreach (var sample in samples)
        {
            Gizmos.DrawLine(prev, sample, EditorStyle.SpritePath.SegmentLineWidth, order: 2);
            prev = sample;
        }
        var nextIdx = (segIdx + 1) % anchors.Count;
        Gizmos.DrawLine(prev, anchors[nextIdx].Position, EditorStyle.SpritePath.SegmentLineWidth, order: 2);
    }

    public override void Cancel()
    {
        Undo.Cancel();
    }
}
