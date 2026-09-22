using System.Numerics;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

public readonly struct VfxHandle3D
{
    internal readonly VfxSystem3D? Owner;
    internal readonly int Index;
    internal readonly uint Version;
    internal VfxHandle3D(VfxSystem3D owner, int index, uint version) { Owner = owner; Index = index; Version = version; }
    public static readonly VfxHandle3D Invalid = default;
}

public readonly record struct VfxParticleState3D(Vector3 Position, Vector3 Velocity, float Age, float Lifetime);

/// <summary>Scene-owned, bounded CPU simulation with instanced camera-facing billboards.</summary>
public sealed class VfxSystem3D : IDisposable
{
    private sealed class Instance
    {
        public Matrix4x4 Transform;
        public Color Tint;
        public bool Loop, Stopped;
        public Emitter[] Emitters = [];
        public int Particles;
    }

    private struct Emitter
    {
        public VfxEmitterDef3D Def;
        public float Age, Duration, RateStart, RateEnd;
        public double Credit;
        public bool Finished;
    }

    private struct Particle
    {
        public Vector3 Position, Direction, GravityVelocity, Velocity;
        public int Instance, Emitter, Frame;
        public float Age, Lifetime, Rotation;
        public float SizeStart, SizeEnd, SpeedStart, SpeedEnd, GravityStart, GravityEnd;
        public float OpacityStart, OpacityEnd, SpinStart, SpinEnd;
        public Color ColorStart, ColorEnd;
    }

    private struct SortItem : IComparable<SortItem>
    {
        public int Index;
        public float Depth;
        public readonly int CompareTo(SortItem other)
        {
            var result = other.Depth.CompareTo(Depth);
            return result != 0 ? result : Index.CompareTo(other.Index);
        }
    }

    private readonly Instance?[] _instances;
    private readonly uint[] _versions;
    private readonly Particle[] _particles;
    private readonly SortItem[] _sort;
    private readonly VfxBillboard3D[] _billboards;
    private readonly Random _random;
    private RenderMesh _quad;
    private bool _disposed;
    public int ActiveParticleCount { get; private set; }
    public int ActiveInstanceCount { get; private set; }
    public Vector3 GravityDirection { get; set; } = -Vector3.UnitY;
    /// <summary>Optional borrowed texture resolver, useful for editor libraries.</summary>
    public Func<string, Texture?>? TextureResolver { get; set; }

