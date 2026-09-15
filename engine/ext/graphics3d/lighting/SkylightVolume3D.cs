//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

/// <summary>
/// Conservative surface voxels plus attenuated sky visibility, not bounced GI.
/// CPU data is rebuilt only when geometry changes; rendering uses a packed R8
/// texture, so the optional extension does not require new core 3D texture APIs.
/// </summary>
public sealed class SkylightVolume3D : IDisposable
{
    private const int TextureWidth = 1024, MaxCells = 2_097_152;
    private long _revision = long.MinValue;
    private Vector3 _boundsMin, _boundsMax;
    private byte[] _solid = [], _sky = [], _packed = [];
    private int[] _queue = [];
    public Vector3 Min { get; private set; }
    public float CellSize { get; private set; } = .5f;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Depth { get; private set; }
    public Texture? Texture { get; private set; }
    public int BuildCount { get; private set; }
    public double LastBuildMilliseconds { get; private set; }
    public void Invalidate() => _revision = long.MinValue;

    public void Prepare(Vector3 min, Vector3 max, long revision, Action<SkylightVolume3D> voxelize)
    {
        var boundsSize = max - min;
        if (!float.IsFinite(min.LengthSquared()) || !float.IsFinite(max.LengthSquared()) ||
            !float.IsFinite(boundsSize.LengthSquared()) || boundsSize.X <= 0 || boundsSize.Y <= 0 || boundsSize.Z <= 0)
            throw new ArgumentException("Skylight bounds must be finite and have positive size.");
        if (_revision == revision && _boundsMin == min && _boundsMax == max && Texture != null) return;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var size = Vector3.Max(boundsSize, Vector3.One);
        CellSize = .5f;
        // Keep dimension conversion bounded even for unusually large worlds.
        while (MathF.Max(size.X, MathF.Max(size.Y, size.Z)) / CellSize > MaxCells - 2) CellSize *= 2;
        while (true)
        {
            Width = (int)MathF.Ceiling(size.X / CellSize) + 2;
            Height = (int)MathF.Ceiling(size.Y / CellSize) + 2;
            Depth = (int)MathF.Ceiling(size.Z / CellSize) + 2;
            if ((double)Width * Height * Depth <= MaxCells) break;
            CellSize *= 2;
        }
        Min = min - new Vector3(CellSize);
        var count = Width * Height * Depth;
        var texHeight = (count + TextureWidth - 1) / TextureWidth;
        if (_solid.Length != count)
        {
            _solid = new byte[count]; _queue = new int[count];
            _sky = new byte[TextureWidth * texHeight];
            _packed = new byte[_sky.Length];
        }
        else { Array.Clear(_solid); Array.Clear(_sky); }
        voxelize(this);
        var tail = 0;
        // Direct sky has no distance attenuation. A solid roof terminates a
        // column; sideways sky entering doors/windows attenuates over ~4m.
        for (var z = 0; z < Depth; z++)
        for (var x = 0; x < Width; x++)
        for (var y = Height - 1; y >= 0; y--)
        {
            var i = Index(x, y, z);
            if (_solid[i] != 0) break;
            _sky[i] = 255; _queue[tail++] = i;
        }
        // All sources have equal energy and all edges have equal cost: BFS
        // visits each cell once, rather than repeatedly relaxing a flood fill.
        for (var head = 0; head < tail; head++)
        {
            var i = _queue[head]; var value = _sky[i] - 32;
            if (value <= 0) continue;
            var x = i % Width; var y = i / Width % Height; var z = i / (Width * Height);
            if (x > 0) Visit(i - 1); if (x + 1 < Width) Visit(i + 1);
            if (y > 0) Visit(i - Width); if (y + 1 < Height) Visit(i + Width);
            if (z > 0) Visit(i - Width * Height); if (z + 1 < Depth) Visit(i + Width * Height);
            void Visit(int j)
            {
                if (_solid[j] != 0 || _sky[j] != 0) return;
                _sky[j] = (byte)value; _queue[tail++] = j;
            }
        }
        // Zero is a solid/unavailable sample, NOT dark air. Reserve 1..255 for
        // air visibility so the shader can ignore solids during interpolation.
        // Blending solid zeroes into surface lighting draws voxel-height bands
        // across otherwise open terrain. Dark interior air must remain valid.
        for (var i = 0; i < count; i++)
            _packed[i] = _solid[i] != 0 ? (byte)0 : (byte)(1 + (_sky[i] * 254 + 127) / 255);
        if (Texture == null || Texture.Height != texHeight)
        {
            Texture?.Dispose();
            Texture = NoZ.Texture.Create(TextureWidth, texHeight, _packed, TextureFormat.R8, TextureFilter.Point);
        }
        else Texture.Update(_packed);
        _revision = revision; _boundsMin = min; _boundsMax = max; BuildCount++;
        LastBuildMilliseconds = timer.Elapsed.TotalMilliseconds;
    }

