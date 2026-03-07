//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ.Platform;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

internal enum NewElementType : byte
{
    Widget,
    Size,
    Padding,
    Fill,
    Border,
    Margin,
    Row,
    Column,
    Flex,
    Align,
    Clip,
    Spacer,
    Opacity,
    Label,
    Image,
    EditableText,
    Popup,
}

internal struct BaseElement
{
    public NewElementType Type;
    public ushort Parent;
    public ushort NextSibling;
    public ushort FirstChild;
    public ushort ChildCount;
    public Rect Rect;
    public Matrix3x2 LocalToWorld;
    public Matrix3x2 WorldToLocal;
}

internal struct SizeElement
{
    public Size2 Size;
}

internal struct PaddingElement
{
    public EdgeInsets Padding;
}

internal struct WidgetElement
{
    public int Id;
    public ushort Data;
    public ElementFlags Flags;
}

internal struct FillElement
{
    public Color Color;
    public BorderRadius Radius;
}

internal struct BorderElement
{
    public float Width;
    public Color Color;
    public BorderRadius Radius;
}

internal struct MarginElement
{
    public EdgeInsets Margin;
}

internal struct RowElement
{
    public float Spacing;
}

internal struct ColumnElement
{
    public float Spacing;
}

internal struct FlexElement
{
    public float Flex;
}

internal struct AlignElement
{
    public Align2 Align;
}

internal struct ClipElement
{
    public BorderRadius Radius;
}

internal struct SpacerElement
{
    public Vector2 Size;
}

internal struct OpacityElement
{
    public float Opacity;
}

internal struct LabelElement
{
    public UnsafeSpan<char> Text;
    public float FontSize;
    public Color Color;
    public Align2 Align;
    public TextOverflow Overflow;
    public ushort AssetIndex;
}

internal struct ImageElement
{
    public Size2 Size;
    public ImageStretch Stretch;
    public Align2 Align;
    public float Scale;
    public Color Color;
    public float Width;
    public float Height;
    public ushort AssetIndex;
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
}

public static unsafe partial class ElementTree
{
    private const int MaxStateSize = 65535;
    private const int MaxElementSize = 65535;
    private const int MaxElementDepth = 64;
    private const int MaxId = 32000;
    private const int MaxAssets = 1024;
    private const int MaxVertices = 16384;
    private const int MaxIndices = 32768;

    private static NativeArray<byte> _elements;
    private static NativeArray<byte>[] _statePools = null!;
    private static int _currentStatePool;
    private static NativeArray<ushort> _elementStack;
    private static NativeArray<ushort> _widgets;
    private static int _elementStackCount;
    private static ushort _frame;
    private static ushort _nextSibling;
    private static ushort _currentWidget;

    private static readonly object?[] _assets = new object?[MaxAssets];
    private static int _assetCount;

    // Input state
    private static int _focusId;
    private static int _captureId;
    private static readonly WidgetInputState[] _widgetStates = new WidgetInputState[MaxId];

    // Drawing state (self-contained, not shared with UI)
    private static RenderMesh _mesh;
    private static NativeArray<UIVertex> _vertices;
    private static NativeArray<ushort> _indices;
    private static Shader _shader = null!;
    private static float _drawOpacity = 1.0f;

    internal struct WidgetInputState
    {
        public ElementFlags Flags;
        public ElementFlags PrevFlags;
        public ushort LastFrame;
        public int StateOffset;
        public ushort StateSize;
    }

    public static void Init()
    {
        _elements = new NativeArray<byte>(MaxElementSize);
        _statePools = [
            new NativeArray<byte>(MaxStateSize),
            new NativeArray<byte>(MaxStateSize)
        ];
        _elementStack = new NativeArray<ushort>(MaxElementDepth, MaxElementDepth);
        _widgets = new NativeArray<ushort>(MaxId, MaxId);

        _vertices = new NativeArray<UIVertex>(MaxVertices);
        _indices = new NativeArray<ushort>(MaxIndices);
        _mesh = Graphics.CreateMesh<UIVertex>(MaxVertices, MaxIndices, BufferUsage.Dynamic, "ElementTreeMesh");
        _shader = Asset.Get<Shader>(AssetType.Shader, "ui")!;
    }

    internal static void Shutdown()
    {
        _vertices.Dispose();
        _indices.Dispose();
        Graphics.Driver.DestroyMesh(_mesh.Handle);
    }

    internal static void Begin()
    {
        _frame++;
        _layoutCycleLogged = false;
        _currentStatePool ^= 1;
        _statePools[_currentStatePool].Clear();
        _assetCount = 0;
        _elements.Clear();
        _elementStackCount = 0;
        _currentWidget = 0;
    }

    internal static void End()
    {
        if (_elements.Length == 0) return;

        LayoutAxis(0, 0, ScreenSize.X, 0, -1);  // Width pass
        LayoutAxis(0, 0, ScreenSize.Y, 1, -1);  // Height pass
        UpdateTransforms(0, Matrix3x2.Identity, Vector2.Zero);
        HandleInput();
    }

    internal static ref BaseElement GetElement(int offset) =>
        ref *(BaseElement*)(_elements.Ptr + offset);

    private static UnsafeRef<T> GetElementData<T>(int offset) where T : unmanaged =>
        new((T*)(_elements.Ptr + offset + sizeof(BaseElement)));

    internal static ref T GetElementData<T>(ref BaseElement element) where T : unmanaged =>
         ref *(T*)((byte*)Unsafe.AsPointer(ref element) + sizeof(BaseElement));

    private static ref BaseElement AllocElement<T>(NewElementType type) where T : unmanaged
    {
        var size = sizeof(T) + sizeof(BaseElement);
        if (!_elements.CheckCapacity(size))
            throw new InvalidOperationException($"Element tree exceeded maximum size of {MaxElementSize} bytes.");

        return ref *(BaseElement*)_elements.AddRange(size).GetUnsafePtr();
    }

    private static ref BaseElement BeginElement<T>(NewElementType type) where T : unmanaged
    {
        ref var e = ref AllocElement<T>(type);
        BeginElementInternal(type, ref e);
        _elementStack[_elementStackCount++] = (ushort)GetOffset(ref e);
        return ref e;
    }

    private static void EndElement(NewElementType type)
    {
        Debug.Assert(_elementStackCount > 0);
        var elementOffset = _elementStack[--_elementStackCount];
        _nextSibling = elementOffset;
        ref var e = ref GetElement(elementOffset);
        e.NextSibling = (ushort)_elements.Length;
        Debug.Assert(e.Type == type);
    }

    internal static void EndElement()
    {
        Debug.Assert(_elementStackCount > 0);
        var elementOffset = _elementStack[--_elementStackCount];
        _nextSibling = elementOffset;
        ref var e = ref GetElement(elementOffset);
        e.NextSibling = (ushort)_elements.Length;
    }

    internal static bool HasCurrentWidget() => _currentWidget != 0;

    private static void BeginElementInternal(NewElementType type, ref BaseElement e)
    {
        e.Type = type;
        e.Parent = _elementStackCount > 0 ? _elementStack[_elementStackCount - 1] : (ushort)0;
        e.NextSibling = 0;
        e.ChildCount = 0;
        e.FirstChild = 0;
        if (_elementStackCount > 0)
        {
            ref var p = ref GetElement(e.Parent);
            p.ChildCount++;
            if (p.FirstChild == 0)
                p.FirstChild = (ushort)((byte*)Unsafe.AsPointer(ref e) - _elements.Ptr);
        }

    }

    internal static int GetOffset(ref BaseElement element)
    {
        var offset = (byte*)Unsafe.AsPointer(ref element) - _elements.Ptr;
        Debug.Assert(offset >= 0);
        Debug.Assert(offset < MaxElementSize);
        return (int)offset;
    }

    internal static ref BaseElement CreateLeafElement<T>(NewElementType type) where T : unmanaged
    {
        ref var e = ref AllocElement<T>(type);
        BeginElementInternal(type, ref e);
        e.NextSibling = (ushort)_elements.Length;
        return ref e;
    }

