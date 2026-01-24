//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;

namespace NoZ;

public struct TextBoxData
{
    public Size Height;
    public float FontSize;
    public Color BackgroundColor;
    public Color TextColor;
    public Color PlaceholderColor;
    public Color SelectionColor;
    public BorderStyle Border;
    public BorderStyle FocusBorder;
    public UnsafeSpan<char> Text;
    public UnsafeSpan<char> Placeholder;
    public bool Password;

    public static TextBoxData Default => new()
    {
        Height = 28f,
        FontSize = 16,
        BackgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f),
        TextColor = Color.White,
        PlaceholderColor = new Color(0.4f, 0.4f, 0.4f, 1f),
        SelectionColor = new Color(0.2f, 0.4f, 0.8f, 0.5f),
        Border = BorderStyle.None,
        FocusBorder = BorderStyle.None,
        Password = false,
        Text = UnsafeSpan<char>.Empty,
        Placeholder = UnsafeSpan<char>.Empty
    };
}

public static partial class UI
{
    private static readonly string PasswordMask = new('*', 64);
    
    //private static byte _focusId;
    //private static byte _focusCanvasId;
    //private static bool _visible;
    //private static bool _renderedThisFrame;

    private static void DrawTextBox(ref Element e)
    {
        Debug.Assert(e.Id != ElementId.None, "TextBox element must have a valid Id");

        ref var data = ref e.Data.TextBox;
        var isFocused = e.Id != 0 && IsFocus(e.Id, e.CanvasId);
        var border = isFocused ? data.FocusBorder : data.Border;

        UIRender.DrawRect(
            new Rect(Vector2.Transform(Vector2.Zero, e.LocalToWorld), e.Rect.Size),
            data.BackgroundColor,
            border.Radius,
            border.Width,
            border.Color
        );

        if (e.Id == ElementId.None)
            return;

        var font = e.Font ?? DefaultFont;
        if (font == null) return;

        var text = UI.GetElementText(e.Id);
        var fontSize = data.FontSize;

        // Custom TextBox Implementation
        if (isFocused)
        {
            DrawTextBoxSelection(ref e, text, font, fontSize);
            DrawTextBoxText(ref e, text, font, fontSize, data.TextColor, data.Password);
            DrawTextBoxCursor(ref e, text, font, fontSize);
            DrawTextBoxPlaceholder(ref e, text, font, fontSize);
            
            // Save modified text back
            UI.SetElementText(e.Id, text);
            
            _focusId = e.Id;
            _focusCanvasId = e.CanvasId;
            _renderedThisFrame = true;
            
            // Ensure native textbox is hidden if we are using custom
            if (_visible)
            {
                Application.Platform.HideTextbox();
                _visible = false;
            }
        }
        else
        {
            // Clear focus state if not focused
            var state = GetElementState(e.CanvasId, e.Id);
            state.HasFocus = false;
            state.Dragging = false;
            state.ScrollOffset = 0;

            // Draw Placeholder if empty
            if (text.Length == 0 && data.Placeholder.Length > 0)
            {
                var placeholderOffset = new Vector2(0, (e.Rect.Height - font.LineHeight * fontSize) * 0.5f);
                var transform = e.LocalToWorld * Matrix3x2.CreateTranslation(placeholderOffset);

                using (Graphics.PushState())
                {
                    Graphics.SetColor(data.PlaceholderColor);
                    Graphics.SetTransform(transform);
                    TextRender.Draw(data.Placeholder.AsReadonlySpan(), font, fontSize);
                }
            }
            else
            {
                // Draw Text (consistent with focused state)
                DrawTextBoxText(ref e, text, font, fontSize, data.TextColor, data.Password);
            }
        }
    }

    private static void HandleTextBoxInput(ref Element e)
    {
        if (e.Id == ElementIdNone)
            return;

        var isFocused = IsFocus(e.Id, e.CanvasId);
        if (!isFocused)
            return;

        var font = e.Font ?? DefaultFont;
        if (font == null) return;

        ref var data = ref e.Data.TextBox;
        var fontSize = data.FontSize;
        ref var state = ref _elementStates[e.Id].Data.TextBox;
        var text = UI.GetElementText(e.Id);

        HandleTextBoxInput(ref e, text, font, fontSize);
        UpdateTextBoxScroll(ref e, ref state, text, font, fontSize);
    }