    public VfxSystem3D(int maxParticles = 4096, int maxInstances = 256, int? seed = null)
    {
        if (maxParticles is < 1 or > 1_000_000 || maxInstances is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(maxParticles));
        _particles = new Particle[maxParticles]; _sort = new SortItem[maxParticles];
        _billboards = new VfxBillboard3D[maxParticles];
        _instances = new Instance[maxInstances]; _versions = new uint[maxInstances];
        Array.Fill(_versions, 1u);
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public VfxHandle3D Play(Vfx3D? effect, Vector3 position, Color? tint = null) =>
        Play(effect, Matrix4x4.CreateTranslation(position), tint);

    public VfxHandle3D Play(Vfx3D? effect, Matrix4x4 transform, Color? tint = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (effect == null || effect.Emitters.Length == 0) return default;
        Vfx3D.Validate(effect.Emitters);
        CheckTransform(transform);
        var index = Array.FindIndex(_instances, i => i == null);
        if (index < 0) return default;
        var instance = new Instance { Transform = transform, Tint = tint ?? Color.White, Loop = effect.Loop,
            Emitters = new Emitter[effect.Emitters.Length] };
        _instances[index] = instance; ActiveInstanceCount++;
        for (var i = 0; i < instance.Emitters.Length; i++)
        {
            instance.Emitters[i].Def = effect.Emitters[i];
            BeginCycle(ref instance.Emitters[i]);
            Burst(index, i, 0);
        }
        return new(this, index, _versions[index]);
    }

    public bool IsPlaying(VfxHandle3D handle) => Get(handle) != null;
    public void SetTransform(VfxHandle3D handle, Vector3 position) => SetTransform(handle, Matrix4x4.CreateTranslation(position));
    public void SetTransform(VfxHandle3D handle, Matrix4x4 transform)
    {
        if (Get(handle) is not { } instance) return;
        CheckTransform(transform); instance.Transform = transform;
    }
    public void SetTint(VfxHandle3D handle, Color tint) { if (Get(handle) is { } instance) instance.Tint = tint; }
    public void Stop(VfxHandle3D handle)
    {
        if (Get(handle) is not { } instance) return;
        instance.Stopped = true; instance.Loop = false;
        if (instance.Particles == 0) FreeInstance(handle.Index);
    }
    public void Stop(ref VfxHandle3D handle) { Stop(handle); handle = default; }
    public void Kill(VfxHandle3D handle)
    {
        if (Get(handle) == null) return;
        for (var i = ActiveParticleCount - 1; i >= 0; i--)
            if (_particles[i].Instance == handle.Index) RemoveParticle(i);
        FreeInstance(handle.Index);
    }
    public void Clear()
    {
        ActiveParticleCount = 0;
        for (var i = 0; i < _instances.Length; i++) if (_instances[i] != null) FreeInstance(i);
    }

    /// <summary>Advances once per scene update. Emissions are aged from their birth time within the frame.</summary>
    public void Update(float seconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!float.IsFinite(seconds) || seconds <= 0) return;
        for (var i = ActiveParticleCount - 1; i >= 0; i--)
            if (!Advance(ref _particles[i], seconds)) RemoveParticle(i);

        for (var i = 0; i < _instances.Length; i++)
        {
            if (_instances[i] is not { } instance) continue;
            var finished = true;
            for (var j = 0; j < instance.Emitters.Length; j++)
            {
                ref var emitter = ref instance.Emitters[j];
                if (!instance.Stopped) UpdateEmitter(i, j, seconds);
                finished &= emitter.Finished;
            }
            if (instance.Particles == 0 && (instance.Stopped || finished)) FreeInstance(i);
        }
    }

    private void UpdateEmitter(int instanceIndex, int emitterIndex, float seconds)
    {
        var instance = _instances[instanceIndex]!;
        ref var e = ref instance.Emitters[emitterIndex];
        var remaining = seconds;
        // Catch-up is bounded even for tiny looping durations after a suspended application.
        var cycles = 0;
        var budget = _particles.Length;
        while (!e.Finished && remaining > 0)
        {
            var step = Math.Min(remaining, e.Duration - e.Age);
            var midpoint = (e.Age + step * .5f) / e.Duration;
            var rate = Math.Max(0, Mix(e.RateStart, e.RateEnd, VfxMath.Evaluate(e.Def.Rate.Lut, midpoint)));
            var credit = e.Credit + (double)rate * step;
            var births = Math.Floor(credit);
            var count = (int)Math.Min(births, budget);
            for (var n = 0; n < count; n++)
            {
                // Under overload retain the newest births, which can still be alive after a long frame.
                var birth = rate > 0 ? (float)((births - count + n + 1 - e.Credit) / rate) : step;
                Emit(instanceIndex, emitterIndex, Math.Max(0, remaining - birth));
            }
            budget -= count;
            e.Credit = credit - Math.Floor(credit);
            e.Age += step; remaining -= step;
            if (e.Age >= e.Duration)
            {
                if (!instance.Loop) { e.Finished = true; break; }
                BeginCycle(ref e);
                Burst(instanceIndex, emitterIndex, remaining);
                if (++cycles >= 256)
                {
                    e.Age = remaining % e.Duration;
                    break;
                }
            }
        }
    }

