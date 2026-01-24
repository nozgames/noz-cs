//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#define NOZ_UI_DEBUG

using System.Diagnostics;
using System.Numerics;

namespace NoZ;

public enum ImageStretch : byte
{
    None,
    Fill,
    Uniform
}

public struct CanvasId : IEquatable<CanvasId>
{
    public byte Value;

    public CanvasId(byte value)
    {
        Debug.Assert(value <= MaxValue, "CanvasId value exceeds maximum");
        Value = value;
    }

    public readonly bool Equals(CanvasId other) => other.Value == Value;
    public override readonly bool Equals(object? obj) =>
        obj is CanvasId canvasId && Equals(canvasId);

    public static bool operator ==(CanvasId a, CanvasId b) => a.Value == b.Value;
    public static bool operator !=(CanvasId a, CanvasId b) => a.Value != b.Value;
    public readonly override int GetHashCode() => Value;
    public static implicit operator byte(CanvasId id) => id.Value;
    public static implicit operator CanvasId(byte value) => new(value);

    public const byte None = 0;
    public const byte MaxValue = 64;
}

public struct ElementId : IEquatable<ElementId>
{
    public byte Value;

    public ElementId(byte value)
    {
        Debug.Assert(value <= MaxValue, "ElementId value exceeds maximum");
        Value = value;
    }

    public readonly bool Equals(ElementId other) => other.Value == Value;
    public override readonly bool Equals(object? obj) =>
        obj is ElementId elementId && Equals(elementId);

    public static bool operator ==(ElementId a, ElementId b) => a.Value == b.Value;
    public static bool operator !=(ElementId a, ElementId b) => a.Value != b.Value;
    public readonly override int GetHashCode() => Value;
    public static implicit operator byte(ElementId id) => id.Value;
    public static implicit operator ElementId(byte value) => new(value);
    public const byte None = 0;
    public const byte MaxValue = 255;
}

public static partial class UI
{
    private const int MaxElements = 4096;
    private const int MaxElementStack = 128;
    private const int MaxPopups = 4;
    private const int MaxTextBuffer = 64 * 1024;
    
    public struct AutoCanvas : IDisposable { readonly void IDisposable.Dispose() => EndCanvas(); }
    public struct AutoContainer : IDisposable { readonly void IDisposable.Dispose() => EndContainer(); }
    public struct AutoColumn : IDisposable { readonly void IDisposable.Dispose() => EndColumn(); }
    public struct AutoRow : IDisposable { readonly void IDisposable.Dispose() => EndRow(); }
    public struct AutoScrollable : IDisposable { readonly void IDisposable.Dispose() => EndScrollable(); }
    public struct AutoFlex : IDisposable { readonly void IDisposable.Dispose() => EndFlex(); }

    private static Font? _defaultFont;
    public static Font? DefaultFont => _defaultFont;
    public static UIConfig Config { get; private set; } = new();


    private static readonly Element[] _elements = new Element[MaxElements];
    private static readonly short[] _elementStack = new short[MaxElementStack];
    private static readonly short[] _popups = new short[MaxPopups];
    private static readonly NativeArray<ElementState> _elementStates = new(CanvasId.MaxValue * (ElementId.MaxValue + 1));
    private static readonly NativeArray<char>[] _textBuffers = [new(MaxTextBuffer), new(MaxTextBuffer)];
    private static readonly NativeArray<CanvasState> _canvasStates = new(CanvasId.MaxValue, CanvasId.MaxValue);

    private static int _currentTextBuffer;
    private static CanvasId _currentCanvasId;
    private static CanvasId _hotCanvasId;
    private static bool _mouseLeftPressed;
    private static Vector2 _mousePosition;

    private static short _elementCount;
    private static int _elementStackCount;
    private static int _popupCount;
    private static byte _focusId;
    private static byte _pendingFocusId;
    private static byte _focusCanvasId;
    private static byte _pendingFocusCanvasId;
    private static Vector2 _size;
    private static Vector2Int _refSize;
    private static byte _activeScrollId;
    private static float _lastScrollMouseY;
    private static bool _closePopups;
    private static bool _elementWasPressed;
    public static Vector2 ScreenSize => _size;

