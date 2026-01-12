//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using SDL;
using static SDL.SDL3;
using System.Numerics;

namespace noz;

public static class Input {
    private static readonly bool[] ButtonState = new bool[(int)InputCode.Count];
    private static readonly float[] AxisState = new float[(int)InputCode.Count];
    private static readonly Stack<InputSet> InputSetStack = new();

    private static float _scrollX;
    private static float _scrollY;

    public static InputSet? CurrentInputSet => InputSetStack.Count > 0 ? InputSetStack.Peek() : null;

    internal static void Init() {
        SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
    }

    internal static void Shutdown() {
        InputSetStack.Clear();
    }

    public static void PushInputSet(InputSet set, bool inheritState = false) {
        if (inheritState && InputSetStack.Count > 0)
            set.CopyFrom(InputSetStack.Peek());

        CurrentInputSet?.Reset();
        CurrentInputSet?.SetActive(false);

        InputSetStack.Push(set);
        set.SetActive(true);
    }

    public static void PopInputSet() {
        if (InputSetStack.Count == 0)
            return;

        var old = InputSetStack.Pop();
        old.SetActive(false);

        if (InputSetStack.Count > 0) {
            var current = InputSetStack.Peek();
            current.SetActive(true);
            current.Reset();
        }
    }

    public static void SetInputSet(InputSet set) {
        while (InputSetStack.Count > 0)
            InputSetStack.Pop().SetActive(false);

        InputSetStack.Push(set);
        set.SetActive(true);
    }

    public static void Update() {
        _scrollX = 0;
        _scrollY = 0;

        CurrentInputSet?.Update();
    }

    public static void ProcessEvent(SDL_Event evt) {
        switch (evt.Type) {
            case SDL_EventType.SDL_EVENT_KEY_DOWN: {
                var code = ScancodeToInputCode(evt.key.scancode);
                if (code != InputCode.None)
                    ButtonState[(int)code] = true;
                break;
            }

            case SDL_EventType.SDL_EVENT_KEY_UP: {
                var code = ScancodeToInputCode(evt.key.scancode);
                if (code != InputCode.None)
                    ButtonState[(int)code] = false;
                break;
            }

            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN: {
                var code = MouseButtonToInputCode(evt.button.button);
                if (code != InputCode.None)
                    ButtonState[(int)code] = true;

                if (evt.button.clicks == 2 && evt.button.button == 1)
                    ButtonState[(int)InputCode.MouseLeftDoubleClick] = true;
                break;
            }

            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP: {
                var code = MouseButtonToInputCode(evt.button.button);
                if (code != InputCode.None)
                    ButtonState[(int)code] = false;

                ButtonState[(int)InputCode.MouseLeftDoubleClick] = false;
                break;
            }

            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                MousePosition = new Vector2(evt.motion.x, evt.motion.y);
                break;

            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                _scrollX = evt.wheel.x;
                _scrollY = evt.wheel.y;
                break;

            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN: {
                var code = GamepadButtonToInputCode((SDL_GamepadButton)evt.gbutton.button);
                if (code != InputCode.None)
                    ButtonState[(int)code] = true;
                break;
            }

            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP: {
                var code = GamepadButtonToInputCode((SDL_GamepadButton)evt.gbutton.button);
                if (code != InputCode.None)
                    ButtonState[(int)code] = false;
                break;
            }

            case SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION: {
                var axis = (SDL_GamepadAxis)evt.gaxis.axis;
                var value = evt.gaxis.value / 32767.0f;

                switch (axis) {
                    case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX:
                        AxisState[(int)InputCode.GamepadLeftStickX] = value;
                        ButtonState[(int)InputCode.GamepadLeftStickLeft] = value < -0.5f;
                        ButtonState[(int)InputCode.GamepadLeftStickRight] = value > 0.5f;
                        break;
                    case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY:
                        AxisState[(int)InputCode.GamepadLeftStickY] = value;
                        ButtonState[(int)InputCode.GamepadLeftStickUp] = value < -0.5f;
                        ButtonState[(int)InputCode.GamepadLeftStickDown] = value > 0.5f;
                        break;
                    case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX:
                        AxisState[(int)InputCode.GamepadRightStickX] = value;
                        break;
                    case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY:
                        AxisState[(int)InputCode.GamepadRightStickY] = value;
                        break;
                    case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER:
                        AxisState[(int)InputCode.GamepadLeftTrigger] = (value + 1.0f) / 2.0f;
                        ButtonState[(int)InputCode.GamepadLeftTriggerButton] = value > 0.5f;
                        break;
                    case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER:
                        AxisState[(int)InputCode.GamepadRightTrigger] = (value + 1.0f) / 2.0f;
                        ButtonState[(int)InputCode.GamepadRightTriggerButton] = value > 0.5f;
                        break;
                }

                break;
            }
        }
    }