    private void BeginCycle(ref Emitter e)
    {
        e.Age = 0; e.Credit = 0; e.Duration = Random(e.Def.Duration);
        e.RateStart = Random(e.Def.Rate.Start); e.RateEnd = Random(e.Def.Rate.End);
    }
    private void Burst(int instance, int emitter, float age)
    {
        var range = _instances[instance]!.Emitters[emitter].Def.Burst;
        var count = Math.Min(_particles.Length, _random.Next(range.Min, range.Max + 1));
        for (var i = 0; i < count; i++) Emit(instance, emitter, age);
    }
    private void Emit(int instanceIndex, int emitterIndex, float age)
    {
        if (ActiveParticleCount == _particles.Length) return;
        var instance = _instances[instanceIndex]!;
        ref var e = ref instance.Emitters[emitterIndex].Def;
        ref var d = ref e.Particle;
        var lifetime = Random(d.Duration);
        if (age >= lifetime) return;
        var position = Spawn(e.Spawn);
        var direction = Cone(e.Direction, e.Spread);
        if (e.WorldSpace)
        {
            position = Vector3.Transform(position, instance.Transform);
            direction = Vector3.TransformNormal(direction, instance.Transform);
        }
        var p = new Particle
        {
            Instance = instanceIndex, Emitter = emitterIndex, Position = position, Direction = direction,
            Lifetime = lifetime, Rotation = Random(d.Rotation) * MathEx.Deg2Rad,
            SizeStart = Random(d.Size.Start), SizeEnd = Random(d.Size.End),
            SpeedStart = Random(d.Speed.Start), SpeedEnd = Random(d.Speed.End),
            GravityStart = Random(d.Gravity.Start), GravityEnd = Random(d.Gravity.End),
            OpacityStart = Random(d.Opacity.Start), OpacityEnd = Random(d.Opacity.End),
            SpinStart = Random(d.RotationSpeed.Start), SpinEnd = Random(d.RotationSpeed.End),
            ColorStart = Color.Mix(d.Color.Start.Min, d.Color.Start.Max, _random.NextSingle()),
            ColorEnd = Color.Mix(d.Color.End.Min, d.Color.End.Max, _random.NextSingle()),
            Frame = d.FrameMode == VfxFrameMode.Random ? _random.Next(d.Columns * d.Rows) : 0
        };
        p.Velocity = direction * p.SpeedStart;
        if (age > 0 && !Advance(ref p, age)) return;
        _particles[ActiveParticleCount++] = p; instance.Particles++;
    }

    private bool Advance(ref Particle p, float seconds)
    {
        if (p.Age + seconds >= p.Lifetime) return false;
        ref var d = ref _instances[p.Instance]!.Emitters[p.Emitter].Def.Particle;
        // Midpoint integration, including the half-acceleration displacement term.
        var t = (p.Age + seconds * .5f) / p.Lifetime;
        var speed = Mix(p.SpeedStart, p.SpeedEnd, VfxMath.Evaluate(d.Speed.Lut, t));
        var gravity = GravityDirection * Mix(p.GravityStart, p.GravityEnd, VfxMath.Evaluate(d.Gravity.Lut, t));
        p.Position += (p.Direction * speed + p.GravityVelocity) * seconds + gravity * (.5f * seconds * seconds);
        p.GravityVelocity += gravity * seconds;
        p.Velocity = p.Direction * speed + p.GravityVelocity;
        p.Rotation += Mix(p.SpinStart, p.SpinEnd, VfxMath.Evaluate(d.RotationSpeed.Lut, t)) * MathEx.Deg2Rad * seconds;
        p.Age += seconds;
        return true;
    }

    public VfxParticleState3D GetParticle(int index)
    {
        if ((uint)index >= (uint)ActiveParticleCount) throw new ArgumentOutOfRangeException(nameof(index));
        ref var p = ref _particles[index];
        var instance = _instances[p.Instance]!;
        var world = instance.Emitters[p.Emitter].Def.WorldSpace;
        return new(world ? p.Position : Vector3.Transform(p.Position, instance.Transform),
            world ? p.Velocity : Vector3.TransformNormal(p.Velocity, instance.Transform), p.Age, p.Lifetime);
    }

    public void Draw(Camera3D camera, Shader shader, ushort order = 15) => Draw(camera, camera.ViewProjectionMatrix, shader, order);

