#!/usr/bin/env python3
"""
Migrate .vfx files from V1 (INI/PropertySet) to V2 (block-based with {} blocks).

Usage:
    python migrate_vfx_v2.py <directory> [--dry-run]

Recursively finds all .vfx files and converts V1 (INI) format to V2 (block).
Already-V2 files (not starting with '[') are skipped.
"""

import os
import sys
import re


def is_v1(text):
    """Check if file is V1 INI format (starts with [section])."""
    return text.lstrip().startswith('[')


def parse_ini(text):
    """Parse V1 INI format into sections dict: {section_name: {key: value}}."""
    sections = {}
    current = None
    for line in text.split('\n'):
        stripped = line.strip()
        if not stripped:
            continue
        m = re.match(r'^\[(.+)\]$', stripped)
        if m:
            current = m.group(1)
            sections[current] = {}
            continue
        if current is not None:
            eq = stripped.find('=')
            if eq >= 0:
                key = stripped[:eq].strip()
                val = stripped[eq+1:].strip()
                sections[current][key] = val
            else:
                # Key-only line (e.g. emitter names in [emitters])
                sections[current][stripped] = ''
    return sections


def convert_v1_to_v2(text):
    """Convert V1 INI format to V2 block format."""
    sections = parse_ini(text)

    # Global properties
    vfx = sections.get('vfx', {})
    duration = vfx.get('duration', '1')
    loop = vfx.get('loop', 'false')

    # Emitter names (from [emitters] section keys)
    emitter_names = list(sections.get('emitters', {}).keys())

    # Collect unique particles and their emitter associations
    particles = {}  # name -> section dict
    emitters = []   # (name, section_dict, particle_ref)

    for ename in emitter_names:
        if ename not in sections:
            continue
        esec = sections[ename]

        particle_ref = esec.get('particle', '')
        if not particle_ref:
            particle_ref = ename + '.particle'

        if particle_ref in sections and particle_ref not in particles:
            particles[particle_ref] = sections[particle_ref]

        emitters.append((ename, esec, particle_ref))

    # Build output
    lines = []
    lines.append(f'duration {duration}')
    lines.append(f'loop {loop}')

    # Write particle blocks
    for pname, psec in particles.items():
        lines.append('')
        lines.append(f'particle "{pname}" {{')
        # Duration is always written
        lines.append(f'  duration {psec.get("duration", "1")}')
        for key in ['size', 'speed', 'color', 'opacity', 'gravity', 'drag',
                     'rotation', 'rotationSpeed']:
            if key in psec:
                lines.append(f'  {key} {psec[key]}')
        if 'sprite' in psec and psec['sprite']:
            lines.append(f'  sprite "{psec["sprite"]}"')
        lines.append('}')

    # Write emitter blocks
    for ename, esec, particle_ref in emitters:
        lines.append('')
        lines.append(f'emitter "{ename}" {{')
        lines.append(f'  rate {esec.get("rate", "0")}')
        lines.append(f'  burst {esec.get("burst", "0")}')
        lines.append(f'  duration {esec.get("duration", "1")}')
        lines.append(f'  particle "{particle_ref}"')
        for key in ['angle', 'spawn', 'direction']:
            if key in esec:
                lines.append(f'  {key} {esec[key]}')
        if 'worldSpace' in esec and esec['worldSpace'].lower() == 'false':
            lines.append('  worldSpace false')
        lines.append('}')

    return '\n'.join(lines) + '\n'


def needs_v3(text):
    """Check if file needs V3 migration (has old angle/direction/spawn syntax)."""
    for line in text.split('\n'):
        stripped = line.strip()
        if stripped.startswith('angle '):
            return True
        if re.match(r'direction\s+[\[\(]', stripped):
            return True
        if re.match(r'spawn\s+[\[\(]', stripped):
            return True
    return False


def convert_angle_to_direction_spread(angle_str):
    """Convert old angle value to (direction, spread) pair.

    'angle [0, 360]' → spread 180 (omnidirectional)
    'angle [80, 100]' → direction 90, spread 10
    'angle 90' → direction 90, spread 0
    """
    angle_str = angle_str.strip()

    m = re.match(r'\[([^,]+),\s*([^\]]+)\]', angle_str)
    if m:
        lo = float(m.group(1))
        hi = float(m.group(2))
        center = (lo + hi) / 2
        half = (hi - lo) / 2
        if abs(half - 180) < 0.01:
            return None, half  # omnidirectional, no direction needed
        return center, half

    val = float(angle_str)
    return val, 0


