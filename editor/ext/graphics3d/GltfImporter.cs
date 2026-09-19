//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;

namespace NoZ.Editor.Graphics3D;

public sealed class ImportedMesh
{
    public required MeshVertex3D[] Vertices { get; init; }
    public required uint[] Indices { get; init; }
    public required MeshPrimitive[] Primitives { get; init; }
    public required Vector3 BoundsMin { get; init; }
    public required Vector3 BoundsMax { get; init; }
}

/// <summary>
/// Imports uncompressed glTF 2.0 geometry without adding a runtime package dependency.
/// </summary>
public static class GltfImporter
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;
    private const int TrianglesMode = 4;

    private const int ComponentByte = 5120;
    private const int ComponentUnsignedByte = 5121;
    private const int ComponentShort = 5122;
    private const int ComponentUnsignedShort = 5123;
    private const int ComponentUnsignedInt = 5125;
    private const int ComponentFloat = 5126;

    /// <summary>Imports mesh-local geometry by unique glTF mesh name. Node transforms
    /// are not applied; exporters must bake the desired local coordinate system.</summary>
    public static Dictionary<string, ImportedMesh> ImportNamedMeshes(string path)
    {
        var (document, _) = ReadDocument(path);
        using (document)
        {
            var result = new Dictionary<string, ImportedMesh>(StringComparer.Ordinal);
            foreach (var mesh in document.RootElement.GetProperty("meshes").EnumerateArray())
            {
                var name = mesh.GetProperty("name").GetString();
                if (string.IsNullOrWhiteSpace(name) || result.ContainsKey(name))
                    throw new InvalidDataException("Named meshes require nonempty, unique names.");
                result.Add(name, Import(path, name));
            }
            return result;
        }
    }

    public static ImportedMesh Import(string path) => Import(path, null);

    private static ImportedMesh Import(string path, string? selectedMesh)
    {
        var (document, glbBuffer) = ReadDocument(path);
        using (document)
        {
            var root = document.RootElement;
            ValidateAsset(root);

            var buffers = LoadBuffers(root, Path.GetDirectoryName(Path.GetFullPath(path))!, glbBuffer);
            var vertices = new List<MeshVertex3D>();
            var indices = new List<uint>();
            var primitives = new List<MeshPrimitive>();

            if (!root.TryGetProperty("meshes", out var meshesElement) ||
                meshesElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("glTF does not contain a meshes array.");

            var meshIndex = 0;
            foreach (var meshElement in meshesElement.EnumerateArray())
            {
                var meshName = GetString(meshElement, "name", $"mesh_{meshIndex}");
                if (selectedMesh != null && meshName != selectedMesh) { meshIndex++; continue; }
                if (!meshElement.TryGetProperty("primitives", out var primitivesElement) ||
                    primitivesElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"Mesh '{meshName}' does not contain primitives.");

                var sourcePrimitiveCount = primitivesElement.GetArrayLength();
                var primitiveIndex = 0;
                foreach (var primitiveElement in primitivesElement.EnumerateArray())
                {
                    ImportPrimitive(
                        root,
                        buffers,
                        primitiveElement,
                        meshName,
                        primitiveIndex,
                        sourcePrimitiveCount,
                        vertices,
                        indices,
                        primitives);
                    primitiveIndex++;
                }

                meshIndex++;
            }

            if (vertices.Count == 0 || primitives.Count == 0)
                throw new InvalidDataException("glTF does not contain any triangle geometry.");

            var boundsMin = vertices[0].Position;
            var boundsMax = vertices[0].Position;
            for (var i = 1; i < vertices.Count; i++)
            {
                boundsMin = Vector3.Min(boundsMin, vertices[i].Position);
                boundsMax = Vector3.Max(boundsMax, vertices[i].Position);
            }

            return new ImportedMesh
            {
                Vertices = [.. vertices],
                Indices = [.. indices],
                Primitives = [.. primitives],
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
            };
        }
    }

    private static void ImportPrimitive(
        JsonElement root,
        byte[][] buffers,
        JsonElement primitiveElement,
        string meshName,
        int primitiveIndex,
        int primitiveCount,
        List<MeshVertex3D> vertices,
        List<uint> indices,
        List<MeshPrimitive> primitives)
    {
        var mode = GetInt32(primitiveElement, "mode", TrianglesMode);
        if (mode != TrianglesMode)
            throw new NotSupportedException(
                $"Mesh '{meshName}' primitive {primitiveIndex} uses glTF mode {mode}; only TRIANGLES (4) is supported.");

        if (primitiveElement.TryGetProperty("extensions", out var extensions) &&
            (extensions.TryGetProperty("KHR_draco_mesh_compression", out _) ||
             extensions.TryGetProperty("EXT_meshopt_compression", out _)))
            throw new NotSupportedException(
                $"Mesh '{meshName}' primitive {primitiveIndex} uses compressed geometry, which is not supported.");

        if (!primitiveElement.TryGetProperty("attributes", out var attributes) ||
            attributes.ValueKind != JsonValueKind.Object ||
            !attributes.TryGetProperty("POSITION", out var positionAccessorElement))
            throw new InvalidDataException($"Mesh '{meshName}' primitive {primitiveIndex} has no POSITION attribute.");

        var positionAccessor = CreateAccessor(root, buffers, positionAccessorElement.GetInt32());
        positionAccessor.RequireComponents(3, "POSITION");

        var normalAccessor = GetOptionalAccessor(root, buffers, attributes, "NORMAL", positionAccessor.Count, 3);
        var tangentAccessor = GetOptionalAccessor(root, buffers, attributes, "TANGENT", positionAccessor.Count, 4);
        var texCoordAccessor = GetOptionalAccessor(root, buffers, attributes, "TEXCOORD_0", positionAccessor.Count, 2);
        var colorAccessor = GetOptionalColorAccessor(root, buffers, attributes, positionAccessor.Count);

        var vertexOffset = vertices.Count;
        for (var i = 0; i < positionAccessor.Count; i++)
        {
            var position = positionAccessor.ReadVector3(i);
            EnsureFinite(position, "POSITION", meshName, primitiveIndex);

            var normal = normalAccessor?.ReadVector3(i) ?? Vector3.Zero;
            var tangent = tangentAccessor?.ReadVector4(i) ?? new Vector4(1, 0, 0, 1);
            var texCoord = texCoordAccessor?.ReadVector2(i) ?? Vector2.Zero;
            var color = colorAccessor?.ReadColor(i) ?? Vector4.One;

            EnsureFinite(normal, "NORMAL", meshName, primitiveIndex);
            EnsureFinite(tangent, "TANGENT", meshName, primitiveIndex);
            EnsureFinite(texCoord, "TEXCOORD_0", meshName, primitiveIndex);
            EnsureFinite(color, "COLOR_0", meshName, primitiveIndex);

            vertices.Add(new MeshVertex3D(position, normal, tangent, texCoord, color));
        }

        var indexOffset = indices.Count;
        if (primitiveElement.TryGetProperty("indices", out var indexAccessorElement))
        {
            var indexAccessor = CreateAccessor(root, buffers, indexAccessorElement.GetInt32());
            indexAccessor.RequireComponents(1, "indices");
            if (indexAccessor.ComponentType is not (ComponentUnsignedByte or ComponentUnsignedShort or ComponentUnsignedInt))
                throw new InvalidDataException("glTF indices must use UNSIGNED_BYTE, UNSIGNED_SHORT, or UNSIGNED_INT.");

            for (var i = 0; i < indexAccessor.Count; i++)
            {
                var index = indexAccessor.ReadIndex(i);
                if (index >= positionAccessor.Count)
                    throw new InvalidDataException(
                        $"Mesh '{meshName}' primitive {primitiveIndex} contains out-of-range index {index}.");
                indices.Add(checked((uint)vertexOffset + index));
            }
        }
        else
        {
            for (uint i = 0; i < positionAccessor.Count; i++)
                indices.Add(checked((uint)vertexOffset + i));
        }

        var indexCount = indices.Count - indexOffset;
        if (indexCount % 3 != 0)
            throw new InvalidDataException(
                $"Mesh '{meshName}' primitive {primitiveIndex} has {indexCount} indices; triangle index counts must be divisible by three.");

        if (normalAccessor == null)
            GenerateNormals(vertices, indices, vertexOffset, positionAccessor.Count, indexOffset, indexCount);

        var primitiveName = primitiveCount == 1 ? meshName : $"{meshName}/primitive_{primitiveIndex}";
        var materialName = GetMaterialName(root, primitiveElement);
        primitives.Add(new MeshPrimitive(
            primitiveName,
            materialName,
            vertexOffset,
            positionAccessor.Count,
            indexOffset,
            indexCount));
    }

    private static void GenerateNormals(
        List<MeshVertex3D> vertices,
        List<uint> indices,
        int vertexOffset,
        int vertexCount,
        int indexOffset,
        int indexCount)
    {
        var normals = new Vector3[vertexCount];
        for (var i = indexOffset; i < indexOffset + indexCount; i += 3)
        {
            var i0 = checked((int)indices[i]);
            var i1 = checked((int)indices[i + 1]);
            var i2 = checked((int)indices[i + 2]);
            var p0 = vertices[i0].Position;
            var p1 = vertices[i1].Position;
            var p2 = vertices[i2].Position;
            var faceNormal = Vector3.Cross(p1 - p0, p2 - p0);
            normals[i0 - vertexOffset] += faceNormal;
            normals[i1 - vertexOffset] += faceNormal;
            normals[i2 - vertexOffset] += faceNormal;
        }

        for (var i = 0; i < vertexCount; i++)
        {
            var normal = normals[i];
            if (normal.LengthSquared() > 1e-20f)
                normal = Vector3.Normalize(normal);
            else
                normal = Vector3.UnitY;
            var vertex = vertices[vertexOffset + i];
            vertices[vertexOffset + i] = new MeshVertex3D(
                vertex.Position,
                normal,
                vertex.Tangent,
                vertex.TexCoord0,
                vertex.Color0);
        }
    }

    private static Accessor? GetOptionalAccessor(
        JsonElement root,
        byte[][] buffers,
        JsonElement attributes,
        string attributeName,
        int expectedCount,
        int expectedComponents)
    {
        if (!attributes.TryGetProperty(attributeName, out var accessorIndex))
            return null;

        var accessor = CreateAccessor(root, buffers, accessorIndex.GetInt32());
        if (accessor.Count != expectedCount)
            throw new InvalidDataException($"{attributeName} count does not match POSITION count.");
        accessor.RequireComponents(expectedComponents, attributeName);
        return accessor;
    }

    private static Accessor? GetOptionalColorAccessor(
        JsonElement root,
        byte[][] buffers,
        JsonElement attributes,
        int expectedCount)
    {
        if (!attributes.TryGetProperty("COLOR_0", out var accessorIndex))
            return null;

        var accessor = CreateAccessor(root, buffers, accessorIndex.GetInt32());
        if (accessor.Count != expectedCount)
            throw new InvalidDataException("COLOR_0 count does not match POSITION count.");
        if (accessor.ComponentCount is not (3 or 4))
            throw new InvalidDataException("COLOR_0 must be VEC3 or VEC4.");
        return accessor;
    }

    private static Accessor CreateAccessor(JsonElement root, byte[][] buffers, int accessorIndex)
    {
        var accessorElement = GetArrayElement(root, "accessors", accessorIndex);
        if (accessorElement.TryGetProperty("sparse", out var sparse) &&
            GetInt32(sparse, "count", 0) > 0)
            throw new NotSupportedException("Sparse glTF accessors are not supported.");

        var componentType = GetRequiredInt32(accessorElement, "componentType");
        var componentSize = GetComponentSize(componentType);
        var componentCount = GetComponentCount(GetRequiredString(accessorElement, "type"));
        var count = GetRequiredInt32(accessorElement, "count");
        if (count < 0)
            throw new InvalidDataException("glTF accessor count cannot be negative.");

        var normalized = GetBoolean(accessorElement, "normalized", false);
        if (!accessorElement.TryGetProperty("bufferView", out var bufferViewIndexElement))
            return Accessor.Zero(componentType, componentCount, count, normalized);

        var bufferViewElement = GetArrayElement(root, "bufferViews", bufferViewIndexElement.GetInt32());
        if (bufferViewElement.TryGetProperty("extensions", out var extensions) &&
            extensions.TryGetProperty("EXT_meshopt_compression", out _))
            throw new NotSupportedException("EXT_meshopt_compression buffer views are not supported.");

        var bufferIndex = GetRequiredInt32(bufferViewElement, "buffer");
        if ((uint)bufferIndex >= (uint)buffers.Length)
            throw new InvalidDataException($"glTF buffer index {bufferIndex} is out of range.");

        var viewOffset = GetInt32(bufferViewElement, "byteOffset", 0);
        var viewLength = GetRequiredInt32(bufferViewElement, "byteLength");
        var accessorOffset = GetInt32(accessorElement, "byteOffset", 0);
        var elementSize = checked(componentSize * componentCount);
        var stride = GetInt32(bufferViewElement, "byteStride", elementSize);
        if (viewOffset < 0 || viewLength < 0 || accessorOffset < 0 || stride < elementSize)
            throw new InvalidDataException("glTF accessor contains an invalid offset, length, or stride.");

        var start = checked(viewOffset + accessorOffset);
        var requiredLength = count == 0 ? 0L : checked((long)(count - 1) * stride + elementSize);
        var end = checked((long)start + requiredLength);
        var viewEnd = checked((long)viewOffset + viewLength);
        if (start < viewOffset || end > viewEnd || end > buffers[bufferIndex].Length)
            throw new InvalidDataException("glTF accessor extends beyond its buffer view.");

        return new Accessor(buffers[bufferIndex], start, stride, componentType, componentCount, count, normalized);
    }

    private static (JsonDocument Document, byte[]? GlbBuffer) ReadDocument(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (!path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            return (JsonDocument.Parse(bytes), null);

        if (bytes.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)) != GlbMagic)
            throw new InvalidDataException("Invalid GLB header.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != 2)
            throw new NotSupportedException("Only glTF/GLB version 2 is supported.");

        var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        if (declaredLength != bytes.Length)
            throw new InvalidDataException("GLB declared length does not match the file length.");

        byte[]? jsonBytes = null;
        byte[]? binBytes = null;
        var offset = 12;
        while (offset < bytes.Length)
        {
            if (offset > bytes.Length - 8)
                throw new InvalidDataException("GLB contains a truncated chunk header.");
            var chunkLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)));
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            offset += 8;
            if (chunkLength < 0 || offset > bytes.Length - chunkLength)
                throw new InvalidDataException("GLB contains a truncated chunk.");

            if (chunkType == JsonChunkType && jsonBytes == null)
                jsonBytes = bytes.AsSpan(offset, chunkLength).ToArray();
            else if (chunkType == BinChunkType && binBytes == null)
                binBytes = bytes.AsSpan(offset, chunkLength).ToArray();
            offset += chunkLength;
        }

        if (jsonBytes == null)
            throw new InvalidDataException("GLB does not contain a JSON chunk.");

        return (JsonDocument.Parse(jsonBytes), binBytes);
    }

    private static void ValidateAsset(JsonElement root)
    {
        if (!root.TryGetProperty("asset", out var asset) ||
            !asset.TryGetProperty("version", out var versionElement))
            throw new InvalidDataException("glTF asset metadata is missing.");

        var version = versionElement.GetString();
        if (version == null || !version.StartsWith("2.", StringComparison.Ordinal))
            throw new NotSupportedException($"Only glTF 2.x is supported (found '{version ?? "unknown"}').");

        if (!root.TryGetProperty("extensionsRequired", out var requiredExtensions) ||
            requiredExtensions.ValueKind != JsonValueKind.Array)
            return;

        foreach (var extension in requiredExtensions.EnumerateArray())
        {
            var name = extension.GetString();
            if (name is "KHR_draco_mesh_compression" or "EXT_meshopt_compression")
                throw new NotSupportedException($"Required glTF extension '{name}' is not supported.");
        }
    }

    private static byte[][] LoadBuffers(JsonElement root, string baseDirectory, byte[]? glbBuffer)
    {
        if (!root.TryGetProperty("buffers", out var buffersElement) ||
            buffersElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("glTF does not contain a buffers array.");

        var buffers = new byte[buffersElement.GetArrayLength()][];
        var index = 0;
        foreach (var bufferElement in buffersElement.EnumerateArray())
        {
            byte[] data;
            if (bufferElement.TryGetProperty("uri", out var uriElement))
            {
                var uri = uriElement.GetString() ?? throw new InvalidDataException("glTF buffer URI is null.");
                data = ReadBufferUri(uri, baseDirectory);
            }
            else if (index == 0 && glbBuffer != null)
            {
                data = glbBuffer;
            }
            else
            {
                throw new InvalidDataException($"glTF buffer {index} has no URI or GLB binary chunk.");
            }

            var declaredLength = GetRequiredInt32(bufferElement, "byteLength");
            if (declaredLength < 0 || data.Length < declaredLength)
                throw new InvalidDataException(
                    $"glTF buffer {index} is {data.Length} bytes but declares {declaredLength} bytes.");
            buffers[index++] = data;
        }

        return buffers;
    }

    private static byte[] ReadBufferUri(string uri, string baseDirectory)
    {
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = uri.IndexOf(',');
            if (comma < 0 || !uri[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Only base64-encoded glTF data URIs are supported.");
            try
            {
                return Convert.FromBase64String(uri[(comma + 1)..]);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("glTF buffer contains an invalid base64 data URI.", ex);
            }
        }

        if (Uri.TryCreate(uri, UriKind.Absolute, out var absoluteUri))
        {
            if (!absoluteUri.IsFile)
                throw new NotSupportedException($"Remote glTF buffer URI '{uri}' is not supported.");
            return File.ReadAllBytes(absoluteUri.LocalPath);
        }

        var relativePath = Uri.UnescapeDataString(uri).Replace('/', Path.DirectorySeparatorChar);
        return File.ReadAllBytes(Path.GetFullPath(Path.Combine(baseDirectory, relativePath)));
    }

    private static string GetMaterialName(JsonElement root, JsonElement primitiveElement)
    {
        if (!primitiveElement.TryGetProperty("material", out var materialIndexElement))
            return string.Empty;

        var materialIndex = materialIndexElement.GetInt32();
        var material = GetArrayElement(root, "materials", materialIndex);
        return GetString(material, "name", $"material_{materialIndex}");
    }

    private static JsonElement GetArrayElement(JsonElement root, string propertyName, int index)
    {
        if (index < 0 || !root.TryGetProperty(propertyName, out var array) ||
            array.ValueKind != JsonValueKind.Array || index >= array.GetArrayLength())
            throw new InvalidDataException($"glTF {propertyName} index {index} is out of range.");
        return array[index];
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"glTF property '{propertyName}' is missing or invalid.");
        return value.GetString()!;
    }

    private static int GetRequiredInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidDataException($"glTF property '{propertyName}' is missing or invalid.");
        return result;
    }

    private static int GetInt32(JsonElement element, string propertyName, int defaultValue) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : defaultValue;

    private static bool GetBoolean(JsonElement element, string propertyName, bool defaultValue) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;

    private static string GetString(JsonElement element, string propertyName, string defaultValue) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? defaultValue
            : defaultValue;

    private static int GetComponentCount(string accessorType) => accessorType switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        _ => throw new NotSupportedException($"glTF accessor type '{accessorType}' is not supported for mesh data."),
    };

    private static int GetComponentSize(int componentType) => componentType switch
    {
        ComponentByte or ComponentUnsignedByte => 1,
        ComponentShort or ComponentUnsignedShort => 2,
        ComponentUnsignedInt or ComponentFloat => 4,
        _ => throw new NotSupportedException($"glTF component type {componentType} is not supported."),
    };

    private static void EnsureFinite(Vector2 value, string attribute, string meshName, int primitiveIndex)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            throw NonFiniteAttribute(attribute, meshName, primitiveIndex);
    }

    private static void EnsureFinite(Vector3 value, string attribute, string meshName, int primitiveIndex)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw NonFiniteAttribute(attribute, meshName, primitiveIndex);
    }

    private static void EnsureFinite(Vector4 value, string attribute, string meshName, int primitiveIndex)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) || !float.IsFinite(value.W))
            throw NonFiniteAttribute(attribute, meshName, primitiveIndex);
    }

    private static InvalidDataException NonFiniteAttribute(string attribute, string meshName, int primitiveIndex) =>
        new($"Mesh '{meshName}' primitive {primitiveIndex} contains a non-finite {attribute} value.");

    private sealed class Accessor
    {
        private readonly byte[]? _data;
        private readonly int _start;
        private readonly int _stride;
        private readonly bool _normalized;

        public int ComponentType { get; }
        public int ComponentCount { get; }
        public int Count { get; }

        public Accessor(
            byte[]? data,
            int start,
            int stride,
            int componentType,
            int componentCount,
            int count,
            bool normalized)
        {
            _data = data;
            _start = start;
            _stride = stride;
            ComponentType = componentType;
            ComponentCount = componentCount;
            Count = count;
            _normalized = normalized;
        }

        public static Accessor Zero(int componentType, int componentCount, int count, bool normalized) =>
            new(null, 0, 0, componentType, componentCount, count, normalized);

        public void RequireComponents(int expected, string semantic)
        {
            if (ComponentCount != expected)
                throw new InvalidDataException($"glTF {semantic} accessor must have {expected} components.");
        }

        public Vector2 ReadVector2(int index) => new(ReadFloat(index, 0), ReadFloat(index, 1));

        public Vector3 ReadVector3(int index) =>
            new(ReadFloat(index, 0), ReadFloat(index, 1), ReadFloat(index, 2));

        public Vector4 ReadVector4(int index) =>
            new(ReadFloat(index, 0), ReadFloat(index, 1), ReadFloat(index, 2), ReadFloat(index, 3));

        public Vector4 ReadColor(int index) => ComponentCount == 3
            ? new Vector4(ReadFloat(index, 0), ReadFloat(index, 1), ReadFloat(index, 2), 1f)
            : ReadVector4(index);

        public uint ReadIndex(int index)
        {
            ValidateIndex(index);
            if (_data == null)
                return 0;
            var offset = checked(_start + index * _stride);
            return ComponentType switch
            {
                ComponentUnsignedByte => _data[offset],
                ComponentUnsignedShort => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(offset, 2)),
                ComponentUnsignedInt => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset, 4)),
                _ => throw new InvalidDataException($"Component type {ComponentType} cannot be used for indices."),
            };
        }

        private float ReadFloat(int index, int component)
        {
            ValidateIndex(index);
            if ((uint)component >= (uint)ComponentCount)
                throw new ArgumentOutOfRangeException(nameof(component));
            if (_data == null)
                return 0;

            var offset = checked(_start + index * _stride + component * GetComponentSize(ComponentType));
            return ComponentType switch
            {
                ComponentFloat => BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(offset, 4))),
                ComponentByte => Normalize(unchecked((sbyte)_data[offset])),
                ComponentUnsignedByte => Normalize(_data[offset]),
                ComponentShort => Normalize(BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(offset, 2))),
                ComponentUnsignedShort => Normalize(BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(offset, 2))),
                ComponentUnsignedInt => Normalize(BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(offset, 4))),
                _ => throw new NotSupportedException($"glTF component type {ComponentType} is not supported."),
            };
        }

        private float Normalize(sbyte value) => _normalized ? MathF.Max(value / 127f, -1f) : value;
        private float Normalize(byte value) => _normalized ? value / 255f : value;
        private float Normalize(short value) => _normalized ? MathF.Max(value / 32767f, -1f) : value;
        private float Normalize(ushort value) => _normalized ? value / 65535f : value;
        private float Normalize(uint value) => _normalized ? value / (float)uint.MaxValue : value;

        private void ValidateIndex(int index)
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
