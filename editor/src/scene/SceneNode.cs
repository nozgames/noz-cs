//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public abstract class SceneNode : IDisposable
{
    public string Name { get; set; } = "";
    public Vector2 Position { get; set; } = Vector2.Zero;
    public float Rotation { get; set; }
    public Vector2 Scale { get; set; } = Vector2.One;
    public Color32 Color { get; set; } = Color32.White;
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public bool Placeholder { get; set; }
    public bool Expanded { get; set; } = true;
    public bool IsSelected { get; set; }
    public List<SceneNode> Children { get; } = [];
    public SceneNode? Parent { get; private set; }

    public virtual bool IsExpandable => false;

    public Matrix3x2 LocalTransform =>
        Matrix3x2.CreateScale(Scale) *
        Matrix3x2.CreateRotation(Rotation) *
        Matrix3x2.CreateTranslation(Position);

    public Matrix3x2 WorldTransform
    {
        get
        {
            var m = LocalTransform;
            var p = Parent;
            while (p != null)
            {
                m *= p.LocalTransform;
                p = p.Parent;
            }
            return m;
        }
    }

    public abstract SceneNode Clone();

    protected void ClonePropertiesTo(SceneNode target)
    {
        target.Name = Name;
        target.Position = Position;
        target.Rotation = Rotation;
        target.Scale = Scale;
        target.Color = Color;
        target.Visible = Visible;
        target.Locked = Locked;
        target.Placeholder = Placeholder;
        target.Expanded = Expanded;
        target.IsSelected = IsSelected;
    }

    public void Add(SceneNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public void Insert(int index, SceneNode child)
    {
        child.Parent = this;
        Children.Insert(index, child);
    }

    public void Remove(SceneNode child)
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

    public void ExpandAncestors()
    {
        var p = Parent;
        while (p != null)
        {
            p.Expanded = true;
            p = p.Parent;
        }
    }

    public virtual void Dispose()
    {
        foreach (var child in Children)
            child.Dispose();
    }

    public void ForEach(Action<SceneNode> action)
    {
        action(this);
        foreach (var child in Children)
            child.ForEach(action);
    }

    public void Collect<T>(List<T> result) where T : SceneNode
    {
        if (this is T match)
            result.Add(match);
        foreach (var child in Children)
            child.Collect(result);
    }

    public T? Find<T>(string name) where T : SceneNode
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
}
