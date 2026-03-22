#!/usr/bin/env python3
"""
Migrate .sprite files from V1 (flat paths) to V2 (layer hierarchy with {} blocks).

Usage:
    python migrate_sprites_v2.py <directory> [--dry-run]

Recursively finds all .sprite files and converts V1 format to V2.
Already-V2 files (containing "layer" with "{") are skipped.
"""

import os
import sys
import re

def is_v2(text):
    """Check if file is already V2 format (uses {} blocks)."""
    return bool(re.search(r'\{', text))

def parse_v1(text):
    """Parse V1 sprite format into structured data."""
    lines = text.split('\n')

    top_level = []  # (key, value) for edges, skeleton, generate blocks
    frames = []
    current_frame = {'hold': 0, 'paths': []}
    current_path = None
    in_generate = False
    generate_lines = []

    i = 0
    while i < len(lines):
        line = lines[i].strip()
        i += 1

        if not line:
            continue

        if in_generate:
            if line.startswith('frame') or line.startswith('path') or line.startswith('edges') or line.startswith('skeleton'):
                in_generate = False
                top_level.append(('generate', '\n'.join(generate_lines)))
                i -= 1  # re-process this line
                continue
            generate_lines.append(lines[i-1].rstrip())
            continue

        if line.startswith('generate'):
            in_generate = True
            generate_lines = [lines[i-1].rstrip()]
            continue

        if line.startswith('edges'):
            top_level.append(('edges', line))
            continue

        if line.startswith('skeleton'):
            top_level.append(('skeleton', line))
            continue

        if line == 'frame' or line.startswith('frame '):
            # Save current frame if it has paths
            if current_path:
                current_frame['paths'].append(current_path)
                current_path = None
            if current_frame['paths']:
                frames.append(current_frame)

            hold = 0
            rest = line[5:].strip()
            if rest.startswith('hold'):
                try:
                    hold = int(rest.split()[1])
                except (IndexError, ValueError):
                    pass
            current_frame = {'hold': hold, 'paths': []}
            continue

        if line == 'path':
            if current_path:
                current_frame['paths'].append(current_path)
            current_path = {'props': [], 'anchors': []}
            continue

        if current_path is not None:
            if line.startswith('fill'):
                current_path['props'].append(line)
            elif line.startswith('stroke'):
                current_path['props'].append(line)
            elif line.startswith('subtract'):
                current_path['props'].append(line)
            elif line.startswith('clip'):
                current_path['props'].append(line)
            elif line.startswith('layer') or line.startswith('bone'):
                pass  # legacy, skip
            elif line.startswith('anchor'):
                current_path['anchors'].append(line)
            else:
                # Unknown line in path context — might be end of path
                pass

    if in_generate:
        top_level.append(('generate', '\n'.join(generate_lines)))

    if current_path:
        current_frame['paths'].append(current_path)
    if current_frame['paths']:
        frames.append(current_frame)

    return top_level, frames

def write_v2(top_level, frames):
    """Generate V2 format string."""
    out = []

    # Top-level properties
    for key, value in top_level:
        if key == 'generate':
            out.append(value)
        else:
            out.append(value)

    if top_level:
        out.append('')

    if len(frames) <= 1:
        # Single frame: paths at top level (implicit root layer)
        if frames:
            for path in frames[0]['paths']:
                write_path(out, path, indent=0)
    else:
        # Multi-frame: one layer per frame + frame visibility blocks
        for fi, frame in enumerate(frames):
            layer_name = f'Frame {fi + 1}'
            out.append(f'layer "{layer_name}" {{')
            for path in frame['paths']:
                write_path(out, path, indent=2)
            out.append('}')
            out.append('')

        for fi, frame in enumerate(frames):
            layer_name = f'Frame {fi + 1}'
            out.append('frame {')
            out.append(f'  visible "{layer_name}"')
            if frame['hold'] > 0:
                out.append(f'  hold {frame["hold"]}')
            out.append('}')
            out.append('')

    return '\n'.join(out) + '\n'

def write_path(out, path, indent=2):
    """Write a single path in V2 format."""
    pad = ' ' * indent
    inner = ' ' * (indent + 2)

    out.append(f'{pad}path {{')
    for prop in path['props']:
        out.append(f'{inner}{prop}')
    for anchor in path['anchors']:
        out.append(f'{inner}{anchor}')
    out.append(f'{pad}}}')
    out.append('')

def migrate_file(filepath, dry_run=False):
    """Migrate a single .sprite file. Returns True if converted."""
    with open(filepath, 'r', encoding='utf-8') as f:
        text = f.read()

    if is_v2(text):
        return False

    # Skip empty files
    stripped = text.strip()
    if not stripped:
        return False

    top_level, frames = parse_v1(text)

    if not frames:
        return False

    v2_text = write_v2(top_level, frames)

    if dry_run:
        print(f'  Would convert: {filepath}')
        return True

    with open(filepath, 'w', encoding='utf-8', newline='\n') as f:
        f.write(v2_text)

    return True

def main():
    if len(sys.argv) < 2:
        print(f'Usage: {sys.argv[0]} <directory> [--dry-run]')
        sys.exit(1)

    directory = sys.argv[1]
    dry_run = '--dry-run' in sys.argv

    if not os.path.isdir(directory):
        print(f'Error: {directory} is not a directory')
        sys.exit(1)

    converted = 0
    skipped = 0
    errors = 0

    for root, dirs, files in os.walk(directory):
        for fname in sorted(files):
            if not fname.endswith('.sprite'):
                continue

            filepath = os.path.join(root, fname)
            try:
                if migrate_file(filepath, dry_run):
                    converted += 1
                    if not dry_run:
                        print(f'  Converted: {filepath}')
                else:
                    skipped += 1
            except Exception as e:
                errors += 1
                print(f'  ERROR: {filepath}: {e}')

    action = 'Would convert' if dry_run else 'Converted'
    print(f'\n{action}: {converted}, Skipped (already V2 or empty): {skipped}, Errors: {errors}')

if __name__ == '__main__':
    main()
