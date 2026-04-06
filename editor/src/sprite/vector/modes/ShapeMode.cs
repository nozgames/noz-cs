//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public enum ShapeType
{
    Rectangle,
    Circle
}

public class ShapeMode : EditorMode<VectorSpriteEditor>
{
    private readonly ShapeType _shapeType;

    private Vector2 _startLocal;
    private Vector2 _currentLocal;
    private bool _isDragging;

    public ShapeMode(ShapeType shapeType)
    {
        _shapeType = shapeType;
    }

    public override void Update()
    {
        EditorCursor.SetCrosshair();

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
        {
            Editor.SetMode(new AnchorMode());
            return;
        }

        Matrix3x2.Invert(Editor.Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        if (Input.IsCtrlDown(InputScope.All))
            mouseLocal = Grid.SnapToPixelGrid(mouseLocal);

        if (!_isDragging && Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
        {
            _startLocal = mouseLocal;
            _currentLocal = mouseLocal;
            _isDragging = true;
            return;
        }

        if (_isDragging)
        {
            _currentLocal = mouseLocal;

            if (Input.IsShiftDown(InputScope.All))
                _currentLocal = ConstrainToUniform(_startLocal, _currentLocal);

            if (Input.WasButtonReleased(InputCode.MouseLeft, InputScope.All))
            {
                Commit();
                return;
            }
        }
    }

    private static Vector2 ConstrainToUniform(Vector2 start, Vector2 current)
    {
        var delta = current - start;
        var size = MathF.Max(MathF.Abs(delta.X), MathF.Abs(delta.Y));
        return new Vector2(
            start.X + MathF.Sign(delta.X) * size,
            start.Y + MathF.Sign(delta.Y) * size);
    }

    public override void Draw()
    {
        if (!_isDragging) return;

        var min = Vector2.Min(_startLocal, _currentLocal);
        var max = Vector2.Max(_startLocal, _currentLocal);
        if (max.X - min.X < 0.001f || max.Y - min.Y < 0.001f) return;

        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(Editor.Document.Transform);
            var lineWidth = Gizmos.GetLineWidth();
            Gizmos.SetColor(EditorStyle.Tool.LineColor);

            if (_shapeType == ShapeType.Rectangle)
                DrawRectanglePreview(min, max, lineWidth);
            else
                DrawCirclePreview(min, max, lineWidth);

            DrawDimensionText(min, max);
        }
    }

    private void DrawDimensionText(Vector2 min, Vector2 max)
    {
        var dpi = EditorApplication.Config.PixelsPerUnit;
        var widthPx = (int)MathF.Round((max.X - min.X) * dpi);
        var heightPx = (int)MathF.Round((max.Y - min.Y) * dpi);
        var text = $"{widthPx} x {heightPx}";

        var font = EditorAssets.Fonts.Seguisb;
        var fontSize = Gizmos.ZoomRefScale * 0.15f;
        var textSize = TextRender.Measure(text, font, fontSize);

        var centerX = (min.X + max.X) * 0.5f;
        var textX = centerX - textSize.X * 0.5f;
        var textY = max.Y + Gizmos.ZoomRefScale * 0.1f;

        Graphics.SetColor(EditorStyle.Tool.LineColor);
        Graphics.SetTransform(Matrix3x2.CreateTranslation(textX, textY) * Editor.Document.Transform);
        TextRender.Draw(text, font, fontSize);
    }

    private static void DrawRectanglePreview(Vector2 min, Vector2 max, float lineWidth)
    {
        var tl = new Vector2(min.X, min.Y);
        var tr = new Vector2(max.X, min.Y);
        var br = new Vector2(max.X, max.Y);
        var bl = new Vector2(min.X, max.Y);
        Gizmos.DrawLine(tl, tr, lineWidth);
        Gizmos.DrawLine(tr, br, lineWidth);
        Gizmos.DrawLine(br, bl, lineWidth);
        Gizmos.DrawLine(bl, tl, lineWidth);
    }

    private static void DrawCirclePreview(Vector2 min, Vector2 max, float lineWidth)
    {
        var center = (min + max) * 0.5f;
        var radiusX = (max.X - min.X) * 0.5f;
        var radiusY = (max.Y - min.Y) * 0.5f;

        const int segments = 32;
        var prev = center + new Vector2(radiusX, 0);

        for (var i = 1; i <= segments; i++)
        {
            var angle = i * MathF.PI * 2f / segments;
            var point = center + new Vector2(MathF.Cos(angle) * radiusX, MathF.Sin(angle) * radiusY);
            Gizmos.DrawLine(prev, point, lineWidth);
            prev = point;
        }
    }

    private void Commit()
    {
        var min = Vector2.Min(_startLocal, _currentLocal);
        var max = Vector2.Max(_startLocal, _currentLocal);
        var activeLayer = Editor.Document.RootLayer;

        if (max.X - min.X < 0.01f || max.Y - min.Y < 0.01f || activeLayer.Locked)
        {
            _isDragging = false;
            return;
        }

        Undo.Record(Editor.Document);
        activeLayer.ForEachEditablePath(p => p.ClearSelection());

        var path = new SpritePath
        {
            FillColor = Editor.Document.CurrentFillColor,
            Operation = Editor.Document.CurrentOperation
        };

        if (_shapeType == ShapeType.Rectangle)
            AddRectangleAnchors(path, min, max);
        else
            AddCircleAnchors(path, min, max);

        path.SelectAll();
        path.UpdateSamples();
        path.UpdateBounds();
        activeLayer.Insert(0, path);

        Editor.MarkDirty();
        _isDragging = false;
        Input.ConsumeButton(InputCode.MouseLeft);
    }

    private static void AddRectangleAnchors(SpritePath path, Vector2 min, Vector2 max)
    {
        path.AddAnchor(new Vector2(min.X, min.Y));
        path.AddAnchor(new Vector2(min.X, max.Y));
        path.AddAnchor(new Vector2(max.X, max.Y));
        path.AddAnchor(new Vector2(max.X, min.Y));
    }

    private static void AddCircleAnchors(SpritePath path, Vector2 min, Vector2 max)
    {
        var center = (min + max) * 0.5f;
        var rx = (max.X - min.X) * 0.5f;
        var ry = (max.Y - min.Y) * 0.5f;

        const float cos45 = 0.7071067812f;
        Span<Vector2> anchors =
        [
            center + new Vector2(rx, 0),
            center + new Vector2(rx * cos45, ry * cos45),
            center + new Vector2(0, ry),
            center + new Vector2(-rx * cos45, ry * cos45),
            center + new Vector2(-rx, 0),
            center + new Vector2(-rx * cos45, -ry * cos45),
            center + new Vector2(0, -ry),
            center + new Vector2(rx * cos45, -ry * cos45),
        ];

        for (var i = 0; i < 8; i++)
        {
            var p0 = anchors[i];
            var p1 = anchors[(i + 1) % 8];

            var midAngle = (i + 0.5f) * MathF.PI * 2f / 8f;
            var arcMid = center + new Vector2(MathF.Cos(midAngle) * rx, MathF.Sin(midAngle) * ry);
            var curve = CalculateSegmentCurve(p0, p1, arcMid);
            path.AddAnchor(p0, curve);
        }
    }

    private static float CalculateSegmentCurve(Vector2 p0, Vector2 p1, Vector2 arcMidpoint)
    {
        var chordMid = (p0 + p1) * 0.5f;
        var dir = p1 - p0;
        var len = dir.Length();
        if (len < 0.0001f) return 0f;
        var perp = new Vector2(-dir.Y, dir.X) / len;
        return 2f * Vector2.Dot(arcMidpoint - chordMid, perp);
    }
}
