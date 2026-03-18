//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static class EditorStyle
{
    // :palette — Colors from Components.pen design system (cool greys)
    public static class Palette
    {
        // Surface Backgrounds (darkest → lightest)
        public static readonly Color Popup = Color.FromRgb(0x161619);
        public static readonly Color Control = Color.FromRgb(0x191A1D);
        public static readonly Color Panel = Color.FromRgb(0x232428);
        public static readonly Color Separator = Color.FromRgb(0x2A2B30);
        public static readonly Color Canvas = Color.FromRgb(0x2F3036);
        public static readonly Color Active = Color.FromRgb(0x393A41);

        // Primary
        public static readonly Color Primary = Color.FromRgb(0xE83A3A);
        public static readonly Color PrimaryHover = Color.FromRgb(0xF04848);
        public static readonly Color PrimaryDisabled = Color.FromRgba(0xE83A3A44);

        // Path editor
        public static readonly Color PathSegment = Color.FromRgb(0x000000);
        public static readonly Color PathAnchor = Color.FromRgb(0x000000);

        // Text & Icon (3-tier: primary, secondary, disabled)
        public static readonly Color Content = Color.FromRgb(0xE8E8E8);
        public static readonly Color SecondaryText = Color.FromRgb(0x999999);
        public static readonly Color Disabled = Color.FromRgb(0x666666);

        // State
        public static readonly Color TextSelection = Color.FromRgba(0xE83A3A44);
        public static readonly Color SelectionOutline = Color.FromRgba(0xE83A3A66);
        public static readonly Color SelectionFill = Color.FromRgba(0xE83A3A11);
        public static readonly Color FocusRing = Primary;
    }

    // Error color
    public static readonly Color ErrorColor = Color.FromRgb(0xdf6b6d);

    // Mesh (used by shape/skeleton editors)
    public const float MeshEdgeWidth = 0.02f;
    public const float MeshVertexSize = 0.12f;
    public const float MeshWeightOutlineSize = 0.20f;
    public const float MeshWeightSize = 0.19f;

    public static readonly ContainerStyle Panel = new()
    {
        Background = Palette.Panel,
        Spacing = Control.Spacing
    };

    // :icon
    public static class Icon
    {
        public const float Size = 9.0f;

        public static readonly ImageStyle Primary = new()
        {
            Color = Palette.Content,
            Size = Size,
            Align = Align.Center
        };

        public static readonly ImageStyle Secondary = Primary with { Color = Palette.SecondaryText };
    }

    // :text
    public static class Text
    {
        public const float Size = 11.0f;

        public readonly static TextStyle Primary = new()
        {
            FontSize = Size,
            Color = Palette.Content,
            AlignX = Align.Min,
            AlignY = Align.Center
        };

        public readonly static TextStyle Secondary = Primary with { Color = Palette.SecondaryText };
        public readonly static TextStyle Disabled = Primary with { Color = Palette.Disabled };
        public readonly static TextStyle SecondarySmall = Secondary with { FontSize = 9.0f };
    }

    // :workspace
    public static class Workspace
    {
        public static readonly Color SelectionColor = Palette.SelectionOutline;
        public static readonly Color FillColor = Palette.Canvas;
        public static readonly Color GridColor = Palette.Active;
        public static readonly Color BoundsColor = Color.FromRgb(0x212121);
        public static readonly Color OriginColor = Color.FromRgb(0xff9f2c);
        public const float XrayAlpha = 0.5f;
        public const float OriginSize = 0.1f;
        public const float DocumentBoundsLineWidth = 0.015f;
        public static readonly Color NameColor = Color.White;
        public static readonly Color NameOutlineColor = Color.Black;
        public static readonly Color SelectedNameColor = Palette.Primary;
        public const float NameSize = 0.36f;
        public const float NamePadding = 0.26f;
        public const float NameOutline = 0.15f;
        public const float GridAlpha = 0.4f;
        public const float GridZeroAlpha = 0.5f;
        public const float Padding = 16f;
        public static readonly Color ReferenceLineColor = new(0.5f, 0.8f, 1f, 0.5f);
    }

    // :control — Global control dimensions
    public static class Control
    {
        public const float TextSize = EditorStyle.Text.Size;
        public const float IconSize = EditorStyle.Icon.Size;
        public const float Height = 27.0f;
        public const float BorderRadius = 3.0f;
        public const float Spacing = 4.0f;
        public const float ContentPadding = 3.0f;

        public static readonly ContainerStyle Root = new()
        {
            Height = Height,
            Spacing = Spacing,
            Padding = EdgeInsets.Symmetric(ContentPadding, ContentPadding * 2)
        };

        public static readonly ContainerStyle RootHovered = Root with
        {
            BorderWidth = 1,
            BorderColor = Palette.FocusRing,
            BorderRadius = BorderRadius
        };

        public static readonly ContainerStyle Fill = new()
        {
            Background = Palette.Control,
            BorderRadius = BorderRadius,
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Disabled) != 0) return s with { Background = Color.Transparent };
                if ((f & WidgetFlags.Checked) != 0) return s with { Background = Palette.Active };
                if ((f & WidgetFlags.Hovered) != 0) return s with { Background = Palette.Active };
                return s;
            },
        };

        public static readonly ContainerStyle Content = new()
        {
            Padding = EdgeInsets.All(ContentPadding),
            Spacing = Spacing
        };

        // Legacy — used by popup system (EditorUI.cs) and ColorPicker
        public static readonly ContainerStyle HoverFill = Fill with { Background = Palette.Active, Resolve = null };
        public static readonly ContainerStyle SelectedFill = Fill with { Background = Palette.Active, Resolve = null };

        public readonly static TextStyle Text = new()
        {
            FontSize = TextSize,
            Color = Palette.Content,
            AlignX = Align.Min,
            AlignY = Align.Center,
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Disabled) != 0) return s with { Color = Palette.Disabled };
                return s;
            },
        };

        public readonly static TextStyle DisabledText = Text with { Color = Palette.Disabled, Resolve = null };
        public readonly static TextStyle HoveredText = Text with { Color = Palette.Content, Resolve = null };
        public readonly static TextStyle SelectedText = Text with { Color = Palette.Content, Resolve = null };

        public readonly static TextStyle PlaceholderText = new()
        {
            FontSize = TextSize,
            Color = Palette.Disabled,
            AlignX = Align.Min,
            AlignY = Align.Center,
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Checked) != 0) return s with { Color = Palette.Content };
                if ((f & WidgetFlags.Hovered) != 0) return s with { Color = Palette.SecondaryText };
                return s;
            },
        };

        public static readonly ImageStyle Icon = new()
        {
            Color = Palette.Content,
            Size = IconSize,
            AlignX = Align.Center,
            AlignY = Align.Center,
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Disabled) != 0) return s with { Color = Palette.Disabled };
                return s;
            },
        };

        public static readonly ImageStyle IconSecondary = Icon with { Color = Palette.SecondaryText };

        // Legacy — used by popup system (EditorUI.cs)
        public static readonly ImageStyle SelectedIcon = Icon with { Color = Palette.Content, Resolve = null };
        public static readonly ImageStyle DisabledIcon = Icon with { Color = Palette.Disabled, Resolve = null };
        public static readonly ImageStyle HoveredIcon = Icon with { Color = Palette.Content, Resolve = null };
    }

    // :popup
    public static class Popup
    {
        public static readonly PopupStyle Left = new()
        {
            AnchorX = Align.Min,
            AnchorY = Align.Min,
            Spacing = 2.0f,
            PopupAlignX = Align.Max,
            PopupAlignY = Align.Min,
            ClampToScreen = true,
        };

        public readonly static ContainerStyle Root = new()
        {
            AlignX = Align.Center,
            AlignY = Align.Center,
            Padding = EdgeInsets.All(3),
            Background = Palette.Popup,
            BorderRadius = Control.BorderRadius,
        };
        public readonly static ContainerStyle Item = Control.Root;
        public readonly static ContainerStyle ItemContent = Control.Content;
        public readonly static ContainerStyle TitleItem = Item with
        {
            Padding = EdgeInsets.LeftRight(4.0f)
        };
        public readonly static ContainerStyle Separator = new() {
            Height = 1,
            Margin = EdgeInsets.Symmetric(2, 4),
            Background = Palette.Separator
        };
        public readonly static TextStyle Title = new()
        {
            FontSize = Control.TextSize,
            AlignX = Align.Min,
            AlignY = Align.Center,
            Color = Palette.SecondaryText,
        };

        public readonly static ContainerStyle CheckContent = new()
        {
            Width = Control.Height / 2,
            AlignY = Align.Center
        };
    }

    // :button
    public static class Button
    {
        public const float IconSize = 9.0f;

        public static readonly ButtonStyle Primary = new()
        {
            Width = Size.Fit,
            MinWidth = 54.0f,
            Height = Control.Height,
            Background = Palette.Primary,
            ContentColor = Palette.Content,
            FontSize = Control.TextSize,
            IconSize = IconSize,
            Spacing = Control.Spacing,
            BorderRadius = Control.BorderRadius,
            Padding = EdgeInsets.LeftRight(8),
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Disabled) != 0) return s with
                {
                    Background = Palette.PrimaryDisabled,
                    ContentColor = Palette.Disabled
                };
                if ((f & WidgetFlags.Hovered) != 0) return s with
                {
                    Background = Palette.PrimaryHover
                };
                return s;
            },
        };

        public static readonly ButtonStyle Secondary = new()
        {
            Width = Size.Fit,
            MinWidth = 54.0f,
            Height = Control.Height,
            Background = Palette.Canvas,
            ContentColor = Palette.SecondaryText,
            FontSize = Control.TextSize,
            IconSize = IconSize,
            Spacing = Control.Spacing,
            BorderRadius = Control.BorderRadius,
            Padding = EdgeInsets.LeftRight(8),
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Disabled) != 0) return s with { Background = Palette.Canvas, ContentColor = Palette.Disabled };
                if ((f & WidgetFlags.Hovered) != 0) return s with { Background = Palette.Active, ContentColor = Palette.Content };
                return s;
            },
        };

        // Toggle icon button (27x27, checked = canvas bg, hover = border)
        public static readonly ButtonStyle ToggleIcon = new()
        {
            Width = Control.Height,
            Height = Control.Height,
            Background = Color.Transparent,
            ContentColor = Palette.Content,
            IconSize = Control.IconSize,
            BorderRadius = Control.BorderRadius,
            Padding = EdgeInsets.All(3),
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Checked) != 0) s.Background = Palette.Canvas;
                if ((f & WidgetFlags.Hovered) != 0) { s.BorderWidth = 1; s.BorderColor = Palette.FocusRing; }
                return s;
            },
        };

        // Icon-only button (27x27, hover = Active bg)
        public static readonly ButtonStyle IconOnly = new()
        {
            Width = Control.Height,
            Height = Control.Height,
            Background = Color.Transparent,
            ContentColor = Palette.Content,
            IconSize = Control.IconSize,
            BorderRadius = Control.BorderRadius,
            Padding = EdgeInsets.All(3),
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Hovered) != 0) s.Background = Palette.Active;
                return s;
            },
        };
    }

    public static DropDownStyle DropDown = new()
    {
        Width = Size.Default,
        Height = Control.Height,
        Color = Palette.Control,
        ContentColor = Palette.Content,
        IconColor = Palette.SecondaryText,
        FontSize = Control.TextSize,
        IconSize = Control.IconSize,
        ArrowSize = 9.0f,
        Spacing = Control.Spacing,
        BorderRadius = Control.BorderRadius,
        Padding = EdgeInsets.LeftRight(7),
        ArrowIcon = null,
        Resolve = (s, f) =>
        {
            if ((f & WidgetFlags.Disabled) != 0) return s with
            {
                Color = Palette.Control,
                ContentColor = Palette.Disabled
            };
            if ((f & WidgetFlags.Checked) != 0) return s with { Color = Palette.Active, IconColor = Palette.Content };
            if ((f & WidgetFlags.Hovered) != 0) return s with { Color = Palette.Active, IconColor = Palette.Content };
            return s;
        },
    };

    private static readonly Func<TextInputStyle, WidgetFlags, TextInputStyle> TextInputResolve = (s, f) =>
    {
        if (f.HasFlag(WidgetFlags.Hot) || f.HasFlag(WidgetFlags.Hovered))
            s.BorderColor = Palette.FocusRing;
        return s;
    };

    public static readonly TextInputStyle TextInput = new()
    {
        Height = Control.Height,
        FontSize = Control.TextSize,
        TextColor = Palette.Content,
        PlaceholderColor = Palette.Disabled,
        SelectionColor = Palette.TextSelection,
        BackgroundColor = Palette.Control,
        BorderRadius = Control.BorderRadius,
        BorderWidth = 1.0f,
        BorderColor = Palette.Control,
        Padding = EdgeInsets.Symmetric(4, 5),
        IconSize = Control.IconSize,
        IconColor = Palette.SecondaryText,
        LabelFontSize = 9,
        LabelColor = Palette.SecondaryText,
        Resolve = TextInputResolve,
    };

    public static readonly TextInputStyle TextArea = TextInput with
    {
        Height = Size.Fit,
        MinHeight = Control.Height,
        BorderRadius = Control.BorderRadius,
        Resolve = TextInputResolve,
    };

    public static class List
    {
        public const float ItemHeight = 27f;
        public const float ItemPadding = 6f;
        public static readonly Color ItemSelectedFillColor = Palette.Active;
        public static readonly Color ItemSelectedTextColor = Palette.Content;
        public static readonly Color ItemTextColor = Palette.Content;
        public static readonly Color HeaderTextColor = Palette.Content;
    }

    public static class BoxSelect
    {
        public const float LineWidth = 0.02f;
        public static readonly Color LineColor = Palette.Primary;
        public static readonly Color FillColor = Palette.SelectionFill;
    }

    public static class CommandPalette
    {
        public static readonly ContainerStyle Root = Popup.Root with
        {
            Width = 340.0f,
            Height = Size.Fit,
            MinHeight = 100.0f,
            Spacing = 2.0f
        };

        public static readonly TextInputStyle SearchTextBox = new()
        {
            FontSize = Control.TextSize,
            TextColor = Palette.Content,
            SelectionColor = Palette.TextSelection,
            PlaceholderColor = Palette.Disabled,
            IconSize = Control.IconSize,
            IconColor = Palette.SecondaryText
        };

        public static readonly ContainerStyle CommandList = new()
        {
            Height = List.ItemHeight * 10,
            Padding = EdgeInsets.Symmetric(3, 0)
        };

        public static readonly ContainerStyle Item = new()
        {
            Height = List.ItemHeight,
            Padding = EdgeInsets.LeftRight(8),
            Spacing = 6.0f
        };

        public static readonly ContainerStyle SelectedItem = Item with
        {
            Background = Palette.Active,
            BorderRadius = Control.BorderRadius
        };

        public static readonly ScrollBarStyle ScrollBar = new()
        {
            Width = 6f,
            MinThumbHeight = 20f,
            TrackColor = Color.Transparent,
            ThumbColor = Palette.Active,
            BorderRadius = 3f,
            Padding = 2f,
            Visibility = ScrollBarVisibility.Auto
        };
    }

    // :assetpalette
    public static class AssetPalette
    {
        public static readonly ContainerStyle Root = Popup.Root with
        {
            Width = 340.0f,
            Height = Size.Fit,
            MinHeight = 100.0f,
            Spacing = 2.0f
        };

        public static readonly ContainerStyle ListContainer = new()
        {
            Height = List.ItemHeight * 10,
            Padding = EdgeInsets.Symmetric(3, 0)
        };

        public static readonly ContainerStyle GridContainer = new()
        {
            Height = 280.0f,
            Padding = EdgeInsets.Symmetric(3, 0)
        };
    }

    // :shape
    public static class Shape
    {
        public const float AnchorSize = 0.14f;
        public const float AnchorHitSize = AnchorSize * 2.0f;
        public const float SegmentLineWidth = 0.02f;
        public const float SegmentHitSize = SegmentLineWidth * 12.0f;

        public static readonly Color ControlPointColor = Color.FromRgb(0xff7900);
        public static readonly Color ControlPointLineColor = Color.FromRgb(0xfd970e);
        public const float ControlPointSize = 0.06f;
        public const float ControlPointLineWidth = 0.01f;
    }

    // :skeleton
    public static class Skeleton
    {
        public static readonly Color BoneColor = Color.FromRgb(0x212121);
        public static readonly Color SelectedBoneColor = Palette.Primary;
        public const float BoneLineWidth = 0.015f;
        public const float BoneBaseRatio = 0.08f;
        public const float BoneHitThreshold = 0.2f;

        public static readonly Color JointColor = Color.Black;
        public const float JointSize = 0.22f;
        public const float JointHitSize = JointSize * 2.0f;

        public static readonly Color ParentLineColor = Color.FromRgb(0x212121);
    }

    // :toolbar
    public static class Toolbar
    {
        public static readonly ContainerStyle Root = new()
        {
            Padding = EdgeInsets.Symmetric(3, Control.Spacing),
            Spacing = Control.Spacing,
            Height = Size.Fit,
            Margin = EdgeInsets.Top(3)
        };

        public static readonly ContainerStyle Spacer = new()
        {
            Width = 1,
            Background = Palette.Active,
            Margin = EdgeInsets.LeftRight(2)
        };
    }

    public static class Shortcut
    {
        public static readonly ContainerStyle ListContainer = new()
        {
            Spacing = 2.0f,
            AlignX = Align.Min,
            AlignY = Align.Center,
            Width = Size.Fit
        };

        public static readonly ContainerStyle RootContainer = new()
        {
            MinWidth = 18.0f,
            Height = 18.0f,
            AlignX = Align.Center,
            AlignY = Align.Center,
            Padding = EdgeInsets.LeftRight(5),
            BorderRadius = Control.BorderRadius
        };

        public static readonly TextStyle Text = Control.Text with { Color = Palette.SecondaryText };
    }

    // :contextmenu
    public static class ContextMenu
    {
        public const float SeparatorHeight = 1f;
        public const float SeparatorSpacing = 4f;
        public static readonly Color TitleColor = Palette.Disabled;
        public static readonly Color SeparatorColor = Palette.Separator;
        public static readonly ContainerStyle Menu = Popup.Root with {
            Size = Size2.Fit,
            AlignX = Align.Min,
            AlignY = Align.Min,
        };

        public static readonly ContainerStyle Item = Popup.Item with
        {
            Width = Size.Default,
            MinWidth = 140.0f,
            Spacing = 0.0f,
            Padding = EdgeInsets.LeftRight(4.0f, 4.0f)
        };

        public static readonly ContainerStyle ItemRight = ContainerStyle.Default with
        {
            Width = 80f
        };

        public static PopupMenuStyle Style = new()
        {
            BackgroundColor = Palette.Popup,
            BorderRadius = Control.BorderRadius,
            BorderWidth = 0,
            BorderColor = Color.Transparent,
            Padding = EdgeInsets.Symmetric(4, 0),
            MinWidth = 180,
            ItemHeight = Control.Height,
            ItemPadding = EdgeInsets.LeftRight(10.0f, 10.0f),
            ItemContentPadding = Control.ContentPadding,
            ItemContentSpacing = 6,
            ItemHoverColor = Palette.Active,
            FontSize = Control.TextSize,
            TextColor = Palette.Content,
            DisabledTextColor = Palette.Disabled,
            IconSize = Control.IconSize,
            CheckWidth = Control.Height / 2,
            SeparatorHeight = 1,
            SeparatorMargin = EdgeInsets.Symmetric(2, 4),
            SeparatorColor = Palette.Separator,
            TitleFontSize = Control.TextSize,
            TitleColor = Palette.SecondaryText,
            TitlePadding = EdgeInsets.LeftRight(10.0f),
            ShortcutColor = Palette.SecondaryText,
            SubmenuSpacing = Control.Spacing,
        };
    }

    // :notification
    public static class Notifications
    {
        public readonly static ContainerStyle Wrapper = new()
        {
            AlignX = Align.Max,
            AlignY = Align.Max,
            Padding = EdgeInsets.BottomRight(Workspace.Padding),
        };

        public readonly static ContainerStyle Root = new()
        {
            Width = 260.0f,
            Height = Size.Fit,
            Spacing = Control.Spacing,
        };

        public readonly static ContainerStyle Notification = new()
        {
            Height = Control.Height,
            Padding = EdgeInsets.Symmetric(0, 10),
            Spacing = 6.0f,
            Background = Palette.Popup,
            BorderRadius = Control.BorderRadius
        };

        public const float IconSize = 12.0f;

        public readonly static ImageStyle InfoIcon = new()
        {
            Color = Palette.SecondaryText,
            Size = IconSize,
            Align = Align.Center
        };

        public readonly static ImageStyle ErrorIcon = InfoIcon with { Color = Palette.Primary };

        public static readonly Color WarningColor = Color.FromRgb(0xD49300);
        public static readonly Color SuccessColor = Color.FromRgb(0x2ECC71);

        public readonly static ImageStyle WarningIcon = InfoIcon with { Color = WarningColor };
        public readonly static ImageStyle SuccessIcon = InfoIcon with { Color = SuccessColor };

        public readonly static TextStyle NotificationText = new()
        {
            FontSize = Control.TextSize,
            Color = Palette.SecondaryText,
            AlignX = Align.Min,
            AlignY = Align.Center
        };
    }

    // :confirm
    public static class Confirm
    {
        public static readonly ContainerStyle Backdrop = new()
        {
            AlignX = Align.Center,
            AlignY = Align.Center,
            Background = new Color(0, 0, 0, 0.4f),
        };

        public static readonly ContainerStyle Root = Popup.Root with
        {
            AlignX = Align.Center,
            Width = Size.Fit,
            Height = Size.Fit,
            Spacing = 10,
            Padding = EdgeInsets.Symmetric(14, 16)
        };

        public static readonly TextStyle MessageLabel = Control.Text;

        public static readonly ContainerStyle ButtonContainer = new()
        {
            AlignX = Align.Center,
            Width = Size.Fit,
            Height = Control.Height,
            Spacing = 6
        };
    }

    // :documenteditor
    public static class DocumentEditor
    {
        public static readonly ContainerStyle Root = new()
        {
            Background = Palette.Control,
            AlignY = Align.Max,
            Height = Size.Fit
        };
    }

    // :inspector
    public static class Inspector
    {
        public const float FontSize = 11f;
        public const float LabelFontSize = 9f;
        public const float BorderRadius = Control.BorderRadius;
        public const float BodyPaddingV = 8f;
        public const float BodyPaddingH = 8f;
        public const float BodyGap = 4f;
        public const float HeaderGap = 4f;
        public const float LabelWidth = 60f;
        public const float SectionHeaderHeight = Control.Height;

        public static readonly ContainerStyle Root = Panel with
        {
            Width = 300.0f,
            Padding = Control.Spacing
        };

        public static readonly ContainerStyle SectionHeader = new()
        {
            Height = SectionHeaderHeight,
            Background = Palette.Separator,
            Padding = EdgeInsets.LeftRight(5),
            Spacing = HeaderGap,
        };

        public static readonly ButtonStyle SectionButton = new()
        {
            Width = Control.Height - Control.Spacing,
            Height = Control.Height - Control.Spacing,
            Background = Color.Transparent,
            ContentColor = Palette.Content,
            IconSize = Control.IconSize,
            BorderRadius = Control.BorderRadius,
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Hovered) != 0) s.Background = Palette.Active;
                return s;
            },
        };

        public static readonly ImageStyle ChevronIcon = new()
        {
            Color = Palette.SecondaryText,
            Size = Control.IconSize,
            AlignX = Align.Center,
            AlignY = Align.Center
        };

        public static readonly ImageStyle SectionIcon = ChevronIcon;

        public static readonly ContainerStyle SectionHeaderActive = SectionHeader with
        {
            Background = Palette.Active
        };

        public static readonly ContainerStyle Section = new()
        {
            Height = Size.Fit,
        };

        public static readonly ContainerStyle Property = new()
        {
            Height = Control.Height,
        };

        public static readonly TextStyle PropertyName = new()
        {
            FontSize = LabelFontSize,
            Color = Palette.SecondaryText,
            AlignX = Align.Min,
            AlignY = Align.Center
        };

        public static readonly ContainerStyle ListItem = new()
        {
            Height = Control.Height,
            Padding = EdgeInsets.LeftRight(BodyGap),
            Spacing = HeaderGap,
            BorderRadius = BorderRadius,
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Hovered) != 0)
                    s.Background = Palette.Active;
                return s;
            },
        };

        public static readonly ContainerStyle Content = new()
        {
            Spacing = Control.Spacing,
            Padding = EdgeInsets.Symmetric(3, Control.Spacing)
        };

        public static readonly ContainerStyle Row = new()
        {
            Height = Size.Fit,
            MinHeight = Control.Height,
            Padding = EdgeInsets.LeftRight(3),
            Spacing = BodyGap
        };

        public static readonly TextInputStyle TextBox = EditorStyle.TextInput;
        public static readonly TextInputStyle TextArea = EditorStyle.TextArea;

        public static readonly ToggleStyle Toggle = new()
        {
            Size = 16,
            IconSize = Control.IconSize,
            Spacing = 6,
            FontSize = Control.TextSize,
            BorderRadius = Control.BorderRadius,
            BorderWidth = 0,
            Color = Palette.Control,
            CheckedColor = Palette.Control,
            BorderColor = Color.Transparent,
            ContentColor = Palette.Content,
            CheckColor = Palette.Content,
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Hovered) != 0)
                    s.Color = Palette.Active;
                return s;
            },
        };

        public static readonly ContainerStyle FieldContainer = new()
        {
            Width = 60f,
            AlignY = Align.Center
        };

        public static readonly TextStyle Label = new()
        {
            FontSize = LabelFontSize,
            Color = Palette.SecondaryText,
            AlignX = Align.Min,
            AlignY = Align.Center
        };

        public static readonly ContainerStyle Separator = new()
        {
            Height = 1,
            Margin = EdgeInsets.Symmetric(Control.Spacing / 2, 0),
            Background = Palette.Separator
        };

        public static readonly ContainerStyle EmitterTab = new()
        {
            Height = Control.Height,
            Padding = EdgeInsets.LeftRight(7),
            BorderRadius = BorderRadius
        };

        public static readonly ContainerStyle EmitterTabFill = new()
        {
            Background = Palette.Control,
            BorderRadius = BorderRadius
        };

        public static readonly ContainerStyle EmitterTabSelected = EmitterTabFill with
        {
            Background = Palette.Active
        };

        public static readonly ContainerStyle EmitterTabHover = EmitterTabFill with
        {
            Background = Palette.Separator
        };

        public static readonly TextStyle EmitterTabText = new()
        {
            FontSize = FontSize,
            Color = Palette.Content,
            AlignX = Align.Center,
            AlignY = Align.Center
        };
    }

    // :slider
    public static class Slider
    {
        public const float TrackHeight = 4;
        public const float ThumbSize = 10;

        public static readonly SliderStyle Style = new()
        {
            Height = Control.Height,
            TrackHeight = TrackHeight,
            ThumbSize = ThumbSize,
            TrackColor = Palette.Control,
            FillColor = Palette.Primary,
            ThumbColor = Palette.Content,
            Step = 0.05f,
        };
    }

    // :spriteeditor
    public static class SpriteEditor
    {
        public const float LayerColumnWidth = 140f;

        public static readonly ContainerStyle Toolbar = new()
        {
            Height = Control.Height,
        };

        public static readonly ContainerStyle LayerToolbar = new()
        {
            Width = LayerColumnWidth,
            Height = Control.Height,
        };

        public static readonly ContainerStyle LayerRow = new()
        {
            Height = Dopesheet.FrameHeight,
            Background = Palette.Canvas,
        };

        public static readonly ContainerStyle LayerNameContainer = new()
        {
            Width = LayerColumnWidth,
            Padding = EdgeInsets.LeftRight(3),
        };

        public static readonly ContainerStyle LayerNameContainerActive = LayerNameContainer with
        {
            Background = Palette.Primary
        };

        public static readonly Color UndefinedColor = new(0f, 0f, 0f, 0.1f);
        public const float ButtonSize = 27f;
        public const float ButtonMarginY = 4f;
        public const float ColorPickerBorderWidth = 2.0f;
        public const float ColorSize = Control.Height;
        public const float ColorPickerWidth = ColorSize * 64 + ColorPickerBorderWidth * 2;
        public const float ColorPickerHeight = ColorSize + ColorPickerBorderWidth * 2;
        public const float ColorPickerSelectionBorderWidth = 2f;
        public static readonly Color BoneOriginColor = Color.White;

        public static readonly ContainerStyle ColorPicker = new()
        {
            Padding = EdgeInsets.All(3f),
            BorderRadius = Control.BorderRadius
        };

        public static readonly ContainerStyle PaletteColor = new()
        {
            Width = ColorSize,
            Height = ColorSize,
            Padding = EdgeInsets.All(1.0f),
        };

        public static readonly ContainerStyle PaletteSelectedColor = new()
        {
            BorderRadius = Control.BorderRadius,
            Background = Palette.Primary,
            Margin = EdgeInsets.All(-1.5f),
        };

        public static readonly ContainerStyle PaletteDisplayColor = new()
        {
            BorderRadius = Control.BorderRadius
        };

        public static readonly ContainerStyle OpacityButtonRoot = new()
        {
            Margin = EdgeInsets.Left(Control.Spacing),
            Width = ColorSize * 2,
            Height = ColorSize * 2,
            AlignY = Align.Max
        };

        public static readonly ContainerStyle OpacityButtonIconContainer = new()
        {
            Padding = EdgeInsets.All(4)
        };

        public static readonly PopupStyle OpacityPopup = new()
        {
            AnchorX = Align.Min,
            AnchorY = Align.Min,
            PopupAlignX = Align.Min,
            PopupAlignY = Align.Max,
            Spacing = 2,
            ClampToScreen = true
        };

        public static readonly PopupStyle PalettePopup = new()
        {
            AnchorX = Align.Max,
            AnchorY = Align.Min,
            PopupAlignX = Align.Max,
            PopupAlignY = Align.Max,
            Spacing = 2,
            ClampToScreen = true
        };

        public readonly static ContainerStyle OpacityPopupRoot = Popup.Root;

        public readonly static ContainerStyle ConstraintsPopupRoot = Popup.Root;
    }

    // :colorpicker
    public static class ColorPicker
    {
        private const float Width = 200;
        private const float Padding = 5;
        public const float SVSize = Width - 4;
        public const float SliderWidth = Width - Padding * 2;
        public const float SliderHeight = 13;

        public readonly static ContainerStyle Root = Popup.Root with
        {
            Spacing = Control.Spacing * 2,
            Padding = Padding,
            Width = Width,
            Height = Size.Fit
        };

        public readonly static ContainerStyle SaturationAndValue = new()
        {
            Size = SVSize,
            Margin = EdgeInsets.LeftRight(-(Padding - 2)),
            Clip = true
        };

        public readonly static ContainerStyle Slider = new()
        {
            Height = SliderHeight,
            Clip = true,
        };

        public readonly static ImageStyle SliderImage = ImageStyle.Fill with { BorderRadius = 7 };
    }

    // :dopesheet
    public static class Dopesheet
    {
        public const float FrameWidth = 12.0f;
        public const float FrameHeight = Control.Height;
        public const float FrameSpacerWidth = 1.0f;

        public static readonly ContainerStyle FrameDot = new()
        {
            Width = 6f,
            Height = 6f,
            AlignX = Align.Center,
            AlignY = Align.Max,
            Margin = EdgeInsets.Bottom(4f),
            Background = Palette.Separator,
            BorderRadius = 4
        };

        public static readonly ContainerStyle SelectedFrameDot = FrameDot with { Background = Color.Black };

        public static readonly ContainerStyle HeaderContainer = new()
        {
            Height = Control.Height
        };

        public static readonly ContainerStyle TimeBlock = new()
        {
            Width = FrameWidth * 4 + (FrameSpacerWidth * 3),
            Padding = EdgeInsets.BottomLeft(0, 2)
        };

        public static readonly TextStyle TimeText = Text.Secondary with
        {
            FontSize = 9.0f,
            AlignY = Align.Max
        };

        public static readonly ContainerStyle FrameContainer = new()
        {
            Height = FrameHeight,
            Background = Palette.Canvas
        };

        public static readonly ContainerStyle Frame = new()
        {
            Width = FrameWidth,
            Background = Color.FromRgb(0x909090)
        };

        public static readonly ContainerStyle SelectedFrame = Frame with
        {
            Background = Palette.Primary
        };

        public static readonly ContainerStyle FrameSeparator = new()
        {
            Width = 1,
            Background = Palette.Separator
        };

        public static readonly ContainerStyle HoldSeparator = new()
        {
            Width = 1,
            Background = Frame.Background
        };

        public static readonly ContainerStyle SelectedHoldSeparator = HoldSeparator with
        {
            Background = SelectedFrame.Background
        };

        public static readonly ContainerStyle EmptyFrame = Frame with
        {
            Background = Color.Transparent
        };

        public static readonly ContainerStyle FourthEmptyFrame = EmptyFrame with
        {
            Background = Color.FromRgb(0x3a3a3a)
        };

        public static readonly ContainerStyle LayerSeparator = new()
        {
            Height = FrameSpacerWidth,
            Background = Palette.Separator
        };
    }

    // :tool
    public static class Tool
    {
        public static readonly Color LineColor = Color.FromRgba(0xf5f04e, 0.7f);
        public static readonly Color PointColor = Color.FromRgb(0x63ffff);
        public const float PointSize = Shape.AnchorSize * 1.5f;
    }

    // :knifetool
    public static class KnifeTool
    {
        public static readonly Color AnchorColor = Color.FromRgb(0x000000);
        public static readonly Color IntersectionColor = Color.FromRgb(0x4ea64e);
        public static readonly Color InvalidSegmentColor = Color.FromRgb(0x953d49);
        public const float IntersectionAnchorScale = 1.2f;
    }

    // :renametool
    public static class RenameTool
    {
        public static readonly ContainerStyle Root = new()
        {
            AlignX = Align.Min,
            AlignY = Align.Min,
            Width = 200.0f,
            Height = Control.Height + Control.ContentPadding * 2
        };

        public readonly static ContainerStyle Content = new()
        {
            Width = Size.Fit,
            Height = Size.Fit,
            AlignX = Align.Center,
            AlignY = Align.Center,
            Background = Palette.Popup,
            Padding = EdgeInsets.Symmetric(Control.ContentPadding, Control.ContentPadding * 2),
            BorderRadius = Control.BorderRadius,
        };

        public static readonly TextInputStyle Text = new()
        {
            Width = Size.Fit,
            FontSize = 11.0f,
            TextColor = Control.Text.Color,
            SelectionColor = Palette.TextSelection
        };
    }

    public static void Init()
    {
        ContextMenu.Style.CheckIcon = EditorAssets.Sprites.IconCheck;
        ContextMenu.Style.SubmenuIcon = EditorAssets.Sprites.IconSubmenu;
        DropDown.ArrowIcon = EditorAssets.Sprites.IconFoldoutClosed;

        Style.DropDown = DropDown;
        Style.PopupMenu = ContextMenu.Style;
        Style.TextInput = EditorStyle.TextInput;
    }

    public static void Shutdown()
    {
    }
}
