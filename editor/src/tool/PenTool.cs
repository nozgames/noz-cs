//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PenTool : Tool
{
    private const int MaxPoints = 64;

    private struct PenPoint
    {
        public Vector2 Position;
        public bool IsExistingAnchor;
    }

    private readonly SpriteEditor _editor;
    private readonly SpriteLayer _rootLayer;
    private readonly SpriteLayer _activeLayer;
    private readonly Color32 _fillColor;
    private readonly SpritePathOperation _operation;
    private readonly PenPoint[] _points = new PenPoint[MaxPoints];
    private int _pointCount;

    private bool _hoveringFirstPoint;
    private bool _hoveringExistingAnchor;
    private Vector2 _hoverSnapPosition;
    private bool _hoveringSegment;
    private bool _snappingToGrid;
    private Vector2 _gridSnapPosition;

    public PenTool(SpriteEditor editor, SpriteLayer rootLayer, SpriteLayer activeLayer,
        Color32 fillColor, SpritePathOperation operation = SpritePathOperation.Normal)
    {
        _editor = editor;
        _rootLayer = rootLayer;
        _activeLayer = activeLayer;
        _fillColor = fillColor;
        _operation = operation;
    }

    public override void Begin()
    {
        base.Begin();
    }

    public override void Update()
    {
        EditorCursor.SetCrosshair();
        var mouseWorld = Workspace.MouseWorldPosition;
        Matrix3x2.Invert(_editor.Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(mouseWorld, invTransform);

        if (Input.WasButtonPressed(InputCode.KeyEscape, Scope))
        {
            Cancel();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter, Scope))
        {
            if (_pointCount >= 3)
                Commit();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseRight, Scope))
        {
            if (_pointCount > 0)
                _pointCount--;
            return;
        }

        UpdateHover(mouseLocal);

        if (Input.WasButtonPressed(InputCode.MouseLeft, Scope))
            HandleLeftClick(mouseLocal);
    }

    private void UpdateHover(Vector2 mouseLocal)
    {
        _hoveringFirstPoint = false;
        if (_pointCount >= 3)
        {
            var anchorHitRadius = EditorStyle.SpritePath.AnchorHitRadius;
            _hoveringFirstPoint = Vector2.DistanceSquared(mouseLocal, _points[0].Position) < anchorHitRadius * anchorHitRadius;
        }

        _hoveringExistingAnchor = false;
        _hoveringSegment = false;
        if (!_hoveringFirstPoint)
        {
            var anchorHit = _rootLayer.HitTestAnchor(mouseLocal);
            if (anchorHit.HasValue)
            {
                _hoveringExistingAnchor = true;
                var pos = anchorHit.Value.Position;
                _hoverSnapPosition = anchorHit.Value.Path.HasTransform
                    ? Vector2.Transform(pos, anchorHit.Value.Path.PathTransform)
                    : pos;
            }
            else
            {
                var segHit = _rootLayer.HitTestSegment(mouseLocal);
                if (segHit.HasValue)
                {
                    _hoveringSegment = true;
                    var pos = segHit.Value.Position;
                    _hoverSnapPosition = segHit.Value.Path.HasTransform
                        ? Vector2.Transform(pos, segHit.Value.Path.PathTransform)
                        : pos;
                }
            }
        }

        _snappingToGrid = false;
        if (!_hoveringFirstPoint && !_hoveringExistingAnchor && !_hoveringSegment && Input.IsCtrlDown(Scope))
        {
            _snappingToGrid = true;
            _gridSnapPosition = Grid.SnapToPixelGrid(mouseLocal);
        }
    }

    private void HandleLeftClick(Vector2 mouseLocal)
    {
        if (_hoveringFirstPoint)
        {
            Commit();
            return;
        }

        if (_pointCount > 0)
        {
            var lastPos = _points[_pointCount - 1].Position;
            if (Vector2.Distance(mouseLocal, lastPos) < 0.001f)
                return;
        }

        if (_pointCount >= MaxPoints)
            return;

        if (_hoveringExistingAnchor || _hoveringSegment)
        {
            _points[_pointCount++] = new PenPoint
            {
                Position = _hoverSnapPosition,
                IsExistingAnchor = _hoveringExistingAnchor
            };
            return;
        }

        var newPosition = _snappingToGrid ? _gridSnapPosition : mouseLocal;
        _points[_pointCount++] = new PenPoint { Position = newPosition };
    }

    public override void Draw()
    {
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(_editor.Document.Transform);

            var lineWidth = Gizmos.GetLineWidth();
            var vertexSize = Gizmos.GetVertexSize();

            Gizmos.SetColor(EditorStyle.Tool.LineColor);
            for (var i = 0; i < _pointCount - 1; i++)
                Gizmos.DrawLine(_points[i].Position, _points[i + 1].Position, lineWidth);

            if (_pointCount > 0)
            {
                var lastPoint = _points[_pointCount - 1].Position;
                var target = GetCurrentTarget();
                Gizmos.DrawLine(lastPoint, target, lineWidth);
            }

            if (_pointCount >= 3 && !_hoveringFirstPoint)
            {
                Gizmos.SetColor(EditorStyle.Workspace.SelectionColor.WithAlpha(0.3f));
                Gizmos.DrawDashedLine(_points[_pointCount - 1].Position, _points[0].Position);
            }

            Gizmos.SetColor(EditorStyle.Workspace.SelectionColor);
            for (var i = 0; i < _pointCount; i++)
                Gizmos.DrawRect(_points[i].Position, vertexSize);

            if (_hoveringFirstPoint)
            {
                Gizmos.SetColor(EditorStyle.Palette.Primary);
                Gizmos.DrawRect(_points[0].Position, vertexSize * 1.3f);
            }

            if ((_hoveringExistingAnchor || _hoveringSegment) && !_hoveringFirstPoint)
            {
                Gizmos.SetColor(EditorStyle.Palette.Primary);
                Gizmos.DrawRect(_hoverSnapPosition, vertexSize);
            }
        }
    }

    private Vector2 GetCurrentTarget()
    {
        if (_hoveringFirstPoint) return _points[0].Position;
        if (_hoveringExistingAnchor || _hoveringSegment) return _hoverSnapPosition;
        if (_snappingToGrid) return _gridSnapPosition;

        Matrix3x2.Invert(_editor.Document.Transform, out var invTransform);
        return Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
    }

    private void Commit()
    {
        if (_pointCount < 3 || _activeLayer.Locked)
        {
            Finish();
            return;
        }

        Undo.Record(_editor.Document);

        // Ensure consistent winding
        var signedArea = 0f;
        for (var i = 0; i < _pointCount; i++)
        {
            var v0 = _points[i].Position;
            var v1 = _points[(i + 1) % _pointCount].Position;
            signedArea += (v1.X - v0.X) * (v1.Y + v0.Y);
        }

        var path = new SpritePath { FillColor = _fillColor, Operation = _operation };

        if (signedArea > 0)
            for (var i = _pointCount - 1; i >= 0; i--)
                path.AddAnchor(_points[i].Position);
        else
            for (var i = 0; i < _pointCount; i++)
                path.AddAnchor(_points[i].Position);

        path.UpdateSamples();
        path.UpdateBounds();
        _activeLayer.Insert(0, path);

        _editor.MarkDirty();
        Finish();
    }

    private void Finish()
    {
        Workspace.EndTool();
        Input.ConsumeButton(InputCode.MouseLeft);
    }

    public override void Cancel() => Finish();
}
