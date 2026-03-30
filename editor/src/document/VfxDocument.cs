//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public enum VfxSelectionType { None, Vfx, Emitter, Particle }

public class VfxDocParticle
{
    public string Name = "";
    public VfxParticleDef Def;
    public DocumentRef<SpriteDocument> SpriteRef;
}

public class VfxDocEmitter
{
    public string Name = "";
    public VfxEmitterDef Def;
    public string ParticleRef = "";
}

public class VfxDocument : Document
{
    public override bool CanSave => true;

    private const int MaxEmitters = 32;
    private const int MaxParticles = 32;

    private Vfx? _vfx;
    private VfxHandle _handle = VfxHandle.Invalid;
    private bool _playing;
    private VfxRange _duration;
    private bool _loop;
    private float _rotation;

    public readonly List<VfxDocEmitter> Emitters = [];
    public readonly List<VfxDocParticle> Particles = [];

    public VfxSelectionType SelectedType { get; set; }
    public int SelectedIndex { get; set; } = -1;

    public ref VfxRange Duration => ref _duration;
    public ref bool Loop => ref _loop;

    public float Rotation
    {
        get => _rotation;
        set
        {
            _rotation = value;
            VfxSystem.SetTransform(_handle, PlayTransform);
        }
    }

    private Matrix3x2 PlayTransform =>
        Matrix3x2.CreateRotation(MathEx.Deg2Rad * _rotation) *
        Matrix3x2.CreateTranslation(Position);

    // --- Emitter management ---

    public void AddEmitter(string name)
    {
        if (Emitters.Count >= MaxEmitters) return;

        var emitter = new VfxDocEmitter
        {
            Name = name,
            Def = new VfxEmitterDef
            {
                Rate = new VfxIntRange(10, 10),
                Duration = VfxRange.One,
            },
            ParticleRef = Particles.Count > 0 ? Particles[0].Name : ""
        };

        Emitters.Add(emitter);
        SelectedType = VfxSelectionType.Emitter;
        SelectedIndex = Emitters.Count - 1;
        ApplyChanges();
    }

    public void RemoveEmitter(int index)
    {
        if (index < 0 || index >= Emitters.Count) return;

        Emitters.RemoveAt(index);

        if (SelectedType == VfxSelectionType.Emitter)
        {
            if (SelectedIndex >= Emitters.Count)
                SelectedIndex = Emitters.Count - 1;
            if (SelectedIndex < 0)
                SelectedType = VfxSelectionType.None;
        }

        ApplyChanges();
    }

    public void RenameEmitter(int index, string newName)
    {
        if (index < 0 || index >= Emitters.Count) return;
        Emitters[index].Name = newName;
        ApplyChanges();
    }

    // --- Particle management ---

    public void AddParticle(string name)
    {
        if (Particles.Count >= MaxParticles) return;

        var particle = new VfxDocParticle
        {
            Name = name,
            Def = new VfxParticleDef
            {
                Duration = new VfxRange(0.5f, 1.0f),
            }
        };

        Particles.Add(particle);
        SelectedType = VfxSelectionType.Particle;
        SelectedIndex = Particles.Count - 1;
        ApplyChanges();
    }

    public void RemoveParticle(int index)
    {
        if (index < 0 || index >= Particles.Count) return;

        var removedName = Particles[index].Name;
        Particles.RemoveAt(index);

        // Update emitter references
        var fallback = Particles.Count > 0 ? Particles[0].Name : "";
        foreach (var e in Emitters)
        {
            if (e.ParticleRef == removedName)
                e.ParticleRef = fallback;
        }

        if (SelectedType == VfxSelectionType.Particle)
        {
            if (SelectedIndex >= Particles.Count)
                SelectedIndex = Particles.Count - 1;
            if (SelectedIndex < 0)
                SelectedType = VfxSelectionType.None;
        }

        ApplyChanges();
    }

    public void RenameParticle(int index, string newName)
    {
        if (index < 0 || index >= Particles.Count) return;

        var oldName = Particles[index].Name;
        Particles[index].Name = newName;

        // Update emitter references
        foreach (var e in Emitters)
        {
            if (e.ParticleRef == oldName)
                e.ParticleRef = newName;
        }

        ApplyChanges();
    }

