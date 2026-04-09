//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace NoZ.Editor;

public abstract partial class SpriteDocument : Document, ISkeletonAttachment
{
    public const string Extension = ".sprite";
    public const float DefaultFrameRate = 12f;

    private static partial class WidgetIds
    {
        public static partial WidgetId PixelsPerUnit { get; }
        public static partial WidgetId FilterDropDown { get; }
        public static partial WidgetId SkeletonDropDown { get; }
        public static partial WidgetId BoneDropDown { get; }
        public static partial WidgetId ShowInSkeleton { get; }
        public static partial WidgetId ConstraintDropDown { get; }
        public static partial WidgetId SortOrder { get; }
    }

    public string? BoneName;
    private string? _sortOrderId;
    private readonly List<Rect> _atlasUV = [];
    private Sprite? _sprite;
    private Texture? _standaloneTexture;
    public float Depth;

    public override bool CanSave => true;

    public byte SortOrder { get; private set; }

    public string? SortOrderId
    {
        get => _sortOrderId;
        set { _sortOrderId = value; ResolveSortOrder(); }
    }

    public SpriteGroup Root { get; } = new() { Name = "Root" };

    public RectInt RasterBounds { get; protected set; }
    public EdgeInsets Edges { get; set; } = EdgeInsets.Zero;

    public int? PixelsPerUnitOverride { get; set; }
    public TextureFilter? TextureFilterOverride { get; set; }

    protected virtual TextureFilter DefaultTextureFilter => TextureFilter.Linear;

    protected virtual int PixelsPerUnit => PixelsPerUnitOverride ?? EditorApplication.Config.PixelsPerUnit;
    protected virtual TextureFilter TextureFilter => TextureFilterOverride ?? DefaultTextureFilter;

    public bool IsAnimated { get; set; }

    public int FrameCount => IsAnimated ? Root.Children.Count : 0;

    public bool ShowInSkeleton { get; set; }
    public bool ShowTiling { get; set; }
    public bool ShowSkeletonOverlay { get; set; }
    public Vector2Int? ConstrainedSize { get; set; }

    public ushort AtlasFrameCount => (ushort)TotalTimeSlots;
    internal AtlasDocument? Atlas { get; set; }

    public DocumentRef<SkeletonDocument> Skeleton;
    public int BoneIndex { get; private set; } = -1;

    public int TotalTimeSlots
    {
        get
        {
            if (!IsAnimated || Root.Children.Count == 0) return 1;
            var total = 0;
            foreach (var child in Root.Children)
                total += 1 + child.Hold;
            return total;
        }
    }

    public Rect AtlasUV => GetAtlasUV(0);

    public Sprite? Sprite
    {
        get
        {
            if (_sprite == null) UpdateSprite();
            return _sprite;
        }
    }

    protected abstract void UpdateContentBounds();

    public virtual Color32 GetPixelAt(Vector2 worldPos) => default;
    internal abstract void RasterizeCore(PixelData<Color32> image, in AtlasSpriteRect rect, int padding);
    protected abstract void SaveContent(StreamWriter writer);
    protected abstract void CloneContent(SpriteDocument source);

    public abstract DocumentEditor CreateEditor();

    public int GetFrameAtTimeSlot(int timeSlot)
    {
        if (!IsAnimated || Root.Children.Count == 0) return 0;
        var accumulated = 0;
        for (var i = 0; i < Root.Children.Count; i++)
        {
            var slots = 1 + Root.Children[i].Hold;
            if (timeSlot < accumulated + slots)
                return i;
            accumulated += slots;
        }
        return Root.Children.Count - 1;
    }

    public void EnableAnimation()
    {
        if (IsAnimated) return;
        Undo.Record(this);
        IsAnimated = true;
    }

    public void DisableAnimation()
    {
        if (!IsAnimated) return;
        Undo.Record(this);
        IsAnimated = false;
    }

    public bool ShouldShowInSkeleton(SkeletonDocument skeleton) => ShowInSkeleton;

