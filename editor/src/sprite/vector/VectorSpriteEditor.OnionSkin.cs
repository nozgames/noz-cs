//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class VectorSpriteEditor
{
    private readonly MeshVertex[] _onionVertices = new MeshVertex[MaxMeshVertices];
    private readonly ushort[] _onionIndices = new ushort[MaxMeshIndices];
    private readonly List<MeshSlotData> _onionSlots = new();
    private int _onionFrame = -1;

    private void DrawOnionSkin()
    {
        if (!_onionSkin || _isPlaying || !Document.IsAnimated || Document.FrameCount <= 1)
            return;

        var currentFi = CurrentFrameIndex;
        if (_onionFrame != currentFi || _meshDirty)
        {
            var frameCount = Document.FrameCount;
            var prevFi = (currentFi - 1 + frameCount) % frameCount;
            var nextFi = (currentFi + 1) % frameCount;

            var prevColor = new Color(1f, 0.3f, 0.3f, 0.1f);
            var nextColor = new Color(0.3f, 1f, 0.3f, 0.1f);

            _onionSlots.Clear();
            _onionFrame = currentFi;

            var vertexOffset = 0;
            var indexOffset = 0;

            TessellateOnionFrame(prevFi, prevColor, ref vertexOffset, ref indexOffset);
            TessellateOnionFrame(nextFi, nextColor, ref vertexOffset, ref indexOffset);
        }

        DrawOnionMesh();
    }

    private void TessellateOnionFrame(int frameIndex, Color tint, ref int vertexOffset, ref int indexOffset)
    {
        var frameCount = Document.FrameCount;
        if (frameIndex < 0 || frameIndex >= frameCount)
            return;

        _tessellateResults.Clear();
        SpriteGroupProcessor.ProcessLayer(Document.Root, _tessellateResults);
        foreach (var result in _tessellateResults)
            TessellateClipperTo(result.Contours, ref vertexOffset, ref indexOffset, tint, _onionVertices, _onionIndices, _onionSlots);
    }

    private void DrawOnionMesh()
    {
        if (_onionSlots.Count == 0) return;

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(4);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            Graphics.SetTransform(Document.Transform);
            Graphics.SetTexture(Graphics.WhiteTexture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);

            foreach (var slot in _onionSlots)
            {
                Graphics.SetColor(slot.FillColor);
                Graphics.Draw(
                    _onionVertices.AsSpan(slot.VertexOffset, slot.VertexCount),
                    _onionIndices.AsSpan(slot.IndexOffset, slot.IndexCount));
            }
        }
    }
}