    public VfxDocParticle? FindParticle(string name)
    {
        foreach (var p in Particles)
        {
            if (p.Name == name)
                return p;
        }
        return null;
    }

    // --- Build & Apply ---

    public void ApplyChanges()
    {
        IncrementVersion();
        BuildVfx();

        if (_playing && _vfx != null)
        {
            VfxSystem.Stop(_handle);
            _handle = VfxSystem.Play(_vfx, PlayTransform);
        }
    }

    private void BuildVfx()
    {
        var emitterDefs = new VfxEmitterDef[Emitters.Count];
        for (var i = 0; i < Emitters.Count; i++)
        {
            emitterDefs[i] = Emitters[i].Def;

            var particle = FindParticle(Emitters[i].ParticleRef);
            if (particle != null)
            {
                emitterDefs[i].Particle = particle.Def;
                emitterDefs[i].Particle.Sprite = particle.SpriteRef.Value?.Sprite;
            }
        }

        _vfx = new Vfx(Name)
        {
            Duration = _duration,
            Loop = _loop,
            EmitterDefs = emitterDefs,
            Bounds = CalculateBounds(emitterDefs)
        };
    }

    private static Rect CalculateBounds(VfxEmitterDef[] emitterDefs)
    {
        var bounds = new Rect(-0.5f, -0.5f, 1f, 1f);

        for (var i = 0; i < emitterDefs.Length; i++)
        {
            ref var e = ref emitterDefs[i];
            ref var p = ref e.Particle;

            var ssmax = MathF.Max(p.Size.Start.Min, p.Size.Start.Max);
            var semax = MathF.Max(p.Size.End.Min, p.Size.End.Max);
            var smax = MathF.Max(ssmax, semax);

            var speedMax = MathF.Max(p.Speed.Start.Max, p.Speed.End.Max);
            var durationMax = p.Duration.Max;
            var extent = speedMax * durationMax + smax;

            var minX = MathF.Min(e.Spawn.Min.X, e.Spawn.Max.X) - extent;
            var minY = MathF.Min(e.Spawn.Min.Y, e.Spawn.Max.Y) - extent;
            var maxX = MathF.Max(e.Spawn.Min.X, e.Spawn.Max.X) + extent;
            var maxY = MathF.Max(e.Spawn.Min.Y, e.Spawn.Max.Y) + extent;

            if (i == 0)
                bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
            else
                bounds = Rect.Union(bounds, new Rect(minX, minY, maxX - minX, maxY - minY));
        }

        return bounds;
    }

    // --- Registration ---

    public static void RegisterDef()
    {
        DocumentDef<VfxDocument>.Register(new DocumentDef
        {
            Type = AssetType.Vfx,
            Name = "Vfx",
            Extensions = [".vfx"],
            Factory = () => new VfxDocument(),
            EditorFactory = doc => new VfxEditor((VfxDocument)doc),
            NewFile = writer =>
            {
                writer.WriteLine("[vfx]");
                writer.WriteLine("duration = 1");
                writer.WriteLine("loop = false");
                writer.WriteLine();
                writer.WriteLine("[emitters]");
                writer.WriteLine("default");
                writer.WriteLine();
                writer.WriteLine("[default]");
                writer.WriteLine("rate = 10");
                writer.WriteLine("burst = 0");
                writer.WriteLine("duration = 1");
                writer.WriteLine("angle = [0, 360]");
                writer.WriteLine();
                writer.WriteLine("[default.particle]");
                writer.WriteLine("duration = [0.5, 1.0]");
                writer.WriteLine("size = 0.5=>[0.0, 0.1]:easeout");
                writer.WriteLine("speed = [20, 40]=>[5, 10]:linear");
                writer.WriteLine("color = white");
                writer.WriteLine("opacity = 1.0=>0.0:easeout");
            },
            Icon = () => EditorAssets.Sprites.AssetIconVfx
        });
    }

    // --- Lifecycle ---

