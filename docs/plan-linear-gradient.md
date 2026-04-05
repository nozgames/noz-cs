# Linear Gradient Fill Support for Sprite Editor

## Context
Currently paths only support flat `Color32` fills. We want to add linear gradient fills (2 colors) with draggable handle overlays, matching Figma-style UX. Radial gradients are a future extension but we'll structure data to accommodate them.

## Critical Files
| File | Change |
|------|--------|
| `noz/editor/src/sprite/SpriteFill.cs` | **NEW** — `SpriteFillType` enum + `SpriteFillGradient` struct |
| `noz/editor/src/sprite/SpritePath.cs` | Add `FillType` + `FillGradient` properties |
| `noz/editor/src/sprite/SpriteLayerProcessor.cs` | Extend `LayerPathResult` with gradient data + pass-through |
| `noz/editor/src/sprite/SpriteEditor.Mesh.cs` | Per-vertex gradient color on tessellated mesh |
| `noz/editor/src/Rasterizer.cs` | Per-pixel gradient color during scanline fill |
| `noz/editor/src/sprite/SpriteDocument.Export.cs` | Pass gradient data to rasterizer |
| `noz/editor/src/sprite/SpriteDocument.File.cs` | Parse/save `gradient` keyword |
| `noz/editor/src/sprite/PathClipboardData.cs` | Copy/paste gradient data |
| `noz/editor/src/UI/ColorPicker.cs` | Add `Linear` mode with two-stop color editing |
| `noz/editor/src/sprite/SpriteEditor.cs` | Inspector UI for gradient fill button, gradient handle overlay drawing |
| `noz/editor/src/sprite/SpriteEditor.Tools.cs` | Gradient handle drag interaction |
| `noz/editor/src/sprite/SpriteEditor.Gradient.cs` | **NEW** — partial class for gradient handle drawing + hit testing + drag tool |

---

## Phase 1: Data Model

### 1.1 Create `noz/editor/src/sprite/SpriteFill.cs`

```csharp
public enum SpriteFillType : byte
{
    Solid,
    Linear,
    // Radial,  // future
}

public struct SpriteFillGradient
{
    public Vector2 Start;      // path-local coordinates
    public Vector2 End;        // path-local coordinates
    public Color32 StartColor;
    public Color32 EndColor;
}
```

### 1.2 Modify `SpritePath.cs`

Add after `FillColor` (line 43):
```csharp
public SpriteFillType FillType { get; set; } = SpriteFillType.Solid;
public SpriteFillGradient FillGradient { get; set; }
```

Add helper to initialize default gradient from current bounds:
```csharp
public void InitializeDefaultGradient()
{
    var b = LocalBounds;
    FillGradient = new SpriteFillGradient
    {
        Start = new Vector2(b.Center.X, b.Top),
        End = new Vector2(b.Center.X, b.Bottom),
        StartColor = FillColor,
        EndColor = Color32.White,
    };
}
```

Update `Clone()` and `SplitAtAnchors()` to copy `FillType` and `FillGradient`.

### 1.3 Extend `LayerPathResult` in `SpriteLayerProcessor.cs`

```csharp
internal readonly struct LayerPathResult(
    PathsD Contours, Color32 Color, bool IsStroke,
    SpriteFillType FillType = SpriteFillType.Solid,
    SpriteFillGradient Gradient = default,
    Matrix3x2 GradientTransform = default)
{
    public readonly PathsD Contours = Contours;
    public readonly Color32 Color = Color;
    public readonly bool IsStroke = IsStroke;
    public readonly SpriteFillType FillType = FillType;
    public readonly SpriteFillGradient Gradient = Gradient;
    public readonly Matrix3x2 GradientTransform = GradientTransform;
}
```

Update `ProcessLayer()` — when emitting fill results (lines 167, 191), pass `path.FillType`, `path.FillGradient`, and `path.PathTransform`. Stroke results stay `Solid`. When re-wrapping results in `TrimOverlaps` and boolean ops, preserve `FillType`/`Gradient`/`GradientTransform`.

---

## Phase 2: File Format

### 2.1 Modify `SpriteDocument.File.cs`

