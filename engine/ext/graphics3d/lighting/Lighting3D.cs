//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

public readonly record struct PointLight3D(int Id, Vector3 Position, Vector3 Color, float Intensity, float Range);

/// <summary>Visible geometry, not gameplay collision proxies. Revision covers in-place mesh edits.</summary>
public readonly record struct ShadowCaster3D(RenderMesh Mesh, int IndexCount, Matrix4x4 Transform,
    Vector3 Min, Vector3 Max, long Revision = 0,
    ReadOnlyMemory<MeshVertex3D> Vertices = default, ReadOnlyMemory<uint> Indices = default);

public sealed class LightingSettings3D
{
    public Vector3 SunDirection { get; set; } = Vector3.Normalize(new(.45f, .8f, .35f));
    public Vector3 SunColor { get; set; } = new(1, .94f, .82f);
    public float SunIntensity { get; set; } = .8f;
    public float SkyIntensity { get; set; } = .45f;
    public float InteriorAmbient { get; set; } = .055f;
    /// <summary>Host shader shadow softness, from hard (0) to broad (1). Does not invalidate shadow maps.</summary>
    public float ShadowSoftness { get; set; } = .65f;
}

/// <summary>
/// Cached direct-light shadows and a world-space skylight volume. Prepare before
/// entering the scene render pass. Bind exposes texture slots 1..4; slot 0 belongs
/// to the material. Each view owns an instance, including previews.
/// </summary>
public sealed class Lighting3D : IDisposable
{
    public const int MaxPointLights = 16;
    public const int ShadowFaceSize = 256;
    private const int FaceWidth = ShadowFaceSize * 3, FaceHeight = ShadowFaceSize * 2;
    private readonly record struct ShadowDraw(Matrix4x4 Normal, Matrix4x4 Model, Vector4 Light);
    private sealed class PointCache : IDisposable
    {
        public PointLight3D Light;
        public ShadowCaster3D[] Casters = [];
        public readonly RenderTexture Target = RenderTexture.Create(FaceWidth, FaceHeight,
            format: TextureFormat.RGBA8, name: "Point shadow faces", depth: true);
        public bool Valid;
        public readonly ShadowGeometry3D Geometry = new();
        public void Dispose() { Target.Dispose(); Geometry.Dispose(); }
    }
    private readonly Dictionary<int, PointCache> _points = [];
    // RGBA32F texels: 0 direction/intensity, 1 sun RGB/sky strength,
    // 2 ambient floor/light count/face size, 3..6 sun VP, 7 sky min/cell size,
    // 8 sky dimensions, 9..40 alternating point position/range and RGB/strength,
    // 41..44 inverse-transpose sun VP, 45 sun texel size XY/depth range/softness.
    // Matrices use System.Numerics row layout.
    private readonly Vector4[] _data = new Vector4[64];
    private readonly List<ShadowCaster3D> _nearby = [];
    private ShadowCaster3D[] _sunCasters = [];
    private readonly ShadowGeometry3D _sunGeometry = new();
    private Matrix4x4 _sunMatrix;
    private bool _sunValid, _atlasValid;
    private int[] _atlasIds = [];
    private RenderTexture? _sun, _atlas;
    private Texture? _parameters;
    private Shader? _shadowShader, _copyShader;
    private RenderMesh _quad;
    public SkylightVolume3D Skylight { get; } = new();
    public int ShadowUpdatesLastFrame { get; private set; }
    public int ActivePointLights { get; private set; }
    public int OmittedPointLights { get; private set; }

    public void Invalidate()
    {
        _sunValid = _atlasValid = false;
        foreach (var cache in _points.Values) cache.Valid = false;
        Skylight.Invalidate();
    }