    public override void Load()
    {
        ParseVfxFile();
        BuildVfx();
    }

    public override void PostLoad()
    {
        ResolveSpriteRefs();
        BuildVfx();
    }

    public override void Export(string outputPath, PropertySet meta)
    {
        ParseVfxFile();
        WriteBinary(outputPath);

        var wasPlaying = _playing;
        if (_playing)
        {
            VfxSystem.Stop(_handle);
            _handle = VfxHandle.Invalid;
        }

        BuildVfx();

        if (wasPlaying && _vfx != null)
        {
            _handle = VfxSystem.Play(_vfx, PlayTransform);
            _playing = true;
        }
    }

    public override void Draw()
    {
        if (!_playing || Emitters.Count == 0)
        {
            using (Graphics.PushState())
            {
                Graphics.SetLayer(EditorLayer.Document);
                Graphics.SetColor(Color.White);
                Graphics.Draw(EditorAssets.Sprites.AssetIconVfx);
            }
            return;
        }

        if (!VfxSystem.IsPlaying(_handle) && _vfx != null)
            _handle = VfxSystem.Play(_vfx, PlayTransform);
    }

    public override bool CanPlay => Emitters.Count > 0;
    public override bool IsPlaying => _playing;

    public override void Play()
    {
        if (_vfx == null) return;
        _playing = true;
        _handle = VfxSystem.Play(_vfx, PlayTransform);
    }

    public override void Stop()
    {
        VfxSystem.Kill(_handle);
        _handle = VfxHandle.Invalid;
        _playing = false;
    }

    public override void LoadMetadata(PropertySet meta)
    {
        _rotation = meta.GetFloat("editor", "rotation", 0f);
    }

    public override void SaveMetadata(PropertySet meta)
    {
        if (_rotation != 0f)
            meta.SetFloat("editor", "rotation", _rotation);
    }

    public override void Dispose()
    {
        if (_playing) Stop();
        base.Dispose();
    }

    public override void Clone(Document source)
    {
        var src = (VfxDocument)source;
        _rotation = src._rotation;
    }

    // --- Text file parsing ---

