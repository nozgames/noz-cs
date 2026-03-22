//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Mesh-based preview: tessellates vector shapes into flat-colored
//  triangle meshes for real-time editing feedback.
//

using Clipper2Lib;
using LibTessDotNet;
using NoZ.Editor.Msdf;

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
        if (!layer.Visible) return;

        // Collect subtract paths within this layer
        List<PathsD>? subtractContours = null;
        for (var pi = 0; pi < layer.Paths.Count; pi++)
        {
            var path = layer.Paths[pi];
            if (!path.IsSubtract || path.Anchors.Count < 3) continue;

            var contours = SpritePathClipper.SpritePathToPaths(path);
            if (contours.Count > 0)
            {
                subtractContours ??= new();
                subtractContours.Add(contours);
            }
        }

        // Tessellate normal/clip paths within this layer
        PathsD? accumulatedPaths = null;

        for (var pi = 0; pi < layer.Paths.Count; pi++)
        {
            var path = layer.Paths[pi];
            if (path.IsSubtract || path.Anchors.Count < 3) continue;

            var contours = SpritePathClipper.SpritePathToPaths(path);
            if (contours.Count == 0) continue;

            if (path.IsClip)
            {
                if (accumulatedPaths is not { Count: > 0 }) continue;
                contours = Clipper.BooleanOp(ClipType.Intersection,
                    contours, accumulatedPaths, FillRule.NonZero, precision: 6);
                if (contours.Count == 0) continue;
            }
            else
            {
                var accContours = contours;
                if (path.StrokeColor.A > 0 && path.StrokeWidth > 0)
                {
                    var halfStroke = path.StrokeWidth * SpritePath.StrokeScale;
                    var contracted = Clipper.InflatePaths(contours, -halfStroke,
                        JoinType.Round, EndType.Polygon, precision: 6);
                    if (contracted.Count > 0)
                        accContours = contracted;
                }

                if (accumulatedPaths == null)
                    accumulatedPaths = new PathsD(accContours);
                else
                    accumulatedPaths = Clipper.BooleanOp(ClipType.Union,
                        accumulatedPaths, accContours, FillRule.NonZero, precision: 6);
            }

            // Apply subtract paths within THIS layer only (layer-scoped)
            if (subtractContours != null)
            {
                PathsD? negativePaths = null;
                foreach (var subContours in subtractContours)
                {
                    negativePaths ??= new PathsD();
                    negativePaths.AddRange(subContours);
                }

                if (negativePaths is { Count: > 0 })
                {
                    contours = Clipper.BooleanOp(ClipType.Difference,
                        contours, negativePaths, FillRule.NonZero, precision: 6);
                    if (contours.Count == 0) continue;
                }
            }

            var hasStroke = path.StrokeColor.A > 0 && path.StrokeWidth > 0;
            var fillColor = path.FillColor;
            var hasFill = fillColor.A > 0;

            if (hasStroke)
            {
                var strokeColor = path.StrokeColor.ToColor();
                var halfStroke = path.StrokeWidth * SpritePath.StrokeScale;
                PathsD? contractedPaths = null;
                if (contours.Count > 0)
                {
                    contractedPaths = Clipper.InflatePaths(contours, -halfStroke,
                        JoinType.Round, EndType.Polygon, precision: 6);
                }

                if (hasFill)
                {
                    TessellateClipper(contours, ref vertexOffset, ref indexOffset, strokeColor);
                    if (contractedPaths is { Count: > 0 })
                        TessellateClipper(contractedPaths, ref vertexOffset, ref indexOffset, fillColor.ToColor());
                }
                else
                {
                    if (contractedPaths is { Count: > 0 })
                    {
                        var strokeRing = Clipper.BooleanOp(ClipType.Difference,
                            contours, contractedPaths, FillRule.NonZero, precision: 6);
                        if (strokeRing.Count > 0)
                            TessellateClipper(strokeRing, ref vertexOffset, ref indexOffset, strokeColor);
                    }
                    else
                    {
                        TessellateClipper(contours, ref vertexOffset, ref indexOffset, strokeColor);
                    }
                }
            }
            else if (hasFill)
            {
                TessellateClipper(contours, ref vertexOffset, ref indexOffset, fillColor.ToColor());
            }
        }

        // Recurse into child layers
        foreach (var child in layer.Children)
            TessellateLayer(child, ref vertexOffset, ref indexOffset);
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
