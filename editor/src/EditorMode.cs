//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract class EditorMode
{
    public DocumentEditor Editor { get; internal set; } = null!;

    public virtual void OnEnter() { }
    public virtual void OnExit() { }
    public virtual void OnUndoRedo() { }
    public virtual void Update() { }
    public virtual void Draw() { }
    public virtual void DrawUI() { }
}

public abstract class EditorMode<T> : EditorMode where T : DocumentEditor
{
    public new T Editor => (T)base.Editor;
}