    private static ushort AddAsset(object asset)
    {
        Debug.Assert(_assetCount < MaxAssets, "Asset array exceeded maximum capacity.");
        var index = (ushort)_assetCount;
        _assets[_assetCount++] = asset;
        return index;
    }

    internal static object? GetAsset(ushort index) => _assets[index];

    public static UnsafeSpan<char> Text(ReadOnlySpan<char> text) => UI.AddText(text);

    // ──────────────────────────────────────────────
    // Size (single-child wrapper)
    // ──────────────────────────────────────────────

    public static int BeginSize(Size width, Size height) => BeginSize(new Size2(width, height));

    public static int BeginSize(Size2 size)
    {
        ref var e = ref BeginElement<SizeElement>(NewElementType.Size);
        ref var d = ref GetElementData<SizeElement>(ref e);
        d.Size = size;
        return GetOffset(ref e);
    }

    public static void EndSize() => EndElement(NewElementType.Size);

    // ──────────────────────────────────────────────
    // Padding (single-child wrapper)
    // ──────────────────────────────────────────────

    public static int BeginPadding(EdgeInsets padding)
    {
        ref var e = ref BeginElement<PaddingElement>(NewElementType.Padding);
        ref var d = ref GetElementData<PaddingElement>(ref e);
        d.Padding = padding;
        return GetOffset(ref e);
    }

    public static void EndPadding() => EndElement(NewElementType.Padding);

    // ──────────────────────────────────────────────
    // Fill (single-child wrapper)
    // ──────────────────────────────────────────────

    public static int BeginFill(Color color, BorderRadius radius = default)
    {
        ref var e = ref BeginElement<FillElement>(NewElementType.Fill);
        ref var d = ref GetElementData<FillElement>(ref e);
        d.Color = color;
        d.Radius = radius;
        return GetOffset(ref e);
    }

    public static void EndFill() => EndElement(NewElementType.Fill);

    // ──────────────────────────────────────────────
    // Border (single-child wrapper)
    // ──────────────────────────────────────────────

    public static int BeginBorder(float width, Color color, BorderRadius radius = default)
    {
        ref var e = ref BeginElement<BorderElement>(NewElementType.Border);
        ref var d = ref GetElementData<BorderElement>(ref e);
        d.Width = width;
        d.Color = color;
        d.Radius = radius;
        return GetOffset(ref e);
    }

    public static void EndBorder() => EndElement(NewElementType.Border);

    // ──────────────────────────────────────────────
    // Margin (single-child wrapper)
    // ──────────────────────────────────────────────

    public static int BeginMargin(EdgeInsets margin)
    {
        ref var e = ref BeginElement<MarginElement>(NewElementType.Margin);
        ref var d = ref GetElementData<MarginElement>(ref e);
        d.Margin = margin;
        return GetOffset(ref e);
    }

    public static void EndMargin() => EndElement(NewElementType.Margin);

    // ──────────────────────────────────────────────
    // Align (single-child wrapper)
    // ──────────────────────────────────────────────

    public static int BeginAlign(Align2 align)
    {
        ref var e = ref BeginElement<AlignElement>(NewElementType.Align);
        ref var d = ref GetElementData<AlignElement>(ref e);
        d.Align = align;
        return GetOffset(ref e);
    }

    public static void EndAlign() => EndElement(NewElementType.Align);

    // ──────────────────────────────────────────────
    // Clip (single-child wrapper)
    // ──────────────────────────────────────────────

    public static int BeginClip(BorderRadius radius = default)
    {
        ref var e = ref BeginElement<ClipElement>(NewElementType.Clip);
        ref var d = ref GetElementData<ClipElement>(ref e);
        d.Radius = radius;
        return GetOffset(ref e);
    }

    public static void EndClip() => EndElement(NewElementType.Clip);

    // ──────────────────────────────────────────────
    // Opacity (single-child wrapper)
    // ──────────────────────────────────────────────

    public static int BeginOpacity(float opacity)
    {
        ref var e = ref BeginElement<OpacityElement>(NewElementType.Opacity);
        ref var d = ref GetElementData<OpacityElement>(ref e);
        d.Opacity = opacity;
        return GetOffset(ref e);
    }

    public static void EndOpacity() => EndElement(NewElementType.Opacity);

    // ──────────────────────────────────────────────
    // Row (multi-child container)
    // ──────────────────────────────────────────────

    public static int BeginRow(float spacing = 0)
    {
        ref var e = ref BeginElement<RowElement>(NewElementType.Row);
        ref var d = ref GetElementData<RowElement>(ref e);
        d.Spacing = spacing;
        return GetOffset(ref e);
    }

    public static void EndRow() => EndElement(NewElementType.Row);

    // ──────────────────────────────────────────────
    // Column (multi-child container)
    // ──────────────────────────────────────────────

    public static int BeginColumn(float spacing = 0)
    {
        ref var e = ref BeginElement<ColumnElement>(NewElementType.Column);
        ref var d = ref GetElementData<ColumnElement>(ref e);
        d.Spacing = spacing;
        return GetOffset(ref e);
    }

    public static void EndColumn() => EndElement(NewElementType.Column);

    // ──────────────────────────────────────────────
    // Flex (leaf or container, parent distributes space)
    // ──────────────────────────────────────────────

    public static int Flex(float flex = 1.0f)
    {
        ref var e = ref CreateLeafElement<FlexElement>(NewElementType.Flex);
        ref var d = ref GetElementData<FlexElement>(ref e);
        d.Flex = flex;
        return GetOffset(ref e);
    }

    public static int BeginFlex(float flex = 1.0f)
    {
        ref var e = ref BeginElement<FlexElement>(NewElementType.Flex);
        ref var d = ref GetElementData<FlexElement>(ref e);
        d.Flex = flex;
        return GetOffset(ref e);
    }

    public static void EndFlex() => EndElement(NewElementType.Flex);

    // ──────────────────────────────────────────────
    // Popup (absolute positioning container)
    // ──────────────────────────────────────────────

    internal static int BeginPopup(Rect anchorRect, Align2 anchor, Align2 popupAlign, float spacing, bool clampToScreen)
    {
        ref var e = ref BeginElement<PopupElement>(NewElementType.Popup);
        ref var d = ref GetElementData<PopupElement>(ref e);
        d.AnchorRect = anchorRect;
        d.AnchorFactorX = anchor.X.ToFactor();
        d.AnchorFactorY = anchor.Y.ToFactor();
        d.PopupAlignFactorX = popupAlign.X.ToFactor();
        d.PopupAlignFactorY = popupAlign.Y.ToFactor();
        d.Spacing = spacing;
        d.ClampToScreen = clampToScreen;
        return GetOffset(ref e);
    }

    internal static void EndPopup() => EndElement(NewElementType.Popup);

    // ──────────────────────────────────────────────
    // Spacer (leaf)
    // ──────────────────────────────────────────────

    public static int Spacer(float width, float height) => Spacer(new Vector2(width, height));

    public static int Spacer(Vector2 size)
    {
        ref var e = ref CreateLeafElement<SpacerElement>(NewElementType.Spacer);
        ref var d = ref GetElementData<SpacerElement>(ref e);
        d.Size = size;
        return GetOffset(ref e);
    }

    // ──────────────────────────────────────────────
    // Label (leaf)
    // ──────────────────────────────────────────────

    public static int Label(UnsafeSpan<char> text, Font font, float fontSize, Color color,
        Align2 align = default, TextOverflow overflow = TextOverflow.Overflow)
    {
        ref var e = ref CreateLeafElement<LabelElement>(NewElementType.Label);
        ref var d = ref GetElementData<LabelElement>(ref e);
        d.Text = text;
        d.FontSize = fontSize;
        d.Color = color;
        d.Align = align;
        d.Overflow = overflow;
        d.AssetIndex = AddAsset(font);
        return GetOffset(ref e);
    }

