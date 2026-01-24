//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_UI_DEBUG

using System.Diagnostics;
using System.Numerics;

namespace NoZ;

public static partial class UI
{
    // Input handling
    private static void HandleInput()
    {
        var mouse = Camera!.ScreenToWorld(Input.MousePosition);
        var mouseLeftPressed = Input.WasButtonPressedRaw(InputCode.MouseLeft);
        var buttonDown = Input.IsButtonDownRaw(InputCode.MouseLeft);

        _mousePosition = mouse;
        _mouseLeftPressed = mouseLeftPressed;

        // Update canvas world bounds and find hot canvas (topmost under mouse)
        _hotCanvasId = ElementIdNone;
        for (var c = 0; c < _activeCanvasCount; c++)
        {
            var canvasId = _activeCanvasIds[c];
            ref var cs = ref _canvasStates[canvasId];
            ref var canvasElement = ref _elements[cs.ElementIndex];
            var pos = Vector2.Transform(Vector2.Zero, canvasElement.LocalToWorld);
            cs.WorldBounds = new Rect(pos.X, pos.Y, canvasElement.Rect.Width, canvasElement.Rect.Height);

            // Later canvases are on top, so last one containing mouse wins
            if (cs.WorldBounds.Contains(mouse))
                _hotCanvasId = canvasId;
        }

        LogUI("Input:", values: [("HotCanvasId", _hotCanvasId, true)]);

        // Handle popup close detection
        _closePopups = false;
        _elementWasPressed = false;
        if (mouseLeftPressed && _popupCount > 0)
        {
            var clickInsidePopup = false;
            for (var i = 0; i < _popupCount; i++)
            {
                ref var popup = ref _elements[_popups[i]];
                var localMouse = Vector2.Transform(mouse, popup.WorldToLocal);
                if (new Rect(0, 0, popup.Rect.Width, popup.Rect.Height).Contains(localMouse))
                {
                    clickInsidePopup = true;
                    break;
                }
            }

            if (!clickInsidePopup)
            {
                _closePopups = true;
                return;
            }
        }

        // Process each canvas independently for hover, but only hot canvas gets press/down
        for (var c = 0; c < _activeCanvasCount; c++)
        {
            var canvasId = _activeCanvasIds[c];
            var isHotCanvas = canvasId == _hotCanvasId;
            ProcessCanvasInput(canvasId, mouse, mouseLeftPressed, buttonDown, isHotCanvas);
        }

        // Consume MouseLeft if any UI element was pressed
        if (_elementWasPressed)
            Input.ConsumeButton(InputCode.MouseLeft);

        // Handle scrollable drag
        HandleScrollableDrag(mouse, buttonDown, mouseLeftPressed);

        // Handle mouse wheel scroll
        HandleMouseWheelScroll(mouse);
    }

    private static void ProcessCanvasInput(byte canvasId, Vector2 mouse, bool mouseLeftPressed, bool buttonDown, bool isHotCanvas)
    {
        ref var canvasState = ref _canvasStates[canvasId];
        ref var canvas = ref _elements[canvasState.ElementIndex];
        if (canvasState.ElementStates == null) return;

        var focusElementPressed = false;

        // Iterate elements belonging to this canvas in reverse order (topmost first)
        for (var elementIndex=canvas.NextSiblingIndex - 1; elementIndex > canvas.Index; elementIndex--)
        {
            ref var e = ref _elements[elementIndex];
            Debug.Assert(e.CanvasId == canvas.Id);
            if (e.Id == ElementIdNone) continue;

            if (e.Type == ElementType.TextBox)
            {
                HandleTextBoxInput(ref e);
            }


            ref var state = ref canvasState.ElementStates[e.Id];
            state.Rect = e.Rect;
            var localMouse = Vector2.Transform(mouse, e.WorldToLocal);
            var mouseOver = new Rect(0, 0, e.Rect.Width, e.Rect.Height).Contains(localMouse);

            // HOVER: Independent per canvas - all canvases track hover
            if (mouseOver) 
                state.Flags |= ElementFlags.Hovered;
            else
                state.Flags &= ~ElementFlags.Hovered;

            // PRESSED: Only hot canvas receives press events
            if (isHotCanvas && mouseOver && mouseLeftPressed && !focusElementPressed && e.Type != ElementType.Canvas)
            {
                state.Flags |= ElementFlags.Pressed;
                focusElementPressed = true;
                _elementWasPressed = true;
                _pendingFocusId = e.Id;
                _pendingFocusCanvasId = canvasId;
            }
            else
            {
                state.Flags &= ~ElementFlags.Pressed;
            }

            // DOWN: Only hot canvas
            if (isHotCanvas && mouseOver && buttonDown)
                state.Flags |= ElementFlags.Down;
            else
                state.Flags &= ~ElementFlags.Down;
        }
    }

