namespace NoZ.Editor.Graphics3D;

public sealed partial class Vfx3DSource
{
    public static Vfx3DSource Parse(string text)
    {
        var r = new AssetTextReader(text); var value = new Vfx3DSource { Emitters = [] };
        while (r.Field(out var key, "emitter"))
            switch (key)
            {
                case "version": r.Version(); break;
                case "loop": value.Loop = r.Bool(); break;
                case "preview_radius": value.PreviewRadius = r.Float(); break;
                case "emitter": value.Emitters.Add(ReadEmitter(ref r)); break;
                default: throw r.Unknown(key);
            }
        value.Bake(); return value;
    }
    public string ToText()
    {
        Bake(); var w = new AssetTextWriter(); w.Field("version", Version);
        w.Field("loop", Loop); w.Field("preview_radius", PreviewRadius);
        foreach (var emitter in Emitters) WriteEmitter(w, emitter);
        return w.ToString();
    }
    private static Vfx3DEmitterSource ReadEmitter(ref AssetTextReader r)
    {
        var value = new Vfx3DEmitterSource(); r.Begin();
        while (r.Field(out var key))
            switch (key)
            {
                case "name": value.Name = r.String(); break;
                case "duration": value.Duration = new(r.Float(), r.Float()); break;
                case "rate": value.Rate = ReadCurve(ref r); break;
                case "burst": value.Burst = new(r.Int(), r.Int()); break;
                case "direction": value.Direction = r.Vector3(); break;
                case "spread": value.Spread = r.Float(); break;
                case "world_space": value.WorldSpace = r.Bool(); break;
                case "lifetime": value.Lifetime = new(r.Float(), r.Float()); break;
                case "size": value.Size = ReadCurve(ref r); break;
                case "speed": value.Speed = ReadCurve(ref r); break;
                case "gravity": value.Gravity = ReadCurve(ref r); break;
                case "opacity": value.Opacity = ReadCurve(ref r); break;
                case "rotation_speed": value.RotationSpeed = ReadCurve(ref r); break;
                case "color": value.Color = ReadColorCurve(ref r); break;
                case "rotation": value.Rotation = new(r.Float(), r.Float()); break;
                case "blend": value.Blend = r.Enum<VfxBlend3D>(); break;
                case "texture": value.Texture = r.String(); break;
                case "columns": value.Columns = r.Int(); break;
                case "rows": value.Rows = r.Int(); break;
                case "frame_mode": value.FrameMode = r.Enum<VfxFrameMode>(); break;
                case "spawn": value.Spawn = ReadSpawn(ref r); break;
                default: throw r.Unknown(key);
            }
        return value;
    }
    private static void WriteEmitter(AssetTextWriter w, Vfx3DEmitterSource value)
    {
        w.Begin("emitter");
        w.Field("name", value.Name);
        w.Field("duration", value.Duration.Min, value.Duration.Max);
        WriteCurve(w, "rate", value.Rate);
        w.Field("burst", value.Burst.Min, value.Burst.Max);
        w.Field("direction", value.Direction);
        w.Field("spread", value.Spread);
        w.Field("world_space", value.WorldSpace);
        w.Field("lifetime", value.Lifetime.Min, value.Lifetime.Max);
        WriteCurve(w, "size", value.Size);
        WriteCurve(w, "speed", value.Speed);
        WriteCurve(w, "gravity", value.Gravity);
        WriteCurve(w, "opacity", value.Opacity);
        WriteCurve(w, "rotation_speed", value.RotationSpeed);
        WriteColorCurve(w, "color", value.Color);
        w.Field("rotation", value.Rotation.Min, value.Rotation.Max);
        w.Field("blend", value.Blend);
        w.Field("texture", value.Texture);
        w.Field("columns", value.Columns);
        w.Field("rows", value.Rows);
        w.Field("frame_mode", value.FrameMode);
        w.Begin("spawn");
        w.Field("shape", value.Spawn.Shape); w.Field("offset", value.Spawn.Offset);
        w.Field("radius", value.Spawn.Radius); w.Field("inner_radius", value.Spawn.InnerRadius);
        w.Field("size", value.Spawn.Size); w.End(); w.End();
    }
    private static VfxSpawnDef3D ReadSpawn(ref AssetTextReader r)
    {
        var value = new VfxSpawnDef3D(); r.Begin();
        while (r.Field(out var key))
            switch (key)
            {
                case "shape": value.Shape = r.Enum<VfxSpawnShape3D>(); break;
                case "offset": value.Offset = r.Vector3(); break;
                case "size": value.Size = r.Vector3(); break;
                case "radius": value.Radius = r.Float(); break;
                case "inner_radius": value.InnerRadius = r.Float(); break;
                default: throw r.Unknown(key);
            }
        return value;
    }
    private static Color ReadColor(ref AssetTextReader r) => new(r.Float(), r.Float(), r.Float(), r.Float());
    private static VfxDocFloatCurve ReadCurve(ref AssetTextReader r)
    {
        var value = new VfxDocFloatCurve { WindowEnd = 1 }; r.Begin();
        while (r.Field(out var key))
            switch (key)
            {
                case "start": value.Start = new(r.Float(), r.Float()); break;
                case "end": value.End = new(r.Float(), r.Float()); break;
                case "curve_type": value.CurveType = r.Enum<VfxCurveType>(); break;
                case "ease_type": value.EaseType = r.Enum<VfxEaseType>(); break;
                case "window": value.WindowBegin = r.Float(); value.WindowEnd = r.Float(); break;
                default: throw r.Unknown(key);
            }
        return value;
    }
    private static void WriteCurve(AssetTextWriter w, string name, VfxDocFloatCurve value)
    {
        w.Begin(name);
        w.Field("start", value.Start.Min, value.Start.Max);
        w.Field("end", value.End.Min, value.End.Max);
        w.Field("curve_type", value.CurveType); w.Field("ease_type", value.EaseType);
        w.Field("window", value.WindowBegin, value.WindowEnd); w.End();
    }
    private static VfxDocColorCurve ReadColorCurve(ref AssetTextReader r)
    {
        var value = new VfxDocColorCurve { WindowEnd = 1 }; r.Begin();
        while (r.Field(out var key))
            switch (key)
            {
                case "start": value.Start = new(ReadColor(ref r), ReadColor(ref r)); break;
                case "end": value.End = new(ReadColor(ref r), ReadColor(ref r)); break;
                case "curve_type": value.CurveType = r.Enum<VfxCurveType>(); break;
                case "ease_type": value.EaseType = r.Enum<VfxEaseType>(); break;
                case "window": value.WindowBegin = r.Float(); value.WindowEnd = r.Float(); break;
                default: throw r.Unknown(key);
            }
        return value;
    }
    private static void WriteColorCurve(AssetTextWriter w, string name, VfxDocColorCurve value)
    {
        w.Begin(name);
        w.Field("start", value.Start.Min.R, value.Start.Min.G, value.Start.Min.B, value.Start.Min.A, value.Start.Max.R, value.Start.Max.G, value.Start.Max.B, value.Start.Max.A);
        w.Field("end", value.End.Min.R, value.End.Min.G, value.End.Min.B, value.End.Min.A, value.End.Max.R, value.End.Max.G, value.End.Max.B, value.End.Max.A);
        w.Field("curve_type", value.CurveType); w.Field("ease_type", value.EaseType);
        w.Field("window", value.WindowBegin, value.WindowEnd); w.End();
    }
}
