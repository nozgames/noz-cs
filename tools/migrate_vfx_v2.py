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


def migrate_file(path, dry_run=False):
    """Migrate a single .vfx file. Returns: 'migrated', 'skipped', or 'error'."""
    try:
        with open(path, 'r', encoding='utf-8') as f:
            text = f.read()
    except Exception as e:
        print(f"  ERROR reading {path}: {e}")
        return 'error'

    if not is_v1(text):
        return 'skipped'

    try:
        new_text = convert_v1_to_v2(text)
    except Exception as e:
        print(f"  ERROR converting {path}: {e}")
        return 'error'

    if dry_run:
        print(f"  WOULD migrate: {path}")
        return 'migrated'

    try:
        with open(path, 'w', encoding='utf-8', newline='\n') as f:
            f.write(new_text)
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
