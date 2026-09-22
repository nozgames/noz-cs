using System.Numerics;
using System.Text;

namespace NoZ;

public enum VfxSpawnShape3D : byte { Point, Sphere, Box }
public enum VfxBlend3D : byte { Alpha, Additive }

public struct VfxSpawnDef3D
{
    public VfxSpawnShape3D Shape;
    public Vector3 Offset;
    public float Radius;
    public float InnerRadius;
    public Vector3 Size;
}

public struct VfxParticleDef3D
{
    public VfxRange Duration;
    public VfxFloatCurve Size, Speed, Gravity, Opacity, RotationSpeed;
    public VfxColorCurve Color;
    /// <summary>Billboard roll in degrees.</summary>
    public VfxRange Rotation;
    public VfxBlend3D Blend;
    /// <summary>Standalone texture asset; empty uses a white quad.</summary>
    public string? Texture;
    public int Columns, Rows;
    public VfxFrameMode FrameMode;

    public static VfxParticleDef3D Default => new()
    {
        Duration = new(.5f, 1f), Size = VfxMath.Linear(new(.15f), new(0f)),
        Speed = VfxMath.Constant(1f), Gravity = VfxMath.Constant(2f),
        Opacity = VfxMath.Linear(new(1f), new(0f)), RotationSpeed = VfxMath.Constant(0f),
        Color = VfxColorCurve.White, Columns = 1, Rows = 1
    };
}

public struct VfxEmitterDef3D
{
    public VfxRange Duration;
    public VfxFloatCurve Rate;
    public VfxIntRange Burst;
    public VfxSpawnDef3D Spawn;
    /// <summary>Local emission axis; spread is the cone half-angle in degrees (0–180).</summary>
    public Vector3 Direction;
    public float Spread;
    public bool WorldSpace;
    public VfxParticleDef3D Particle;

    public static VfxEmitterDef3D Default => new()
    {
        Duration = new(1f), Rate = VfxMath.Constant(10f), Burst = new(8),
        Direction = Vector3.UnitY, Spread = 35f, WorldSpace = true,
        Particle = VfxParticleDef3D.Default
    };
}

/// <summary>Optional 3D particle asset. Definitions are copied when played.</summary>
public sealed class Vfx3D : Asset
{
    public static readonly AssetType Type = AssetType.FromString("VFX3");
    public const ushort Version = 1;
    public const int MaxEmitters = 32;
    public bool Loop { get; set; }
    public VfxEmitterDef3D[] Emitters { get; set; } = [];

    public Vfx3D() : base(Type) { }
    private Vfx3D(string name) : base(Type, name) { }

    public static void RegisterDef()
    {
        if (Asset.GetDef(Type) is { } existing)
        {
            if (existing.RuntimeType != typeof(Vfx3D))
                throw new InvalidOperationException($"Asset type {Type} is already registered.");
            return;
        }
        RegisterDef(new AssetDef(Type, "Vfx3D", typeof(Vfx3D), LoadAsset, Version));
    }

    private static Asset LoadAsset(Stream stream, string name)
    {
        var effect = new Vfx3D(name);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        effect.Load(reader);
        return effect;
    }

    public override void Reload() => Reload(dispose: false);

    protected override void Load(BinaryReader reader)
    {
        var loop = reader.ReadBoolean();
        var count = reader.ReadInt32();
        if (count is < 0 or > MaxEmitters) throw new InvalidDataException("Invalid VFX emitter count.");
        var emitters = new VfxEmitterDef3D[count];
        for (var i = 0; i < count; i++)
        {
            ref var e = ref emitters[i];
            e.Duration = ReadRange(reader); e.Rate = ReadCurve(reader);
            e.Burst = new(reader.ReadInt32(), reader.ReadInt32());
            e.Spawn.Shape = (VfxSpawnShape3D)reader.ReadByte(); e.Spawn.Offset = ReadVector(reader);
            e.Spawn.Radius = reader.ReadSingle(); e.Spawn.InnerRadius = reader.ReadSingle(); e.Spawn.Size = ReadVector(reader);
            e.Direction = ReadVector(reader); e.Spread = reader.ReadSingle(); e.WorldSpace = reader.ReadBoolean();
            ref var p = ref e.Particle;
            p.Duration = ReadRange(reader); p.Size = ReadCurve(reader); p.Speed = ReadCurve(reader);
            p.Gravity = ReadCurve(reader); p.Opacity = ReadCurve(reader); p.RotationSpeed = ReadCurve(reader);
            p.Color.Start = new(ReadColor(reader), ReadColor(reader)); p.Color.End = new(ReadColor(reader), ReadColor(reader));
            for (var j = 0; j < VfxCurveLut.Samples; j++) p.Color.Lut[j] = reader.ReadSingle();
            p.Rotation = ReadRange(reader); p.Blend = (VfxBlend3D)reader.ReadByte(); p.Texture = reader.ReadString();
            p.Columns = reader.ReadInt32(); p.Rows = reader.ReadInt32(); p.FrameMode = (VfxFrameMode)reader.ReadByte();
        }
        Validate(emitters);
        Loop = loop; Emitters = emitters;
    }

