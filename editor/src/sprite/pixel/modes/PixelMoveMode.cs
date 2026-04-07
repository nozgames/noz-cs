//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PixelMoveMode : EditorMode<PixelSpriteEditor>
{
    private struct FloatingLayer
    {
        public PixelLayer Layer;
        public PixelData<Color32> Pixels;
        public PixelData<byte>? Mask;
    }

    private List<FloatingLayer>? _floatingLayers;
    public bool IsLifted => _floatingLayers != null;
    private RectInt _sourceRect;
    private Vector2Int _offset;
    private Vector2Int _dragStartPixel;
    private bool _isDragging;

    private List<PixelLayer> GetTargetLayers()
    {
        var layers = new List<PixelLayer>();
        var selected = Editor.SelectedNode;
        if (selected is SpriteGroup group)
        {
            group.ForEach((PixelLayer layer) =>
            {
                if (layer.Pixels != null)
                    layers.Add(layer);
            });
        }
        else if (Editor.ActiveLayer is { Pixels: not null } active)
        {
            layers.Add(active);
        }
        return layers;
    }

    private RectInt GetContentBounds()
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        if (_floatingLayers != null)
        {
            foreach (var fl in _floatingLayers)
            {
                var fw = fl.Pixels.Width;
                var fh = fl.Pixels.Height;

                for (var y = 0; y < fh; y++)
                    for (var x = 0; x < fw; x++)
                    {
                        if (fl.Mask != null && fl.Mask[x, y] == 0) continue;
                        if (fl.Pixels[x, y].A == 0) continue;

                        var dx = _sourceRect.X + x;
                        var dy = _sourceRect.Y + y;
                        if (dx < minX) minX = dx;
                        if (dy < minY) minY = dy;
                        if (dx >= maxX) maxX = dx + 1;
                        if (dy >= maxY) maxY = dy + 1;
                    }
            }
        }

        if (minX > maxX) return _sourceRect;
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    public override void OnEnter()
    {
        Lift();
        if (_floatingLayers == null)
            Editor.SetMode(new PencilMode());
    }

    public override void Update()
    {
        EditorCursor.SetCrosshair();

        if (_floatingLayers == null) return;

        var pixel = Editor.WorldToPixel(Workspace.MouseWorldPosition);

        // Start drag — erase stamped pixels so they don't leave a copy behind
        if (!_isDragging && Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All))
        {
            Undo.Record(Editor.Document);
            ClearFloatingFromCanvas();
            _isDragging = true;
            _dragStartPixel = pixel;
            _offset = Vector2Int.Zero;
        }

        // Continue drag
        if (_isDragging && Input.IsButtonDown(InputCode.MouseLeft, InputScope.All))
        {
            _offset = pixel - _dragStartPixel;
        }

        // Release — stamp pixels but keep floating buffer
        if (_isDragging && !Input.IsButtonDown(InputCode.MouseLeft, InputScope.All))
        {
            _isDragging = false;
            if (_offset != Vector2Int.Zero)
                Stamp();
        }
    }

    private void Lift()
    {
        var layers = GetTargetLayers();
        if (layers.Count == 0) return;

        var w = Editor.Document.CanvasSize.X;
        var h = Editor.Document.CanvasSize.Y;
        _floatingLayers = new List<FloatingLayer>();

        if (!Editor.HasSelection)
        {
            _sourceRect = new RectInt(0, 0, w, h);
            foreach (var layer in layers)
            {
                var floating = new PixelData<Color32>(w, h);
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                        floating[x, y] = layer.Pixels![x, y];
                _floatingLayers.Add(new FloatingLayer { Layer = layer, Pixels = floating });
            }
        }
        else
        {
            var sb = Editor.GetSelectionBounds();
            if (sb == null) return;

            var s = sb.Value;
            _sourceRect = s;
            foreach (var layer in layers)
            {
                var floating = new PixelData<Color32>(s.Width, s.Height);
                var mask = new PixelData<byte>(s.Width, s.Height);

                for (var y = 0; y < s.Height; y++)
                    for (var x = 0; x < s.Width; x++)
                    {
                        var sx = s.X + x;
                        var sy = s.Y + y;
                        if (!Editor.IsPixelInBounds(new Vector2Int(sx, sy))) continue;
                        if (!Editor.IsPixelSelected(sx, sy)) continue;

                        floating[x, y] = layer.Pixels![sx, sy];
                        mask[x, y] = 255;
                    }
                _floatingLayers.Add(new FloatingLayer { Layer = layer, Pixels = floating, Mask = mask });
            }
        }
    }

    private void ClearFloatingFromCanvas()
    {
        if (_floatingLayers == null) return;

        foreach (var fl in _floatingLayers)
        {
            if (fl.Layer.Pixels == null) continue;

            var fw = fl.Pixels.Width;
            var fh = fl.Pixels.Height;

            for (var y = 0; y < fh; y++)
                for (var x = 0; x < fw; x++)
                {
                    if (fl.Mask != null && fl.Mask[x, y] == 0) continue;
                    if (fl.Pixels[x, y].A == 0) continue;

                    var dx = _sourceRect.X + x;
                    var dy = _sourceRect.Y + y;
                    if (Editor.IsPixelInBounds(new Vector2Int(dx, dy)))
                        fl.Layer.Pixels.Set(dx, dy, default);
                }
        }

        Editor.InvalidateComposite();
    }

    private void Stamp()
    {
        if (_floatingLayers == null) return;

        foreach (var fl in _floatingLayers)
        {
            if (fl.Layer.Pixels == null) continue;

            var fw = fl.Pixels.Width;
            var fh = fl.Pixels.Height;

            for (var y = 0; y < fh; y++)
                for (var x = 0; x < fw; x++)
                {
                    if (fl.Mask != null && fl.Mask[x, y] == 0) continue;
                    var src = fl.Pixels[x, y];
                    if (src.A == 0) continue;

                    var dx = _sourceRect.X + _offset.X + x;
                    var dy = _sourceRect.Y + _offset.Y + y;
                    if (Editor.IsPixelInBounds(new Vector2Int(dx, dy)))
                        fl.Layer.Pixels.Set(dx, dy, src);
                }
        }

        // Update source rect to new position
        _sourceRect = new RectInt(
            _sourceRect.X + _offset.X,
            _sourceRect.Y + _offset.Y,
            _sourceRect.Width,
            _sourceRect.Height);
        _offset = Vector2Int.Zero;

        if (Editor.HasSelection)
        {
            Editor.ApplyRectSelection(
                GetContentBounds(),
                SelectionOp.Replace);
        }

        Editor.InvalidateComposite();
    }

    private void Drop()
    {
        if (_floatingLayers == null) return;
        Stamp();
        Editor.InvalidateActiveLayerPreview();
        DisposeFloating();
    }

    private void DisposeFloating()
    {
        if (_floatingLayers == null) return;
        foreach (var fl in _floatingLayers)
        {
            fl.Pixels.Dispose();
            fl.Mask?.Dispose();
        }
        _floatingLayers = null;
    }

    public override void OnUndoRedo()
    {
        DisposeFloating();
        Lift();
    }

    public override void OnExit()
    {
        if (_floatingLayers != null)
            Drop();
    }

    public override void Draw()
    {
        if (_floatingLayers == null) return;

        var bounds = Editor.CanvasRect;
        var epr = Editor.EditablePixelRect;
        var cellW = bounds.Width / epr.Width;
        var cellH = bounds.Height / epr.Height;

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Editor.Document.Transform);

            foreach (var fl in _floatingLayers)
            {
                var fw = fl.Pixels.Width;
                var fh = fl.Pixels.Height;

                for (var y = 0; y < fh; y++)
                    for (var x = 0; x < fw; x++)
                    {
                        if (fl.Mask != null && fl.Mask[x, y] == 0) continue;
                        var c = fl.Pixels[x, y];
                        if (c.A == 0) continue;

                        var dx = _sourceRect.X + _offset.X + x;
                        var dy = _sourceRect.Y + _offset.Y + y;

                        Graphics.SetColor(c.ToColor());
                        Graphics.Draw(new Rect(
                            bounds.X + (dx - epr.X) * cellW,
                            bounds.Y + (dy - epr.Y) * cellH,
                            cellW, cellH));
                    }
            }
        }

        var selRect = new Rect(
            bounds.X + (_sourceRect.X + _offset.X - epr.X) * cellW,
            bounds.Y + (_sourceRect.Y + _offset.Y - epr.Y) * cellH,
            _sourceRect.Width * cellW,
            _sourceRect.Height * cellH);

        Editor.DrawSelectionRect(selRect);
    }
}
