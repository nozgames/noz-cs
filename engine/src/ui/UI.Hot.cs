//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class UI
{
    private static WidgetId _prevHotId;
    private static int _hotOriginalHash;
    private static int _hotCurrentHash;

    public static void SetHot<T>(WidgetId id, T originalValue) where T : notnull
    {
        _hotOriginalHash = originalValue.GetHashCode();
        _hotCurrentHash = _hotOriginalHash;
        SetHot(id);
    }

    public static void SetHot(WidgetId id, ReadOnlySpan<char> originalValue)
    {
        _hotOriginalHash = string.GetHashCode(originalValue);
        _hotCurrentHash = _hotOriginalHash;
        SetHot(id);
    }

    public static void SetHot(WidgetId id, int originalHash)
    {
        _hotOriginalHash = originalHash;
        _hotCurrentHash = originalHash;
        SetHot(id);
    }

    public static void SetHot(WidgetId id)
    {
        ElementTree._hotId = id;
        ElementTree.SetWidgetFlag(id, WidgetFlags.Hot);
    }

    public static void ClearHot()
    {
        if (ElementTree._hotId != WidgetId.None)
        {
            ElementTree.ClearWidgetFlag(ElementTree._hotId, WidgetFlags.Hot);
            ElementTree._hotId = WidgetId.None;
        }
    }

    public static bool HasHot() => ElementTree._hotId != 0 || _prevHotId != 0;
    internal static WidgetId HotId => ElementTree._hotId;

    public static void NotifyChanged<T>(T currentValue) where T : notnull
    {
        _hotCurrentHash = currentValue.GetHashCode();
        ElementTree.SetWidgetFlag(ElementTree._lastWidgetId, WidgetFlags.Changed);
    }

    public static void NotifyChanged(int currentHash)
    {
        _hotCurrentHash = currentHash;
        ElementTree.SetWidgetFlag(ElementTree._lastWidgetId, WidgetFlags.Changed);
    }

    public static bool IsHot() => ElementTree.GetWidgetFlags(ElementTree._lastWidgetId).HasFlag(WidgetFlags.Hot);
    public static bool WasHot() => ElementTree.GetPrevWidgetFlags(ElementTree._lastWidgetId).HasFlag(WidgetFlags.Hot);
    public static bool WasChanged() => ElementTree.GetWidgetFlags(ElementTree._lastWidgetId).HasFlag(WidgetFlags.Changed);

    public static bool IsChanged() => IsHot() && WasChanged();

    public static bool HotEnter() => IsHot() && !WasHot();
    public static bool HotExit() => !IsHot() && WasHot();

    public static bool WasChangeStarted() => HotEnter() || (WasChanged() && !IsHot() && !WasHot());
    public static bool WasChangeEnded() => HotExit() || (WasChanged() && !IsHot() && !WasHot());
    public static bool WasChangeCancelled() => WasChangeEnded() && _hotCurrentHash == _hotOriginalHash;
}
