//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

internal struct DrawCommand : IComparable<DrawCommand>
{
    public long SortKey;
    // Render targets have an explicit submission order, independent of the
    // material/layer key. A four-bit signed key could only order seven passes.
    public byte PassOrder;
    public int IndexOffset;
    public int IndexCount;
    public ushort BatchState;
    public int InstanceCount;
    public int FirstInstance;

    readonly int IComparable<DrawCommand>.CompareTo(DrawCommand x)
    {
        var pass = PassOrder.CompareTo(x.PassOrder);
        return pass != 0 ? pass : SortKey.CompareTo(x.SortKey);
    }
}
