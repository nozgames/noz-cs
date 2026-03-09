//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static unsafe partial class ElementTree
{
    private static bool _inputMousePressed;
    private static bool _inputMouseDown;
    private static WidgetId _hoveredWidget;

    private static void HandlePopupAutoClose()
    {
        ClosePopups = false;
        if (_popupCount == 0) return;

        var anyAutoClose = false;
        for (var i = 0; i < _popupCount; i++)
        {
            ref var pe = ref GetElement(_popups[i]);
            ref var pd = ref pe.Data.Popup;
            if (pd.AutoClose) { anyAutoClose = true; break; }
        }
        if (!anyAutoClose) return;

        if (Input.WasButtonPressedRaw(InputCode.KeyEscape))
        {
            ClosePopups = true;
            Input.ConsumeButton(InputCode.KeyEscape);
        }

        if (_inputMousePressed)
        {
            var clickInsideAutoClosePopup = false;
            for (var i = 0; i < _popupCount; i++)
            {
                ref var pe = ref GetElement(_popups[i]);
                ref var pd = ref pe.Data.Popup;
                if (!pd.AutoClose) continue;

                Matrix3x2.Invert(pe.Transform, out var popupInv);
                var localMouse = Vector2.Transform(MouseWorldPosition, popupInv);
                if (pe.Rect.Contains(localMouse))
                {
                    clickInsideAutoClosePopup = true;
                    break;
                }
            }

            if (!clickInsideAutoClosePopup)
            {
                ClosePopups = true;
                _inputMousePressed = false;
                Input.ConsumeButton(InputCode.MouseLeft);
            }
        }
    }

    private static bool IsInsidePopup(int index)
    {
        for (var i = 0; i < _popupCount; i++)
        {
            ref var pe = ref GetElement(_popups[i]);
            ref var pd = ref pe.Data.Popup;
            if (!pd.Interactive) continue;
            if (IsDescendantOf(index, _popups[i]))
                return true;
        }
        return false;
    }

    private static bool IsInsideNonInteractivePopup(int offset)
    {
        for (var i = 0; i < _popupCount; i++)
        {
            ref var pe = ref GetElement(_popups[i]);
            ref var pd = ref pe.Data.Popup;
            if (!pd.Interactive && IsDescendantOf(offset, _popups[i]))
                return true;
        }
        return false;
    }

    private static bool IsDescendantOf(int offset, int ancestorOffset)
    {
        var current = offset;
        while (current != 0)
        {
            if (current == ancestorOffset)
                return true;
            ref var e = ref GetElement(current);
            current = e.Parent;
        }
        return false;
    }

    // Cursor state
    private static bool _cursorActive;
    private static Sprite? _savedCursorSprite;
    private static SystemCursor _savedSystemCursor;

    private static void HandleInput()
    {
        _inputMousePressed = Input.WasButtonPressedRaw(InputCode.MouseLeft);
        _inputMouseDown = Input.IsButtonDownRaw(InputCode.MouseLeft);
        MouseOverScene = false;

        if (_captureId != 0 && !_inputMouseDown)
        {
            _captureId = WidgetId.None;
            Input.ReleaseMouseCapture();
        }

        HandlePopupAutoClose();
        _hoveredWidget = WidgetId.None;
        FindHoveredWidget(0);
        HandleInputElement(0);
        HandleScrollableInput();
        HandleSceneInput(0);
        HandleCursor(0);
    }

    private static void HandleCursor(int offset)
    {
        int cursorOffset = -1;
        FindCursorUnderMouse(0, ref cursorOffset);

        if (cursorOffset >= 0)
        {
            if (!_cursorActive)
            {
                _savedCursorSprite = Cursor.ActiveSprite;
                _savedSystemCursor = Cursor.ActiveSystemCursor;
                _cursorActive = true;
            }

            ref var ce = ref GetElement(cursorOffset);
            ref var cd = ref ce.Data.Cursor;
            if (cd.IsSprite)
                Cursor.Set((Sprite)_assets[cd.AssetIndex]!);
            else
                Cursor.Set(cd.SystemCursor);
        }
        else if (_cursorActive)
        {
            _cursorActive = false;
            if (_savedCursorSprite != null)
                Cursor.Set(_savedCursorSprite);
            else
                Cursor.Set(_savedSystemCursor);
        }
    }

    private static void FindCursorUnderMouse(int offset, ref int foundOffset)
    {
        ref var e = ref GetElement(offset);

        if (e.Type == ElementType.Cursor)
        {
            Matrix3x2.Invert(e.Transform, out var cursorInv);
            var localMouse = Vector2.Transform(MouseWorldPosition, cursorInv);
            if (e.Rect.Contains(localMouse))
                foundOffset = offset;
        }

        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            FindCursorUnderMouse(childOffset, ref foundOffset);
            childOffset = child.NextSibling;
        }
    }

    internal static bool ScrollbarDragging => _scrollbarDragging;
    private static bool _scrollbarDragging;
    private static int _scrollbarDragWidgetId;
    private static float _scrollbarDragStartOffset;
    private static float _scrollbarDragStartMouseY;
    private static int _activeScrollWidgetId;
    private static float _lastScrollMouseY;

    private static void HandleScrollableInput()
    {
        HandleScrollbarDrag();
        HandleScrollableContentDrag();
        HandleScrollableMouseWheel();
    }

    private static void HandleScrollbarDrag()
    {
#if false
        if (_scrollbarDragging)
        {
            if (!_inputMouseDown)
            {
                _scrollbarDragging = false;
                Input.ReleaseMouseCapture();
                return;
            }

            ref var state = ref GetWidgetData<ScrollableState>(_scrollbarDragWidgetId);
            var viewportHeight = GetScrollableViewportHeight(_scrollbarDragWidgetId);
            var maxScroll = Math.Max(0, state.ContentHeight - viewportHeight);
            if (maxScroll <= 0) return;

            // Find the scrollable element to get style data
            var scrollOffset = FindScrollableOffset(_scrollbarDragWidgetId);
            if (scrollOffset < 0) return;
            ref var e = ref GetElement(scrollOffset);
            ref var d = ref GetElementData<ScrollElement>(ref e);

            var trackH = viewportHeight - d.ScrollbarPadding * 2;
            var thumbHeightRatio = viewportHeight / state.ContentHeight;
            var thumbH = Math.Max(d.ScrollbarMinThumbHeight, trackH * thumbHeightRatio);
            var availableTrackSpace = trackH - thumbH;

            if (availableTrackSpace > 0)
            {
                var mouseDeltaY = MouseWorldPosition.Y - _scrollbarDragStartMouseY;
                var scrollDelta = (mouseDeltaY / availableTrackSpace) * maxScroll;
                state.Offset = Math.Clamp(_scrollbarDragStartOffset + scrollDelta, 0, maxScroll);
            }
            return;
        }

        if (!_inputMousePressed) return;

        // Check for new scrollbar interactions
        FindScrollbarInteraction(0);
#endif
    }

    private static void FindScrollbarInteraction(int offset)
    {
#if false
        ref var e = ref GetElement(offset);

        if (e.Type == ElementType.Scroll)
        {
            ref var d = ref GetElementData<ScrollElement>(ref e);
            if (d.WidgetId > 0)
            {
                ref var state = ref GetWidgetData<ScrollableState>(d.WidgetId);
                var viewportHeight = e.Rect.Height;
                var maxScroll = Math.Max(0, state.ContentHeight - viewportHeight);

                // Check thumb hit
                if (GetScrollbarThumbRect(ref e, ref d, ref state, out var thumbRect) && thumbRect.Contains(MouseWorldPosition))
                {
                    _scrollbarDragging = true;
                    _scrollbarDragWidgetId = d.WidgetId;
                    _scrollbarDragStartOffset = state.Offset;
                    _scrollbarDragStartMouseY = MouseWorldPosition.Y;
                    Input.CaptureMouse();
                    return;
                }

                // Check track hit (page scroll)
                if (GetScrollbarTrackRect(ref e, ref d, ref state, out var trackRect) && trackRect.Contains(MouseWorldPosition))
                {
                    if (maxScroll > 0)
                    {
                        var clickRelativeY = MouseWorldPosition.Y - trackRect.Y;
                        var clickRatio = clickRelativeY / trackRect.Height;
                        var targetOffset = clickRatio * maxScroll;
                        var pageAmount = viewportHeight * 0.9f;
                        var newOffset = state.Offset < targetOffset
                            ? Math.Min(state.Offset + pageAmount, targetOffset)
                            : Math.Max(state.Offset - pageAmount, targetOffset);
                        state.Offset = Math.Clamp(newOffset, 0, maxScroll);
                    }
                    Input.ConsumeButton(InputCode.MouseLeft);
                    return;
                }
            }
        }

        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            FindScrollbarInteraction(childOffset);
            childOffset = child.NextSibling;
        }
