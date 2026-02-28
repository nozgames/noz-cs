# NoZ UI System

Immediate-mode UI system. No retained widget objects — every element is drawn every frame, layout is computed automatically. All methods live on the static `UI` class in the `NoZ` namespace.

## Screen Structure

Each screen is a `static partial class` with a nested `ElementId` class and a `Draw()` method.

```csharp
public static partial class MyScreen
{
    [ElementId("Root")]
    [ElementId("SubmitButton")]
    [ElementId("NameInput")]
    [ElementId("ItemBase", 100)]   // reserves 100 IDs for a dynamic list
    private static partial class ElementId { }

    private static readonly ContainerStyle CardStyle = new()
    {
        Width = 400,
        Height = Size.Fit,
        Align = Align.Center,
        Color = new Color(30, 30, 35),
        BorderRadius = BorderRadius.Circular(12),
        Padding = new EdgeInsets(32, 32, 32, 32),
    };

    private static readonly LabelStyle TitleStyle = new()
    {
        FontSize = 24,
        Color = Color.White,
        Align = Align.Center,
    };

    public static void Draw()
    {
        using (UI.BeginContainer(ElementId.Root, new ContainerStyle { Align = Align.Center }))
        {
            using (UI.BeginColumn(CardStyle))
            {
                UI.Label("Hello", TitleStyle);
            }
        }
    }
}
```

`[ElementId("Name")]` attributes generate `public const int` fields via source generator. `[ElementId("Base", 100)]` reserves a range — use `ElementId.ItemBase + i` in loops.

## Layout Containers

Three container types control child layout direction:

| Method | Children layout |
|--------|----------------|
| `BeginContainer` / `EndContainer` | No layout direction — children overlay each other |
| `BeginColumn` / `EndColumn` | Vertical (top to bottom) |
| `BeginRow` / `EndRow` | Horizontal (left to right) |

All return disposable auto-structs for the `using()` pattern:

```csharp
using (UI.BeginRow(ElementId.MyRow, rowStyle))
{
    // children laid out left-to-right
}
```

Overloads: `()`, `(id)`, `(style)`, `(id, style)`.

## Sizing

```csharp
Size.Default          // fill available space in parent's layout axis
Size.Fit              // shrink to fit content
new Size(200)         // fixed 200 pixels
Size.Percent(0.5f)    // 50% of parent
```

Set via `ContainerStyle`:
```csharp
Width = 240,          // fixed 240px
Height = Size.Fit,    // shrink to content
```

Both `Width` and `Height` are `Size` — when omitted they default to `Size.Default`.

## Flex & Spacers

`Flex` takes remaining space in a Row or Column (like CSS `flex-grow`):

```csharp
using (UI.BeginRow())
{
    using (UI.BeginContainer(sidebarStyle)) { ... }  // fixed width
    using (UI.BeginFlex()) { ... }                    // fills remaining
}
```

`Spacer` inserts a fixed gap:
```csharp
UI.Spacer(16);  // 16px gap
```

`UI.Spacer(0)` in a Row pushes the next element to the far right (acts as a flexible spacer between siblings).

## Alignment

```csharp
Align.Min       // left / top
Align.Center    // center
Align.Max       // right / bottom
```

`Align2` has X and Y axes. Implicit conversion from `Align` sets both:

```csharp
Align = Align.Center,                            // both axes centered
Align = new Align2(Align.Min, Align.Center),      // left-aligned, vertically centered
```

Container `Align` positions children within the container. Label `Align` positions text within the label bounds.

## Elements

### Label

```csharp
UI.Label("Hello World", style);
UI.WrappedLabel(elementId, "Long text that wraps", style);  // needs ID for layout cache
```

### TextBox (single-line input)

```csharp
if (UI.TextBox(ElementId.EmailBox, inputStyle, "placeholder text"))
{
    // text changed this frame
}

// Read/write text
var text = UI.GetTextBoxText(ElementId.EmailBox);
UI.SetTextBoxText(ElementId.EmailBox, "new value");
```

### TextArea (multi-line input)

```csharp
if (UI.TextArea(ElementId.NotesArea, textAreaStyle, "placeholder"))
{
    // text changed
}

var text = UI.GetTextAreaText(ElementId.NotesArea);
UI.SetTextAreaText(ElementId.NotesArea, "new value");
```

### Image

```csharp
UI.Image(sprite, imageStyle);
UI.Image(texture, imageStyle);
```

### Scene (viewport)

```csharp
UI.Scene(ElementId.GameScene, camera, () =>
{
    // draw world content here
}, new SceneStyle { Color = new Color(20, 20, 25) });
```

### Empty Container (rectangle)

```csharp
UI.Container(new ContainerStyle
{
    Width = 32,
    Height = 32,
    Color = new Color(70, 130, 230),
    BorderRadius = BorderRadius.Circular(16),  // circle
});
```

## Interaction

### Hover & Press

