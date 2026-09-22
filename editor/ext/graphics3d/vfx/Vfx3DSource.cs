using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoZ.Editor.Graphics3D;

/// <summary>Editable .vfx3d source; curves use the same authoring model as 2D effects.</summary>
public sealed class Vfx3DSource
{
    public int Version = 1;
    public bool Loop;
    public float PreviewRadius = 2f;
    public List<Vfx3DEmitterSource> Emitters = [new()];

    public VfxEmitterDef3D[] Bake()
    {
        if (Version != 1 || Emitters == null || Emitters.Count > Vfx3D.MaxEmitters ||
            !float.IsFinite(PreviewRadius) || PreviewRadius <= 0)
            throw new InvalidDataException("Invalid VFX3D source settings.");
        var result = Emitters.Select(e => e?.Bake() ?? throw new InvalidDataException("Null VFX emitter.")).ToArray();
        Vfx3D.Validate(result);
        return result;
    }
    public string ToJson() => JsonSerializer.Serialize(this, Vfx3DJsonContext.Default.Vfx3DSource);
    public static Vfx3DSource Parse(string json)
    {
        var source = JsonSerializer.Deserialize(json, Vfx3DJsonContext.Default.Vfx3DSource)
            ?? throw new InvalidDataException("Missing VFX3D source.");
        source.Bake();
        return source;
    }
}

public sealed class Vfx3DEmitterSource
{
    public string Name = "Emitter";
    public VfxRange Duration = new(1f);
    public VfxDocFloatCurve Rate = Constant(10);
    public VfxIntRange Burst = new(8);
    public VfxSpawnDef3D Spawn;
    public Vector3 Direction = Vector3.UnitY;
    public float Spread = 35;
    public bool WorldSpace = true;
    public VfxRange Lifetime = new(.5f, 1f);
    public VfxDocFloatCurve Size = Linear(new(.15f), new(0f));
    public VfxDocFloatCurve Speed = Constant(1);
    public VfxDocFloatCurve Gravity = Constant(2);
    public VfxDocFloatCurve Opacity = Linear(new(1f), new(0f));
    public VfxDocFloatCurve RotationSpeed = Constant(0);
    public VfxDocColorCurve Color = VfxDocColorCurve.White;
    public VfxRange Rotation;
    public VfxBlend3D Blend;
    public string Texture = "";
    public int Columns = 1, Rows = 1;
    public VfxFrameMode FrameMode;

    public VfxEmitterDef3D Bake()
    {
        if (Name == null || Texture == null) throw new InvalidDataException("VFX emitter name and texture must be strings.");
        CheckCurve(Rate); CheckCurve(Size); CheckCurve(Speed); CheckCurve(Gravity); CheckCurve(Opacity); CheckCurve(RotationSpeed);
        CheckShape(Color.CurveType, Color.EaseType, Color.WindowBegin, Color.WindowEnd);
        return new()
        {
            Duration = Duration, Rate = Rate.Bake(), Burst = Burst, Spawn = Spawn, Direction = Direction,
            Spread = Spread, WorldSpace = WorldSpace,
            Particle = new()
            {
                Duration = Lifetime, Size = Size.Bake(), Speed = Speed.Bake(), Gravity = Gravity.Bake(), Opacity = Opacity.Bake(),
                RotationSpeed = RotationSpeed.Bake(), Color = Color.Bake(), Rotation = Rotation,
                Blend = Blend, Texture = Texture, Columns = Columns, Rows = Rows, FrameMode = FrameMode
            }
        };
    }
    public static VfxDocFloatCurve Constant(float value) => new() { Start = new(value), End = new(value), WindowEnd = 1 };
    public static VfxDocFloatCurve Linear(VfxRange start, VfxRange end) => new()
        { Start = start, End = end, WindowEnd = 1, CurveType = VfxCurveType.Linear, EaseType = VfxEaseType.In };
    private static void CheckCurve(VfxDocFloatCurve c) => CheckShape(c.CurveType, c.EaseType, c.WindowBegin, c.WindowEnd);
    private static void CheckShape(VfxCurveType curve, VfxEaseType ease, float begin, float end)
    {
        if (!Enum.IsDefined(curve) || !Enum.IsDefined(ease) || !float.IsFinite(begin) || !float.IsFinite(end) ||
            begin < 0 || end > 1 || end < begin) throw new InvalidDataException("Invalid VFX lifetime curve.");
    }
}

[JsonSourceGenerationOptions(IncludeFields = true, IgnoreReadOnlyProperties = true, WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(Vfx3DSource))]
internal partial class Vfx3DJsonContext : JsonSerializerContext { }
