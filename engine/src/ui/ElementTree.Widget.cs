//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NoZ;

public static unsafe partial class ElementTree
{
    private struct WidgetState
    {
        public int Index;
        public Rect Rect;
        public Matrix3x2 Transform;
        public WidgetFlags Flags;
    }

    internal static bool HasCurrentWidget => _currentWidget != 0;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidWidgetId(WidgetId id) => id > 0 && _widgets.ContainsKey(id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsWidgetValid(WidgetId id) => IsValidWidgetId(id);

    private static ref WidgetState GetWidgetState(WidgetId id) =>
        ref Unsafe.AsRef<WidgetState>(_widgets[id].Ptr);

    public static ref T GetWidgetState<T>(WidgetId id) where T : unmanaged =>
        ref *((T*)(_widgets[id].Ptr + 1));

    public static ref T GetWidgetState<T>() where T : unmanaged =>
        ref GetWidgetState<T>(_currentWidget);

    public static WidgetFlags GetWidgetFlags() => GetWidgetFlags(_currentWidget);

    public static WidgetFlags GetWidgetFlags(WidgetId id) => IsValidWidgetId(id)
        ? GetWidgetState(id).Flags
        : WidgetFlags.None;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref Element GetWidget(WidgetId id) =>
        ref GetElement(_widgets[id].Ptr->Index);

    internal static void SetWidgetFlag(WidgetFlags flag, bool value)
    {
        if (value) _pendingWidgetFlags |= flag;
        else _pendingWidgetFlags &= ~flag;
    }

    public static bool IsHovered() => GetWidgetFlags().HasFlag(WidgetFlags.Hovered);
    public static bool IsDisabled() => GetWidgetFlags().HasFlag(WidgetFlags.Disabled);
    public static bool IsChecked() => GetWidgetFlags().HasFlag(WidgetFlags.Checked);
    public static bool WasPressed() => GetWidgetFlags().HasFlag(WidgetFlags.Pressed);
    public static bool IsDown() => GetWidgetFlags().HasFlag(WidgetFlags.Down);
    public static bool HoverEnter() { var f = GetWidgetFlags(); return f.HasFlag(WidgetFlags.HoverChanged) && f.HasFlag(WidgetFlags.Hovered); }
    public static bool HoverExit() { var f = GetWidgetFlags(); return f.HasFlag(WidgetFlags.HoverChanged) && !f.HasFlag(WidgetFlags.Hovered); }
    public static bool HoverChanged() => GetWidgetFlags().HasFlag(WidgetFlags.HoverChanged);
    public static bool IsHot() => _hotId == _currentWidget;
    public static bool IsHot(WidgetId id) => _hotId == id;

    internal static bool IsHovered(WidgetId id) => GetWidgetFlags(id).HasFlag(WidgetFlags.Hovered);
    internal static bool WasPressed(WidgetId id) => GetWidgetFlags(id).HasFlag(WidgetFlags.Pressed);
    internal static bool IsDown(WidgetId id) => GetWidgetFlags(id).HasFlag(WidgetFlags.Down);
    internal static bool HoverChanged(WidgetId id) => GetWidgetFlags(id).HasFlag(WidgetFlags.HoverChanged);

    internal static bool IsParentRow() => GetElement(_stack[^1]).Type == ElementType.Row;
    internal static bool IsParentColumn() => GetElement(_stack[^1]).Type == ElementType.Column;

    internal static Rect GetWidgetWorldRect(WidgetId id)
    {
        if (id <= 0) return Rect.Zero;
        WidgetState state;
        if (_widgets.ContainsKey(id))
            state = Unsafe.AsRef<WidgetState>(_widgets[id].Ptr);
        else if (_widgetsPrev.ContainsKey(id))
            state = Unsafe.AsRef<WidgetState>(_widgetsPrev[id].Ptr);
        else
            return Rect.Zero;
        var topLeft = Vector2.Transform(state.Rect.Position, state.Transform);
        var bottomRight = Vector2.Transform(state.Rect.Position + state.Rect.Size, state.Transform);
        return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    internal static Rect GetWidgetRect(WidgetId id)
    {
        if (id <= 0) return Rect.Zero;
        if (_widgets.ContainsKey(id))
            return Unsafe.AsRef<WidgetState>(_widgets[id].Ptr).Rect;
        if (_widgetsPrev.ContainsKey(id))
            return Unsafe.AsRef<WidgetState>(_widgetsPrev[id].Ptr).Rect;
        return Rect.Zero;
    }

    public static bool HasCapture() =>
        _captureId != 0 && _captureId == _currentWidget;

    public static void SetHot()
    {
        _hotId = _currentWidget;
    }

    internal static void SetHotById(WidgetId id) => _hotId = id;

    public static void ClearHot()
    {
        _hotId = WidgetId.None;
    }

    public static void SetCapture()
    {
        _captureId = _currentWidget;
        Input.CaptureMouse();
    }

    internal static void SetCaptureById(WidgetId id)
    {
        _captureId = id;
        Input.CaptureMouse();
    }

    internal static bool HasCaptureById(WidgetId id) => _captureId != 0 && _captureId == id;

    public static void ReleaseCapture()
    {
        _captureId = WidgetId.None;
        Input.ReleaseMouseCapture();
    }

    public static Vector2 GetLocalMousePosition()
    {
        //ref var e = ref GetElement(_currentWidget);
        //Matrix3x2.Invert(e.Transform, out var inv);
        //return Vector2.Transform(MouseWorldPosition, inv);
        return Vector2.Zero;
    }

    private static UnsafeRef<WidgetState> BeginWidgetInternal(WidgetId id, int stateSize, bool interactive = true)
    {
        Debug.Assert(id >= 0);

        stateSize += sizeof(WidgetState);
        var data = AllocData(stateSize);
        NativeMemory.Clear(data.Ptr, (nuint)stateSize);
        var state = new UnsafeRef<WidgetState>((WidgetState*)data.Ptr);
        var hasPrev = _widgetsPrev.ContainsKey(id);
        if (hasPrev)
            NativeMemory.Copy(_widgetsPrev[id].Ptr, state.Ptr, (nuint)stateSize);

        ref var e = ref BeginElement(ElementType.Widget);
        e.Data = default;
        e.Data.Widget.Id = id;
        e.Data.Widget.LastFrame = _frame;
        e.Data.Widget.IsInteractive = interactive;

        state.Ptr->Index = e.Index;
        state.Ptr->Flags |= _pendingWidgetFlags;
        _pendingWidgetFlags = WidgetFlags.None;

        _widgets[id] = state;
        _currentWidget = id;
        return state;
    }

    public static ref T BeginWidget<T>(WidgetId id, bool interactive = true) where T : unmanaged =>
        ref *((T*)(BeginWidgetInternal(id, sizeof(T), interactive).Ptr + 1));

    public static void BeginWidget(WidgetId id, bool interactive = true)
    {
        Debug.Assert(id >= 0);
        BeginWidgetInternal(id, 0, interactive);
    }

    public static void EndWidget()
    {
        EndElement(ElementType.Widget);

        _currentWidget = WidgetId.None;
        for (int i = _stack.Length - 1; i >= 0; i--)
        {
            ref var e = ref GetElement(_stack[i]);
            if (e.Type == ElementType.Widget)
            {
                _currentWidget = e.Data.Widget.Id;
                break;
            }
        }
    }

}
