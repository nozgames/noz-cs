//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public abstract class PixelStrokeMode : EditorMode<PixelEditor>
{
    private Vector2Int _lastPixel = new(-1, -1);
    private Vector2 _lastWorldPixel;
    private bool _isDrawing;
    private InputCode _button;

    protected abstract PixelBrushType BrushType { get; }
    protected abstract void PaintPixel(Vector2Int pixel);
    protected virtual Color OutlineColor => new(1f, 1f, 1f, 0.6f);
    protected virtual EditorMode? EyeDropperExitMode => null;

    protected bool UsesSubPixelStroke => BrushType == PixelBrushType.Brush;
    protected virtual void OnSoftStrokeBegin(Vector2 worldPixel) { }
    protected virtual void OnSoftStrokeSegment(Vector2 from, Vector2 to) { }
    protected virtual void OnSoftStrokeEnd() { }

    public PixelStrokeMode()
    {
        _button = Application.IsTablet ? InputCode.Pen : InputCode.MouseLeft;
    }

    private bool UpdateEyeDropper()
    {
        if (!Input.IsAltDown(InputScope.All) || _isDrawing)
            return false;

        EditorCursor.SetDropper();

        if (Input.WasButtonPressed(_button, InputScope.All))
        {
            var color = Workspace.ReadPixelAtMouse();
            Input.ConsumeButton(_button);
            if (color.A > 0)
                Editor.Document.BrushColor = color;
            if (EyeDropperExitMode is { } exit)
                Editor.SetMode(exit);
        }

        return true;
    }

    public override void Update()
    {
        var mouseWorld = Workspace.PenWorldPosition;
        var pixel = Editor.WorldToPixelSnapped(mouseWorld);
        var worldPixel = Editor.WorldToPixel(mouseWorld);

        if (UpdateEyeDropper())
            return;

        EditorCursor.SetCrosshair();

        if (Input.WasButtonPressed(_button, InputScope.All))
        {
            BeginStroke(pixel, worldPixel);
        }
        else if (_isDrawing)
        {
            if (Input.IsButtonDown(_button, InputScope.All))
            {
                if (!ContinueStrokeFromPenSamples())
                    ContinueStroke(pixel, worldPixel);
            }
            else
                EndStroke();
        }
    }

    // When using a pen, multiple motion samples can arrive per frame.
    // Drain them all so fast strokes don't drop intermediate positions.
    // Returns true if any samples were consumed.
    private bool ContinueStrokeFromPenSamples()
    {
        if (_button != InputCode.Pen) return false;
        var samples = Input.PenMoveSamples;
        if (samples.Length == 0) return false;

        foreach (var screenPos in samples)
        {
            var world = Workspace.Camera.ScreenToWorld(screenPos);
            var p = Editor.WorldToPixelSnapped(world);
            var wp = Editor.WorldToPixel(world);
            ContinueStroke(p, wp);
        }
        return true;
    }

    private void BeginStroke(Vector2Int pixel, Vector2 worldPixel)
    {
        var layer = Editor.ActiveLayer;
        if (layer == null || layer.Pixels == null || layer.Locked || !layer.Visible) return;

        Undo.Record(Editor.Document);
        
        Input.CaptureMouse();
        _isDrawing = true;
        _lastPixel = pixel;
        _lastWorldPixel = worldPixel;
        if (UsesSubPixelStroke)
        {
            Editor.BeginSoftStroke();
            OnSoftStrokeBegin(worldPixel);
        }
        else
        {
            PaintPixel(pixel);
        }
    }

    private void ContinueStroke(Vector2Int pixel, Vector2 worldPixel)
    {
        if (UsesSubPixelStroke)
        {
            if (worldPixel == _lastWorldPixel) return;
            OnSoftStrokeSegment(_lastWorldPixel, worldPixel);
            _lastWorldPixel = worldPixel;
            _lastPixel = pixel;
            return;
        }

        if (pixel == _lastPixel) return;
        DrawLine(_lastPixel, pixel);
        _lastPixel = pixel;
    }

    private void EndStroke()
    {
        if (UsesSubPixelStroke)
        {
            OnSoftStrokeEnd();
            Editor.EndSoftStroke();
        }
        Input.ReleaseMouseCapture();
        _isDrawing = false;
        _lastPixel = new Vector2Int(-1, -1);
        Editor.InvalidateActiveLayerPreview();
        Editor.Document.InvalidateBounds();
        Editor.Document.MarkSpriteDirty();
    }

    private void DrawLine(Vector2Int from, Vector2Int to)
    {
        // Step-then-paint: `from` was already painted by BeginStroke or the previous
        // ContinueStroke, so re-painting it here double-stamps the origin and accumulates
        // extra alpha around it. Advance one Bresenham step first, then paint.
        var dx = Math.Abs(to.X - from.X);
        var dy = Math.Abs(to.Y - from.Y);
        var sx = from.X < to.X ? 1 : -1;
        var sy = from.Y < to.Y ? 1 : -1;
        var err = dx - dy;

        var x = from.X;
        var y = from.Y;

        while (x != to.X || y != to.Y)
        {
            var e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
            PaintPixel(new Vector2Int(x, y));
        }
    }

    public override void Draw()
    {
        if (!UI.IsHovered(Workspace.SceneWidgetId)) return;
        if (UsesSubPixelStroke)
        {
            var worldPixel = Editor.WorldToPixel(Workspace.PenWorldPosition);
            Editor.DrawSoftBrushOutline(worldPixel, OutlineColor);
        }
        else
        {
            var pixel = Editor.WorldToPixelSnapped(Workspace.PenWorldPosition);
            Editor.DrawBrushOutline(pixel, OutlineColor);
        }
    }
}
