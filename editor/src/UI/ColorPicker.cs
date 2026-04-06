//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal static partial class ColorPicker
{
    private const float SVSize = EditorStyle.ColorPicker.SVSize;
    private const float SliderHeight = EditorStyle.ColorPicker.SliderHeight;
    private const float ThumbSize = SliderHeight - 2;
    private const float ThumbRadius = ThumbSize / 2;
    private const float SwatchCellSize = 28;

    private static partial class ElementId
    {
        public static partial WidgetId Hue { get; }
        public static partial WidgetId Alpha { get; }
        public static partial WidgetId Close { get; }
        public static partial WidgetId SaturationAndValue { get; }
        public static partial WidgetId ModeNone { get; }
        public static partial WidgetId ModeColor { get; }
        public static partial WidgetId ModePalette { get; }
        public static partial WidgetId ModeLinear { get; }
        public static partial WidgetId GradientStop0 { get; }
        public static partial WidgetId GradientStop1 { get; }
        public static partial WidgetId ColorPickerPaletteScroll { get; }
        public static partial WidgetId ColorPickerPaletteItem { get; }
        public static partial WidgetId Intensity { get; }
        public static partial WidgetId EyeDropper { get; }
    }

    private enum ColorMode
    {
        None,
        Color,
        Palette,
        Linear,
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
    private static bool _trackNeedsInit;

    // Eyedropper state
    private static bool _eyeDropperActive;
    private static Task<Color>? _eyeDropperTask;

    // Gradient state
    private static int _gradientActiveStop;  // 0 or 1
    private static SpriteFillGradient _gradient;

    // Public API for sprite editor to read gradient state
    internal static SpriteFillType ResultFillType => _paletteMode == ColorMode.Linear ? SpriteFillType.Linear : SpriteFillType.Solid;
    internal static SpriteFillGradient ResultGradient => _gradient;
    internal static int ActiveGradientStop
    {
        get => _gradientActiveStop;
        set
        {
            if (_gradientActiveStop == value) return;
            _gradientActiveStop = value;
            var color = value == 0 ? _gradient.StartColor : _gradient.EndColor;
            RgbToHsv(color, out _hue, out _sat, out _val);
            _alpha = color.A / 255f;
            _trackNeedsInit = true;
            InvalidateSVTexture();
        }
    }

    internal static Func<bool>? OnBackdropClick;

    public static bool IsOpen(WidgetId id) => _popupId == id;

    internal static void Close()
    {
        _popupId = WidgetId.None;
        OnBackdropClick = null;
        _eyeDropperActive = false;
        _eyeDropperTask = null;
        FloatingPanel.Close();
        UI.ClearHot();
    }

    private static void InitiateEyeDropperReadback()
    {
        if (!UI.TryGetSceneRenderInfo(Workspace.SceneWidgetId, out var info) || info.Handle == 0)
            return;

        var mouseScreen = Workspace.MousePosition;
        var relX = (mouseScreen.X - info.ScreenRect.X) / info.ScreenRect.Width;
        var relY = (mouseScreen.Y - info.ScreenRect.Y) / info.ScreenRect.Height;
        var px = Math.Clamp((int)(relX * info.Width), 0, info.Width - 1);
        var py = Math.Clamp((int)(relY * info.Height), 0, info.Height - 1);

        _eyeDropperTask = Graphics.Driver.ReadPixelAsync(info.Handle, px, py);
        Input.ConsumeButton(InputCode.MouseLeft);
    }

    private static void OpenPanel(WidgetId id)
    {
        var anchorRect = UI.GetElementWorldRect(id);
        var panelWidth = EditorStyle.ColorPicker.SVSize + 60 + 10; // SV + padding/chrome + gap
        FloatingPanel.Open(id, new Vector2(anchorRect.Left - panelWidth, anchorRect.Top));
    }

    internal static void Open(WidgetId id, Color color)
    {
        // Extract HDR intensity from color: factor = max component, intensity = log2(factor)
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

        Open(id, color.ToColor32());
    }

    internal static void Open(WidgetId id, Color32 color)
    {
        _popupId = id;
        _originalColor = color;
        _prevColor = color;
        _trackNeedsInit = true;
        UI.SetHot<Color32>(id, color);
        RgbToHsv(color, out _hue, out _sat, out _val);
        _alpha = color.A / 255f;

        if (color.A == 0)
            _paletteMode = ColorMode.None;
        else
        {
            _paletteMode = ColorMode.Color;

            foreach (var palette in PaletteManager.Palettes)
            {
                for (int i = 0; i < palette.Count; i++)
                {
                    var paletteColor = palette.Colors[i];
                    if (paletteColor.ToColor32() == color)
                    {
                        _paletteMode = ColorMode.Palette;
                        break;
                    }
                }
                if (_paletteMode == ColorMode.Palette) break;
            }
        }

        OpenPanel(id);
    }

    internal static void Open(WidgetId id, SpriteFillType fillType, Color32 solidColor, SpriteFillGradient gradient)
    {
        if (fillType == SpriteFillType.Linear)
        {
            _popupId = id;
            _originalColor = solidColor;
            _prevColor = solidColor;
            _trackNeedsInit = true;
            _paletteMode = ColorMode.Linear;
            _gradient = gradient;
            _gradientActiveStop = 0;
            UI.SetHot<Color32>(id, solidColor);

            var activeColor = gradient.StartColor;
            RgbToHsv(activeColor, out _hue, out _sat, out _val);
            _alpha = activeColor.A / 255f;

            OpenPanel(id);
        }
        else
        {
            Open(id, solidColor);
        }
    }

    /// Draw the floating color picker panel. Call from overlay UI.
    internal static void Draw()
    {
        if (_popupId == WidgetId.None) return;
        if (!FloatingPanel.IsOpen(_popupId)) return;

        if (_eyeDropperActive)
            EditorCursor.SetDropper();

        if (_eyeDropperTask != null)
        {
            if (_eyeDropperTask.IsCompleted)
            {
                var sampledColor = _eyeDropperTask.Result.ToColor32();
                _eyeDropperTask = null;
                _eyeDropperActive = false;

                RgbToHsv(sampledColor, out _hue, out _sat, out _val);
                _alpha = sampledColor.A / 255f;
                InvalidateSVTexture();
                _trackNeedsInit = true;

                if (_paletteMode == ColorMode.Linear)
                {
                    if (_gradientActiveStop == 0)
                        _gradient.StartColor = sampledColor;
                    else
                        _gradient.EndColor = sampledColor;
                }
                else if (_paletteMode == ColorMode.None)
                {
                    _paletteMode = ColorMode.Color;
                }
            }
        }

        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            Input.ConsumeButton(InputCode.KeyEscape);
            if (_eyeDropperActive)
            {
                _eyeDropperActive = false;
                _eyeDropperTask = null;
            }
            else
            {
                Close();
                return;
            }
        }

        if (_eyeDropperActive && Input.WasButtonPressed(InputCode.MouseRight))
        {
            Input.ConsumeButton(InputCode.MouseRight);
            _eyeDropperActive = false;
            _eyeDropperTask = null;
        }

        using var panel = FloatingPanel.Begin(_popupId, EditorStyle.ColorPicker.Root);

        if (FloatingPanel.WasBackdropPressed)
        {
            if (_eyeDropperActive)
            {
                InitiateEyeDropperReadback();
            }
            else if (_paletteMode != ColorMode.Linear || OnBackdropClick == null || !OnBackdropClick())
            {
                Close();
                return;
            }
        }

        using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing, Height = EditorStyle.Control.Height }))
        {
            if (UI.Button(ElementId.ModeNone, EditorAssets.Sprites.IconNofill, EditorStyle.Button.ToggleIcon, isSelected: _paletteMode == ColorMode.None))
                _paletteMode = ColorMode.None;

            if (UI.Button(ElementId.ModeColor, EditorAssets.Sprites.IconFill, EditorStyle.Button.ToggleIcon, isSelected: _paletteMode == ColorMode.Color))
            {
                _paletteMode = ColorMode.Color;
                if (_alpha == 0)
                    _alpha = 1;
            }

            if (UI.Button(ElementId.ModeLinear, "Grad", EditorStyle.Button.ToggleIcon, isSelected: _paletteMode == ColorMode.Linear))
            {
                _paletteMode = ColorMode.Linear;
                if (_alpha == 0)
                    _alpha = 1;
                if (_gradient.StartColor.A == 0 && _gradient.EndColor.A == 0)
                {
                    var c = HsvToColor32(_hue, _sat, _val, _alpha);
                    _gradient.StartColor = c;
                    _gradient.EndColor = Color32.White;
                }
            }

            if (PaletteManager.Palettes.Count > 0)
            {
                if (UI.Button(ElementId.ModePalette, EditorAssets.Sprites.IconPalette, EditorStyle.Button.ToggleIcon, isSelected: _paletteMode == ColorMode.Palette))
                    _paletteMode = ColorMode.Palette;
            }

            if (UI.Button(ElementId.EyeDropper, EditorAssets.Sprites.CurorDropper, EditorStyle.Button.ToggleIcon, isSelected: _eyeDropperActive))
                _eyeDropperActive = !_eyeDropperActive;

            UI.Flex();

            if (UI.Button(ElementId.Close, EditorAssets.Sprites.IconClose, EditorStyle.Button.IconOnly))
            {
                UI.NotifyChanged(_originalColor.GetHashCode());
                Close();
                return;
            }
        }

        Color32 color;

        if (_paletteMode == ColorMode.Color)
        {
            SaturationAndValue();
            Hue();
            Alpha();
            if (Graphics.RenderConfig.HDR)
                Intensity();
            _trackNeedsInit = false;
            color = HsvToColor32(_hue, _sat, _val, _alpha);
        }
        else if (_paletteMode == ColorMode.Linear)
        {
            GradientStopsUI();
            SaturationAndValue();
            Hue();
            Alpha();
            _trackNeedsInit = false;

            var editedColor = HsvToColor32(_hue, _sat, _val, _alpha);
            if (_gradientActiveStop == 0)
                _gradient.StartColor = editedColor;
            else
                _gradient.EndColor = editedColor;

            color = _gradient.StartColor;
        }
        else if (_paletteMode == ColorMode.None)
        {
            color = Color.Transparent;
        }
        else
        {
            PaletteUI();
            color = HsvToColor32(_hue, _sat, _val, _alpha);
        }

        if (color != _prevColor)
        {
            UI.NotifyChanged(color.GetHashCode());
            _prevColor = color;
        }
    }

    /// Read the current color. Call from where the color button is drawn.
    internal static void Popup(WidgetId id, ref Color32 color)
    {
        if (_popupId != id) return;
        color = _prevColor;
    }

    internal static void Popup(WidgetId id, ref Color color)
    {
        if (_popupId != id) return;

        var baseColor = _prevColor.ToColor();
        if (_intensity > 0f)
        {
            var factor = MathF.Pow(2f, _intensity);
            color = new Color(baseColor.R * factor, baseColor.G * factor, baseColor.B * factor, baseColor.A);
        }
        else
        {
            color = baseColor;
        }
    }

    private static void SaturationAndValue()
    {
        EnsureSVTexture();

        ref var trackState = ref ElementTree.BeginWidget<TrackState>(ElementId.SaturationAndValue);
        ElementTree.BeginTrack(ref trackState, ElementId.SaturationAndValue, 1, 1);

        if (_trackNeedsInit)
        {
            trackState.X = _sat;
            trackState.Y = 1 - _val;
        }
        else
        {
            _sat = trackState.X;
            _val = 1 - trackState.Y;
        }

        using (UI.BeginContainer(EditorStyle.ColorPicker.SaturationAndValue))
        {
            if (_svTexture != null)
                UI.Image(_svTexture, ImageStyle.Fill);

            DrawCrosshair(
                _sat * SVSize,
                (1 - _val) * SVSize);
        }

        ElementTree.EndTrack();
        ElementTree.EndWidget();
    }

    private static void Hue()
    {
        EnsureHueTexture();

        ref var trackState = ref ElementTree.BeginWidget<TrackState>(ElementId.Hue);
        ElementTree.BeginTrack(ref trackState, ElementId.Hue, ThumbSize);

        if (_trackNeedsInit)
            trackState.X = _hue / 360f;
        else
        {
            var newHue = trackState.X * 360f;
            if (newHue != _hue)
            {
                _hue = newHue;
                InvalidateSVTexture();
            }
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
    }

    private static void Alpha()
    {
        EnsureCheckerTexture();

        ref var trackState = ref ElementTree.BeginWidget<TrackState>(ElementId.Alpha);
        ElementTree.BeginTrack(ref trackState, ElementId.Alpha, ThumbSize);

        if (_trackNeedsInit)
            trackState.X = _alpha;
        else
            _alpha = trackState.X;

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
    }

    private static void Intensity()
    {
        ref var trackState = ref ElementTree.BeginWidget<TrackState>(ElementId.Intensity);
        ElementTree.BeginTrack(ref trackState, ElementId.Intensity, ThumbSize);

        // Map intensity 0..10 to track 0..1
        const float maxIntensity = 10f;
        if (_trackNeedsInit)
            trackState.X = _intensity / maxIntensity;
        else
            _intensity = trackState.X * maxIntensity;

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

    private static void SliderThumb(float t, Color color, Color borderColor)
    {
        var travel = EditorStyle.ColorPicker.SliderWidth - ThumbSize;

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
            Padding = EdgeInsets.All(3),
            Background = selected ? EditorStyle.Palette.Primary : Color.Transparent,
            BorderRadius = EditorStyle.Control.BorderRadius
        });

        if (!selected && UI.IsHovered())
            UI.Container(EditorStyle.Control.HoverFill with
            {
                Margin = EdgeInsets.All(-3)
            });

        return container;
    }

    private static void GradientStopsUI()
    {
        using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing, Height = EditorStyle.Control.Height }))
        {
            using (BeginColorContainer(ElementId.GradientStop0, _gradientActiveStop == 0))
            {
                UI.Container(new ContainerStyle
                {
                    Background = _gradient.StartColor.ToColor(),
                    BorderRadius = EditorStyle.Control.BorderRadius - 2,
                    Width = SwatchCellSize,
                    Height = SwatchCellSize,
                });

                if (UI.WasPressed())
                    ActiveGradientStop = 0;
            }

            using (BeginColorContainer(ElementId.GradientStop1, _gradientActiveStop == 1))
            {
                UI.Container(new ContainerStyle
                {
                    Background = _gradient.EndColor.ToColor(),
                    BorderRadius = EditorStyle.Control.BorderRadius - 2,
                    Width = SwatchCellSize,
                    Height = SwatchCellSize,
                });

                if (UI.WasPressed())
                    ActiveGradientStop = 1;
            }
        }
    }

    private static void PaletteUI()
    {
        var nextPaletteItemId = ElementId.ColorPickerPaletteItem;

        foreach (var palette in PaletteManager.Palettes)
        {
            UI.Text(palette.Label, EditorStyle.Control.Text);

            using var grid = UI.BeginGrid(new GridStyle
            {
                CellHeight = SwatchCellSize,
                CellWidth = SwatchCellSize,
                Columns = 16
            });

            for (int i = 0; i < palette.Count; i++)
            {
                var swatchColor = palette.Colors[i];
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

        // Create a checkerboard pattern sized so cells appear roughly square
        // when stretched to the alpha bar (~280 x 20).  Each texel = one cell.
        const int w = 40;
        const int h = 3;
        var light = new Color32(255, 255, 255);
        var dark = new Color32(204, 204, 204);

        _checkerPixels ??= new PixelData<Color32>(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                _checkerPixels[x, y] = (x + y) % 2 == 0 ? light : dark;

        _checkerTexture = Texture.Create(w, h, _checkerPixels.AsByteSpan(),
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

        _hueTexture = Texture.Create(w, h, _huePixels.AsByteSpan(), TextureFormat.RGBA8, TextureFilter.Linear, "EditorHueBar");
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
            _svTexture = Texture.Create(size, size, _svPixels.AsByteSpan(), TextureFormat.RGBA8, TextureFilter.Linear, "EditorSVGradient");
        else
            _svTexture.Update(_svPixels.AsByteSpan());

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
