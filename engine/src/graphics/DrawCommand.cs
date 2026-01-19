//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

internal struct DrawCommand : IComparable<DrawCommand>
{
    public long SortKey;
    public int IndexOffset;
    public int IndexCount;
    public ushort BatchState;

    readonly int IComparable<DrawCommand>.CompareTo(DrawCommand x)
    {
        var diff = SortKey - x.SortKey;
        return diff < 0 ? -1 : (diff > 0 ? 1 : 0);
    }
}
