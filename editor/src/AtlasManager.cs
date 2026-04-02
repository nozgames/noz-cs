//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#define NOZ_ATLAS_DEBUG

using System.Diagnostics;
using System.Threading.Tasks;

namespace NoZ.Editor;

public static class AtlasManager
{
    private const string EditorAtlasPrefix = "editor_only";

    private readonly static List<AtlasDocument> _atlases = new(32);
    private readonly static List<SpriteDocument> _sources = new(64);
    private static Texture? _previousAtlasArray;

    private readonly static List<AtlasDocument> _editorAtlases = new(4);
    private readonly static List<SpriteDocument> _editorSources = new(16);

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
        _editorAtlases.Clear();
        _editorSources.Clear();
    }

    private static void HandleDocumentAdded(Document doc)
    {
        if (doc is SpriteDocument { ShouldAtlas: true } sprite)
        {
            if (sprite.ShouldExport)
                AddSource(sprite);
            else
                AddEditorSource(sprite);
        }
    }

    private static string GetAtlasName(int index) => $"{EditorApplication.Config.AtlasPrefix}{index:000}.atlas";
    private static string GetEditorAtlasName(int index) => $"{EditorAtlasPrefix}{index:000}.atlas";
    internal static int GetAtlasIndex(string name)
    {
        if (name.StartsWith(EditorApplication.Config.AtlasPrefix))
        {
            var indexStr = name.Substring(EditorApplication.Config.AtlasPrefix.Length);
            if (int.TryParse(indexStr, out var index))
                return index;
        }
        else if (name.StartsWith(EditorAtlasPrefix))
        {
            var indexStr = name.Substring(EditorAtlasPrefix.Length);
            if (int.TryParse(indexStr, out var index))
                return index;
        }
        return -1;
    }

    private static void UpdateAssets()
    {
        _atlases.Clear();
        _editorAtlases.Clear();

        var rebuild = false;
        var editorRebuild = false;
        var minIndex = int.MaxValue;
        var maxIndex = int.MinValue;
        var editorMinIndex = int.MaxValue;
        var editorMaxIndex = int.MinValue;

        for (int i = 0, c = DocumentManager.Count; i < c; i++ )
        {
            var doc = DocumentManager.Get(i);
            if (doc is AtlasDocument atlas)
            {
                if (atlas.Name.StartsWith(EditorAtlasPrefix))
                {
                    atlas.IsEditorOnly = true;
                    atlas.ShouldExport = false;
                    LogAtlas($"Rebuild: {atlas.Name} Rect Count 0", () => atlas.RectCount == 0);
                    editorRebuild |= atlas.RectCount == 0;
                    atlas.ResolveSprites();
                    LogAtlas($"Rebuild: {atlas.Name} Atlas Modified", () => atlas.IsModified);
                    editorRebuild |= atlas.IsModified;
                    editorMinIndex = Math.Min(editorMinIndex, atlas.Index);
                    editorMaxIndex = Math.Max(editorMaxIndex, atlas.Index);
                    atlas.IsVisible = false;
                    _editorAtlases.Add(atlas);
                }
                else if (atlas.Name.StartsWith(EditorApplication.Config.AtlasPrefix))
                {
                    atlas.ShouldExport = true;
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
            }
            else if (doc is SpriteDocument { ShouldAtlas: true } sprite)
            {
                if (sprite.ShouldExport)
                    _sources.Add(sprite);
                else
                    _editorSources.Add(sprite);
            }
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

        if (!editorRebuild)
        {
            for (int i = 0, c = _editorSources.Count; !editorRebuild && i < c; i++)
                editorRebuild = _editorSources[i].Atlas == null;

            LogAtlas($"Rebuild: One or more unatlased editor sprites", () => editorRebuild);
        }

        if (!editorRebuild && _editorAtlases.Count > 0 && (editorMinIndex != 0 || editorMaxIndex != _editorAtlases.Count - 1))
        {
            LogAtlas($"Rebuild: Editor atlas index mismatch");
            editorRebuild = true;
        }

        if (rebuild)
            Rebuild();

        if (editorRebuild)
            RebuildEditorAtlases();

        if ((rebuild || editorRebuild) && Graphics.Driver != null)
            RebuildTextureArray();
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

        for (int i = 0; i < _editorSources.Count; i++)
        {
            if (_editorSources[i].Atlas != null)
                continue;

            AddToEditorAtlas(_editorSources[i]);
        }

        for (int atlasIndex = 0; atlasIndex < _editorAtlases.Count; atlasIndex++)
            _editorAtlases[atlasIndex].Update();
    }

    internal static void UpdateSource(SpriteDocument source) => UpdateSource(source, null);

    internal static void UpdateSource(SpriteDocument source, PixelData<Color32>?[]? pixels)
    {
        Debug.Assert(source.Atlas != null);

        var isEditorOnly = source.Atlas.IsEditorOnly;
        var atlases = isEditorOnly ? _editorAtlases : _atlases;

        // See if the source can remain in its current atlas
        if (source.Atlas.TryUpdate(source))
        {
            source.Atlas.Update(source, pixels);
            DocumentManager.QueueExport(source, force: true);
            DocumentManager.QueueExport(source.Atlas, force: true);
            SyncTextureArrayLayer(source.Atlas);
            return;
        }

        // Source no longer fits in its atlas, need to relocate it
        var oldAtlas = source.Atlas;
        var oldAtlasCount = atlases.Count;
        source.Atlas = null;

        if (isEditorOnly)
            AddToEditorAtlas(source);
        else
            Add(source);

        // Update both the old atlas (to clear the old rect) and the new one
        oldAtlas.Update();
        DocumentManager.QueueExport(oldAtlas, force: true);
        if (source.Atlas != null && source.Atlas != oldAtlas)
        {
            source.Atlas.Update(source, pixels);
            DocumentManager.QueueExport(source.Atlas, force: true);
        }

        // If Add() created a new atlas, rebuild the entire texture array
        if (atlases.Count != oldAtlasCount && Graphics.Driver != null)
        {
            RebuildTextureArray();
        }
        else
        {
            SyncTextureArrayLayer(oldAtlas);
            if (source.Atlas != null && source.Atlas != oldAtlas)
                SyncTextureArrayLayer(source.Atlas);
        }
    }

    private static void SyncTextureArrayLayer(AtlasDocument atlas)
    {
        if (TextureArray == null || atlas.Image == null)
            return;

        TextureArray.UpdateLayer(atlas.Index, atlas.Image.AsByteSpan());
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

    internal static void AddEditorSource(SpriteDocument source)
    {
        if (!_editorSources.Contains(source))
            _editorSources.Add(source);

        if (source.Atlas != null)
            return;

        AddToEditorAtlas(source);
        Update();
    }

    internal static void RemoveSource(SpriteDocument source)
    {
        if (source.Atlas != null)
        {
            source.Atlas.Remove(source);
            source.Atlas.Update();
            DocumentManager.QueueExport(source.Atlas, force: true);
            source.Atlas = null;
            source.ClearAtlasUVs();
        }

        _sources.Remove(source);
        _editorSources.Remove(source);
        DocumentManager.QueueExport(source, force: true);
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

    private static void AddToEditorAtlas(SpriteDocument source)
    {
        Debug.Assert(source.Atlas == null);

        for (int i = 0; i < _editorAtlases.Count; i++)
        {
            if (_editorAtlases[i].TryAdd(source))
            {
                _editorAtlases[i].IncrementVersion();
                return;
            }
        }

        var atlas = DocumentManager.New(AssetType.Atlas, GetEditorAtlasName(_editorAtlases.Count)) as AtlasDocument;
        if (atlas == null)
        {
            Log.Error($"Failed to create editor atlas for '{source.Name}'");
            return;
        }
        atlas.IsVisible = false;
        atlas.IsEditorOnly = true;
        atlas.ShouldExport = false;
        _editorAtlases.Add(atlas);

        if (!atlas.TryAdd(source))
            Log.Error($"Sprite '{source.Name}' too large for editor atlas ({source.RasterBounds.Width}x{source.RasterBounds.Height})");
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

    private static void RebuildEditorAtlases()
    {
        for (int i = 0; i < _editorAtlases.Count; i++)
            _editorAtlases[i].Clear();

        for (int i = 0; i < _editorSources.Count; i++)
            _editorSources[i].UpdateBounds();

        for (int i = 0; i < _editorSources.Count; i++)
        {
            Debug.Assert(_editorSources[i].Atlas == null);
            AddToEditorAtlas(_editorSources[i]);
        }

        for (int atlasIndex = _editorAtlases.Count - 1; atlasIndex > 0; atlasIndex--)
            if (_editorAtlases[atlasIndex].RectCount == 0)
                DocumentManager.Delete(_editorAtlases[atlasIndex]);

        for (int atlasIndex = 0; atlasIndex < _editorAtlases.Count; atlasIndex++)
            _editorAtlases[atlasIndex].Update();

        DocumentManager.SaveAll();
    }

    public static void RebuildTextureArray()
    {
        // Defer disposal — current frame's render commands may still reference the old array
        _previousAtlasArray?.Dispose();
        _previousAtlasArray = TextureArray;
        TextureArray = null;

        // Assign editor atlas indices after exported atlases
        for (int i = 0; i < _editorAtlases.Count; i++)
            _editorAtlases[i].Index = _atlases.Count + i;

        // Build unified texture array: exported layers first, then editor-only layers
        var allAtlases = _atlases.Concat(_editorAtlases).Where(a => a.Image != null).ToList();
        if (allAtlases.Count > 0)
        {
            var width = allAtlases[0].Image!.Width;
            var height = allAtlases[0].Image!.Height;
            var layerData = allAtlases.Select(a => a.Image!.AsByteSpan().ToArray()).ToArray();
            TextureArray = Texture.CreateArray("GameSpriteAtlas", width, height, layerData);
        }

        // Update all sprites with the unified texture array
        for (int i = 0; i < _sources.Count; i++)
            _sources[i].UpdateSpriteAtlas(TextureArray);
        for (int i = 0; i < _editorSources.Count; i++)
            _editorSources[i].UpdateSpriteAtlas(TextureArray);
    }

    [Conditional("NOZ_ATLAS_DEBUG")]
    public static void LogAtlas(string msg, Func<bool>? condition = null)
    {
        if (condition == null || condition())
            Log.Debug($"[ATLAS] {msg}");
    }
}