    public static float UserScale { get; set; } = 1.0f;

    public static float GetUIScale() => Application.Platform.DisplayScale * UserScale;

    public static Camera? Camera { get; private set; } = null!;

    public static bool IsHotCanvas =>
        _currentCanvasId != CanvasId.None && _currentCanvasId == _hotCanvasId;

    public static Vector2Int GetRefSize()
    {
        var screenSize = Application.WindowSize;
        var scale = GetUIScale();
        return new Vector2Int(
            (int)(screenSize.X / scale),
            (int)(screenSize.Y / scale)
        );
    }

    public static void Init(UIConfig? config = null)
    {
        Config = config ?? new UIConfig();
        Camera = new Camera { FlipY = true };
        
        for (int canvasStateIndex=0; canvasStateIndex <= ElementId.MaxValue; canvasStateIndex++)
            _canvasStates[canvasStateIndex] = new CanvasState
            {
                ElementIndex = -1,
                ElementStates = _elementStates.AsUnsafeSpan(canvasStateIndex * (ElementId.MaxValue + 1), ElementId.MaxValue + 1)
            };

        UIRender.Init(Config);

        _defaultFont = Asset.Get<Font>(AssetType.Font, Config.DefaultFont);
    }

    public static void Shutdown()
    {
        _textBuffers[0].Dispose();
        _textBuffers[1].Dispose();
    }

    private static ref Element CreateElement(ElementType type)
    {
        ref var element = ref _elements[_elementCount];
        element.Type = type;
        element.Id = 0;
        element.CanvasId = _currentCanvasId;
        element.Index = _elementCount;
        element.NextSiblingIndex = 0;
        element.ParentIndex = _elementStackCount > 0 ? _elementStack[_elementStackCount - 1] : (short)-1;
        element.ChildCount = 0;
        element.Rect = Rect.Zero;
        element.MeasuredSize = Vector2.Zero;
        element.LocalToWorld = Matrix3x2.Identity;
        element.WorldToLocal = Matrix3x2.Identity;        

        if (_elementStackCount > 0)
            _elements[_elementStack[_elementStackCount - 1]].ChildCount++;

        _elementCount++;
        return ref element;
    }

    private static int GetDepth(ref readonly Element e)
    {
        int depth = 0;
        var parentIndex = e.ParentIndex;
        while (parentIndex != -1)
        {
            depth++;
            parentIndex = _elements[parentIndex].ParentIndex;
        }
        return depth;
    }

    private static ref Element GetParent() => 
        ref _elements[_elementStackCount - 2];

    private static ref Element GetParent(ref readonly Element e) =>
        ref GetElement(e.ParentIndex);

    private static ref Element GetSelf() =>
        ref _elements[_elementStack[_elementStackCount - 1]];

    private static ref Element GetElement(int index) =>
        ref _elements[index];

    private static ref ElementState GetElementState(ref Element e) =>
        ref _canvasStates[e.Id].ElementStates[e.Id];

    private static ref ElementState GetElementState(CanvasId canvasId, ElementId elementId) =>
        ref _canvasStates[canvasId].ElementStates[elementId];

    private static ref CanvasState GetCanvasState(CanvasId canvasId) =>
        ref _canvasStates[canvasId];

    private static NativeArray<char> GetTextBuffer() => _textBuffers[_currentTextBuffer];

    public static byte GetElementId() => HasCurrentElement() ? GetSelf().Id : (byte)0;

    public static Rect GetElementRect(CanvasId canvasId, ElementId id)
    {
        if (id == ElementId.None) return Rect.Zero;
        if (canvasId == CanvasId.None) return Rect.Zero;
        return GetElementState(canvasId, id).Rect;
    }

    private static bool HasCurrentElement() => _elementStackCount > 0;