    // ──────────────────────────────────────────────
    // Image (leaf)
    // ──────────────────────────────────────────────

    public static int Image(Sprite sprite, Size2 size = default, ImageStretch stretch = ImageStretch.Uniform,
        Color color = default, float scale = 1.0f)
    {
        ref var e = ref CreateLeafElement<ImageElement>(NewElementType.Image);
        ref var d = ref GetElementData<ImageElement>(ref e);
        d.Size = size;
        d.Stretch = stretch;
        d.Align = NoZ.Align.Center;
        d.Scale = scale;
        d.Color = color.IsTransparent ? Color.White : color;
        d.Width = sprite.Bounds.Width;
        d.Height = sprite.Bounds.Height;
        d.AssetIndex = AddAsset(sprite);
        return GetOffset(ref e);
    }

    // ──────────────────────────────────────────────
    // Widget
    // ──────────────────────────────────────────────

    private static ref WidgetElement GetCurrentWidget()
    {
        Debug.Assert(_currentWidget != 0);
        ref var e = ref GetElement(_currentWidget);
        Debug.Assert(e.Type == NewElementType.Widget);
        return ref GetElementData<WidgetElement>(ref e);
    }

    private static ref WidgetInputState GetCurrentWidgetState()
    {
        ref var w = ref GetCurrentWidget();
        Debug.Assert(w.Id > 0 && w.Id < MaxId);
        return ref _widgetStates[w.Id];
    }

    internal static ElementFlags GetCurrentWidgetFlags() => GetCurrentWidgetState().Flags;

    internal static void SetWidgetFlag(ElementFlags flag, bool value)
    {
        ref var ws = ref GetCurrentWidgetState();
        if (value) ws.Flags |= flag;
        else ws.Flags &= ~flag;
    }

    public static bool IsHovered() => GetCurrentWidgetState().Flags.HasFlag(ElementFlags.Hovered);
    public static bool WasPressed() => GetCurrentWidgetState().Flags.HasFlag(ElementFlags.Pressed);
    public static bool IsDown() => GetCurrentWidgetState().Flags.HasFlag(ElementFlags.Down);
    public static bool HoverEnter() { ref var ws = ref GetCurrentWidgetState(); return ws.Flags.HasFlag(ElementFlags.HoverChanged) && ws.Flags.HasFlag(ElementFlags.Hovered); }
    public static bool HoverExit() { ref var ws = ref GetCurrentWidgetState(); return ws.Flags.HasFlag(ElementFlags.HoverChanged) && !ws.Flags.HasFlag(ElementFlags.Hovered); }
    public static bool HoverChanged() => GetCurrentWidgetState().Flags.HasFlag(ElementFlags.HoverChanged);
    public static bool HasFocus() => _focusId == GetCurrentWidget().Id;
    public static bool HasFocusOn(int id) => _focusId == id;

    internal static bool IsWidgetId(int id) => id > 0 && id < MaxId && _widgets[id] != 0 && _widgetStates[id].LastFrame >= (ushort)(_frame - 1);

    internal static bool IsHoveredById(int id)
    {
        if (!IsWidgetId(id)) return false;
        return _widgetStates[id].Flags.HasFlag(ElementFlags.Hovered);
    }

    internal static bool WasPressedById(int id)
    {
        if (!IsWidgetId(id)) return false;
        return _widgetStates[id].Flags.HasFlag(ElementFlags.Pressed);
    }

    internal static bool IsDownById(int id)
    {
        if (!IsWidgetId(id)) return false;
        return _widgetStates[id].Flags.HasFlag(ElementFlags.Down);
    }

    internal static bool HoverChangedById(int id)
    {
        if (!IsWidgetId(id)) return false;
        return _widgetStates[id].Flags.HasFlag(ElementFlags.HoverChanged);
    }

