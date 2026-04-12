//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

public static unsafe partial class ElementTree
{
    private enum UndoEditKind : byte { None, Insert, Delete }

    private class EditableTextHelper
    {
        private const int MaxUndo = 64;
        private const float CoalesceTimeout = 0.5f;

        private struct UndoSnapshot
        {
            public string Text;
            public int CursorIndex;
            public int SelectionStart;
        }

        private readonly UndoSnapshot[] _undo = new UndoSnapshot[MaxUndo];
        private int _undoCount;
        private int _undoCurrent;
        private UndoEditKind _lastEditKind;
        private float _lastEditTime;

        public bool CanUndo => _undoCurrent > 0;
        public bool CanRedo => _undoCurrent < _undoCount - 1;

        public void Clear()
        {
            _undoCount = 0;
            _undoCurrent = 0;
            _lastEditKind = UndoEditKind.None;
            _lastEditTime = 0;
        }

        public void Record(string text, int cursorIndex, int selectionStart, UndoEditKind kind = UndoEditKind.None)
        {
            var now = Time.TotalTime;
            var shouldCoalesce = kind != UndoEditKind.None
                && kind == _lastEditKind
                && (now - _lastEditTime) < CoalesceTimeout
                && _undoCount > 0
                && _undoCurrent == _undoCount - 1;

            _lastEditKind = kind;
            _lastEditTime = now;

            if (shouldCoalesce)
            {
                // Update the current entry in place
                _undo[_undoCurrent] = new UndoSnapshot
                {
                    Text = text,
                    CursorIndex = cursorIndex,
                    SelectionStart = selectionStart
                };
                return;
            }

            // Discard any redo history beyond current position
            _undoCount = _undoCurrent + 1;

            if (_undoCount >= MaxUndo)
            {
                // Shift everything down to make room
                Array.Copy(_undo, 1, _undo, 0, MaxUndo - 1);
                _undoCount = MaxUndo - 1;
            }

            _undo[_undoCount] = new UndoSnapshot
            {
                Text = text,
                CursorIndex = cursorIndex,
                SelectionStart = selectionStart
            };
            _undoCurrent = _undoCount;
            _undoCount++;
        }

        public void Undo(ref EditableTextState state)
        {
            if (!CanUndo) return;
            _undoCurrent--;
            _lastEditKind = UndoEditKind.None;
            ref var s = ref _undo[_undoCurrent];
            state.EditText = AllocString(s.Text.AsSpan());
            state.CursorIndex = s.CursorIndex;
            state.SelectionStart = s.SelectionStart;
            state.TextHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
        }

        public void Redo(ref EditableTextState state)
        {
            if (!CanRedo) return;
            _undoCurrent++;
            _lastEditKind = UndoEditKind.None;
            ref var s = ref _undo[_undoCurrent];
            state.EditText = AllocString(s.Text.AsSpan());
            state.CursorIndex = s.CursorIndex;
            state.SelectionStart = s.SelectionStart;
            state.TextHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
        }
    }

    private static readonly EditableTextHelper _editableTextHelper = new();

