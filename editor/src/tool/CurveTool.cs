//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class CurveTool : Tool
{
    private readonly SpriteDocument _document;
    private readonly SpritePath _path;
    private readonly int _contourIndex;
    private readonly SpritePathAnchor[] _saved;
    private readonly Matrix3x2 _transform;
    private readonly Matrix3x2 _invTransform;
    private readonly List<int> _selectedSegments = [];

    private readonly Vector2 _savedBoundsCenter;
    private readonly Vector2 _savedTranslation;

    private Vector2 _startWorld;

    public bool CommitOnRelease { get; set; }

    public CurveTool(SpriteDocument document, SpritePath path, Matrix3x2 transform, SpritePathAnchor[] saved, int contourIndex = 0)
    {
        _document = document;
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

    public CurveTool(SpriteDocument document, SpritePath path, Matrix3x2 transform, SpritePathAnchor[] saved, int segmentIndex, int contourIndex = 0)
    {
        _document = document;
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
        _startWorld = Workspace.MouseWorldPosition;
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
            _document.IncrementVersion();
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
        var startLocal = Vector2.Transform(_startWorld, _invTransform);
        var delta = mouseLocal - startLocal;

        var anchors = _path.Contours[_contourIndex].Anchors;
        foreach (var segIdx in _selectedSegments)
        {
            var a0 = anchors[segIdx];
            var a1 = anchors[(segIdx + 1) % anchors.Count];

            var p0 = a0.Position;
            var p1 = a1.Position;
            var dir = p1 - p0;
            var len = dir.Length();

            if (len < 0.0001f)
                continue;

            var perp = new Vector2(-dir.Y, dir.X) / len;
            var curveDelta = Vector2.Dot(delta, perp);

            var newCurve = _saved[segIdx].Curve + curveDelta;

            if (Input.IsCtrlDown())
                newCurve = SnapCurve(newCurve, len);

            _path.SetAnchorCurve(_contourIndex, segIdx, newCurve);
        }

        _path.UpdateSamples();
        _path.UpdateBounds();
        _path.PathTranslation = _savedTranslation;
        _path.CompensateTranslation(_savedBoundsCenter);
        _document.IncrementVersion();
    }

    public override void Draw()
    {
        using var _ = Gizmos.PushState(EditorLayer.Tool);

        Graphics.SetTransform(_transform);

        var anchors = _path.Contours[_contourIndex].Anchors;
        Gizmos.SetColor(EditorStyle.Workspace.SelectionColor.WithAlpha(0.5f));
        foreach (var segIdx in _selectedSegments)
        {
            var a0 = anchors[segIdx];
            var a1 = anchors[(segIdx + 1) % anchors.Count];
            Gizmos.DrawDashedLine(a0.Position, a1.Position);
        }

        Graphics.SetTransform(Matrix3x2.Identity);

        Gizmos.SetColor(EditorStyle.Tool.PointColor);
        Gizmos.DrawCircle(_startWorld, EditorStyle.Tool.PointSize, order: 10);

        Gizmos.SetColor(EditorStyle.Tool.LineColor);
        Gizmos.DrawDashedLine(_startWorld, Workspace.MouseWorldPosition);
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