    private static void HandleTextBoxInput(ref Element e, in ReadOnlySpan<char> text, Font font, float fontSize)
    {
        var control = Input.IsCtrlDown();
        var shift = Input.IsShiftDown();
        var mousePos = UI.Camera!.ScreenToWorld(Input.MousePosition);
        var localMouse = Vector2.Transform(mousePos, e.WorldToLocal);
        var isMouseOver = new Rect(0, 0, e.Rect.Width, e.Rect.Height).Contains(localMouse);
        var isHotCanvas = e.CanvasId == UI.HotCanvasId();

        ref var elementState = ref GetElementState(ref e);
        ref var state = ref elmentState.Data.TextBox;
        
        if (!elementState.HasFocus)
        {
            elementState.SetFlags(ElementFlags.Focus, ElementFlags.Focus);

            // If mouse is down inside, start dragging immediately
            // We verify hot canvas to ensure we don't steal focus/drag if obscured, 
            // though UI system usually handles the focus switch.
            if (isMouseOver && Input.IsButtonDown(InputCode.MouseLeft) && isHotCanvas)
            {
                var mouseIndex = GetCharacterIndexAtPosition(ref e, state, text, font, fontSize, mousePos);
                state.CursorIndex = mouseIndex;
                state.SelectionStart = mouseIndex;
                state.Dragging = true;
                state.BlinkTimer = 0;
            }
        }

        // Double Click to Select All
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeftDoubleClick) && isHotCanvas)
        {
            state.SelectionStart = 0;
            state.CursorIndex = text.Length;
            state.Dragging = false; // Stop dragging to prevent overwrite on next frame
            state.BlinkTimer = 0;
            return; // Skip standard click handling
        }

        // Standard Mouse Input
        if (isMouseOver && Input.WasButtonPressed(InputCode.MouseLeft) && isHotCanvas)
        {
            var mouseIndex = GetCharacterIndexAtPosition(ref e, state, text, font, fontSize, mousePos);
            state.CursorIndex = mouseIndex;
            state.SelectionStart = mouseIndex;
            state.Dragging = true;
            state.BlinkTimer = 0;
        }
        else if (state.Dragging)
        {
            if (Input.IsButtonDown(InputCode.MouseLeft))
            {
                var mouseIndex = GetCharacterIndexAtPosition(ref e, state, text, font, fontSize, mousePos);
                state.CursorIndex = mouseIndex;
                // Scroll if dragging outside bounds?
                // For now just update cursor.
            }
            else
            {
                state.Dragging = false;
            }
        }

        // Keyboard Navigation
        if (Input.WasButtonPressed(InputCode.KeyLeft, true))
        {
            if (control)
            {
                // Move by word
                state.CursorIndex = FindPrevWordStart(text, state.CursorIndex);
            }
            else if (state.CursorIndex > 0)
            {
                state.CursorIndex--;
            }
            else if (!shift && state.CursorIndex != state.SelectionStart)
            {
                 // Left at start with selection clears selection to start
                 state.CursorIndex = Math.Min(state.CursorIndex, state.SelectionStart);
            }

            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyRight, true))
        {
            if (control)
            {
                // Move by word
                state.CursorIndex = FindNextWordStart(text, state.CursorIndex);
            }
            else if (state.CursorIndex < text.Length)
            {
                state.CursorIndex++;
            }
            else if (!shift && state.CursorIndex != state.SelectionStart)
            {
                 // Right at end with selection clears selection to end
                 state.CursorIndex = Math.Max(state.CursorIndex, state.SelectionStart);
            }

            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyHome))
        {
            state.CursorIndex = 0;
            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyEnd))
        {
            state.CursorIndex = text.Length;
            if (!shift) state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyA))
        {
            state.SelectionStart = 0;
            state.CursorIndex = text.Length;
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyC))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                var start = Math.Min(state.CursorIndex, state.SelectionStart);
                var length = Math.Abs(state.CursorIndex - state.SelectionStart);
                Application.Platform.SetClipboardText(new string(text.Slice(start, length)));
            }
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyV))
        {
            var clipboard = Application.Platform.GetClipboardText();
            if (!string.IsNullOrEmpty(clipboard))
            {
                if (state.CursorIndex != state.SelectionStart)
                    DeleteSelection(ref text, state);
                
                text = text.Insert(state.CursorIndex, clipboard);
                state.CursorIndex += clipboard.Length;
                state.SelectionStart = state.CursorIndex;
                state.BlinkTimer = 0;
            }
        }
        else if (control && Input.WasButtonPressed(InputCode.KeyX))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                var start = Math.Min(state.CursorIndex, state.SelectionStart);
                var length = Math.Abs(state.CursorIndex - state.SelectionStart);
                Application.Platform.SetClipboardText(text.Substring(start, length));
                DeleteSelection(ref text, state);
                state.BlinkTimer = 0;
            }
        }
        
        // Text Modification
        if (Input.WasButtonPressed(InputCode.KeyBackspace, true))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                DeleteSelection(ref text, state);
            }
            else if (state.CursorIndex > 0)
            {
                var removeCount = 1;
                if (control)
                {
                    var prevWord = FindPrevWordStart(text, state.CursorIndex);
                    removeCount = state.CursorIndex - prevWord;
                }
                
                text = text.Remove(state.CursorIndex - removeCount, removeCount);
                state.CursorIndex -= removeCount;
                state.SelectionStart = state.CursorIndex;
            }
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyDelete, true))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                DeleteSelection(ref text, state);
            }
            else if (state.CursorIndex < text.Length)
            {
                var removeCount = 1;
                if (control)
                {
                    var nextWord = FindNextWordStart(text, state.CursorIndex);
                    removeCount = nextWord - state.CursorIndex;
                }
                text = text.Remove(state.CursorIndex, removeCount);
            }
            state.BlinkTimer = 0;
        }
        else if (Input.WasButtonPressed(InputCode.KeyEnter) || Input.WasButtonPressed(InputCode.KeyEscape))
        {
            UI.ClearFocus();
            return;
        }

        // Character Input
        var input = Input.GetTextInput();
        if (!string.IsNullOrEmpty(input))
        {
            if (state.CursorIndex != state.SelectionStart)
            {
                DeleteSelection(ref text, state);
            }
            
            // Filter non-printable? 
            // Input.GetTextInput usually returns printable chars.
            text = text.Insert(state.CursorIndex, input);
            state.CursorIndex += input.Length;
            state.SelectionStart = state.CursorIndex;
            state.BlinkTimer = 0;
        }
    }

    private static void DeleteSelection(ref ReadOnlySpan<char> text, State state)
    {
        var start = Math.Min(state.CursorIndex, state.SelectionStart);
        var end = Math.Max(state.CursorIndex, state.SelectionStart);
        text = text.Remove(start, end - start);
        state.CursorIndex = start;
        state.SelectionStart = start;
    }

    private static int FindPrevWordStart(in ReadOnlySpan<char> text, int index)
    {
        if (index <= 0) return 0;
        index--;
        while (index > 0 && char.IsWhiteSpace(text[index])) index--;
        while (index > 0 && !char.IsWhiteSpace(text[index - 1])) index--;
        return index;
    }

    private static int FindNextWordStart(in ReadOnlySpan<char> text, int index)
    {
        if (index >= text.Length) return text.Length;
        while (index < text.Length && !char.IsWhiteSpace(text[index])) index++;
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }

    private static void UpdateTextBoxScroll(
        ref Element e,
        ref TextBoxState state,
        in ReadOnlySpan<char> text,
        Font font,
        float fontSize)
    {
        var cursorX = MeasureText(text, 0, state.CursorIndex, font, fontSize);
        var viewportWidth = e.Rect.Width;
        var cursorScreenX = cursorX - state.ScrollOffset;

        if (cursorScreenX < 0)
            state.ScrollOffset = cursorX;
        else if (cursorScreenX > viewportWidth)
            state.ScrollOffset = cursorX - viewportWidth;
        
        var totalWidth = MeasureText(text, 0, text.Length, font, fontSize);
        if (totalWidth < viewportWidth)
            state.ScrollOffset = 0;
        else
            state.ScrollOffset = Math.Clamp(state.ScrollOffset, 0, totalWidth - viewportWidth);
    }

    private static void DrawTextBoxSelection(
        ref Element e,
        in ReadOnlySpan<char> text,
        Font font,
        float fontSize)
    {
        ref var es = ref GetElementState(ref e);
        ref var tb = ref es.Data.TextBox;

        if (tb.CursorIndex == tb.SelectionStart) return;

        var start = Math.Min(tb.CursorIndex, tb.SelectionStart);
        var end = Math.Max(tb.CursorIndex, tb.SelectionStart);
        var startX = MeasureText(text, 0, start, font, fontSize) - tb.ScrollOffset;
        var endX = MeasureText(text, 0, end, font, fontSize) - tb.ScrollOffset;        
        var rectX = 0f;
        var rectW = e.Rect.Width;
        var drawX = Math.Max(rectX, startX);
        var drawW = Math.Min(rectW, endX) - drawX;

        if (drawW <= 0) return;

        var pos = Vector2.Transform(new Vector2(drawX, 0), e.LocalToWorld);
        UIRender.DrawRect(new Rect(pos.X, pos.Y, drawW, e.Rect.Height), e.Data.TextBox.SelectionColor);
    }

    private static void DrawTextBoxText(
        ref Element e,
        in ReadOnlySpan<char> text,
        Font font,
        float fontSize,
        Color color,
        bool password)
    {
        if (text.Length == 0) return;

        ref var es = ref GetElementState(ref e);
        ref var tb = ref es.Data.TextBox;

        var scale = UI.GetUIScale();
        var screenPos = UI.Camera!.WorldToScreen(Vector2.Transform(Vector2.Zero, e.LocalToWorld));
        var screenHeight = Application.WindowSize.Y;
        var scissor = new RectInt(
            (int)screenPos.X,
            (int)(screenHeight - screenPos.Y - e.Rect.Height * scale),
            (int)(e.Rect.Width * scale),
            (int)(e.Rect.Height * scale));

        var textOffset = new Vector2(-tb.ScrollOffset, (e.Rect.Height - font.LineHeight * fontSize) * 0.5f);
        var textToRender = password
             ? PasswordMask.AsSpan()[..Math.Min(text.Length, PasswordMask!.Length)]
             : text;
             
        using (Graphics.PushState())
        {
            Graphics.SetScissor(scissor);
            Graphics.SetColor(color);
            Graphics.SetTransform(e.LocalToWorld * Matrix3x2.CreateTranslation(textOffset));
            TextRender.Draw(textToRender, font, fontSize);
            Graphics.DisableScissor();
        }
    }

    private static void DrawTextBoxPlaceholder(ref Element e, in ReadOnlySpan<char> text, Font font, float fontSize)
    {
        ref var data = ref e.Data.TextBox;
        if (text.Length == 0 && data.Placeholder.Length > 0)
            return;

        ref var state = ref GetElementState(ref e).Data.TextBox;
        var placeholder = new string(data.Placeholder.AsReadonlySpan());
        var placeholderOffset = new Vector2(-state.ScrollOffset, (e.Rect.Height - font.LineHeight * fontSize) * 0.5f);
        var transform = e.LocalToWorld * Matrix3x2.CreateTranslation(placeholderOffset);

        using (Graphics.PushState())
        {
            Graphics.SetColor(data.PlaceholderColor);
            Graphics.SetTransform(transform);
            TextRender.Draw(placeholder, font, fontSize);
        }
    }

    private static void DrawTextBoxCursor(
        ref Element e,
        in ReadOnlySpan<char> text,
        Font font,
        float fontSize)
    {
        ref var es = ref GetElementState(ref e);
        ref var tb = ref es.Data.TextBox;

        if (tb.CursorIndex != tb.SelectionStart) return;

        tb.BlinkTimer += Time.DeltaTime;
        if ((int)(tb.BlinkTimer * 2) % 2 == 1 && !es.IsDragging) return; // Blink off
        
        var cursorX = MeasureText(text, 0, tb.CursorIndex, font, fontSize) - tb.ScrollOffset;
        
        if (cursorX < 0 || cursorX > e.Rect.Width) return;
        
        var cursorW = 1f; // 1 pixel width
        var cursorH = font.LineHeight * fontSize;
        var cursorY = (e.Rect.Height - cursorH) * 0.5f;

        var pos = Vector2.Transform(new Vector2(cursorX, cursorY), e.LocalToWorld);        
        UIRender.DrawRect(new Rect(pos.X, pos.Y, cursorW, cursorH), Color.White);
    }

    private static float MeasureText(
        in ReadOnlySpan<char> text,
        int start,
        int length,
        Font font,
        float fontSize)
    {
        if (length <= 0) return 0;
        return TextRender.Measure(text.Slice(start, length), font, fontSize).X;
    }

    private static int GetCharacterIndexAtPosition(ref Element e, in ReadOnlySpan<char> text, Font font, float fontSize, Vector2 worldMousePos)
    {
        ref var es = ref GetElementState(ref e);
        ref var tb = ref es.Data.TextBox;
        var localMouse = Vector2.Transform(worldMousePos, e.WorldToLocal);
        var x = localMouse.X + tb.ScrollOffset;
        
        if (x <= 0) return 0;
        
        var currentX = 0f;
        
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var glyph = font.GetGlyph(ch);
            var advance = glyph.Advance * fontSize;
            if (i + 1 < text.Length)
                advance += font.GetKerning(ch, text[i + 1]) * fontSize;
                
            if (x < currentX + advance * 0.5f)
                return i;
                
            currentX += advance;
        }
        
        return text.Length;
    }

    public static void TextBoxEndFrame()
    {
        // Hide textbox if it wasn't rendered this frame (element no longer exists)
        if (_visible && !_renderedThisFrame)
        {
            Application.Platform.HideTextbox();
            _focusId = 0;
            _focusCanvasId = 0;
            _visible = false;
        }
        
        // Reset for next frame
        _renderedThisFrame = false;
    }
}
