//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Text;

namespace NoZ.Editor;

internal struct AtlasSpriteRect
{
    public string Name;
    public SpriteDocument? Source;
    public RectInt Rect;
    public ushort FrameIndex;
    public ushort Layer;
}

public static class AtlasManager
{
    public const string GameAtlasName = "sprites";
    public const string EditorAtlasName = "editor_sprites";

    private class Group
    {
        public string Name = "";
        public bool IsEditorOnly;
        public bool ShipsToRuntime;
        public readonly List<SpriteDocument> Sources = new(64);
        public readonly List<RectPacker> Packers = new(2);
        public readonly List<PixelData<Color32>> Layers = new(2);
        public readonly List<AtlasSpriteRect> Rects = new(128);
        public readonly HashSet<SpriteDocument> DirtySprites = new();
        public readonly Atlas Atlas = new();
        public byte[]? LastSerialized;
        public bool Dirty = true;
        public bool ExportPending;

        public void DisposeLayers()
        {
            foreach (var layer in Layers) layer.Dispose();
            Layers.Clear();
            Packers.Clear();
            Rects.Clear();
        }
    }

    private static readonly Group _game = new() { Name = GameAtlasName, IsEditorOnly = false, ShipsToRuntime = true };
    private static readonly Group _editor = new() { Name = EditorAtlasName, IsEditorOnly = true, ShipsToRuntime = false };

    public static Atlas GameAtlas => _game.Atlas;
    public static Atlas EditorAtlas => _editor.Atlas;

    public static void Init()
    {
        Project.DocumentAdded += OnDocumentAdded;
        Project.DocumentRemoved += OnDocumentRemoved;
        Project.OnExported += OnExported;

        foreach (var doc in Project.Documents)
            if (doc is SpriteDocument sprite)
                AddSourceInternal(sprite);

        Update();
    }

    public static void Shutdown()
    {
        _game.Sources.Clear();
        _game.DisposeLayers();
        _editor.Sources.Clear();
        _editor.DisposeLayers();
    }

    private static void OnDocumentAdded(Document doc)
    {
        if (doc is SpriteDocument sprite)
        {
            AddSourceInternal(sprite);
            Update();
        }
    }

    private static void OnDocumentRemoved(Document doc)
    {
        if (doc is SpriteDocument sprite)
        {
            RemoveSourceInternal(sprite);
            Update();
        }
    }

    private static void OnExported(Document doc)
    {
        if (doc is SpriteDocument sprite)
            GroupOf(sprite).ExportPending = true;
    }

    private static Group GroupOf(SpriteDocument sprite) =>
        sprite.ShouldExport ? _game : _editor;

    private static void AddSourceInternal(SpriteDocument sprite)
    {
        var group = GroupOf(sprite);
        if (!group.Sources.Contains(sprite))
        {
            group.Sources.Add(sprite);
            group.Dirty = true;
        }
    }

    private static void RemoveSourceInternal(SpriteDocument sprite)
    {
        if (_game.Sources.Remove(sprite)) _game.Dirty = true;
        if (_editor.Sources.Remove(sprite)) _editor.Dirty = true;
    }

    public static void MarkDirty(SpriteDocument sprite) =>
        GroupOf(sprite).Dirty = true;

    public static void MarkAllDirty()
    {
        _game.Dirty = true;
        _editor.Dirty = true;
    }

    /// <summary>
    /// Re-pack/re-rasterize for one sprite. Tries to keep the sprite in its existing rect
    /// (fast in-place rasterize). Falls back to a full repack if the sprite is new or its
    /// frame sizes no longer fit its rects.
    /// </summary>
    public static void UpdateSource(SpriteDocument sprite)
    {
        var group = GroupOf(sprite);

        // Already pending a full repack — let that subsume this change.
        if (group.Dirty)
        {
            Update();
            return;
        }

        // Sprite isn't packed yet — needs a full repack to allocate its rects.
        if (!HasRectsFor(group, sprite))
        {
            group.Dirty = true;
            Update();
            return;
        }

        sprite.UpdateBounds();

        // If any frame no longer fits its existing rect, full repack.
        if (!FitsInExistingRects(group, sprite))
        {
            group.Dirty = true;
            Update();
            return;
        }

        // Fast path: in-place re-rasterize for this sprite only.
        group.DirtySprites.Add(sprite);
        Update();
    }

    private static bool HasRectsFor(Group group, SpriteDocument sprite)
    {
        foreach (var rect in group.Rects)
            if (rect.Source == sprite) return true;
        return false;
    }

    private static bool FitsInExistingRects(Group group, SpriteDocument sprite)
    {
        var frameCount = sprite.AtlasFrameCount;
        Span<bool> seen = frameCount <= 64 ? stackalloc bool[frameCount] : new bool[frameCount];
        foreach (var rect in group.Rects)
        {
            if (rect.Source != sprite) continue;
            if (rect.FrameIndex >= frameCount) return false;
            seen[rect.FrameIndex] = true;
            var size = sprite.GetFrameAtlasSize(rect.FrameIndex);
            if (size.X == 0 || size.Y == 0) return false;
            if (size.X > rect.Rect.Width || size.Y > rect.Rect.Height) return false;
        }
        for (int i = 0; i < frameCount; i++)
            if (!seen[i]) return false;
        return true;
    }

