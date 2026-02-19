# Mobile Web Platform Support for NoZ Engine

## Context

The NoZ engine's web platform has basic touch support that maps `touchstart`/`touchmove`/`touchend` to mouse events, but it's unreliable on mobile devices. Touch input can get stuck, there's no virtual keyboard support (needed for any game with text fields), clipboard is unimplemented, and the HTML/CSS lacks mobile-specific polish (no `touch-action`, no safe area handling, no overscroll prevention). This work improves the engine for all games, not just Dominoz.

## Summary of Changes

| File | What |
|------|------|
| `noz/platform/web/wwwroot/js/noz/noz-platform.js` | Replace mouse+touch with Pointer Events; add hidden input for virtual keyboard; add clipboard; add safe area query |
| `noz/platform/web/WebPlatform.cs` | Implement ShowTextbox/HideTextbox, clipboard, IsTextboxVisible |
| `platform/web/wwwroot/index.html` | `touch-action: none`, `viewport-fit=cover`, safe area CSS, overscroll prevention |
| `noz/engine/src/ui/UI.TextBox.cs` | Call ShowTextbox on focus gain, HideTextbox on focus loss |
| `noz/engine/src/ui/UI.TextArea.cs` | Same ShowTextbox/HideTextbox integration |
| `noz/engine/src/ui/UI.cs` | Call HideTextbox in ClearFocus() |

---

## Step 1: Mobile CSS/HTML Polish

**File: `platform/web/wwwroot/index.html`**

- Update viewport meta to add `viewport-fit=cover` (enables safe area inset access)
- Add to `html, body` CSS: `overscroll-behavior: none; position: fixed;` (prevents pull-to-refresh and iOS bounce)
- Add to `#canvas` CSS: `touch-action: none; -webkit-user-select: none; user-select: none; -webkit-touch-callout: none;` (prevents browser touch gestures, text selection, callout menus)
- Add CSS custom properties for safe area insets using `env(safe-area-inset-*)`

## Step 2: Pointer Events Migration

**File: `noz/platform/web/wwwroot/js/noz/noz-platform.js`**

Replace all 6 mouse listeners (`mousedown`, `mouseup`, `mousemove`, `mouseenter`, `mouseleave`) and 3 touch listeners (`touchstart`, `touchend`, `touchmove`) with 6 pointer listeners (`pointerdown`, `pointerup`, `pointermove`, `pointerenter`, `pointerleave`, `pointercancel`). Keep `wheel` listener (no pointer equivalent).

Key design points:
- **`setPointerCapture(e.pointerId)`** on `pointerdown` — ensures `pointerup` fires even if finger leaves canvas (fixes the main reliability bug)
- **`isPrimary` guard** on all handlers — ignores multi-touch (single-pointer design)
- **`pointerType` tracking** — track whether current input is touch vs mouse for cursor visibility and keyboard resize guard
- **`pointercancel`** → `OnMouseUp(0)` — clean state reset on interrupted touches
- **Touch-specific `OnMouseEnter`/`OnMouseLeave` behavior** — send `OnMouseEnter` on `pointerdown` for touch; skip `OnMouseLeave` on `pointerleave` for touch (touch "leave" is the natural finger lift)
- Click count / double-click detection remains unchanged
- DPI coordinate conversion remains unchanged

Also update `shutdown()` to remove the new pointer listeners instead of the old mouse/touch ones.

### Pointer Event Handlers (Reference Implementation)