    private void ParseVfxFile()
    {
        var content = File.ReadAllText(Path);
        var props = PropertySet.Load(content);
        if (props == null) return;

        _duration = ParseFloat(props.GetString("vfx", "duration", "1.0"), new VfxRange(1, 1));
        _loop = props.GetBool("vfx", "loop", false);

        // Parse particles first — collect unique particle sections
        var parsedParticles = new Dictionary<string, VfxDocParticle>();
        var emitterNames = props.GetKeys("emitters").ToArray();

        foreach (var emitterName in emitterNames)
        {
            if (string.IsNullOrWhiteSpace(emitterName)) continue;
            if (!props.HasGroup(emitterName)) continue;

            var particleSection = props.GetString(emitterName, "particle", "");
            if (string.IsNullOrEmpty(particleSection))
                particleSection = emitterName + ".particle";

            if (!props.HasGroup(particleSection)) continue;

            if (!parsedParticles.ContainsKey(particleSection))
            {
                var particle = new VfxDocParticle { Name = particleSection };
                ref var p = ref particle.Def;

                p.Duration = ParseFloat(props.GetString(particleSection, "duration", "1.0"), VfxRange.One);

                if (props.HasKey(particleSection, "size"))
                    p.Size = ParseFloatCurve(props.GetString(particleSection, "size", ""), VfxFloatCurve.One);

                if (props.HasKey(particleSection, "speed"))
                    p.Speed = ParseFloatCurve(props.GetString(particleSection, "speed", ""), VfxFloatCurve.Zero);

                if (props.HasKey(particleSection, "color"))
                    p.Color = ParseColorCurve(props.GetString(particleSection, "color", ""), VfxColorCurve.White);

                if (props.HasKey(particleSection, "opacity"))
                    p.Opacity = ParseFloatCurve(props.GetString(particleSection, "opacity", ""), VfxFloatCurve.One);

                if (props.HasKey(particleSection, "gravity"))
                    p.Gravity = ParseVec2(props.GetString(particleSection, "gravity", ""), VfxVec2Range.Zero);

                if (props.HasKey(particleSection, "drag"))
                    p.Drag = ParseFloat(props.GetString(particleSection, "drag", ""), VfxRange.Zero);

                if (props.HasKey(particleSection, "rotation"))
                    p.Rotation = ParseFloat(props.GetString(particleSection, "rotation", ""), VfxRange.Zero);

                if (props.HasKey(particleSection, "rotationSpeed"))
                    p.RotationSpeed = ParseFloatCurve(props.GetString(particleSection, "rotationSpeed", ""), VfxFloatCurve.Zero);

                particle.SpriteRef = new DocumentRef<SpriteDocument>
                {
                    Name = props.GetString(particleSection, "sprite", "")
                };

                parsedParticles[particleSection] = particle;
            }
        }

        // Parse emitters
        var parsedEmitters = new List<VfxDocEmitter>();
        foreach (var emitterName in emitterNames)
        {
            if (string.IsNullOrWhiteSpace(emitterName)) continue;
            if (!props.HasGroup(emitterName)) continue;

            var particleSection = props.GetString(emitterName, "particle", "");
            if (string.IsNullOrEmpty(particleSection))
                particleSection = emitterName + ".particle";

            if (!parsedParticles.ContainsKey(particleSection)) continue;

            var emitter = new VfxDocEmitter
            {
                Name = emitterName,
                ParticleRef = particleSection
            };
            ref var e = ref emitter.Def;

            e.Rate = ParseInt(props.GetString(emitterName, "rate", "0"), VfxIntRange.Zero);
            e.Burst = ParseInt(props.GetString(emitterName, "burst", "0"), VfxIntRange.Zero);
            e.Duration = ParseFloat(props.GetString(emitterName, "duration", "1.0"), VfxRange.One);
            e.WorldSpace = props.GetString(emitterName, "worldSpace", "true").Trim()
                .Equals("true", StringComparison.OrdinalIgnoreCase);

            if (props.HasKey(emitterName, "angle"))
                e.Angle = ParseFloat(props.GetString(emitterName, "angle", ""), new VfxRange(0, 360));

            if (props.HasKey(emitterName, "spawn"))
                e.Spawn = ParseVec2(props.GetString(emitterName, "spawn", ""), VfxVec2Range.Zero);

            if (props.HasKey(emitterName, "direction"))
                e.Direction = ParseVec2(props.GetString(emitterName, "direction", ""), VfxVec2Range.Zero);

            parsedEmitters.Add(emitter);
            if (parsedEmitters.Count >= MaxEmitters) break;
        }

        Emitters.Clear();
        Emitters.AddRange(parsedEmitters);

        Particles.Clear();
        Particles.AddRange(parsedParticles.Values);
    }

    private void ResolveSpriteRefs()
    {
        foreach (var particle in Particles)
        {
            particle.SpriteRef.Resolve();
            particle.Def.Sprite = particle.SpriteRef.Value?.Sprite;
        }
    }

    // --- Text file serialization ---

