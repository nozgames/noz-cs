//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public struct ButtonStyle
{
    public struct State
    {
        public Color Color;
        public float BorderWidth;
        public float BorderRadius;
        public Color BorderColor;
        public Color ContentColor;
    }

    public Font? Font = null;
    public float FontSize = 12.0f;
    public Size Width = Size.Fit;
    public Size Height = 30.0f;    
    public State Normal;
    public State Hovered;
    public EdgeInsets Padding;

    public ButtonStyle()
    {
    }
}

public struct StyleValue<T> where T : unmanaged
{
    public T Normal;
    public T? Hovered;
    public T? Pressed;
    public T? Disabled;
    public T? Checked;

    public StyleValue()
    {
    }

    // Implicitly convert from T to StyleProperty<T>
    public static implicit operator StyleValue<T>(T value)
    {
        return new StyleValue<T> { Normal = value };
    }
}

public struct NewContainerStyle
{
    public StyleValue<Color> BackgroundColor;
    public StyleValue<Color> BorderColor;
    public StyleValue<float> BorderWidth;
    public StyleValue<float> BorderRadius;
    public Size Width = Size.Fit;
    public Size Height = Size.Fit;    
    public EdgeInsets Padding;
    public NewContainerStyle()
    {
    }
}

public struct NewButtonStyle
{
    public StyleValue<Color> BackgroundColor;
    public StyleValue<Color> BorderColor;
    public StyleValue<float> BorderWidth;
    public StyleValue<float> BorderRadius;
    public StyleValue<Color> ContentColor;
    public Font? Font = null;
    public float FontSize = 12.0f;
    public Size Width = Size.Fit;
    public Size Height = 30.0f;    
    public EdgeInsets Padding;
    public NewButtonStyle()
    {
    }
}

public static partial class UI
{
    public static bool Button(int id, ButtonStyle style)
    {
        var wasPressed = false;
        ElementTree.BeginWidget(id);
        wasPressed = ElementTree.WasPressed();
        ElementTree.EndWidget();
        return wasPressed;
    }
}