    public static void Update()
    {
        if (_game.Dirty) Pack(_game);
        else if (_game.DirtySprites.Count > 0) Rasterize(_game);

        if (_editor.Dirty) Pack(_editor);
        else if (_editor.DirtySprites.Count > 0) Rasterize(_editor);
    }

    private static void Rasterize(Group group)
    {
        var sw = Stopwatch.StartNew();
        var padding = EditorApplication.Config.AtlasPadding;
        var dirtyCount = group.DirtySprites.Count;

        var rects = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(group.Rects);
        for (int i = 0; i < rects.Length; i++)
        {
            ref readonly var rect = ref rects[i];
            if (rect.Source == null || !group.DirtySprites.Contains(rect.Source))
                continue;

            group.Layers[rect.Layer].Clear(rect.Rect);
            rect.Source.Rasterize(group.Layers[rect.Layer], in rect, padding);
        }

        foreach (var sprite in group.DirtySprites)
            sprite.MarkSpriteDirty();
        group.DirtySprites.Clear();

        UploadAtlas(group, padding);

        sw.Stop();
        Log.Info($"[AtlasManager] Rasterize '{group.Name}' incremental: total={sw.ElapsedMilliseconds}ms, sprites={dirtyCount}");
    }

    internal static bool TryGetEntry(SpriteDocument sprite, out AtlasSpriteRect[] frames, out ushort layer)
    {
        var group = GroupOf(sprite);
        if (group.Dirty) Pack(group);

        var collected = new List<AtlasSpriteRect>(sprite.AtlasFrameCount);
        ushort foundLayer = 0;
        foreach (var rect in group.Rects)
        {
            if (rect.Source == sprite)
            {
                collected.Add(rect);
                foundLayer = rect.Layer;
            }
        }

        if (collected.Count == 0)
        {
            frames = [];
            layer = 0;
            return false;
        }

        collected.Sort((a, b) => a.FrameIndex.CompareTo(b.FrameIndex));
        frames = collected.ToArray();
        layer = foundLayer;
        return true;
    }

    private static void Pack(Group group)
    {
        var swTotal = Stopwatch.StartNew();
        group.Dirty = false;
        group.DirtySprites.Clear();
        group.DisposeLayers();

        var atlasSize = EditorApplication.Config.AtlasSize;
        var padding = EditorApplication.Config.AtlasPadding;

        var swBounds = Stopwatch.StartNew();
        foreach (var sprite in group.Sources)
            sprite.UpdateBounds();
        swBounds.Stop();

        var swPack = Stopwatch.StartNew();
        var swRasterize = new Stopwatch();
        var spriteCount = 0;
        var rectCount = 0;
        foreach (var sprite in group.Sources)
        {
            spriteCount++;
            for (ushort frameIndex = 0; frameIndex < sprite.AtlasFrameCount; frameIndex++)
            {
                var size = sprite.GetFrameAtlasSize(frameIndex);
                if (size == Vector2Int.Zero) continue;

                if (!TryInsert(group, atlasSize, size, out var layer, out var rect))
                {
                    Log.Error($"Sprite '{sprite.Name}' frame {frameIndex} too large to pack ({size.X}x{size.Y})");
                    continue;
                }

                var spriteRect = new AtlasSpriteRect
                {
                    Name = sprite.Name,
                    Source = sprite,
                    Rect = rect,
                    FrameIndex = frameIndex,
                    Layer = (ushort)layer,
                };
                group.Rects.Add(spriteRect);
                rectCount++;

                swRasterize.Start();
                sprite.Rasterize(group.Layers[layer], in spriteRect, padding);
                swRasterize.Stop();
            }
        }
        swPack.Stop();

        var swUpload = Stopwatch.StartNew();
        UploadAtlas(group, padding);
        swUpload.Stop();

        foreach (var sprite in group.Sources)
            sprite.MarkSpriteDirty();

        swTotal.Stop();
        Log.Info(
            $"[AtlasManager] Pack '{group.Name}': total={swTotal.ElapsedMilliseconds}ms " +
            $"(bounds={swBounds.ElapsedMilliseconds}ms, " +
            $"pack={swPack.ElapsedMilliseconds}ms, " +
            $"rasterize={swRasterize.ElapsedMilliseconds}ms, " +
            $"upload={swUpload.ElapsedMilliseconds}ms) " +
            $"sprites={spriteCount}, rects={rectCount}, layers={group.Layers.Count}");
    }

    private static bool TryInsert(Group group, int atlasSize, in Vector2Int size, out int layer, out RectInt rect)
    {
        for (int i = 0; i < group.Packers.Count; i++)
        {
            if (group.Packers[i].Insert(size, out rect) >= 0)
            {
                layer = i;
                return true;
            }
        }

        var packer = new RectPacker(atlasSize, atlasSize);
        var image = new PixelData<Color32>(atlasSize, atlasSize);
        group.Packers.Add(packer);
        group.Layers.Add(image);

        if (packer.Insert(size, out rect) >= 0)
        {
            layer = group.Packers.Count - 1;
            return true;
        }

        layer = -1;
        return false;
    }

