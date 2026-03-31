//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#define NOZ_ATLAS_DEBUG

using System.Diagnostics;
using System.Threading.Tasks;

namespace NoZ.Editor;

public static class AtlasManager
{
    private readonly static List<AtlasDocument> _atlases = new(32);
    private readonly static List<SpriteDocument> _sources = new(64);
    private static Texture? _previousAtlasArray;

    public static Texture? TextureArray { get; private set; }

    public static void Init()
    {
        UpdateAssets();
        Update();

        DocumentManager.DocumentAdded += HandleDocumentAdded;
    }

    public static void Shutdown()
    {
        _atlases.Clear();
        _sources.Clear();
    }

    private static void HandleDocumentAdded(Document doc)
    {
        if (doc is SpriteDocument { ShouldAtlas: true } sprite)
            AddSource(sprite);
    }

    private static string GetAtlasName(int index) => $"{EditorApplication.Config.AtlasPrefix}{index:000}.atlas";
    internal static int GetAtlasIndex(string name)
    {
        if (!name.StartsWith(EditorApplication.Config.AtlasPrefix))
            return -1;
        var indexStr = name.Substring(EditorApplication.Config.AtlasPrefix.Length);
        if (int.TryParse(indexStr, out var index))
            return index;
        return -1;
    }

    private static void UpdateAssets()
    {
        _atlases.Clear();

        var rebuild = false;
        var minIndex = int.MaxValue;
        var maxIndex = int.MinValue;
        for (int i = 0, c = DocumentManager.Count; i < c; i++ )
        {
            var doc = DocumentManager.Get(i);
            if (doc is AtlasDocument atlas)
            {
                if (!atlas.Name.StartsWith(EditorApplication.Config.AtlasPrefix)) continue;
                LogAtlas($"Rebuild: {atlas.Name} Rect Count 0", () => atlas.RectCount == 0);
                rebuild |= atlas.RectCount == 0;
                atlas.ResolveSprites();
                LogAtlas($"Rebuild: {atlas.Name} Atlas Modified", () => atlas.IsModified);
                rebuild |= atlas.IsModified;
                minIndex = Math.Min(minIndex, atlas.Index);
                maxIndex = Math.Max(maxIndex, atlas.Index);
                atlas.IsVisible = false;
                _atlases.Add(atlas);
            }
            else if (doc is SpriteDocument { ShouldAtlas: true } sprite)
                _sources.Add(sprite);
        }

        if (!rebuild)
        {
            for (int i = 0, c = _sources.Count; !rebuild && i < c; i++)
                rebuild = _sources[i].Atlas == null;

            LogAtlas($"Rebuild: One or more unatlased sprites", () => rebuild);
        }

        if (!rebuild && (minIndex != 0 || maxIndex != _atlases.Count - 1))
        {
            LogAtlas($"Rebuild: No Atlases", () => _atlases.Count == 0);
            LogAtlas($"Rebuild: Atlas index mismatch", () => _atlases.Count > 0);
            rebuild = true;
        }

        if (rebuild)
        {
            Rebuild();
            if (Graphics.Driver != null)
                RebuildTextureArray();
        }
    }

    public static void Update()
    {
        for (int i = 0; i < _sources.Count; i++)
        {
            if (_sources[i].Atlas != null)
                continue;

            Add(_sources[i]);
        }

        for (int atlasIndex = 0; atlasIndex < _atlases.Count; atlasIndex++)
            _atlases[atlasIndex].Update();
    }

    internal static void UpdateSource(SpriteDocument source) => UpdateSource(source, null);

    internal static void UpdateSource(SpriteDocument source, PixelData<Color32>?[]? pixels)
    {
        Debug.Assert(source.Atlas != null);

        // See if the source can remain in its current atlas
        if (source.Atlas.TryUpdate(source))
        {
            source.Atlas.Update(source, pixels);
            source.Reexport();
            source.Atlas.Reexport();
            return;
        }

        // Source no longer fits in its atlas, need to relocate it
        var oldAtlas = source.Atlas;
        source.Atlas = null;
        Add(source);

        // Update both the old atlas (to clear the old rect) and the new one
        oldAtlas.Update();
        oldAtlas.Reexport();
        if (source.Atlas != null && source.Atlas != oldAtlas)
        {
            source.Atlas.Update(source, pixels);
            source.Atlas.Reexport();
        }
    }

    internal static void AddSource(SpriteDocument source)
    {
        if (!_sources.Contains(source))
            _sources.Add(source);

        if (source.Atlas != null)
            return;

        Add(source);
        Update();
    }

    internal static void RemoveSource(SpriteDocument source)
    {
        if (source.Atlas != null)
        {
            source.Atlas.Remove(source);
            source.Atlas.Update();
            source.Atlas.Reexport();
            source.Atlas = null;
            source.ClearAtlasUVs();
        }

        _sources.Remove(source);
        source.Reexport();
    }

    private static void Add(SpriteDocument source)
    {
        Debug.Assert(source.Atlas == null);

        for (int i = 0; i < _atlases.Count; i++)
        {
            if (_atlases[i].TryAdd(source))
            {
                _atlases[i].IncrementVersion();
                return;
            }
        }

        // No existing atlas has space — create a new one and pack into it
        var atlas = DocumentManager.New(AssetType.Atlas, GetAtlasName(_atlases.Count)) as AtlasDocument;
        if (atlas == null)
        {
            Log.Error($"Failed to create new atlas for '{source.Name}'");
            return;
        }
        atlas.IsVisible = false;
        _atlases.Add(atlas);

        if (!atlas.TryAdd(source))
            Log.Error($"Sprite '{source.Name}' too large for atlas ({source.RasterBounds.Width}x{source.RasterBounds.Height})");
        else
            atlas.IncrementVersion();
    }

    public static void Rebuild()
    {
        for (int i = 0; i < _atlases.Count; i++)
            _atlases[i].Clear();

        // Ensure all sprite bounds are clamped before packing
        for (int i = 0; i < _sources.Count; i++)
            _sources[i].UpdateBounds();

        for (int i = 0; i < _sources.Count; i++)
        {
            Debug.Assert(_sources[i].Atlas == null);
            Add(_sources[i]);
        }

        for (int atlasIndex = _atlases.Count - 1; atlasIndex > 1; atlasIndex--)
            if (_atlases[atlasIndex].RectCount == 0)
                DocumentManager.Delete(_atlases[atlasIndex]);

        for (int atlasIndex = 0; atlasIndex < _atlases.Count; atlasIndex++)
            _atlases[atlasIndex].Update();

        DocumentManager.SaveAll();
    }

    public static void RebuildTextureArray()
    {
        // Defer disposal — current frame's render commands may still reference the old array
        _previousAtlasArray?.Dispose();
        _previousAtlasArray = TextureArray;
        TextureArray = null;

        var validAtlases = _atlases.Where(a => a.Image != null).ToList();
        if (validAtlases.Count > 0)
        {
            var width = validAtlases[0].Image!.Width;
            var height = validAtlases[0].Image!.Height;
            var layerData = validAtlases.Select(a => a.Image!.AsByteSpan().ToArray()).ToArray();
            TextureArray = Texture.CreateArray("GameSpriteAtlas", width, height, layerData);
        }

        // Update all existing sprites in-place with new atlas data
        for (int i = 0; i < _sources.Count; i++)
            _sources[i].UpdateSpriteAtlas(TextureArray);
    }

    [Conditional("NOZ_ATLAS_DEBUG")]
    public static void LogAtlas(string msg, Func<bool>? condition = null)
    {
        if (condition == null || condition())
            Log.Debug($"[ATLAS] {msg}");
    }
}