    public void DrawSkinned(
        ReadOnlySpan<Matrix3x2> bindPose,
        ReadOnlySpan<Matrix3x2> animatedPose,
        in Matrix3x2 baseTransform)
    {
        DrawSprite(bindPose, animatedPose, baseTransform);
    }

    public static void RegisterDef()
    {
        DocumentDef<SpriteDocument>.Register(new DocumentDef
        {
            Type = AssetType.Sprite,
            Name = "Sprite",
            Extensions = [Extension],
            Factory = CreateFromFile,
            EditorFactory = doc => ((SpriteDocument)doc)!.CreateEditor(),
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });

        DocumentDef<ImageSpriteDocument>.Register(new DocumentDef
        {
            Type = AssetType.Sprite,
            Name = "Image",
            Extensions = [".png", ".jpg", ".jpeg", ".tga", ".webp", ".bmp"],
            Factory = _ => new ImageSpriteDocument(),
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });
    }

    private static SpriteDocument CreateFromFile(string? path)
    {
        if (path == null || !EditorApplication.Store.FileExists(path))
            return new VectorSpriteDocument();

        var content = EditorApplication.Store.ReadAllText(path);
        foreach (var line in content.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("type "))
                continue;

            return trimmed[5..].Trim().ToString() switch
            {
                "raster" => new PixelSpriteDocument(),
                "generated" => new GeneratedSpriteDocument(),
                _ => new VectorSpriteDocument(),
            };
        }

