//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using NoZ;

namespace NoZ.Editor.Graphics3D;

public partial class PaletteTextureDocument
{
    // Each painted cell retains its gradient construction data. Keeping coverage
    // per cell allows repainting/clearing one swatch without flattening the rest
    // of a gradient, and makes overlapping fills follow the same last-write rule
    // as solid colors. Records and their segment arrays are never mutated, so
    // they can be shared safely by undo/redo snapshots.
    private Dictionary<int, GradientCell> _gradientCells = [];

    private readonly record struct GradientCell(GradientSegment[] Segments)
    {
        public Color32 Sample(int pixelX, int pixelY)
        {
            var nearestDistance = float.PositiveInfinity;
            var color = Color32.Transparent;
            foreach (var segment in Segments)
            {
                var sample = segment.Sample(pixelX, pixelY, out var distance);
                if (distance > nearestDistance)
                    continue;

                // At a shared stop, keep both incoming and outgoing ramps.
                // Choosing the nearest segment avoids flattening half the cell
                // when a later segment starts at the same stop.
                nearestDistance = distance;
                color = sample;
            }
            return color;
        }
    }

    private readonly record struct GradientSegment(
        int StartX, int StartY, int EndX, int EndY, Color32 StartColor, Color32 EndColor)
    {
        public Color32 Sample(int pixelX, int pixelY, out float distanceSquared)
        {
            var dx = (EndX - StartX) * (float)CellPixelSize;
            var dy = (EndY - StartY) * (float)CellPixelSize;
            var lengthSquared = dx * dx + dy * dy;
            // The authored stop is at the center texel of its 8-pixel cell.
            // At the endpoints clamp the ramp so padding retains the stop color.
            var x = pixelX - (StartX * CellPixelSize + CellPixelSize / 2);
            var y = pixelY - (StartY * CellPixelSize + CellPixelSize / 2);
            var t = lengthSquared == 0f ? 0f : Math.Clamp((x * dx + y * dy) / lengthSquared, 0f, 1f);
            var residualX = x - t * dx;
            var residualY = y - t * dy;
            distanceSquared = residualX * residualX + residualY * residualY;
            return Color32.Mix(StartColor, EndColor, t);
        }
    }

    private void LoadGradientMetadata(PropertySet meta)
    {
        for (var y = 0; y < GridSize; y++)
        for (var x = 0; x < GridSize; x++)
        {
            var value = meta.GetString("palette_texture", $"gradient_{x}_{y}", "");
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var entries = value.Split('|');
            var segments = new List<GradientSegment>();
            foreach (var entry in entries)
            {
                if (!TryParseSegment(entry, out var segment))
                    break;
                segments.Add(segment);
            }
            if (segments.Count != entries.Length)
            {
                ReportWarning($"Invalid gradient data for palette cell ({x}, {y}); using its solid color");
                continue;
            }

            var gradient = new GradientCell([.. segments]);
            _gradientCells[y * GridSize + x] = gradient;
            _pixels[y * GridSize + x] = gradient.Sample(x * CellPixelSize + CellPixelSize / 2, y * CellPixelSize + CellPixelSize / 2);
        }
    }

    private void SaveGradientMetadata(PropertySet meta)
    {
        foreach (var (index, gradient) in _gradientCells.OrderBy(pair => pair.Key))
        {
            var value = string.Join(" | ", gradient.Segments.Select(segment =>
            {
                var start = segment.StartColor;
                var end = segment.EndColor;
                return FormattableString.Invariant(
                    $"{segment.StartX} {segment.StartY} {segment.EndX} {segment.EndY} {start.R:X2}{start.G:X2}{start.B:X2}{start.A:X2} {end.R:X2}{end.G:X2}{end.B:X2}{end.A:X2}");
            }));
            meta.SetString("palette_texture", $"gradient_{index % GridSize}_{index / GridSize}", value);
        }
    }

    private static bool TryParseSegment(string value, out GradientSegment segment)
    {
        segment = default;
        var fields = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length != 6 ||
            !TryParseCoordinate(fields[0], out var startX) ||
            !TryParseCoordinate(fields[1], out var startY) ||
            !TryParseCoordinate(fields[2], out var endX) ||
            !TryParseCoordinate(fields[3], out var endY) ||
            !TryParseMetadataColor(fields[4], out var startColor) ||
            !TryParseMetadataColor(fields[5], out var endColor))
            return false;

        segment = new GradientSegment(startX, startY, endX, endY, startColor, endColor);
        return true;
    }

    private static bool TryParseCoordinate(string value, out int coordinate) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out coordinate) &&
        coordinate >= 0 && coordinate < MaxSize / CellPixelSize;
}