**Parsing** — add after the `fill` case (line 92):
```csharp
else if (tk.ExpectIdentifier("gradient"))
{
    if (tk.ExpectIdentifier("linear"))
    {
        tk.ExpectColor(out var c1);
        var x1 = tk.ExpectFloat();
        var y1 = tk.ExpectFloat();
        tk.ExpectColor(out var c2);
        var x2 = tk.ExpectFloat();
        var y2 = tk.ExpectFloat();
        path.FillType = SpriteFillType.Linear;
        path.FillGradient = new SpriteFillGradient
        {
            StartColor = c1.ToColor32(), Start = new Vector2(x1, y1),
            EndColor = c2.ToColor32(), End = new Vector2(x2, y2),
        };
    }
}
```

**Saving** — in `SavePath()` (line 294), after writing `fill`:
```csharp
if (path.FillType == SpriteFillType.Linear)
{
    var g = path.FillGradient;
    writer.WriteLine($"{propIndent}gradient linear {FormatColor(g.StartColor)} {g.Start.X} {g.Start.Y} {FormatColor(g.EndColor)} {g.End.X} {g.End.Y}");
}
```

The `fill` line is still written as fallback (using `StartColor`) for backward compat.

---

## Phase 3: Clipboard

### 3.1 Modify `PathClipboardData.cs`

Add `SpriteFillType FillType` and `SpriteFillGradient FillGradient` to `PathData`. Capture in constructor, restore in `PasteAsPaths()`.

---

## Phase 4: Editor Mesh Rendering

### 4.1 Modify `SpriteEditor.Mesh.cs`

**`MeshSlotData`** — add `SpriteFillType FillType` and `SpriteFillGradient Gradient` and `Matrix3x2 GradientTransform`.

**`EmitTessellationTo`** — add gradient params. After emitting vertices, if `FillType == Linear`:
```csharp
var gradStart = Vector2.Transform(gradient.Start, gradientTransform);
var gradEnd = Vector2.Transform(gradient.End, gradientTransform);
var axis = gradEnd - gradStart;
var axisSqLen = Vector2.Dot(axis, axis);
for (int v = startVert; v < endVert; v++)
{
    var pos = new Vector2(vertices[v].Position.X, vertices[v].Position.Y);
    float t = axisSqLen > 0 ? Math.Clamp(Vector2.Dot(pos - gradStart, axis) / axisSqLen, 0, 1) : 0;
    vertices[v] = vertices[v] with { Color = Color32.Lerp(gradient.StartColor, gradient.EndColor, t).ToColor() };
}
```

**`DrawMesh`** — for gradient slots, use `Graphics.SetColor(Color.White.WithAlpha(Workspace.XrayAlpha))` instead of `slot.FillColor`.

**`TessellateLayer`** — pass `result.FillType`, `result.Gradient`, `result.GradientTransform` through to tessellation.

---

## Phase 5: Export Rasterizer

### 5.1 Modify `Rasterizer.cs`

Add new overload:
```csharp
public static void Fill(PathsD paths, PixelData<Color32> target, RectInt targetRect,
    Vector2Int sourceOffset, int dpi, SpriteFillType fillType, Color32 color,
    SpriteFillGradient gradient, Matrix3x2 gradientTransform)
```

For `Linear` fills, at the per-pixel compositing step (line 118-138):
- Pre-compute gradient start/end in pixel space (transform by `gradientTransform`, then scale by `dpi` and offset by `sourceOffset`)
- Pre-compute axis vector and inverse dot product
- Per pixel: project `(px, py)` onto gradient axis to get `t`, lerp between `StartColor`/`EndColor` to get `color`, then blend as before

Keep the original `Fill(PathsD, ..., Color32)` as a convenience overload that delegates with `SpriteFillType.Solid`.

### 5.2 Modify `SpriteDocument.Export.cs`

Pass `result.FillType`, `result.Gradient`, `result.GradientTransform` to the new `Rasterizer.Fill` overload.

---

## Phase 6: Color Picker UI

### 6.1 Modify `ColorPicker.cs`

Add `Linear` to `ColorMode` enum (after `Palette`).

