//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Platform;

public struct PlatformEvent
{
    public PlatformEventType Type;

    // Keyboard
    public InputCode KeyCode;

    // Text Input
    public string? Text;

    // Mouse
    public InputCode MouseButton;
    public Vector2 MousePosition;
    public float ScrollX;
    public float ScrollY;
    public int ClickCount;

    // Gamepad
    public InputCode GamepadButton;
    public InputCode GamepadAxis;
    public float AxisValue;

    // Touch
    public long FingerId;
    public Vector2 TouchPosition;
    public Vector2 TouchDelta;
    public float Pressure;

    // Pinch
    public float PinchScale;

    // Pen
    public Vector2 PenPosition;
    public bool PenEraser;

    // Window
    public int WindowWidth;
    public int WindowHeight;

    public static PlatformEvent KeyDown(InputCode code) => new() { Type = PlatformEventType.KeyDown, KeyCode = code };
    public static PlatformEvent KeyUp(InputCode code) => new() { Type = PlatformEventType.KeyUp, KeyCode = code };
    public static PlatformEvent TextInputEvent(string text) => new() { Type = PlatformEventType.TextInput, Text = text };

    public static PlatformEvent MouseDown(InputCode button, int clickCount = 1) => new() { Type = PlatformEventType.MouseButtonDown, MouseButton = button, ClickCount = clickCount };
    public static PlatformEvent MouseUp(InputCode button) => new() { Type = PlatformEventType.MouseButtonUp, MouseButton = button };
    public static PlatformEvent MouseMove(Vector2 position) => new() { Type = PlatformEventType.MouseMove, MousePosition = position };
    public static PlatformEvent MouseScroll(float x, float y) => new() { Type = PlatformEventType.MouseScroll, ScrollX = x, ScrollY = y };

    public static PlatformEvent GamepadDown(InputCode button) => new() { Type = PlatformEventType.GamepadButtonDown, GamepadButton = button };
    public static PlatformEvent GamepadUp(InputCode button) => new() { Type = PlatformEventType.GamepadButtonUp, GamepadButton = button };
    public static PlatformEvent GamepadAxisMove(InputCode axis, float value) => new() { Type = PlatformEventType.GamepadAxis, GamepadAxis = axis, AxisValue = value };

    public static PlatformEvent PinchBeginEvent() => new() { Type = PlatformEventType.PinchBegin };
    public static PlatformEvent PinchUpdateEvent(float scale) => new() { Type = PlatformEventType.PinchUpdate, PinchScale = scale };
    public static PlatformEvent PinchEndEvent() => new() { Type = PlatformEventType.PinchEnd };

    public static PlatformEvent TouchDown(long fingerId, Vector2 position, float pressure) => new() { Type = PlatformEventType.TouchDown, FingerId = fingerId, TouchPosition = position, Pressure = pressure };
    public static PlatformEvent TouchUp(long fingerId, Vector2 position) => new() { Type = PlatformEventType.TouchUp, FingerId = fingerId, TouchPosition = position };
    public static PlatformEvent TouchMoveEvent(long fingerId, Vector2 position, Vector2 delta, float pressure) => new() { Type = PlatformEventType.TouchMove, FingerId = fingerId, TouchPosition = position, TouchDelta = delta, Pressure = pressure };
    public static PlatformEvent TouchCancelEvent(long fingerId) => new() { Type = PlatformEventType.TouchCancel, FingerId = fingerId };

    public static PlatformEvent PenDownEvent(Vector2 position, float pressure, bool eraser) => new() { Type = PlatformEventType.PenDown, PenPosition = position, Pressure = pressure, PenEraser = eraser };
    public static PlatformEvent PenUpEvent(Vector2 position) => new() { Type = PlatformEventType.PenUp, PenPosition = position };
    public static PlatformEvent PenMoveEvent(Vector2 position, float pressure) => new() { Type = PlatformEventType.PenMove, PenPosition = position, Pressure = pressure };

    public static PlatformEvent Resize(int width, int height) => new() { Type = PlatformEventType.WindowResize, WindowWidth = width, WindowHeight = height };
    public static PlatformEvent Focus() => new() { Type = PlatformEventType.WindowFocus };
    public static PlatformEvent Unfocus() => new() { Type = PlatformEventType.WindowUnfocus };
}