```javascript
let isTouchPointer = false;

function onPointerDown(e) {
    if (!e.isPrimary) return;
    canvas.setPointerCapture(e.pointerId);
    isTouchPointer = (e.pointerType === 'touch');

    const rect = canvas.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    const x = (e.clientX - rect.left) * dpr;
    const y = (e.clientY - rect.top) * dpr;

    dotNetRef.invokeMethod('OnMouseEnter');
    dotNetRef.invokeMethod('OnMouseMove', x, y);

    const now = Date.now();
    if (now - lastClickTime < 300 && e.button === 0) {
        clickCount++;
    } else {
        clickCount = 1;
    }
    lastClickTime = now;

    dotNetRef.invokeMethod('OnMouseDown', e.button, clickCount);
}

function onPointerUp(e) {
    if (!e.isPrimary) return;

    const rect = canvas.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    const x = (e.clientX - rect.left) * dpr;
    const y = (e.clientY - rect.top) * dpr;

    dotNetRef.invokeMethod('OnMouseMove', x, y);
    dotNetRef.invokeMethod('OnMouseUp', e.button);
}

function onPointerMove(e) {
    if (!e.isPrimary) return;

    const rect = canvas.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    const x = (e.clientX - rect.left) * dpr;
    const y = (e.clientY - rect.top) * dpr;

    if (e.pointerType !== 'touch') {
        isTouchPointer = false;
        dotNetRef.invokeMethod('OnMouseEnter');
    }

    dotNetRef.invokeMethod('OnMouseMove', x, y);
}

function onPointerEnter(e) {
    if (!e.isPrimary) return;
    if (e.pointerType !== 'touch') {
        dotNetRef.invokeMethod('OnMouseEnter');
        const rect = canvas.getBoundingClientRect();
        const dpr = window.devicePixelRatio || 1;
        dotNetRef.invokeMethod('OnMouseMove',
            (e.clientX - rect.left) * dpr,
            (e.clientY - rect.top) * dpr);
    }
}

function onPointerLeave(e) {
    if (!e.isPrimary) return;
    if (e.pointerType !== 'touch') {
        dotNetRef.invokeMethod('OnMouseLeave');
    }
}

function onPointerCancel(e) {
    if (!e.isPrimary) return;
    dotNetRef.invokeMethod('OnMouseUp', 0);
}
```

## Step 3: Clipboard Implementation

**JS side (`noz-platform.js`):**
- Add `paste` event listener on `window`
- On paste, cache text from `e.clipboardData.getData('text/plain')` and call `dotNetRef.invokeMethod('OnClipboardPaste', text)`
- Export `setClipboardText(text)` using `navigator.clipboard.writeText()`
- Cleanup listener in `shutdown()`

```javascript
let cachedClipboardText = null;

// In init():
window.addEventListener('paste', onPaste);

function onPaste(e) {
    const text = e.clipboardData?.getData('text/plain');
    if (text) {
        cachedClipboardText = text;
        dotNetRef.invokeMethod('OnClipboardPaste', text);
    }
}

export async function setClipboardText(text) {
    try {
        await navigator.clipboard.writeText(text);
    } catch (e) {
        console.warn('[Platform] Clipboard write failed:', e);
    }
}
```

**C# side (`WebPlatform.cs`):**
- Add `_clipboardText` cache field
- Add `[JSInvokable] OnClipboardPaste(string text)` — caches paste content
- `GetClipboardText()` returns `_clipboardText` (synchronous, already cached when paste event fires before engine processes Ctrl+V)
- `SetClipboardText(text)` caches locally + calls JS `setClipboardText`

```csharp
private string? _clipboardText;

[JSInvokable]
public void OnClipboardPaste(string text)
{
    _clipboardText = text;
}

public string? GetClipboardText()
{
    return _clipboardText;
}

public void SetClipboardText(string text)
{
    _clipboardText = text;
    _module?.InvokeVoidAsync("setClipboardText", text);
}
```

## Step 4: Virtual Keyboard / Hidden Input

### 4A: JavaScript Hidden Input (`noz-platform.js`)

- Create a hidden `<input>` element in `init()`: positioned at `left:-9999px; top:50%` (within viewport to avoid iOS scroll), `font-size:16px` (prevents iOS auto-zoom), `opacity:0`
- Add `input` event listener that:
  - `insertText` → calls `OnTextInput(e.data)`
  - `deleteContentBackward` → sends synthetic `OnKeyDown('Backspace')` + `OnKeyUp('Backspace')`
  - `insertLineBreak` → sends synthetic Enter key events
  - Resets `hiddenInput.value = ''` after each event (stateless)
- Guard the existing `onKeyDown` text input forwarding (line 161) with `!hiddenInputActive` to prevent double-sending
- Export `showTextbox(text, isPassword)` — sets type, value, calls `focus({ preventScroll: true })`
- Export `hideTextbox()` — blurs, clears, refocuses canvas
- Guard `onResize` to skip when `hiddenInputActive && isTouchPointer` (prevents canvas resize when mobile keyboard opens)

