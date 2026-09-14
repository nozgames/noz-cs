//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using NoZ;
using NoZ.Platform;

namespace NoZ;

/// <summary>
/// A vertex imported from a glTF mesh.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct MeshVertex3D : IVertex
{
    public readonly Vector3 Position;
    public readonly Vector3 Normal;
    public readonly Vector4 Tangent;
    public readonly Vector2 TexCoord0;
    public readonly Vector4 Color0;

    public MeshVertex3D(
        Vector3 position,
        Vector3 normal,
        Vector4 tangent,
        Vector2 texCoord0,
        Vector4 color0)
    {
        Position = position;
        Normal = normal;
        Tangent = tangent;
        TexCoord0 = texCoord0;
        Color0 = color0;
    }

    public static VertexFormatDescriptor GetFormatDescriptor() => new()
    {
        Stride = Marshal.SizeOf<MeshVertex3D>(),
        Attributes =
        [
            new VertexAttribute(0, 3, VertexAttribType.Float, (int)Marshal.OffsetOf<MeshVertex3D>(nameof(Position))),
            new VertexAttribute(1, 3, VertexAttribType.Float, (int)Marshal.OffsetOf<MeshVertex3D>(nameof(Normal))),
            new VertexAttribute(2, 4, VertexAttribType.Float, (int)Marshal.OffsetOf<MeshVertex3D>(nameof(Tangent))),
            new VertexAttribute(3, 2, VertexAttribType.Float, (int)Marshal.OffsetOf<MeshVertex3D>(nameof(TexCoord0))),
            new VertexAttribute(4, 4, VertexAttribType.Float, (int)Marshal.OffsetOf<MeshVertex3D>(nameof(Color0))),
        ]
    };
}

/// <summary>
/// A contiguous indexed primitive within a <see cref="Mesh"/>.
/// </summary>
public readonly record struct MeshPrimitive(
    string Name,
    string Material,
    int VertexOffset,
    int VertexCount,
    int IndexOffset,
    int IndexCount);

/// <summary>
/// Runtime mesh data produced by the graphics3d editor extension's glTF importer.
/// </summary>
public sealed class Mesh : Asset
{
    public static readonly AssetType Type = AssetType.FromString("MESH");
    public const ushort Version = 1;

    private const int MaxElementCount = 100_000_000;
    private RenderMesh _renderMesh;

    public MeshVertex3D[] Vertices { get; private set; } = [];
    public uint[] Indices { get; private set; } = [];
    public MeshPrimitive[] Primitives { get; private set; } = [];
    public Vector3 BoundsMin { get; private set; }
    public Vector3 BoundsMax { get; private set; }
    public Vector3 BoundsCenter => (BoundsMin + BoundsMax) * 0.5f;
    public Vector3 BoundsSize => BoundsMax - BoundsMin;
    public RenderMesh RenderMesh => _renderMesh;

    public Mesh() : base(Type)
    {
    }

    private Mesh(string name) : base(Type, name)
    {
    }

    /// <summary>
    /// Registers this optional asset type with NoZ. Safe to call more than once.
    /// </summary>
    public static void RegisterDef()
    {
        var existing = Asset.GetDef(Type);
        if (existing != null)
        {
            if (existing.RuntimeType != typeof(Mesh))
                throw new InvalidOperationException($"Asset type {Type} is already registered by {existing.RuntimeType.FullName}.");
            return;
        }

        RegisterDef(new AssetDef(Type, "Mesh", typeof(Mesh), LoadAsset, Version));
    }

    protected override void Load(BinaryReader reader)
    {
        BoundsMin = ReadVector3(reader);
        BoundsMax = ReadVector3(reader);

        var vertexCount = ReadCount(reader, "vertex");
        var vertices = new MeshVertex3D[vertexCount];
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = new MeshVertex3D(
                ReadVector3(reader),
                ReadVector3(reader),
                ReadVector4(reader),
                ReadVector2(reader),
                ReadVector4(reader));
        }

