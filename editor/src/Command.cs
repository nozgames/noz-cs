//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class Command
{
    public required string Name { get; init; }
    public required Action Handler { get; init; }

    public InputCode Key { get; init; } = InputCode.None;
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Shift { get; init; }
    public Sprite? Icon { get; init;  }

    public PopupMenuItem ToPopupMenuItem(Func<bool>? enabled = null) =>
        new()
        {
            Label = Name,
            Icon = Icon,
            Handler = Handler,
            Key = Key,
            Ctrl = Ctrl,
            Shift = Shift,
            Alt = Alt,
            GetEnabled = enabled
        };
}
