//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class BevelTool : Tool
{
    private struct BevelTarget
    {
        public int AnchorIndex;
        public int PrevIdx, NextIdx;
        public Vector2 OriginalPos, PrevPos, NextPos;
        public float PrevCurve, OrigCurve;
        public float MaxDist;

        // Current frame state
        public Vector2 P1, P2;
        public float LeftCurve, RightCurve, BevelCurve;
    }

    private readonly SpriteEditor _editor;

    // Active drag state (only valid when _dragging)
    private bool _dragging;
    private SpritePath? _dragPath;
    private int _dragContourIndex;
    private SpritePathAnchor[]? _saved;
    private Matrix3x2 _transform;
    private Matrix3x2 _invTransform;
    private Vector2 _savedBoundsCenter;
    private Vector2 _savedTranslation;
    private BevelTarget[]? _targets;
    private int _primaryIndex;
    private Dictionary<int, int> _targetByAnchor = new();
    private Dictionary<int, int> _prevOfTarget = new();

    // Hover state (idle)
    private bool _hoveringAnchor;
    private Vector2 _hoverAnchorPos;

    public BevelTool(SpriteEditor editor)
    {
        _editor = editor;
    }

    public override void Begin()
    {
        base.Begin();
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape, Scope) || Input.WasButtonPressed(InputCode.MouseRight, Scope))
        {
            if (_dragging)
            {
                CancelDrag();
                return;
            }
            Workspace.CancelTool();
            return;
        }

        if (_dragging)
            UpdateDrag();
        else
            UpdateIdle();
    }

    private void UpdateIdle()
    {
        EditorCursor.SetCrosshair();

        Matrix3x2.Invert(_editor.Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        // Update hover
        _hoveringAnchor = false;
        var path = _editor.Document.RootLayer.GetPathWithSelection();
        if (path == null)
        {
            // Try to find any path with an anchor under the cursor
            var hit = _editor.Document.RootLayer.HitTestAnchor(mouseLocal);
            if (hit.HasValue)
            {
                _hoveringAnchor = true;
                _hoverAnchorPos = hit.Value.Position;
            }
            return;
        }

        var anchorHit = path.HitTestAnchor(mouseLocal);
        if (anchorHit.AnchorIndex >= 0)
        {
            _hoveringAnchor = true;
            _hoverAnchorPos = anchorHit.Position;
        }

        if (!Input.WasButtonPressed(InputCode.MouseLeft, Scope))
            return;

        // Start a drag
        if (anchorHit.AnchorIndex < 0)
            return;

        var ci = anchorHit.ContourIndex;
        var contour = path.Contours[ci];
        if (contour.Anchors.Count < 3) return;

        var hitIdx = anchorHit.AnchorIndex;
        if (contour.Open && (hitIdx == 0 || hitIdx == contour.Anchors.Count - 1)) return;

        var hitIsSelected = contour.Anchors[hitIdx].IsSelected;
        if (!hitIsSelected)
        {
            path.ClearAnchorSelection();
            path.SetAnchorSelected(ci, hitIdx, true);
        }

        // Collect all selected valid anchors
        var anchorIndices = new List<int>();
        for (var i = 0; i < contour.Anchors.Count; i++)
        {
            if (!contour.Anchors[i].IsSelected) continue;
            if (contour.Open && (i == 0 || i == contour.Anchors.Count - 1)) continue;
            anchorIndices.Add(i);
        }

        if (anchorIndices.Count == 0) return;

        BeginDrag(path, ci, anchorIndices.ToArray(), hitIdx);
    }

    private void BeginDrag(SpritePath path, int contourIndex, int[] anchorIndices, int primaryAnchorIndex)
    {
        Undo.Record(_editor.Document);

        _dragging = true;
        _dragPath = path;
        _dragContourIndex = contourIndex;
        _saved = path.SnapshotAnchors(contourIndex);
        _transform = path.HasTransform ? path.PathTransform * _editor.Document.Transform : _editor.Document.Transform;
        _savedBoundsCenter = path.LocalBounds.Center;
        _savedTranslation = path.PathTranslation;

        Matrix3x2.Invert(_transform, out _invTransform);

        var count = _saved.Length;
        _targets = new BevelTarget[anchorIndices.Length];
        _primaryIndex = 0;
        _targetByAnchor.Clear();
        _prevOfTarget.Clear();

        for (var ti = 0; ti < anchorIndices.Length; ti++)
        {
            var ai = anchorIndices[ti];
            if (ai == primaryAnchorIndex)
                _primaryIndex = ti;

            var prevIdx = (ai - 1 + count) % count;
            var nextIdx = (ai + 1) % count;

            _targets[ti] = new BevelTarget
            {
                AnchorIndex = ai,
                PrevIdx = prevIdx,
                NextIdx = nextIdx,
                OriginalPos = _saved[ai].Position,
                PrevPos = _saved[prevIdx].Position,
                NextPos = _saved[nextIdx].Position,
                PrevCurve = _saved[prevIdx].Curve,
                OrigCurve = _saved[ai].Curve,
                MaxDist = MathF.Min(
                    Vector2.Distance(_saved[ai].Position, _saved[prevIdx].Position),
                    Vector2.Distance(_saved[ai].Position, _saved[nextIdx].Position)),
                P1 = _saved[ai].Position,
                P2 = _saved[ai].Position,
            };

            _targetByAnchor[ai] = ti;
            _prevOfTarget[prevIdx] = ti;
        }
    }

    private void UpdateDrag()
    {
        if (Input.WasButtonReleased(InputCode.MouseLeft, Scope))
        {
            CommitDrag();
            return;
        }

        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);

        ref var primary = ref _targets![_primaryIndex];
        if (primary.MaxDist < 0.001f)
            return;

        var d1 = Vector2.Normalize(primary.PrevPos - primary.OriginalPos);
        var d2 = Vector2.Normalize(primary.NextPos - primary.OriginalPos);
        var dc = mouseLocal - primary.OriginalPos;
        var dcLen = dc.Length();

        float t;
        if (dcLen < 0.0001f)
        {
            t = 0f;
        }
        else
        {
            var cross_d1_d2 = d1.X * d2.Y - d1.Y * d2.X;
            var cross_d1_dc = d1.X * dc.Y - d1.Y * dc.X;
            var cross_d2_dc = d2.X * dc.Y - d2.Y * dc.X;

            var inside = (cross_d1_dc * cross_d1_d2 >= 0f) && (cross_d2_dc * -cross_d1_d2 >= 0f);
            t = inside ? Math.Clamp(dcLen / primary.MaxDist, 0f, 0.95f) : 0f;
        }

        // Restore anchors from snapshot
        var contour = _dragPath!.Contours[_dragContourIndex];
        contour.Anchors.Clear();
        for (var i = 0; i < _saved!.Length; i++)
            contour.Anchors.Add(_saved[i]);

        if (t < 0.001f)
        {
            for (var ti = 0; ti < _targets.Length; ti++)
            {
                _targets[ti].P1 = _targets[ti].OriginalPos;
                _targets[ti].P2 = _targets[ti].OriginalPos;
            }
            FinishPathUpdate();
            return;
        }

        var ctrlDown = Input.IsCtrlDown(Scope);

        for (var ti = 0; ti < _targets.Length; ti++)
        {
            var clampedT = Math.Clamp(t * primary.MaxDist / MathF.Max(_targets[ti].MaxDist, 0.001f), 0f, 0.95f);
            ComputeBevel(ref _targets[ti], clampedT, ctrlDown);
        }

        // Rebuild anchor list with all bevels applied
        contour.Anchors.Clear();
        for (var i = 0; i < _saved.Length; i++)
        {
            if (_targetByAnchor.TryGetValue(i, out var ti))
            {
                ref var target = ref _targets[ti];
                contour.Anchors.Add(new SpritePathAnchor
                {
                    Position = target.P1,
                    Curve = SpritePath.ClampCurve(target.BevelCurve),
                    Flags = SpritePathAnchorFlags.Selected,
                });
                contour.Anchors.Add(new SpritePathAnchor
                {
                    Position = target.P2,
                    Curve = SpritePath.ClampCurve(target.RightCurve),
                    Flags = SpritePathAnchorFlags.Selected,
                });
            }
            else if (_prevOfTarget.TryGetValue(i, out var prevTi) && !_targetByAnchor.ContainsKey(i))
            {
                var a = _saved[i];
                a.Curve = SpritePath.ClampCurve(_targets[prevTi].LeftCurve);
                contour.Anchors.Add(a);
            }
            else
            {
                contour.Anchors.Add(_saved[i]);
            }
        }

        FinishPathUpdate();
    }

    private void FinishPathUpdate()
    {
        _dragPath!.MarkDirty();
        _dragPath.UpdateSamples();
        _dragPath.UpdateBounds();
        _dragPath.PathTranslation = _savedTranslation;
        _dragPath.CompensateTranslation(_savedBoundsCenter);
        _editor.MarkDirty();
    }

    private void CommitDrag()
    {
        FinishPathUpdate();
        Input.ConsumeButton(InputCode.MouseLeft);
        _dragging = false;
        _dragPath = null;
        _saved = null;
        _targets = null;
        _targetByAnchor.Clear();
        _prevOfTarget.Clear();
    }

    private void CancelDrag()
    {
        // Restore original anchors
        var contour = _dragPath!.Contours[_dragContourIndex];
        contour.Anchors.Clear();
        for (var i = 0; i < _saved!.Length; i++)
            contour.Anchors.Add(_saved[i]);

        FinishPathUpdate();
        Undo.Cancel();

        _dragging = false;
        _dragPath = null;
        _saved = null;
        _targets = null;
        _targetByAnchor.Clear();
        _prevOfTarget.Clear();
    }

    private static void ComputeBevel(ref BevelTarget target, float t, bool ctrlDown)
    {
        if (MathF.Abs(target.PrevCurve) < SpritePath.MinCurve)
        {
            target.P1 = Vector2.Lerp(target.OriginalPos, target.PrevPos, t);
            target.LeftCurve = 0f;
        }
        else
        {
            var splitT = 1f - t;
            var mid = (target.PrevPos + target.OriginalPos) * 0.5f;
            var dir = target.OriginalPos - target.PrevPos;
            var perp = new Vector2(-dir.Y, dir.X);
            var len = perp.Length();
            if (len > 0) perp /= len;
            var control = mid + perp * target.PrevCurve;

            var u = 1f - splitT;
            target.P1 = u * u * target.PrevPos + 2 * u * splitT * control + splitT * splitT * target.OriginalPos;

            var cLeft = Vector2.Lerp(target.PrevPos, control, splitT);
            target.LeftCurve = SpritePath.ProjectCurve(target.PrevPos, target.P1, cLeft);
        }

        if (MathF.Abs(target.OrigCurve) < SpritePath.MinCurve)
        {
            target.P2 = Vector2.Lerp(target.OriginalPos, target.NextPos, t);
            target.RightCurve = 0f;
        }
        else
        {
            var mid = (target.OriginalPos + target.NextPos) * 0.5f;
            var dir = target.NextPos - target.OriginalPos;
            var perp = new Vector2(-dir.Y, dir.X);
            var len = perp.Length();
            if (len > 0) perp /= len;
            var control = mid + perp * target.OrigCurve;

            var u = 1f - t;
            target.P2 = u * u * target.OriginalPos + 2 * u * t * control + t * t * target.NextPos;

            var cRight = Vector2.Lerp(control, target.NextPos, t);
            target.RightCurve = SpritePath.ProjectCurve(target.P2, target.NextPos, cRight);
        }

        if (ctrlDown)
            target.BevelCurve = SpritePath.ProjectCurve(target.P1, target.P2, target.OriginalPos);
        else
            target.BevelCurve = 0f;
    }

    public override void Draw()
    {
        if (!_dragging || _dragPath == null || _targets == null)
        {
            // Idle: draw hover indicator
            if (_hoveringAnchor)
            {
                using var _ = Gizmos.PushState(EditorLayer.Tool);
                Graphics.SetTransform(_editor.Document.Transform);
                Gizmos.SetColor(EditorStyle.Palette.Primary);
                Gizmos.DrawRect(_hoverAnchorPos, Gizmos.GetVertexSize() * 1.3f);
            }
            return;
        }

        using var __ = Gizmos.PushState(EditorLayer.Tool);
        Graphics.SetTransform(_transform);

        var contour = _dragPath.Contours[_dragContourIndex];
        var anchors = contour.Anchors;

        for (var ti = 0; ti < _targets.Length; ti++)
        {
            ref var target = ref _targets[ti];

            var p1Idx = target.AnchorIndex + CountTargetsBefore(target.AnchorIndex);
            var p2Idx = p1Idx + 1;
            var prevIdx = target.PrevIdx + CountTargetsBefore(target.PrevIdx);

            Gizmos.SetColor(EditorStyle.Palette.Primary);

            if (prevIdx < anchors.Count)
                DrawSegment(contour, prevIdx);
            if (p1Idx < anchors.Count)
                DrawSegment(contour, p1Idx);
            if (p2Idx < anchors.Count)
                DrawSegment(contour, p2Idx);

            Gizmos.SetColor(EditorStyle.Workspace.SelectionColor.WithAlpha(0.5f));
            Gizmos.DrawDashedLine(target.P1, target.OriginalPos);
            Gizmos.DrawDashedLine(target.OriginalPos, target.P2);
        }
    }

    private int CountTargetsBefore(int savedIndex)
    {
        var count = 0;
        for (var ti = 0; ti < _targets!.Length; ti++)
            if (_targets[ti].AnchorIndex < savedIndex)
                count++;
        return count;
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
        if (_dragging)
            CancelDrag();
    }
}