    public static string EditableText(
        ref EditableTextState state,
        string value,
        Font font,
        float fontSize,
        Color textColor,
        Color cursorColor,
        Color selectionColor,
        string? placeholder,
        Color placeholderColor,
        bool multiLine = false,
        bool commitOnEnter = false,
        InputScope scope = default)
    {
        ref var e = ref BeginElement(ElementType.EditableText);
        ref var d = ref e.Data.EditableText;

        // Store state pointer for input phase
        d.State = (EditableTextState*)Unsafe.AsPointer(ref state);

        // Handle commit from previous frame's focus exit
        if (state.FocusExited != 0)
        {
            if (state.WasCancelled == 0)
            {
                var finalHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
                if (finalHash != state.PrevTextHash)
                    value = new string(state.EditText.AsReadOnlySpan());
            }
            state.Focused = 0;
            state.FocusExited = 0;
            state.WasCancelled = 0;
        }

        // Detect external defocus: focused but no longer hot (ClearHot was called)
        var focused = state.Focused != 0;
        if (focused && _hotId != _currentWidget)
        {
            // External defocus — don't commit. Value was already applied each frame.
            state.Focused = 0;
            state.WasCancelled = 0;
            Application.Platform.HideTextbox();
            focused = false;
        }

        // Populate element for layout and draw
        if (focused)
        {
            // Re-alloc editing buffer from previous frame's (still valid) pool into current pool
            state.EditText = AllocString(state.EditText.AsReadOnlySpan());
            d.Text = state.EditText;
            value = new string(state.EditText.AsReadOnlySpan());
        }
        else
        {
            d.Text = AllocString(value.AsSpan());
        }

        d.FontSize = fontSize;
        d.TextColor = textColor;
        d.CursorColor = cursorColor;
        d.SelectionColor = selectionColor;
        d.PlaceholderColor = placeholderColor;
        d.Placeholder = placeholder != null ? AllocString(placeholder.AsSpan()) : default;
        d.MultiLine = multiLine;
        d.CommitOnEnter = commitOnEnter;
        d.Focused = focused;
        d.Font = AddObject(font);
        d.CursorIndex = state.CursorIndex;
        d.SelectionStart = state.SelectionStart;
        d.BlinkTime = state.BlinkTime;
        d.Scope = scope;

        EndElement(ElementType.EditableText);

        return value;
    }

    internal static void HandleEditableTextInput(ref Element e)
    {
        ref var d = ref e.Data.EditableText;
        ref var state = ref *d.State;
        var font = (Font)_assets[d.Font]!;
        var focused = state.Focused != 0;

        // Block input when inside an active popup
        if (_activePopupCount > 0 && !IsInsidePopup(e.Index))
            return;

        // Hit test against parent widget rect (covers padding/border area)
        var widgetId = FindParentWidgetId(e.Index);
        ref var widgetEl = ref GetWidget(widgetId);
        Matrix3x2.Invert(widgetEl.Transform, out var widgetInv);
        var widgetMouse = Vector2.Transform(MouseWorldPosition, widgetInv);
        var mouseInside = widgetEl.Rect.Contains(widgetMouse);

        // Local mouse relative to EditableText element (for cursor positioning)
        Matrix3x2.Invert(e.Transform, out var inv);
        var localMouse = Vector2.Transform(MouseWorldPosition, inv);

        // Focus enter (only if this widget is the deepest hovered — prevents overlapping widgets from stealing focus)
        if (_inputMousePressed && mouseInside && !focused && _hoveredWidget == widgetId)
        {
            _hotId = widgetId;
            state.Focused = 1;
            state.JustFocused = 1;
            state.EditText = AllocString(d.Text.AsReadOnlySpan());
            state.PrevTextHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
            state.TextHash = state.PrevTextHash;

            // Initialize undo history with the original text
            _editableTextHelper.Clear();
            var initialText = new string(state.EditText.AsReadOnlySpan());
            _editableTextHelper.Record(initialText, 0, initialText.Length);

            // Select all on focus enter — drag will override if mouse moves to a different char
            var clickIndex = HitTestCharIndex(state.EditText.AsReadOnlySpan(), font, d.FontSize,
                d.MultiLine, e.Rect.Width, localMouse.X - e.Rect.X, localMouse.Y - e.Rect.Y);
            state.FocusClickIndex = clickIndex;
            state.SelectionStart = 0;
            state.CursorIndex = state.EditText.AsReadOnlySpan().Length;
            state.BlinkTime = 0;

            d.Focused = true;
            d.Text = state.EditText;
            d.CursorIndex = state.CursorIndex;
            d.SelectionStart = state.SelectionStart;

            Application.Platform.ShowTextbox(
                e.Rect,
                new string(d.Text.AsReadOnlySpan()),
                new Platform.NativeTextboxStyle { FontSize = (int)d.FontSize });

            return;
        }

        if (!focused) return;

        // Click outside — defocus and commit
        if (_inputMousePressed && !mouseInside)
        {
            state.FocusExited = 1;
            state.Focused = 0;
            Application.Platform.HideTextbox();
            ClearHot(widgetId);
            return;
        }

        // Mouse click to place cursor / drag to select
        if (_inputMouseDown && mouseInside)
        {
            var charIndex = HitTestCharIndex(state.EditText.AsReadOnlySpan(), font, d.FontSize,
                d.MultiLine, e.Rect.Width, localMouse.X - e.Rect.X, localMouse.Y - e.Rect.Y);

            if (state.JustFocused != 0)
            {
                // Only switch from select-all to drag-selection if mouse moved to a different char
                if (charIndex != state.FocusClickIndex)
                {
                    state.SelectionStart = state.FocusClickIndex;
                    state.CursorIndex = charIndex;
                    state.JustFocused = 0;
                }
                // Otherwise keep select-all
            }
            else if (_inputMousePressed)
            {
                // Fresh click while already focused — place cursor and clear selection
                state.CursorIndex = charIndex;
                state.SelectionStart = charIndex;
                state.BlinkTime = 0;
            }
            else
            {
                // Dragging — extend selection
                state.CursorIndex = charIndex;
            }
        }
        else
        {
            state.JustFocused = 0;
        }

        // Advance blink timer
        state.BlinkTime += Time.DeltaTime;

        // Keyboard and text input
        var prevCursor = state.CursorIndex;
        var prevSelection = state.SelectionStart;
        var prevHash = state.TextHash;
        HandleTextInput(ref state, font, d.FontSize, d.MultiLine, e.Rect.Width, d.Scope, d.CommitOnEnter, e.Index, widgetId);

        // Reset blink timer when cursor moves or text changes
        if (state.CursorIndex != prevCursor || state.SelectionStart != prevSelection || state.TextHash != prevHash)
            state.BlinkTime = 0;

        // Update element for draw
        d.Text = state.EditText;
        d.CursorIndex = state.CursorIndex;
        d.SelectionStart = state.SelectionStart;
        d.BlinkTime = state.BlinkTime;
        d.Focused = state.Focused != 0;
    }