    private static void SetId(ref Element e, CanvasId canvasId, ElementId elementId)
    {
        if (elementId == ElementId.None) return;
        if (canvasId == CanvasId.None) return;
        
        e.Id = elementId;
        e.CanvasId = canvasId;

        ref var es = ref GetElementState(canvasId, elementId);
        es.Index = e.Index;
    }

    private static void PushElement(short index)
    {
        if (_elementStackCount >= MaxElementStack) return;
        _elementStack[_elementStackCount++] = index;
    }

    private static void PopElement()
    {
        if (_elementStackCount == 0) return;
        _elements[_elementStack[_elementStackCount - 1]].NextSiblingIndex = _elementCount;
        _elementStackCount--;
    }

    private static void EndElement(ElementType expectedType)
    {
        if (!HasCurrentElement())
            throw new InvalidOperationException("No current element to end");
        ref var current = ref GetSelf();
        if (current.Type != expectedType)
            throw new InvalidOperationException($"Expected {expectedType} but got {current.Type}");
        PopElement();
    }

    internal static UnsafeSpan<char> AddText(ReadOnlySpan<char> text)
    {
        var tb = GetTextBuffer();
        if (tb.Length + text.Length > tb.Capacity)
            return UnsafeSpan<char>.Empty;
        return tb.AddRange(text);
    }

    public static bool IsHovered() => GetElementState(ref GetSelf()).IsHovered;
    public static bool WasPressed() => GetElementState(ref GetSelf()).IsPressed;
    public static bool IsDown() => GetElementState(ref GetSelf()).IsDown;

    public static float GetScrollOffset(CanvasId canvasId, ElementId elementId)
    {
        if (canvasId == CanvasId.None) return 0;
        if (elementId == CanvasId.None) return 0;
        ref var es = ref GetElementState(canvasId, elementId);
        ref var e = ref GetElement(es.Index);
        Debug.Assert(e.Type == ElementType.Scrollable, "GetScrollOffset called on non-scrollable element");
        return es.Data.Scrollable.Offset;
    }

    public static void SetScrollOffset(CanvasId canvasId, ElementId elementId, float offset)
    {
        if (canvasId == CanvasId.None) return;
        if (elementId == CanvasId.None) return;
        ref var es = ref GetElementState(canvasId, elementId);
        ref var e = ref GetElement(es.Index);
        Debug.Assert(e.Type == ElementType.Scrollable, "SetScrollOffset called on non-scrollable element");
        es.Data.Scrollable.Offset = offset;
    }

    public static Vector2 ScreenToUI(Vector2 screenPos) =>
        screenPos / Application.WindowSize * _size;

    public static Vector2 ScreenToElement(Vector2 screen)
    {
        if (!HasCurrentElement()) return Vector2.Zero;
        ref var e = ref GetSelf();
        return Vector2.Transform(Camera!.ScreenToWorld(screen), e.WorldToLocal);
    }

    public static bool HasFocus()
    {
        if (!HasCurrentElement()) return false;
        ref var e = ref GetSelf();
        return _focusId != 0 && e.Id == _focusId && e.CanvasId == _focusCanvasId;
    }

    public static void SetFocus(byte elementId)
    {
        _focusId = elementId;
        _pendingFocusId = elementId;

        // Set canvas ID from current element context
        if (HasCurrentElement())
        {
            ref var e = ref GetSelf();
            _focusCanvasId = e.CanvasId;
            _pendingFocusCanvasId = e.CanvasId;
        }
    }

    public static void SetFocus(byte elementId, byte canvasId)
    {
        _focusId = elementId;
        _pendingFocusId = elementId;
        _focusCanvasId = canvasId;
        _pendingFocusCanvasId = canvasId;
    }
    
    public static void ClearFocus()
    {
        UI.SetFocus(0, 0);
    }

    public static bool IsClosed() =>
        HasCurrentElement() && GetSelf().Type == ElementType.Popup && _closePopups;

