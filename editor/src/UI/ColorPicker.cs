//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal struct ColorButtonStyle()
{
    public PopupStyle Popup = EditorStyle.PopupLeft;
    public bool FillWidth = false;
    public bool ShowAlpha = true;
    public bool ShowHDR = false;
    public bool ShowClose = true;
}

internal static partial class ColorPicker
{
    private const float SVSize = EditorStyle.ColorPicker.SVSize;
    private const float SliderHeight = EditorStyle.ColorPicker.SliderHeight;
    private const float ThumbSize = SliderHeight - 2;
    private const float ThumbRadius = ThumbSize / 2;

    private static partial class ElementId
    {
        public static partial WidgetId Hue { get; }
        public static partial WidgetId Saturation { get; }
        public static partial WidgetId Value { get; }
        public static partial WidgetId Alpha { get; }
        public static partial WidgetId Close { get; }
        public static partial WidgetId SaturationAndValue { get; }
        public static partial WidgetId ModeNone { get; }
        public static partial WidgetId ModeColor { get; }
        public static partial WidgetId PaletteDropDown { get; }
        public static partial WidgetId ColorPickerPaletteScroll { get; }
        public static partial WidgetId ColorPickerPaletteItem { get; }
        public static partial WidgetId Intensity { get; }
        public static partial WidgetId EyeDropper { get; }
        public static partial WidgetId PaletteToggle { get; }
        public static partial WidgetId Popup { get; }
        public static partial WidgetId SwatchGrid { get; }
        public static partial WidgetId Hex { get; }
        public static partial WidgetId InputR { get; }
        public static partial WidgetId InputG { get; }
        public static partial WidgetId InputB { get; }
        public static partial WidgetId InputH { get; }
        public static partial WidgetId InputS { get; }
        public static partial WidgetId InputV { get; }
        public static partial WidgetId InputA { get; }
    }

    private enum ColorMode
    {
        None,
        Color,
    }

    private static WidgetId _popupId;
    private static float _hue;
    private static float _sat;
    private static float _val;
    private static float _alpha;
    private static float _intensity; // HDR: pow(2, _intensity) multiplier
    private static Color32 _originalColor;

    // Textures
    private static Texture? _svTexture;
    private static Texture? _hueTexture;
    private static Texture? _checkerTexture;
    private static float _svTextureHue = -1;

    // Reusable pixel buffers (native memory, no GC pressure)
    private static PixelData<Color32>? _svPixels;
    private static PixelData<Color32>? _huePixels;
    private static PixelData<Color32>? _checkerPixels;

    private static Color32 _prevColor;

    private static ColorMode _paletteMode = ColorMode.None;
    private static float _savedAlpha;
    private static int _selectedPaletteIndex;
    private static bool _paletteView;

    // Eyedropper state
    private static bool _eyeDropperActive;
    private static bool _eyeDropperMouseWasDown;

    private static bool _hdr;
    private static bool _showAlpha;
    private static bool _showClose;

    // Popup
    private static PopupStyle _popupStyle;

    public static bool IsOpen(WidgetId id) => _popupId == id;

    internal static void Close()
    {
        _popupId = WidgetId.None;
        _eyeDropperActive = false;
        _eyeDropperMouseWasDown = false;
        UI.ClearHot();
    }

    private static void PickEyeDropperColor()
    {
        Input.ConsumeButton(InputCode.MouseLeft);

        var color = Workspace.ReadPixelAtMouse();
        if (color.A > 0)
        {
            RgbToHsv(color, out _hue, out _sat, out _val);
            _alpha = color.A / 255f;
            InvalidateSVTexture();

            if (_paletteMode == ColorMode.None)
                _paletteMode = ColorMode.Color;

            if (_paletteView && !IsColorInCurrentPalette(color))
                _paletteView = false;
        }

        _eyeDropperActive = false;
    }