    public void Prepare(IReadOnlyList<ShadowCaster3D> casters, IReadOnlyList<PointLight3D> lights,
        LightingSettings3D settings, Vector3 min, Vector3 max, long skyRevision,
        Action<SkylightVolume3D> voxelize, Vector3 focus)
    {
        if (!float.IsFinite(min.LengthSquared()) || !float.IsFinite(max.LengthSquared()) ||
            min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z)
            throw new ArgumentException("Lighting bounds must be finite and have positive size.");
        EnsureResources();
        ShadowUpdatesLastFrame = 0;
        Skylight.Prepare(min, max, skyRevision, voxelize);
        var direction = settings.SunDirection;
        if (!float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() < .0001f) direction = Vector3.UnitY;
        direction = Vector3.Normalize(direction);
        var sunMatrix = SunProjection(min, max, direction);
        if (!_sunValid || sunMatrix != _sunMatrix || !_sunCasters.SequenceEqual(casters))
        {
            if (!_sunValid || !_sunCasters.SequenceEqual(casters)) _sunGeometry.Update(casters);
            _sunMatrix = sunMatrix;
            Graphics.BeginPass(_sun!, Color.White);
            try
            {
                ShadowState();
                DrawGeometry(_sunGeometry, casters, sunMatrix, Vector4.Zero);
            }
            finally { Graphics.EndPass(); }
            _sunCasters = casters.ToArray(); _sunValid = true; ShadowUpdatesLastFrame++;
        }

        // A bounded budget: excess lights are omitted, never rendered without
        // shadows. Stable distance/id ordering chooses the nearest sixteen.
        var selected = lights.Where(l => l.Range >= .1f && l.Range <= 64 && l.Intensity > 0 &&
                float.IsFinite(l.Position.LengthSquared()) && float.IsFinite(l.Color.LengthSquared()) && float.IsFinite(l.Intensity))
            .OrderBy(l => Vector3.DistanceSquared(focus, l.Position)).ThenBy(l => l.Id)
            .DistinctBy(l => l.Id).Take(MaxPointLights).OrderBy(l => l.Id).ToArray();
        ActivePointLights = selected.Length; OmittedPointLights = Math.Max(0, lights.Count - selected.Length);
        var ids = selected.Select(l => l.Id).ToArray();
        var changed = !_atlasValid || !ids.SequenceEqual(_atlasIds);
        foreach (var id in _points.Keys.Where(id => !ids.Contains(id)).ToArray()) { _points[id].Dispose(); _points.Remove(id); }
        foreach (var light in selected)
        {
            if (!_points.TryGetValue(light.Id, out var cache)) _points.Add(light.Id, cache = new());
            _nearby.Clear();
            foreach (var caster in casters)
                if (Vector3.DistanceSquared(light.Position, Vector3.Clamp(light.Position, caster.Min, caster.Max)) < light.Range * light.Range)
                    _nearby.Add(caster);
            if (!cache.Valid || cache.Light.Position != light.Position || cache.Light.Range != light.Range || !cache.Casters.SequenceEqual(_nearby))
            {
                RenderPoint(cache, light, _nearby);
                cache.Casters = _nearby.ToArray(); cache.Valid = true; changed = true;
                ShadowUpdatesLastFrame++;
            }
            cache.Light = light;
        }
        if (changed)
        {
            Graphics.BeginPass(_atlas!, Color.White);
            try
            {
                Graphics.ClearScissor(); Graphics.SetLayer(0); Graphics.SetBlendMode(BlendMode.None);
                Graphics.SetShader(_copyShader!); Graphics.SetViewProjection(Matrix4x4.Identity);
                Graphics.SetDrawParameters(Matrix4x4.Identity); Graphics.SetMesh(_quad);
                for (var i = 0; i < selected.Length; i++)
                {
                    Graphics.SetViewport((i % 4) * FaceWidth, (i / 4) * FaceHeight, FaceWidth, FaceHeight);
                    Graphics.SetTexture(_points[selected[i].Id].Target, 0); Graphics.SetTextureFilter(TextureFilter.Point, 0);
                    Graphics.DrawElements(6, order: (ushort)i);
                }
            }
            finally { Graphics.EndPass(); }
            _atlasIds = ids; _atlasValid = true;
        }
        Array.Clear(_data);
        _data[0] = new(direction, FiniteClamp(settings.SunIntensity, 4));
        var sunColor = float.IsFinite(settings.SunColor.LengthSquared()) ? Vector3.Max(settings.SunColor, Vector3.Zero) : Vector3.One;
        _data[1] = new(sunColor, FiniteClamp(settings.SkyIntensity, 2));
        _data[2] = new(FiniteClamp(settings.InteriorAmbient, 1), selected.Length, ShadowFaceSize, 0);
        MemoryMarshal.Cast<Vector4, Matrix4x4>(_data.AsSpan(3, 4))[0] = _sunMatrix;
        Matrix4x4.Invert(_sunMatrix, out var inverseSun);
        MemoryMarshal.Cast<Vector4, Matrix4x4>(_data.AsSpan(41, 4))[0] = Matrix4x4.Transpose(inverseSun);
        _data[45] = new(
            Vector3.TransformNormal(Vector3.UnitX, inverseSun).Length() * 2 / _sun!.Width,
            Vector3.TransformNormal(Vector3.UnitY, inverseSun).Length() * 2 / _sun.Height,
            Vector3.TransformNormal(Vector3.UnitZ, inverseSun).Length(), FiniteClamp(settings.ShadowSoftness, 1));
        _data[7] = new(Skylight.Min, Skylight.CellSize);
        _data[8] = new(Skylight.Width, Skylight.Height, Skylight.Depth, 0);
        for (var i = 0; i < selected.Length; i++)
        {
            _data[9 + i * 2] = new(selected[i].Position, selected[i].Range);
            _data[10 + i * 2] = new(Vector3.Max(Vector3.Zero, selected[i].Color), selected[i].Intensity);
        }
        _parameters!.Update(MemoryMarshal.AsBytes(_data.AsSpan()));
    }