```csharp
var hovered = UI.IsHovered(ElementId.MyButton);
using (UI.BeginContainer(ElementId.MyButton, hovered ? hoverStyle : normalStyle))
{
    UI.Label("Click Me", labelStyle);
}

if (UI.WasPressed(ElementId.MyButton))
{
    // handle click
}
```

`IsHovered()` and `WasPressed()` also have no-arg overloads that check the most recently begun element.

### Focus

```csharp
UI.SetFocus(ElementId.Input);
UI.ClearFocus();
bool focused = UI.IsFocused(ElementId.Input);
bool anyFocus = UI.HasFocus();
```

### Scrolling

```csharp
float offset = UI.GetScrollOffset(ElementId.MyScroll);
UI.SetScrollOffset(ElementId.MyScroll, 0);  // scroll to top
```

## Scrollable

Wraps content in a vertically scrollable region. Typically inside a `Flex` so it gets a bounded height:

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

## Style Reference

### ContainerStyle

```csharp
new ContainerStyle
{
    Width = Size.Default,              // Size — default fills parent axis
    Height = Size.Default,             // Size
    MinWidth = 0,                      // float
    MinHeight = 0,                     // float
    MaxWidth = float.MaxValue,         // float
    MaxHeight = float.MaxValue,        // float
    Align = Align.Min,                 // Align2 — child positioning
    Margin = EdgeInsets.Zero,          // EdgeInsets — outer spacing
    Padding = EdgeInsets.Zero,         // EdgeInsets — inner spacing
    Color = Color.Transparent,         // Color — background fill
    BorderRadius = BorderRadius.Zero,  // BorderRadius — rounded corners
    BorderWidth = 0,                   // float
    BorderColor = Color.Transparent,   // Color
    Spacing = 0,                       // float — gap between children (Column/Row)
    Clip = false,                      // bool — clip children to bounds
    Order = 0,                         // ushort — draw order
}
```

`ContainerStyle.Fit` is a preset with `Width = Size.Fit, Height = Size.Fit`.

### LabelStyle

```csharp
new LabelStyle
{
    FontSize = 16,                       // float
    Color = Color.White,                 // Color
    Align = new Align2(Align.Min, Align.Center),  // Align2
    Font = null,                         // Font? — null uses default
    Overflow = TextOverflow.Overflow,    // TextOverflow
    Order = 2,                           // ushort
}
```

`TextOverflow` enum: `Overflow`, `Ellipsis`, `Scale`, `Wrap`.

### TextBoxStyle

```csharp
new TextBoxStyle
{
    Height = Size.Default,               // Size
    FontSize = 16,                       // float
    BackgroundColor = Color.Transparent,  // Color
    TextColor = Color.White,             // Color
    PlaceholderColor = ...,              // Color
    SelectionColor = ...,               // Color
    BorderRadius = BorderRadius.Zero,    // BorderRadius
    BorderWidth = 0,                     // float
    BorderColor = Color.Transparent,     // Color
    FocusBorderRadius = BorderRadius.Zero, // BorderRadius — when focused
    FocusBorderWidth = 0,                // float — when focused
    FocusBorderColor = Color.Transparent, // Color — when focused
    Padding = EdgeInsets.Zero,           // EdgeInsets
    IsPassword = false,                  // bool — mask input
    Scope = InputScope.All,             // InputScope
}
```

`TextAreaStyle` is the same as `TextBoxStyle` without `IsPassword`.

### ImageStyle

```csharp
new ImageStyle
{
    Width = Size.Default,     // Size
    Height = Size.Default,    // Size
    Stretch = ImageStretch.Uniform,  // ImageStretch: None, Fill, Uniform
    Align = Align.Min,        // Align2
    Scale = 1.0f,             // float
    Color = Color.White,      // Color — tint
    BorderRadius = BorderRadius.Zero,
    Order = 1,
}
```

### ScrollableStyle

```csharp
new ScrollableStyle
{
    ScrollSpeed = 30f,
    Scrollbar = ScrollbarVisibility.Auto,  // Auto, Always, Never
    ScrollbarWidth = 8f,
    ScrollbarMinThumbHeight = 20f,
    ScrollbarTrackColor = ...,
    ScrollbarThumbColor = ...,
    ScrollbarThumbHoverColor = ...,
    ScrollbarPadding = 2f,
    ScrollbarBorderRadius = 4f,
}
```

### SceneStyle

```csharp
new SceneStyle
{
    Width = Size.Default,
    Height = Size.Default,
    Color = Color.Transparent,  // clear color
    SampleCount = 1,            // MSAA samples
}
```

### PopupStyle

```csharp
new PopupStyle
{
    Anchor = Align.Min,         // Align2 — anchor point on trigger element
    PopupAlign = Align.Min,     // Align2 — alignment of popup relative to anchor
    Spacing = 0,                // float — gap between anchor and popup
    ClampToScreen = false,      // bool
    AnchorRect = Rect.Zero,     // Rect — manual anchor rect
    MinWidth = 0,               // float
    AutoClose = true,           // bool — close on click outside
    Interactive = true,         // bool
}
```

