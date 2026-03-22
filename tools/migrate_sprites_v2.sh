#!/bin/bash
# Migrate .sprite files from V1 (flat paths) to V2 (layer hierarchy with {} blocks).
# Usage: bash migrate_sprites_v2.sh <directory> [--dry-run]

DIR="$1"
DRY_RUN="$2"
CONVERTED=0
SKIPPED=0

if [ -z "$DIR" ]; then
    echo "Usage: $0 <directory> [--dry-run]"
    exit 1
fi

migrate_file() {
    local file="$1"

    # Skip if already V2 (contains 'layer "' followed by '{')
    if grep -qP 'layer\s+"[^"]*"\s*\{' "$file" 2>/dev/null; then
        SKIPPED=$((SKIPPED + 1))
        return
    fi

    # Skip empty files
    if [ ! -s "$file" ]; then
        SKIPPED=$((SKIPPED + 1))
        return
    fi

    # Check if it has any paths
    if ! grep -q "^path" "$file"; then
        SKIPPED=$((SKIPPED + 1))
        return
    fi

    # Check if multi-frame
    local frame_count
    frame_count=$(grep -c "^frame" "$file")

    if [ "$DRY_RUN" = "--dry-run" ]; then
        echo "  Would convert: $file (frames: $frame_count)"
        CONVERTED=$((CONVERTED + 1))
        return
    fi

    local tmpfile="${file}.tmp"

    # Extract top-level properties (edges, skeleton)
    local top_lines=""
    while IFS= read -r line; do
        case "$line" in
            edges*|skeleton*) top_lines="${top_lines}${line}\n" ;;
        esac
    done < "$file"

    # Build output
    {
        # Write top-level properties
        if [ -n "$top_lines" ]; then
            printf "%b" "$top_lines"
            echo ""
        fi

        if [ "$frame_count" -le 1 ]; then
            # Single frame: wrap all paths in one layer
            echo 'layer "Layer 1" {'
            local in_path=0
            while IFS= read -r line; do
                local trimmed
                trimmed=$(echo "$line" | sed 's/^[[:space:]]*//')
                case "$trimmed" in
                    "path"|path)
                        if [ "$in_path" -eq 1 ]; then
                            echo "  }"
                            echo ""
                        fi
                        echo "  path {"
                        in_path=1
                        ;;
                    fill*|stroke*|subtract*|clip*|anchor*)
                        if [ "$in_path" -eq 1 ]; then
                            echo "    $trimmed"
                        fi
                        ;;
                    frame*|edges*|skeleton*|layer*|bone*|generate*|"")
                        # skip
                        ;;
                esac
            done < "$file"
            if [ "$in_path" -eq 1 ]; then
                echo "  }"
                echo ""
            fi
            echo "}"
            echo ""
        else
            # Multi-frame: one layer per frame
            local frame_idx=0
            local in_path=0
            local in_frame=0
            local holds=()

            while IFS= read -r line; do
                local trimmed
                trimmed=$(echo "$line" | sed 's/^[[:space:]]*//')
                case "$trimmed" in
                    frame|frame\ *)
                        # Close previous path/layer
                        if [ "$in_path" -eq 1 ]; then
                            echo "  }"
                            echo ""
                            in_path=0
                        fi
                        if [ "$in_frame" -eq 1 ]; then
                            echo "}"
                            echo ""
                        fi

                        frame_idx=$((frame_idx + 1))
                        echo "layer \"Frame $frame_idx\" {"
                        in_frame=1

                        # Parse hold
                        local hold=0
                        if echo "$trimmed" | grep -q "hold"; then
                            hold=$(echo "$trimmed" | grep -oP 'hold\s+\K\d+')
                        fi
                        holds+=("$hold")
                        ;;
                    "path"|path)
                        if [ "$in_path" -eq 1 ]; then
                            echo "  }"
                            echo ""
                        fi
                        echo "  path {"
                        in_path=1
                        ;;
                    fill*|stroke*|subtract*|clip*|anchor*)
                        if [ "$in_path" -eq 1 ]; then
                            echo "    $trimmed"
                        fi
                        ;;
                    hold\ *)
                        # Already captured in frame line parsing
                        local hold_val
                        hold_val=$(echo "$trimmed" | grep -oP 'hold\s+\K\d+')
                        if [ -n "$hold_val" ] && [ ${#holds[@]} -gt 0 ]; then
                            holds[$((${#holds[@]} - 1))]="$hold_val"
                        fi
                        ;;
                    layer*|bone*|generate*|"")
                        # skip
                        ;;
                esac
            done < "$file"

            if [ "$in_path" -eq 1 ]; then
                echo "  }"
                echo ""
            fi
            if [ "$in_frame" -eq 1 ]; then
                echo "}"
                echo ""
            fi

            # Write frame visibility blocks
            for ((i=0; i<frame_idx; i++)); do
                local fn=$((i + 1))
                echo "frame {"
                echo "  visible \"Frame $fn\""
                if [ "${holds[$i]}" -gt 0 ] 2>/dev/null; then
                    echo "  hold ${holds[$i]}"
                fi
                echo "}"
                echo ""
            done
        fi
    } > "$tmpfile"

    mv "$tmpfile" "$file"
    echo "  Converted: $file"
    CONVERTED=$((CONVERTED + 1))
}

find "$DIR" -name "*.sprite" -type f | sort | while read -r file; do
    migrate_file "$file"
done

echo ""
echo "Converted: $CONVERTED, Skipped: $SKIPPED"
