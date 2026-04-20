//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using static SDL.SDL3;

namespace NoZ.Platform;

public unsafe partial class SDLPlatform
{
    private bool _textInputActive;

    public bool IsTextboxVisible => _textInputActive;

    public void ShowTextbox(Rect rect, string text, NativeTextboxStyle style)
    {
        if (_window != null && !_textInputActive)
        {
            SDL_StartTextInput(_window);
            _textInputActive = true;
        }
    }

    public void HideTextbox()
    {
        if (_window != null && _textInputActive)
        {
            SDL_StopTextInput(_window);
            _textInputActive = false;
        }
    }

    public void UpdateTextboxRect(Rect rect, int fontSize) { }
    public bool UpdateTextboxText(ref string text) => false;
}