    internal static bool IsButtonDownRaw(InputCode code) => ButtonState[(int)code];

    internal static float GetAxisValue(InputCode code) {
        return code switch {
            InputCode.MouseX => MousePosition.X,
            InputCode.MouseY => MousePosition.Y,
            InputCode.MouseScrollX => _scrollX,
            InputCode.MouseScrollY => _scrollY,
            _ => AxisState[(int)code]
        };
    }

    public static bool IsButtonDown(InputCode code) => CurrentInputSet?.IsButtonDown(code) ?? false;
    public static bool WasButtonPressed(InputCode code) => CurrentInputSet?.WasButtonPressed(code) ?? false;
    public static bool WasButtonReleased(InputCode code) => CurrentInputSet?.WasButtonReleased(code) ?? false;
    public static float GetAxis(InputCode code) => CurrentInputSet?.GetAxis(code) ?? 0.0f;

    public static bool IsShiftDown() => IsButtonDown(InputCode.KeyLeftShift) || IsButtonDown(InputCode.KeyRightShift);
    public static bool IsCtrlDown() => IsButtonDown(InputCode.KeyLeftCtrl) || IsButtonDown(InputCode.KeyRightCtrl);
    public static bool IsAltDown() => IsButtonDown(InputCode.KeyLeftAlt) || IsButtonDown(InputCode.KeyRightAlt);
    public static bool IsSuperDown() => IsButtonDown(InputCode.KeyLeftSuper) || IsButtonDown(InputCode.KeyRightSuper);

    public static Vector2 MousePosition { get; private set; }

