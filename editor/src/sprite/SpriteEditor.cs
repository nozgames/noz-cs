//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public abstract partial class SpriteEditor(SpriteDocument document) : DocumentEditor(document)
{
    public new SpriteDocument Document => (SpriteDocument)base.Document;

    protected abstract bool IsNodeSelected(SpriteNode node);
    protected virtual bool IsNodeActive(SpriteNode node) => false;
    protected abstract void OnNodeClicked(SpriteNode node);
    protected abstract void OnOutlinerChanged();
    protected virtual void OnVisibilityChanged(SpriteNode node) { }
    protected virtual string GetNodeFallbackName(SpriteNode node) => "Node";
    protected virtual Sprite GetNodeIcon(SpriteNode node) => node is SpriteGroup
        ? EditorAssets.Sprites.IconFolder
        : EditorAssets.Sprites.IconPath;
    protected virtual bool ReverseChildren => false;

    protected virtual void OnNodeRightClicked(SpriteNode node, bool isHovered) { }
    protected virtual Sprite? GetNodePreview(SpriteNode node) => null;
    protected virtual void OnNodeFrameSwitch(int frameIndex) { }

    protected void DrawSkeletonOverlay()
    {
        var skeleton = Document.Skeleton.Value;
        if (skeleton == null)
            return;

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(0);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            foreach (var bound in skeleton.Attachments)
            {
                if (bound is not SpriteDocument sprite || sprite == Document) continue;
                Graphics.SetBlendMode(BlendMode.Alpha);
                Graphics.SetTransform(Document.Transform);
                sprite.DrawSprite(alpha: 0.3f);
            }
        }

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetSortGroup(6);
            Graphics.SetTransform(Document.Transform);

            for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
                Gizmos.DrawBoneAndJoints(skeleton, boneIndex, selected: false);
        }
    }

    protected virtual void PopulateDragNodes(SpriteNode clickedNode, List<SpriteNode> dragNodes)
    {
        dragNodes.Clear();
        dragNodes.Add(clickedNode);
    }
}
