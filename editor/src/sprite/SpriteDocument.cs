//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NoZ.Editor;

public partial class SpriteDocument : Document, ISkeletonAttachment
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".tga", ".webp", ".bmp"];

    public const float DefaultFrameRate = 12f;

    public override bool CanSave => IsMutable;

    public bool IsMutable { get; private set; } = true;
    public bool IsReference { get; private set; }
    public string? ImageFilePath { get; private set; }
    private Texture? _texture;
    private Vector2Int _textureSize;
    private Vector2Int _sourceImageSize;
    private Texture? _standaloneTexture;
    private int _standaloneTextureVersion = -1;
    
    public byte SortOrder { get; private set; }

    private string? _sortOrderId;
    public string? SortOrderId
    {
        get => _sortOrderId;
        set { _sortOrderId = value; ResolveSortOrder(); }
    }

    public bool ShouldAtlas => true;

    // Layer-based model
    public SpriteLayer RootLayer { get; } = new() { Name = "Root" };
    public List<SpriteAnimFrame> AnimFrames { get; } = new();

    private readonly List<Rect> _atlasUV = new();
    private readonly List<SpritePath> _visiblePathsCache = new();
    private Sprite? _sprite;
    public float Depth;
    public RectInt RasterBounds { get; private set; }
    public EdgeInsets Edges { get; set; } = EdgeInsets.Zero;

    public Color32 CurrentFillColor = Color32.White;
    public Color32 CurrentStrokeColor = new(0, 0, 0, 0);
    public byte CurrentStrokeWidth = 1;
    public SpriteStrokeJoin CurrentStrokeJoin;
    public SpritePathOperation CurrentOperation;


    public int TotalTimeSlots
    {
        get
        {
            if (AnimFrames.Count == 0) return 1;
            var total = 0;
            foreach (var frame in AnimFrames)
                total += 1 + frame.Hold;
            return total;
        }
    }

    public int GetFrameAtTimeSlot(int timeSlot)
    {
        if (AnimFrames.Count == 0) return 0;
        var accumulated = 0;
        for (var i = 0; i < AnimFrames.Count; i++)
        {
            var slots = 1 + AnimFrames[i].Hold;
            if (timeSlot < accumulated + slots)
                return i;
            accumulated += slots;
        }
        return AnimFrames.Count - 1;
    }

    public bool ShowInSkeleton { get; set; }
    public bool ShowTiling { get; set; }
    public bool ShowSkeletonOverlay { get; set; }
    public Vector2Int? ConstrainedSize { get; set; }

    public ushort AtlasFrameCount => (ushort)TotalTimeSlots;
    internal AtlasDocument? Atlas { get; set; }

    public DocumentRef<SkeletonDocument> Skeleton;
    public string? BoneName;
    public int BoneIndex { get; private set; } = -1;

    public bool ShouldShowInSkeleton(SkeletonDocument skeleton) => ShowInSkeleton;

    public int InsertFrame(int insertAt)
    {
        var index = Math.Clamp(insertAt, 0, AnimFrames.Count);
        var frame = new SpriteAnimFrame();
        // Copy visibility from adjacent frame if available
        if (AnimFrames.Count > 0)
        {
            var sourceIndex = Math.Min(index, AnimFrames.Count - 1);
            foreach (var layer in AnimFrames[sourceIndex].VisibleLayers)
                frame.VisibleLayers.Add(layer);
        }
        AnimFrames.Insert(index, frame);
        IncrementVersion();
        return index;
    }

    public int DeleteFrame(int frameIndex)
    {
        if (AnimFrames.Count <= 1 || frameIndex < 0 || frameIndex >= AnimFrames.Count)
            return Math.Max(0, frameIndex);
        AnimFrames.RemoveAt(frameIndex);
        IncrementVersion();
        return Math.Min(frameIndex, AnimFrames.Count - 1);
    }

    public void DrawSkinned(
        ReadOnlySpan<Matrix3x2> bindPose,
        ReadOnlySpan<Matrix3x2> animatedPose,
        in Matrix3x2 baseTransform)
    {
        DrawSprite(bindPose, animatedPose, baseTransform);
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

    public static void RegisterDef()
    {
        DocumentDef<SpriteDocument>.Register(new DocumentDef
        {
            Type = AssetType.Sprite,
            Name = "Sprite",
            Extensions = [".sprite", ".png", ".jpg", ".jpeg", ".tga", ".webp", ".bmp"],
            Factory = () => new SpriteDocument(),
            EditorFactory = doc => new SpriteEditor((SpriteDocument)doc),
            NewFile = NewFile,
            Icon = () => EditorAssets.Sprites.AssetIconSprite
        });
    }

    private static void NewFile(StreamWriter writer)
    {
    }

    public override void Load()
    {
        DiscoverFiles();

        if (IsMutable)
        {
            var contents = File.ReadAllText(Path);
            var tk = new Tokenizer(contents);
            Load(ref tk);
        }
        else
        {
            LoadStaticImage();
        }

        UpdateBounds();
        Loaded = true;
    }

    private void DiscoverFiles()
    {
        var ext = System.IO.Path.GetExtension(Path).ToLowerInvariant();
        var dir = System.IO.Path.GetDirectoryName(Path) ?? "";
        var stem = System.IO.Path.GetFileNameWithoutExtension(Path);

        if (ext == ".sprite")
        {
            // Created from .sprite — look for companion image
            foreach (var imgExt in ImageExtensions)
            {
                var imgPath = System.IO.Path.Combine(dir, stem + imgExt);
                if (File.Exists(imgPath))
                {
                    ImageFilePath = imgPath;
                    break;
                }
            }
        }
        else
        {
            // Created from an image file — check if .sprite exists
            var spritePath = System.IO.Path.Combine(dir, stem + ".sprite");
            if (File.Exists(spritePath))
            {
                // .sprite is the primary file, image is companion
                ImageFilePath = Path;
                Path = System.IO.Path.GetFullPath(spritePath).ToLowerInvariant();
            }
            else
            {
                // No .sprite — this is a static/immutable image
                IsMutable = false;
                ImageFilePath = Path;
            }
        }

        // Files in a "reference" directory are never exported
        if (Path.Contains("reference", StringComparison.OrdinalIgnoreCase))
        {
            IsReference = true;
            ShouldExport = false;
        }
    }

    private void LoadStaticImage()
    {
        if (ImageFilePath == null || !File.Exists(ImageFilePath))
            return;

        var info = Image.Identify(ImageFilePath);
        if (info == null)
            return;

        var w = info.Width;
        var h = info.Height;

        _sourceImageSize = new Vector2Int(w, h);
        RasterBounds = new RectInt(-w / 2, -h / 2, w, h);
    }

    public override void Reload()
    {
        if (!IsMutable)
        {
            LoadStaticImage();
            UpdateBounds();
            return;
        }

        Edges = EdgeInsets.Zero;
        Skeleton.Clear();
        BoneName = null;
        ReloadGeneration();
        RootLayer.Clear();
        AnimFrames.Clear();
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);

        Skeleton.Resolve();
        ResolveGeneration();
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
        if (!IsMutable)
        {
            UpdateImmutableBounds();
            return;
        }

        _visiblePathsCache.Clear();
        RootLayer.CollectVisiblePaths(_visiblePathsCache);

        if (_visiblePathsCache.Count == 0)
        {
            SetDefaultBounds();
            return;
        }

        var first = true;
        var bounds = Rect.Zero;

        foreach (var path in _visiblePathsCache)
        {
            path.UpdateSamples();
            path.UpdateBounds();

            if (path.TotalAnchorCount == 0)
                continue;

            if (first)
            {
                bounds = path.Bounds;
                first = false;
            }
            else
            {
                var pb = path.Bounds;
                var minX = MathF.Min(bounds.X, pb.X);
                var minY = MathF.Min(bounds.Y, pb.Y);
                var maxX = MathF.Max(bounds.Right, pb.Right);
                var maxY = MathF.Max(bounds.Bottom, pb.Bottom);
                bounds = Rect.FromMinMax(new Vector2(minX, minY), new Vector2(maxX, maxY));
            }
        }

        if (first)
        {
            SetDefaultBounds();
            return;
        }

        var dpi = EditorApplication.Config.PixelsPerUnit;
        var rMinX = SnapFloor(bounds.X * dpi);
        var rMinY = SnapFloor(bounds.Y * dpi);
        var rMaxX = SnapCeil(bounds.Right * dpi);
        var rMaxY = SnapCeil(bounds.Bottom * dpi);
        RasterBounds = new RectInt(rMinX, rMinY, rMaxX - rMinX, rMaxY - rMinY);

        Bounds = bounds;

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            SetDefaultBounds();
            return;
        }

        if (ConstrainedSize.HasValue)
        {
            var cs = ConstrainedSize.Value;
            RasterBounds = new RectInt(
                -cs.X / 2,
                -cs.Y / 2,
                cs.X,
                cs.Y);
        }

        ClampToMaxSpriteSize();
        Bounds = RasterBounds.ToRect().Scale(1.0f / EditorApplication.Config.PixelsPerUnit);
        MarkSpriteDirty();
    }

    private void UpdateImmutableBounds()
    {
        if (ConstrainedSize.HasValue)
        {
            var cs = ConstrainedSize.Value;
            RasterBounds = new RectInt(-cs.X / 2, -cs.Y / 2, cs.X, cs.Y);
        }
        else
        {
            var w = _sourceImageSize.X;
            var h = _sourceImageSize.Y;
            RasterBounds = new RectInt(-w / 2, -h / 2, w, h);
        }

        ClampToMaxSpriteSize();
        var ppu = EditorApplication.Config.PixelsPerUnitInv;
        Bounds = new Rect(
            RasterBounds.X * ppu,
            RasterBounds.Y * ppu,
            RasterBounds.Width * ppu,
            RasterBounds.Height * ppu);
        MarkSpriteDirty();
    }

    private void SetDefaultBounds()
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

    private static int SnapFloor(float v)
    {
        var r = MathF.Round(v);
        return (int)(MathF.Abs(v - r) < 0.01f ? r : MathF.Floor(v));
    }

    private static int SnapCeil(float v)
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

        EnsurePreviewTexture();
        if (_texture != null)
        {
            UI.Image(_texture, ImageStyle.Center);
            return true;
        }

        return false;
    }

    private void EnsurePreviewTexture()
    {
        if (_texture != null || ImageFilePath == null || !File.Exists(ImageFilePath))
            return;

        try
        {
            using var srcImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ImageFilePath);
            _textureSize = new Vector2Int(srcImage.Width, srcImage.Height);
            _texture = CreateTextureFromImage(srcImage, Name + "_preview");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load preview texture '{ImageFilePath}': {ex.Message}");
        }
    }

    private static Texture CreateTextureFromImage(Image<Rgba32> image, string name)
    {
        var w = image.Width;
        var h = image.Height;
        var data = new byte[w * h * 4];
        image.CopyPixelDataTo(data);
        return Texture.Create(w, h, data, TextureFormat.RGBA8, TextureFilter.Linear, name);
    }

    private void DrawTexturedRect(Texture texture, Rect bounds, Color color, Rect? uv = null)
    {
        using (Graphics.PushState())
        {
            Graphics.SetTransform(Transform);
            Graphics.SetTexture(texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetTextureFilter(TextureFilter.Linear);
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

            Graphics.SetShader(EditorAssets.Shaders.Sprite);
            Graphics.SetColor(Color.White.WithAlpha(alpha * Workspace.XrayAlpha));
            if (offset != default)
                Graphics.SetTransform(Matrix3x2.CreateTranslation(offset) * Graphics.Transform);
            Graphics.Draw(sprite, frame: frame);
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
            Graphics.SetTransform(transform);
            Graphics.Draw(sprite, frame: frame);
        }
    }

    public override void Clone(Document source)
    {
        var src = (SpriteDocument)source;
        Depth = src.Depth;
        Bounds = src.Bounds;
        CurrentFillColor = src.CurrentFillColor;
        CurrentStrokeColor = src.CurrentStrokeColor;
        CurrentStrokeWidth = src.CurrentStrokeWidth;
        CurrentStrokeJoin = src.CurrentStrokeJoin;

        Edges = src.Edges;
        Skeleton = src.Skeleton;
        BoneName = src.BoneName;

        // Clone layer model
        RootLayer.Clear();
        foreach (var child in src.RootLayer.Children)
            RootLayer.Add(child.Clone());

        AnimFrames.Clear();
        foreach (var frame in src.AnimFrames)
            AnimFrames.Add(frame.Clone(src.RootLayer, RootLayer));

        CloneGeneration(src);
    }

    public override void LoadMetadata(PropertySet meta)
    {
        ShowInSkeleton = meta.GetBool("sprite", "show_in_skeleton", false);
        ShowTiling = meta.GetBool("sprite", "show_tiling", false);
        ShowSkeletonOverlay = meta.GetBool("sprite", "show_skeleton_overlay", false);
        ConstrainedSize = ParseConstrainedSize(meta.GetString("sprite", "constrained_size", ""));

        // Backward compat: migrate style from metadata to file content
        if (Generation != null && string.IsNullOrEmpty(Generation.Config.Name))
        {
            var style = meta.GetString("sprite", "style", "");
            if (!string.IsNullOrEmpty(style))
                Generation.Config.Name = style;
        }

        // Recompute bounds now that ConstrainedSize is available
        // (Load() calls UpdateBounds() before metadata is loaded)
        if (Loaded && ConstrainedSize.HasValue)
            UpdateBounds();
    }

    private static Vector2Int? ParseConstrainedSize(string value)
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

        meta.RemoveKey("sprite", "prompt_hash");

        // Clean up legacy style from metadata (now stored in file)
        meta.RemoveKey("sprite", "style");
    }

    public override void PostLoad()
    {
        Skeleton.Resolve();
        PostLoadGeneration();
        ResolveSortOrder();
        ResolveBone();
    }

    public override void GetReferences(List<Document> references)
    {
        if (Generation != null)
            foreach (var r in Generation.References)
                if (r.Value != null)
                    references.Add(r.Value);
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

        // Build a frame→rect index for this sprite's rects only (O(n) scan once)
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
            pixelsPerUnit: EditorApplication.Config.PixelsPerUnit,
            boneIndex: -1,
            frames: frames,
            frameRate: 12.0f,
            edges: Edges,
            sliceMask: Sprite.CalculateSliceMask(RasterBounds, Edges),
            atlasIndex: Atlas?.Index ?? 0,
            atlas: AtlasManager.TextureArray);
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
        _texture?.Dispose();
        _texture = null;
        _standaloneTexture?.Dispose();
        _standaloneTexture = null;
        DisposeGeneration();
        base.Dispose();
    }
}