    private static void RequestTabNavigation(int currentElementIndex, bool reverse)
    {
        var count = _elements.Length;
        var step = reverse ? count - 1 : 1;
        var i = (currentElementIndex + step) % count;
        for (int n = 0; n < count - 1; n++, i = (i + step) % count)
        {
            if (GetElement(i).Type == ElementType.EditableText)
            {
                _tabNavigationTarget = i;
                return;
            }
        }
    }

    private static void FocusEditableText(int elementIndex)
    {
        ref var e = ref GetElement(elementIndex);
        ref var d = ref e.Data.EditableText;
        ref var state = ref *d.State;
        var widgetId = FindParentWidgetId(elementIndex);
        if (widgetId == WidgetId.None) return;

        SetHot(widgetId);
        _prevHotId = widgetId;
        state.Focused = 1;
        state.JustFocused = 1;
        state.EditText = AllocString(d.Text.AsReadOnlySpan());
        state.PrevTextHash = string.GetHashCode(state.EditText.AsReadOnlySpan());
        state.TextHash = state.PrevTextHash;
        state.SelectionStart = 0;
        state.CursorIndex = state.EditText.AsReadOnlySpan().Length;
        state.BlinkTime = 0;
        _editableTextHelper.Clear();
        var initialText = new string(state.EditText.AsReadOnlySpan());
        _editableTextHelper.Record(initialText, 0, initialText.Length);
    }

    private static WidgetId FindParentWidgetId(int elementIndex)
    {
        var current = elementIndex;
        while (current != 0)
        {
            ref var el = ref GetElement(current);
            if (el.Type == ElementType.Widget)
                return el.Data.Widget.Id;
            current = el.Parent;
        }
        return WidgetId.None;
    }

