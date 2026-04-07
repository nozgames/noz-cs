//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract class DocumentEditor(Document document) : IDisposable
{
    public Document Document { get; } = document;
    public EditorMode? Mode { get; private set; }

    public Command[]? Commands { get; protected set; }

    public virtual bool ShowInspector => false;
    public virtual bool ShowOutliner => false;
    public virtual bool RunInBackground => false;

    public void SetMode(EditorMode? mode)
    {
        Mode?.OnExit();
        Mode = mode;
        if (mode != null)
            mode.Editor = this;
        Mode?.OnEnter();
    }

    public void SetMode<T>(T mode) where T : EditorMode
    {
        SetMode((EditorMode?)mode);
    }

    public virtual void Update() { }
    public virtual void UpdateUI() { }
    public virtual void UpdateOverlayUI() { }
    public virtual void LateUpdate() { }
    public virtual void OnUndoRedo() { Mode?.OnUndoRedo(); }
    public virtual void Dispose()
    {
        Mode?.OnExit();
        Mode = null;
        GC.SuppressFinalize(this);
    }

    public virtual void InspectorUI() { }
    public virtual void OutlinerUI() { }
    public virtual void OpenContextMenu(WidgetId popupId) { }
}
