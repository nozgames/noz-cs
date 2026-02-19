//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public enum Align
{
    Min,
    Center,
    Max
}

public struct Align2(Align x, Align y)
{
    public Align X = x;
    public Align Y = y;

    public unsafe Align this[int index]
    {
        get
        {
            fixed (Align* ptr = &X)
                return ptr[index];
        }
        set
        {
            fixed (Align* ptr = &X)
                ptr[index] = value;
        }
    }

    public static implicit operator Align2(Align align) => new(align, align);
    public override string ToString() => $"X: {X}, Y: {Y}";
}

public static class AlignExtensions
{
    private static readonly float[] AlignFactor = [0.0f, 0.5f, 1.0f];

    public static float ToFactor(this Align align) => AlignFactor[(int)align];
}
