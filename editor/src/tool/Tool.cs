//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract class Tool : IDisposable
{
    protected InputScope Scope { get; private set; }

    public bool HideSelected { get; init; }

    public virtual void Begin() 
    { 
        Scope = Input.PushScope();
    }
    public virtual void Cancel() => Dispose();
    public virtual void Update() { }
    public virtual void UpdateUI() { }
    public virtual void Draw() { }

    public virtual void Dispose() 
    {        
        Input.PopScope(Scope);
    }
}
