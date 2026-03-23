//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class CurveTool : Tool
{
    private readonly SpritePath _path;
    private readonly SpritePathAnchor[] _saved;
    private readonly Matrix3x2 _transform;
    private readonly Matrix3x2 _invTransform;
    private readonly List<int> _selectedSegments = [];

    private Vector2 _startWorld;

    public CurveTool(SpritePath path, Matrix3x2 transform, SpritePathAnchor[] saved)
    {
        _path = path;
        _transform = transform;
        _saved = saved;

        Matrix3x2.Invert(transform, out _invTransform);

        for (var i = 0; i < path.Anchors.Count; i++)
        {
            if (path.IsSegmentSelected(i))
                _selectedSegments.Add(i);
        }
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

        if (Input.WasButtonPressed(InputCode.MouseLeft) || Input.WasButtonPressed(InputCode.KeyEnter))
        {
            _path.UpdateSamples();
            _path.UpdateBounds();
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
        var startLocal = Vector2.Transform(_startWorld, _invTransform);
        var delta = mouseLocal - startLocal;

        foreach (var segIdx in _selectedSegments)
        {
            var a0 = _path.Anchors[segIdx];
            var a1 = _path.Anchors[(segIdx + 1) % _path.Anchors.Count];

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

            _path.SetAnchorCurve(segIdx, newCurve);
        }

        _path.UpdateSamples();
        _path.UpdateBounds();
    }

    public override void Draw()
    {
        using var _ = Gizmos.PushState(EditorLayer.Tool);

        Graphics.SetTransform(_transform);

        Gizmos.SetColor(EditorStyle.Workspace.SelectionColor.WithAlpha(0.5f));
        foreach (var segIdx in _selectedSegments)
        {
            var a0 = _path.Anchors[segIdx];
            var a1 = _path.Anchors[(segIdx + 1) % _path.Anchors.Count];
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