    public override void Save(StreamWriter sw)
    {
        sw.WriteLine("[vfx]");
        sw.WriteLine($"duration = {FormatRange(_duration)}");
        sw.WriteLine($"loop = {_loop.ToString().ToLowerInvariant()}");

        // Emitter list
        sw.WriteLine();
        sw.WriteLine("[emitters]");
        foreach (var e in Emitters)
            sw.WriteLine(e.Name);

        // Emitter sections
        foreach (var e in Emitters)
        {
            sw.WriteLine();
            sw.WriteLine($"[{e.Name}]");
            sw.WriteLine($"rate = {FormatIntRange(e.Def.Rate)}");
            sw.WriteLine($"burst = {FormatIntRange(e.Def.Burst)}");
            sw.WriteLine($"duration = {FormatRange(e.Def.Duration)}");

            if (e.Def.Angle != default)
                sw.WriteLine($"angle = {FormatRange(e.Def.Angle)}");
            if (e.Def.Spawn != VfxVec2Range.Zero)
                sw.WriteLine($"spawn = {FormatVec2Range(e.Def.Spawn)}");
            if (e.Def.Direction != VfxVec2Range.Zero)
                sw.WriteLine($"direction = {FormatVec2Range(e.Def.Direction)}");
            if (!e.Def.WorldSpace)
                sw.WriteLine("worldSpace = false");

            // Write particle ref if it doesn't match the default naming
            if (e.ParticleRef != e.Name + ".particle")
                sw.WriteLine($"particle = {e.ParticleRef}");
        }

        // Particle sections — write each unique particle once
        var written = new HashSet<string>();
        foreach (var p in Particles)
        {
            if (!written.Add(p.Name)) continue;

            sw.WriteLine();
            sw.WriteLine($"[{p.Name}]");
            sw.WriteLine($"duration = {FormatRange(p.Def.Duration)}");

            if (p.Def.Size != VfxFloatCurve.One)
                sw.WriteLine($"size = {FormatFloatCurve(p.Def.Size)}");
            if (p.Def.Speed != VfxFloatCurve.Zero)
                sw.WriteLine($"speed = {FormatFloatCurve(p.Def.Speed)}");
            if (p.Def.Color != VfxColorCurve.White)
                sw.WriteLine($"color = {FormatColorCurve(p.Def.Color)}");
            if (p.Def.Opacity != VfxFloatCurve.One)
                sw.WriteLine($"opacity = {FormatFloatCurve(p.Def.Opacity)}");
            if (p.Def.Gravity != VfxVec2Range.Zero)
                sw.WriteLine($"gravity = {FormatVec2Range(p.Def.Gravity)}");
            if (p.Def.Drag != VfxRange.Zero)
                sw.WriteLine($"drag = {FormatRange(p.Def.Drag)}");
            if (p.Def.Rotation != VfxRange.Zero)
                sw.WriteLine($"rotation = {FormatRange(p.Def.Rotation)}");
            if (p.Def.RotationSpeed != VfxFloatCurve.Zero)
                sw.WriteLine($"rotationSpeed = {FormatFloatCurve(p.Def.RotationSpeed)}");
            if (p.SpriteRef.HasValue)
                sw.WriteLine($"sprite = {p.SpriteRef.Name}");
        }
    }

    // --- Binary serialization ---

    private void WriteBinary(string outputPath)
    {
        // Build the runtime emitter array first
        var emitterDefs = new VfxEmitterDef[Emitters.Count];
        for (var i = 0; i < Emitters.Count; i++)
        {
            emitterDefs[i] = Emitters[i].Def;
            var particle = FindParticle(Emitters[i].ParticleRef);
            if (particle != null)
            {
                emitterDefs[i].Particle = particle.Def;
                emitterDefs[i].Particle.Sprite = particle.SpriteRef.Value?.Sprite;
            }
        }

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Vfx, Vfx.Version);

        var bounds = CalculateBounds(emitterDefs);
        writer.Write(bounds.X);
        writer.Write(bounds.Y);
        writer.Write(bounds.Width);
        writer.Write(bounds.Height);

        writer.Write(_duration.Min);
        writer.Write(_duration.Max);
        writer.Write(_loop);

        writer.Write(emitterDefs.Length);

