# Add Non-Destructive Layer Opacity

## Context
The sprite editor has no per-layer opacity control. Opacity should be a non-destructive property on all layers and groups, applied at composite time (not baked into pixels), saved in `.sprite` files, and editable in the inspector.

## Files to Modify

| File | Change |
|------|--------|
| `noz/editor/src/sprite/SpriteNode.cs` | Add `Opacity` property + clone support |
| `noz/editor/src/sprite/SpriteDocument.File.cs` | Save/load `opacity` keyword for groups and layers |
| `noz/editor/src/sprite/pixel/PixelSpriteEditor.cs` | Apply accumulated opacity in `CompositeChildren` |
| `noz/editor/src/sprite/pixel/PixelSpriteEditor.Inspector.cs` | Add LAYER section with opacity slider |

## Step 1: Add Opacity to SpriteNode

In `SpriteNode.cs:37`, add after `Locked`:
```csharp
public float Opacity { get; set; } = 1.0f;
```

In `ClonePropertiesTo` (line 46), add:
```csharp
target.Opacity = Opacity;
```

This goes on the base class so both `PixelLayer` and `SpriteGroup` (and `SpritePath`) inherit it.

## Step 2: Serialize Opacity in .sprite Files

### Saving — `SaveGroup` (line 419)
After the opening brace (line 422), emit opacity when not default:
```csharp
if (group.Opacity < 1.0f)
    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
        "{0}opacity {1}", new string(' ', (depth + 1) * 2), group.Opacity));
```

### Saving — `SavePixelLayer` (line 438)
Same pattern after opening brace (line 442):
```csharp
if (layer.Opacity < 1.0f)
    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
        "{0}opacity {1}", new string(' ', (depth + 1) * 2), layer.Opacity));
```

### Loading — `ParseGroup` (line 68)
Add `else if` in the while loop (after line 84):
```csharp
else if (tk.ExpectIdentifier("opacity"))
    group.Opacity = tk.ExpectFloat(1.0f);
```

### Loading — `ParseLayer` (line 96)
Capture opacity before the while loop, parse inside it, assign after creation:
```csharp
var opacity = 1.0f;
// in while loop:
else if (tk.ExpectIdentifier("opacity"))
    opacity = tk.ExpectFloat(1.0f);
// at line 128, add Opacity = opacity to the initializer
```

Backward-compatible: files without `opacity` default to 1.0.

## Step 3: Apply Opacity During Compositing

In `PixelSpriteEditor.cs:366`, change `CompositeChildren` to accept accumulated opacity:
```csharp
private void CompositeChildren(SpriteNode parent, in RectInt epr, float parentOpacity = 1.0f)
```

For groups (line 372-375), multiply and pass down:
```csharp
CompositeChildren(group, epr, parentOpacity * group.Opacity);
```

For pixel layers, compute effective alpha per pixel:
```csharp
var layerOpacity = parentOpacity * layer.Opacity;
// in pixel loop, replace src.A usage:
var effectiveA = (int)(src.A * layerOpacity);
if (effectiveA == 0) continue;
var sa = effectiveA / 255f;
// when dst.A == 0: dst = new Color32(src.R, src.G, src.B, (byte)effectiveA);
```

Update call site (line 357): `CompositeChildren(Document.Root, epr, 1.0f);`

## Step 4: Inspector UI

In `PixelSpriteEditor.Inspector.cs`, add `WidgetId`:
```csharp
public static partial WidgetId LayerOpacity { get; }
```

Add LAYER section in `InspectorUI()` between `SpriteInspectorUI()` and BRUSH:
```csharp
if (SelectedNode is SpriteGroup or PixelLayer)
{
    using (Inspector.BeginSection("LAYER"))
    {
        if (!Inspector.IsSectionCollapsed)
        {
            using (Inspector.BeginProperty("Opacity"))
            {
                var opacity = SelectedNode.Opacity;
                if (UI.Slider(WidgetIds.LayerOpacity, ref opacity))
                {
                    Undo.Record(Document);
                    SelectedNode.Opacity = Math.Clamp(opacity, 0f, 1f);
                    InvalidateComposite();
                }
            }
        }
    }
}
```

`UI.Slider` defaults to 0–1 range. Undo is handled by `Undo.Record(Document)`.

## Verification
1. Build: `dotnet build noz/editor/editor.csproj`
2. Open a pixel sprite with groups and layers
3. Select a group or layer → verify LAYER section appears with Opacity slider
4. Drag slider → verify canvas updates non-destructively
5. Save and reopen → verify opacity persists
6. Verify old files without opacity still load (backward compat)