    internal static Rect GetWidgetWorldRect(int id)
    {
        if (!IsWidgetId(id)) return Rect.Zero;
        ref var e = ref GetElement(_widgets[id]);
        var topLeft = Vector2.Transform(e.Rect.Position, e.LocalToWorld);
        var bottomRight = Vector2.Transform(e.Rect.Position + e.Rect.Size, e.LocalToWorld);
        return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    internal static Rect GetWidgetRect(int id)
    {
        if (!IsWidgetId(id)) return Rect.Zero;
        return GetElement(_widgets[id]).Rect;
    }

    public static bool HasCapture()
    {
        ref var w = ref GetCurrentWidget();
        return _captureId != 0 && _captureId == w.Id;
    }

    public static void SetFocus()
    {
        ref var w = ref GetCurrentWidget();
        _focusId = w.Id;
    }

    public static void ClearFocus()
    {
        _focusId = 0;
    }

    public static void SetCapture()
    {
        ref var w = ref GetCurrentWidget();
        _captureId = w.Id;
        Input.CaptureMouse();
    }

    public static void ReleaseCapture()
    {
        _captureId = 0;
        Input.ReleaseMouseCapture();
    }

    public static ref T GetState<T>() where T : unmanaged
    {
        ref var w = ref GetCurrentWidget();
        Debug.Assert(w.Id > 0 && w.Id < MaxId);
        ref var ws = ref _widgetStates[w.Id];

        ref var writePool = ref _statePools[_currentStatePool];

        // Already allocated this frame — return existing
        if (ws.LastFrame == _frame)
            return ref *(T*)(writePool.Ptr + ws.StateOffset);

        // Bump-allocate in current frame's pool (8-byte aligned)
        var size = (sizeof(T) + 7) & ~7;
        if (!writePool.CheckCapacity(size))
            throw new InvalidOperationException("Widget state pool exceeded capacity.");

        var offset = writePool.Length;
        writePool.AddRange(size);
        var ptr = writePool.Ptr + offset;

        // Copy from previous frame if widget existed last frame
        if (ws.LastFrame == (ushort)(_frame - 1) && ws.StateSize == size)
        {
            ref var readPool = ref _statePools[_currentStatePool ^ 1];
            System.Runtime.InteropServices.NativeMemory.Copy(
                readPool.Ptr + ws.StateOffset, ptr, (nuint)size);
        }
        else
        {
            System.Runtime.InteropServices.NativeMemory.Clear(ptr, (nuint)size);
        }

        ws.StateOffset = offset;
        ws.StateSize = (ushort)size;

        return ref *(T*)ptr;
    }

    internal static ref T GetStateByWidgetId<T>(int widgetId) where T : unmanaged
    {
        Debug.Assert(widgetId > 0 && widgetId < MaxId);
        ref var ws = ref _widgetStates[widgetId];
        ref var writePool = ref _statePools[_currentStatePool];

        if (ws.LastFrame == _frame)
            return ref *(T*)(writePool.Ptr + ws.StateOffset);

        // No state allocated this frame — return zeroed
        var size = (sizeof(T) + 7) & ~7;
        if (!writePool.CheckCapacity(size))
            throw new InvalidOperationException("Widget state pool exceeded capacity.");

        var offset = writePool.Length;
        writePool.AddRange(size);
        var ptr = writePool.Ptr + offset;

        if (ws.LastFrame == (ushort)(_frame - 1) && ws.StateSize == size)
        {
            ref var readPool = ref _statePools[_currentStatePool ^ 1];
            System.Runtime.InteropServices.NativeMemory.Copy(
                readPool.Ptr + ws.StateOffset, ptr, (nuint)size);
        }
        else
        {
            System.Runtime.InteropServices.NativeMemory.Clear(ptr, (nuint)size);
        }

        ws.StateOffset = offset;
        ws.StateSize = (ushort)size;
        return ref *(T*)ptr;
    }

    public static Vector2 GetLocalMousePosition()
    {
        ref var e = ref GetElement(_currentWidget);
        return Vector2.Transform(MouseWorldPosition, e.WorldToLocal);
    }

    public static int BeginWidget<T>(int id) where T : unmanaged
    {
        var offset = BeginWidget(id);
        ref var e = ref GetElement(offset);
        ref var d = ref GetElementData<WidgetElement>(ref e);
        var wd = _elements.AddRange(sizeof(T));
        d.Data = (ushort)(wd.GetUnsafePtr() - _elements.Ptr);
        return offset;
    }

    public static int BeginWidget(int id)
    {
        ref var e = ref BeginElement<WidgetElement>(NewElementType.Widget);
        ref var d = ref GetElementData<WidgetElement>(ref e);
        var offset = (ushort)GetOffset(ref e);
        d.Id = id;
        d.Data = 0;
        _widgets[id] = offset;
        _currentWidget = offset;
        return offset;
    }

    public static void EndWidget()
    {
        EndElement(NewElementType.Widget);

        _currentWidget = 0;
        for (int i = _elementStackCount - 1; i >= 0; i--)
        {
            ref var e = ref GetElement(_elementStack[i]);
            if (e.Type == NewElementType.Widget)
            {
                _currentWidget = _elementStack[i];
                break;
            }
        }
    }

    // ──────────────────────────────────────────────
    // High-level widgets
    // ──────────────────────────────────────────────

    public static bool Button(int id, UnsafeSpan<char> text, Font font, float fontSize,
        Color textColor, Color bgColor, Color hoverColor,
        EdgeInsets padding = default, BorderRadius radius = default)
    {
        BeginWidget(id);

        var hovered = IsHovered();
        var down = IsDown();
        var fillColor = down ? hoverColor : (hovered ? hoverColor : bgColor);

        if (radius.TopLeft > 0 || radius.TopRight > 0 || radius.BottomLeft > 0 || radius.BottomRight > 0)
            BeginBorder(0, Color.Transparent, radius);

        BeginFill(fillColor);
        BeginPadding(padding);
        BeginAlign(Align.Center);
        Label(text, font, fontSize, textColor);
        EndAlign();
        EndPadding();
        EndFill();

        if (radius.TopLeft > 0 || radius.TopRight > 0 || radius.BottomLeft > 0 || radius.BottomRight > 0)
            EndBorder();

        var pressed = WasPressed();
        EndWidget();
        return pressed;
    }

    public static bool Toggle(int id, bool value, Sprite icon, Color color, Color activeColor)
    {
        BeginWidget(id);
        var fillColor = value ? activeColor : (IsHovered() ? activeColor.WithAlpha(0.5f) : Color.Transparent);
        BeginFill(fillColor);
        Image(icon, color: value ? Color.White : color);
        EndFill();
        var toggled = WasPressed();
        EndWidget();
        return toggled;
    }

    public static bool Slider(int id, ref float value, float min = 0f, float max = 1f,
        Color trackColor = default, Color fillColor = default, Color thumbColor = default,
        float height = 20f, float trackHeight = 4f)
    {
        if (trackColor.IsTransparent) trackColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        if (fillColor.IsTransparent) fillColor = new Color(0.4f, 0.6f, 1f, 1f);
        if (thumbColor.IsTransparent) thumbColor = Color.White;

        var changed = false;
        var t = max > min ? Math.Clamp((value - min) / (max - min), 0f, 1f) : 0f;

        BeginWidget(id);
        BeginSize(Size.Percent(1), new Size(height));

        // Track background (centered vertically)
        BeginAlign(new Align2(Align.Min, Align.Center));
        BeginSize(Size.Percent(1), new Size(trackHeight));
        BeginFill(trackColor);
        EndFill();
        EndSize();
        EndAlign();

        // Fill bar
        if (t > 0)
        {
            BeginAlign(new Align2(Align.Min, Align.Center));
            BeginSize(Size.Percent(t), new Size(trackHeight));
            BeginFill(IsDown() ? fillColor : fillColor.WithAlpha(0.8f));
            EndFill();
            EndSize();
            EndAlign();
        }

        // Input: capture on down, update value while captured
        if (IsDown() && !HasCapture())
            SetCapture();

        if (HasCapture())
        {
            var localMouse = GetLocalMousePosition();
            ref var we = ref GetElement(_currentWidget);
            var widgetWidth = we.Rect.Width;
            if (widgetWidth > 0)
            {
                var localX = Math.Clamp(localMouse.X / widgetWidth, 0f, 1f);
                var newValue = min + localX * (max - min);
                newValue = Math.Clamp(newValue, min, max);

                if (newValue != value)
                {
                    value = newValue;
                    changed = true;
                }
            }
        }

        EndSize();
        EndWidget();
        return changed;
    }

    // ──────────────────────────────────────────────
    // Layout (axis-independent: width first, then height)
    // ──────────────────────────────────────────────

    private static float EdgeInset(in EdgeInsets ei, int axis) => axis == 0 ? ei.Horizontal : ei.Vertical;
    private static float EdgeMin(in EdgeInsets ei, int axis) => axis == 0 ? ei.L : ei.T;

    private static float FitAxis(int offset, int axis, int layoutAxis)
    {
        ref var e = ref GetElement(offset);
        switch (e.Type)
        {
            case NewElementType.Size:
            {
                ref var d = ref GetElementData<SizeElement>(ref e);
                var mode = d.Size[axis].Mode;
                if (mode == SizeMode.Default)
                    mode = SizeMode.Fit;
                return mode switch
                {
                    SizeMode.Fixed => d.Size[axis].Value,
                    SizeMode.Fit => e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0,
                    SizeMode.Percent => 0,
                    _ => 0
                };
            }

            case NewElementType.Padding:
            {
                ref var d = ref GetElementData<PaddingElement>(ref e);
                var child = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;
                return child + EdgeInset(d.Padding, axis);
            }

            case NewElementType.Margin:
            {
                ref var d = ref GetElementData<MarginElement>(ref e);
                var child = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;
                return child + EdgeInset(d.Margin, axis);
            }

            case NewElementType.Border:
            {
                ref var d = ref GetElementData<BorderElement>(ref e);
                var child = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;
                return child + d.Width * 2;
            }

            case NewElementType.Fill:
            case NewElementType.Clip:
            case NewElementType.Opacity:
            case NewElementType.Widget:
            case NewElementType.Align:
                return e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;

            case NewElementType.Row:
            {
                ref var d = ref GetElementData<RowElement>(ref e);
                return FitRowColumn(ref e, axis, 0, d.Spacing);
            }

            case NewElementType.Column:
            {
                ref var d = ref GetElementData<ColumnElement>(ref e);
                return FitRowColumn(ref e, axis, 1, d.Spacing);
            }

            case NewElementType.Flex:
                return e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;

            case NewElementType.Spacer:
            {
                ref var d = ref GetElementData<SpacerElement>(ref e);
                return d.Size[axis];
            }

            case NewElementType.Label:
            {
                ref var d = ref GetElementData<LabelElement>(ref e);
                var font = (Font)_assets[d.AssetIndex]!;
                if (axis == 1 && d.Overflow == TextOverflow.Wrap && e.Rect.Width > 0)
                    return TextRender.MeasureWrapped(d.Text.AsReadOnlySpan(), font, d.FontSize, e.Rect.Width).Y;
                var measure = TextRender.Measure(d.Text.AsReadOnlySpan(), font, d.FontSize);
                return measure[axis];
            }

            case NewElementType.Image:
            {
                ref var d = ref GetElementData<ImageElement>(ref e);
                if (d.Size[axis].IsFixed) return d.Size[axis].Value;
                return (axis == 0 ? d.Width : d.Height) * d.Scale;
            }

            case NewElementType.EditableText:
                return FitEditableTextAxis(ref e, axis);

            case NewElementType.Popup:
                return e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, -1) : 0;

            default:
                return 0;
        }
    }

