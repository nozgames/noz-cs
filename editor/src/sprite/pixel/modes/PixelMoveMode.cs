//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PixelMoveMode : EditorMode<PixelSpriteEditor>
{
    private PixelData<Color32>? _floating;
    private PixelData<byte>? _floatingMask;
    private RectInt _sourceRect;
    private Vector2Int _offset;
    private Vector2Int _dragStartPixel;

    public override void Update()
    {
        EditorCursor.SetCrosshair();

        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
        {
            if (_floating != null)
                Cancel();
            return;
        }

        var pixel = Editor.WorldToPixel(Workspace.MouseWorldPosition);

        // Start drag — lift pixels
        if (_floating == null && Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
        {
            Lift();
            _dragStartPixel = pixel;
            _offset = Vector2Int.Zero;
        }

        // Continue drag
        if (_floating != null && Input.IsButtonDown(InputCode.MouseLeft, InputScope.All))
        {
            _offset = pixel - _dragStartPixel;
        }

        // Release — drop pixels
        if (_floating != null && !Input.IsButtonDown(InputCode.MouseLeft, InputScope.All))
        {
            Drop();
        }
    }

    private void Lift()
    {
        var layer = Editor.ActiveLayer;
        if (layer?.Pixels == null) return;

        Undo.Record(Editor.Document);

        var w = Editor.Document.CanvasSize.X;
        var h = Editor.Document.CanvasSize.Y;

        if (!Editor.HasSelection)
        {
            _sourceRect = new RectInt(0, 0, w, h);
            _floating = new PixelData<Color32>(w, h);
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    _floating[x, y] = layer.Pixels[x, y];
                    layer.Pixels.Set(x, y, default);
                }
        }
        else
        {
            var sb = Editor.GetSelectionBounds();
            if (sb == null) return;

            var s = sb.Value;
            _sourceRect = s;
            _floating = new PixelData<Color32>(s.Width, s.Height);
            _floatingMask = new PixelData<byte>(s.Width, s.Height);

            for (var y = 0; y < s.Height; y++)
                for (var x = 0; x < s.Width; x++)
                {
                    var sx = s.X + x;
                    var sy = s.Y + y;
                    if (!Editor.IsPixelInBounds(new Vector2Int(sx, sy))) continue;
                    if (!Editor.IsPixelSelected(sx, sy)) continue;

                    _floating[x, y] = layer.Pixels[sx, sy];
                    _floatingMask[x, y] = 255;
                    layer.Pixels.Set(sx, sy, default);
                }
        }

        Editor.InvalidateComposite();
    }

    private void Drop()
    {
        if (_floating == null) return;

        var layer = Editor.ActiveLayer;
        if (layer?.Pixels != null)
        {
            var fw = _floating.Width;
            var fh = _floating.Height;

            for (var y = 0; y < fh; y++)
                for (var x = 0; x < fw; x++)
                {
                    if (_floatingMask != null && _floatingMask[x, y] == 0) continue;
                    var src = _floating[x, y];
                    if (src.A == 0) continue;

                    var dx = _sourceRect.X + _offset.X + x;
                    var dy = _sourceRect.Y + _offset.Y + y;
                    if (Editor.IsPixelInBounds(new Vector2Int(dx, dy)))
                        layer.Pixels.Set(dx, dy, src);
                }

            if (Editor.HasSelection && _offset != Vector2Int.Zero)
            {
                Editor.ApplyRectSelection(
                    new RectInt(
                        _sourceRect.X + _offset.X,
                        _sourceRect.Y + _offset.Y,
                        _sourceRect.Width,
                        _sourceRect.Height),
                    SelectionOp.Replace);
            }
        }

        _floating.Dispose();
        _floating = null;
        _floatingMask?.Dispose();
        _floatingMask = null;
        Editor.InvalidateComposite();
    }

    private void Cancel()
    {
        _floating?.Dispose();
        _floating = null;
        _floatingMask?.Dispose();
        _floatingMask = null;
        Undo.DoUndo();
    }

    public override void OnExit()
    {
        if (_floating != null)
            Drop();
    }

    public override void Draw()
    {
        if (_floating == null) return;

        var bounds = Editor.Document.Bounds;
        var cellW = bounds.Width / Editor.Document.CanvasSize.X;
        var cellH = bounds.Height / Editor.Document.CanvasSize.Y;

        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(Editor.Document.Transform);
            Graphics.SetSortGroup(6);

            var fw = _floating.Width;
            var fh = _floating.Height;

            for (var y = 0; y < fh; y++)
                for (var x = 0; x < fw; x++)
                {
                    if (_floatingMask != null && _floatingMask[x, y] == 0) continue;
                    var c = _floating[x, y];
                    if (c.A == 0) continue;

                    var dx = _sourceRect.X + _offset.X + x;
                    var dy = _sourceRect.Y + _offset.Y + y;

                    Graphics.SetColor(c.ToColor());
                    Graphics.Draw(new Rect(
                        bounds.X + dx * cellW,
                        bounds.Y + dy * cellH,
                        cellW, cellH));
                }
        }

        var selRect = new Rect(
            bounds.X + (_sourceRect.X + _offset.X) * cellW,
            bounds.Y + (_sourceRect.Y + _offset.Y) * cellH,
            _sourceRect.Width * cellW,
            _sourceRect.Height * cellH);

        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(Editor.Document.Transform);
            Graphics.SetColor(new Color(0f, 0f, 0f, 0.6f));
            Gizmos.DrawRect(selRect, EditorStyle.Workspace.DocumentBoundsLineWidth * 2f);
            Graphics.SetColor(new Color(1f, 1f, 1f, 0.8f));
            Gizmos.DrawRect(selRect, EditorStyle.Workspace.DocumentBoundsLineWidth);
        }
    }
}
