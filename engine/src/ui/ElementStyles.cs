//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ
{

    public struct ContainerStyle()
    {
        public float Width = float.MaxValue;
        public float Height = float.MaxValue;
        public float MinWidth = 0;
        public float MinHeight = 0;
        public float MaxWidth = float.MaxValue;
        public float MaxHeight = float.MaxValue;
        public Align AlignX = Align.Fill;
        public Align AlignY = Align.Fill;
        public EdgeInsets Margin = EdgeInsets.Zero;
        public EdgeInsets Padding = EdgeInsets.Zero;
        public Color Color = Color.Transparent;
        public BorderStyle Border = BorderStyle.None;
        public float Spacing = 0;
        public bool Clip = false;

        public ContainerData ToData() => new()
        {
            Width = Width,
            Height = Height,
            MinWidth = MinWidth,
            MinHeight = MinHeight,
            MaxWidth = MaxWidth,
            MaxHeight = MaxHeight,
            AlignX = AlignX,
            AlignY = AlignY,
            Margin = Margin,
            Padding = Padding,
            Color = Color,
            Border = Border,
            Spacing = Spacing,
            Clip = Clip
        };

        public static ContainerStyle Default => new();
    }

    public struct LabelStyle()
    {
        public float FontSize = 16;
        public Color Color = Color.White;
        public Align AlignX = Align.Min;
        public Align AlignY = Align.Center;
        public Font? Font = null;
    }

    public struct ImageStyle()
    {
        public ImageStretch Stretch = ImageStretch.Uniform;
        public Align AlignX = Align.Min;
        public Align AlignY = Align.Min;
        public float Scale = 1.0f;
        public Color Color = Color.White;
    }

    public struct RectangleStyle()
    {
        public float Width = float.MaxValue;
        public float Height = float.MaxValue;
        public Color Color = Color.White;
    }

    public struct ExpandedStyle()
    {
        public float Flex = 1.0f;
    }

    public struct TransformStyle()
    {
        public Vector2 Origin = new(0.5f, 0.5f);
        public Vector2 Translate = Vector2.Zero;
        public float Rotate = 0;
        public Vector2 Scale = Vector2.One;
    }

    public struct ScrollableStyle()
    {
    }

    public struct GridStyle()
    {
        public float Spacing = 0;
        public int Columns = 3;
        public float CellWidth = 100;
        public float CellHeight = 100;
        public int VirtualCount = 0;
        public byte ScrollId = 0;
        public Action<int, int>? VirtualCellFunc = null;
        public Action<int, int>? VirtualRangeFunc = null;
    }

    public struct PopupStyle()
    {
        public Align AnchorX = Align.Min;
        public Align AnchorY = Align.Min;
        public Align PopupAlignX = Align.Min;
        public Align PopupAlignY = Align.Min;
        public EdgeInsets Margin = EdgeInsets.Zero;
    }

    public struct CanvasStyle()
    {
        public CanvasType Type = CanvasType.Screen;
        public Color Color = Color.Transparent;
        public Vector2Int ColorOffset = Vector2Int.Zero;
        public Vector2 WorldPosition = Vector2.Zero;
        public Vector2 WorldSize = Vector2.Zero;
        public Camera? WorldCamera = null;

        public CanvasData ToData() => new()
        {
            Type = Type,
            Color = Color,
            ColorOffset = ColorOffset,
            WorldPosition = WorldPosition,
            WorldSize = WorldSize
        };
    }

    public struct TextBoxStyle()
    {
        public float Height = 28f;
        public float FontSize = 16;
        public Color BackgroundColor = new(0.22f, 0.22f, 0.22f, 1f);
        public Color TextColor = Color.White;
        public Color PlaceholderColor = new(0.4f, 0.4f, 0.4f, 1f);
        public Color SelectionColor = new(0.2f, 0.4f, 0.8f, 0.5f);
        public BorderStyle Border = BorderStyle.None;

        public BorderStyle FocusBorder = BorderStyle.None;
        public bool IsPassword = false;

        public TextBoxData ToData() => new()
        {
            Height = Height,
            FontSize = FontSize,
            BackgroundColor = BackgroundColor,
            TextColor = TextColor,
            PlaceholderColor = PlaceholderColor,
            SelectionColor = SelectionColor,
            Border = Border,

            FocusBorder = FocusBorder,
            Password = IsPassword,
            TextStart = 0,
            TextLength = 0
        };
    }

    public static class ElementStyles
    {
        public static ContainerStyle WithColor(this ContainerStyle style, Color color)
        {
            style.Color = color;
            return style;
        }

        public static ContainerStyle WithSize(this ContainerStyle style, float width=float.MaxValue, float height=float.MaxValue)
        {
            style.Width = width;
            style.Height = height;
            return style;
        }

        public static ContainerStyle WithAlign(this ContainerStyle style, Align align)
        {
            style.AlignX = align;
            style.AlignY = align;
            return style;
        }

        public static ContainerStyle WithAlignX(this ContainerStyle style, Align alignX)
        {
            style.AlignX = alignX;
            return style;
        }

        public static ContainerStyle WithAlignY(this ContainerStyle style, Align alignY)
        {
            style.AlignY = alignY;
            return style;
        }

        public static ContainerStyle WithMargin(this ContainerStyle style, in EdgeInsets margin)
            {
            style.Margin = margin;
            return style;
        }

        public static ContainerStyle WithMinSize(this ContainerStyle style, float minWidth=0, float minHeight=0)
        {
            style.MinWidth = minWidth;
            style.MinHeight = minHeight;
            return style;
        }

        public static ContainerStyle WithMaxSize(this ContainerStyle style, float maxWidth=float.MaxValue, float maxHeight=float.MaxValue)
        {
            style.MaxWidth = maxWidth;
            style.MaxHeight = maxHeight;
            return style;
        }

        public static LabelStyle WithColor(this LabelStyle style, Color color)
        {
            style.Color = color;
            return style;
        }
    }
}
