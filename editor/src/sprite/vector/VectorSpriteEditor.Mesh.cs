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

    private readonly MeshVertex[] _meshVertices = new MeshVertex[MaxMeshVertices];
    private readonly ushort[] _meshIndices = new ushort[MaxMeshIndices];
    private bool _meshDirty = true;

    private struct MeshSlotData
    {
        public int VertexOffset;
        public int VertexCount;
        public int IndexOffset;
        public int IndexCount;
        public Color FillColor;
        public SpriteFillType FillType;
        public SpriteFillGradient Gradient;
        public Matrix3x2 GradientTransform;
    }

    private readonly List<MeshSlotData> _meshSlots = new();
    private readonly List<LayerPathResult> _tessellateResults = new();

    private int _meshFrame = -1;

    private bool TessellateClipper(PathsD paths, ref int vertexOffset, ref int indexOffset, Color color,
        SpriteFillType fillType = SpriteFillType.Solid,
        SpriteFillGradient gradient = default,
        Matrix3x2 gradientTransform = default)
    {
        return TessellateClipperTo(paths, ref vertexOffset, ref indexOffset, color,
            _meshVertices, _meshIndices, _meshSlots, fillType, gradient, gradientTransform);
    }

    private bool TessellateClipperTo(PathsD paths, ref int vertexOffset, ref int indexOffset, Color color,
        MeshVertex[] vertices, ushort[] indices, List<MeshSlotData> slots,
        SpriteFillType fillType = SpriteFillType.Solid,
        SpriteFillGradient gradient = default,
        Matrix3x2 gradientTransform = default)
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
            vertices, indices, slots, fillType, gradient, gradientTransform);
    }

    private bool EmitTessellationTo(Tess tess, ref int vertexOffset, ref int indexOffset, Color color,
        MeshVertex[] vertices, ushort[] indices, List<MeshSlotData> slots,
        SpriteFillType fillType = SpriteFillType.Solid,
        SpriteFillGradient gradient = default,
        Matrix3x2 gradientTransform = default)
    {
        if (tess.ElementCount == 0) return false;

        var vertCount = tess.VertexCount;
        var idxCount = tess.ElementCount * 3;

        if (vertexOffset + vertCount > MaxMeshVertices ||
            indexOffset + idxCount > MaxMeshIndices)
            return false;

        var startVert = vertexOffset;

        for (int v = 0; v < vertCount; v++)
        {
            ref var tv = ref tess.Vertices[v];
            vertices[vertexOffset + v] = new MeshVertex(
                tv.Position.X, tv.Position.Y, 0, 0, Color.White);
        }

        if (fillType == SpriteFillType.Linear)
        {
            var gradStart = Vector2.Transform(gradient.Start, gradientTransform);
            var gradEnd = Vector2.Transform(gradient.End, gradientTransform);
            var axis = gradEnd - gradStart;
            var axisSqLen = Vector2.Dot(axis, axis);
            var endVert = vertexOffset + vertCount;
            for (int v = startVert; v < endVert; v++)
            {
                var pos = vertices[v].Position;
                float t = axisSqLen > 0 ? Math.Clamp(Vector2.Dot(pos - gradStart, axis) / axisSqLen, 0, 1) : 0;
                vertices[v].Color = Color32.Mix(gradient.StartColor, gradient.EndColor, t).ToColor();
            }
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
            FillType = fillType,
            Gradient = gradient,
            GradientTransform = gradientTransform,
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
                if (slot.FillType == SpriteFillType.Linear)
                    Graphics.SetColor(Color.White.WithAlpha(Workspace.XrayAlpha));
                else
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
        if (!_meshDirty && _meshFrame == CurrentFrameIndex) return;

        _meshDirty = false;
        _meshFrame = CurrentFrameIndex;
        _meshSlots.Clear();

        var vertexOffset = 0;
        var indexOffset = 0;

        TessellateLayer(Document.RootLayer, ref vertexOffset, ref indexOffset);
    }

    private void TessellateLayer(SpriteLayer layer, ref int vertexOffset, ref int indexOffset)
    {
        _tessellateResults.Clear();
        SpriteLayerProcessor.ProcessLayer(layer, _tessellateResults);
        foreach (var result in _tessellateResults)
            TessellateClipper(result.Contours, ref vertexOffset, ref indexOffset, result.Color.ToColor(),
                result.FillType, result.Gradient, result.GradientTransform);
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
