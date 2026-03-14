//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public interface IChangeHandler
{
    void BeginChange();
    void NotifyChange();
    void CancelChange();
}

public static partial class UI
{
    public static void HandleChange(IChangeHandler? handler)
    {
        if (handler == null) return;
        if (HotEnter()) handler.BeginChange();
        if (WasChanged()) handler.NotifyChange();
        if (HotExit() && !IsChanged()) handler.CancelChange();
    }
}
