//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ
{
    public readonly struct KeyBinding(InputCode key, bool ctrl = false, bool alt = false, bool shift = false)
    {
        public InputCode Key { get; } = key;
        public bool Ctrl { get; } = ctrl;
        public bool Alt { get; } = alt;
        public bool Shift { get; } = shift;

        public static implicit operator KeyBinding(InputCode key) => new(key);
    }
}