    public static bool IsFocus(byte id, byte canvasId) => 
        id != 0 &&
        _focusId != 0 &&
        _focusId == id &&
        _focusCanvasId == canvasId;


    internal static void Begin()
    {
        _refSize = GetRefSize();
        _elementStackCount = 0;
        _elementCount = 0;
        _popupCount = 0;
        _currentTextBuffer = 1 - _currentTextBuffer;
        GetTextBuffer().Clear();

        _hotCanvasId = CanvasId.None;
        _currentCanvasId = CanvasId.None;

        var screenSize = Application.WindowSize;
        var rw = (float)_refSize.X;
        var rh = (float)_refSize.Y;
        var sw = screenSize.X;
        var sh = screenSize.Y;

        if (rw > 0 && rh > 0)
        {
            var aspectRef = rw / rh;
            var aspectScreen = sw / sh;

            if (aspectScreen >= aspectRef)
            {
                _size.Y = rh;
                _size.X = rh * aspectScreen;
            }
            else
            {
                _size.X = rw;
                _size.Y = rw / aspectScreen;
            }
        }
        else if (rw > 0)
        {
            _size.X = rw;
            _size.Y = rw * (sh / sw);
        }
        else if (rh > 0)
        {
            _size.Y = rh;
            _size.X = rh * (sw / sh);
        }
        else
        {
            _size.X = sw;
            _size.Y = sh;
        }

        Camera!.SetExtents(new Rect(0, 0, _size.X, _size.Y));
        Camera!.Update();

        // Apply pending focus (element ID + canvas ID)
        _focusId = _pendingFocusId;
        _focusCanvasId = _pendingFocusCanvasId;
    }

    public static AutoCanvas BeginCanvas(CanvasStyle style = default, CanvasId id = default)
    {
        ref var e = ref CreateElement(ElementType.Canvas);
        e.Data.Canvas = style.ToData();

        if (id != CanvasId.None)
        {
            _currentCanvasId = id;
            ref var cs = ref GetCanvasState(id);
            cs.ElementIndex = e.Index;
        }

        PushElement(e.Index);
        return new AutoCanvas();
    }

    public static void EndCanvas()
    {
        EndElement(ElementType.Canvas);
        _currentCanvasId = CanvasId.None;
    }

    public static AutoContainer BeginContainer(ElementId id=default)
    {
        ref var e = ref CreateElement(ElementType.Container);
        e.Data.Container = ContainerData.Default;
        SetId(ref e, _currentCanvasId, id);
        PushElement(e.Index);
        return new AutoContainer();
    }

    public static AutoContainer BeginContainer(in ContainerStyle style, ElementId id = default)
    {
        ref var e = ref CreateElement(ElementType.Container);
        e.Data.Container = style.ToData();
        SetId(ref e, _currentCanvasId, id);
        PushElement(e.Index);
        return new AutoContainer();
    }

    public static void EndContainer() => EndElement(ElementType.Container);

    public static void Container(byte id=0)
    {
        BeginContainer(id:id);
        EndContainer();
    }

    public static void Container(ContainerStyle style, ElementId id = default)
    {
        BeginContainer(style, id:id);
        EndContainer();
    }

    public static AutoColumn BeginColumn(ElementId id = default)
    {
        ref var e = ref CreateElement(ElementType.Column);
        e.Data.Container = ContainerData.Default;
        SetId(ref e, _currentCanvasId, id);
        PushElement(e.Index);
        return new AutoColumn();
    }

    public static AutoColumn BeginColumn(ContainerStyle style, ElementId id = default)
    {
        ref var e = ref CreateElement(ElementType.Column);
        e.Data.Container = style.ToData();
        SetId(ref e, _currentCanvasId, id);
        PushElement(e.Index);
        return new AutoColumn();
    }

    public static void EndColumn() => EndElement(ElementType.Column);

