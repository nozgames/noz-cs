//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class BevelMode : AnchorBasedMode
{
    private const float MaxRatio = 0.95f;

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

    private SpritePath? _path;
    private int _contourIndex;
    private SpritePathAnchor[]? _saved;
    private Matrix3x2 _transform;
    private Matrix3x2 _invTransform;
    private Vector2 _savedBoundsCenter;
    private Vector2 _savedTranslation;
    private BevelTarget[]? _targets;
    private int _primaryIndex;
    private readonly Dictionary<int, int> _targetByAnchor = new();
    private readonly Dictionary<int, int> _prevOfTarget = new();

    protected override bool OnAnchorDragStart(SpritePath path, int contourIndex, int anchorIndex)
    {
        var contour = path.Contours[contourIndex];
        if (contour.Anchors.Count < 3) return false;
        if (contour.Open && (anchorIndex == 0 || anchorIndex == contour.Anchors.Count - 1)) return false;

        // Collect all selected valid anchors in this contour
        var anchorIndices = new List<int>();
        for (var i = 0; i < contour.Anchors.Count; i++)
        {
            if (!contour.Anchors[i].IsSelected) continue;
            if (contour.Open && (i == 0 || i == contour.Anchors.Count - 1)) continue;
            anchorIndices.Add(i);
        }

        if (anchorIndices.Count == 0) return false;

        Undo.Record(Editor.Document);

        _path = path;
        _contourIndex = contourIndex;
        _saved = path.SnapshotAnchors(contourIndex);
        _transform = path.HasTransform ? path.PathTransform * Editor.Document.Transform : Editor.Document.Transform;
        _savedBoundsCenter = path.LocalBounds.Center;
        _savedTranslation = path.PathTranslation;

        Matrix3x2.Invert(_transform, out _invTransform);

        var count = _saved.Length;
        _targets = new BevelTarget[anchorIndices.Count];
        _primaryIndex = 0;
        _targetByAnchor.Clear();
        _prevOfTarget.Clear();

        for (var ti = 0; ti < anchorIndices.Count; ti++)
        {
            var ai = anchorIndices[ti];
            if (ai == anchorIndex)
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

        return true;
    }

    protected override void OnDragUpdate()
    {
        if (_path == null || _targets == null || _saved == null) return;

        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);

        ref var primary = ref _targets[_primaryIndex];
        if (primary.MaxDist < 0.001f) return;

        var d1 = Vector2.Normalize(primary.PrevPos - primary.OriginalPos);
        var d2 = Vector2.Normalize(primary.NextPos - primary.OriginalPos);
        var dc = mouseLocal - primary.OriginalPos;
        var dcLen = dc.Length();

        float worldDist;
        if (dcLen < 0.0001f)
        {
            worldDist = 0f;
        }
        else
        {
            var cross_d1_d2 = d1.X * d2.Y - d1.Y * d2.X;
            var cross_d1_dc = d1.X * dc.Y - d1.Y * dc.X;
            var cross_d2_dc = d2.X * dc.Y - d2.Y * dc.X;

            var inside = (cross_d1_dc * cross_d1_d2 >= 0f) && (cross_d2_dc * -cross_d1_d2 >= 0f);
            worldDist = inside ? MathF.Min(dcLen, primary.MaxDist * MaxRatio) : 0f;
        }

        // Restore anchors from snapshot
        var contour = _path.Contours[_contourIndex];
        contour.Anchors.Clear();
        for (var i = 0; i < _saved.Length; i++)
            contour.Anchors.Add(_saved[i]);

        if (worldDist < 0.001f)
        {
            for (var ti = 0; ti < _targets.Length; ti++)
            {
                _targets[ti].P1 = _targets[ti].OriginalPos;
                _targets[ti].P2 = _targets[ti].OriginalPos;
            }
            FinishPathUpdate();
            return;
        }

        var ctrlDown = Input.IsCtrlDown(InputScope.All);

        // Compute bevel for each target using the same world distance, clamped per-target
        for (var ti = 0; ti < _targets.Length; ti++)
        {
            var targetDist = MathF.Min(worldDist, _targets[ti].MaxDist * MaxRatio);
            ComputeBevel(ref _targets[ti], targetDist, ctrlDown);
        }

        // Rebuild anchor list with all bevels
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

    protected override void OnDragCommit()
    {
        FinishPathUpdate();
        ClearDragState();
    }

    protected override void OnDragCancel()
    {
        if (_path != null && _saved != null)
        {
            var contour = _path.Contours[_contourIndex];
            contour.Anchors.Clear();
            foreach (var a in _saved)
                contour.Anchors.Add(a);
            FinishPathUpdate();
        }
        Undo.Cancel();
        ClearDragState();
    }

    public override void Draw()
    {
        base.Draw();
        if (!IsDragging || _path == null || _targets == null) return;

        using var _ = Gizmos.PushState(EditorLayer.Tool);
        Graphics.SetTransform(_transform);

        var contour = _path.Contours[_contourIndex];
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

    private void FinishPathUpdate()
    {
        if (_path == null) return;
        _path.MarkDirty();
        _path.UpdateSamples();
        _path.UpdateBounds();
        _path.PathTranslation = _savedTranslation;
        _path.CompensateTranslation(_savedBoundsCenter);
        Editor.MarkDirty();
    }

    private void ClearDragState()
    {
        _path = null;
        _saved = null;
        _targets = null;
        _targetByAnchor.Clear();
        _prevOfTarget.Clear();
    }

    private int CountTargetsBefore(int savedIndex)
    {
        var count = 0;
        for (var ti = 0; ti < _targets!.Length; ti++)
            if (_targets[ti].AnchorIndex < savedIndex)
                count++;
        return count;
    }

    private static void DrawSegment(SpriteContour contour, int segIdx)
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

    private static void ComputeBevel(ref BevelTarget target, float worldDist, bool ctrlDown)
    {
        var prevLen = Vector2.Distance(target.OriginalPos, target.PrevPos);
        var nextLen = Vector2.Distance(target.OriginalPos, target.NextPos);

        var tPrev = prevLen > 0.0001f ? Math.Clamp(worldDist / prevLen, 0f, MaxRatio) : 0f;
        var tNext = nextLen > 0.0001f ? Math.Clamp(worldDist / nextLen, 0f, MaxRatio) : 0f;

        if (MathF.Abs(target.PrevCurve) < SpritePath.MinCurve)
        {
            target.P1 = Vector2.Lerp(target.OriginalPos, target.PrevPos, tPrev);
            target.LeftCurve = 0f;
        }
        else
        {
            var splitT = 1f - tPrev;
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
            target.P2 = Vector2.Lerp(target.OriginalPos, target.NextPos, tNext);
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

            var u = 1f - tNext;
            target.P2 = u * u * target.OriginalPos + 2 * u * tNext * control + tNext * tNext * target.NextPos;

            var cRight = Vector2.Lerp(control, target.NextPos, tNext);
            target.RightCurve = SpritePath.ProjectCurve(target.P2, target.NextPos, cRight);
        }

        if (ctrlDown)
            target.BevelCurve = SpritePath.ProjectCurve(target.P1, target.P2, target.OriginalPos);
        else
            target.BevelCurve = 0f;
    }
}