### GridStyle

```csharp
new GridStyle
{
    Spacing = 0,           // float
    Columns = 3,           // int
    CellWidth = 100,       // float
    CellHeight = 100,      // float
    CellMinWidth = 0,      // float — auto-adjust columns
    CellHeightOffset = 0,  // float
    VirtualCount = 0,      // int — total items for virtualization
    StartIndex = 0,        // int — first visible item
}
```

### TransformStyle

```csharp
new TransformStyle
{
    Origin = new Vector2(0.5f, 0.5f),  // pivot point (0-1)
    Translate = Vector2.Zero,
    Rotate = 0,             // radians
    Scale = Vector2.One,
}
```

## Value Types

### EdgeInsets

Constructor order: **top, left, bottom, right** (TLBR).

```csharp
new EdgeInsets(8, 16, 8, 16)           // T=8, L=16, B=8, R=16
new EdgeInsets(12)                      // all sides 12
EdgeInsets.All(10)
EdgeInsets.Symmetric(8, 16)            // vertical=8, horizontal=16
EdgeInsets.Top(10)
EdgeInsets.Bottom(10)
EdgeInsets.Left(10)
EdgeInsets.Right(10)
EdgeInsets.LeftRight(16)
EdgeInsets.TopBottom(8)
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

### Color

```csharp
new Color(70, 130, 230)         // RGB (0-255 ints)
new Color(0, 0, 0, 128)         // RGBA
Color.White
Color.Black
Color.Transparent
```

## Advanced Elements

### Opacity

```csharp
using (UI.BeginOpacity(0.5f))
{
    // children drawn at 50% opacity
}
```

### Transform

```csharp
using (UI.BeginTransformed(new TransformStyle
{
    Rotate = MathF.PI / 4,  // 45 degrees
    Scale = new Vector2(1.2f, 1.2f),
}))
{
    // children rotated and scaled
}
```

### Popup

```csharp
using (UI.BeginPopup(ElementId.Menu, new PopupStyle
{
    Anchor = new Align2(Align.Min, Align.Max),
    AutoClose = true,
}))
{
    // popup content
}

if (UI.IsClosed())
    _menuOpen = false;
```

### Grid

```csharp
using (UI.BeginGrid(new GridStyle { Columns = 3, CellWidth = 100, CellHeight = 100, Spacing = 8 }))
{
    for (int i = 0; i < items.Count; i++)
    {
        // each item fills one cell
    }
}
```

### Cursor

```csharp
using (UI.BeginCursor(SystemCursor.Hand))
{
    // children show hand cursor on hover
}
```

## Common Patterns

### Hover Button

```csharp
var hovered = UI.IsHovered(ElementId.MyButton);
using (UI.BeginContainer(ElementId.MyButton, hovered ? hoverStyle : normalStyle))
{
    UI.Label("Click", labelStyle);
}
if (UI.WasPressed(ElementId.MyButton))
    DoAction();
```

### Fixed Sidebar + Flex Content

```csharp
using (UI.BeginRow())
{
    using (UI.BeginContainer(new ContainerStyle { Width = 240, Color = sidebarColor }))
    {
        Sidebar.Draw();
    }
    UI.Container(new ContainerStyle { Width = 1, Color = separatorColor });
    using (UI.BeginFlex())
    {
        ContentScreen.Draw();
    }
}
```

### Scrollable List

```csharp
using (UI.BeginFlex())
{
    using (UI.BeginScrollable(ElementId.ListScroll))
    {
        using (UI.BeginColumn())
        {
            for (int i = 0; i < items.Count; i++)
            {
                var id = ElementId.ItemBase + i;
                var hovered = UI.IsHovered(id);
                using (UI.BeginRow(id, hovered ? itemHoverStyle : itemStyle))
                {
                    UI.Label(items[i].Name, nameStyle);
                }
                if (UI.WasPressed(id))
                    SelectItem(items[i]);
            }
        }
    }
}
```

### Overlay / Modal

Later children in a `Container` render on top of earlier ones:

```csharp
using (UI.BeginContainer(ElementId.Root))
{
    // Main content
    DrawContent();

    // Modal overlay drawn on top
    if (_showModal)
    {
        using (UI.BeginContainer(ElementId.Backdrop, new ContainerStyle
        {
            Color = new Color(0, 0, 0, 128),
            Align = Align.Center,
        }))
        {
            DrawModalCard();
        }
        if (UI.WasPressed(ElementId.Backdrop))
            _showModal = false;
    }
}
```

### Push Element to Far End

Use `UI.Spacer(0)` as a flexible spacer in a Row:

```csharp
using (UI.BeginRow(rowStyle))
{
    UI.Label("Left", labelStyle);
    UI.Spacer(0);                       // pushes next element to the right
    UI.Label("Right", labelStyle);
}
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
