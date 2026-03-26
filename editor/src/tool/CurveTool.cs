//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class CurveTool : Tool
{
    private readonly SpriteEditor _editor;
    private readonly SpritePath _path;
    private readonly int _contourIndex;
    private readonly SpritePathAnchor[] _saved;
    private readonly Matrix3x2 _transform;
    private readonly Matrix3x2 _invTransform;
    private readonly List<int> _selectedSegments = [];
    private Vector2[] _offsets = [];  // offset from mouse to control point at drag start

    private readonly Vector2 _savedBoundsCenter;
    private readonly Vector2 _savedTranslation;

    public bool CommitOnRelease { get; set; }

    public CurveTool(SpriteEditor editor, SpritePath path, Matrix3x2 transform, SpritePathAnchor[] saved, int contourIndex = 0)
    {
        _editor = editor;
        _path = path;
        _contourIndex = contourIndex;
        _transform = transform;
        _saved = saved;
        _savedBoundsCenter = path.LocalBounds.Center;
        _savedTranslation = path.PathTranslation;

        Matrix3x2.Invert(transform, out _invTransform);

        var contour = path.Contours[contourIndex];
        for (var i = 0; i < contour.Anchors.Count; i++)
        {
            if (path.IsSegmentSelected(contourIndex, i))
                _selectedSegments.Add(i);
        }
    }

    public CurveTool(SpriteEditor editor, SpritePath path, Matrix3x2 transform, SpritePathAnchor[] saved, int segmentIndex, int contourIndex = 0)
    {
        _editor = editor;
        _path = path;
        _contourIndex = contourIndex;
        _transform = transform;
        _saved = saved;
        _savedBoundsCenter = path.LocalBounds.Center;
        _savedTranslation = path.PathTranslation;
        Matrix3x2.Invert(transform, out _invTransform);
        _selectedSegments.Add(segmentIndex);
    }

    public override void Begin()
    {
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
        var anchors = _path.Contours[_contourIndex].Anchors;

        _offsets = new Vector2[_selectedSegments.Count];
        for (var i = 0; i < _selectedSegments.Count; i++)
        {
            var segIdx = _selectedSegments[i];
            var a0 = anchors[segIdx];
            var a1 = anchors[(segIdx + 1) % anchors.Count];
            var mid = (a0.Position + a1.Position) * 0.5f;
            var dir = a1.Position - a0.Position;
            var len = dir.Length();
            if (len < 0.0001f) continue;
            var perp = new Vector2(-dir.Y, dir.X) / len;
            var controlPoint = mid + perp * a0.Curve;
            _offsets[i] = controlPoint - mouseLocal;
        }
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape) || Input.WasButtonPressed(InputCode.MouseRight))
        {
            Workspace.CancelTool();
            return;
        }

        var commitInput = CommitOnRelease
            ? Input.WasButtonReleased(InputCode.MouseLeft, Scope)
            : Input.WasButtonPressed(InputCode.MouseLeft) || Input.WasButtonPressed(InputCode.KeyEnter);

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
        var anchors = _path.Contours[_contourIndex].Anchors;

        for (var i = 0; i < _selectedSegments.Count; i++)
        {
            var segIdx = _selectedSegments[i];
            var a0 = anchors[segIdx];
            var a1 = anchors[(segIdx + 1) % anchors.Count];
            var dir = a1.Position - a0.Position;
            var len = dir.Length();
            if (len < 0.0001f) continue;

            var perp = new Vector2(-dir.Y, dir.X) / len;
            var mid = (a0.Position + a1.Position) * 0.5f;
            var desiredControlPoint = mouseLocal + _offsets[i];
            var newCurve = Vector2.Dot(desiredControlPoint - mid, perp);

            if (Input.IsCtrlDown())
                newCurve = SnapCurve(newCurve, len);

            _path.SetAnchorCurve(_contourIndex, segIdx, newCurve);
        }

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

        // Draw the curved segment in the selected color
        var localTransform = _path.HasTransform ? _path.PathTransform : Matrix3x2.Identity;
        Gizmos.SetColor(EditorStyle.Palette.Primary);
        foreach (var segIdx in _selectedSegments)
        {
            var samples = contour.GetSegmentSamples(segIdx);
            var prev = Vector2.Transform(anchors[segIdx].Position, localTransform);
            foreach (var sample in samples)
            {
                var transformed = Vector2.Transform(sample, localTransform);
                Gizmos.DrawLine(prev, transformed, EditorStyle.SpritePath.SegmentLineWidth, order: 2);
                prev = transformed;
            }
            var nextIdx = (segIdx + 1) % anchors.Count;
            Gizmos.DrawLine(prev, Vector2.Transform(anchors[nextIdx].Position, localTransform), EditorStyle.SpritePath.SegmentLineWidth, order: 2);
        }

        // Draw dashed chord lines
        Gizmos.SetColor(EditorStyle.Workspace.SelectionColor.WithAlpha(0.5f));
        foreach (var segIdx in _selectedSegments)
        {
            var a0 = anchors[segIdx];
            var a1 = anchors[(segIdx + 1) % anchors.Count];
            Gizmos.DrawDashedLine(a0.Position, a1.Position);
        }
    }

    public override void Cancel()
    {
        Undo.Cancel();
    }

    private static float SnapCurve(float curve, float segmentLength)
    {
        var threshold = segmentLength * 0.1f;

        if (MathF.Abs(curve) < threshold)
            return 0f;

        var circleOffset = segmentLength * 0.5f * 0.5522847498f;

        if (MathF.Abs(curve - circleOffset) < threshold)
            return circleOffset;

        if (MathF.Abs(curve + circleOffset) < threshold)
            return -circleOffset;

        return curve;
    }
}