    public void AddHeightField(Func<float, float, float> heightAt)
    {
        for (var z = 0; z < Depth; z++)
        for (var x = 0; x < Width; x++)
        {
            var top = (int)MathF.Floor((heightAt(Min.X + (x + .5f) * CellSize,
                Min.Z + (z + .5f) * CellSize) - Min.Y) / CellSize);
            for (var y = 0; y <= Math.Min(top, Height - 1); y++) _solid[Index(x, y, z)] = 1;
        }
    }

    public void AddMesh(Mesh mesh, Matrix4x4 transform)
    {
        var vertices = mesh.Vertices; var indices = mesh.Indices;
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            if (vertices[indices[i]].Color0.W < .5f && vertices[indices[i + 1]].Color0.W < .5f && vertices[indices[i + 2]].Color0.W < .5f) continue;
            AddTriangle(Vector3.Transform(vertices[indices[i]].Position, transform),
                Vector3.Transform(vertices[indices[i + 1]].Position, transform),
                Vector3.Transform(vertices[indices[i + 2]].Position, transform));
        }
    }

    public void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        a = (a - Min) / CellSize; b = (b - Min) / CellSize; c = (c - Min) / CellSize;
        var lo = Vector3.Max(Floor(Vector3.Min(a, Vector3.Min(b, c)) - new Vector3(.001f)), Vector3.Zero);
        var hi = Vector3.Min(Floor(Vector3.Max(a, Vector3.Max(b, c)) + new Vector3(.001f)), new(Width - 1, Height - 1, Depth - 1));
        for (var z = (int)lo.Z; z <= (int)hi.Z; z++)
        for (var y = (int)lo.Y; y <= (int)hi.Y; y++)
        for (var x = (int)lo.X; x <= (int)hi.X; x++)
        {
            var i = Index(x, y, z);
            if (_solid[i] != 0) continue;
            var center = new Vector3(x + .5f, y + .5f, z + .5f);
            if (Overlaps(a - center, b - center, c - center)) _solid[i] = 1;
        }
    }

    // Triangle/box separating axes retain thin and diagonal surfaces without
    // filling a prefab's AABB (which would incorrectly seal open door frames).
    private static bool Overlaps(Vector3 a, Vector3 b, Vector3 c)
    {
        var e0 = b - a; var e1 = c - b; var e2 = a - c;
        if (Separated(Vector3.Cross(e0, e1), a, b, c)) return false;
        for (var axis = 0; axis < 3; axis++)
        {
            var v = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
            if (Separated(v, a, b, c) || Separated(Vector3.Cross(e0, v), a, b, c) ||
                Separated(Vector3.Cross(e1, v), a, b, c) || Separated(Vector3.Cross(e2, v), a, b, c)) return false;
        }
        return true;
    }

    private static bool Separated(Vector3 axis, Vector3 a, Vector3 b, Vector3 c)
    {
        var radius = .501f * (MathF.Abs(axis.X) + MathF.Abs(axis.Y) + MathF.Abs(axis.Z));
        var pa = Vector3.Dot(a, axis); var pb = Vector3.Dot(b, axis); var pc = Vector3.Dot(c, axis);
        return MathF.Min(pa, MathF.Min(pb, pc)) > radius || MathF.Max(pa, MathF.Max(pb, pc)) < -radius;
    }
    private int Index(int x, int y, int z) => x + Width * (y + Height * z);
    private static Vector3 Floor(Vector3 p) => new(MathF.Floor(p.X), MathF.Floor(p.Y), MathF.Floor(p.Z));
    public void Dispose() { Texture?.Dispose(); Texture = null; Invalidate(); }
}