#endif
    }

    private static bool GetScrollbarThumbRect(ref Element e, ref ScrollElement d, ref ScrollableState state, out Rect thumbRect)
    {
#if false
        thumbRect = Rect.Zero;
        var viewportHeight = e.Rect.Height;
        var maxScroll = Math.Max(0, state.ContentHeight - viewportHeight);
        if (d.ScrollbarVisibility == ScrollbarVisibility.Never || maxScroll <= 0) return false;

        var pos = Vector2.Transform(e.Rect.Position, GetTransform(ref e));
        var trackX = pos.X + e.Rect.Width - d.ScrollbarWidth - d.ScrollbarPadding;
        var trackY = pos.Y + d.ScrollbarPadding;
        var trackH = viewportHeight - d.ScrollbarPadding * 2;
        var thumbHeightRatio = viewportHeight / state.ContentHeight;
        var thumbH = Math.Max(d.ScrollbarMinThumbHeight, trackH * thumbHeightRatio);
        var scrollRatio = state.Offset / maxScroll;
        var thumbY = trackY + scrollRatio * (trackH - thumbH);

        thumbRect = new Rect(trackX, thumbY, d.ScrollbarWidth, thumbH);
#endif
            thumbRect = Rect.Zero;
        return true;
    }

    private static bool GetScrollbarTrackRect(ref Element e, ref ScrollElement d, ref ScrollableState state, out Rect trackRect)
    {
#if false
        trackRect = Rect.Zero;
        var viewportHeight = e.Rect.Height;
        var maxScroll = Math.Max(0, state.ContentHeight - viewportHeight);
        if (d.ScrollbarVisibility == ScrollbarVisibility.Never) return false;
        if (d.ScrollbarVisibility == ScrollbarVisibility.Auto && maxScroll <= 0) return false;

        var pos = Vector2.Transform(e.Rect.Position, GetTransform(ref e));
        var trackX = pos.X + e.Rect.Width - d.ScrollbarWidth - d.ScrollbarPadding;
        var trackY = pos.Y + d.ScrollbarPadding;
        var trackH = viewportHeight - d.ScrollbarPadding * 2;

        trackRect = new Rect(trackX, trackY, d.ScrollbarWidth, trackH);
#endif
        trackRect = Rect.Zero;
        return true;
    }

    private static void HandleScrollableContentDrag()
    {
#if false
        if (_scrollbarDragging) return;

        if (!_inputMouseDown)
        {
            if (_activeScrollWidgetId != 0)
                Input.ReleaseMouseCapture();
            _activeScrollWidgetId = 0;
        }
        else if (_activeScrollWidgetId != 0)
        {
            var deltaY = _lastScrollMouseY - MouseWorldPosition.Y;
            _lastScrollMouseY = MouseWorldPosition.Y;

            ref var state = ref GetWidgetData<ScrollableState>(_activeScrollWidgetId);
            var viewportHeight = GetScrollableViewportHeight(_activeScrollWidgetId);
            var maxScroll = Math.Max(0, state.ContentHeight - viewportHeight);
            state.Offset = Math.Clamp(state.Offset + deltaY, 0, maxScroll);
        }
        else if (_inputMousePressed)
        {
            FindScrollableContentDragStart(0);
        }
#endif
    }

    private static void FindScrollableContentDragStart(int offset)
    {
#if false
        ref var e = ref GetElement(offset);

        if (e.Type == ElementType.Scroll)
        {
            ref var d = ref GetElementData<ScrollElement>(ref e);
            if (d.WidgetId > 0)
            {
                ref var ws = ref _widgetStates[d.WidgetId];
                if ((ws.Flags & ElementFlags.Pressed) != 0)
                {
                    _activeScrollWidgetId = d.WidgetId;
                    _lastScrollMouseY = MouseWorldPosition.Y;
                    Input.CaptureMouse();
                    return;
                }
            }
        }

        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            FindScrollableContentDragStart(childOffset);
            childOffset = child.NextSibling;
        }
