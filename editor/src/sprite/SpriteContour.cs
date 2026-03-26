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
    private bool _samplesDirty = true;

    public void MarkDirty() => _samplesDirty = true;

    public ReadOnlySpan<Vector2> GetSegmentSamples(int anchorIndex)
    {
        if (_samplesDirty)
            UpdateSamples();

        var offset = anchorIndex * SpritePath.MaxSegmentSamples;
        return new ReadOnlySpan<Vector2>(_samples, offset, SpritePath.MaxSegmentSamples);
    }

    public void UpdateSamples()
    {
        var count = Anchors.Count;
        if (count == 0)
        {
            _samples = null;
            _samplesDirty = false;
            return;
        }

        var segmentCount = Open ? count - 1 : count;
        var totalSamples = count * SpritePath.MaxSegmentSamples;
        if (_samples == null || _samples.Length < totalSamples)
            _samples = new Vector2[totalSamples];

        for (var i = 0; i < segmentCount; i++)
        {
            var a0 = Anchors[i];
            var a1 = Anchors[(i + 1) % count];
            var offset = i * SpritePath.MaxSegmentSamples;

            if (MathF.Abs(a0.Curve) < SpritePath.MinCurve)
            {
                for (var s = 0; s < SpritePath.MaxSegmentSamples; s++)
                {
                    var t = (s + 1) / (float)(SpritePath.MaxSegmentSamples + 1);
                    _samples[offset + s] = Vector2.Lerp(a0.Position, a1.Position, t);
                }
            }
            else
            {
                var mid = (a0.Position + a1.Position) * 0.5f;
                var dir = a1.Position - a0.Position;
                var perp = new Vector2(-dir.Y, dir.X);
                var len = perp.Length();
                if (len > 0) perp /= len;
                var control = mid + perp * a0.Curve;

                for (var s = 0; s < SpritePath.MaxSegmentSamples; s++)
                {
                    var t = (s + 1) / (float)(SpritePath.MaxSegmentSamples + 1);
                    var u = 1.0f - t;
                    _samples[offset + s] =
                        u * u * a0.Position +
                        2 * u * t * control +
                        t * t * a1.Position;
                }
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
