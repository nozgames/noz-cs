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

        Undo.Record(Document);
        Workspace.BeginTool(new GradientHandleDragTool(this, _selectedPaths[0], handle));
        return true;
    }
}

internal class GradientHandleDragTool : MoveTool
{
    private readonly SpriteEditor _editor;
    private readonly SpritePath _path;
    private readonly int _handle; // 0 = start, 1 = end
    private readonly SpriteFillGradient _initialGradient;

    public GradientHandleDragTool(SpriteEditor editor, SpritePath path, int handle)
    {
        _editor = editor;
        _path = path;
        _handle = handle;
        _initialGradient = path.FillGradient;
        CommitOnRelease = true;
    }

    protected override void OnUpdate(Vector2 worldDelta)
    {
        // Convert world delta to document-local delta
        Matrix3x2.Invert(_editor.Document.Transform, out var invDoc);
        var docDelta = Vector2.TransformNormal(worldDelta, invDoc);

        // Transform to path-local space
        var localDelta = docDelta;
        if (_path.HasTransform)
        {
            Matrix3x2.Invert(_path.PathTransform, out var invPath);
            localDelta = Vector2.TransformNormal(docDelta, invPath);
        }

        // Reset to initial and apply full delta
        var g = _initialGradient;
        if (_handle == 0)
            g.Start += localDelta;
        else
            g.End += localDelta;
        _path.FillGradient = g;

        _editor.MarkDirty();
    }

    protected override void OnCancel()
    {
        _path.FillGradient = _initialGradient;
        _editor.MarkDirty();
    }
}
