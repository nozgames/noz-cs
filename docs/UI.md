# NoZ UI System

Immediate-mode UI system. No retained widget objects — every element is drawn every frame, layout is computed automatically. All methods live on the static `UI` class in the `NoZ` namespace.

## Interaction (Hover & Press)

Containers with an element ID can be hovered and pressed. This is the primary way to build buttons and clickable elements.

```csharp
using (UI.BeginContainer(ElementId.MyButton, style))
{
    UI.Label("Click Me", labelStyle);
    if (UI.WasPressed())
        DoAction();
}
```

**The no-arg overloads (`IsHovered()`, `WasPressed()`, `IsDown()`) reference the nearest ancestor element with an ID in the current hierarchy.** This is the most common usage — call them inside the container's `using` block.

The overloads with an explicit ID (`IsHovered(id)`, `WasPressed(id)`) can be called anywhere:

```csharp
var hovered = UI.IsHovered(ElementId.MyButton);
using (UI.BeginContainer(ElementId.MyButton, hovered ? hoverStyle : normalStyle))
{
    UI.Label("Click", labelStyle);
}
```

Focus:
```csharp
UI.SetFocus(ElementId.Input);
UI.ClearFocus();
bool focused = UI.IsFocused(ElementId.Input);
bool anyFocus = UI.HasFocus();
```

## ElementId

`[ElementId("Name")]` attributes on a `partial class` generate `public const int` fields via source generator. These integer IDs are used for hover/press detection, scroll state, focus, and layout caching.

```csharp
[ElementId("Root")]
[ElementId("SubmitButton")]
[ElementId("ItemBase", 100)]   // reserves 100 consecutive IDs for dynamic lists
private static partial class ElementId { }

// Generated: ElementId.Root, ElementId.SubmitButton, ElementId.ItemBase
// Dynamic indexing: ElementId.ItemBase + i
```

The `count` parameter reserves a range of IDs for use in loops.

## Layout Containers

Three container types control child layout direction:

| Method | Description |
|--------|-------------|
| `BeginContainer` | Children overlay each other (no layout direction) |
| `BeginColumn` | Children laid out top to bottom |
| `BeginRow` | Children laid out left to right |

All return disposable auto-structs for the `using()` pattern. Overloads: `()`, `(id)`, `(style)`, `(id, style)`.

```csharp
using (UI.BeginRow(ElementId.MyRow, rowStyle))
{
    // children laid out left-to-right
}
```

## Sizing

```csharp
Size.Default          // fill available space in parent's layout axis
Size.Fit              // shrink to fit content
new Size(200)         // fixed 200 pixels
Size.Percent(0.5f)    // 50% of parent
```

Set via `ContainerStyle.Width` / `ContainerStyle.Height`. When omitted they default to `Size.Default`.

## Flex & Spacers

**Flex** takes remaining space proportionally in a Row or Column (like CSS `flex-grow`):

```csharp
using (UI.BeginRow())
{
    using (UI.BeginContainer(sidebarStyle)) { ... }  // fixed width
    using (UI.BeginFlex()) { ... }                    // fills remaining space
}
```

`UI.Flex()` (without Begin) creates an empty flexible spacer with no children — useful for pushing elements apart:

```csharp
using (UI.BeginRow(rowStyle))
{
    UI.Label("Left", labelStyle);
    UI.Flex();                          // pushes next element to the right
    UI.Label("Right", labelStyle);
}
```

**Spacer** inserts a fixed-size gap. It is always a fixed size, never flexible:

```csharp
UI.Spacer(16);  // 16px gap
```

## Alignment

```csharp
Align.Min       // left / top
Align.Center    // center
Align.Max       // right / bottom
```

`Align2` has X and Y axes. Implicit conversion from `Align` sets both:

```csharp
Align = Align.Center,                            // both axes centered
Align = new Align2(Align.Min, Align.Center),      // left, vertically centered
```

**What Align does on each element type:**

- **Container / Image**: Positions the element within its parent's bounds.
- **Label**: Positions text within the parent's bounds (label rect fills its parent by default).

In a **Row**, AlignX is ignored (X position is determined by row layout). In a **Column**, AlignY is ignored (Y position is determined by column layout). In a plain **Container** parent, both axes apply.

## Elements

### Label — single-line text

```csharp
UI.Label("Hello World", style);
```

### WrappedLabel — multi-line text with word wrapping

Requires an ID for layout caching.

```csharp
UI.WrappedLabel(elementId, "Long text that wraps", style);
```

### TextBox — single-line text input

```csharp
if (UI.TextBox(ElementId.EmailBox, inputStyle, "placeholder text"))
{
    // text changed this frame
}

var text = UI.GetTextBoxText(ElementId.EmailBox);
UI.SetTextBoxText(ElementId.EmailBox, "new value");
```