        for (var i = 0; i < emitterDefs.Length; i++)
        {
            ref var e = ref emitterDefs[i];

            writer.Write(e.Rate.Min);
            writer.Write(e.Rate.Max);
            writer.Write(e.Burst.Min);
            writer.Write(e.Burst.Max);
            writer.Write(e.Duration.Min);
            writer.Write(e.Duration.Max);
            writer.Write(e.Angle.Min);
            writer.Write(e.Angle.Max);
            writer.Write(e.Spawn.Min.X);
            writer.Write(e.Spawn.Min.Y);
            writer.Write(e.Spawn.Max.X);
            writer.Write(e.Spawn.Max.Y);
            writer.Write(e.Direction.Min.X);
            writer.Write(e.Direction.Min.Y);
            writer.Write(e.Direction.Max.X);
            writer.Write(e.Direction.Max.Y);
            writer.Write(e.WorldSpace);

            ref var p = ref e.Particle;
            writer.Write(p.Duration.Min);
            writer.Write(p.Duration.Max);
            WriteFloatCurve(writer, p.Size);
            WriteFloatCurve(writer, p.Speed);
            WriteColorCurve(writer, p.Color);
            WriteFloatCurve(writer, p.Opacity);
            writer.Write(p.Gravity.Min.X);
            writer.Write(p.Gravity.Min.Y);
            writer.Write(p.Gravity.Max.X);
            writer.Write(p.Gravity.Max.Y);
            writer.Write(p.Drag.Min);
            writer.Write(p.Drag.Max);
            writer.Write(p.Rotation.Min);
            writer.Write(p.Rotation.Max);
            WriteFloatCurve(writer, p.RotationSpeed);

            var particle = FindParticle(Emitters[i].ParticleRef);
            var spriteName = particle?.SpriteRef.Name ?? "";
            var spriteBytes = System.Text.Encoding.UTF8.GetBytes(spriteName);
            writer.Write(spriteBytes.Length);
            if (spriteBytes.Length > 0)
                writer.Write(spriteBytes);
        }
    }

    private static void WriteFloatCurve(BinaryWriter writer, VfxFloatCurve curve)
    {
        writer.Write((byte)curve.Type);
        writer.Write(curve.Start.Min);
        writer.Write(curve.Start.Max);
        writer.Write(curve.End.Min);
        writer.Write(curve.End.Max);
        if (curve.Type == VfxCurveType.CubicBezier)
        {
            writer.Write(curve.Bezier.X);
            writer.Write(curve.Bezier.Y);
            writer.Write(curve.Bezier.Z);
            writer.Write(curve.Bezier.W);
        }
    }

    private static void WriteColorCurve(BinaryWriter writer, VfxColorCurve curve)
    {
        writer.Write((byte)curve.Type);
        writer.Write(curve.Start.Min.R);
        writer.Write(curve.Start.Min.G);
        writer.Write(curve.Start.Min.B);
        writer.Write(curve.Start.Min.A);
        writer.Write(curve.Start.Max.R);
        writer.Write(curve.Start.Max.G);
        writer.Write(curve.Start.Max.B);
        writer.Write(curve.Start.Max.A);
        writer.Write(curve.End.Min.R);
        writer.Write(curve.End.Min.G);
        writer.Write(curve.End.Min.B);
        writer.Write(curve.End.Min.A);
        writer.Write(curve.End.Max.R);
        writer.Write(curve.End.Max.G);
        writer.Write(curve.End.Max.B);
        writer.Write(curve.End.Max.A);
        if (curve.Type == VfxCurveType.CubicBezier)
        {
            writer.Write(curve.Bezier.X);
            writer.Write(curve.Bezier.Y);
            writer.Write(curve.Bezier.Z);
            writer.Write(curve.Bezier.W);
        }
    }

    // --- Value parsers ---

    internal static VfxRange ParseFloat(string str, VfxRange defaultValue)
    {
        if (string.IsNullOrEmpty(str)) return defaultValue;
        var tk = new Tokenizer(str);

        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectFloat(out float min)) return defaultValue;
            if (!tk.ExpectDelimiter(',')) return defaultValue;
            if (!tk.ExpectFloat(out float max)) return defaultValue;
            tk.ExpectDelimiter(']');
            return new VfxRange(MathF.Min(min, max), MathF.Max(min, max));
        }

        if (tk.ExpectFloat(out float value))
            return new VfxRange(value, value);

        return defaultValue;
    }

    internal static VfxIntRange ParseInt(string str, VfxIntRange defaultValue)
    {
        if (string.IsNullOrEmpty(str)) return defaultValue;
        var tk = new Tokenizer(str);

        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectInt(out int min)) return defaultValue;
            if (!tk.ExpectDelimiter(',')) return defaultValue;
            if (!tk.ExpectInt(out int max)) return defaultValue;
            tk.ExpectDelimiter(']');
            return new VfxIntRange(Math.Min(min, max), Math.Max(min, max));
        }

        if (tk.ExpectInt(out int value))
            return new VfxIntRange(value, value);

        return defaultValue;
    }

    private static VfxVec2Range ParseVec2(string str, VfxVec2Range defaultValue)
    {
        if (string.IsNullOrEmpty(str)) return defaultValue;
        var tk = new Tokenizer(str);

        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectVec2(out Vector2 min)) return defaultValue;
            if (!tk.ExpectDelimiter(',')) return defaultValue;
            if (!tk.ExpectVec2(out Vector2 max)) return defaultValue;
            tk.ExpectDelimiter(']');
            return new VfxVec2Range(
                new Vector2(MathF.Min(min.X, max.X), MathF.Min(min.Y, max.Y)),
                new Vector2(MathF.Max(min.X, max.X), MathF.Max(min.Y, max.Y)));
        }

        if (tk.ExpectVec2(out Vector2 value))
            return new VfxVec2Range(value, value);

        return defaultValue;
    }

    private static VfxFloatCurve ParseFloatCurve(string str, VfxFloatCurve defaultValue)
    {
        if (string.IsNullOrEmpty(str)) return defaultValue;
        var tk = new Tokenizer(str);
        var curve = new VfxFloatCurve { Type = VfxCurveType.Linear };

        if (!ParseFloatValue(ref tk, out curve.Start)) return defaultValue;

        if (!tk.ExpectDelimiter('='))
        {
            curve.End = curve.Start;
            return curve;
        }

        if (!tk.ExpectDelimiter('>')) return defaultValue;
        if (!ParseFloatValue(ref tk, out curve.End)) return defaultValue;

        if (tk.ExpectDelimiter(':') && tk.ExpectIdentifier(out string curveType))
        {
            if (curveType.Equals("bezier", StringComparison.OrdinalIgnoreCase) && tk.ExpectVec4(out Vector4 bezierPoints))
            {
                curve.Type = VfxCurveType.CubicBezier;
                curve.Bezier = bezierPoints;
            }
            else
            {
                curve.Type = ParseCurveType(curveType);
            }
        }

        return curve;
    }

    private static bool ParseFloatValue(ref Tokenizer tk, out VfxRange value)
    {
        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectFloat(out float min)) { value = default; return false; }
            if (!tk.ExpectDelimiter(',')) { value = default; return false; }
            if (!tk.ExpectFloat(out float max)) { value = default; return false; }
            tk.ExpectDelimiter(']');
            value = new VfxRange(MathF.Min(min, max), MathF.Max(min, max));
            return true;
        }

        if (tk.ExpectFloat(out float v))
        {
            value = new VfxRange(v, v);
            return true;
        }

        value = default;
        return false;
    }

    private static VfxColorCurve ParseColorCurve(string str, VfxColorCurve defaultValue)
    {
        if (string.IsNullOrEmpty(str)) return defaultValue;
        var tk = new Tokenizer(str);
        var curve = new VfxColorCurve { Type = VfxCurveType.Linear };

        if (!ParseColorValue(ref tk, out curve.Start)) return defaultValue;

        if (!tk.ExpectDelimiter('='))
        {
            curve.End = curve.Start;
            return curve;
        }

        if (!tk.ExpectDelimiter('>')) return defaultValue;
        if (!ParseColorValue(ref tk, out curve.End)) return defaultValue;

        if (tk.ExpectDelimiter(':') && tk.ExpectIdentifier(out string curveType))
        {
            if (curveType.Equals("bezier", StringComparison.OrdinalIgnoreCase) && tk.ExpectVec4(out Vector4 bezierPoints))
            {
                curve.Type = VfxCurveType.CubicBezier;
                curve.Bezier = bezierPoints;
            }
            else
            {
                curve.Type = ParseCurveType(curveType);
            }
        }

        return curve;
    }

    private static bool ParseColorValue(ref Tokenizer tk, out VfxColorRange value)
    {
        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectColor(out Color min)) { value = default; return false; }
            if (!tk.ExpectDelimiter(',')) { value = default; return false; }
            if (!tk.ExpectColor(out Color max)) { value = default; return false; }
            tk.ExpectDelimiter(']');
            value = new VfxColorRange(min, max);
            return true;
        }

        if (tk.ExpectColor(out Color c))
        {
            value = new VfxColorRange(c, c);
            return true;
        }

        value = default;
        return false;
    }

    private static VfxCurveType ParseCurveType(string name) => name.ToLowerInvariant() switch
    {
        "linear" => VfxCurveType.Linear,
        "easein" => VfxCurveType.EaseIn,
        "easeout" => VfxCurveType.EaseOut,
        "easeinout" => VfxCurveType.EaseInOut,
        "quadratic" => VfxCurveType.Quadratic,
        "cubic" => VfxCurveType.Cubic,
        "sine" => VfxCurveType.Sine,
        "bell" => VfxCurveType.Bell,
        _ => VfxCurveType.Linear
    };

    // --- Formatters ---

    internal static string FormatFloat(float v) =>
        v == (int)v ? ((int)v).ToString() : v.ToString("G");

    internal static string FormatRange(VfxRange r) =>
        r.Min == r.Max ? FormatFloat(r.Min) : $"[{FormatFloat(r.Min)}, {FormatFloat(r.Max)}]";

    internal static string FormatIntRange(VfxIntRange r) =>
        r.Min == r.Max ? r.Min.ToString() : $"[{r.Min}, {r.Max}]";

    internal static string FormatVec2Range(VfxVec2Range r)
    {
        if (r.Min == r.Max)
            return $"({FormatFloat(r.Min.X)}, {FormatFloat(r.Min.Y)})";
        return $"[({FormatFloat(r.Min.X)}, {FormatFloat(r.Min.Y)}), ({FormatFloat(r.Max.X)}, {FormatFloat(r.Max.Y)})]";
    }

    internal static string FormatFloatCurve(VfxFloatCurve c)
    {
        var start = FormatRange(c.Start);
        var end = FormatRange(c.End);

        if (c.Start.Min == c.End.Min && c.Start.Max == c.End.Max)
            return start;

        var type = c.Type == VfxCurveType.CubicBezier
            ? FormatBezier(c.Bezier)
            : FormatCurveType(c.Type);
        return $"{start}=>{end}:{type}";
    }

    internal static string FormatColorCurve(VfxColorCurve c)
    {
        var start = FormatColorRange(c.Start);
        var end = FormatColorRange(c.End);

        if (c.Start.Min == c.End.Min && c.Start.Max == c.End.Max)
            return start;

        var type = c.Type == VfxCurveType.CubicBezier
            ? FormatBezier(c.Bezier)
            : FormatCurveType(c.Type);
        return $"{start}=>{end}:{type}";
    }

    private static string FormatColorRange(VfxColorRange r)
    {
        if (r.Min == r.Max) return FormatColor(r.Min);
        return $"[{FormatColor(r.Min)}, {FormatColor(r.Max)}]";
    }

    internal static string FormatColor(Color c)
    {
        var r = (byte)(Math.Clamp(c.R, 0, 1) * 255);
        var g = (byte)(Math.Clamp(c.G, 0, 1) * 255);
        var b = (byte)(Math.Clamp(c.B, 0, 1) * 255);
        var a = (byte)(Math.Clamp(c.A, 0, 1) * 255);
        return a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }

    private static string FormatBezier(Vector4 b) =>
        $"bezier({FormatFloat(b.X)}, {FormatFloat(b.Y)}, {FormatFloat(b.Z)}, {FormatFloat(b.W)})";

    internal static string FormatCurveType(VfxCurveType type) => type switch
    {
        VfxCurveType.Linear => "linear",
        VfxCurveType.EaseIn => "easein",
        VfxCurveType.EaseOut => "easeout",
        VfxCurveType.EaseInOut => "easeinout",
        VfxCurveType.Quadratic => "quadratic",
        VfxCurveType.Cubic => "cubic",
        VfxCurveType.Sine => "sine",
        VfxCurveType.Bell => "bell",
        _ => "linear"
    };
}
