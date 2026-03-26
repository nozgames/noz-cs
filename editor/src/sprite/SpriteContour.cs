//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class SpriteContour
{
    public List<SpritePathAnchor> Anchors { get; } = new();
    public bool Open { get; set; }

    private Vector2[]? _samples;
    private int[] _sampleOffsets = [];
    private int[] _sampleCounts = [];
    private bool _samplesDirty = true;

    public void MarkDirty() => _samplesDirty = true;

    public ReadOnlySpan<Vector2> GetSegmentSamples(int anchorIndex)
    {
        if (_samplesDirty)
            UpdateSamples();

        var count = _sampleCounts![anchorIndex];
        if (count == 0)
            return ReadOnlySpan<Vector2>.Empty;

        return new ReadOnlySpan<Vector2>(_samples, _sampleOffsets[anchorIndex], count);
    }

    public void UpdateSamples()
    {
        var count = Anchors.Count;
        if (count == 0)
        {
            _samples = null;
            _sampleOffsets = [];
            _sampleCounts = [];
            _samplesDirty = false;
            return;
        }

        var segmentCount = Open ? count - 1 : count;

        if (_sampleOffsets.Length < count)
        {
            _sampleOffsets = new int[count];
            _sampleCounts = new int[count];
        }

        // First pass: compute per-segment sample counts and total
        var totalSamples = 0;
        for (var i = 0; i < segmentCount; i++)
        {
            var a0 = Anchors[i];
            var a1 = Anchors[(i + 1) % count];
            var dir = a1.Position - a0.Position;
            var len = dir.Length();
            var sc = SpritePath.ComputeSegmentSamples(a0.Curve, len);
            _sampleOffsets[i] = totalSamples;
            _sampleCounts[i] = sc;
            totalSamples += sc;
        }

        // Zero out non-segment entries
        for (var i = segmentCount; i < count; i++)
        {
            _sampleOffsets[i] = totalSamples;
            _sampleCounts[i] = 0;
        }

        if (totalSamples == 0)
        {
            _samples = null;
            _samplesDirty = false;
            return;
        }

        if (_samples == null || _samples.Length < totalSamples)
            _samples = new Vector2[totalSamples];

        // Second pass: generate samples
        for (var i = 0; i < segmentCount; i++)
        {
            var sc = _sampleCounts[i];
            if (sc == 0) continue;

            var a0 = Anchors[i];
            var a1 = Anchors[(i + 1) % count];
            var offset = _sampleOffsets[i];

            var mid = (a0.Position + a1.Position) * 0.5f;
            var dir = a1.Position - a0.Position;
            var perp = new Vector2(-dir.Y, dir.X);
            var len = perp.Length();
            if (len > 0) perp /= len;
            var control = mid + perp * a0.Curve;

            for (var s = 0; s < sc; s++)
            {
                var t = (s + 1) / (float)(sc + 1);
                var u = 1.0f - t;
                _samples[offset + s] =
                    u * u * a0.Position +
                    2 * u * t * control +
                    t * t * a1.Position;
            }
        }

        _samplesDirty = false;
    }

    public SpriteContour Clone()
    {
        var clone = new SpriteContour { Open = Open };
        clone.Anchors.AddRange(Anchors);
        return clone;
    }
}