#endif
    }

    private static void HandleScrollableMouseWheel()
    {
        var scrollDelta = Input.GetAxisValue(InputCode.MouseScrollY);
        if (scrollDelta == 0) return;

        FindScrollableForWheel(0, scrollDelta);
    }

    private static bool FindScrollableForWheel(int offset, float scrollDelta)
    {
#if false
        ref var e = ref GetElement(offset);

        // Check children first (deeper scrollables take priority)
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            if (FindScrollableForWheel(childOffset, scrollDelta)) return true;
            childOffset = child.NextSibling;
        }

        if (e.Type == ElementType.Scroll)
        {
            ref var d = ref GetElementData<ScrollElement>(ref e);
            if (d.WidgetId > 0)
            {
                Matrix3x2.Invert(GetTransform(ref e), out var scrollInv);
                var localMouse = Vector2.Transform(MouseWorldPosition, scrollInv);
                if (e.Rect.Contains(localMouse))
                {
                    ref var state = ref GetWidgetData<ScrollableState>(d.WidgetId);
                    var maxScroll = Math.Max(0, state.ContentHeight - e.Rect.Height);
                    state.Offset = Math.Clamp(state.Offset - scrollDelta * d.ScrollSpeed, 0, maxScroll);
                    Input.ConsumeScroll();
                    return true;
                }
            }
        }
