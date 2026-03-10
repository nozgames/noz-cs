//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static class Style
{
    public static class Widget
    {
        public const float Height = 30.0f;
        public const float FontSize = 12.0f;
        public const float IconSize = 16.0f;
        public const float Spacing = 6.0f;
        public const float BorderRadius = 0;
    }

    public static class Palette
    {
        public static readonly Color Background = Color.Transparent;
        public static readonly Color Content = Color.White;
        public static readonly Color Primary = Color.White;
        public static readonly Color Border = Color.Transparent;
    }

    public static DropDownStyle DropDown = new();
    public static PopupMenuStyle PopupMenu = new();
    public static TextInputStyle TextInput = new();
}
