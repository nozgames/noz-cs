//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract class SpriteNode
{
    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public List<SpriteNode> Children { get; } = [];
    public SpriteNode? Parent { get; private set; }
    public bool Expanded { get; set; } = true;
    public bool IsSelected { get; set; }
    public virtual bool IsExpandable => false;

    // Preview handle (transient, not cloned/saved)
    public int PreviewIndex = -1;
    public int PreviewGeneration;

    public void InvalidatePreview()
    {
        PreviewGeneration++;
        Parent?.InvalidatePreview();
    }

    public abstract SpriteNode Clone();

    protected void ClonePropertiesTo(SpriteNode target)
    {
        target.Name = Name;
        target.Visible = Visible;
        target.Locked = Locked;
        target.Expanded = Expanded;
        target.IsSelected = IsSelected;
    }

    #region Hierarchy

    public void Add(SpriteNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public void Insert(int index, SpriteNode child)
    {
        child.Parent = this;
        Children.Insert(index, child);
    }

    public void Remove(SpriteNode child)
    {
        child.Parent = null;
        Children.Remove(child);
    }

    public void RemoveFromParent() => Parent?.Remove(this);

    public void RemoveAt(int index)
    {
        Children[index].Parent = null;
        Children.RemoveAt(index);
    }

    public void Clear()
    {
        foreach (var child in Children)
            child.Parent = null;
        Children.Clear();
    }

    #endregion

    #region Tree Traversal

    public void ForEach(Action<SpriteNode> action)
    {
        action(this);
        foreach (var child in Children)
            child.ForEach(action);
    }

    public void ForEach(Action<SpriteGroup> action)
    {
        if (this is SpriteGroup group)
            action(group);
        foreach (var child in Children)
            child.ForEach(action);
    }

    public void ForEach(Action<PixelLayer> action)
    {
        if (this is PixelLayer pixel)
            action(pixel);
        foreach (var child in Children)
            child.ForEach(action);
    }

    public T? Find<T>(string name) where T : SpriteNode
    {
        if (this is T match && match.Name == name)
            return match;

        foreach (var child in Children)
        {
            var found = child.Find<T>(name);
            if (found != null)
                return found;
        }

        return null;
    }

    public virtual void ClearSelection()
    {
        IsSelected = false;
        foreach (var child in Children)
            child.ClearSelection();
    }

    #endregion
}
