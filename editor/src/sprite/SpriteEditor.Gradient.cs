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
    private bool IsGradientOverlayVisible()
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

    private bool HandleGradientDrag(Vector2 localMousePos)
    {
        var handle = HitTestGradientHandle(localMousePos);
        if (handle < 0) return false;

        ColorPicker.ActiveGradientStop = handle;
        Undo.Record(Document);
        Workspace.BeginTool(new GradientHandleDragTool(this, _selectedPaths[0], handle));
        return true;
    }

    private bool OnGradientBackdropClick()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        return HandleGradientDrag(localMousePos);
    }
}

internal class GradientHandleDragTool : Tool
{
    private readonly SpriteEditor _editor;
    private readonly SpritePath _path;
    private readonly int _handle;
    private readonly SpriteFillGradient _initialGradient;
    private Vector2 _startWorld;

    public GradientHandleDragTool(SpriteEditor editor, SpritePath path, int handle)
    {
        _editor = editor;
        _path = path;
        _handle = handle;
        _initialGradient = path.FillGradient;
    }

    public override void Begin()
    {
        base.Begin();
        _startWorld = Workspace.MouseWorldPosition;
    }

    public override void Update()
    {
        if (Input.WasButtonReleasedRaw(InputCode.MouseLeft))
        {
            Workspace.EndTool();
            return;
        }

        var worldDelta = Workspace.MouseWorldPosition - _startWorld;

        Matrix3x2.Invert(_editor.Document.Transform, out var invDoc);
        var docDelta = Vector2.TransformNormal(worldDelta, invDoc);

        var localDelta = docDelta;
        if (_path.HasTransform)
        {
            Matrix3x2.Invert(_path.PathTransform, out var invPath);
            localDelta = Vector2.TransformNormal(docDelta, invPath);
        }

        var g = _initialGradient;
        if (_handle == 0)
            g.Start += localDelta;
        else
            g.End += localDelta;
        _path.FillGradient = g;

        _editor.MarkDirty();
    }

    public override void Cancel()
    {
        _path.FillGradient = _initialGradient;
        _editor.MarkDirty();
        base.Cancel();
    }
}