    public static AutoRow BeginRow(ElementId id = default)
    {
        ref var e = ref CreateElement(ElementType.Row);
        e.Data.Container = ContainerData.Default;
        SetId(ref e, _currentCanvasId, id);
        PushElement(e.Index);
        return new AutoRow();
    }

    public static AutoRow BeginRow(ContainerStyle style, ElementId id = default)
    {
        ref var e = ref CreateElement(ElementType.Row);
        e.Data.Container = style.ToData();
        SetId(ref e, _currentCanvasId, id);
        PushElement(e.Index);
        return new AutoRow();
    }

    public static void EndRow() => EndElement(ElementType.Row);

    public static void BeginCenter()
    {
        ref var e = ref CreateElement(ElementType.Container);
        e.Data.Container = ContainerData.Default;
        e.Data.Container.AlignX = Align.Center;
        e.Data.Container.AlignY = Align.Center;
        PushElement(e.Index);
    }

    public static void EndCenter() => EndElement(ElementType.Container);

    public static AutoFlex BeginFlex() => BeginFlex(1.0f);
    public static AutoFlex BeginFlex(float flex)
    {
        if (!HasCurrentElement())
            throw new InvalidOperationException("Flex must be inside a Row or Column");
        ref var parent = ref GetSelf();
        if (parent.Type != ElementType.Row && parent.Type != ElementType.Column)
            throw new InvalidOperationException("Flex must be inside a Row or Column");

        ref var e = ref CreateElement(ElementType.Flex);
        e.Data.Flex.Flex = flex;
        e.Data.Flex.Axis = parent.Type == ElementType.Row ? 0 : 1;
        PushElement(e.Index);
        return new AutoFlex();
    }

    public static void EndFlex() => EndElement(ElementType.Flex);

    public static void Flex() => Flex(1.0f);
    public static void Flex(float flex)
    {
        BeginFlex(flex);
        EndFlex();
    }

    public static void Spacer(float size)
    {
        if (!HasCurrentElement())
            throw new InvalidOperationException("Spacer must be inside a Row or Column");
        ref var parent = ref GetSelf();
        if (parent.Type != ElementType.Row && parent.Type != ElementType.Column)
            throw new InvalidOperationException("Spacer must be inside a Row or Column");

        ref var e = ref CreateElement(ElementType.Spacer);
        e.Data.Spacer.Size = parent.Type == ElementType.Row ? new Vector2(size, 0) : new Vector2(0, size);

        PushElement(e.Index);
        PopElement();
    }

    public static void BeginBorder(BorderStyle style)
    {
        ref var e = ref CreateElement(ElementType.Container);
        e.Data.Container = ContainerData.Default;
        e.Data.Container.Border = style;
        PushElement(e.Index);
    }

    public static void EndBorder() => EndElement(ElementType.Container);

    public static void BeginTransformed(TransformStyle style)
    {
        ref var e = ref CreateElement(ElementType.Transform);
        e.Data.Transform = new TransformData
        {
            Origin = style.Origin,
            Translate = style.Translate,
            Rotate = style.Rotate,
            Scale = style.Scale
        };
        PushElement(e.Index);
    }

    public static void EndTransformed() => EndElement(ElementType.Transform);

    public static AutoScrollable BeginScrollable(ElementId id)
    {
        Debug.Assert(id != ElementId.None);
        ref var e = ref CreateElement(ElementType.Scrollable);
        e.Data.Scrollable.ContentHeight = 0;
        SetId(ref e, _currentCanvasId, id);
        PushElement(e.Index);
        return new AutoScrollable();
    }

    public static void EndScrollable() => EndElement(ElementType.Scrollable);