    public void Bind()
    {
        Graphics.SetTexture(_sun!.DepthTextureHandle, 1);
        Graphics.SetTexture(_atlas!, 2);
        Graphics.SetTexture(Skylight.Texture!, 3);
        Graphics.SetTexture(_parameters!, 4);
        for (var i = 1; i <= 4; i++) Graphics.SetTextureFilter(TextureFilter.Point, i);
    }

    private void RenderPoint(PointCache cache, PointLight3D light, IReadOnlyList<ShadowCaster3D> casters)
    {
        cache.Geometry.Update(casters, light);
        Graphics.BeginPass(cache.Target, Color.White);
        try
        {
            ShadowState();
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2, 1, .025f, light.Range);
            for (var face = 0; face < 6; face++)
            {
                var forward = FaceDirection(face);
                var up = face is 2 or 3 ? Vector3.UnitZ : Vector3.UnitY;
                var vp = Matrix4x4.CreateLookAt(light.Position, light.Position + forward, up) * projection;
                Graphics.SetViewport((face % 3) * ShadowFaceSize, (face / 3) * ShadowFaceSize, ShadowFaceSize, ShadowFaceSize);
                DrawGeometry(cache.Geometry, casters, vp, new(light.Position, light.Range));
            }
        }
        finally { Graphics.EndPass(); }
    }

    private void ShadowState()
    {
        Graphics.ClearScissor(); Graphics.SetLayer(0); Graphics.SetShader(_shadowShader!); Graphics.SetBlendMode(BlendMode.None);
        // Never bind an attachment as one of its own sampled resources.
        for (var i = 0; i < 8; i++) Graphics.SetTexture(nuint.Zero, i);
    }

    private static void DrawShadow(in ShadowCaster3D caster, in Matrix4x4 vp, Vector4 light)
    {
        if (caster.Mesh.Handle == 0 || caster.IndexCount == 0) return;
        Graphics.SetDrawParameters(new ShadowDraw(Matrix4x4.Identity, caster.Transform, light));
        Graphics.SetViewProjection(caster.Transform * vp);
        Graphics.SetMesh(caster.Mesh); Graphics.DrawElements(caster.IndexCount);
    }

    private static void DrawGeometry(ShadowGeometry3D geometry, IReadOnlyList<ShadowCaster3D> casters, Matrix4x4 vp, Vector4 light)
    {
        if (geometry.Count > 0)
            DrawShadow(new(geometry.Mesh, geometry.Count, Matrix4x4.Identity, default, default), vp, light);
        // GPU-only geometry remains supported, but uses a per-caster draw.
        foreach (var caster in casters)
            if (caster.Vertices.IsEmpty || caster.Indices.IsEmpty) DrawShadow(caster, vp, light);
    }

    private static Vector3 FaceDirection(int face) => face switch
    { 0 => Vector3.UnitX, 1 => -Vector3.UnitX, 2 => Vector3.UnitY, 3 => -Vector3.UnitY, 4 => Vector3.UnitZ, _ => -Vector3.UnitZ };

    private static float FiniteClamp(float value, float maximum) => float.IsFinite(value) ? Math.Clamp(value, 0, maximum) : 0;

    private static Matrix4x4 SunProjection(Vector3 min, Vector3 max, Vector3 direction)
    {
        var center = (min + max) * .5f;
        var radius = (max - min).Length() * .5f + 2;
        var up = MathF.Abs(direction.Y) > .98f ? Vector3.UnitZ : Vector3.UnitY;
        var view = Matrix4x4.CreateLookAt(center + direction * (radius + 2), center, up);
        var lo = new Vector3(float.MaxValue); var hi = new Vector3(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var p = Vector3.Transform(new Vector3((i & 1) == 0 ? min.X : max.X,
                (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z), view);
            lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
        }
        return view * Matrix4x4.CreateOrthographicOffCenter(lo.X - 1, hi.X + 1, lo.Y - 1, hi.Y + 1,
            MathF.Max(.1f, -hi.Z - 1), -lo.Z + 1);
    }

    private void EnsureResources()
    {
        _shadowShader ??= Asset.Get<Shader>(AssetType.Shader, "lighting_shadow") ?? throw new InvalidOperationException("Import graphics3d lighting assets first.");
        _copyShader ??= Asset.Get<Shader>(AssetType.Shader, "lighting_shadow_copy") ?? throw new InvalidOperationException("Import graphics3d lighting assets first.");
        _sun ??= RenderTexture.Create(2048, 2048, format: TextureFormat.RGBA8, name: "Sun shadow", depth: true);
        _atlas ??= RenderTexture.Create(FaceWidth * 4, FaceHeight * 4, format: TextureFormat.RGBA8, name: "Point shadow atlas");
        _parameters ??= Texture.Create(_data.Length, 1, TextureFormat.RGBA32F, TextureFilter.Point);
        if (_quad.Handle != 0) return;
        MeshVertex3D[] vertices = [
            new(new(-1,-1,0), Vector3.UnitZ, Vector4.Zero, new(0,1), Vector4.One),
            new(new(1,-1,0), Vector3.UnitZ, Vector4.Zero, new(1,1), Vector4.One),
            new(new(1,1,0), Vector3.UnitZ, Vector4.Zero, new(1,0), Vector4.One),
            new(new(-1,1,0), Vector3.UnitZ, Vector4.Zero, new(0,0), Vector4.One)];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        _quad = Graphics.CreateMesh<MeshVertex3D>(4, 6, BufferUsage.Static, "Shadow atlas copy", MeshIndexFormat.UInt32);
        Graphics.UpdateMesh(_quad, vertices.AsSpan(), indices.AsSpan());
    }

    public void Dispose()
    {
        foreach (var point in _points.Values) point.Dispose();
        _points.Clear(); _sun?.Dispose(); _atlas?.Dispose(); _parameters?.Dispose(); Skylight.Dispose(); _sunGeometry.Dispose();
        if (_quad.Handle != 0) Graphics.DestroyMesh(_quad);
        _quad = default; _sun = _atlas = null; _parameters = null; Invalidate();
    }
}
