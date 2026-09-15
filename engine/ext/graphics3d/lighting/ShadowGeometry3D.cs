//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

/// <summary>One cached world-space draw per shadow face, not one per prefab.</summary>
internal sealed class ShadowGeometry3D : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Vertex(Vector3 Position, Vector4 Color) : IVertex
    {
        public static VertexFormatDescriptor GetFormatDescriptor() => new()
        {
            Stride = 28,
            Attributes = [new VertexAttribute(0, 3, VertexAttribType.Float, 0), new VertexAttribute(4, 4, VertexAttribType.Float, 12)]
        };
    }
    private readonly List<Vertex> _vertices = [];
    private uint[] _indices = [];
    private int _capacity;
    public RenderMesh Mesh { get; private set; }
    public int Count => _vertices.Count;

    public void Update(IReadOnlyList<ShadowCaster3D> casters, PointLight3D? light = null)
    {
        _vertices.Clear();
        foreach (var caster in casters)
        {
            var vertices = caster.Vertices.Span; var indices = caster.Indices.Span;
            if (vertices.IsEmpty || indices.IsEmpty) continue;
            var count = Math.Min(caster.IndexCount, indices.Length);
            for (var i = 0; i + 2 < count; i += 3)
            {
                var va = vertices[(int)indices[i]]; var vb = vertices[(int)indices[i + 1]]; var vc = vertices[(int)indices[i + 2]];
                if (va.Color0.W < .5f && vb.Color0.W < .5f && vc.Color0.W < .5f) continue;
                var a = Vector3.Transform(va.Position, caster.Transform);
                var b = Vector3.Transform(vb.Position, caster.Transform);
                var c = Vector3.Transform(vc.Position, caster.Transform);
                if (light is { } l)
                {
                    var min = Vector3.Min(a, Vector3.Min(b, c)); var max = Vector3.Max(a, Vector3.Max(b, c));
                    if (Vector3.DistanceSquared(l.Position, Vector3.Clamp(l.Position, min, max)) >= l.Range * l.Range) continue;
                }
                _vertices.Add(new(a, va.Color0)); _vertices.Add(new(b, vb.Color0)); _vertices.Add(new(c, vc.Color0));
            }
        }
        if (_vertices.Count == 0) return;
        if (_capacity < _vertices.Count)
        {
            if (Mesh.Handle != 0) Graphics.DestroyMesh(Mesh);
            _capacity = Math.Max(_vertices.Count, _capacity * 2);
            _indices = Enumerable.Range(0, _capacity).Select(i => (uint)i).ToArray();
            Mesh = Graphics.CreateMesh<Vertex>(_capacity, _capacity, BufferUsage.Dynamic, "Cached shadow geometry", MeshIndexFormat.UInt32);
        }
        Graphics.UpdateMesh(Mesh, CollectionsMarshal.AsSpan(_vertices), _indices.AsSpan(0, _vertices.Count));
    }
    public void Dispose() { if (Mesh.Handle != 0) Graphics.DestroyMesh(Mesh); Mesh = default; _capacity = 0; _vertices.Clear(); }
}
