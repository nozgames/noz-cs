//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace NoZ;

public enum VfxCurveType : byte
{
    Linear = 0,
    EaseIn,
    EaseOut,
    EaseInOut,
    Quadratic,
    Cubic,
    Sine,
    CubicBezier,
    Bell
}

public struct VfxRange
{
    public float Min;
    public float Max;

    public VfxRange(float min, float max) { Min = min; Max = max; }
    public VfxRange(float value) { Min = value; Max = value; }

    public static readonly VfxRange Zero = new(0, 0);
    public static readonly VfxRange One = new(1, 1);

    public static bool operator ==(VfxRange a, VfxRange b) => a.Min == b.Min && a.Max == b.Max;
    public static bool operator !=(VfxRange a, VfxRange b) => !(a == b);
    public override bool Equals(object? obj) => obj is VfxRange r && this == r;
    public override int GetHashCode() => HashCode.Combine(Min, Max);
}

public struct VfxIntRange
{
    public int Min;
    public int Max;

    public VfxIntRange(int min, int max) { Min = min; Max = max; }
    public VfxIntRange(int value) { Min = value; Max = value; }

    public static readonly VfxIntRange Zero = new(0, 0);

    public static bool operator ==(VfxIntRange a, VfxIntRange b) => a.Min == b.Min && a.Max == b.Max;
    public static bool operator !=(VfxIntRange a, VfxIntRange b) => !(a == b);
    public override bool Equals(object? obj) => obj is VfxIntRange r && this == r;
    public override int GetHashCode() => HashCode.Combine(Min, Max);
}

public struct VfxColorRange
{
    public Color Min;
    public Color Max;

    public VfxColorRange(Color min, Color max) { Min = min; Max = max; }
    public VfxColorRange(Color value) { Min = value; Max = value; }

    public static readonly VfxColorRange White = new(Color.White, Color.White);

    public static bool operator ==(VfxColorRange a, VfxColorRange b) => a.Min == b.Min && a.Max == b.Max;
    public static bool operator !=(VfxColorRange a, VfxColorRange b) => !(a == b);
    public override bool Equals(object? obj) => obj is VfxColorRange r && this == r;
    public override int GetHashCode() => HashCode.Combine(Min, Max);
}

public struct VfxVec2Range
{
    public Vector2 Min;
    public Vector2 Max;

    public VfxVec2Range(Vector2 min, Vector2 max) { Min = min; Max = max; }
    public VfxVec2Range(Vector2 value) { Min = value; Max = value; }

    public static readonly VfxVec2Range Zero = new(Vector2.Zero, Vector2.Zero);

    public static bool operator ==(VfxVec2Range a, VfxVec2Range b) => a.Min == b.Min && a.Max == b.Max;
    public static bool operator !=(VfxVec2Range a, VfxVec2Range b) => !(a == b);
    public override bool Equals(object? obj) => obj is VfxVec2Range r && this == r;
    public override int GetHashCode() => HashCode.Combine(Min, Max);
}

public struct VfxFloatCurve
{
    public VfxCurveType Type;
    public VfxRange Start;
    public VfxRange End;
    public Vector4 Bezier; // (x1, y1, x2, y2) control points, only used when Type == CubicBezier

    public static readonly VfxFloatCurve Zero = new() { Type = VfxCurveType.Linear, Start = VfxRange.Zero, End = VfxRange.Zero };
    public static readonly VfxFloatCurve One = new() { Type = VfxCurveType.Linear, Start = VfxRange.One, End = VfxRange.One };

    public static bool operator ==(VfxFloatCurve a, VfxFloatCurve b) =>
        a.Type == b.Type && a.Start == b.Start && a.End == b.End && a.Bezier == b.Bezier;
    public static bool operator !=(VfxFloatCurve a, VfxFloatCurve b) => !(a == b);
    public override bool Equals(object? obj) => obj is VfxFloatCurve c && this == c;
    public override int GetHashCode() => HashCode.Combine(Type, Start, End, Bezier);
}

public struct VfxColorCurve
{
    public VfxCurveType Type;
    public VfxColorRange Start;
    public VfxColorRange End;
    public Vector4 Bezier;

    public static readonly VfxColorCurve White = new() { Type = VfxCurveType.Linear, Start = VfxColorRange.White, End = VfxColorRange.White };

    public static bool operator ==(VfxColorCurve a, VfxColorCurve b) =>
        a.Type == b.Type && a.Start == b.Start && a.End == b.End && a.Bezier == b.Bezier;
    public static bool operator !=(VfxColorCurve a, VfxColorCurve b) => !(a == b);
    public override bool Equals(object? obj) => obj is VfxColorCurve c && this == c;
    public override int GetHashCode() => HashCode.Combine(Type, Start, End, Bezier);
}

public enum VfxSpawnShape : byte
{
    Point = 0,
    Circle = 1,
    Box = 2,
}

public struct VfxSpawnCircle
{
    public float Radius;
    public float InnerRadius;
}

public struct VfxSpawnBox
{
    public Vector2 Size;
    public Vector2 InnerSize;
    public float Rotation;
}