    private static bool IsColorInCurrentPalette(Color32 color)
    {
        var palettes = PaletteManager.Palettes;
        if (palettes.Count == 0 || _selectedPaletteIndex < 0 || _selectedPaletteIndex >= palettes.Count)
            return false;

        var palette = palettes[_selectedPaletteIndex];
        for (int i = 0; i < palette.Count; i++)
        {
            var swatch = palette.Colors[i].ToColor32();
            if (swatch.A > 0 && swatch.R == color.R && swatch.G == color.G && swatch.B == color.B)
                return true;
        }
        return false;
    }

    internal static void Open(WidgetId id, Color color, ColorButtonStyle? style = null)
    {
        var resolved = style ?? new ColorButtonStyle();

        _hdr = resolved.ShowHDR;
        _showAlpha = resolved.ShowAlpha;    
        _showClose = resolved.ShowClose;

        if (_hdr)
        {
            var maxComponent = MathF.Max(color.R, MathF.Max(color.G, color.B));
            if (maxComponent > 1f)
            {
                _intensity = MathF.Log2(maxComponent);
                var factor = MathF.Pow(2f, _intensity);
                color = new Color(color.R / factor, color.G / factor, color.B / factor, color.A);
            }
            else
            {
                _intensity = 0f;
            }
        }
        else
        {
            _intensity = 0f;
        }

        var c32 = color.ToColor32();
        _popupId = id;
        _originalColor = c32;
        _prevColor = c32;
        UI.SetHot<Color32>(id, c32);
        RgbToHsv(c32, out _hue, out _sat, out _val);
        _alpha = c32.A / 255f;

        if (c32.A == 0)
            _paletteMode = ColorMode.None;
        else
            _paletteMode = ColorMode.Color;

        var s = style ?? new ColorButtonStyle();
        _popupStyle = s.Popup;
        _popupStyle.AnchorRect = UI.GetElementWorldRect(id);
    }

    internal static void Update()
    {
        if (_popupId == WidgetId.None) return;

        var close = false;

        if (_eyeDropperActive)
            EditorCursor.SetDropper();

        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            Input.ConsumeButton(InputCode.KeyEscape);
            if (_eyeDropperActive)
            {
                _eyeDropperActive = false;
            }
            else
            {
                // Cancel: restore original color
                _prevColor = _originalColor;
                close = true;
            }
        }

        if (_eyeDropperActive && Input.WasButtonPressed(InputCode.MouseRight))
        {
            Input.ConsumeButton(InputCode.MouseRight);
            _eyeDropperActive = false;
        }

        // Eyedropper: detect clicks via physical state edge detection.
        // WasButtonPressedRaw is unreliable here because the UI widget system's
        // HandleInput consumes the button when the eye dropper button is clicked,
        // and the Consumed flag can persist into subsequent frames.
        if (_eyeDropperActive)
        {
            var mouseDown = Input.IsButtonDownRaw(InputCode.MouseLeft);
            if (mouseDown && !_eyeDropperMouseWasDown)
                PickEyeDropperColor();
            _eyeDropperMouseWasDown = mouseDown;
        }

        var wasEyeDropperActive = _eyeDropperActive;
        if (wasEyeDropperActive)
            ElementTree.BeginCursor(new SpriteCursor(EditorAssets.Sprites.CursorDropper));
        else
            ElementTree.BeginCursor(new SpriteCursor(EditorAssets.Sprites.CursorArrow));

        _popupStyle.AutoClose = !_eyeDropperActive;
        UI.BeginPopup(ElementId.Popup, _popupStyle);

        if (UI.IsClosed())
            close = true;