    /// <summary>Draw within the host's depth-enabled pass. Shader must use the VFX billboard instance layout.</summary>
    public void Draw(Camera3D camera, in Matrix4x4 viewProjection, Shader shader, ushort order = 15)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ActiveParticleCount == 0) return;
        for (var i = 0; i < ActiveParticleCount; i++)
            _sort[i] = new() { Index = i, Depth = Vector3.Dot(GetParticle(i).Position - camera.Position, camera.Forward) };
        Array.Sort(_sort, 0, ActiveParticleCount);
        Matrix4x4.Invert(camera.ViewMatrix, out var inverseView);
        var right = new Vector3(inverseView.M11, inverseView.M12, inverseView.M13);
        var up = new Vector3(inverseView.M21, inverseView.M22, inverseView.M23);
        for (var i = 0; i < ActiveParticleCount; i++)
        {
            ref var p = ref _particles[_sort[i].Index];
            var instance = _instances[p.Instance]!;
            ref var d = ref instance.Emitters[p.Emitter].Def.Particle;
            var t = p.Age / p.Lifetime;
            var size = Math.Max(0, Mix(p.SizeStart, p.SizeEnd, VfxMath.Evaluate(d.Size.Lut, t)));
            var color = Color.Mix(p.ColorStart, p.ColorEnd, VfxMath.Evaluate(d.Color.Lut, t));
            var opacity = Math.Clamp(Mix(p.OpacityStart, p.OpacityEnd, VfxMath.Evaluate(d.Opacity.Lut, t)), 0, 1);
            var tint = instance.Tint;
            var frame = d.FrameMode == VfxFrameMode.Random ? p.Frame : Math.Min((int)(t * d.Columns * d.Rows), d.Columns * d.Rows - 1);
            var cos = MathF.Cos(p.Rotation) * size; var sin = MathF.Sin(p.Rotation) * size;
            _billboards[i] = new()
            {
                Position = GetParticle(_sort[i].Index).Position,
                Right = right * cos + up * sin, Up = up * cos - right * sin,
                Color = new(color.R * tint.R, color.G * tint.G, color.B * tint.B, color.A * tint.A * opacity),
                UV = new((frame % d.Columns) / (float)d.Columns, (frame / d.Columns) / (float)d.Rows, 1f / d.Columns, 1f / d.Rows)
            };
        }
        EnsureQuad();
        using var state = Graphics.PushState();
        Graphics.SetShader(shader); Graphics.SetMesh(_quad); Graphics.ClearDrawParameters();
        Graphics.SetViewProjection(viewProjection);
        var start = 0;
        while (start < ActiveParticleCount)
        {
            ref readonly var d = ref Definition(_sort[start].Index);
            var end = start + 1;
            while (end < ActiveParticleCount)
            {
                ref readonly var next = ref Definition(_sort[end].Index);
                if (next.Blend != d.Blend || next.Texture != d.Texture) break;
                end++;
            }
            var texture = string.IsNullOrEmpty(d.Texture) ? null : TextureResolver != null
                ? TextureResolver(d.Texture) : Asset.Load(AssetType.Texture, d.Texture) as Texture;
            if (texture != null) Graphics.SetTexture(texture); else Graphics.SetTexture(Graphics.WhiteTexture);
            Graphics.SetTextureFilter(texture?.Filter ?? TextureFilter.Linear);
            Graphics.SetBlendMode(d.Blend == VfxBlend3D.Additive ? BlendMode.Additive : BlendMode.Alpha);
            Graphics.DrawElementsInstanced<VfxBillboard3D>(6, _billboards.AsSpan(start, end - start), order: order);
            start = end;
        }
    }

    private ref readonly VfxParticleDef3D Definition(int index)
    {
        ref var p = ref _particles[index];
        return ref _instances[p.Instance]!.Emitters[p.Emitter].Def.Particle;
    }
    private void EnsureQuad()
    {
        if (_quad.Handle != 0) return;
        _quad = Graphics.CreateMesh<MeshVertex3D>(4, 6, BufferUsage.Static, "VFX billboard quad");
        MeshVertex3D[] vertices =
        [
            new(new(-.5f, .5f, 0), Vector3.UnitZ, new(1,0,0,1), new(0,0), Vector4.One),
            new(new(.5f, .5f, 0), Vector3.UnitZ, new(1,0,0,1), new(1,0), Vector4.One),
            new(new(.5f, -.5f, 0), Vector3.UnitZ, new(1,0,0,1), new(1,1), Vector4.One),
            new(new(-.5f, -.5f, 0), Vector3.UnitZ, new(1,0,0,1), new(0,1), Vector4.One)
        ];
        Graphics.UpdateMesh<MeshVertex3D>(_quad, vertices, new ushort[] { 0, 2, 1, 0, 3, 2 });
    }
    private Instance? Get(VfxHandle3D h) => !_disposed && h.Owner == this && (uint)h.Index < (uint)_instances.Length &&
        h.Version == _versions[h.Index] ? _instances[h.Index] : null;
    private void FreeInstance(int index)
    {
        _instances[index] = null; ActiveInstanceCount--;
        if (++_versions[index] == 0) _versions[index] = 1;
    }
    private void RemoveParticle(int index)
    {
        _instances[_particles[index].Instance]!.Particles--;
        _particles[index] = _particles[--ActiveParticleCount];
    }
    private static void CheckTransform(Matrix4x4 transform)
    {
        var values = MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(in transform, 1));
        foreach (var value in values) if (!float.IsFinite(value)) throw new ArgumentException("VFX transform must be finite.");
    }
    private float Random(VfxRange range) => Mix(range.Min, range.Max, _random.NextSingle());
    private static float Mix(float a, float b, float t) => a + (b - a) * t;
    private Vector3 Spawn(VfxSpawnDef3D spawn)
    {
        if (spawn.Shape == VfxSpawnShape3D.Box)
            return spawn.Offset + new Vector3(_random.NextSingle() - .5f, _random.NextSingle() - .5f, _random.NextSingle() - .5f) * spawn.Size;
        if (spawn.Shape != VfxSpawnShape3D.Sphere) return spawn.Offset;
        var inner = spawn.InnerRadius; var outer = spawn.Radius;
        var radius = MathF.Cbrt(Mix(inner * inner * inner, outer * outer * outer, _random.NextSingle()));
        return spawn.Offset + Cone(Vector3.UnitY, 180) * radius;
    }
    private Vector3 Cone(Vector3 direction, float spread)
    {
        var axis = direction.LengthSquared() > 1e-10f ? Vector3.Normalize(direction) : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Y) < .99f ? Vector3.UnitY : Vector3.UnitX));
        var up = Vector3.Cross(right, axis);
        var cos = Mix(MathF.Cos(spread * MathEx.Deg2Rad), 1, _random.NextSingle());
        var sin = MathF.Sqrt(Math.Max(0, 1 - cos * cos));
        var angle = _random.NextSingle() * MathF.Tau;
        return axis * cos + (right * MathF.Cos(angle) + up * MathF.Sin(angle)) * sin;
    }
    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        if (_quad.Handle != 0) Graphics.DestroyMesh(_quad);
        _quad = default; _disposed = true;
    }
}

/// <summary>Per-billboard instance attributes for the vfx3d shader.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct VfxBillboard3D : IVertex
{
    public Vector3 Position, Right, Up;
    public Vector4 Color, UV;
    public static VertexFormatDescriptor GetFormatDescriptor() => new()
    {
        Stride = Marshal.SizeOf<VfxBillboard3D>(),
        Attributes = [new(5, 3, VertexAttribType.Float, 0), new(6, 3, VertexAttribType.Float, 12),
            new(7, 3, VertexAttribType.Float, 24), new(8, 4, VertexAttribType.Float, 36), new(9, 4, VertexAttribType.Float, 52)]
    };
}