```javascript
let hiddenInput = null;
let hiddenInputActive = false;

// In init(), after canvas setup:
hiddenInput = document.createElement('input');
hiddenInput.id = 'noz-hidden-input';
hiddenInput.type = 'text';
hiddenInput.autocapitalize = 'off';
hiddenInput.autocomplete = 'off';
hiddenInput.spellcheck = false;
hiddenInput.setAttribute('autocorrect', 'off');
hiddenInput.style.cssText =
    'position:absolute;left:-9999px;top:50%;width:1px;height:1px;opacity:0;font-size:16px;z-index:-1;';
document.body.appendChild(hiddenInput);
hiddenInput.addEventListener('input', onHiddenInput);

function onHiddenInput(e) {
    if (!hiddenInputActive) return;

    if (e.inputType === 'insertText' && e.data) {
        dotNetRef.invokeMethod('OnTextInput', e.data);
    } else if (e.inputType === 'deleteContentBackward') {
        dotNetRef.invokeMethod('OnKeyDown', 'Backspace');
        dotNetRef.invokeMethod('OnKeyUp', 'Backspace');
    } else if (e.inputType === 'insertLineBreak') {
        dotNetRef.invokeMethod('OnKeyDown', 'Enter');
        dotNetRef.invokeMethod('OnKeyUp', 'Enter');
    }

    // Reset to prevent accumulation
    hiddenInput.value = '';
}

// Guard existing onKeyDown text input forwarding:
// Change line 161 from:
//   if (e.key.length === 1 && !e.ctrlKey && !e.metaKey) {
// To:
//   if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !hiddenInputActive) {

export function showTextbox(text, isPassword) {
    if (!hiddenInput) return;
    hiddenInputActive = true;
    hiddenInput.type = isPassword ? 'password' : 'text';
    hiddenInput.value = text || '';
    hiddenInput.focus({ preventScroll: true });
}

export function hideTextbox() {
    if (!hiddenInput) return;
    hiddenInputActive = false;
    hiddenInput.blur();
    hiddenInput.value = '';
    canvas.focus();
}

// Guard onResize to prevent canvas resize when mobile keyboard opens:
function onResize() {
    if (hiddenInputActive && isTouchPointer) return;
    // ... existing resize logic
}
```

### 4B: C# Platform (`WebPlatform.cs`)

- Add `_textboxVisible` state field
- `IsTextboxVisible` returns `_textboxVisible` (replace `=> false`)
- `ShowTextbox(rect, text, style)` — sets `_textboxVisible = true`, calls JS `showTextbox(text, style.Password)`
- `HideTextbox()` — guarded by `_textboxVisible`, sends synthetic KeyUp events for Escape/Enter/Tab (prevents stuck keys, matches desktop impl), calls JS `hideTextbox()`
- `UpdateTextboxRect` — no-op (hidden input is off-screen, engine renders visual textbox)
- `UpdateTextboxText` — returns `false` (engine owns text state)

```csharp
private bool _textboxVisible;

public bool IsTextboxVisible => _textboxVisible;

public void ShowTextbox(Rect rect, string text, NativeTextboxStyle style)
{
    _textboxVisible = true;
    _module?.InvokeVoidAsync("showTextbox", text ?? "", style.Password);
}

public void HideTextbox()
{
    if (!_textboxVisible) return;
    _textboxVisible = false;

    _eventQueue.Enqueue(PlatformEvent.KeyUp(InputCode.KeyEscape));
    _eventQueue.Enqueue(PlatformEvent.KeyUp(InputCode.KeyEnter));
    _eventQueue.Enqueue(PlatformEvent.KeyUp(InputCode.KeyTab));

    _module?.InvokeVoidAsync("hideTextbox");
}
```

### 4C: Engine UI Integration

**`noz/engine/src/ui/UI.TextBox.cs` — `HandleTextBoxInput` (line 95-101):**
After the focus-gain detection (`if (!es.HasFocus)`), add `Application.Platform.ShowTextbox()` call with the element's rect, text, and style (FontSize, Password).