        var indexCount = ReadCount(reader, "index");
        var indices = new uint[indexCount];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = reader.ReadUInt32();
            if (indices[i] >= vertexCount)
                throw new InvalidDataException($"Mesh index {indices[i]} is outside the vertex array.");
        }

        var primitiveCount = ReadCount(reader, "primitive");
        var primitives = new MeshPrimitive[primitiveCount];
        for (var i = 0; i < primitives.Length; i++)
        {
            var primitive = new MeshPrimitive(
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32());

            ValidatePrimitive(primitive, vertexCount, indexCount);
            primitives[i] = primitive;
        }

        Vertices = vertices;
        Indices = indices;
        Primitives = primitives;
        Upload();
    }

    private void Upload()
    {
        if (_renderMesh.Handle != nuint.Zero)
            Graphics.DestroyMesh(_renderMesh);

        if (Vertices.Length == 0 || Indices.Length == 0)
        {
            _renderMesh = default;
            Native = nuint.Zero;
            return;
        }

        _renderMesh = Graphics.CreateMesh<MeshVertex3D>(
            Vertices.Length,
            Indices.Length,
            BufferUsage.Static,
            Name,
            MeshIndexFormat.UInt32);
        Graphics.UpdateMesh(_renderMesh, Vertices.AsSpan(), Indices.AsSpan());
        Native = _renderMesh.Handle;
    }

    /// <summary>
    /// Writes the versioned binary representation consumed by <see cref="Load(BinaryReader)"/>.
    /// </summary>
    public static void Write(
        Stream stream,
        IReadOnlyList<MeshVertex3D> vertices,
        IReadOnlyList<uint> indices,
        IReadOnlyList<MeshPrimitive> primitives,
        Vector3 boundsMin,
        Vector3 boundsMax)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(primitives);

        if (vertices.Count > MaxElementCount || indices.Count > MaxElementCount || primitives.Count > MaxElementCount)
            throw new ArgumentOutOfRangeException(nameof(vertices), "Mesh exceeds the supported element count.");

        foreach (var index in indices)
            if (index >= vertices.Count)
                throw new InvalidDataException($"Mesh index {index} is outside the vertex array.");

        foreach (var primitive in primitives)
            ValidatePrimitive(primitive, vertices.Count, indices.Count);

        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.WriteAssetHeader(Type, Version);
        Write(writer, boundsMin);
        Write(writer, boundsMax);

        writer.Write(vertices.Count);
        foreach (var vertex in vertices)
        {
            Write(writer, vertex.Position);
            Write(writer, vertex.Normal);
            Write(writer, vertex.Tangent);
            Write(writer, vertex.TexCoord0);
            Write(writer, vertex.Color0);
        }

        writer.Write(indices.Count);
        foreach (var index in indices)
            writer.Write(index);

        writer.Write(primitives.Count);
        foreach (var primitive in primitives)
        {
            writer.Write(primitive.Name ?? string.Empty);
            writer.Write(primitive.Material ?? string.Empty);
            writer.Write(primitive.VertexOffset);
            writer.Write(primitive.VertexCount);
            writer.Write(primitive.IndexOffset);
            writer.Write(primitive.IndexCount);
        }
    }

    private static Asset LoadAsset(Stream stream, string name)
    {
        var mesh = new Mesh(name);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        mesh.Load(reader);
        return mesh;
    }

    private static int ReadCount(BinaryReader reader, string elementName)
    {
        var count = reader.ReadInt32();
        if (count is < 0 or > MaxElementCount)
            throw new InvalidDataException($"Invalid {elementName} count: {count}.");
        return count;
    }

    private static void ValidatePrimitive(MeshPrimitive primitive, int vertexCount, int indexCount)
    {
        if (primitive.VertexOffset < 0 || primitive.VertexCount < 0 ||
            primitive.VertexOffset > vertexCount - primitive.VertexCount)
            throw new InvalidDataException($"Primitive '{primitive.Name}' has an invalid vertex range.");

        if (primitive.IndexOffset < 0 || primitive.IndexCount < 0 ||
            primitive.IndexOffset > indexCount - primitive.IndexCount)
            throw new InvalidDataException($"Primitive '{primitive.Name}' has an invalid index range.");
    }

    private static Vector2 ReadVector2(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle());

    private static Vector3 ReadVector3(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static Vector4 ReadVector4(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static void Write(BinaryWriter writer, Vector2 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
    }

    private static void Write(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void Write(BinaryWriter writer, Vector4 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
        writer.Write(value.W);
    }

    public override void Dispose()
    {
        if (_renderMesh.Handle != nuint.Zero)
        {
            Graphics.DestroyMesh(_renderMesh);
            _renderMesh = default;
            Native = nuint.Zero;
        }

        base.Dispose();
    }
}