Add state:
```csharp
private static int _gradientActiveStop;  // 0 or 1
private static SpriteFillGradient _gradient;
```

Add `ElementId.ModeLinear` widget ID.

Add mode toggle button in the mode row (alongside None/Color/Palette).

When in `Linear` mode:
- Show two color swatches at the top (one per stop), clickable to select which stop is active
- The selected stop's color feeds the HSV picker below
- Editing HSV updates the active stop's color in `_gradient`

Add API for the sprite editor to open/read gradient state:
```csharp
internal static void Open(WidgetId id, SpriteFillType fillType, Color32 solidColor, SpriteFillGradient gradient)
internal static SpriteFillType ResultFillType { get; }
internal static SpriteFillGradient ResultGradient { get; }
internal static int ActiveGradientStop { get; }
```

---

## Phase 7: Gradient Handle Overlay

### 7.1 Create `noz/editor/src/sprite/SpriteEditor.Gradient.cs`

New partial class containing:

**`IsGradientOverlayVisible()`** — returns true when:
- Exactly one path selected
- That path's `FillType == Linear`
- The fill color picker popup is open

**`DrawGradientOverlay()`** — called from the editor's overlay drawing:
- Get the single selected path
- Transform `FillGradient.Start` and `.End` from path-local to screen via `PathTransform * Document.Transform`
- Draw a red line between them using `Gizmos.DrawLine`
- Draw handle circles at each endpoint: unselected anchor style but filled with the gradient stop color
- The active stop (matching `ColorPicker.ActiveGradientStop`) renders as selected

**`HitTestGradientHandle(Vector2 localMousePos)`** — returns 0, 1, or -1:
- Transform gradient endpoints to document space
- Squared distance check against mouse pos (same radius as anchor hit testing)

**`HandleGradientDrag(Vector2 localMousePos)`** — called from `HandleDragStart()`:
- If gradient overlay is visible and a handle is hit, start a `GradientHandleDragTool`
- Clicking a handle also sets `ColorPicker.ActiveGradientStop`

**`GradientHandleDragTool`** (inner class or separate):
- On begin: snapshot initial gradient endpoint position, record undo
- On drag: transform mouse delta to path-local space, update the endpoint in `path.FillGradient`
- Mark mesh dirty on each frame

### 7.2 Modify `SpriteEditor.Tools.cs`

In `HandleDragStart()` (line 77), add before V-mode/A-mode checks:
```csharp
if (IsGradientOverlayVisible() && HandleGradientDrag(localMousePos))
    return;
```

### 7.3 Modify `SpriteEditor.cs`

In the overlay drawing section, call `DrawGradientOverlay()` when visible.

In the inspector UI (line ~1568), update the fill color button to be gradient-aware:
- When fill type is Linear, show a gradient preview swatch instead of flat color
- Open color picker with gradient state
- Read back gradient changes from picker

Track `CurrentFillType` and `CurrentFillGradient` on `SpriteDocument` alongside `CurrentFillColor`, synced from selection.

---

## Phase 8: Edge Cases

- **Selection sync**: When path selected, sync `FillType`/`FillGradient` to `Document.Current*` (in `OnSelectionChanged`)
- **New paths**: Pen/Rectangle/Circle tools create paths with `Solid` fill. User switches to gradient via picker.
- **Multi-path selection**: Gradient handles hidden, picker can still switch mode but handles only appear with 1 path selected
- **Onion skin**: Ignore gradient, use `FillColor` (start color fallback) — gradient fidelity not needed for onion skin
- **Boolean ops**: When subtract/clip re-wraps `LayerPathResult`, preserve the gradient fields from the original result

---

## Verification

1. Create a path, switch fill to Linear in color picker — gradient appears in editor preview
2. Drag gradient handles — gradient direction updates in real-time
3. Pick different colors for each stop via the color picker swatches
4. Transform the path (move/rotate/scale in V-mode) — gradient transforms with it
5. Save and reload the sprite file — gradient persists
6. Copy/paste the path — gradient preserved
7. Export/build atlas — gradient renders correctly in rasterized output
8. Select multiple paths — gradient handles hidden
9. Undo/redo gradient changes — works correctly (undo system captures full document state)