    public static void Write(Stream stream, bool loop, VfxEmitterDef3D[] emitters)
    {
        Validate(emitters);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.WriteAssetHeader(Type, Version);
        writer.Write(loop); writer.Write(emitters.Length);
        foreach (var e in emitters)
        {
            WriteRange(writer, e.Duration); WriteCurve(writer, e.Rate);
            writer.Write(e.Burst.Min); writer.Write(e.Burst.Max);
            writer.Write((byte)e.Spawn.Shape); WriteVector(writer, e.Spawn.Offset);
            writer.Write(e.Spawn.Radius); writer.Write(e.Spawn.InnerRadius); WriteVector(writer, e.Spawn.Size);
            WriteVector(writer, e.Direction); writer.Write(e.Spread); writer.Write(e.WorldSpace);
            var p = e.Particle;
            WriteRange(writer, p.Duration); WriteCurve(writer, p.Size); WriteCurve(writer, p.Speed);
            WriteCurve(writer, p.Gravity); WriteCurve(writer, p.Opacity); WriteCurve(writer, p.RotationSpeed);
            WriteColor(writer, p.Color.Start.Min); WriteColor(writer, p.Color.Start.Max);
            WriteColor(writer, p.Color.End.Min); WriteColor(writer, p.Color.End.Max);
            for (var j = 0; j < VfxCurveLut.Samples; j++) writer.Write(p.Color.Lut[j]);
            WriteRange(writer, p.Rotation); writer.Write((byte)p.Blend); writer.Write(p.Texture ?? "");
            writer.Write(p.Columns); writer.Write(p.Rows); writer.Write((byte)p.FrameMode);
        }
    }

    public static void Validate(VfxEmitterDef3D[] emitters)
    {
        if (emitters == null || emitters.Length > MaxEmitters) throw new InvalidDataException("Too many VFX emitters.");
        foreach (var e in emitters)
        {
            CheckRange(e.Duration, .001f); CheckCurve(e.Rate, 0);
            if (e.Burst.Min < 0 || e.Burst.Max < e.Burst.Min || e.Burst.Max > 1_000_000)
                throw new InvalidDataException("Invalid VFX burst.");
            CheckVector(e.Spawn.Offset); CheckVector(e.Spawn.Size); CheckVector(e.Direction);
            if (!Enum.IsDefined(e.Spawn.Shape) || !float.IsFinite(e.Spawn.Radius) || !float.IsFinite(e.Spawn.InnerRadius) ||
                e.Spawn.InnerRadius < 0 || e.Spawn.Radius < e.Spawn.InnerRadius ||
                Vector3.Min(e.Spawn.Size, Vector3.Zero) != Vector3.Zero ||
                !float.IsFinite(e.Spread) || e.Spread is < 0 or > 180)
                throw new InvalidDataException("Invalid VFX spawn shape or direction.");
            var p = e.Particle;
            CheckRange(p.Duration, .001f); CheckRange(p.Rotation); CheckCurve(p.Size, 0);
            CheckCurve(p.Speed); CheckCurve(p.Gravity); CheckCurve(p.Opacity, 0); CheckCurve(p.RotationSpeed);
            CheckColor(p.Color.Start.Min); CheckColor(p.Color.Start.Max); CheckColor(p.Color.End.Min); CheckColor(p.Color.End.Max);
            for (var i = 0; i < VfxCurveLut.Samples; i++) CheckFinite(p.Color.Lut[i]);
            if (!Enum.IsDefined(p.Blend) || !Enum.IsDefined(p.FrameMode) || p.Columns is < 1 or > 256 || p.Rows is < 1 or > 256 ||
                (p.Texture?.Length ?? 0) > 1024)
                throw new InvalidDataException("Invalid VFX billboard settings.");
        }
    }

    private static void CheckFinite(float value) { if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite VFX value."); }
    private static void CheckVector(Vector3 value) { CheckFinite(value.X); CheckFinite(value.Y); CheckFinite(value.Z); }
    private static void CheckColor(Color value) { CheckFinite(value.R); CheckFinite(value.G); CheckFinite(value.B); CheckFinite(value.A); }
    private static void CheckRange(VfxRange value, float min = float.MinValue)
    {
        CheckFinite(value.Min); CheckFinite(value.Max);
        if (value.Min < min || value.Max < value.Min) throw new InvalidDataException("Invalid VFX range.");
    }
    private static void CheckCurve(VfxFloatCurve value, float min = float.MinValue)
    {
        CheckRange(value.Start, min); CheckRange(value.End, min);
        for (var i = 0; i < VfxCurveLut.Samples; i++) CheckFinite(value.Lut[i]);
    }
    private static VfxRange ReadRange(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle());
    private static Vector3 ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    private static Color ReadColor(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    private static VfxFloatCurve ReadCurve(BinaryReader r)
    {
        var c = new VfxFloatCurve { Start = ReadRange(r), End = ReadRange(r) };
        for (var i = 0; i < VfxCurveLut.Samples; i++) c.Lut[i] = r.ReadSingle();
        return c;
    }
    private static void WriteRange(BinaryWriter w, VfxRange v) { w.Write(v.Min); w.Write(v.Max); }
    private static void WriteVector(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    private static void WriteColor(BinaryWriter w, Color v) { w.Write(v.R); w.Write(v.G); w.Write(v.B); w.Write(v.A); }
    private static void WriteCurve(BinaryWriter w, VfxFloatCurve v)
    {
        WriteRange(w, v.Start); WriteRange(w, v.End);
        for (var i = 0; i < VfxCurveLut.Samples; i++) w.Write(v.Lut[i]);
    }
}