### TextArea — multi-line text input

```csharp
if (UI.TextArea(ElementId.NotesArea, textAreaStyle, "placeholder"))
{
    // text changed this frame
}

var text = UI.GetTextAreaText(ElementId.NotesArea);
UI.SetTextAreaText(ElementId.NotesArea, "new value");
```

### Image — displays a sprite or texture

```csharp
UI.Image(sprite, imageStyle);
UI.Image(texture, imageStyle);
```

### Scene — renders world-space content inside UI layout

```csharp
UI.Scene(ElementId.GameScene, camera, () =>
{
    // draw world content here
}, new SceneStyle { Color = new Color(20, 20, 25) });
```

### Container — empty rectangle (separator, dot, colored block)

```csharp
UI.Container(new ContainerStyle
{
    Width = 32,
    Height = 32,
    Color = new Color(70, 130, 230),
    BorderRadius = BorderRadius.Circular(16),
});
```

## Scrollable

Wraps content in a vertically scrollable region. **Must have a constrained height** — `Size.Fit` does not work because it grows to fit all content, leaving nothing to scroll. Any bounded height works: fixed size, percentage, or flex.

```csharp
using (UI.BeginFlex())
{
    using (UI.BeginScrollable(ElementId.ListScroll))
    {
        using (UI.BeginColumn())
        {
            for (int i = 0; i < items.Count; i++)
            {
                // draw each item
            }
        }
    }
}
```

Scroll offset control (only works on scrollable elements):

```csharp
float offset = UI.GetScrollOffset(ElementId.MyScroll);
UI.SetScrollOffset(ElementId.MyScroll, 0);  // scroll to top
```

## Style Reference

### ContainerStyle

| Property | Description |
|----------|-------------|
| `Width`, `Height` | `Size` — element dimensions. Default fills parent axis |
| `MinWidth`, `MinHeight` | Minimum size constraints |
| `MaxWidth`, `MaxHeight` | Maximum size constraints |
| `Align` | Positions this container within its parent (Min/Center/Max per axis) |
| `Margin` | Outer spacing (EdgeInsets — TLBR order) |
| `Padding` | Inner spacing |
| `Color` | Background fill color |
| `Border` | Composite setter for `BorderRadius`, `BorderWidth`, `BorderColor` |
| `Spacing` | Gap between children (Row/Column only) |
| `Clip` | Clip children that overflow bounds |
| `Order` | Draw sort order (higher draws later/on top) |

Presets: `ContainerStyle.Fit` (both axes Fit), `ContainerStyle.Center` (Fit + Center align).

### LabelStyle

| Property | Description |
|----------|-------------|
| `FontSize` | Text size (default 16) |
| `Color` | Text color |
| `Align` | Text positioning within parent bounds (default: left, vertically centered) |
| `Font` | Custom font (null = default) |
| `Overflow` | `TextOverflow.Overflow`, `Ellipsis`, `Scale`, `Wrap` |
| `Order` | Draw sort order |

### TextBoxStyle

| Property | Description |
|----------|-------------|
| `Height` | Element height |
| `FontSize` | Text size |
| `BackgroundColor`, `TextColor`, `PlaceholderColor`, `SelectionColor` | Colors |
| `BorderRadius`, `BorderWidth`, `BorderColor` | Border styling |
| `FocusBorderRadius`, `FocusBorderWidth`, `FocusBorderColor` | Border styling when focused |
| `Padding` | Inner spacing |
| `IsPassword` | Mask input characters |
| `Scope` | `InputScope` filter |

`TextAreaStyle` is the same as `TextBoxStyle` without `IsPassword`.

### ImageStyle

| Property | Description |
|----------|-------------|
| `Width`, `Height` | `Size` — element dimensions |
| `Stretch` | `ImageStretch.None`, `Fill`, `Uniform` — how image fills bounds |
| `Align` | Positions image within parent AND image content within element bounds |
| `Scale` | Image scale multiplier |
| `Color` | Tint/multiply color |
| `BorderRadius` | Corner rounding (Texture images only) |
| `Order` | Draw sort order |

### ScrollableStyle

Controls scrollbar appearance only — no layout properties.

| Property | Description |
|----------|-------------|
| `ScrollSpeed` | Scroll speed (default 30) |
| `Scrollbar` | `ScrollbarVisibility.Auto`, `Always`, `Never` |
| `ScrollbarWidth` | Scrollbar track width |
| `ScrollbarMinThumbHeight` | Minimum thumb size |
| `ScrollbarTrackColor`, `ScrollbarThumbColor`, `ScrollbarThumbHoverColor` | Colors |
| `ScrollbarPadding` | Padding around scrollbar |
| `ScrollbarBorderRadius` | Thumb corner rounding |

