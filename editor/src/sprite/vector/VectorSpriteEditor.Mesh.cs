//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using Clipper2Lib;
using LibTessDotNet;

namespace NoZ.Editor;

public partial class VectorSpriteEditor
{
    private const int MaxMeshVertices = 1024 * 16;
    private const int MaxMeshIndices = MaxMeshVertices * 3;

    private struct MeshSlotData
    {
        public int VertexOffset;
        public int VertexCount;
        public int IndexOffset;
        public int IndexCount;
        public Color FillColor;
    }

    private readonly MeshVertex[] _meshVertices = new MeshVertex[MaxMeshVertices];
    private readonly ushort[] _meshIndices = new ushort[MaxMeshIndices];
    private readonly List<MeshSlotData> _meshSlots = new();
    private readonly List<LayerPathResult> _tessellateResults = new();
    private int _meshFrame = -1;
    private int _meshVersion;
    private int _meshBuildVersion = -1;

    private bool TessellateClipper(PathsD paths, ref int vertexOffset, ref int indexOffset, Color color)
    {
        return TessellateClipperTo(paths, ref vertexOffset, ref indexOffset, color,
            _meshVertices, _meshIndices, _meshSlots);
    }

    private bool TessellateClipperTo(PathsD paths, ref int vertexOffset, ref int indexOffset, Color color,
        MeshVertex[] vertices, ushort[] indices, List<MeshSlotData> slots)
    {
        var tess = new Tess();
        foreach (var path in paths)
        {
            if (path.Count < 3) continue;
            var verts = new ContourVertex[path.Count];
            for (int j = 0; j < path.Count; j++)
                verts[j].Position = new Vec3((float)path[j].x, (float)path[j].y, 0);
            tess.AddContour(verts);
        }

        tess.Tessellate(WindingRule.NonZero, LibTessDotNet.ElementType.Polygons, 3);
        return EmitTessellationTo(tess, ref vertexOffset, ref indexOffset, color,
            vertices, indices, slots);
    }

    private bool EmitTessellationTo(Tess tess, ref int vertexOffset, ref int indexOffset, Color color,
        MeshVertex[] vertices, ushort[] indices, List<MeshSlotData> slots)
    {
        if (tess.ElementCount == 0) return false;

        var vertCount = tess.VertexCount;
        var idxCount = tess.ElementCount * 3;

        if (vertexOffset + vertCount > MaxMeshVertices ||
            indexOffset + idxCount > MaxMeshIndices)
            return false;

        for (int v = 0; v < vertCount; v++)
        {
            ref var tv = ref tess.Vertices[v];
            vertices[vertexOffset + v] = new MeshVertex(
                tv.Position.X, tv.Position.Y, 0, 0, Color.White);
        }

        for (int e = 0; e < tess.ElementCount; e++)
        {
            indices[indexOffset + e * 3 + 0] = (ushort)tess.Elements[e * 3 + 0];
            indices[indexOffset + e * 3 + 1] = (ushort)tess.Elements[e * 3 + 1];
            indices[indexOffset + e * 3 + 2] = (ushort)tess.Elements[e * 3 + 2];
        }

        slots.Add(new MeshSlotData
        {
            VertexOffset = vertexOffset,
            VertexCount = vertCount,
            IndexOffset = indexOffset,
            IndexCount = idxCount,
            FillColor = color,
        });

        vertexOffset += vertCount;
        indexOffset += idxCount;
        return true;
    }

    private void DrawMesh() => DrawMesh(Document.Transform);

    private void DrawMesh(Matrix3x2 transform)
    {
        if (_meshSlots.Count == 0) return;

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(3);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetTransform(transform);
            Graphics.SetTexture(Graphics.WhiteTexture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);

            foreach (var slot in _meshSlots)
            {
                Graphics.SetColor(slot.FillColor.WithAlpha(
                    slot.FillColor.A * Workspace.XrayAlpha));
                Graphics.Draw(
                    _meshVertices.AsSpan(slot.VertexOffset, slot.VertexCount),
                    _meshIndices.AsSpan(slot.IndexOffset, slot.IndexCount));
            }
        }
    }

    private void DrawTiling()
    {
        var bounds = Document.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        for (var dy = -2; dy <= 2; dy++)
        for (var dx = -2; dx <= 2; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var offset = new Vector2(dx * bounds.Width, dy * bounds.Height);
            var tileTransform = Matrix3x2.CreateTranslation(offset) * Document.Transform;

            if (PreviewRasterize)
                DrawPreviewQuad(tileTransform);
            else
                DrawMesh(tileTransform);
        }
    }

    // Layer-scoped mesh update: tessellates paths per-layer with booleans scoped to each layer
    private void UpdateMeshFromLayers()
    {
        if (_meshBuildVersion == _meshVersion && _meshFrame == CurrentFrameIndex) return;

        // Frame-only rebuilds don't bump the version via MarkDirty, so bump it here
        // to invalidate downstream caches (preview RT, onion skin) that watch _meshVersion.
        if (_meshBuildVersion == _meshVersion)
            _meshVersion++;

        _meshBuildVersion = _meshVersion;
        _meshFrame = CurrentFrameIndex;
        _meshSlots.Clear();

        var vertexOffset = 0;
        var indexOffset = 0;

        TessellateLayer(ActiveRoot, ref vertexOffset, ref indexOffset);
    }

    private void TessellateLayer(SpriteGroup layer, ref int vertexOffset, ref int indexOffset)
    {
        _tessellateResults.Clear();
        SpriteGroupProcessor.ProcessLayer(layer, _tessellateResults);
        if (Document.TryBuildOutlineResult(_tessellateResults, out var outline))
            _tessellateResults.Insert(0, outline);
        foreach (var result in _tessellateResults)
            TessellateClipper(result.Contours, ref vertexOffset, ref indexOffset, result.Color.ToColor());
    }

    private void DrawColoredMesh(int sortGroup)
    {
        if (_meshSlots.Count == 0) return;

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(sortGroup);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetTransform(Document.Transform);
            Graphics.SetTexture(Graphics.WhiteTexture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);

            foreach (var slot in _meshSlots)
            {
                Graphics.SetColor(slot.FillColor);
                Graphics.Draw(
                    _meshVertices.AsSpan(slot.VertexOffset, slot.VertexCount),
                    _meshIndices.AsSpan(slot.IndexOffset, slot.IndexCount));
            }
        }
    }
}