def convert_vec2_direction_to_angle(dir_str):
    """Convert old direction vector to angle in degrees.

    'direction (0, -1)' → 270
    'direction [(0, 0.8), (0, 1)]' → [~90, 90]
    """
    import math

    def norm_angle(a):
        """Normalize angle to [0, 360)."""
        return a % 360

    # Single vector: (x, y)
    m = re.match(r'\(([^,]+),\s*([^)]+)\)', dir_str.strip())
    if m:
        x, y = float(m.group(1)), float(m.group(2))
        if abs(x) < 0.0001 and abs(y) < 0.0001:
            return None  # zero direction, not used
        angle = norm_angle(math.degrees(math.atan2(y, x)))
        return angle, 0

    # Range: [(x1, y1), (x2, y2)]
    m = re.match(r'\[\(([^,]+),\s*([^)]+)\),\s*\(([^,]+),\s*([^)]+)\)\]', dir_str.strip())
    if m:
        x1, y1 = float(m.group(1)), float(m.group(2))
        x2, y2 = float(m.group(3)), float(m.group(4))
        a1 = norm_angle(math.degrees(math.atan2(y1, x1)))
        a2 = norm_angle(math.degrees(math.atan2(y2, x2)))
        center = (a1 + a2) / 2
        half = abs(a2 - a1) / 2
        return center, half

    return None


def convert_spawn_to_box(spawn_str):
    """Convert old spawn [(minX, minY), (maxX, maxY)] to spawn box { ... } syntax."""
    spawn_str = spawn_str.strip()

    # Single point: (x, y)
    m = re.match(r'^\(([^,]+),\s*([^)]+)\)$', spawn_str)
    if m:
        x, y = float(m.group(1)), float(m.group(2))
        if abs(x) < 0.0001 and abs(y) < 0.0001:
            return None  # default point, remove line
        return f'spawn point {{ offset ({fmt(x)}, {fmt(y)}) }}'

    # Range: [(minX, minY), (maxX, maxY)]
    m = re.match(r'^\[\(([^,]+),\s*([^)]+)\),\s*\(([^,]+),\s*([^)]+)\)\]$', spawn_str)
    if m:
        min_x, min_y = float(m.group(1)), float(m.group(2))
        max_x, max_y = float(m.group(3)), float(m.group(4))
        size_x = max_x - min_x
        size_y = max_y - min_y
        off_x = (max_x + min_x) / 2
        off_y = (max_y + min_y) / 2

        parts = [f'size ({fmt(size_x)}, {fmt(size_y)})']
        if abs(off_x) > 0.0001 or abs(off_y) > 0.0001:
            parts.append(f'offset ({fmt(off_x)}, {fmt(off_y)})')

        return 'spawn box { ' + ' '.join(parts) + ' }'

    return None  # can't parse, leave as-is


def fmt(v):
    """Format a float: use int if whole, otherwise trim trailing zeros."""
    if v == int(v):
        return str(int(v))
    return f'{v:g}'


