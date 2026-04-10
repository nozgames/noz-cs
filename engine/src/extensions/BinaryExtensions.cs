//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

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

    public static BitMask256 ReadBitMask256(this BinaryReader r) =>
        new(r.ReadUInt64(),r.ReadUInt64(),r.ReadUInt64(),r.ReadUInt64());

    public unsafe static void Write(this BinaryWriter w, BitMask256 v)
    {
        w.Write(v.Bits[0]);
        w.Write(v.Bits[1]);
        w.Write(v.Bits[2]);
        w.Write(v.Bits[3]);
    }
}
