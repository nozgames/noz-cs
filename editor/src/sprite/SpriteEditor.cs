//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract partial class SpriteEditor(SpriteDocument document) : DocumentEditor(document)
{
    public new SpriteDocument Document => (SpriteDocument)base.Document;

    // Outliner hooks for subclasses
    protected abstract bool IsNodeSelected(SpriteNode node);
    protected abstract void OnNodeClicked(SpriteNode node);
    protected abstract void OnOutlinerChanged();
    protected virtual void OnVisibilityChanged(SpriteNode node) { }
    protected virtual bool IsNodeExpandable(SpriteNode node) => node is SpriteLayer && node.Children.Count > 0;
    protected virtual string GetNodeFallbackName(SpriteNode node) => node is SpriteLayer ? "Group" : "Path";
    protected virtual Sprite GetNodeIcon(SpriteNode node) => node is SpriteLayer
        ? EditorAssets.Sprites.IconPathLayer
        : EditorAssets.Sprites.IconPath;
    protected virtual bool ReverseChildren => false;

    protected virtual void OnNodeRightClicked(SpriteNode node, bool isHovered) { }

    protected virtual void PopulateDragNodes(SpriteNode clickedNode, List<SpriteNode> dragNodes)
    {
        dragNodes.Clear();
        dragNodes.Add(clickedNode);
    }
}