#endif

        return false;
    }

    private static int FindScrollableOffset(int widgetId)
    {
        return FindScrollableOffsetRecursive(0, widgetId);
    }

    private static int FindScrollableOffsetRecursive(int offset, int widgetId)
    {
#if false
        ref var e = ref GetElement(offset);
        if (e.Type == ElementType.Scroll)
        {
            ref var d = ref GetElementData<ScrollElement>(ref e);
            if (d.WidgetId == widgetId) return offset;
        }
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            var found = FindScrollableOffsetRecursive(childOffset, widgetId);
            if (found >= 0) return found;
            childOffset = child.NextSibling;
        }
#endif

        return -1;
    }

    private static float GetScrollableViewportHeight(int widgetId)
    {
        var offset = FindScrollableOffset(widgetId);
        if (offset < 0) return 0;
        ref var e = ref GetElement(offset);
        return e.Rect.Height;
    }

    internal static float GetScrollOffset(WidgetId id)
    {
#if false
        if (widgetId <= 0) return 0;
        ref var state = ref GetWidgetData<ScrollableState>(widgetId);
        return state.Offset;
#endif
        return 0;
    }

    internal static void SetScrollOffset(WidgetId id, float offset)
    {
        //if (widgetId <= 0) return;
        //ref var state = ref GetWidgetData<ScrollableState>(widgetId);
        //state.Offset = offset;
    }

    private static void HandleTrackInput(ref Element e)
    {
        ref var d = ref e.Data.Track;
        var widgetId = d.Id;

        if (IsDown(widgetId) && !HasCaptureById(widgetId))
            SetCaptureById(widgetId);

        if (!HasCaptureById(widgetId))
            return;

        var worldRect = GetWidgetWorldRect(widgetId);
        if (d.Vertical)
        {
            var trackSize = worldRect.Height;
            if (trackSize <= 0) return;
            var thumbHalf = d.ThumbSize / 2;
            var usable = trackSize - d.ThumbSize;
            if (usable <= 0) return;
            var norm = MathEx.Clamp01((MouseWorldPosition.Y - worldRect.Y - thumbHalf) / usable);
            ref var state = ref GetWidgetState<TrackState>(widgetId);
            state.Value = norm;
        }
        else
        {
            var trackSize = worldRect.Width;
            if (trackSize <= 0) return;
            var thumbHalf = d.ThumbSize / 2;
            var usable = trackSize - d.ThumbSize;
            if (usable <= 0) return;
            var norm = MathEx.Clamp01((MouseWorldPosition.X - worldRect.X - thumbHalf) / usable);
            ref var state = ref GetWidgetState<TrackState>(widgetId);
            state.Value = norm;
        }
    }

    private static void HandleSceneInput(int offset)
    {
        ref var e = ref GetElement(offset);

        if (e.Type == ElementType.Scene)
        {
            Matrix3x2.Invert(e.Transform, out var sceneInv);
            var localMouse = Vector2.Transform(MouseWorldPosition, sceneInv);
            if (e.Rect.Contains(localMouse))
                MouseOverScene = true;
        }

        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            HandleSceneInput(childOffset);
            childOffset = child.NextSibling;
        }
    }

    private static void FindHoveredWidget(int index)
    {
        ref var e = ref GetElement(index);

        if (e.Type == ElementType.Widget)
        {
            if (_activePopupCount > 0 && !IsInsidePopup(e.Index))
                return;

            if (IsInsideNonInteractivePopup(e.Index))
                return;

            Matrix3x2.Invert(e.Transform, out var inv);
            var localMouse = Vector2.Transform(MouseWorldPosition, inv);
            if (e.Rect.Contains(localMouse))
                _hoveredWidget = e.Data.Widget.Id;
        }

        var childIndex = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childIndex);
            FindHoveredWidget(childIndex);
            childIndex = child.NextSibling;
        }
    }

    private static void HandleWidgetInput(ref Element e)
    {
        ref var d = ref e.Data.Widget;
        ref var state = ref GetWidgetState(d.Id);
        var prevFlags = state.Flags;
        state.Flags = WidgetFlags.None;

        if (_activePopupCount > 0 && !IsInsidePopup(e.Index))
            return;

        if (IsInsideNonInteractivePopup(e.Index))
            return;

        var isHovered = _hoveredWidget == d.Id;

        if (isHovered)
            state.Flags |= WidgetFlags.Hovered;

        var isCaptured = _captureId != 0 && _captureId == d.Id;

        if (isHovered && _inputMousePressed && (_captureId == 0 || _captureId == d.Id))
            state.Flags |= WidgetFlags.Pressed;

        if (isCaptured ? _inputMouseDown : (isHovered && _inputMouseDown))
            state.Flags |= WidgetFlags.Down;

        if (_hotId == d.Id)
            state.Flags |= WidgetFlags.Hot;

        if ((state.Flags & WidgetFlags.Hovered) != (prevFlags & WidgetFlags.Hovered))
            state.Flags |= WidgetFlags.HoverChanged;
    }

    private static void HandleInputElement(int index)
    {
        ref var e = ref GetElement(index);

        if (e.Type == ElementType.Widget)
            HandleWidgetInput(ref e);

        if (e.Type == ElementType.Track)
            HandleTrackInput(ref e);

        if (e.Type == ElementType.EditableText)
            HandleEditableTextInput(ref e);

        var childIndex = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childIndex);
            HandleInputElement(childIndex);
            childIndex = child.NextSibling;
        }
    }
}
