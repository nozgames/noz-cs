//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Gradient handle overlay: drawing, hit testing, and drag interaction
//  for linear gradient endpoints on selected paths.
//

using System.Numerics;

namespace NoZ.Editor;

public partial class SpriteEditor
{
    internal bool IsGradientOverlayVisible()
    {
        if (_selectedPaths.Count != 1) return false;
        if (_selectedPaths[0].FillType != SpriteFillType.Linear) return false;
        return ColorPicker.IsOpen(WidgetIds.FillColor);
    }

    private static readonly Color GradientHandleOutline = new(1f, 0.3f, 0.3f);

    private void DrawGradientOverlay()
    {
        if (!IsGradientOverlayVisible()) return;

        var path = _selectedPaths[0];
        var gradient = path.FillGradient;
        var pathToDoc = path.HasTransform ? path.PathTransform : Matrix3x2.Identity;
        var docTransform = Document.Transform;

        var startDoc = Vector2.Transform(gradient.Start, pathToDoc);
        var endDoc = Vector2.Transform(gradient.End, pathToDoc);

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(docTransform);
            Graphics.SetSortGroup(7);

            // Line between endpoints
            Gizmos.SetColor(GradientHandleOutline.WithAlpha(0.8f));
            Gizmos.DrawLine(startDoc, endDoc, EditorStyle.SpritePath.SegmentLineWidth, order: 8);

            // Handles: red outline, filled with stop color
            DrawGradientHandle(startDoc, gradient.StartColor.ToColor(), docTransform, order: 9);
            DrawGradientHandle(endDoc, gradient.EndColor.ToColor(), docTransform, order: 9);
        }
    }

    private static void DrawGradientHandle(Vector2 center, Color fillColor, Matrix3x2 docTransform, ushort order)
    {
        var sprite = EditorAssets.Sprites.GizmoHandle;
        var baseSize = EditorStyle.SpritePath.AnchorSize * Gizmos.ZoomRefScale;
        var ppu = sprite.PixelsPerUnit / sprite.Bounds.Width;

        using (Graphics.PushState())
        {
            Graphics.SetShader(EditorAssets.Shaders.Sprite);
            var baseTransform = Matrix3x2.CreateTranslation(center) * docTransform;

            // Red outline (full size)
            Graphics.SetTransform(Matrix3x2.CreateScale(baseSize * ppu) * baseTransform);
            Graphics.SetTextureFilter(TextureFilter.Linear);
            Graphics.SetColor(GradientHandleOutline);
            Graphics.Draw(sprite, order);

            // Stop color fill (inset)
            var fillSize = (baseSize - EditorStyle.SpritePath.AnchorOutlineSize * Gizmos.ZoomRefScale) * ppu;
            Graphics.SetTransform(Matrix3x2.CreateScale(fillSize) * baseTransform);
            Graphics.SetColor(fillColor);
            Graphics.Draw(sprite, (ushort)(order + 1));
        }
    }

    private int HitTestGradientHandle(Vector2 localMousePos)
    {
        if (_selectedPaths.Count != 1) return -1;
        var path = _selectedPaths[0];
        var gradient = path.FillGradient;
        var pathToDoc = path.HasTransform ? path.PathTransform : Matrix3x2.Identity;

        var startDoc = Vector2.Transform(gradient.Start, pathToDoc);
        var endDoc = Vector2.Transform(gradient.End, pathToDoc);

        var radius = EditorStyle.SpritePath.AnchorHitRadius;
        var radiusSqr = radius * radius;

        var distStart = Vector2.DistanceSquared(localMousePos, startDoc);
        var distEnd = Vector2.DistanceSquared(localMousePos, endDoc);

        if (distStart < radiusSqr && distStart <= distEnd) return 0;
        if (distEnd < radiusSqr) return 1;
        return -1;
    }

    internal bool HandleGradientDrag(Vector2 localMousePos)
    {
        var handle = HitTestGradientHandle(localMousePos);
        if (handle < 0) return false;

        ColorPicker.ActiveGradientStop = handle;
        Undo.Record(Document);
        _gradientDragPath = _selectedPaths[0];
        _gradientDragHandle = handle;
        _gradientDragInitial = _gradientDragPath.FillGradient;
        _gradientDragStartWorld = Workspace.MouseWorldPosition;
        _isGradientDragging = true;
        return true;
    }

    // Gradient drag state — updated by editor modes
    internal bool _isGradientDragging;
    internal SpritePath? _gradientDragPath;
    internal int _gradientDragHandle;
    internal SpriteFillGradient _gradientDragInitial;
    internal Vector2 _gradientDragStartWorld;

    internal void UpdateGradientDrag()
    {
        if (!_isGradientDragging || _gradientDragPath == null) return;

        var worldDelta = Workspace.MouseWorldPosition - _gradientDragStartWorld;
        Matrix3x2.Invert(Document.Transform, out var invDoc);
        var docDelta = Vector2.TransformNormal(worldDelta, invDoc);

        var localDelta = docDelta;
        if (_gradientDragPath.HasTransform)
        {
            Matrix3x2.Invert(_gradientDragPath.PathTransform, out var invPath);
            localDelta = Vector2.TransformNormal(docDelta, invPath);
        }

        var g = _gradientDragInitial;
        if (_gradientDragHandle == 0)
            g.Start += localDelta;
        else
            g.End += localDelta;
        _gradientDragPath.FillGradient = g;
        MarkDirty();
    }

    internal void CommitGradientDrag()
    {
        _isGradientDragging = false;
        _gradientDragPath = null;
    }

    internal void CancelGradientDrag()
    {
        if (_gradientDragPath != null)
        {
            _gradientDragPath.FillGradient = _gradientDragInitial;
            MarkDirty();
        }
        Undo.Cancel();
        _isGradientDragging = false;
        _gradientDragPath = null;
    }

    private bool OnGradientBackdropClick()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        return HandleGradientDrag(localMousePos);
    }
}