    private static void UploadAtlas(Group group, int padding)
    {
        // Serialize the atlas binary into a buffer and load it back through the engine's
        // normal Atlas binary parser. This keeps a single source of truth for the format.
        var atlasSize = EditorApplication.Config.AtlasSize;
        // Layer data dominates; reserve enough so MemoryStream doesn't realloc as it grows.
        var layerBytes = atlasSize * atlasSize * 4 * group.Layers.Count;
        var capacity = layerBytes + 64 * 1024; // 64KB slop for header/entries/frames

        var swSerialize = Stopwatch.StartNew();
        using var ms = new MemoryStream(capacity);
        SerializeAtlas(group, padding, ms);
        group.LastSerialized = ms.ToArray();
        swSerialize.Stop();

        var swLoad = Stopwatch.StartNew();
        using var loadStream = new MemoryStream(group.LastSerialized);
        group.Atlas.LoadFromStream(loadStream, group.Name);
        swLoad.Stop();

        Log.Info(
            $"[AtlasManager] Upload '{group.Name}': serialize={swSerialize.ElapsedMilliseconds}ms, " +
            $"load={swLoad.ElapsedMilliseconds}ms, bytes={group.LastSerialized.Length / 1024}KB");
    }

    private static void SerializeAtlas(Group group, int padding, Stream output)
    {
        var atlasSize = (float)EditorApplication.Config.AtlasSize;

        // Collect frames per sprite (ordered by frameIndex, padded if missing)
        var frameLists = new Dictionary<string, (ushort Layer, List<Atlas.Frame> Frames)>();
        var orderedNames = new List<string>(group.Sources.Count);
        foreach (var rect in group.Rects)
        {
            if (!frameLists.TryGetValue(rect.Name, out var entry))
            {
                entry = (rect.Layer, new List<Atlas.Frame>());
                frameLists[rect.Name] = entry;
                orderedNames.Add(rect.Name);
            }

            while (entry.Frames.Count <= rect.FrameIndex)
                entry.Frames.Add(default);

            var u = (rect.Rect.Left + padding) / atlasSize;
            var v = (rect.Rect.Top + padding) / atlasSize;
            var trim = rect.Source!.RasterBounds.Size;
            var s = u + trim.X / atlasSize;
            var t = v + trim.Y / atlasSize;
            entry.Frames[rect.FrameIndex] = new Atlas.Frame
            {
                UV = Rect.FromMinMax(u, v, s, t),
                TrimSize = trim,
            };
        }

        var width = EditorApplication.Config.AtlasSize;
        var height = EditorApplication.Config.AtlasSize;

        var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.WriteAssetHeader(AssetType.Atlas, Atlas.Version, 0);
        writer.Write((byte)TextureFormat.RGBA8);
        writer.Write((byte)TextureFilter.Linear);
        writer.Write((byte)TextureClamp.Clamp);
        writer.Write((uint)width);
        writer.Write((uint)height);
        writer.Write((ushort)group.Layers.Count);

        foreach (var layer in group.Layers)
            writer.Write(layer.AsReadonlySpan());

        writer.Write((ushort)orderedNames.Count);
        foreach (var name in orderedNames)
        {
            var entry = frameLists[name];
            var nameBytes = Encoding.UTF8.GetBytes(name);
            writer.Write((ushort)nameBytes.Length);
            writer.Write(nameBytes);
            writer.Write(entry.Layer);
            writer.Write((ushort)entry.Frames.Count);
        }

        foreach (var name in orderedNames)
        {
            var entry = frameLists[name];
            foreach (var f in entry.Frames)
            {
                writer.Write(f.UV.Left);
                writer.Write(f.UV.Top);
                writer.Write(f.UV.Right);
                writer.Write(f.UV.Bottom);
                writer.Write((short)f.TrimSize.X);
                writer.Write((short)f.TrimSize.Y);
            }
        }

        writer.Flush();
    }

    /// <summary>
    /// Called by the Project export pipeline. Writes the binary atlas if any sprite in the
    /// group was exported in the current pass.
    /// </summary>
    public static void ExportIfNeeded()
    {
        if (_game.ExportPending && _game.ShipsToRuntime)
            ExportGroup(_game);
        _game.ExportPending = false;
        _editor.ExportPending = false;
    }

    private static void ExportGroup(Group group)
    {
        if (group.Dirty) Pack(group);
        if (group.LastSerialized == null) return;

        var swWrite = Stopwatch.StartNew();
        var outputDir = Path.Combine(Project.OutputPath, "atlas");
        Directory.CreateDirectory(outputDir);
        File.WriteAllBytes(Path.Combine(outputDir, group.Name), group.LastSerialized);
        swWrite.Stop();

        Log.Info($"[AtlasManager] Exported atlas/{group.Name} ({group.LastSerialized.Length / 1024}KB) in {swWrite.ElapsedMilliseconds}ms");
    }
}