def convert_v2_to_v3(text):
    """Convert V2 block format: angle/direction → direction/spread, old spawn → spawn box."""
    lines = text.split('\n')

    # First pass: find which direction lines are paired with angle lines in the same emitter block
    # so we can skip them when we encounter them
    skip_lines = set()
    for i, line in enumerate(lines):
        if not re.match(r'\s*angle\s+', line):
            continue
        # Look ahead for a direction vector line in the same emitter block
        # Track nesting depth to skip over spawn { } blocks
        depth = 0
        for j in range(i + 1, len(lines)):
            s = lines[j].strip()
            if '{' in s:
                depth += 1
            if '}' in s:
                if depth > 0:
                    depth -= 1
                    continue
                else:
                    break  # end of emitter block
            if depth > 0:
                continue  # inside a nested block (e.g. spawn)
            dm = re.match(r'\s*direction\s+([\[\(].+)', lines[j])
            if dm:
                skip_lines.add(j)
                break

    out = []
    for i, line in enumerate(lines):
        stripped = line.strip()
        indent = line[:len(line) - len(line.lstrip())]

        # Handle old spawn [(min), (max)] or spawn (x, y)
        m = re.match(r'(\s*)spawn\s+([\[\(].+)', line)
        if m:
            result = convert_spawn_to_box(m.group(2))
            if result is not None:
                out.append(f'{m.group(1)}{result}')
            continue

        # Handle old 'angle' line
        m = re.match(r'(\s*)angle\s+(.+)', line)
        if m:
            indent = m.group(1)
            angle_val = m.group(2).strip()

            # Check if there's a paired direction vector line (skip nested blocks)
            vec_dir_result = None
            depth = 0
            for j in range(i + 1, len(lines)):
                s = lines[j].strip()
                if '{' in s:
                    depth += 1
                if '}' in s:
                    if depth > 0:
                        depth -= 1
                        continue
                    else:
                        break
                if depth > 0:
                    continue
                dm = re.match(r'\s*direction\s+([\[\(].+)', lines[j])
                if dm:
                    vec_dir_result = convert_vec2_direction_to_angle(dm.group(1))
                    break

            if vec_dir_result is not None and vec_dir_result[0] is not None:
                # Direction vector overrides angle
                dir_angle, dir_spread = vec_dir_result
                out.append(f'{indent}direction {fmt(dir_angle)}')
                if dir_spread > 0.01:
                    out.append(f'{indent}spread {fmt(dir_spread)}')
            else:
                # Angle only
                direction, spread = convert_angle_to_direction_spread(angle_val)
                if direction is not None:
                    out.append(f'{indent}direction {fmt(direction)}')
                if spread > 0.01:
                    out.append(f'{indent}spread {fmt(spread)}')
            continue

        # Handle old 'direction' vector line (standalone, not paired with angle)
        if i in skip_lines:
            continue  # already handled by the angle line

        m = re.match(r'(\s*)direction\s+([\[\(].+)', line)
        if m:
            indent = m.group(1)
            vec_dir_result = convert_vec2_direction_to_angle(m.group(2))
            if vec_dir_result is not None:
                dir_angle, dir_spread = vec_dir_result
                if dir_angle is not None:
                    out.append(f'{indent}direction {fmt(dir_angle)}')
                    if dir_spread > 0.01:
                        out.append(f'{indent}spread {fmt(dir_spread)}')
            continue

        out.append(line)

    return '\n'.join(out)


def migrate_file(path, dry_run=False):
    """Migrate a single .vfx file. Returns: 'migrated', 'skipped', or 'error'."""
    try:
        with open(path, 'r', encoding='utf-8') as f:
            text = f.read()
    except Exception as e:
        print(f"  ERROR reading {path}: {e}")
        return 'error'

    migrated = False

    if is_v1(text):
        try:
            text = convert_v1_to_v2(text)
            migrated = True
        except Exception as e:
            print(f"  ERROR converting V1→V2 {path}: {e}")
            return 'error'

    if needs_v3(text):
        try:
            text = convert_v2_to_v3(text)
            migrated = True
        except Exception as e:
            print(f"  ERROR converting V2→V3 {path}: {e}")
            return 'error'

    if not migrated:
        return 'skipped'

    if dry_run:
        print(f"  WOULD migrate: {path}")
        return 'migrated'

    try:
        with open(path, 'w', encoding='utf-8', newline='\n') as f:
            f.write(text)
        print(f"  Migrated: {path}")
        return 'migrated'
    except Exception as e:
        print(f"  ERROR writing {path}: {e}")
        return 'error'


def main():
    if len(sys.argv) < 2:
        print("Usage: python migrate_vfx_v2.py <directory> [--dry-run]")
        sys.exit(1)

    directory = sys.argv[1]
    dry_run = '--dry-run' in sys.argv

    if not os.path.isdir(directory):
        print(f"Error: '{directory}' is not a directory")
        sys.exit(1)

    if dry_run:
        print(f"DRY RUN: scanning {directory} for .vfx files...")
    else:
        print(f"Migrating .vfx files in {directory}...")

    counts = {'migrated': 0, 'skipped': 0, 'error': 0}

    for root, dirs, files in os.walk(directory):
        for fname in files:
            if not fname.endswith('.vfx'):
                continue
            path = os.path.join(root, fname)
            result = migrate_file(path, dry_run)
            counts[result] += 1

    print(f"\nDone: {counts['migrated']} migrated, {counts['skipped']} already V2, {counts['error']} errors")


if __name__ == '__main__':
    main()
