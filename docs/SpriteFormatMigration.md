# Sprite Format Migration: V1 → V2

## Overview

The sprite file format changed from a flat path list (V1) to a layer-based hierarchy (V2) with `{}` block delimiters. V1 files must be converted to V2 before the editor can load them.

## Format Differences

### V1 Format (old)
```
frame
hold 2

path
fill #FF0000
stroke #000000 3
anchor -0.094 -0.155
anchor 0.099 -0.153
anchor 0.205 -0.004

path
subtract true
fill #000000
anchor 0.2 0.2
anchor 0.8 0.8
anchor 0.5 0.9

frame

path
fill #FFFFFF
anchor -0.063 -0.096
anchor 0.071 -0.107
```

- Paths are flat (no hierarchy)
- Frames own independent geometry
- `frame` keyword starts a frame, paths follow until next `frame`
- No `{}` delimiters
- Optional: `layer "name"` and `bone "name"` per-path (legacy, ignored)

### V2 Format (new)
```
layer "base" {
  path "body" {
    fill #FF0000
    stroke #000000 3
    anchor -0.094 -0.155
    anchor 0.099 -0.153
    anchor 0.205 -0.004
  }
  path {
    subtract true
    fill #000000
    anchor 0.2 0.2
    anchor 0.8 0.8
    anchor 0.5 0.9
  }
}

frame {
  visible "base"
  hold 2
}
```

- Paths live inside named layers (nested hierarchy)
- `{}` block delimiters (not indentation)
- Frames are visibility states (which layers are visible), not independent geometry
- Paths can have optional names: `path "name" { }`
- New property: `open true` for open (non-closed) paths

## Migration Rules

### Single-frame sprites (no `frame` keyword, or single `frame`)
1. Create a layer named `"Layer 1"`
2. Move all paths into it
3. No `frame` blocks needed (all layers visible by default)

### Multi-frame sprites
1. For each frame, create a layer named `"Frame N"` (1-indexed)
2. Move that frame's paths into the layer
3. Create `frame` blocks with `visible` listing the corresponding layer
4. Copy `hold` values from original frames

### Path properties
All path properties carry over unchanged:
- `fill` → `fill`
- `stroke` → `stroke`
- `subtract true` → `subtract true`
- `clip true` → `clip true`
- `anchor X Y [CURVE]` → `anchor X Y [CURVE]`

### Properties to drop
- `layer "name"` (legacy per-path layer — was ignored)
- `bone "name"` (legacy per-path bone — was ignored)

### Top-level properties (unchanged)
- `edges (T,L,B,R)` → keep as-is
- `skeleton "name"` → keep as-is
- `generate` block → keep as-is

## Migration Script

Located at `noz/tools/migrate_sprites_v2.py`. Requires Python 3 (use `uv run` on systems with uv).

### Usage

```bash
# Dry run — shows what would be converted without changing files
uv run noz/tools/migrate_sprites_v2.py assets/sprite --dry-run

# Convert all sprites in a directory (recursive)
uv run noz/tools/migrate_sprites_v2.py assets/sprite
uv run noz/tools/migrate_sprites_v2.py noz/editor/assets/sprite
```

The script:
- Recursively finds all `.sprite` files
- Skips files already in V2 format (detected by `layer "..." {` pattern)
- Skips empty files
- Converts V1 → V2 in place
- Single-frame sprites get one `layer "Layer 1" { ... }`
- Multi-frame sprites get `layer "Frame N" { ... }` per frame + `frame { visible "Frame N" }` blocks
- Preserves all path properties (fill, stroke, subtract, clip, anchors with curves)
- Drops legacy `layer` and `bone` per-path properties (were already ignored)
- Preserves top-level `edges`, `skeleton`, `generate` blocks

## Notes

- V1 files will NOT load in the editor after this migration — conversion is required
- The editor always saves in V2 format
- Layer names are preserved across save/load cycles
- Path names are optional and preserved if set
- Animation frames reference layers by name in `visible` blocks
