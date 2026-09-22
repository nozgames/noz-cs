namespace NoZ;

/// <summary>Dimension-independent lifetime curve evaluation for VFX runtimes.</summary>
public static class VfxMath
{
    public static float Evaluate(in VfxCurveLut lut, float t)
    {
        var index = Math.Clamp(t, 0f, 1f) * (VfxCurveLut.Samples - 1);
        var first = (int)index;
        var second = Math.Min(first + 1, VfxCurveLut.Samples - 1);
        var fraction = index - first;
        return lut[first] * (1f - fraction) + lut[second] * fraction;
    }

    public static VfxFloatCurve Constant(float value) => new()
    {
        Start = new(value), End = new(value), Lut = VfxCurveLut.Linear
    };

    public static VfxFloatCurve Linear(VfxRange start, VfxRange end) => new()
    {
        Start = start, End = end, Lut = VfxCurveLut.Linear
    };
}
