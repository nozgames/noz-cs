//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class Command(string name, Action handler, KeyBinding[]? shortcuts=null, Sprite? icon=null)
{
    public string Name { get; init; } = name;
    public Action Handler { get; init; } = handler;

    public KeyBinding[]? Shortcuts { get; init; } = shortcuts;
    public Sprite? Icon { get; init; } = icon;

    public PopupMenuItem ToPopupMenuItem(Func<bool>? enabled = null) =>
        new()
        {
            Label = Name,
            Icon = Icon,
            Handler = Handler,
            Shortcut = Shortcuts?[0] ?? null,
            GetEnabled = enabled
        };
}