    public static void BeginGrid(GridStyle style)
    {
#if false
        ref var e = ref CreateElement(ElementType.Grid);
        e.Data.Grid = new GridData
        {
            Spacing = style.Spacing,
            Columns = style.Columns,
            CellWidth = style.CellWidth,
            CellHeight = style.CellHeight,
            VirtualCount = style.VirtualCount,
            StartRow = 0
        };
        PushElement(e.Index);

        if (style.VirtualCount == 0) return;

        var containerHeight = GetFixedParentHeight();
        if (IsAuto(containerHeight)) return;

        var rowHeight = style.CellHeight + style.Spacing;
        var scrollOffset = GetScrollOffset(style.ScrollId);

        var startRow = Math.Max(0, (int)MathF.Floor(scrollOffset / rowHeight));
        var visibleRows = (int)MathF.Ceiling(containerHeight / rowHeight) + 1;
        var endRow = startRow + visibleRows;

        var startIndex = startRow * style.Columns;
        var endIndex = Math.Min(endRow * style.Columns, style.VirtualCount);

        e.Data.Grid.StartRow = startRow;

        style.VirtualRangeFunc?.Invoke(startIndex, endIndex);

        for (var virtualIndex = startIndex; virtualIndex < endIndex; virtualIndex++)
        {
            var cellIndex = virtualIndex - startIndex;
            style.VirtualCellFunc?.Invoke(cellIndex, virtualIndex);
        }
#endif
    }

    public static void EndGrid() => EndElement(ElementType.Grid);

    public static void BeginPopup(PopupStyle style)
    {
        ref var e = ref CreateElement(ElementType.Popup);
        e.Data.Popup = new PopupData
        {
            AnchorX = style.AnchorX,
            AnchorY = style.AnchorY,
            PopupAlignX = style.PopupAlignX,
            PopupAlignY = style.PopupAlignY,
            Margin = style.Margin
        };
        PushElement(e.Index);
        _popups[_popupCount++] = e.Index;
    }

    public static void EndPopup() => EndElement(ElementType.Popup);

    public static void Label(string text, LabelStyle style = default)
    {
        ref var e = ref CreateElement(ElementType.Label);
        e.Font = style.Font ?? _defaultFont;
        e.Data.Label = new LabelData
        {
            FontSize = style.FontSize > 0 ? style.FontSize : 16,
            Color = style.Color,
            AlignX = style.AlignX,
            AlignY = style.AlignY,
            Text = GetTextBuffer().AddRange(text)
        };

        PushElement(e.Index);
        PopElement();
    }

    public static void Image(Sprite? sprite) => Image(sprite, new ImageStyle());

    public static void Image(Sprite? sprite, in ImageStyle style)
    {
        if (sprite == null) return;

        ref var e = ref CreateElement(ElementType.Image);
        e.Sprite = sprite;
        e.Data.Image = new ImageData
        {
            Stretch = style.Stretch,
            AlignX = style.AlignX,
            AlignY = style.AlignY,
            Scale = style.Scale,
            Color = style.Color,
            Texture = Graphics.SpriteAtlas?.Handle ?? nuint.Zero,
            UV0 = sprite.UV.TopLeft,
            UV1 = sprite.UV.BottomRight,
            Width = sprite.Bounds.Width,
            Height = sprite.Bounds.Height,
            AtlasIndex = sprite.AtlasIndex
        };

        PushElement(e.Index);
        PopElement();
    }

    public static void Image(Texture? texture, float width, float height, ImageStyle style = default)
    {
        if (texture == null) return;

        ref var e = ref CreateElement(ElementType.Image);
        e.Data.Image = new ImageData
        {
            Stretch = style.Stretch,
            AlignX = style.AlignX,
            AlignY = style.AlignY,
            Scale = style.Scale,
            Color = style.Color,
            Texture = texture.Handle,
            UV0 = Vector2.Zero,
            UV1 = Vector2.One,
            Width = width,
            Height = height,
            AtlasIndex = -1
        };

        PushElement(e.Index);
        PopElement();
    }