        return new VectorSpriteDocument();
    }

    public override void Load()
    {
        var contents = EditorApplication.Store.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);
        UpdateBounds();
        Loaded = true;
    }

    public override void Reload()
    {
        Edges = EdgeInsets.Zero;
        Skeleton.Clear();
        BoneName = null;
        IsAnimated = false;
        Root.Clear();
        var contents = EditorApplication.Store.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);

        Skeleton.Resolve();
        ResolveSortOrder();
        ResolveBone();
        UpdateBounds();
    }

    public void ResolveBone()
    {
        BoneIndex = -1;
        if (BoneName != null && Skeleton.Value is { } skel)
            BoneIndex = skel.FindBoneIndex(BoneName);
    }

    private void ResolveSortOrder()
    {
        SortOrder = 0;
        if (EditorApplication.Config != null && EditorApplication.Config.TryGetSortOrder(_sortOrderId, out var def))
            SortOrder = def.SortOrder;
    }

    public void UpdateBounds()
    {
        UpdateContentBounds();
        ClampToMaxSpriteSize();
        MarkSpriteDirty();
    }

    protected void SetDefaultBounds()
    {
        if (ConstrainedSize.HasValue)
        {
            var cs = ConstrainedSize.Value;
            RasterBounds = new RectInt(-cs.X / 2, -cs.Y / 2, cs.X, cs.Y);
            var ppu = EditorApplication.Config.PixelsPerUnitInv;
            Bounds = new Rect(
                RasterBounds.X * ppu,
                RasterBounds.Y * ppu,
                RasterBounds.Width * ppu,
                RasterBounds.Height * ppu);
        }
        else
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
        }
    }

    protected static int SnapFloor(float v)
    {
        var r = MathF.Round(v);
        return (int)(MathF.Abs(v - r) < 0.01f ? r : MathF.Floor(v));
    }

    protected static int SnapCeil(float v)
    {
        var r = MathF.Round(v);
        return (int)(MathF.Abs(v - r) < 0.01f ? r : MathF.Ceiling(v));
    }

    private void ClampToMaxSpriteSize()
    {
        var maxSize = EditorApplication.Config.AtlasSize - EditorApplication.Config.AtlasPadding * 2;
        var width = RasterBounds.Width;
        var height = RasterBounds.Height;

        if (width <= maxSize && height <= maxSize)
            return;

        var clampedWidth = Math.Min(width, maxSize);
        var clampedHeight = Math.Min(height, maxSize);

        RasterBounds = new RectInt(
            -clampedWidth / 2,
            -clampedHeight / 2,
            clampedWidth,
            clampedHeight);
    }

    public override void Draw()
    {
        DrawOrigin();

        if (Sprite != null)
            DrawSprite();
        else
            DrawBounds();
    }

    public override bool DrawThumbnail()
    {
        if (Sprite != null)
        {
            UI.Image(Sprite, ImageStyle.Center);
            return true;
        }

        return false;
    }

    protected static Texture CreateTextureFromImage(SixLabors.ImageSharp.Image<Rgba32> image, string name)
    {
        var w = image.Width;
        var h = image.Height;
        var data = new byte[w * h * 4];
        image.CopyPixelDataTo(data);
        return Texture.Create(w, h, data, TextureFormat.RGBA8, TextureFilter.Linear, name);
    }

    protected void DrawTexturedRect(Texture texture, Rect bounds, Color color, Rect? uv = null)
    {
        using (Graphics.PushState())
        {
            Graphics.SetTransform(Transform);
            Graphics.SetTexture(texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetTextureFilter(TextureFilter);
            Graphics.SetColor(color);
            if (uv.HasValue)
                Graphics.Draw(bounds, uv.Value, order: SortOrder);
            else
                Graphics.Draw(bounds, order: SortOrder);
        }
    }

    public void DrawSprite(in Vector2 offset = default, float alpha = 1.0f, int frame = 0)
    {
        var sprite = Sprite;
        if (sprite == null) return;

        using (Graphics.PushState())
        {
            Graphics.SetTextureFilter(TextureFilter);
            Graphics.SetShader(EditorAssets.Shaders.Sprite);
            Graphics.SetColor(Color.White.WithAlpha(alpha * Workspace.XrayAlpha));
            if (offset != default)
                Graphics.SetTransform(Matrix3x2.CreateTranslation(offset) * Graphics.Transform);
            Graphics.Draw(sprite, SortOrder, frame: frame);
        }
    }

    public void DrawSprite(ReadOnlySpan<Matrix3x2> bindPose, ReadOnlySpan<Matrix3x2> animatedPose, in Matrix3x2 baseTransform, int frame = 0, Color? tint = null)
    {
        var sprite = Sprite;
        if (sprite == null)
        {
            Log.Info($"[SKEL DEBUG] '{Name}': Sprite is null, Atlas={Atlas?.Name ?? "NULL"}");
            return;
        }

        using (Graphics.PushState())
        {
            Graphics.SetShader(EditorAssets.Shaders.Sprite);
            Graphics.SetColor(tint ?? Color.White);

            var boneIndex = BoneIndex >= 0 ? BoneIndex : 0;
            var transform = bindPose[boneIndex] * animatedPose[boneIndex] * baseTransform;
            Graphics.SetTextureFilter(TextureFilter);
            Graphics.SetTransform(transform);
            Graphics.Draw(sprite, SortOrder, frame: frame);
        }
    }

    public override void Clone(Document source)
    {
        var src = (SpriteDocument)source;
        Depth = src.Depth;
        Bounds = src.Bounds;
        RasterBounds = src.RasterBounds;
        Edges = src.Edges;
        Skeleton = src.Skeleton;
        BoneName = src.BoneName;
        IsAnimated = src.IsAnimated;
        PixelsPerUnitOverride = src.PixelsPerUnitOverride;
        TextureFilterOverride = src.TextureFilterOverride;

        Root.Dispose();
        Root.Clear();
        foreach (var child in src.Root.Children)
            Root.Add(child.Clone());

        CloneContent(src);
    }

    public override void LoadMetadata(PropertySet meta)
    {
        ShowInSkeleton = meta.GetBool("sprite", "show_in_skeleton", false);
        ShowTiling = meta.GetBool("sprite", "show_tiling", false);
        ShowSkeletonOverlay = meta.GetBool("sprite", "show_skeleton_overlay", false);
        ConstrainedSize = ParseConstrainedSize(meta.GetString("sprite", "constrained_size", ""));

        var ppu = meta.GetInt("sprite", "ppu", 0);
        PixelsPerUnitOverride = ppu > 0 ? ppu : null;

        var filter = meta.GetInt("sprite", "filter", -1);
        TextureFilterOverride = filter >= 0 ? (TextureFilter)filter : null;

        LoadContentMetadata(meta);

        if (Loaded)
            UpdateBounds();
    }

    protected virtual void LoadContentMetadata(PropertySet meta) { }

    protected static Vector2Int? ParseConstrainedSize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        var parts = value.Split('x');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var w) &&
            int.TryParse(parts[1], out var h))
        {
            return new Vector2Int(w, h);
        }
        return null;
    }

    public override void SaveMetadata(PropertySet meta)
    {
        meta.SetBool("sprite", "show_in_skeleton", ShowInSkeleton);
        meta.SetBool("sprite", "show_tiling", ShowTiling);
        meta.SetBool("sprite", "show_skeleton_overlay", ShowSkeletonOverlay);

        if (ConstrainedSize.HasValue)
            meta.SetString("sprite", "constrained_size", $"{ConstrainedSize.Value.X}x{ConstrainedSize.Value.Y}");
        else
            meta.RemoveKey("sprite", "constrained_size");

        if (PixelsPerUnitOverride.HasValue)
            meta.SetInt("sprite", "ppu", PixelsPerUnitOverride.Value);
        else
            meta.RemoveKey("sprite", "ppu");

        if (TextureFilterOverride.HasValue)
            meta.SetInt("sprite", "filter", (int)TextureFilterOverride.Value);
        else
            meta.RemoveKey("sprite", "filter");

        SaveContentMetadata(meta);
    }

    protected virtual void SaveContentMetadata(PropertySet meta) { }

    public override void InspectorUI()
    {
        if (Inspector.IsSectionCollapsed)
            return;

        using (Inspector.BeginProperty("Pixels Per Unit"))
        {
            var current = PixelsPerUnitOverride ?? EditorApplication.Config.PixelsPerUnit;
            var label = PixelsPerUnitOverride.HasValue
                ? $"{current}"
                : $"{current} (Default)";
            UI.DropDown(WidgetIds.PixelsPerUnit, () =>
            [
                ..new[] { 8, 16, 32, 64, 128, 256 }.Select(v => new PopupMenuItem
                {
                    Label = v == EditorApplication.Config.PixelsPerUnit ? $"{v} (Default)" : $"{v}",
                    Handler = () =>
                    {
                        Undo.Record(this);
                        PixelsPerUnitOverride = v == EditorApplication.Config.PixelsPerUnit ? null : v;
                        UpdateBounds();
                        if (Atlas != null)
                            AtlasManager.UpdateSource(this);
                        AssetManifest.IsModified = true;
                    }
                })
            ], label);
        }

        using (Inspector.BeginProperty("Filter"))
        {
            var current = TextureFilterOverride ?? DefaultTextureFilter;
            var filterLabel = TextureFilterOverride.HasValue
                ? $"{current}"
                : $"{current} (Default)";
            UI.DropDown(WidgetIds.FilterDropDown, () =>
            [
                new PopupMenuItem
                {
                    Label = DefaultTextureFilter == TextureFilter.Point ? "Point (Default)" : "Point",
                    Handler = () =>
                    {
                        Undo.Record(this);
                        TextureFilterOverride = DefaultTextureFilter == TextureFilter.Point ? null : TextureFilter.Point;
                        MarkSpriteDirty();
                        AssetManifest.IsModified = true;
                    }
                },
                new PopupMenuItem
                {
                    Label = DefaultTextureFilter == TextureFilter.Linear ? "Linear (Default)" : "Linear",
                    Handler = () =>
                    {
                        Undo.Record(this);
                        TextureFilterOverride = DefaultTextureFilter == TextureFilter.Linear ? null : TextureFilter.Linear;
                        MarkSpriteDirty();
                        AssetManifest.IsModified = true;
                    }
                }
            ], filterLabel);
        }

        using (Inspector.BeginProperty("Size"))
        {
            var sizes = EditorApplication.Config.SpriteSizes;
            var constraintLabel = "Auto";
            if (ConstrainedSize.HasValue)
                for (int i = 0; i < sizes.Length; i++)
                    if (ConstrainedSize.Value == sizes[i].Size)
                    {
                        constraintLabel = sizes[i].Label;
                        break;
                    }

            UI.DropDown(WidgetIds.ConstraintDropDown, () =>
            [
                ..EditorApplication.Config.SpriteSizes.Select(s =>
                    new PopupMenuItem { Label = s.Label, Handler = () =>
                    {
                        Undo.Record(this);
                        ConstrainedSize = s.Size;
                        UpdateBounds();
                        if (Atlas != null)
                            AtlasManager.UpdateSource(this);
                        AssetManifest.IsModified = true;
                    }}),
                new PopupMenuItem { Label = "Auto", Handler = () =>
                {
                    Undo.Record(this);
                    ConstrainedSize = null;
                    UpdateBounds();
                    if (Atlas != null)
                        AtlasManager.UpdateSource(this);
                    AssetManifest.IsModified = true;
                }}
            ], constraintLabel, EditorAssets.Sprites.IconConstraint);
        }

        using (Inspector.BeginProperty("Sort Order"))
        {
            EditorUI.SortOrderDropDown(WidgetIds.SortOrder, SortOrderId, id =>
            {
                Undo.Record(this);
                SortOrderId = id;
                AssetManifest.IsModified = true;
            });
        }

        using (Inspector.BeginProperty("Skeleton"))
        {
            var skeleton = EditorUI.SkeletonField(WidgetIds.SkeletonDropDown, Skeleton.Value);
            if (UI.WasChanged())
            {
                Undo.Record(this);
                if (skeleton != null)
                {
                    Skeleton = skeleton;
                    skeleton.UpdateSprites();
                }
                else
                {
                    var old = Skeleton.Value;
                    Skeleton.Clear();
                    BoneName = null;
                    old?.UpdateSprites();
                }
            }
        }

        if (Skeleton.IsResolved)
        {
            using (Inspector.BeginProperty("Bone"))
            {
                var skeleton = Skeleton.Value!;
                var boneLabel = BoneName ?? "None";

                UI.DropDown(WidgetIds.BoneDropDown, () =>
                {
                    var boneItems = new List<PopupMenuItem>();
                    for (var i = 0; i < skeleton.BoneCount; i++)
                    {
                        var boneName = skeleton.Bones[i].Name;
                        boneItems.Add(new PopupMenuItem
                        {
                            Label = boneName,
                            Handler = () =>
                            {
                                Undo.Record(this);
                                BoneName = boneName;
                                ResolveBone();
                                Skeleton.Value?.UpdateSprites();
                            }
                        });
                    }
                    boneItems.Add(new PopupMenuItem
                    {
                        Label = "None",
                        Handler = () =>
                        {
                            Undo.Record(this);
                            BoneName = null;
                            ResolveBone();
                            Skeleton.Value?.UpdateSprites();
                        }
                    });
                    return [.. boneItems];
                }, boneLabel, EditorAssets.Sprites.IconBone);
            }

            using (Inspector.BeginProperty("Show In Skeleton"))
            {
                if (UI.Toggle(WidgetIds.ShowInSkeleton, ShowInSkeleton, EditorStyle.Inspector.Toggle))
                {
                    Undo.Record(this);
                    ShowInSkeleton = !ShowInSkeleton;
                    Skeleton.Value?.UpdateSprites();
                }
            }
        }
    }

    public override void PostLoad()
    {
        Skeleton.Resolve();
        ResolveSortOrder();
        ResolveBone();
    }

    internal void ClearAtlasUVs()
    {
        _atlasUV.Clear();
        MarkSpriteDirty();
    }

    internal void UpdateAtlasUVs(AtlasDocument atlas, ReadOnlySpan<AtlasSpriteRect> allRects, int padding)
    {
        ClearAtlasUVs();
        int uvIndex = 0;
        var ts = (float)EditorApplication.Config.AtlasSize;

        var totalSlots = (ushort)TotalTimeSlots;

        Span<int> frameToRect = totalSlots <= 64 ? stackalloc int[totalSlots] : new int[totalSlots];
        frameToRect.Fill(-1);
        for (int i = 0; i < allRects.Length; i++)
        {
            if (allRects[i].Source == this && allRects[i].FrameIndex < totalSlots)
                frameToRect[allRects[i].FrameIndex] = i;
        }

        for (ushort frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            var rectIndex = frameToRect[frameIndex];
            if (rectIndex == -1) return;

            ref readonly var rect = ref allRects[rectIndex];

            var u = (rect.Rect.Left + padding) / ts;
            var v = (rect.Rect.Top + padding) / ts;
            var s = u + RasterBounds.Size.X / ts;
            var t = v + RasterBounds.Size.Y / ts;
            SetAtlasUV(uvIndex++, Rect.FromMinMax(u, v, s, t));
        }
    }

    internal void SetAtlasUV(int slotIndex, Rect uv)
    {
        while (_atlasUV.Count <= slotIndex)
            _atlasUV.Add(Rect.Zero);
        _atlasUV[slotIndex] = uv;
        MarkSpriteDirty();
    }

    internal Rect GetAtlasUV(int slotIndex) =>
        slotIndex < _atlasUV.Count ? _atlasUV[slotIndex] : Rect.Zero;

    private void UpdateSprite()
    {
        if (Atlas == null)
        {
            _sprite = null;
            return;
        }

        var totalSlots = TotalTimeSlots;
        var frames = new NoZ.SpriteFrame[totalSlots];

        for (int frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            var uv = GetAtlasUV(frameIndex);
            if (uv == Rect.Zero)
            {
                _sprite = null;
                return;
            }

            frames[frameIndex] = new NoZ.SpriteFrame(uv, RasterBounds.Position, RasterBounds.Size);
        }

        _sprite = Sprite.Create(
            name: Name,
            bounds: RasterBounds,
            pixelsPerUnit: PixelsPerUnit,
            boneIndex: -1,
            frames: frames,
            frameRate: 12.0f,
            edges: Edges,
            sliceMask: Sprite.CalculateSliceMask(RasterBounds, Edges),
            atlasIndex: Atlas?.Index ?? 0,
            atlas: AtlasManager.TextureArray,
            filter: TextureFilter);
    }

    internal void MarkSpriteDirty()
    {
        _sprite?.Dispose();
        _sprite = null;
        _standaloneTexture?.Dispose();
        _standaloneTexture = null;
    }

    internal void UpdateSpriteAtlas(Texture? atlas)
    {
        if (_sprite == null || Atlas == null) return;

        var totalSlots = TotalTimeSlots;
        var frames = new NoZ.SpriteFrame[totalSlots];
        for (int frameIndex = 0; frameIndex < totalSlots; frameIndex++)
        {
            var uv = GetAtlasUV(frameIndex);
            if (uv == Rect.Zero) return;
            frames[frameIndex] = new NoZ.SpriteFrame(uv, RasterBounds.Position, RasterBounds.Size);
        }

        _sprite.UpdateAtlas(atlas, Atlas.Index, frames);
    }

    public override void OnUndoRedo()
    {
        UpdateBounds();

        if (!IsEditing && Atlas != null)
            AtlasManager.UpdateSource(this);

        base.OnUndoRedo();
    }

    public Vector2Int GetFrameAtlasSize(ushort timeSlot)
    {
        var padding2 = EditorApplication.Config.AtlasPadding * 2;
        return new(RasterBounds.Size.X + padding2, RasterBounds.Size.Y + padding2);
    }

    public override void Dispose()
    {
        Root.Dispose();
        _standaloneTexture?.Dispose();
        _standaloneTexture = null;
        base.Dispose();
    }
}
