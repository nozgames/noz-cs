using System.Numerics;

namespace NoZ;

public static class BinaryExtensions
{
    public static void Write(this BinaryWriter w, Vector2 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
    }

    public static Vector2 ReadVector2(this BinaryReader r) =>
        new(r.ReadSingle(), r.ReadSingle());

    public static void Write(this BinaryWriter w, Vector2Int v)
    {
        w.Write(v.X);
        w.Write(v.Y);
    }

    public static Vector2Int ReadVector2Int(this BinaryReader r) =>
        new(r.ReadInt32(), r.ReadInt32());
}