[StructLayout(LayoutKind.Explicit)]
public struct VfxSpawnDef
{
    [FieldOffset(0)]  public VfxSpawnShape Shape;
    [FieldOffset(4)]  public Vector2 Offset;
    [FieldOffset(12)] public VfxSpawnCircle Circle;
    [FieldOffset(12)] public VfxSpawnBox Box;

    public static readonly VfxSpawnDef Default = new() { Shape = VfxSpawnShape.Point };
}

public struct VfxParticleDef
{
    public VfxVec2Range Gravity;
    public VfxRange Duration;
    public VfxRange Drag;
    public VfxFloatCurve Size;
    public VfxFloatCurve Speed;
    public VfxColorCurve Color;
    public VfxFloatCurve Opacity;
    public VfxRange Rotation;
    public VfxFloatCurve RotationSpeed;
    public Sprite? Sprite;
}

public struct VfxEmitterDef
{
    public VfxIntRange Rate;
    public VfxIntRange Burst;
    public VfxRange Duration;
    public VfxSpawnDef Spawn;
    public VfxRange Direction;
    public VfxRange Spread;
    public float Radial;
    public bool WorldSpace;
    public VfxParticleDef Particle;
}

public class Vfx : Asset
{
    internal const ushort Version = 7;

    public VfxRange Duration { get; internal set; }
    public VfxEmitterDef[] EmitterDefs { get; internal set; } = [];
    public Rect Bounds { get; internal set; }
    public bool Loop { get; internal set; }

    internal Vfx(string name) : base(AssetType.Vfx, name) { }
    public Vfx() : base(AssetType.Vfx) { }

    protected override void Load(BinaryReader reader)
    {
        Bounds = new Rect(
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle());

        Duration = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
        Loop = reader.ReadBoolean();

        var emitterCount = reader.ReadInt32();
        EmitterDefs = new VfxEmitterDef[emitterCount];

        for (var i = 0; i < emitterCount; i++)
        {
            ref var e = ref EmitterDefs[i];

            e.Rate = new VfxIntRange(reader.ReadInt32(), reader.ReadInt32());
            e.Burst = new VfxIntRange(reader.ReadInt32(), reader.ReadInt32());
            e.Duration = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
            e.Spawn = ReadSpawnDef(reader);
            e.Direction = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
            e.Spread = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
            e.Radial = reader.ReadSingle();
            e.WorldSpace = reader.ReadBoolean();

            ref var p = ref e.Particle;
            p.Duration = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
            p.Size = ReadFloatCurve(reader);
            p.Speed = ReadFloatCurve(reader);
            p.Color = ReadColorCurve(reader);
            p.Opacity = ReadFloatCurve(reader);
            p.Gravity = new VfxVec2Range(
                new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                new Vector2(reader.ReadSingle(), reader.ReadSingle()));
            p.Drag = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
            p.Rotation = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
            p.RotationSpeed = ReadFloatCurve(reader);

            var spriteNameLen = reader.ReadInt32();
            if (spriteNameLen > 0)
            {
                var spriteName = Encoding.UTF8.GetString(reader.ReadBytes(spriteNameLen));
                p.Sprite = Asset.Load(AssetType.Sprite, spriteName) as Sprite;
            }
            p.Sprite ??= Asset.Load(AssetType.Sprite, "square") as Sprite;
        }
    }

    private static Asset Load(Stream stream, string name)
    {
        var vfx = new Vfx(name);
        using var reader = new BinaryReader(stream);
        vfx.Load(reader);
        return vfx;
    }

    private static VfxSpawnDef ReadSpawnDef(BinaryReader reader)
    {
        var def = new VfxSpawnDef();
        def.Shape = (VfxSpawnShape)reader.ReadByte();
        def.Offset = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        switch (def.Shape)
        {
            case VfxSpawnShape.Circle:
                def.Circle.Radius = reader.ReadSingle();
                def.Circle.InnerRadius = reader.ReadSingle();
                break;
            case VfxSpawnShape.Box:
                def.Box.Size = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                def.Box.InnerSize = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                def.Box.Rotation = reader.ReadSingle();
                break;
        }
        return def;
    }

    private static VfxFloatCurve ReadFloatCurve(BinaryReader reader)
    {
        var curve = new VfxFloatCurve
        {
            Type = (VfxCurveType)reader.ReadByte(),
            Start = new VfxRange(reader.ReadSingle(), reader.ReadSingle()),
            End = new VfxRange(reader.ReadSingle(), reader.ReadSingle())
        };
        if (curve.Type == VfxCurveType.CubicBezier)
            curve.Bezier = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return curve;
    }

    private static VfxColorCurve ReadColorCurve(BinaryReader reader)
    {
        var curve = new VfxColorCurve
        {
            Type = (VfxCurveType)reader.ReadByte(),
            Start = new VfxColorRange(
                new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())),
            End = new VfxColorRange(
                new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()))
        };
        if (curve.Type == VfxCurveType.CubicBezier)
            curve.Bezier = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return curve;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Vfx, "Vfx", typeof(Vfx), Load, Version));
    }
}
