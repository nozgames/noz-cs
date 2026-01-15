//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract class Tool
{
    public bool HideSelected { get; init; }

    public virtual void Begin() { }
    public virtual void End() { }
    public virtual void Cancel() => End();
    public virtual void Update() { }
    public virtual void Draw() { }
}
