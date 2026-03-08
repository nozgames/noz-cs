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
        //ref var e = ref CurrentWidget;
        //if (value) e.Data.Widget.Flags |= flag;
        //else e.Data.Widget.Flags &= ~flag;
    }

    public static bool IsHovered() => GetWidgetFlags().HasFlag(WidgetFlags.Hovered);
    public static bool IsDisabled() => GetWidgetFlags().HasFlag(WidgetFlags.Disabled);
    public static bool IsChecked() => GetWidgetFlags().HasFlag(WidgetFlags.Checked);
    public static bool WasPressed() => GetWidgetFlags().HasFlag(WidgetFlags.Pressed);
    public static bool IsDown() => GetWidgetFlags().HasFlag(WidgetFlags.Down);
    public static bool HoverEnter() { var f = GetWidgetFlags(); return f.HasFlag(WidgetFlags.HoverChanged) && f.HasFlag(WidgetFlags.Hovered); }
    public static bool HoverExit() { var f = GetWidgetFlags(); return f.HasFlag(WidgetFlags.HoverChanged) && !f.HasFlag(WidgetFlags.Hovered); }
    public static bool HoverChanged() => GetWidgetFlags().HasFlag(WidgetFlags.HoverChanged);
    public static bool HasFocus() => false; //  _focusId == CurrentWidget.Data.Widget.Id;
    public static bool HasFocusOn(WidgetId id) => _focusId == id;

    internal static bool IsHovered(WidgetId id) => GetWidgetFlags(id).HasFlag(WidgetFlags.Hovered);
    internal static bool WasPressed(WidgetId id) => GetWidgetFlags(id).HasFlag(WidgetFlags.Pressed);
    internal static bool IsDown(WidgetId id) => GetWidgetFlags(id).HasFlag(WidgetFlags.Down);
    internal static bool HoverChanged(WidgetId id) => GetWidgetFlags(id).HasFlag(WidgetFlags.HoverChanged);

    internal static bool IsParentRow() => GetElement(_stack[^1]).Type == ElementType.Row;
    internal static bool IsParentColumn() => GetElement(_stack[^1]).Type == ElementType.Column;

    internal static Rect GetWidgetWorldRect(WidgetId id)
    {
        if (!IsValidWidgetId(id)) return Rect.Zero;
        ref readonly var state = ref GetWidgetState(id);
        var topLeft = Vector2.Transform(state.Rect.Position, state.Transform);
        var bottomRight = Vector2.Transform(state.Rect.Position + state.Rect.Size, state.Transform);
        return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    internal static Rect GetWidgetRect(WidgetId id)
    {
        if (!IsValidWidgetId(id)) return Rect.Zero;
        ref readonly var state = ref GetWidgetState(id);
        return state.Rect;
    }

    public static bool HasCapture() =>
        _captureId != 0 && _captureId == _currentWidget;

    public static void SetFocus()
    {
        _focusId = _currentWidget;
    }

    internal static void SetFocusById(WidgetId id) => _focusId = id;

    public static void ClearFocus()
    {
        _focusId = WidgetId.None;
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

    private static UnsafeRef<WidgetState> BeginWidgetInternal(WidgetId id, int stateSize)
    {
        Debug.Assert(id >= 0);
        
        stateSize += sizeof(WidgetState);   
        var state = new UnsafeRef<WidgetState>((WidgetState*)AllocData(stateSize).Ptr);
        if (IsValidWidgetId(id))
            NativeMemory.Copy(_widgets[id].Ptr, state.Ptr, (nuint)stateSize);        

        ref var e = ref BeginElement(ElementType.Widget);
        e.Data = default;
        e.Data.Widget.Id = id;
        e.Data.Widget.LastFrame = _frame;

        state.Ptr->Index = e.Index;
        _widgets[id] = state;
        _currentWidget = id;
        return state;
    }

    public static ref T BeginWidget<T>(WidgetId id) where T : unmanaged =>
        ref *((T*)BeginWidgetInternal(id, sizeof(T)).Ptr);

    public static void BeginWidget(WidgetId id)
    {
        Debug.Assert(id >= 0);
        BeginWidgetInternal(id, 0);
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

    #if false
    public static bool Toggle(int id, bool value, Sprite icon, Color color, Color activeColor)
    {
        BeginWidget(id);
        var fillColor = value ? activeColor : (IsHovered() ? activeColor.WithAlpha(0.5f) : Color.Transparent);
        BeginFill(fillColor);
        Image(icon, color: value ? Color.White : color);
        EndFill();
        var toggled = WasPressed();
        EndWidget();
        return toggled;
    }

    public static bool Slider(int id, ref float value, float min = 0f, float max = 1f,
        Color trackColor = default, Color fillColor = default, Color thumbColor = default,
        float height = 20f, float trackHeight = 4f)
    {
        if (trackColor.IsTransparent) trackColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        if (fillColor.IsTransparent) fillColor = new Color(0.4f, 0.6f, 1f, 1f);
        if (thumbColor.IsTransparent) thumbColor = Color.White;

        var changed = false;
        var t = max > min ? Math.Clamp((value - min) / (max - min), 0f, 1f) : 0f;

        BeginWidget(id);
        BeginSize(Size.Percent(1), new Size(height));

        // Track background (centered vertically)
        BeginAlign(new Align2(Align.Min, Align.Center));
        BeginSize(Size.Percent(1), new Size(trackHeight));
        BeginFill(trackColor);
        EndFill();
        EndSize();
        EndAlign();

        // Fill bar
        if (t > 0)
        {
            BeginAlign(new Align2(Align.Min, Align.Center));
            BeginSize(Size.Percent(t), new Size(trackHeight));
            BeginFill(IsDown() ? fillColor : fillColor.WithAlpha(0.8f));
            EndFill();
            EndSize();
            EndAlign();
        }

        // Input: capture on down, update value while captured
        if (IsDown() && !HasCapture())
            SetCapture();

        if (HasCapture())
        {
            var localMouse = GetLocalMousePosition();
            ref var we = ref GetElement(_currentWidget);
            var widgetWidth = we.Rect.Width;
            if (widgetWidth > 0)
            {
                var localX = Math.Clamp(localMouse.X / widgetWidth, 0f, 1f);
                var newValue = min + localX * (max - min);
                newValue = Math.Clamp(newValue, min, max);

                if (newValue != value)
                {
                    value = newValue;
                    changed = true;
                }
            }
        }

        EndSize();
        EndWidget();
        return changed;
    }
#endif
}
