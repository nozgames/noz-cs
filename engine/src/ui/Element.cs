//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace NoZ;

internal enum ElementType : byte
{
    Widget,
    Size,
    Padding,
    Fill,
    Margin,
    Row,
    Column,
    Flex,
    Align,
    Clip,
    Spacer,
    Opacity,
    Text,
    Image,
    EditableText,
    Popup,
    Cursor,
    Transform,
    Collection,
    Scene,
    Scroll,
    Track,
    FlexSplitter,
}

internal struct Element
{
    public ElementType Type;
    public ushort Index;
    public ushort Parent;
    public ushort NextSibling;
    public ushort FirstChild;
    public ushort ChildCount;
    public Rect Rect;
    public ElementData Data;
    public Matrix3x2 Transform;
}

[StructLayout(LayoutKind.Explicit)]
internal struct ElementData
{
    [FieldOffset(0)] public SizeElement Size;
    [FieldOffset(0)] public WidgetElement Widget;
    [FieldOffset(0)] public FillElement Fill;
    [FieldOffset(0)] public EditableTextElement EditableText;
    [FieldOffset(0)] public EdgeInsets Margin;
    [FieldOffset(0)] public EdgeInsets Padding;
    [FieldOffset(0)] public Align2 Align;
    [FieldOffset(0)] public ClipElement Clip;
    [FieldOffset(0)] public float Opacity;
    [FieldOffset(0)] public CursorElement Cursor;
    [FieldOffset(0)] public TransformElement Transform;
    [FieldOffset(0)] public CollectionElement Collection;
    [FieldOffset(0)] public ScrollElement Scroll;
    [FieldOffset(0)] public TrackElement Track;
    [FieldOffset(0)] public float Spacing;
    [FieldOffset(0)] public float Flex;
    [FieldOffset(0)] public PopupElement Popup;
    [FieldOffset(0)] public TextElement Text;
    [FieldOffset(0)] public ImageElement Image;
    [FieldOffset(0)] public SceneElement Scene;
    [FieldOffset(0)] public FlexSplitterElement FlexSplitter;
}

internal struct SizeElement
{
    public Size2 Size;
    public float MinWidth;
    public float MaxWidth;
    public float MinHeight;
    public float MaxHeight;
}

internal struct WidgetElement
{
    public WidgetId Id;
    public UnsafeSpan<byte> State;
    public ushort LastFrame;
    public bool IsInteractive;
}

internal struct FillElement
{
    public Color Color;
    public Color GradientColor;
    public float GradientAngle;
    public BorderRadius Radius;
    public float BorderWidth;
    public Color BorderColor;
    public Color ImageColor;
    public ushort ImageAsset;
    public bool HasGradient;
    public bool HasImage;
    public ImageStretch ImageStretch;
    public ushort Order;
}

internal struct FlexElement
{
    
}

internal struct ClipElement
{
    public BorderRadius Radius;
}

internal struct SpacerElement
{
    public Vector2 Size;
}

internal struct TextElement
{
    public UnsafeSpan<char> Text;
    public float FontSize;
    public Color Color;
    public Align2 Align;
    public TextOverflow Overflow;
    public ushort Font;
    public Color OutlineColor;
    public float OutlineWidth;
    public float OutlineSoftness;
}

internal struct ImageElement
{
    public Size2 Size;
    public ImageStretch Stretch;
    public Align2 Align;
    public float Scale;
    public Color Color;
    public Color OverlayColor;
    public float Width;
    public float Height;
    public ushort Asset;
}

internal struct CollectionElement
{
    public float Spacing;
    public int Columns;
    public float ItemWidth;
    public float ItemHeight;
    public int VirtualCount;
    public int StartIndex;
}

internal struct TransformElement
{
    public Vector2 Pivot;
    public Vector2 Translate;
    public float Rotate;
    public Vector2 Scale;
}

internal struct SceneElement
{
    public Size2 Size;
    public Color ClearColor;
    public int SampleCount;
    public bool PixelPerfect;
    public ushort Camera;
    public ushort DrawCallback;
}

internal unsafe struct ScrollElement
{
    public float ScrollSpeed;
    public ScrollState* State;
}

internal unsafe struct TrackElement
{
    public WidgetId Id;
    public float ThumbSizeX;
    public float ThumbSizeY;
    public TrackState* State;
}

internal struct CursorElement
{
    public SystemCursor SystemCursor;
    public ushort AssetIndex;
    public bool IsSprite;
    public float Rotation;
    public float HotspotX;
    public float HotspotY;
}

internal struct PopupElement
{
    public Rect AnchorRect;
    public float AnchorFactorX;
    public float AnchorFactorY;
    public float PopupAlignFactorX;
    public float PopupAlignFactorY;
    public float Spacing;
    public bool ClampToScreen;
    public bool AutoClose;
    public bool Interactive;
}

internal unsafe struct FlexSplitterElement
{
    public WidgetId Id;
    public float BarSize;
    public float MinSize;
    public float MaxSize;
    public float MinSize2;
    public float MaxSize2;
    public FlexSplitterState* State;
    public ushort PrevFlex;  // cached by layout: element index of prev flex sibling
    public ushort NextFlex;  // cached by layout: element index of next flex sibling
    public byte Axis;        // cached by layout: 0=horizontal, 1=vertical
}

internal unsafe struct EditableTextElement
{
    public EditableTextState* State;
    public UnsafeSpan<char> Text;
    public UnsafeSpan<char> Placeholder;
    public float FontSize;
    public Color TextColor;
    public Color CursorColor;
    public Color SelectionColor;
    public Color PlaceholderColor;
    public bool MultiLine;
    public bool Focused;
    public bool CommitOnEnter;
    public ushort Font;
    public int CursorIndex;
    public int SelectionStart;
    public float BlinkTime;
    public InputScope Scope;
}
