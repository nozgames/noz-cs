//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Mesh preview for generated sprites: tessellates mask shapes with
//  per-path fill/stroke colors and composites the generated image.
//

using Clipper2Lib;
using LibTessDotNet;
using NoZ.Editor.Msdf;

namespace NoZ.Editor;

public partial class GenSpriteEditor
{
    private const int MaxMeshVertices = Shape.MaxAnchors * 16;
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

    private void UpdateMesh()
    {
        if (_meshVersion == Document.Version) return;
        _meshVersion = Document.Version;
        _meshSlots.Clear();

        var vertexOffset = 0;
        var indexOffset = 0;

        foreach (var layer in Document.Layers)
        {
            var shape = layer.Shape;

            // Collect subtract paths
            var negativePaths = new PathsD();
            for (ushort pi = 0; pi < shape.PathCount; pi++)
            {
                ref readonly var path = ref shape.GetPath(pi);
                if (!path.IsSubtract || path.AnchorCount < 3) continue;

                var subShape = new Msdf.Shape();
                ShapeClipper.AppendContour(subShape, shape, pi);
                subShape = ShapeClipper.Union(subShape);
                var contours = ShapeClipper.ShapeToPaths(subShape, 8);
                if (contours.Count > 0)
                    negativePaths.AddRange(contours);
            }

            // Tessellate each normal/clip path with its own fill/stroke color
            for (ushort pi = 0; pi < shape.PathCount; pi++)
            {
                ref readonly var path = ref shape.GetPath(pi);
                if (path.IsSubtract || path.AnchorCount < 3) continue;

                var pathShape = new Msdf.Shape();
                ShapeClipper.AppendContour(pathShape, shape, pi);
                pathShape = ShapeClipper.Union(pathShape);
                var contours = ShapeClipper.ShapeToPaths(pathShape, 8);
                if (contours.Count == 0) continue;

                // Apply subtract paths
                if (negativePaths.Count > 0)
                {
                    contours = Clipper.BooleanOp(ClipType.Difference,
                        contours, negativePaths, FillRule.NonZero, precision: 6);
                    if (contours.Count == 0) continue;
                }

                var hasStroke = path.StrokeColor.A > 0 && path.StrokeWidth > 0;
                var fillColor = path.FillColor;
                var hasFill = fillColor.A > 0;

                if (hasStroke)
                {
                    var strokeColor = path.StrokeColor.ToColor();
                    var halfStroke = path.StrokeWidth * Shape.StrokeScale;
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
        }
    }

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
        var texture = Document.Generation.Texture;
        if (texture == null) return;

        var ppu = EditorApplication.Config.PixelsPerUnitInv;
        var cs = Document.ConstrainedSize;

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