    private static float FitRowColumn(ref BaseElement e, int axis, int containerAxis, float spacing)
    {
        var fit = 0f;
        var childCount = 0;
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            if (child.Type == NewElementType.Flex)
            {
                if (axis != containerAxis)
                    fit = Math.Max(fit, FitAxis(childOffset, axis, containerAxis));
            }
            else
            {
                var childFit = FitAxis(childOffset, axis, containerAxis);
                if (axis == containerAxis)
                    fit += childFit;
                else
                    fit = Math.Max(fit, childFit);
            }
            childCount++;
            childOffset = child.NextSibling;
        }
        if (axis == containerAxis && childCount > 1)
            fit += (childCount - 1) * spacing;
        return fit;
    }

    private static int _layoutDepth;
    private static bool _layoutCycleLogged;

    private static void LayoutAxis(int offset, float position, float available, int axis, int layoutAxis)
    {
        if (_layoutDepth > 200)
        {
            if (!_layoutCycleLogged)
            {
                _layoutCycleLogged = true;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"ElementTree: LayoutAxis depth > 200 at offset {offset}, axis={axis}, layoutAxis={layoutAxis}");
                sb.AppendLine($"Tree has {_elements.Length} bytes. Linear dump:");
                DebugDumpLinear(sb);
                Log.Error(sb.ToString());
            }
            return;
        }
        _layoutDepth++;
        LayoutAxisImpl(offset, position, available, axis, layoutAxis);
        _layoutDepth--;
    }

    private static void DebugDumpLinear(System.Text.StringBuilder sb)
    {
        var offset = 0;
        var count = 0;
        while (offset < _elements.Length && count < 200)
        {
            ref var e = ref GetElement(offset);
            var elemSize = GetElementSize(e.Type);
            sb.Append($"  [{offset}] {e.Type} parent={e.Parent} first={e.FirstChild} next={e.NextSibling} children={e.ChildCount}");
            if (e.Type == NewElementType.Widget)
            {
                ref var d = ref GetElementData<WidgetElement>(ref e);
                sb.Append($" id={d.Id}");
            }
            sb.AppendLine();
            offset += elemSize;
            count++;
        }
    }

    private static int GetElementSize(NewElementType type) => type switch
    {
        NewElementType.Widget => sizeof(BaseElement) + sizeof(WidgetElement),
        NewElementType.Size => sizeof(BaseElement) + sizeof(SizeElement),
        NewElementType.Padding => sizeof(BaseElement) + sizeof(PaddingElement),
        NewElementType.Fill => sizeof(BaseElement) + sizeof(FillElement),
        NewElementType.Border => sizeof(BaseElement) + sizeof(BorderElement),
        NewElementType.Margin => sizeof(BaseElement) + sizeof(MarginElement),
        NewElementType.Row => sizeof(BaseElement) + sizeof(RowElement),
        NewElementType.Column => sizeof(BaseElement) + sizeof(ColumnElement),
        NewElementType.Flex => sizeof(BaseElement) + sizeof(FlexElement),
        NewElementType.Align => sizeof(BaseElement) + sizeof(AlignElement),
        NewElementType.Clip => sizeof(BaseElement) + sizeof(ClipElement),
        NewElementType.Spacer => sizeof(BaseElement) + sizeof(SpacerElement),
        NewElementType.Opacity => sizeof(BaseElement) + sizeof(OpacityElement),
        NewElementType.Label => sizeof(BaseElement) + sizeof(LabelElement),
        NewElementType.Image => sizeof(BaseElement) + sizeof(ImageElement),
        NewElementType.EditableText => sizeof(BaseElement) + sizeof(EditableTextElement),
        NewElementType.Popup => sizeof(BaseElement) + sizeof(PopupElement),
        _ => sizeof(BaseElement)
    };

    private static void LayoutAxisImpl(int offset, float position, float available, int axis, int layoutAxis)
    {
        ref var e = ref GetElement(offset);
        float size;

        switch (e.Type)
        {
            case NewElementType.Size:
            {
                ref var d = ref GetElementData<SizeElement>(ref e);
                var mode = d.Size[axis].Mode;
                var isDefault = mode == SizeMode.Default;
                if (isDefault)
                    mode = (layoutAxis == axis) ? SizeMode.Fit : SizeMode.Percent;
                size = mode switch
                {
                    SizeMode.Fixed => d.Size[axis].Value,
                    SizeMode.Percent => available * (isDefault ? 1.0f : d.Size[axis].Value),
                    SizeMode.Fit => e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0,
                    _ => 0
                };
                break;
            }

            case NewElementType.Padding:
            {
                ref var d = ref GetElementData<PaddingElement>(ref e);
                var inset = EdgeInset(d.Padding, axis);
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0) + inset;
                break;
            }

            case NewElementType.Margin:
            {
                ref var d = ref GetElementData<MarginElement>(ref e);
                var inset = EdgeInset(d.Margin, axis);
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0) + inset;
                break;
            }

            case NewElementType.Border:
            {
                ref var d = ref GetElementData<BorderElement>(ref e);
                var inset = d.Width * 2;
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0) + inset;
                break;
            }

            case NewElementType.Fill:
            case NewElementType.Clip:
            case NewElementType.Opacity:
            case NewElementType.Widget:
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0);
                break;

            case NewElementType.Align:
                size = available;
                break;

            case NewElementType.Row:
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(offset, axis, 0) : 0);
                break;

            case NewElementType.Column:
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(offset, axis, 1) : 0);
                break;

            case NewElementType.Flex:
                size = available;
                break;

            case NewElementType.Spacer:
            {
                ref var d = ref GetElementData<SpacerElement>(ref e);
                size = d.Size[axis];
                break;
            }

            case NewElementType.Label:
            {
                ref var d = ref GetElementData<LabelElement>(ref e);
                var font = (Font)_assets[d.AssetIndex]!;
                if (axis == 1 && d.Overflow == TextOverflow.Wrap && e.Rect.Width > 0)
                    size = TextRender.MeasureWrapped(d.Text.AsReadOnlySpan(), font, d.FontSize, e.Rect.Width).Y;
                else
                    size = TextRender.Measure(d.Text.AsReadOnlySpan(), font, d.FontSize)[axis];
                break;
            }

            case NewElementType.Image:
            {
                ref var d = ref GetElementData<ImageElement>(ref e);
                if (d.Size[axis].IsFixed)
                    size = d.Size[axis].Value;
                else
                    size = (axis == 0 ? d.Width : d.Height) * d.Scale;
                break;
            }

            case NewElementType.EditableText:
                size = LayoutEditableTextAxis(ref e, axis, available);
                break;

            case NewElementType.Popup:
                size = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, -1) : 0;
                break;

            default:
                size = 0;
                break;
        }

        e.Rect[axis] = position;
        e.Rect[axis + 2] = size;

        // Popup: override position to anchor rect after size is known
        if (e.Type == NewElementType.Popup)
        {
            ref var pd = ref GetElementData<PopupElement>(ref e);
            var anchorPos = pd.AnchorRect[axis] + pd.AnchorRect[axis + 2] * (axis == 0 ? pd.AnchorFactorX : pd.AnchorFactorY);
            var popupAlignFactor = axis == 0 ? pd.PopupAlignFactorX : pd.PopupAlignFactorY;
            var anchorFactor = axis == 0 ? pd.AnchorFactorX : pd.AnchorFactorY;
            e.Rect[axis] = anchorPos - size * popupAlignFactor;
            if (anchorFactor != popupAlignFactor)
                e.Rect[axis] += pd.Spacing * (1f - 2f * popupAlignFactor);
            if (pd.ClampToScreen)
                e.Rect[axis] = Math.Clamp(e.Rect[axis], 0, ScreenSize[axis] - size);
        }

        // Recurse children
        switch (e.Type)
        {
            case NewElementType.Row when axis == 0:
                LayoutRowColumnAxis(ref e, axis, 0);
                break;
            case NewElementType.Row when axis == 1:
                LayoutCrossAxis(ref e, axis);
                break;
            case NewElementType.Column when axis == 1:
                LayoutRowColumnAxis(ref e, axis, 1);
                break;
            case NewElementType.Column when axis == 0:
                LayoutCrossAxis(ref e, axis);
                break;
            case NewElementType.Align:
                LayoutAlignAxis(ref e, axis);
                break;
            case NewElementType.Padding:
            {
                ref var d = ref GetElementData<PaddingElement>(ref e);
                var inset = EdgeInset(d.Padding, axis);
                var childPos = e.Rect[axis] + EdgeMin(d.Padding, axis);
                var childAvail = Math.Max(0, size - inset);
                LayoutChildrenAxis(ref e, childPos, childAvail, axis, layoutAxis);
                break;
            }
            case NewElementType.Margin:
            {
                ref var d = ref GetElementData<MarginElement>(ref e);
                var inset = EdgeInset(d.Margin, axis);
                var childPos = e.Rect[axis] + EdgeMin(d.Margin, axis);
                var childAvail = Math.Max(0, size - inset);
                LayoutChildrenAxis(ref e, childPos, childAvail, axis, layoutAxis);
                break;
            }
            case NewElementType.Border:
            {
                ref var d = ref GetElementData<BorderElement>(ref e);
                var childPos = e.Rect[axis] + d.Width;
                var childAvail = Math.Max(0, size - d.Width * 2);
                LayoutChildrenAxis(ref e, childPos, childAvail, axis, layoutAxis);
                break;
            }
            case NewElementType.Size:
            {
                ref var d = ref GetElementData<SizeElement>(ref e);
                var mode = d.Size[axis].Mode;
                var isFit = mode == SizeMode.Fit || (mode == SizeMode.Default && layoutAxis == axis);
                LayoutChildrenAxis(ref e, e.Rect[axis], size, axis, isFit ? layoutAxis : -1);
                break;
            }
            case NewElementType.Flex:
                LayoutChildrenAxis(ref e, e.Rect[axis], size, axis, -1);
                break;
            case NewElementType.Popup:
                LayoutChildrenAxis(ref e, e.Rect[axis], size, axis, -1);
                break;
            default:
                LayoutChildrenAxis(ref e, e.Rect[axis], size, axis, layoutAxis);
                break;
        }
    }

    private static void LayoutChildrenAxis(ref BaseElement e, float childPos, float childAvail, int axis, int layoutAxis)
    {
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            LayoutAxis(childOffset, childPos, childAvail, axis, layoutAxis);
            childOffset = child.NextSibling;
        }
    }

    private static void LayoutAlignAxis(ref BaseElement e, int axis)
    {
        if (e.ChildCount == 0) return;
        ref var d = ref GetElementData<AlignElement>(ref e);
        var childFit = FitAxis(e.FirstChild, axis, -1);
        var alignFactor = (axis == 0 ? d.Align.X : d.Align.Y).ToFactor();
        var childPos = e.Rect[axis] + (e.Rect.GetSize(axis) - childFit) * alignFactor;
        LayoutAxis(e.FirstChild, childPos, childFit, axis, -1);
    }

    private static void LayoutCrossAxis(ref BaseElement e, int axis)
    {
        var pos = e.Rect[axis];
        var avail = e.Rect.GetSize(axis);
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            LayoutAxis(childOffset, pos, avail, axis, axis == 0 ? 1 : 0);
            childOffset = child.NextSibling;
        }
    }

    private static void LayoutRowColumnAxis(ref BaseElement e, int axis, int containerAxis)
    {
        float spacing;
        if (e.Type == NewElementType.Row)
        {
            ref var d = ref GetElementData<RowElement>(ref e);
            spacing = d.Spacing;
        }
        else
        {
            ref var d = ref GetElementData<ColumnElement>(ref e);
            spacing = d.Spacing;
        }

        var fixedTotal = 0f;
        var flexTotal = 0f;
        var childCount = 0;
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            if (child.Type == NewElementType.Flex)
            {
                ref var fd = ref GetElementData<FlexElement>(ref child);
                flexTotal += fd.Flex;
            }
            else
            {
                fixedTotal += FitAxis(childOffset, axis, containerAxis);
            }
            childCount++;
            childOffset = child.NextSibling;
        }
        if (childCount > 1)
            fixedTotal += (childCount - 1) * spacing;

        var offset = 0f;
        var remaining = e.Rect.GetSize(axis) - fixedTotal;
        childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            if (i > 0) offset += spacing;

            ref var child = ref GetElement(childOffset);
            var childPos = e.Rect[axis] + offset;

            if (child.Type == NewElementType.Flex)
            {
                ref var fd = ref GetElementData<FlexElement>(ref child);
                var flexSize = flexTotal > 0 ? (fd.Flex / flexTotal) * remaining : 0;
                LayoutAxis(childOffset, childPos, flexSize, axis, containerAxis);
                offset += flexSize;
            }
            else
            {
                LayoutAxis(childOffset, childPos, e.Rect.GetSize(axis), axis, containerAxis);
                offset += child.Rect.GetSize(axis);
            }

            childOffset = child.NextSibling;
        }
    }

    // ──────────────────────────────────────────────
    // Transforms
    // ──────────────────────────────────────────────

    private static void UpdateTransforms(int offset, in Matrix3x2 parentTransform, Vector2 parentSize)
    {
        ref var e = ref GetElement(offset);

        var localTransform = Matrix3x2.CreateTranslation(e.Rect.X, e.Rect.Y);
        e.LocalToWorld = localTransform * parentTransform;
        Matrix3x2.Invert(e.LocalToWorld, out e.WorldToLocal);

        var rectSize = e.Rect.Size;
        e.Rect.X = 0;
        e.Rect.Y = 0;

        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            var absPos = child.Rect.Position;
            child.Rect.X = absPos.X - e.LocalToWorld.M31;
            child.Rect.Y = absPos.Y - e.LocalToWorld.M32;
            UpdateTransforms(childOffset, e.LocalToWorld, rectSize);
            childOffset = child.NextSibling;
        }
    }

    // ──────────────────────────────────────────────
    // Input
    // ──────────────────────────────────────────────

    private static void HandleInput()
    {
        _inputMousePressed = Input.WasButtonPressedRaw(InputCode.MouseLeft);
        _inputMouseDown = Input.IsButtonDownRaw(InputCode.MouseLeft);

        if (_captureId != 0 && !_inputMouseDown)
        {
            _captureId = 0;
            Input.ReleaseMouseCapture();
        }

        HandleInputElement(0);
    }

    private static bool _inputMousePressed;
    private static bool _inputMouseDown;

    private static void HandleInputElement(int offset)
    {
        ref var e = ref GetElement(offset);

        if (e.Type == NewElementType.Widget)
        {
            ref var d = ref GetElementData<WidgetElement>(ref e);
            if (d.Id > 0 && d.Id < MaxId)
            {
                ref var ws = ref _widgetStates[d.Id];
                ws.PrevFlags = ws.Flags;
                ws.Flags = ElementFlags.None;

                var localMouse = Vector2.Transform(MouseWorldPosition, e.WorldToLocal);
                var hovered = e.Rect.Contains(localMouse);

                if (hovered)
                    ws.Flags |= ElementFlags.Hovered;

                if (hovered && _inputMousePressed && (_captureId == 0 || _captureId == d.Id))
                    ws.Flags |= ElementFlags.Pressed;

                var isCaptured = _captureId != 0 && _captureId == d.Id;
                if (isCaptured ? _inputMouseDown : (hovered && _inputMouseDown))
                    ws.Flags |= ElementFlags.Down;

                if (_focusId == d.Id)
                    ws.Flags |= ElementFlags.Focus;

                if ((ws.Flags & ElementFlags.Hovered) != (ws.PrevFlags & ElementFlags.Hovered))
                    ws.Flags |= ElementFlags.HoverChanged;

                ws.LastFrame = _frame;
            }
        }

        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            HandleInputElement(childOffset);
            childOffset = child.NextSibling;
        }
    }

    // ──────────────────────────────────────────────
    // Drawing (self-contained)
    // ──────────────────────────────────────────────

    private static int _drawSortGroup;

    internal static void Draw()
    {
        if (_elements.Length == 0) return;

        _drawOpacity = 1.0f;
        _drawSortGroup = 0;
        using var _ = Graphics.PushState();
        Graphics.SetBlendMode(BlendMode.Alpha);
        Graphics.SetShader(_shader);
        Graphics.SetLayer(UI.Config.UILayer);
        Graphics.SetSortGroup(0);
        Graphics.SetTransform(Matrix3x2.Identity);
        Graphics.SetMesh(_mesh);

        DrawElement(0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Color ApplyOpacity(Color c) => c.WithAlpha(c.A * _drawOpacity);

    private static void DrawElement(int offset)
    {
        ref var e = ref GetElement(offset);
        var previousOpacity = _drawOpacity;
        var setScissor = false;

        switch (e.Type)
        {
            case NewElementType.Fill:
            {
                ref var d = ref GetElementData<FillElement>(ref e);
                if (!d.Color.IsTransparent)
                    DrawTexturedRect(e.Rect, e.LocalToWorld, null, ApplyOpacity(d.Color), d.Radius);
                break;
            }

            case NewElementType.Border:
            {
                ref var d = ref GetElementData<BorderElement>(ref e);
                if (d.Width > 0)
                    DrawTexturedRect(e.Rect, e.LocalToWorld, null, Color.Transparent,
                        d.Radius, d.Width, ApplyOpacity(d.Color));
                break;
            }

            case NewElementType.Label:
            {
                ref var d = ref GetElementData<LabelElement>(ref e);
                var font = (Font)_assets[d.AssetIndex]!;
                DrawLabel(ref e, ref d, font);
                break;
            }

            case NewElementType.Image:
            {
                ref var d = ref GetElementData<ImageElement>(ref e);
                DrawImage(ref e, ref d);
                break;
            }

            case NewElementType.EditableText:
                DrawEditableText(ref e);
                break;

            case NewElementType.Clip:
            {
                var topLeft = Vector2.Transform(e.Rect.Position, e.LocalToWorld);
                var bottomRight = Vector2.Transform(e.Rect.Position + e.Rect.Size, e.LocalToWorld);
                var screenTopLeft = UI.Camera!.WorldToScreen(topLeft);
                var screenBottomRight = UI.Camera!.WorldToScreen(bottomRight);
                var screenHeight = Application.WindowSize.Y;
                Graphics.SetScissor(
                    (int)screenTopLeft.X,
                    (int)(screenHeight - screenBottomRight.Y),
                    (int)(screenBottomRight.X - screenTopLeft.X),
                    (int)(screenBottomRight.Y - screenTopLeft.Y));
                setScissor = true;
                break;
            }

            case NewElementType.Opacity:
            {
                ref var d = ref GetElementData<OpacityElement>(ref e);
                _drawOpacity *= d.Opacity;
                break;
            }
        }

        // Popup: bump sort group so popup content renders on top
        if (e.Type == NewElementType.Popup)
        {
            _drawSortGroup++;
            Graphics.SetSortGroup(_drawSortGroup);
        }

        // Recurse children
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            DrawElement(childOffset);
            childOffset = child.NextSibling;
        }

        if (setScissor)
            Graphics.ClearScissor();

        _drawOpacity = previousOpacity;
    }

    private static void DrawTexturedRect(
        in Rect rect,
        in Matrix3x2 transform,
        Texture? texture,
        Color color,
        BorderRadius borderRadius = default,
        float borderWidth = 0,
        Color borderColor = default,
        ushort order = 0)
    {
        var vertexOffset = _vertices.Length;
        var indexOffset = _indices.Length;

        if (!_vertices.CheckCapacity(4) || !_indices.CheckCapacity(6))
            return;

        var w = rect.Width;
        var h = rect.Height;

        var maxR = MathF.Min(w, h) / 2;
        var radii = new Vector4(
            MathF.Min(borderRadius.TopLeft, maxR),
            MathF.Min(borderRadius.TopRight, maxR),
            MathF.Min(borderRadius.BottomLeft, maxR),
            MathF.Min(borderRadius.BottomRight, maxR));
        var rectSize = new Vector2(w, h);

        var p0 = Vector2.Transform(new Vector2(rect.X, rect.Y), transform);
        var p1 = Vector2.Transform(new Vector2(rect.X + w, rect.Y), transform);
        var p2 = Vector2.Transform(new Vector2(rect.X + w, rect.Y + h), transform);
        var p3 = Vector2.Transform(new Vector2(rect.X, rect.Y + h), transform);

        _vertices.Add(new UIVertex
        {
            Position = p0, UV = new Vector2(0, 0), Normal = rectSize,
            Color = color, BorderRatio = borderWidth, BorderColor = borderColor, CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = p1, UV = new Vector2(1, 0), Normal = rectSize,
            Color = color, BorderRatio = borderWidth, BorderColor = borderColor, CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = p2, UV = new Vector2(1, 1), Normal = rectSize,
            Color = color, BorderRatio = borderWidth, BorderColor = borderColor, CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = p3, UV = new Vector2(0, 1), Normal = rectSize,
            Color = color, BorderRatio = borderWidth, BorderColor = borderColor, CornerRadii = radii
        });

        _indices.Add((ushort)vertexOffset);
        _indices.Add((ushort)(vertexOffset + 1));
        _indices.Add((ushort)(vertexOffset + 2));
        _indices.Add((ushort)(vertexOffset + 2));
        _indices.Add((ushort)(vertexOffset + 3));
        _indices.Add((ushort)vertexOffset);

        using var _ = Graphics.PushState();
        Graphics.SetTexture(texture ?? Graphics.WhiteTexture, filter: texture?.Filter ?? TextureFilter.Point);
        Graphics.SetMesh(_mesh);
        Graphics.DrawElements(6, indexOffset, order: order);
    }

    internal static void Flush()
    {
        if (_indices.Length == 0) return;
        Graphics.Driver.BindMesh(_mesh.Handle);
        Graphics.Driver.UpdateMesh(_mesh.Handle, _vertices.AsByteSpan(), _indices.AsSpan());
        _vertices.Clear();
        _indices.Clear();
    }

    private static void DrawLabel(ref BaseElement e, ref LabelElement d, Font font)
    {
        var text = d.Text.AsReadOnlySpan();
        var fontSize = d.FontSize;

        switch (d.Overflow)
        {
            case TextOverflow.Wrap:
            {
                var wrappedHeight = TextRender.MeasureWrapped(text, font, fontSize, e.Rect.Width).Y;
                var effectiveHeight = wrappedHeight + font.InternalLeading * fontSize;
                var offsetY = (e.Rect.Height - effectiveHeight) * d.Align.Y.ToFactor();
                var displayScale = Application.Platform.DisplayScale;
                offsetY = MathF.Round(offsetY * displayScale) / displayScale;
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position + new Vector2(0, offsetY)) * e.LocalToWorld;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(d.Color));
                    Graphics.SetTransform(transform);
                    TextRender.DrawWrapped(text, font, fontSize, e.Rect.Width,
                        e.Rect.Width, d.Align.X.ToFactor(), e.Rect.Height);
                }
                break;
            }

            case TextOverflow.Scale:
            {
                var textWidth = TextRender.Measure(text, font, fontSize).X;
                var scaledFontSize = fontSize;
                if (textWidth > e.Rect.Width && e.Rect.Width > 0)
                    scaledFontSize = fontSize * (e.Rect.Width / textWidth);

                var textOffset = GetTextOffset(text, font, scaledFontSize, e.Rect.Size, d.Align.X, d.Align.Y);
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position + textOffset) * e.LocalToWorld;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(d.Color));
                    Graphics.SetTransform(transform);
                    TextRender.Draw(text, font, scaledFontSize);
                }
                break;
            }

            case TextOverflow.Ellipsis:
            {
                var textOffset = GetTextOffset(text, font, fontSize, e.Rect.Size, d.Align.X, d.Align.Y);
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position + textOffset) * e.LocalToWorld;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(d.Color));
                    Graphics.SetTransform(transform);
                    TextRender.DrawEllipsized(text, font, fontSize, e.Rect.Width);
                }
                break;
            }

            default:
            {
                var textOffset = GetTextOffset(text, font, fontSize, e.Rect.Size, d.Align.X, d.Align.Y);
                var transform = Matrix3x2.CreateTranslation(e.Rect.Position + textOffset) * e.LocalToWorld;

                using (Graphics.PushState())
                {
                    Graphics.SetColor(ApplyOpacity(d.Color));
                    Graphics.SetTransform(transform);
                    TextRender.Draw(text, font, fontSize);
                }
                break;
            }
        }
    }

    private static Vector2 GetTextOffset(ReadOnlySpan<char> text, Font font, float fontSize, in Vector2 containerSize, Align alignX, Align alignY)
    {
        var textWidth = TextRender.Measure(text, font, fontSize).X;
        var textHeight = (font.LineHeight + font.InternalLeading) * fontSize;
        var offset = new Vector2(
            (containerSize.X - textWidth) * alignX.ToFactor(),
            (containerSize.Y - textHeight) * alignY.ToFactor()
        );

        var displayScale = Application.Platform.DisplayScale;
        offset.X = MathF.Round(offset.X * displayScale) / displayScale;
        offset.Y = MathF.Round(offset.Y * displayScale) / displayScale;
        return offset;
    }

    private static Vector2 GetImageScale(ImageStretch stretch, Vector2 srcSize, Vector2 dstSize)
    {
        var scale = dstSize / srcSize;
        return stretch switch
        {
            ImageStretch.None => Vector2.One,
            ImageStretch.Uniform => new Vector2(MathF.Min(scale.X, scale.Y)),
            _ => scale
        };
    }

    private static void DrawImage(ref BaseElement e, ref ImageElement d)
    {
        var asset = _assets[d.AssetIndex];
        if (asset == null) return;

        var srcSize = new Vector2(d.Width, d.Height);
        var scale = GetImageScale(d.Stretch, srcSize, e.Rect.Size);
        var scaledSize = scale * srcSize;
        var offset = e.Rect.Position + (e.Rect.Size - scaledSize) * new Vector2(d.Align.X.ToFactor(), d.Align.Y.ToFactor());

        if (asset is Sprite sprite)
        {
            using var _ = Graphics.PushState();
            Graphics.SetColor(ApplyOpacity(d.Color));
            Graphics.SetTextureFilter(sprite.TextureFilter);

            if (sprite.IsSliced)
            {
                Graphics.SetTransform(e.LocalToWorld);
                Graphics.DrawSliced(sprite, new Rect(offset.X, offset.Y, scaledSize.X, scaledSize.Y));
            }
            else
            {
                offset -= new Vector2(sprite.Bounds.X, sprite.Bounds.Y) * scale;
                var transform = Matrix3x2.CreateScale(scale * sprite.PixelsPerUnit) * Matrix3x2.CreateTranslation(offset) * e.LocalToWorld;
                Graphics.SetTransform(transform);
                Graphics.DrawFlat(sprite, bone: -1);
            }
        }
    }

    internal static Vector2 ScreenSize;
    internal static Vector2 MouseWorldPosition;

