//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Mesh-based preview: tessellates vector shapes into flat-colored
//  triangle meshes for real-time editing feedback.
//

using Clipper2Lib;
using LibTessDotNet;

namespace NoZ.Editor;

public partial class SpriteEditor
{
    private const int MaxMeshVertices = 1024 * 16;
    private const int MaxMeshIndices = MaxMeshVertices * 3;

    private readonly MeshVertex[] _meshVertices = new MeshVertex[MaxMeshVertices];
    private readonly ushort[] _meshIndices = new ushort[MaxMeshIndices];
    private int _meshVersion = -1;

    private struct MeshSlotData
    {
        public int VertexOffset;
        public int VertexCount;
        public int IndexOffset;
        public int IndexCount;
        public Color FillColor;
    }

    private readonly List<MeshSlotData> _meshSlots = new();

    private int _meshFrame = -1;


    private bool TessellateClipper(PathsD paths, ref int vertexOffset, ref int indexOffset, Color color)
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
        return EmitTessellation(tess, ref vertexOffset, ref indexOffset, color);
    }

    private bool EmitTessellation(Tess tess, ref int vertexOffset, ref int indexOffset, Color color)
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
            _meshVertices[vertexOffset + v] = new MeshVertex(
                tv.Position.X, tv.Position.Y, 0, 0, Color.White);
        }

        for (int e = 0; e < tess.ElementCount; e++)
        {
            _meshIndices[indexOffset + e * 3 + 0] = (ushort)tess.Elements[e * 3 + 0];
            _meshIndices[indexOffset + e * 3 + 1] = (ushort)tess.Elements[e * 3 + 1];
            _meshIndices[indexOffset + e * 3 + 2] = (ushort)tess.Elements[e * 3 + 2];
        }

        _meshSlots.Add(new MeshSlotData
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

    private void DrawMesh()
    {
        if (_meshSlots.Count == 0) return;

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(3);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetTransform(Document.Transform);
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

    // Layer-scoped mesh update: tessellates paths per-layer with booleans scoped to each layer
    private void UpdateMeshFromLayers()
    {
        if (_meshVersion == Document.Version && _meshFrame == CurrentFrameIndex) return;

        _meshVersion = Document.Version;
        _meshFrame = CurrentFrameIndex;
        _meshSlots.Clear();

        var vertexOffset = 0;
        var indexOffset = 0;

        TessellateLayer(Document.RootLayer, ref vertexOffset, ref indexOffset);
    }

    private void TessellateLayer(SpriteLayer layer, ref int vertexOffset, ref int indexOffset)
    {
        var vo = vertexOffset;
        var io = indexOffset;
        SpriteLayerProcessor.ProcessLayer(layer, result =>
        {
            TessellateClipper(result.Contours, ref vo, ref io, result.Color.ToColor());
        });
        vertexOffset = vo;
        indexOffset = io;
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

    private void DrawGeneratedImage(int sortGroup, float alpha)
    {
        var texture = Document.Generation?.Job.Texture;
        if (texture == null) return;

        var cs = Document.ConstrainedSize ?? new Vector2Int(256, 256);
        var ppu = EditorApplication.Config.PixelsPerUnitInv;

        var rect = new Rect(
            cs.X * ppu * -0.5f,
            cs.Y * ppu * -0.5f,
            cs.X * ppu,
            cs.Y * ppu);

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(sortGroup);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetTransform(Document.Transform);
            Graphics.SetTexture(texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetColor(Color.White.WithAlpha(alpha));
            Graphics.Draw(rect);
        }
    }
}
