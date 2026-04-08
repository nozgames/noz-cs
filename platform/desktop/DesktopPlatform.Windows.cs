//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using static SDL.SDL3;

namespace NoZ.Platform;

public unsafe partial class SDLPlatform
{
    private bool _iosTextInputActive;

    public bool IsTextboxVisible => _iosTextInputActive;

    public void ShowTextbox(Rect rect, string text, NativeTextboxStyle style)
    {
        if (OperatingSystem.IsIOS())
        {
            if (_window != null && !_iosTextInputActive)
            {
                SDL_StartTextInput(_window);
                _iosTextInputActive = true;
            }
        }
    }

    public void HideTextbox()
    {
        if (OperatingSystem.IsIOS())
        {
            if (_window != null && _iosTextInputActive)
            {
                SDL_StopTextInput(_window);
                _iosTextInputActive = false;
            }
        }
    }

    public void UpdateTextboxRect(Rect rect, int fontSize) { }
    public bool UpdateTextboxText(ref string text) => false;
}