    private static void HandleScrollableDrag(Vector2 mouse, bool buttonDown, bool mouseLeftPressed)
    {
        if (!buttonDown)
        {
            _activeScrollId = ElementIdNone;
        }
        else if (_activeScrollId != ElementIdNone)
        {
            var deltaY = _lastScrollMouseY - mouse.Y;
            _lastScrollMouseY = mouse.Y;

            for (var i = 0; i < _elementCount; i++)
            {
                ref var e = ref _elements[i];
                if (e.Type == ElementType.Scrollable && e.Id == _activeScrollId && e.CanvasId != ElementIdNone)
                {
                    ref var cs = ref _canvasStates[e.CanvasId];
                    if (cs.ElementStates == null) continue;
                    ref var state = ref cs.ElementStates[e.Id];

                    var newOffset = e.Data.Scrollable.Offset + deltaY;
                    var maxScroll = Math.Max(0, e.Data.Scrollable.ContentHeight - e.Rect.Height);
                    newOffset = Math.Clamp(newOffset, 0, maxScroll);

                    e.Data.Scrollable.Offset = newOffset;
                    state.ScrollOffset = newOffset;
                    break;
                }
            }
        }
        else if (mouseLeftPressed)
        {
            for (var i = _elementCount; i > 0; i--)
            {
                ref var e = ref _elements[i - 1];
                if (e.Type == ElementType.Scrollable && e.Id != ElementIdNone && e.CanvasId == _hotCanvasId)
                {
                    ref var cs = ref _canvasStates[e.CanvasId];
                    if (cs.ElementStates == null) continue;
                    ref var state = ref cs.ElementStates[e.Id];
                    if ((state.Flags & ElementFlags.Pressed) != 0)
                    {
                        _activeScrollId = e.Id;
                        _lastScrollMouseY = mouse.Y;
                        break;
                    }
                }
            }
        }
    }

    private static void HandleMouseWheelScroll(Vector2 mouse)
    {
        var scrollDelta = Input.GetAxisValue(InputCode.MouseScrollY);
        if (scrollDelta == 0) return;

        for (var i = _elementCount; i > 0; i--)
        {
            ref var e = ref _elements[i - 1];
            if (e.Type != ElementType.Scrollable || e.Id == ElementIdNone || e.CanvasId == ElementIdNone)
                continue;

            var localMouse = Vector2.Transform(mouse, e.WorldToLocal);
            if (!new Rect(0, 0, e.Rect.Width, e.Rect.Height).Contains(localMouse))
                continue;

            ref var cs = ref _canvasStates[e.CanvasId];
            if (cs.ElementStates == null) continue;
            ref var state = ref cs.ElementStates[e.Id];

            var scrollSpeed = 30f;
            var newOffset = e.Data.Scrollable.Offset - scrollDelta * scrollSpeed;
            var maxScroll = Math.Max(0, e.Data.Scrollable.ContentHeight - e.Rect.Height);
            newOffset = Math.Clamp(newOffset, 0, maxScroll);

            e.Data.Scrollable.Offset = newOffset;
            state.ScrollOffset = newOffset;
            break;
        }
    }
}