    private static InputCode ScancodeToInputCode(SDL_Scancode scancode) {
        return scancode switch {
            SDL_Scancode.SDL_SCANCODE_A => InputCode.KeyA,
            SDL_Scancode.SDL_SCANCODE_B => InputCode.KeyB,
            SDL_Scancode.SDL_SCANCODE_C => InputCode.KeyC,
            SDL_Scancode.SDL_SCANCODE_D => InputCode.KeyD,
            SDL_Scancode.SDL_SCANCODE_E => InputCode.KeyE,
            SDL_Scancode.SDL_SCANCODE_F => InputCode.KeyF,
            SDL_Scancode.SDL_SCANCODE_G => InputCode.KeyG,
            SDL_Scancode.SDL_SCANCODE_H => InputCode.KeyH,
            SDL_Scancode.SDL_SCANCODE_I => InputCode.KeyI,
            SDL_Scancode.SDL_SCANCODE_J => InputCode.KeyJ,
            SDL_Scancode.SDL_SCANCODE_K => InputCode.KeyK,
            SDL_Scancode.SDL_SCANCODE_L => InputCode.KeyL,
            SDL_Scancode.SDL_SCANCODE_M => InputCode.KeyM,
            SDL_Scancode.SDL_SCANCODE_N => InputCode.KeyN,
            SDL_Scancode.SDL_SCANCODE_O => InputCode.KeyO,
            SDL_Scancode.SDL_SCANCODE_P => InputCode.KeyP,
            SDL_Scancode.SDL_SCANCODE_Q => InputCode.KeyQ,
            SDL_Scancode.SDL_SCANCODE_R => InputCode.KeyR,
            SDL_Scancode.SDL_SCANCODE_S => InputCode.KeyS,
            SDL_Scancode.SDL_SCANCODE_T => InputCode.KeyT,
            SDL_Scancode.SDL_SCANCODE_U => InputCode.KeyU,
            SDL_Scancode.SDL_SCANCODE_V => InputCode.KeyV,
            SDL_Scancode.SDL_SCANCODE_W => InputCode.KeyW,
            SDL_Scancode.SDL_SCANCODE_X => InputCode.KeyX,
            SDL_Scancode.SDL_SCANCODE_Y => InputCode.KeyY,
            SDL_Scancode.SDL_SCANCODE_Z => InputCode.KeyZ,

            SDL_Scancode.SDL_SCANCODE_1 => InputCode.Key1,
            SDL_Scancode.SDL_SCANCODE_2 => InputCode.Key2,
            SDL_Scancode.SDL_SCANCODE_3 => InputCode.Key3,
            SDL_Scancode.SDL_SCANCODE_4 => InputCode.Key4,
            SDL_Scancode.SDL_SCANCODE_5 => InputCode.Key5,
            SDL_Scancode.SDL_SCANCODE_6 => InputCode.Key6,
            SDL_Scancode.SDL_SCANCODE_7 => InputCode.Key7,
            SDL_Scancode.SDL_SCANCODE_8 => InputCode.Key8,
            SDL_Scancode.SDL_SCANCODE_9 => InputCode.Key9,
            SDL_Scancode.SDL_SCANCODE_0 => InputCode.Key0,

            SDL_Scancode.SDL_SCANCODE_RETURN => InputCode.KeyEnter,
            SDL_Scancode.SDL_SCANCODE_ESCAPE => InputCode.KeyEscape,
            SDL_Scancode.SDL_SCANCODE_BACKSPACE => InputCode.KeyBackspace,
            SDL_Scancode.SDL_SCANCODE_TAB => InputCode.KeyTab,
            SDL_Scancode.SDL_SCANCODE_SPACE => InputCode.KeySpace,

            SDL_Scancode.SDL_SCANCODE_MINUS => InputCode.KeyMinus,
            SDL_Scancode.SDL_SCANCODE_EQUALS => InputCode.KeyEquals,
            SDL_Scancode.SDL_SCANCODE_LEFTBRACKET => InputCode.KeyLeftBracket,
            SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET => InputCode.KeyRightBracket,
            SDL_Scancode.SDL_SCANCODE_SEMICOLON => InputCode.KeySemicolon,
            SDL_Scancode.SDL_SCANCODE_APOSTROPHE => InputCode.KeyQuote,
            SDL_Scancode.SDL_SCANCODE_GRAVE => InputCode.KeyTilde,
            SDL_Scancode.SDL_SCANCODE_COMMA => InputCode.KeyComma,
            SDL_Scancode.SDL_SCANCODE_PERIOD => InputCode.KeyPeriod,

            SDL_Scancode.SDL_SCANCODE_F1 => InputCode.KeyF1,
            SDL_Scancode.SDL_SCANCODE_F2 => InputCode.KeyF2,
            SDL_Scancode.SDL_SCANCODE_F3 => InputCode.KeyF3,
            SDL_Scancode.SDL_SCANCODE_F4 => InputCode.KeyF4,
            SDL_Scancode.SDL_SCANCODE_F5 => InputCode.KeyF5,
            SDL_Scancode.SDL_SCANCODE_F6 => InputCode.KeyF6,
            SDL_Scancode.SDL_SCANCODE_F7 => InputCode.KeyF7,
            SDL_Scancode.SDL_SCANCODE_F8 => InputCode.KeyF8,
            SDL_Scancode.SDL_SCANCODE_F9 => InputCode.KeyF9,
            SDL_Scancode.SDL_SCANCODE_F10 => InputCode.KeyF10,
            SDL_Scancode.SDL_SCANCODE_F11 => InputCode.KeyF11,
            SDL_Scancode.SDL_SCANCODE_F12 => InputCode.KeyF12,

            SDL_Scancode.SDL_SCANCODE_RIGHT => InputCode.KeyRight,
            SDL_Scancode.SDL_SCANCODE_LEFT => InputCode.KeyLeft,
            SDL_Scancode.SDL_SCANCODE_DOWN => InputCode.KeyDown,
            SDL_Scancode.SDL_SCANCODE_UP => InputCode.KeyUp,

            SDL_Scancode.SDL_SCANCODE_LCTRL => InputCode.KeyLeftCtrl,
            SDL_Scancode.SDL_SCANCODE_LSHIFT => InputCode.KeyLeftShift,
            SDL_Scancode.SDL_SCANCODE_LALT => InputCode.KeyLeftAlt,
            SDL_Scancode.SDL_SCANCODE_LGUI => InputCode.KeyLeftSuper,
            SDL_Scancode.SDL_SCANCODE_RCTRL => InputCode.KeyRightCtrl,
            SDL_Scancode.SDL_SCANCODE_RSHIFT => InputCode.KeyRightShift,
            SDL_Scancode.SDL_SCANCODE_RALT => InputCode.KeyRightAlt,
            SDL_Scancode.SDL_SCANCODE_RGUI => InputCode.KeyRightSuper,

            _ => InputCode.None
        };
    }

    private static InputCode MouseButtonToInputCode(byte button) {
        return button switch {
            1 => InputCode.MouseLeft,
            2 => InputCode.MouseMiddle,
            3 => InputCode.MouseRight,
            4 => InputCode.MouseButton4,
            5 => InputCode.MouseButton5,
            _ => InputCode.None
        };
    }

    private static InputCode GamepadButtonToInputCode(SDL_GamepadButton button) {
        return button switch {
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH => InputCode.GamepadA,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST => InputCode.GamepadB,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST => InputCode.GamepadX,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH => InputCode.GamepadY,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK => InputCode.GamepadBack,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE => InputCode.GamepadGuide,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START => InputCode.GamepadStart,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK => InputCode.GamepadLeftStickButton,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK => InputCode.GamepadRightStickButton,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER => InputCode.GamepadLeftShoulder,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER => InputCode.GamepadRightShoulder,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP => InputCode.GamepadDpadUp,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN => InputCode.GamepadDpadDown,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT => InputCode.GamepadDpadLeft,
            SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT => InputCode.GamepadDpadRight,
            _ => InputCode.None
        };
    }
}