        using (UI.BeginColumn(EditorStyle.ColorPicker.Root))
        {
            var inColorMode = _paletteMode == ColorMode.Color;

            if (_showAlpha || _showClose || inColorMode)
            {
                using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing, Height = EditorStyle.Control.Height }))
                {
                    if (_showAlpha)
                    {
                        if (UI.Button(ElementId.ModeNone, EditorAssets.Sprites.IconNofill, EditorStyle.Button.ToggleIcon, isSelected: _paletteMode == ColorMode.None))
                        {
                            _savedAlpha = _alpha;
                            _paletteMode = ColorMode.None;
                            UI.ClosePopupMenu();
                        }

                        if (UI.Button(ElementId.ModeColor, EditorAssets.Sprites.IconFill, EditorStyle.Button.ToggleIcon, isSelected: inColorMode && !_paletteView))
                        {
                            _paletteMode = ColorMode.Color;
                            _paletteView = false;
                            if (_alpha == 0)
                                _alpha = _savedAlpha > 0 ? _savedAlpha : 1;
                            UI.ClosePopupMenu();
                        }
                    }

                    if (inColorMode)
                    {
                        if (UI.Button(ElementId.PaletteToggle, EditorAssets.Sprites.IconPalette, EditorStyle.Button.ToggleIcon, isSelected: _paletteView))
                        {
                            _paletteView = true;
                            UI.ClosePopupMenu();
                        }

                        if (UI.Button(ElementId.EyeDropper, EditorAssets.Sprites.CursorDropper, EditorStyle.Button.ToggleIcon, isSelected: _eyeDropperActive))
                        {
                            _eyeDropperActive = !_eyeDropperActive;
                            _eyeDropperMouseWasDown = Input.IsButtonDownRaw(InputCode.MouseLeft);
                        }
                    }

                    if (_showClose)
                    {
                        UI.Flex();
                        if (UI.Button(ElementId.Close, EditorAssets.Sprites.IconClose, EditorStyle.Button.IconOnly))
                            close = true;
                    }
                }
            }

            Color32 color;

            if (inColorMode)
            {
                if (_paletteView)
                {
                    PaletteContent();
                }
                else
                {
                    SaturationAndValue();
                    Hue();
                    Saturation();
                    Value();
                }

                Alpha();
                if (_hdr)
                    Intensity();

                color = HsvToColor32(_hue, _sat, _val, _alpha);

                if (!_paletteView)
                    HexAndRgbRow(ref color);
            }
            else
            {
                color = Color.Transparent;
            }

            if (color != _prevColor)
            {
                UI.NotifyChanged(color);
                _prevColor = color;
            }
        }

        UI.EndPopup();
        ElementTree.EndCursor();

        if (close)
            Close();
    }

    private static Color GetResultColor()
    {
        var baseColor = _prevColor.ToColor();
        if (_hdr && _intensity > 0f)
        {
            var factor = MathF.Pow(2f, _intensity);
            return new Color(baseColor.R * factor, baseColor.G * factor, baseColor.B * factor, baseColor.A);
        }
        return baseColor;
    }

    internal static void Popup(WidgetId id, ref Color color)
    {
        if (_popupId != id) return;
        color = GetResultColor();
    }

    private static void SaturationAndValue()
    {
        EnsureSVTexture();

        ref var trackState = ref ElementTree.BeginWidget<TrackState>(ElementId.SaturationAndValue);
        ElementTree.BeginTrack(ref trackState, ElementId.SaturationAndValue, 1, 1);

        if (UI.HasCapture(ElementId.SaturationAndValue))
        {
            _sat = trackState.X;
            _val = 1 - trackState.Y;
        }
        else
        {
            trackState.X = _sat;
            trackState.Y = 1 - _val;
        }

        using (UI.BeginContainer(EditorStyle.ColorPicker.SaturationAndValue))
        {
            if (_svTexture != null)
                UI.Image(_svTexture, ImageStyle.Fill);

            var svMargin = (SVSize - EditorStyle.ColorPicker.SliderWidth) / 2;
            DrawCrosshair(
                _sat * EditorStyle.ColorPicker.SliderWidth + svMargin,
                (1 - _val) * SVSize);
        }

        ElementTree.EndTrack();
        ElementTree.EndWidget();
    }

    private static void Hue()
    {
        EnsureHueTexture();

        using (UI.BeginRow(EditorStyle.ColorPicker.SliderRow))
        {
            ref var trackState = ref ElementTree.BeginWidget<TrackState>(ElementId.Hue);
            ElementTree.BeginTrack(ref trackState, ElementId.Hue, ThumbSize);

            if (UI.HasCapture(ElementId.Hue))
            {
                var newHue = trackState.X * 360f;
                if (newHue != _hue)
                {
                    _hue = newHue;
                    InvalidateSVTexture();
                }
            }
            else
            {
                trackState.X = _hue / 360f;
            }

            using (UI.BeginContainer(EditorStyle.ColorPicker.Slider))
            {
                if (_hueTexture != null)
                    UI.Image(_hueTexture, EditorStyle.ColorPicker.SliderImage);

                var color = HsvToColor(_hue, 1f, 1f);
                SliderThumb(_hue / 360f, color, Color.Black);
            }

            ElementTree.EndTrack();
            ElementTree.EndWidget();

            int hueInt = (int)MathF.Round(_hue);
            if (UI.NumberInput(ElementId.InputH, ref hueInt, EditorStyle.ColorPicker.ChannelInput, min: 0, max: 360))
            {
                _hue = hueInt;
                InvalidateSVTexture();
            }
        }
    }

    private static void Saturation()
    {
        using (UI.BeginRow(EditorStyle.ColorPicker.SliderRow))
        {
            ref var trackState = ref ElementTree.BeginWidget<TrackState>(ElementId.Saturation);
            ElementTree.BeginTrack(ref trackState, ElementId.Saturation, ThumbSize);

            if (UI.HasCapture(ElementId.Saturation))
                _sat = trackState.X;
            else
                trackState.X = _sat;

            var lowColor = HsvToColor(_hue, 0f, _val);
            var highColor = HsvToColor(_hue, 1f, _val);

            using (UI.BeginContainer(new ContainerStyle
            {
                Width = EditorStyle.ColorPicker.SliderTrackWidth,
                Height = SliderHeight,
                AlignY = Align.Center,
                Background = new BackgroundStyle
                {
                    Color = lowColor,
                    GradientColor = highColor,
                    GradientAngle = 0
                },
                BorderRadius = EditorStyle.ColorPicker.SliderImage.BorderRadius,
                Clip = true,
            }))
            {
                var thumbColor = HsvToColor(_hue, _sat, _val);
                SliderThumb(_sat, thumbColor, Color.Black);
            }

            ElementTree.EndTrack();
            ElementTree.EndWidget();

            int satInt = (int)MathF.Round(_sat * 255f);
            if (UI.NumberInput(ElementId.InputS, ref satInt, EditorStyle.ColorPicker.ChannelInput, min: 0, max: 255))
                _sat = satInt / 255f;
        }
    }

    private static void Value()
    {
        using (UI.BeginRow(EditorStyle.ColorPicker.SliderRow))
        {
            ref var trackState = ref ElementTree.BeginWidget<TrackState>(ElementId.Value);
            ElementTree.BeginTrack(ref trackState, ElementId.Value, ThumbSize);

            if (UI.HasCapture(ElementId.Value))
                _val = trackState.X;
            else
                trackState.X = _val;

            var lowColor = HsvToColor(_hue, _sat, 0f);
            var highColor = HsvToColor(_hue, _sat, 1f);

            using (UI.BeginContainer(new ContainerStyle
            {
                Width = EditorStyle.ColorPicker.SliderTrackWidth,
                Height = SliderHeight,
                AlignY = Align.Center,
                Background = new BackgroundStyle
                {
                    Color = lowColor,
                    GradientColor = highColor,
                    GradientAngle = 0
                },
                BorderRadius = EditorStyle.ColorPicker.SliderImage.BorderRadius,
                Clip = true,
            }))
            {
                var thumbColor = HsvToColor(_hue, _sat, _val);
                SliderThumb(_val, thumbColor, Color.Black);
            }

            ElementTree.EndTrack();
            ElementTree.EndWidget();

            int valInt = (int)MathF.Round(_val * 255f);
            if (UI.NumberInput(ElementId.InputV, ref valInt, EditorStyle.ColorPicker.ChannelInput, min: 0, max: 255))
                _val = valInt / 255f;
        }
    }

    private static void Alpha()
    {
        if (!_showAlpha) return;

        EnsureCheckerTexture();

        using (UI.BeginRow(EditorStyle.ColorPicker.SliderRow))
        {
            ref var trackState = ref ElementTree.BeginWidget<TrackState>(ElementId.Alpha);
            ElementTree.BeginTrack(ref trackState, ElementId.Alpha, ThumbSize);

            if (UI.HasCapture(ElementId.Alpha))
                _alpha = trackState.X;
            else
                trackState.X = _alpha;

            using (UI.BeginContainer(EditorStyle.ColorPicker.Slider))
            {
                if (_checkerTexture != null)
                    UI.Image(_checkerTexture, EditorStyle.ColorPicker.SliderImage);

                SliderThumb(
                    _alpha,
                    Color.Mix(Color.Black, Color.White, _alpha),
                    Color.Mix(Color.White, Color.Black, _alpha));
            }

            ElementTree.EndTrack();
            ElementTree.EndWidget();

            int alphaInt = (int)MathF.Round(_alpha * 255f);
            if (UI.NumberInput(ElementId.InputA, ref alphaInt, EditorStyle.ColorPicker.ChannelInput, min: 0, max: 255))
                _alpha = alphaInt / 255f;
        }
    }

    private static void Intensity()
    {
        ref var trackState = ref ElementTree.BeginWidget<TrackState>(ElementId.Intensity);
        ElementTree.BeginTrack(ref trackState, ElementId.Intensity, ThumbSize);

        // Map intensity 0..10 to track 0..1
        const float maxIntensity = 10f;
        if (UI.HasCapture(ElementId.Intensity))
            _intensity = trackState.X * maxIntensity;
        else
            trackState.X = _intensity / maxIntensity;

        var baseColor = HsvToColor(_hue, _sat, _val);
        var maxFactor = MathF.Pow(2f, maxIntensity);
        var brightColor = new Color(
            MathF.Min(baseColor.R * maxFactor, 1f),
            MathF.Min(baseColor.G * maxFactor, 1f),
            MathF.Min(baseColor.B * maxFactor, 1f));

        using (UI.BeginContainer(new ContainerStyle
        {
            Height = SliderHeight,
            Background = new BackgroundStyle
            {
                Color = baseColor,
                GradientColor = brightColor,
                GradientAngle = 0
            },
            Clip = true
        }))
        {
            var t = _intensity / maxIntensity;
            var factor = MathF.Pow(2f, _intensity);
            var thumbColor = new Color(
                MathF.Min(baseColor.R * factor, 1f),
                MathF.Min(baseColor.G * factor, 1f),
                MathF.Min(baseColor.B * factor, 1f));
            SliderThumb(t, thumbColor, Color.Black);
        }

        ElementTree.EndTrack();
        ElementTree.EndWidget();
    }

    private static void HexAndRgbRow(ref Color32 color)
    {
        Span<char> hexBuf = stackalloc char[6];
        Strings.ColorHex(color, hexBuf);
        var hexStr = new string(hexBuf);

        using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing, Height = 20 }))
        {
            using (UI.BeginFlex())
            {
                var newHex = UI.TextInput(ElementId.Hex, hexStr, EditorStyle.ColorPicker.HexInput, "#HEX");
                if (newHex != hexStr && Color32.TryParseHex(newHex, out var parsed))
                {
                    RgbToHsv(parsed, out _hue, out _sat, out _val);
                    InvalidateSVTexture();
                    color = new Color32(parsed.R, parsed.G, parsed.B, color.A);
                }
            }

            int r = color.R, g = color.G, b = color.B;

            if (UI.NumberInput(ElementId.InputR, ref r, EditorStyle.ColorPicker.RgbInput, min: 0, max: 255))
            {
                RgbToHsv(new Color32((byte)r, (byte)g, (byte)b), out _hue, out _sat, out _val);
                InvalidateSVTexture();
                color = new Color32((byte)r, color.G, color.B, color.A);
            }

            if (UI.NumberInput(ElementId.InputG, ref g, EditorStyle.ColorPicker.RgbInput, min: 0, max: 255))
            {
                RgbToHsv(new Color32((byte)r, (byte)g, (byte)b), out _hue, out _sat, out _val);
                InvalidateSVTexture();
                color = new Color32(color.R, (byte)g, color.B, color.A);
            }

            if (UI.NumberInput(ElementId.InputB, ref b, EditorStyle.ColorPicker.RgbInput, min: 0, max: 255))
            {
                RgbToHsv(new Color32((byte)r, (byte)g, (byte)b), out _hue, out _sat, out _val);
                InvalidateSVTexture();
                color = new Color32(color.R, color.G, (byte)b, color.A);
            }
        }
    }

    private static void SliderThumb(float t, Color color, Color borderColor)
    {
        var travel = EditorStyle.ColorPicker.SliderTrackWidth - ThumbSize;

        UI.Container(new ContainerStyle
        {
            Size = ThumbSize,
            Background = color,
            BorderRadius = ThumbRadius,
            BorderColor = borderColor,
            BorderWidth = 1.5f,
            Margin = EdgeInsets.TopLeft(1, t * travel)
        });
    }

    private static UI.AutoContainer BeginColorContainer(WidgetId id, bool selected)
    {
        var container = UI.BeginContainer(id, new ContainerStyle
        {
            Padding = EdgeInsets.All(1),
        });

        if (selected || UI.IsHovered())
            UI.Container(new ContainerStyle()
            {
                Background = EditorStyle.Palette.Primary,
                BorderRadius = EditorStyle.Control.BorderRadius,
                Margin = EdgeInsets.All(-3)
            });

        return container;
    }

    private static void PaletteContent()
    {
        var palettes = PaletteManager.Palettes;
        if (palettes.Count == 0) return;

        if (_selectedPaletteIndex < 0 || _selectedPaletteIndex >= palettes.Count)
            _selectedPaletteIndex = 0;

        var selectedPalette = palettes[_selectedPaletteIndex];

        UI.DropDown(ElementId.PaletteDropDown, () =>
        {
            var items = new PopupMenuItem[palettes.Count];
            for (int i = 0; i < palettes.Count; i++)
            {
                var index = i;
                items[i] = PopupMenuItem.Item(palettes[i].Label, () => _selectedPaletteIndex = index);
            }
            return items;
        }, text: selectedPalette.Label);

        var nextPaletteItemId = ElementId.ColorPickerPaletteItem;

        var swatchLayout = new CollectionLayout
        {
            ItemHeight = EditorStyle.ColorPicker.SwatchCellSize,
            ItemWidth = EditorStyle.ColorPicker.SwatchCellSize,
            Columns = EditorStyle.ColorPicker.SwatchColumns
        };
        using var grid = UI.BeginCollection(ElementId.SwatchGrid, swatchLayout, selectedPalette.Count, out var swatchStart, out var swatchEnd);

        for (int i = 0; i < selectedPalette.Count; i++)
        {
            var swatchColor = selectedPalette.Colors[i];
            if (swatchColor.A <= float.Epsilon) continue;

            var itemId = nextPaletteItemId++;
            var c32 = swatchColor.ToColor32();
            var isSelected = IsSwatchSelected(c32);

            using (BeginColorContainer(itemId, isSelected))
            {
                UI.Container(new ContainerStyle
                {
                    Background = swatchColor,
                    BorderRadius = EditorStyle.Control.BorderRadius - 2
                });

                if (UI.WasPressed())
                {
                    RgbToHsv(c32, out _hue, out _sat, out _val);
                    _alpha = c32.A / 255f;
                    InvalidateSVTexture();
                }
            }
        }
    }

    private static bool IsSwatchSelected(Color32 swatch)
    {
        var current = HsvToColor32(_hue, _sat, _val, _alpha);
        return swatch.R == current.R && swatch.G == current.G && swatch.B == current.B;
    }

    private static void DrawCrosshair(float x, float y)
    {
        const float size = 6;
        UI.Container(new ContainerStyle
        {
            Width = size * 2,
            Height = size * 2,
            BorderWidth = 1.5f,
            BorderColor = Color.White,
            BorderRadius = size,
            Background = Color.Transparent,
            Margin = new EdgeInsets(y - size, x - size, 0, 0)
        });
    }

    private static void EnsureCheckerTexture()
    {
        if (_checkerTexture != null) return;

        const int w = 40;
        const int h = 3;
        var light = new Color32(255, 255, 255);
        var dark = new Color32(204, 204, 204);

        _checkerPixels ??= new PixelData<Color32>(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                _checkerPixels[x, y] = (x + y) % 2 == 0 ? light : dark;

        _checkerTexture = Texture.Create(w, h, _checkerPixels.AsReadonlySpan(),
            TextureFormat.RGBA8, TextureFilter.Point, "EditorCheckerboard");
    }

    private static void EnsureHueTexture()
    {
        if (_hueTexture != null) return;

        const int w = 256;
        const int h = 1;
        _huePixels ??= new PixelData<Color32>(w, h);

        for (int x = 0; x < w; x++)
            _huePixels[x, 0] = HsvToColor32(x / (float)w * 360f, 1f, 1f, 1f);

        _hueTexture = Texture.Create(w, h, _huePixels.AsReadonlySpan(), TextureFormat.RGBA8, TextureFilter.Linear, "EditorHueBar");
    }

    private static void EnsureSVTexture()
    {
        if (_svTexture != null && MathEx.Approximately(_svTextureHue, _hue))
            return;

        const int size = 64;
        _svPixels ??= new PixelData<Color32>(size, size);

        for (int y = 0; y < size; y++)
        {
            float v = 1f - y / (float)(size - 1);
            for (int x = 0; x < size; x++)
                _svPixels[x, y] = HsvToColor32(_hue, x / (float)(size - 1), v, 1f);
        }

        if (_svTexture == null)
            _svTexture = Texture.Create(size, size, _svPixels.AsReadonlySpan(), TextureFormat.RGBA8, TextureFilter.Linear, "EditorSVGradient");
        else
            _svTexture.Update(_svPixels.AsReadonlySpan());

        _svTextureHue = _hue;
    }

    private static void InvalidateSVTexture()
    {
        _svTextureHue = -1;
    }

    // --- HSV <-> RGB ---

    private static Color HsvToColor(float h, float s, float v)
    {
        return HsvToColor32(h, s, v, 1f).ToColor();
    }

    private static Color32 HsvToColor32(float h, float s, float v, float a)
    {
        h = ((h % 360f) + 360f) % 360f;
        float c = v * s;
        float x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        float m = v - c;

        float r, g, b;
        if (h < 60f) { r = c; g = x; b = 0; }
        else if (h < 120f) { r = x; g = c; b = 0; }
        else if (h < 180f) { r = 0; g = c; b = x; }
        else if (h < 240f) { r = 0; g = x; b = c; }
        else if (h < 300f) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return new Color32(
            (byte)((r + m) * 255f + 0.5f),
            (byte)((g + m) * 255f + 0.5f),
            (byte)((b + m) * 255f + 0.5f),
            (byte)(a * 255f + 0.5f)
        );
    }

    private static void RgbToHsv(Color32 c, out float h, out float s, out float v)
    {
        float r = c.R / 255f;
        float g = c.G / 255f;
        float b = c.B / 255f;

        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;

        v = max;
        s = max > 0f ? delta / max : 0f;

        if (delta <= float.Epsilon)
        {
            h = 0f;
        }
        else if (max == r)
        {
            h = 60f * ((g - b) / delta % 6f);
        }
        else if (max == g)
        {
            h = 60f * ((b - r) / delta + 2f);
        }
        else
        {
            h = 60f * ((r - g) / delta + 4f);
        }

        if (h < 0f) h += 360f;
    }
}