```csharp
if (!es.HasFocus)
{
    es.SetFlags(ElementFlags.Focus, ElementFlags.Focus);
    tb.SelectionStart = 0;
    tb.CursorIndex = tb.Text.AsReadOnlySpan().Length;
    tb.BlinkTimer = 0;

    Application.Platform.ShowTextbox(
        e.Rect,
        new string(tb.Text.AsReadOnlySpan()),
        new NativeTextboxStyle
        {
            FontSize = (int)e.Data.TextBox.FontSize,
            Password = e.Data.TextBox.Password,
        });

    return;
}
```

**`noz/engine/src/ui/UI.TextBox.cs` — `UpdateTextBoxState` (line 49-53):**
Before clearing focus flags, check `es.HasFocus` and call `HideTextbox()`:

```csharp
ref var es = ref GetElementState(e.Id);
if (!IsFocused(ref e))
{
    if (es.HasFocus)
        Application.Platform.HideTextbox();

    es.SetFlags(ElementFlags.Focus | ElementFlags.Dragging, ElementFlags.None);
    es.Data.TextBox.ScrollOffset = 0.0f;
    return;
}
```

**`noz/engine/src/ui/UI.TextArea.cs`** — Same pattern in `HandleTextAreaInput` and `UpdateTextAreaState`.

**`noz/engine/src/ui/UI.cs` — `ClearFocus()` (line 440):**
Add `if (_focusElementId != 0) Application.Platform.HideTextbox();` before clearing IDs:

```csharp
public static void ClearFocus()
{
    if (_focusElementId != 0)
        Application.Platform.HideTextbox();

    _focusElementId = 0;
    _pendingFocusElementId = 0;
}
```

## Step 5: Safe Area Query (Optional Engine API)

**JS (`noz-platform.js`):** Export `getSafeAreaInsets()` that reads computed CSS `env()` values.

**C# (`WebPlatform.cs`):** Add `GetSafeAreaInsetsAsync()` returning a `Rect` with top/bottom/left/right insets. Games can query this to offset UI from notches.

```javascript
export function getSafeAreaInsets() {
    const style = getComputedStyle(document.documentElement);
    return {
        top: parseFloat(style.getPropertyValue('--sai-top')) || 0,
        bottom: parseFloat(style.getPropertyValue('--sai-bottom')) || 0,
        left: parseFloat(style.getPropertyValue('--sai-left')) || 0,
        right: parseFloat(style.getPropertyValue('--sai-right')) || 0
    };
}
```

---

## iOS Safari Quirks & Edge Cases

- **300ms tap delay**: Already mitigated by `<meta name="viewport" content="width=device-width">` (iOS 9.3+). `touch-action: none` further eliminates it.
- **Auto-zoom on input focus**: Prevented by `font-size:16px` on hidden input.
- **Scroll-to-input**: Prevented by `focus({ preventScroll: true })` and `top:50%` positioning.
- **Keyboard resize**: Guard in `onResize` skips canvas resize when `hiddenInputActive && isTouchPointer`.
- **Context menu on long press**: Prevented by `contextmenu` preventDefault + `-webkit-touch-callout: none` CSS.
- **Bounce/overscroll**: Prevented by `overscroll-behavior: none` + `position: fixed`.

---

## Verification

1. **Build**: `dotnet build platform/desktop/dominoz.Desktop.csproj` — verify no compile errors from engine changes
2. **Desktop regression**: Run desktop build, verify mouse input, TextBox/TextArea focus/unfocus, clipboard all still work
3. **Web build**: `dotnet build platform/web/dominoz.Web.csproj` (or however the web project builds)
4. **Mobile browser testing** (Chrome DevTools device emulation or real device):
   - Tap and drag pieces — should be reliable, no stuck states
   - Drag finger off screen and release — piece should drop cleanly (pointer capture)
   - No pull-to-refresh or bounce scroll
   - No pinch-to-zoom
   - No context menu on long press
   - If a TextBox is added to any screen: tapping it should open virtual keyboard, typing should insert text, pressing Enter/back should dismiss keyboard
