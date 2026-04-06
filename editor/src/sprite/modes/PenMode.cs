//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PenMode : EditorMode<SpriteEditor>
{
    private const int MaxPoints = 64;

    private struct PenPoint
    {
        public Vector2 Position;
        public bool IsExistingAnchor;
    }

    private readonly PenPoint[] _points = new PenPoint[MaxPoints];
    private int _pointCount;

    private bool _hoveringFirstPoint;
    private bool _hoveringExistingAnchor;
    private Vector2 _hoverSnapPosition;
    private bool _hoveringSegment;
    private bool _snappingToGrid;
    private Vector2 _gridSnapPosition;

    public override void Update()
    {
        EditorCursor.SetCrosshair();
        var mouseWorld = Workspace.MouseWorldPosition;
        Matrix3x2.Invert(Editor.Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(mouseWorld, invTransform);

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
        {
            Cancel();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter, InputScope.All))
        {
            if (_pointCount >= 3)
                Commit();
            return;
        }

        if (Input.WasButtonReleased(InputCode.MouseRight, InputScope.All) && !Workspace.WasDragging)
        {
            if (_pointCount > 0)
                _pointCount--;
            return;
        }

        UpdateHover(mouseLocal);

        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
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
            var anchorHit = Editor.Document.RootLayer.HitTestAnchor(mouseLocal);
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
                var segHit = Editor.Document.RootLayer.HitTestSegment(mouseLocal);
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
        if (!_hoveringFirstPoint && !_hoveringExistingAnchor && !_hoveringSegment && Input.IsCtrlDown(InputScope.All))
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
            Graphics.SetTransform(Editor.Document.Transform);

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

        Matrix3x2.Invert(Editor.Document.Transform, out var invTransform);
        return Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
    }

    private void Commit()
    {
        var activeLayer = Editor.Document.RootLayer;
        if (_pointCount < 3 || activeLayer.Locked)
        {
            _pointCount = 0;
            return;
        }

        Undo.Record(Editor.Document);

        var signedArea = 0f;
        for (var i = 0; i < _pointCount; i++)
        {
            var v0 = _points[i].Position;
            var v1 = _points[(i + 1) % _pointCount].Position;
            signedArea += (v1.X - v0.X) * (v1.Y + v0.Y);
        }

        var path = new SpritePath
        {
            FillColor = Editor.Document.CurrentFillColor,
            Operation = Editor.Document.CurrentOperation
        };

        if (signedArea > 0)
            for (var i = _pointCount - 1; i >= 0; i--)
                path.AddAnchor(_points[i].Position);
        else
            for (var i = 0; i < _pointCount; i++)
                path.AddAnchor(_points[i].Position);

        path.UpdateSamples();
        path.UpdateBounds();
        activeLayer.Insert(0, path);

        Editor.MarkDirty();
        _pointCount = 0;
        Input.ConsumeButton(InputCode.MouseLeft);
    }

    private void Cancel()
    {
        _pointCount = 0;
        Editor.SetMode(new AnchorMode());
    }
}
