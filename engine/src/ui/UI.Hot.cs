//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class UI
{
    private static WidgetId _hotId;
    private static WidgetId _prevHotId;
    private static int _hotOriginalHash;
    private static int _hotCurrentHash;
    private static WidgetId _lastWidgetId;
    private static bool _valueChanged;
    public static void SetHot<T>(WidgetId id, T originalValue) where T : notnull
        => SetHot(id, originalValue.GetHashCode());

    public static void SetHot(WidgetId id, ReadOnlySpan<char> originalValue)
        => SetHot(id, string.GetHashCode(originalValue));

    public static void SetHot(WidgetId id, int originalHash)
    {
        if (_prevHotId != id && _hotId != id)
        {
            _hotOriginalHash = originalHash;
            _hotCurrentHash = originalHash;
        }

        _hotId = id;
    }

    public static void SetHot(WidgetId id)
    {
        _hotId = id;
        ElementTree.SetFocusById(id);
    }

    public static void ClearHot()
    {
        if (_hotId != 0)
            ElementTree.ClearFocus();
        _hotId = WidgetId.None;
    }

    public static bool HasHot() => _hotId != 0;
    internal static WidgetId HotId => _hotId;

    public static void NotifyChanged(int currentHash)
    {
        _valueChanged = true;
        _hotCurrentHash = currentHash;
    }

    public static void SetLastElement(WidgetId id)
    {
        _lastWidgetId = id;
    }

    public static bool IsHot() => _hotId != 0 && _hotId == _lastWidgetId;
    public static bool WasHot() => _prevHotId != 0 && _prevHotId == _lastWidgetId;
    public static bool WasChanged() => _valueChanged && _hotId == _lastWidgetId;

    public static bool IsChanged() => _hotId == _lastWidgetId
        ? _hotCurrentHash != _hotOriginalHash
        : _prevHotId == _lastWidgetId && _hotCurrentHash != _hotOriginalHash;

    public static bool HotEnter() => IsHot() && !WasHot();
    public static bool HotExit() => !IsHot() && WasHot();
}
