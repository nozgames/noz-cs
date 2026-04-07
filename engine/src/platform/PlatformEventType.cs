//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Platform;

public enum PlatformEventType
{
    None,

    // Keyboard
    KeyDown,
    KeyUp,
    TextInput,

    // Mouse
    MouseButtonDown,
    MouseButtonUp,
    MouseMove,
    MouseScroll,

    // Gamepad
    GamepadButtonDown,
    GamepadButtonUp,
    GamepadAxis,

    // Touch
    TouchDown,
    TouchUp,
    TouchMove,
    TouchCancel,

    // Pinch
    PinchBegin,
    PinchUpdate,
    PinchEnd,

    // Pen
    PenDown,
    PenUp,
    PenMove,

    // Window
    WindowResize,
    WindowFocus,
    WindowUnfocus,
}