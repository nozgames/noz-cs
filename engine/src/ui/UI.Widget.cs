//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public interface IChangeHandler
{
    void BeginChange();
    void NotifyChange();
    void EndChange();
    void CancelChange();
}

public static partial class UI
{
    public static void HandleChange(IChangeHandler? handler)
    {
        if (handler == null) return;
        if (WasChangeStarted()) handler.BeginChange();
        if (WasChanged()) handler.NotifyChange();
        if (WasChangeCancelled())
            handler.CancelChange();
        else if (WasChangeEnded())
            handler.EndChange();
    }

    public static AutoWidget BeginWidget(WidgetId id, WidgetFlags flags = WidgetFlags.None)
    {
        ElementTree.BeginWidget(id);
        return new AutoWidget();
    }

    public static void EndWidget()
    {
        ElementTree.EndWidget();
    }
}