    public static bool TextBox(
        ElementId id,
        ref string text,
        TextBoxStyle style = default,
        string? placeholder = null)
    {
        ref var e = ref CreateElement(ElementType.TextBox);
        e.Data.TextBox = style.ToData();
        e.Data.TextBox.Text = AddText(text);

        if (!string.IsNullOrEmpty(placeholder))
            e.Data.TextBox.Placeholder = AddText(placeholder);
        
        SetId(ref e, _currentCanvasId, id);

        var textChanged = false;
        var isFocused = _focusId != 0 && id == _focusId;
        if (isFocused && id != 0)
        {
            var textHash = text.GetHashCode();
            ref var state = ref _elementStates[id];
            textChanged = state.Data.TextBox.TextHash != textHash;
        }

        PushElement(e.Index);
        PopElement();

        return textChanged;
    }

    public static ReadOnlySpan<char> GetElementText(byte id)
    {
        if (id == 0) return [];
        ref var e = ref _elements[_elementStates[id].Index];
        if (e.Type == ElementType.TextBox)
            return e.Data.TextBox.Text.AsReadonlySpan();
        if (e.Type == ElementType.Label)
            return e.Data.Label.Text.AsReadonlySpan();

        return [];
    }

    internal static void End()
    {
        LayoutElements();

        Graphics.SetCamera(Camera);

        HandleInput();

        // Flush any pending world-space rendering before drawing UI
        // SetCamera also sets u_projection uniform which UIRender will use
        Graphics.SetCamera(Camera);

        DrawElements();

        // Reset textbox tracking before draw pass
        // _textboxRenderedThisFrame = false;

        //for (var popupIndex = 0; popupIndex < _popupCount; popupIndex++)
        //{
        //    var pIdx = _popups[popupIndex];
        //    ref var p = ref _elements[pIdx];
        //    for (var idx = pIdx; idx < p.NextSiblingIndex;)
        //        idx = DrawElement(idx, true);
        //}

        TextBoxEndFrame();
    }

    // Transform calculation
    private static int CalculateTransforms(int elementIndex, Matrix3x2 parentTransform)
    {
        ref var e = ref _elements[elementIndex++];

        if (e.Type == ElementType.Transform)
        {
            ref var t = ref e.Data.Transform;
            var pivot = new Vector2(e.Rect.Width * t.Origin.X, e.Rect.Height * t.Origin.Y);

            var localTransform =
                Matrix3x2.CreateTranslation(t.Translate + new Vector2(e.Rect.X, e.Rect.Y)) *
                Matrix3x2.CreateTranslation(pivot) *
                Matrix3x2.CreateRotation(t.Rotate) *
                Matrix3x2.CreateScale(t.Scale) *
                Matrix3x2.CreateTranslation(-pivot);

            e.LocalToWorld = parentTransform * localTransform;
        }
        else
        {
            e.LocalToWorld = parentTransform * Matrix3x2.CreateTranslation(e.Rect.X, e.Rect.Y);
        }

        Matrix3x2.Invert(e.LocalToWorld, out e.WorldToLocal);

        if (e.Type == ElementType.Scrollable)
        {
            var scrollTransform = e.LocalToWorld * Matrix3x2.CreateTranslation(0, -e.Data.Scrollable.Offset);
            for (var i = 0; i < e.ChildCount; i++)
                elementIndex = CalculateTransforms(elementIndex, scrollTransform);
        }
        else
        {
            for (var i = 0; i < e.ChildCount; i++)
                elementIndex = CalculateTransforms(elementIndex, e.LocalToWorld);
        }

        return elementIndex;
    }



    [Conditional("NOZ_UI_DEBUG")]
    private static void LogUI(string msg, int depth=0, Func<bool>? condition = null, (string name, object? value, bool condition)[]? values = null)
    {
        if (condition == null || condition())
            Log.Debug($"[UI] {new string(' ', depth)}{msg}{Log.Params(values)}");
    }

    [Conditional("NOZ_UI_DEBUG")]
    private static void LogUI(in Element e, string msg, int depth=0, Func<bool>? condition = null, (string name, object? value, bool condition)[]? values = null)
    {
        if (condition == null || condition())
            Log.Debug($"[UI]   {new string(' ', GetDepth(in e) * 2 + depth * 2)}{msg}{Log.Params(values)}");
    }
}