    private static void HandleTextInput(
        ref EditableTextState state,
        Font font,
        float fontSize,
        bool multiLine,
        float contentWidth,
        InputScope scope,
        bool commitOnEnter,
        int elementIndex,
        WidgetId widgetId)
    {
        var ctrl = Input.IsCtrlDown(scope);
        var shift = Input.IsShiftDown(scope);
        ref var text = ref state.EditText;

        // Escape — cancel
        if (Input.WasButtonPressed(InputCode.KeyEscape, true, scope))
        {
            state.WasCancelled = 1;
            state.FocusExited = 1;
            state.Focused = 0;
            Application.Platform.HideTextbox();
            ClearHot(widgetId);
            return;
        }

        // Tab / Shift-Tab — commit and navigate to next/prev focusable widget
        if (Input.WasButtonPressed(InputCode.KeyTab, true, scope))
        {
            var reverse = Input.IsShiftDown(scope);
            state.FocusExited = 1;
            state.Focused = 0;
            Application.Platform.HideTextbox();
            RequestTabNavigation(elementIndex, reverse);
            if (_tabNavigationTarget < 0)
                ClearHot(widgetId);
            Input.ConsumeButton(InputCode.KeyTab);
            return;
        }

        // Enter
        if (Input.WasButtonPressed(InputCode.KeyEnter, true, scope))
        {
            if (multiLine && !commitOnEnter)
            {
                RemoveSelected(ref state);
                text = InsertText(text.AsReadOnlySpan(), state.CursorIndex, "\n");
                state.CursorIndex++;
                state.SelectionStart = state.CursorIndex;
                state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
                _editableTextHelper.Record(new string(text.AsReadOnlySpan()), state.CursorIndex, state.SelectionStart);
            }
            else
            {
                state.FocusExited = 1;
                state.Focused = 0;
                Application.Platform.HideTextbox();
                ClearHot(widgetId);
            }
            return;
        }

        // Ctrl+A — select all
        if (ctrl && Input.WasButtonPressed(InputCode.KeyA, true, scope))
        {
            state.SelectionStart = 0;
            state.CursorIndex = text.AsReadOnlySpan().Length;
            return;
        }

        // Ctrl+Z — undo
        if (ctrl && !shift && Input.WasButtonPressed(InputCode.KeyZ, true, scope))
        {
            _editableTextHelper.Undo(ref state);
            return;
        }

        // Ctrl+Y or Ctrl+Shift+Z — redo
        if ((ctrl && Input.WasButtonPressed(InputCode.KeyY, true, scope)) ||
            (ctrl && shift && Input.WasButtonPressed(InputCode.KeyZ, true, scope)))
        {
            _editableTextHelper.Redo(ref state);
            return;
        }

        // Ctrl+C — copy
        if (ctrl && Input.WasButtonPressed(InputCode.KeyC, true, scope))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                var editText = text.AsReadOnlySpan();
                var selStart = Math.Min(state.CursorIndex, state.SelectionStart);
                var selEnd = Math.Max(state.CursorIndex, state.SelectionStart);
                Application.Platform.SetClipboardText(new string(editText[selStart..selEnd]));
            }
            return;
        }

        // Ctrl+X — cut
        if (ctrl && Input.WasButtonPressed(InputCode.KeyX, true, scope))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                var editText = text.AsReadOnlySpan();
                var selStart = Math.Min(state.CursorIndex, state.SelectionStart);
                var selEnd = Math.Max(state.CursorIndex, state.SelectionStart);
                Application.Platform.SetClipboardText(new string(editText[selStart..selEnd]));
                RemoveSelected(ref state);
                state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
                _editableTextHelper.Record(new string(text.AsReadOnlySpan()), state.CursorIndex, state.SelectionStart);
            }
            return;
        }

        // Ctrl+V — paste
        if (ctrl && Input.WasButtonPressed(InputCode.KeyV, true, scope))
        {
            var clipboard = Application.Platform.GetClipboardText();
            if (clipboard != null && clipboard.Length > 0)
            {
                RemoveSelected(ref state);
                text = InsertText(text.AsReadOnlySpan(), state.CursorIndex, clipboard);
                state.CursorIndex += clipboard.Length;
                state.SelectionStart = state.CursorIndex;
                state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
                _editableTextHelper.Record(new string(text.AsReadOnlySpan()), state.CursorIndex, state.SelectionStart);
            }
            return;
        }

        // Backspace
        if (Input.WasButtonPressed(InputCode.KeyBackspace, true, scope))
        {
            var hadSelection = state.CursorIndex != state.SelectionStart;
            if (hadSelection)
            {
                RemoveSelected(ref state);
            }
            else if (state.CursorIndex > 0)
            {
                text = RemoveText(text.AsReadOnlySpan(), state.CursorIndex - 1, 1);
                state.CursorIndex--;
                state.SelectionStart = state.CursorIndex;
            }
            state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
            _editableTextHelper.Record(new string(text.AsReadOnlySpan()), state.CursorIndex, state.SelectionStart,
                hadSelection ? UndoEditKind.None : UndoEditKind.Delete);
            return;
        }

        // Delete
        if (Input.WasButtonPressed(InputCode.KeyDelete, true, scope))
        {
            var hadSelection = state.CursorIndex != state.SelectionStart;
            if (hadSelection)
            {
                RemoveSelected(ref state);
            }
            else if (state.CursorIndex < text.AsReadOnlySpan().Length)
            {
                text = RemoveText(text.AsReadOnlySpan(), state.CursorIndex, 1);
            }
            state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
            _editableTextHelper.Record(new string(text.AsReadOnlySpan()), state.CursorIndex, state.SelectionStart,
                hadSelection ? UndoEditKind.None : UndoEditKind.Delete);
            return;
        }

        // Left arrow (Ctrl = word boundary)
        if (Input.WasButtonPressed(InputCode.KeyLeft, true, scope))
        {
            if (ctrl)
                state.CursorIndex = FindPreviousWordBoundary(text.AsReadOnlySpan(), state.CursorIndex);
            else if (state.CursorIndex > 0)
                state.CursorIndex--;
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            return;
        }

        // Right arrow (Ctrl = word boundary)
        if (Input.WasButtonPressed(InputCode.KeyRight, true, scope))
        {
            if (ctrl)
                state.CursorIndex = FindNextWordBoundary(text.AsReadOnlySpan(), state.CursorIndex);
            else if (state.CursorIndex < text.AsReadOnlySpan().Length)
                state.CursorIndex++;
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            return;
        }

        // Up arrow (multiline)
        if (multiLine && Input.WasButtonPressed(InputCode.KeyUp, true, scope))
        {
            MoveCursorVertically(ref state, font, fontSize, contentWidth, -1);
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            return;
        }

        // Down arrow (multiline)
        if (multiLine && Input.WasButtonPressed(InputCode.KeyDown, true, scope))
        {
            MoveCursorVertically(ref state, font, fontSize, contentWidth, 1);
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            return;
        }

        // Home — start of line (multiline) or start of text; Ctrl+Home always start of text
        if (Input.WasButtonPressed(InputCode.KeyHome, true, scope))
        {
            if (multiLine && !ctrl)
                state.CursorIndex = GetCurrentLineStart(text.AsReadOnlySpan(), font, fontSize, contentWidth, state.CursorIndex);
            else
                state.CursorIndex = 0;
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            return;
        }

        // End — end of line (multiline) or end of text; Ctrl+End always end of text
        if (Input.WasButtonPressed(InputCode.KeyEnd, true, scope))
        {
            if (multiLine && !ctrl)
                state.CursorIndex = GetCurrentLineEnd(text.AsReadOnlySpan(), font, fontSize, contentWidth, state.CursorIndex);
            else
                state.CursorIndex = text.AsReadOnlySpan().Length;
            if (!shift)
                state.SelectionStart = state.CursorIndex;
            return;
        }

        // Text input (typed characters)
        var textInput = Input.GetTextInput(scope);
        if (textInput.Length > 0)
        {
            RemoveSelected(ref state);
            text = InsertText(text.AsReadOnlySpan(), state.CursorIndex, textInput);
            state.CursorIndex += textInput.Length;
            state.SelectionStart = state.CursorIndex;
            state.TextHash = string.GetHashCode(text.AsReadOnlySpan());
            _editableTextHelper.Record(new string(text.AsReadOnlySpan()), state.CursorIndex, state.SelectionStart, UndoEditKind.Insert);
        }
    }

    private static void RemoveSelected(ref EditableTextState state)
    {
        if (state.CursorIndex == state.SelectionStart) return;
        var start = Math.Min(state.CursorIndex, state.SelectionStart);
        var length = Math.Abs(state.CursorIndex - state.SelectionStart);
        state.EditText = RemoveText(state.EditText.AsReadOnlySpan(), start, length);
        state.CursorIndex = start;
        state.SelectionStart = start;
    }

    private static float FitEditableTextAxis(ref Element e, int axis)
    {
        ref var d = ref e.Data.EditableText;
        var font = (Font)_assets[d.Font]!;
        var text = d.Text.AsReadOnlySpan();
        var lineHeight = font.LineHeight * d.FontSize;

        if (axis == 1 && d.MultiLine && e.Rect.Width > 0)
            return Math.Max(TextRender.MeasureWrapped(text, font, d.FontSize, e.Rect.Width).Y, lineHeight);

        if (axis == 1)
            return Math.Max(TextRender.Measure(text, font, d.FontSize).Y, lineHeight);

        return TextRender.Measure(text, font, d.FontSize).X;
    }

    private static float LayoutEditableTextAxis(ref Element e, int axis, float available)
    {
        ref var d = ref e.Data.EditableText;
        var font = (Font)_assets[d.Font]!;
        var text = d.Text.AsReadOnlySpan();
        var lineHeight = font.LineHeight * d.FontSize;

        if (available > 0)
            return Math.Max(available, lineHeight);

        if (axis == 1)
            return Math.Max(TextRender.Measure(text, font, d.FontSize).Y, lineHeight);

        return TextRender.Measure(text, font, d.FontSize).X;
    }

    private static void DrawEditableText(ref Element e)
    {
        ref var d = ref e.Data.EditableText;
        ref var t = ref e.Transform;
        var font = (Font)_assets[d.Font]!;
        var text = d.Text.AsReadOnlySpan();
        var fontSize = d.FontSize;

        var focused = d.Focused;
        var lineHeight = font.LineHeight * fontSize;

        // selection (drawn before text so text is visible on top)
        if (focused && d.CursorIndex != d.SelectionStart)
        {
            var selStart = Math.Min(d.CursorIndex, d.SelectionStart);
            var selEnd = Math.Max(d.CursorIndex, d.SelectionStart);

            if (d.MultiLine && e.Rect.Width > 0)
            {
                DrawMultilineSelection(ref e, ref d, font, text, fontSize, lineHeight, selStart, selEnd);
            }
            else
            {
                var x0 = MeasureTextWidth(text[..selStart], font, fontSize);
                var x1 = MeasureTextWidth(text[..selEnd], font, fontSize);
                var selY = e.Rect.Y + (e.Rect.Height - lineHeight) * 0.5f;
                var selRect = new Rect(e.Rect.X + x0, selY, x1 - x0, lineHeight);
                DrawTexturedRect(selRect, t, null, ApplyOpacity(d.SelectionColor));
            }
        }

        // text or placeholder
        var showPlaceholder = text.Length == 0 && !focused && d.Placeholder.Length > 0;
        var displayText = showPlaceholder ? d.Placeholder.AsReadOnlySpan() : text;
        var displayColor = showPlaceholder ? d.PlaceholderColor : d.TextColor;

        if (displayText.Length > 0)
        {
            if (d.MultiLine && e.Rect.Width > 0)
            {
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position) * t;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(displayColor));
                    Graphics.SetTransform(transform);
                    TextRender.DrawWrapped(displayText, font, fontSize, e.Rect.Width,
                        e.Rect.Width, 0f, e.Rect.Height);
                }
            }
            else
            {
                var textOffset = GetTextOffset(displayText, font, fontSize, e.Rect.Size, Align.Min, Align.Center);
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position + textOffset) * t;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(displayColor));
                    Graphics.SetTransform(transform);
                    TextRender.Draw(displayText, font, fontSize);
                }
            }
        }

        if (!focused) return;

        // cursor (only when no selection)
        if (d.CursorIndex == d.SelectionStart && d.BlinkTime % 1.0f < 0.5f)
        {
            float cursorX, cursorY;

            if (d.MultiLine && e.Rect.Width > 0)
            {
                GetCursorPositionWrapped(text, font, fontSize, e.Rect.Width, d.CursorIndex, out cursorX, out cursorY);
            }
            else
            {
                cursorX = MeasureTextWidth(text[..d.CursorIndex], font, fontSize);
                cursorY = (e.Rect.Height - lineHeight) * 0.5f;
            }

            var cursorRect = new Rect(e.Rect.X + cursorX, e.Rect.Y + cursorY, 1.5f, lineHeight);
            DrawTexturedRect(cursorRect, t, null, ApplyOpacity(d.CursorColor));
        }
    }

    private static void DrawMultilineSelection(
        ref Element e,
        ref EditableTextElement d,
        Font font,
        ReadOnlySpan<char> text,
        float fontSize,
        float lineHeight,
        int selStart,
        int selEnd)
    {
        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[64];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, e.Rect.Width, 0, lines);
        if (lineCount == 0) return;

        var leadingOffset = font.InternalLeading * 0.5f * fontSize;

        for (var i = 0; i < lineCount; i++)
        {
            var line = lines[i];
            // Actual line end includes trailing spaces (up to next line start or text end)
            var actualEnd = i + 1 < lineCount ? lines[i + 1].Start : text.Length;
            // Exclude newline character at the boundary
            if (actualEnd > line.Start && actualEnd <= text.Length && text[actualEnd - 1] == '\n')
                actualEnd--;

            if (actualEnd <= selStart || line.Start >= selEnd) continue;

            var lineSelStart = Math.Max(selStart, line.Start) - line.Start;
            var lineSelEnd = Math.Min(selEnd, actualEnd) - line.Start;
            var lineText = text[line.Start..actualEnd];

            var x0 = MeasureTextWidth(lineText[..lineSelStart], font, fontSize);
            var x1 = MeasureTextWidth(lineText[..lineSelEnd], font, fontSize);
            var y = i * lineHeight + leadingOffset;

            var selRect = new Rect(e.Rect.X + x0, e.Rect.Y + y, x1 - x0, lineHeight);
            DrawTexturedRect(selRect, e.Transform, null, ApplyOpacity(d.SelectionColor));
        }
    }

    private static void GetCursorPositionWrapped(ReadOnlySpan<char> text, Font font, float fontSize,
        float maxWidth, int cursorIndex, out float x, out float y)
    {
        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[64];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, maxWidth, 0, lines);
        var lineHeight = font.LineHeight * fontSize;

        if (lineCount == 0)
        {
            x = 0;
            y = 0;
            return;
        }

        for (var i = 0; i < lineCount; i++)
        {
            var line = lines[i];
            // Actual line end includes trailing spaces (next line start or text end)
            var actualEnd = i + 1 < lineCount ? lines[i + 1].Start : text.Length;

            if (cursorIndex <= actualEnd || i == lineCount - 1)
            {
                var posInLine = Math.Clamp(cursorIndex - line.Start, 0, actualEnd - line.Start);
                var lineText = text[line.Start..actualEnd];
                x = MeasureTextWidth(lineText[..posInLine], font, fontSize);
                y = i * lineHeight;
                return;
            }
        }

        x = 0;
        y = (lineCount - 1) * lineHeight;
    }

    private static void MoveCursorVertically(ref EditableTextState state, Font font, float fontSize, float maxWidth, int direction)
    {
        var text = state.EditText.AsReadOnlySpan();
        if (text.Length == 0 || maxWidth <= 0) return;

        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[64];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, maxWidth, 0, lines);
        if (lineCount <= 1) return;

        // Find which line the cursor is on
        var cursorLine = lineCount - 1;
        for (var i = 0; i < lineCount; i++)
        {
            var lineEnd = i + 1 < lineCount ? lines[i + 1].Start : text.Length;
            if (state.CursorIndex <= lineEnd || i == lineCount - 1)
            {
                cursorLine = i;
                break;
            }
        }

        var targetLine = cursorLine + direction;
        if (targetLine < 0 || targetLine >= lineCount) return;

        // Get X position of cursor on current line
        var currentLineStart = lines[cursorLine].Start;
        var currentLineEnd = cursorLine + 1 < lineCount ? lines[cursorLine + 1].Start : text.Length;
        var posInLine = Math.Clamp(state.CursorIndex - currentLineStart, 0, currentLineEnd - currentLineStart);
        var cursorX = MeasureTextWidth(text[currentLineStart..(currentLineStart + posInLine)], font, fontSize);

        // Hit-test on the target line to find closest char index
        var targetLineStart = lines[targetLine].Start;
        var targetLineEnd = targetLine + 1 < lineCount ? lines[targetLine + 1].Start : text.Length;
        var targetLineText = text[targetLineStart..targetLineEnd];
        state.CursorIndex = targetLineStart + FindCharIndexAtX(targetLineText, font, fontSize, cursorX);
    }

    private static int GetCurrentLineStart(ReadOnlySpan<char> text, Font font, float fontSize, float maxWidth, int cursorIndex)
    {
        if (text.Length == 0 || maxWidth <= 0) return 0;

        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[64];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, maxWidth, 0, lines);
        if (lineCount == 0) return 0;

        for (var i = lineCount - 1; i >= 0; i--)
        {
            if (cursorIndex >= lines[i].Start)
                return lines[i].Start;
        }
        return 0;
    }

    private static int GetCurrentLineEnd(ReadOnlySpan<char> text, Font font, float fontSize, float maxWidth, int cursorIndex)
    {
        if (text.Length == 0 || maxWidth <= 0) return text.Length;

        Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[64];
        var lineCount = TextRender.GetWrapLines(text, font, fontSize, maxWidth, 0, lines);
        if (lineCount == 0) return text.Length;

        for (var i = 0; i < lineCount; i++)
        {
            var lineEnd = i + 1 < lineCount ? lines[i + 1].Start : text.Length;
            if (cursorIndex < lineEnd || i == lineCount - 1)
            {
                // Exclude trailing newline from the "end" position
                if (lineEnd > 0 && lineEnd <= text.Length && text[lineEnd - 1] == '\n')
                    lineEnd--;
                return lineEnd;
            }
        }
        return text.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float MeasureTextWidth(ReadOnlySpan<char> text, Font font, float fontSize)
    {
        return TextRender.Measure(text, font, fontSize).X;
    }

    internal static int HitTestCharIndex(ReadOnlySpan<char> text, Font font, float fontSize,
        bool multiLine, float contentWidth, float relX, float relY)
    {
        if (text.Length == 0) return 0;

        if (multiLine && contentWidth > 0)
        {
            Span<TextRender.CachedLine> lines = stackalloc TextRender.CachedLine[64];
            var lineCount = TextRender.GetWrapLines(text, font, fontSize, contentWidth, 0, lines);
            if (lineCount == 0) return 0;

            var lineHeight = font.LineHeight * fontSize;
            var lineIndex = Math.Clamp((int)(relY / lineHeight), 0, lineCount - 1);
            var line = lines[lineIndex];
            // Use actual line end (including trailing spaces) for hit testing
            var actualEnd = lineIndex + 1 < lineCount ? lines[lineIndex + 1].Start : text.Length;
            var lineText = text[line.Start..actualEnd];
            return line.Start + FindCharIndexAtX(lineText, font, fontSize, relX);
        }

        return FindCharIndexAtX(text, font, fontSize, relX);
    }

    private static int FindCharIndexAtX(ReadOnlySpan<char> text, Font font, float fontSize, float targetX)
    {
        var x = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            var glyph = font.GetGlyph(text[i]);
            var advance = glyph.Advance * fontSize;
            if (i + 1 < text.Length)
                advance += font.GetKerning(text[i], text[i + 1]) * fontSize;
            if (x + advance * 0.5f > targetX)
                return i;
            x += advance;
        }
        return text.Length;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int FindPreviousWordBoundary(ReadOnlySpan<char> text, int index)
    {
        if (index <= 0) return 0;

        // Skip whitespace/punctuation going back
        var i = index - 1;
        while (i > 0 && !IsWordChar(text[i]))
            i--;

        // Skip word chars going back
        while (i > 0 && IsWordChar(text[i - 1]))
            i--;

        return i;
    }

    private static int FindNextWordBoundary(ReadOnlySpan<char> text, int index)
    {
        if (index >= text.Length) return text.Length;

        // Skip word chars forward
        var i = index;
        while (i < text.Length && IsWordChar(text[i]))
            i++;

        // Skip whitespace/punctuation forward
        while (i < text.Length && !IsWordChar(text[i]))
            i++;

        return i;
    }
}