#if DEBUG
    internal static string DebugDumpTree()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ElementTree: {_elements.Length} bytes, Frame={_frame}, Screen={ScreenSize.X:0}x{ScreenSize.Y:0}");
        sb.AppendLine("───────────────────────────────");

        if (_elements.Length == 0)
        {
            sb.AppendLine("(empty)");
            return sb.ToString();
        }

        DebugDumpElement(sb, 0, 0);
        return sb.ToString();
    }

    private static void DebugDumpElement(System.Text.StringBuilder sb, int offset, int depth)
    {
        if (offset < 0 || offset >= _elements.Length) return;
        if (depth > 100) { sb.AppendLine($"{new string(' ', depth * 2)}... (depth limit)"); return; }

        ref var e = ref GetElement(offset);
        var indent = new string(' ', depth * 2);

        sb.Append($"{indent}[{offset}] {e.Type}");
        sb.Append($" {e.Rect.X:0},{e.Rect.Y:0} {e.Rect.Width:0}x{e.Rect.Height:0}");
        sb.Append($" children={e.ChildCount} first={e.FirstChild} next={e.NextSibling}");

        switch (e.Type)
        {
            case NewElementType.Widget:
            {
                ref var d = ref GetElementData<WidgetElement>(ref e);
                sb.Append($" id={d.Id}");
                var name = UI.DebugGetElementName(d.Id);
                if (name.Length > 0) sb.Append($" \"{name}\"");
                break;
            }
            case NewElementType.Size:
            {
                ref var d = ref GetElementData<SizeElement>(ref e);
                sb.Append($" size={d.Size}");
                break;
            }
            case NewElementType.Fill:
            {
                ref var d = ref GetElementData<FillElement>(ref e);
                sb.Append($" color=#{(int)(d.Color.R*255):X2}{(int)(d.Color.G*255):X2}{(int)(d.Color.B*255):X2}");
                break;
            }
            case NewElementType.Padding:
            {
                ref var d = ref GetElementData<PaddingElement>(ref e);
                sb.Append($" pad={d.Padding.T:0},{d.Padding.R:0},{d.Padding.B:0},{d.Padding.L:0}");
                break;
            }
            case NewElementType.Margin:
            {
                ref var d = ref GetElementData<MarginElement>(ref e);
                sb.Append($" margin={d.Margin.T:0},{d.Margin.R:0},{d.Margin.B:0},{d.Margin.L:0}");
                break;
            }
            case NewElementType.Row:
            {
                ref var d = ref GetElementData<RowElement>(ref e);
                if (d.Spacing > 0) sb.Append($" spacing={d.Spacing:0}");
                break;
            }
            case NewElementType.Column:
            {
                ref var d = ref GetElementData<ColumnElement>(ref e);
                if (d.Spacing > 0) sb.Append($" spacing={d.Spacing:0}");
                break;
            }
            case NewElementType.Flex:
            {
                ref var d = ref GetElementData<FlexElement>(ref e);
                if (d.Flex != 1f) sb.Append($" flex={d.Flex}");
                break;
            }
            case NewElementType.Align:
            {
                ref var d = ref GetElementData<AlignElement>(ref e);
                sb.Append($" align={d.Align.X},{d.Align.Y}");
                break;
            }
            case NewElementType.Label:
            {
                ref var d = ref GetElementData<LabelElement>(ref e);
                var text = d.Text.AsReadOnlySpan().ToString();
                if (text.Length > 40) text = text[..37] + "...";
                sb.Append($" \"{text}\" size={d.FontSize:0}");
                break;
            }
            case NewElementType.Image:
            {
                ref var d = ref GetElementData<ImageElement>(ref e);
                var asset = _assets[d.AssetIndex];
                if (asset != null) sb.Append($" asset={asset}");
                break;
            }
            case NewElementType.Border:
            {
                ref var d = ref GetElementData<BorderElement>(ref e);
                sb.Append($" width={d.Width:0}");
                break;
            }
            case NewElementType.Opacity:
            {
                ref var d = ref GetElementData<OpacityElement>(ref e);
                sb.Append($" opacity={d.Opacity:0.##}");
                break;
            }
            case NewElementType.Spacer:
            {
                ref var d = ref GetElementData<SpacerElement>(ref e);
                sb.Append($" {d.Size.X:0}x{d.Size.Y:0}");
                break;
            }
            case NewElementType.Popup:
            {
                ref var d = ref GetElementData<PopupElement>(ref e);
                sb.Append($" anchor={d.AnchorRect.X:0},{d.AnchorRect.Y:0}");
                break;
            }
        }

        sb.AppendLine();

        // Recurse children
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            if (childOffset <= 0 && i > 0) break; // safety
            if (childOffset >= _elements.Length) break;
            DebugDumpElement(sb, childOffset, depth + 1);
            ref var child = ref GetElement(childOffset);
            childOffset = child.NextSibling;
        }
    }
#endif
}
