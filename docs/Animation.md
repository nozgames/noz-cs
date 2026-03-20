# Animation File Format (.anim)

## Format

```
skeleton "skeleton_name"
bone "bone_name"
bone "bone_name"
...

frame hold 7
bone 2 position 0.1 0.2
bone 3 rotation 45.0
frame event "step"
bone 1 position 0.5 -0.3 rotation 12.0
frame
bone 4 rotation -30.0
```

### Header

- `skeleton "name"` — which skeleton this animation targets
- `bone "name"` — one line per bone, in order (defines the bone index mapping)

### Frames

- `frame` — starts a new frame
- `frame hold N` — frame repeats N extra times
- `frame event "name"` — frame triggers a named event

### Bone Transforms (within a frame)

- `bone INDEX position X Y` — position delta
- `bone INDEX rotation DEGREES` — rotation delta
- `bone INDEX scale S` — uniform scale
- `bone INDEX position X Y rotation DEGREES` — combined

Only bones with non-zero transforms need to be listed.

## Migrating from Old Format

The old format used single-letter keywords. To migrate `.anim` files:

| Old | New |
|-----|-----|
| `s "name"` | `skeleton "name"` |
| `b "name"` | `bone "name"` |
| `f` | `frame` |
| `h N` | `hold N` |
| `e "name"` | `event "name"` |
| `b N` | `bone N` |
| `p X Y` | `position X Y` |
| `r DEG` | `rotation DEG` |

### Quick migration with sed

```bash
for f in assets/animation/*.anim; do
  sed -i \
    -e 's/^s "/skeleton "/g' \
    -e 's/^b "/bone "/g' \
    -e 's/^f$/frame/g' \
    -e 's/^f h /frame hold /g' \
    -e 's/^f e /frame event /g' \
    -e 's/^b \([0-9]\)/bone \1/g' \
    -e 's/ p \([-0-9]\)/ position \1/g' \
    -e 's/ r \([-0-9]\)/ rotation \1/g' \
    "$f"
done
```