## Other Elements

These exist but are less commonly used:

- `UI.BeginOpacity(float)` — draws children at reduced opacity
- `UI.BeginTransformed(TransformStyle)` — applies translate/rotate/scale to children (pivot via `Origin`)
- `UI.BeginPopup(id, PopupStyle)` — anchored popup overlay; check `UI.IsClosed()` for dismissal
- `UI.BeginGrid(GridStyle)` — grid layout with fixed cell sizes and optional virtualization
- `UI.BeginCursor(SystemCursor)` — changes cursor on hover for children

## Value Types

### EdgeInsets

Constructor order: **top, left, bottom, right** (TLBR).

```csharp
new EdgeInsets(8, 16, 8, 16)           // T=8, L=16, B=8, R=16
new EdgeInsets(12)                      // all sides 12
EdgeInsets.Symmetric(8, 16)            // vertical=8, horizontal=16
EdgeInsets.Top(10)  .Bottom(10)  .Left(10)  .Right(10)
EdgeInsets.LeftRight(16)  .TopBottom(8)
EdgeInsets.Zero
```

### BorderRadius

```csharp
BorderRadius.Circular(8)               // all corners 8px
BorderRadius.Only(topLeft: 8, bottomRight: 8)
BorderRadius.Vertical(top: 8, bottom: 0)
BorderRadius.Horizontal(left: 8, right: 0)
BorderRadius.Zero
```

Implicit conversion from `float`: `Border = new BorderStyle { Radius = 8f }`

### Color

```csharp
new Color(70, 130, 230)         // RGB (0-255 ints)
new Color(0, 0, 0, 128)         // RGBA
Color.White  Color.Black  Color.Transparent
```

## Debug Dump (Ctrl+F12)

Press **Ctrl+F12** while the game is running to write the current UI tree to `temp/ui_dump.txt`. The dump captures the previous frame's state.

### Format

```
Frame: 53930  Screen: 1084x818
───────────────────────────────
[0] Container "MainLayout.Root" 0,0 1084x818 [hovered]
  [1] Row 0,0 1084x818
    [2] Container 0,0 240x818 bg:#1E1E23
      [3] Column "Sidebar.Root" 0,0 240x818
        [4] Row "Sidebar.LiveRoomRow" 0,0 240x48 bg:#23232A pad=0,12,0,12 align=Min,Center
          [5] Label 0,0 95x48 "Join Live Room" size=14 color=#4682E6
```

### Field Reference

| Field | Example | Meaning |
|-------|---------|---------|
| `[index]` | `[3]` | Position in element array |
| `Type` | `Container` | Element type: Container, Column, Row, Label, TextBox, TextArea, Scrollable, Scene, Spacer, Flex, Image, Opacity, Grid, Popup, Transform, Cursor |
| `"Name"` | `"Sidebar.Root"` | Element ID name from `[ElementId]` attributes |
| `x,y wxh` | `0,0 240x818` | Position and size in pixels |
| `bg:#RRGGBB` | `bg:#1E1E23` | Background color (omitted if transparent) |
| `radius=N` | `radius=12` | Border radius |
| `pad=...` | `pad=0,12,0,12` | Padding (single value if uniform) |
| `spacing=N` | `spacing=8` | Child spacing |
| `align=...` | `align=center` | Alignment (omitted if Min,Min) |
| `margin=...` | `margin=532,0,0,176` | Margin (TRBL) |
| `"text"` | `"Send Code"` | Label text (truncated at 50 chars) |
| `text="..."` | `text=""` | TextBox/TextArea current value |
| `placeholder="..."` | `placeholder="Type..."` | Placeholder text |
| `size=N` | `size=14` | Font size |
| `color=#RRGGBB` | `color=#8C8C96` | Text color (omitted if white) |
| `scroll=N/N` | `scroll=0/678` | Scroll offset / content height |
| `NxN` | `0x24` | Spacer dimensions |
| `flex=N` | `flex=2` | Flex factor (omitted if 1.0) |
| `opacity=N` | `opacity=0.5` | Opacity value |
| `asset=Name` | `asset=Icon` | Image asset name |
| `password` | `password` | Password mode |
| State flags | `[hovered]` | `[hovered]` `[pressed]` `[focused]` `[down]` `[dragging]` |

### Reading Tips

- **Indentation** = nesting depth (2 spaces per level)
- **No name** = anonymous element (no ElementId assigned)
- **Container with no children** = colored rectangle (separator, dot, avatar circle)
- **Spacer `0x24`** = vertical gap of 24px; `10x0` = horizontal gap of 10px
- **Flex with no number** = flex factor 1.0 (default)
- **State flags** only appear on elements that have an ID
- **Margin** is used for absolute positioning within a Container (e.g., overlays placed at specific coordinates)
