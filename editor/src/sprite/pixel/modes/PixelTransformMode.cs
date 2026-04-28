//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class PixelTransformMode : EditorMode<PixelEditor>, IActiveLayerHandler
{
    private enum DragType { None, Move, Rotate, Scale }

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
    private bool _isFloating;

    // Transform state
    private DragType _dragType;
    private float _rotation;
    private Vector2 _scale = Vector2.One;
    private SpritePathHandle _hoverHandle;

    // Move drag
    private Vector2Int _dragStartPixel;

    // Rotate drag
    private Vector2 _rotatePivotLocal;
    private float _rotateStartAngle;

    // Scale drag
    private SpritePathHandle _scaleHandle;
    private Vector2 _scalePivotLocal;
    private Vector2 _scalePivotSel;
    private Vector2 _scaleMouseStartSel;
    private bool _scaleConstrainX;
    private bool _scaleConstrainY;

    public override void OnEnter()
    {
        _rotation = 0f;
        _scale = Vector2.One;
        Lift();
        if (_floatingLayers == null)
            Editor.SetMode(new BrushMode());
    }

    public override void Update()
    {
        if (_floatingLayers == null) return;

        // Active drag handling
        if (_dragType != DragType.None)
        {
            if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
                Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
            {
                CancelTransform();
                return;
            }

            if (!Input.IsButtonDown(InputCode.MouseLeft, InputScope.All))
            {
                CommitTransform();
                return;
            }

            UpdateTransformDrag();
            return;
        }

        // Idle — hit-test handles and set cursor
        Matrix3x2.Invert(Editor.Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        _hoverHandle = HitTestHandles(mouseLocal);
        SetCursorForHandle(_hoverHandle);

        // Start drag
        if (Input.WasButtonPressed(InputCode.MouseLeft, InputScope.All) && _hoverHandle != SpritePathHandle.None)
        {
            EnsureFloating();

            switch (_hoverHandle)
            {
                case SpritePathHandle.Move:
                    StartMoveDrag();
                    break;
                default:
                    if (VectorSpriteEditor.IsRotateHandle(_hoverHandle))
                        StartRotateDrag(mouseLocal);
                    else if (VectorSpriteEditor.IsScaleHandle(_hoverHandle))
                        StartScaleDrag(mouseLocal);
                    break;
            }
        }
    }

    private void EnsureFloating()
    {
        if (_isFloating) return;
        Undo.Record(Editor.Document);
        ClearFloatingFromCanvas();
        _isFloating = true;
    }

    private void StartMoveDrag()
    {
        _dragStartPixel = Editor.WorldToPixelSnapped(Workspace.MouseWorldPosition);
        _dragType = DragType.Move;
    }

    private void StartRotateDrag(Vector2 mouseLocal)
    {
        var selRect = GetSelectionRectLocal();
        _rotatePivotLocal = selRect.Center;
        var dir = mouseLocal - _rotatePivotLocal;
        _rotateStartAngle = MathF.Atan2(dir.Y, dir.X) - _rotation;
        _dragType = DragType.Rotate;
    }

    private void StartScaleDrag(Vector2 mouseLocal)
    {
        _scaleHandle = _hoverHandle;
        _scaleConstrainX = _scaleHandle is SpritePathHandle.ScaleTop or SpritePathHandle.ScaleBottom;
        _scaleConstrainY = _scaleHandle is SpritePathHandle.ScaleLeft or SpritePathHandle.ScaleRight;

        var selRect = GetSelectionRectLocal();
        _scalePivotLocal = GetOppositePivotLocal(_scaleHandle, selRect);
        var invRot = Matrix3x2.CreateRotation(-_rotation, selRect.Center);
        _scalePivotSel = Vector2.Transform(_scalePivotLocal, invRot);
        _scaleMouseStartSel = Vector2.Transform(mouseLocal, invRot);
        _dragType = DragType.Scale;
    }

    private void UpdateTransformDrag()
    {
        switch (_dragType)
        {
            case DragType.Move: UpdateMoveDrag(); break;
            case DragType.Rotate: UpdateRotateDrag(); break;
            case DragType.Scale: UpdateScaleDrag(); break;
        }
    }

    private void UpdateMoveDrag()
    {
        var pixel = Editor.WorldToPixelSnapped(Workspace.MouseWorldPosition);
        var newOffset = new Vector2Int(
            _offset.X + pixel.X - _dragStartPixel.X,
            _offset.Y + pixel.Y - _dragStartPixel.Y);

        if (newOffset != _offset)
        {
            _offset = newOffset;
            _dragStartPixel = pixel;
        }
    }

    private void UpdateRotateDrag()
    {
        Matrix3x2.Invert(Editor.Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        var dir = mouseLocal - _rotatePivotLocal;
        var currentAngle = MathF.Atan2(dir.Y, dir.X);
        _rotation = currentAngle - _rotateStartAngle;

        if (Input.IsCtrlDown(InputScope.All))
        {
            var snap = MathF.PI / 12f; // 15 degrees
            _rotation = MathF.Round(_rotation / snap) * snap;
        }

        EditorCursor.SetRotate();
    }

    private void UpdateScaleDrag()
    {
        Matrix3x2.Invert(Editor.Document.Transform, out var invTransform);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);

        var selRect = GetSelectionRectLocal();
        var invRot = Matrix3x2.CreateRotation(-_rotation, selRect.Center);
        var mouseSel = Vector2.Transform(mouseLocal, invRot);

        var startDelta = _scaleMouseStartSel - _scalePivotSel;
        var mouseDelta = mouseSel - _scalePivotSel;

        var scaleX = MathF.Abs(startDelta.X) > 0.0001f ? mouseDelta.X / startDelta.X : 1f;
        var scaleY = MathF.Abs(startDelta.Y) > 0.0001f ? mouseDelta.Y / startDelta.Y : 1f;

        if (_scaleConstrainX) scaleX = 1f;
        if (_scaleConstrainY) scaleY = 1f;

        if (!_scaleConstrainX && !_scaleConstrainY && Input.IsShiftDown(InputScope.All))
        {
            var uniform = (scaleX + scaleY) * 0.5f;
            scaleX = uniform;
            scaleY = uniform;
        }

        _scale = new Vector2(MathF.Max(scaleX, 0.01f), MathF.Max(scaleY, 0.01f));
        EditorCursor.SetScale(_scaleHandle, _rotation);
    }

    private void CommitTransform()
    {
        UpdateTransformDrag();
        _dragType = DragType.None;

        if (_isFloating)
        {
            StampTransformed();
            _isFloating = false;

            // Re-lift so the user can continue transforming
            Lift();
        }
    }

    private void CancelTransform()
    {
        _rotation = 0f;
        _scale = Vector2.One;
        _offset = Vector2Int.Zero;
        _dragType = DragType.None;

        // Restore original pixels
        if (_isFloating)
        {
            Stamp();
            _isFloating = false;
            Undo.Cancel();

            // Re-lift from the restored state
            DisposeFloating();
            Lift();
        }
    }

    // --- Coordinate helpers ---

    private Rect GetSelectionRectLocal()
    {
        var canvas = Editor.CanvasRect;
        var epr = Editor.EditablePixelRect;
        var cellW = canvas.Width / epr.Width;
        var cellH = canvas.Height / epr.Height;
        return new Rect(
            canvas.X + (_sourceRect.X + _offset.X - epr.X) * cellW,
            canvas.Y + (_sourceRect.Y + _offset.Y - epr.Y) * cellH,
            _sourceRect.Width * cellW,
            _sourceRect.Height * cellH);
    }

    private Matrix3x2 GetPixelToLocalTransform()
    {
        var selRect = GetSelectionRectLocal();
        return Matrix3x2.CreateScale(_scale, _scalePivotLocal)
             * Matrix3x2.CreateRotation(_rotation, selRect.Center);
    }

    // --- Handle system ---

    private static Vector2 GetRotateHandleOffset(Vector2 corner, Vector2 boundsCenter)
    {
        var dir = Vector2.Normalize(corner - boundsCenter);
        var offset = EditorStyle.SpritePath.RotateHandleOffset * Gizmos.ZoomRefScale;
        return corner + dir * offset;
    }

    private static Vector2 GetOppositePivotLocal(SpritePathHandle handle, Rect b)
    {
        var midX = b.X + b.Width * 0.5f;
        var midY = b.Y + b.Height * 0.5f;
        return handle switch
        {
            SpritePathHandle.ScaleTopLeft => new Vector2(b.Right, b.Bottom),
            SpritePathHandle.ScaleTop => new Vector2(midX, b.Bottom),
            SpritePathHandle.ScaleTopRight => new Vector2(b.X, b.Bottom),
            SpritePathHandle.ScaleRight => new Vector2(b.X, midY),
            SpritePathHandle.ScaleBottomRight => new Vector2(b.X, b.Y),
            SpritePathHandle.ScaleBottom => new Vector2(midX, b.Y),
            SpritePathHandle.ScaleBottomLeft => new Vector2(b.Right, b.Y),
            SpritePathHandle.ScaleLeft => new Vector2(b.Right, midY),
            _ => b.Center,
        };
    }

    private SpritePathHandle HitTestHandles(Vector2 docLocalPos)
    {
        if (_floatingLayers == null) return SpritePathHandle.None;

        var selRect = GetSelectionRectLocal();
        if (selRect.Width <= 0 && selRect.Height <= 0) return SpritePathHandle.None;

        var center = selRect.Center;
        var invRot = Matrix3x2.CreateRotation(-_rotation, center);
        var selToLocal = Matrix3x2.CreateRotation(_rotation, center)
                       * Matrix3x2.CreateScale(_scale, center);
        var localToSel = selToLocal;
        Matrix3x2.Invert(localToSel, out localToSel);
        var selPos = Vector2.Transform(docLocalPos, localToSel);

        var hitRadius = EditorStyle.SpritePath.AnchorHitRadius;
        var hitRadiusSqr = hitRadius * hitRadius;

        var tl = new Vector2(selRect.X, selRect.Y);
        var tr = new Vector2(selRect.Right, selRect.Y);
        var br = new Vector2(selRect.Right, selRect.Bottom);
        var bl = new Vector2(selRect.X, selRect.Bottom);

        var midX = selRect.X + selRect.Width * 0.5f;
        var midY = selRect.Y + selRect.Height * 0.5f;
        var boundsCenter = new Vector2(midX, midY);

        // Rotation handles (outside corners)
        var rotateHitRadius = hitRadius * EditorStyle.SpritePath.RotateHandleScale;
        var rotateHitRadiusSqr = rotateHitRadius * rotateHitRadius;
        Span<Vector2> corners = [tl, tr, br, bl];
        for (var i = 0; i < 4; i++)
        {
            var rotPos = GetRotateHandleOffset(corners[i], boundsCenter);
            if (Vector2.DistanceSquared(selPos, rotPos) <= rotateHitRadiusSqr)
                return SpritePathHandle.RotateTopLeft + i;
        }

        // Corner scale handles
        Span<SpritePathHandle> cornerHandles = [
            SpritePathHandle.ScaleTopLeft, SpritePathHandle.ScaleTopRight,
            SpritePathHandle.ScaleBottomRight, SpritePathHandle.ScaleBottomLeft
        ];
        for (var i = 0; i < 4; i++)
        {
            if (Vector2.DistanceSquared(selPos, corners[i]) <= hitRadiusSqr)
                return cornerHandles[i];
        }

        // Edge midpoint scale handles
        Span<Vector2> edges = [
            new(midX, selRect.Y), new(selRect.Right, midY),
            new(midX, selRect.Bottom), new(selRect.X, midY)
        ];
        Span<SpritePathHandle> edgeHandles = [
            SpritePathHandle.ScaleTop, SpritePathHandle.ScaleRight,
            SpritePathHandle.ScaleBottom, SpritePathHandle.ScaleLeft
        ];
        for (var i = 0; i < 4; i++)
        {
            if (Vector2.DistanceSquared(selPos, edges[i]) <= hitRadiusSqr)
                return edgeHandles[i];
        }

        // Inside bounds = move
        if (selRect.Contains(selPos))
            return SpritePathHandle.Move;

        return SpritePathHandle.None;
    }

    private void SetCursorForHandle(SpritePathHandle handle)
    {
        if (handle == SpritePathHandle.None)
            EditorCursor.SetArrow();
        else if (handle == SpritePathHandle.Move)
            EditorCursor.SetMove();
        else if (VectorSpriteEditor.IsRotateHandle(handle))
            EditorCursor.SetRotate(handle, _rotation);
        else if (VectorSpriteEditor.IsScaleHandle(handle))
            EditorCursor.SetScale(handle, _rotation);
    }

    // --- Floating layer operations ---

    private void Lift()
    {
        var layers = Editor.GetSelectedLayers();
        if (layers.Count == 0) return;

        if (!Editor.HasSelection)
        {
            var contentBounds = Editor.GetLayerContentBounds(layers);
            if (contentBounds == null) return;
            Editor.ApplyRectSelection(contentBounds.Value, SelectionOp.Replace);
        }

        var sb = Editor.GetSelectionBounds();
        if (sb == null) return;

        _floatingLayers = new List<FloatingLayer>();
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

        _offset = Vector2Int.Zero;
        _rotation = 0f;
        _scale = Vector2.One;
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

        _sourceRect = new RectInt(
            _sourceRect.X + _offset.X,
            _sourceRect.Y + _offset.Y,
            _sourceRect.Width,
            _sourceRect.Height);
        _offset = Vector2Int.Zero;

        if (Editor.HasSelection)
            Editor.ApplyRectSelection(GetContentBounds(), SelectionOp.Replace);

        Editor.InvalidateComposite();
    }

    private void StampTransformed()
    {
        if (_floatingLayers == null) return;

        // Fast path: no rotation or scale, just use integer offset
        if (MathF.Abs(_rotation) < 0.0001f && MathF.Abs(_scale.X - 1f) < 0.0001f && MathF.Abs(_scale.Y - 1f) < 0.0001f)
        {
            Stamp();
            return;
        }

        var w = _sourceRect.Width;
        var h = _sourceRect.Height;
        var cx = w * 0.5f;
        var cy = h * 0.5f;

        // Convert _scalePivotLocal (document-local coords) into source-rect pixel coords (pre-offset).
        var canvas = Editor.CanvasRect;
        var epr = Editor.EditablePixelRect;
        var cellW = canvas.Width / epr.Width;
        var cellH = canvas.Height / epr.Height;
        var selRect = GetSelectionRectLocal();
        var pivotPixels = new Vector2(
            (_scalePivotLocal.X - selRect.X) / cellW + _offset.X,
            (_scalePivotLocal.Y - selRect.Y) / cellH + _offset.Y);

        // Forward transform: source pixel -> destination pixel (in source-rect-local coords)
        var fwd = Matrix3x2.CreateScale(_scale, pivotPixels)
                * Matrix3x2.CreateRotation(_rotation, new Vector2(cx, cy))
                * Matrix3x2.CreateTranslation(_offset.X, _offset.Y);

        Matrix3x2.Invert(fwd, out var inv);

        // Compute destination bounding box
        Span<Vector2> srcCorners = [
            Vector2.Transform(new Vector2(0, 0), fwd),
            Vector2.Transform(new Vector2(w, 0), fwd),
            Vector2.Transform(new Vector2(w, h), fwd),
            Vector2.Transform(new Vector2(0, h), fwd)
        ];

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        for (var i = 0; i < 4; i++)
        {
            var fx = (int)MathF.Floor(srcCorners[i].X);
            var fy = (int)MathF.Floor(srcCorners[i].Y);
            var cx2 = (int)MathF.Ceiling(srcCorners[i].X);
            var cy2 = (int)MathF.Ceiling(srcCorners[i].Y);
            if (fx < minX) minX = fx;
            if (fy < minY) minY = fy;
            if (cx2 > maxX) maxX = cx2;
            if (cy2 > maxY) maxY = cy2;
        }

        foreach (var fl in _floatingLayers)
        {
            if (fl.Layer.Pixels == null) continue;

            for (var dy = minY; dy < maxY; dy++)
                for (var dx = minX; dx < maxX; dx++)
                {
                    var src = Vector2.Transform(new Vector2(dx + 0.5f, dy + 0.5f), inv);
                    var sx = (int)MathF.Floor(src.X);
                    var sy = (int)MathF.Floor(src.Y);

                    if (sx < 0 || sx >= w || sy < 0 || sy >= h) continue;
                    if (fl.Mask != null && fl.Mask[sx, sy] == 0) continue;
                    var color = fl.Pixels[sx, sy];
                    if (color.A == 0) continue;

                    var canvasX = _sourceRect.X + dx;
                    var canvasY = _sourceRect.Y + dy;
                    if (Editor.IsPixelInBounds(new Vector2Int(canvasX, canvasY)))
                        fl.Layer.Pixels.Set(canvasX, canvasY, color);
                }
        }

        // Update source rect to encompass transformed bounds
        _sourceRect = new RectInt(
            _sourceRect.X + minX,
            _sourceRect.Y + minY,
            maxX - minX,
            maxY - minY);
        _offset = Vector2Int.Zero;
        _rotation = 0f;
        _scale = Vector2.One;

        if (Editor.HasSelection)
            Editor.ApplyRectSelection(_sourceRect, SelectionOp.Replace);

        Editor.InvalidateComposite();
    }

    private RectInt GetContentBounds()
    {
        var cMinX = int.MaxValue;
        var cMinY = int.MaxValue;
        var cMaxX = int.MinValue;
        var cMaxY = int.MinValue;

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
                        if (dx < cMinX) cMinX = dx;
                        if (dy < cMinY) cMinY = dy;
                        if (dx >= cMaxX) cMaxX = dx + 1;
                        if (dy >= cMaxY) cMaxY = dy + 1;
                    }
            }
        }

        if (cMinX > cMaxX) return _sourceRect;
        return new RectInt(cMinX, cMinY, cMaxX - cMinX, cMaxY - cMinY);
    }

    private void Drop()
    {
        if (_floatingLayers == null) return;
        if (_isFloating)
        {
            StampTransformed();
            _isFloating = false;
        }
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


    public void OnActiveLayerChanged(PixelEditor editor)
    {
        if (IsLifted)
            editor.SetMode(new BrushMode());
    }

    public override void OnUndoRedo()
    {
        DisposeFloating();
        _isFloating = false;
        _rotation = 0f;
        _scale = Vector2.One;
        _offset = Vector2Int.Zero;
        _dragType = DragType.None;
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

        var canvas = Editor.CanvasRect;
        var epr = Editor.EditablePixelRect;
        var cellW = canvas.Width / epr.Width;
        var cellH = canvas.Height / epr.Height;

        var selRect = GetSelectionRectLocal();

        // Build the transform for rotated/scaled pixel rendering
        var pixelTransform = Matrix3x2.CreateScale(_scale, _scalePivotLocal)
                           * Matrix3x2.CreateRotation(_rotation, selRect.Center);

        // Draw floating pixels when they've been cleared from canvas
        if (_isFloating)
        {
            using (Gizmos.PushState(EditorLayer.DocumentEditor))
            {
                Graphics.SetTransform(pixelTransform * Editor.Document.Transform);

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
                                canvas.X + (dx - epr.X) * cellW,
                                canvas.Y + (dy - epr.Y) * cellH,
                                cellW, cellH));
                        }
                }
            }
        }

        // Draw transform handles
        DrawTransformHandles(selRect, pixelTransform);
    }

    private void DrawTransformHandles(Rect selRect, Matrix3x2 pixelTransform)
    {
        var center = selRect.Center;

        // Compute transformed corners
        var tl = Vector2.Transform(new Vector2(selRect.X, selRect.Y), pixelTransform);
        var tr = Vector2.Transform(new Vector2(selRect.Right, selRect.Y), pixelTransform);
        var br = Vector2.Transform(new Vector2(selRect.Right, selRect.Bottom), pixelTransform);
        var bl = Vector2.Transform(new Vector2(selRect.X, selRect.Bottom), pixelTransform);

        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(Editor.Document.Transform);
            var lineWidth = EditorStyle.SpritePath.SegmentLineWidth;

            // Bounding box lines
            Gizmos.SetColor(EditorStyle.Palette.Primary);
            Gizmos.DrawLine(tl, tr, lineWidth, order: 2);
            Gizmos.DrawLine(tr, br, lineWidth, order: 2);
            Gizmos.DrawLine(br, bl, lineWidth, order: 2);
            Gizmos.DrawLine(bl, tl, lineWidth, order: 2);

            // Scale handles at corners
            var h = _hoverHandle;
            Gizmos.DrawAnchor(tl, selected: h is SpritePathHandle.ScaleTopLeft, order: 6);
            Gizmos.DrawAnchor(tr, selected: h is SpritePathHandle.ScaleTopRight, order: 6);
            Gizmos.DrawAnchor(br, selected: h is SpritePathHandle.ScaleBottomRight, order: 6);
            Gizmos.DrawAnchor(bl, selected: h is SpritePathHandle.ScaleBottomLeft, order: 6);

            // Scale handles at edge midpoints
            var mt = Vector2.Transform(new Vector2(center.X, selRect.Y), pixelTransform);
            var mr = Vector2.Transform(new Vector2(selRect.Right, center.Y), pixelTransform);
            var mb = Vector2.Transform(new Vector2(center.X, selRect.Bottom), pixelTransform);
            var ml = Vector2.Transform(new Vector2(selRect.X, center.Y), pixelTransform);

            Gizmos.DrawAnchor(mt, selected: h is SpritePathHandle.ScaleTop, order: 6);
            Gizmos.DrawAnchor(mr, selected: h is SpritePathHandle.ScaleRight, order: 6);
            Gizmos.DrawAnchor(mb, selected: h is SpritePathHandle.ScaleBottom, order: 6);
            Gizmos.DrawAnchor(ml, selected: h is SpritePathHandle.ScaleLeft, order: 6);

            // Rotate handles offset outside corners
            var transformedCenter = Vector2.Transform(center, pixelTransform);
            var rotScale = EditorStyle.SpritePath.RotateHandleScale;
            Gizmos.DrawAnchor(GetRotateHandleOffset(tl, transformedCenter), selected: h is SpritePathHandle.RotateTopLeft, scale: rotScale, order: 6);
            Gizmos.DrawAnchor(GetRotateHandleOffset(tr, transformedCenter), selected: h is SpritePathHandle.RotateTopRight, scale: rotScale, order: 6);
            Gizmos.DrawAnchor(GetRotateHandleOffset(br, transformedCenter), selected: h is SpritePathHandle.RotateBottomRight, scale: rotScale, order: 6);
            Gizmos.DrawAnchor(GetRotateHandleOffset(bl, transformedCenter), selected: h is SpritePathHandle.RotateBottomLeft, scale: rotScale, order: 6);
        }
    }
}
