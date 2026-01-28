//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class CollectionDef(string id, string name, int index)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public int Index { get; } = index;
}

public static class CollectionManager
{
    private static readonly List<CollectionDef> _collections = [];
    private static readonly Dictionary<string, CollectionDef> _byId = [];
    private static readonly Dictionary<int, CollectionDef> _byIndex = [];
    private static readonly HashSet<int> _visibleIndices = [];
    private static readonly HashSet<Type> _alwaysVisibleTypes = [];
    private static string _defaultId = "";

    public static IReadOnlyList<CollectionDef> Collections => _collections;
    public static IReadOnlySet<int> VisibleIndices => _visibleIndices;

    public static void Init(EditorConfig config)
    {
        _collections.Clear();
        _byId.Clear();
        _byIndex.Clear();
        _visibleIndices.Clear();
        _alwaysVisibleTypes.Clear();

        var index = 1;
        foreach (var id in config.GetCollectionIds())
        {
            var name = config.GetCollectionName(id);
            var def = new CollectionDef(id, name, index);
            _collections.Add(def);
            _byId[id.ToLowerInvariant()] = def;
            _byIndex[index] = def;
            index++;
        }

        _defaultId = _collections.Count > 0 ? _collections[0].Id : "";

        // Default: show first collection
        if (_collections.Count > 0)
            _visibleIndices.Add(1);
    }

    public static void Shutdown()
    {
        _collections.Clear();
        _byId.Clear();
        _byIndex.Clear();
        _visibleIndices.Clear();
        _alwaysVisibleTypes.Clear();
    }

    public static CollectionDef? GetById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        return _byId.TryGetValue(id.ToLowerInvariant(), out var def) ? def : null;
    }

    public static CollectionDef? GetByIndex(int index)
    {
        return _byIndex.TryGetValue(index, out var def) ? def : null;
    }

    public static string GetIdOrDefault(string id)
    {
        if (!string.IsNullOrEmpty(id) && _byId.ContainsKey(id.ToLowerInvariant()))
            return id;
        return _defaultId;
    }

    public static void SetExclusive(int index)
    {
        if (!_byIndex.ContainsKey(index))
            return;
        _visibleIndices.Clear();
        _visibleIndices.Add(index);
    }

    public static void Toggle(int index)
    {
        if (!_byIndex.ContainsKey(index))
            return;

        if (!_visibleIndices.Remove(index))
            _visibleIndices.Add(index);
    }

    public static bool IsVisible(int index) => _visibleIndices.Contains(index);

    public static string GetFirstVisibleId()
    {
        if (_visibleIndices.Count == 0)
            return _defaultId;
        var minIndex = _visibleIndices.Min();
        return _byIndex.TryGetValue(minIndex, out var def) ? def.Id : _defaultId;
    }

    public static bool IsDocumentVisible(Document doc)
    {
        if (_alwaysVisibleTypes.Contains(doc.GetType()))
            return true;
        var docCollection = GetById(doc.CollectionId);
        return docCollection != null && _visibleIndices.Contains(docCollection.Index);
    }

    public static void ShowAlways(Type type, bool value)
    {
        if (value)
            _alwaysVisibleTypes.Add(type);
        else
            _alwaysVisibleTypes.Remove(type);
    }

    public static void SetVisibleIndices(IEnumerable<int> indices)
    {
        _visibleIndices.Clear();
        foreach (var index in indices)
        {
            if (_byIndex.ContainsKey(index))
                _visibleIndices.Add(index);
        }

        // Ensure at least one collection is visible
        if (_visibleIndices.Count == 0 && _collections.Count > 0)
            _visibleIndices.Add(1);
    }

    public static void LoadUserSettings(PropertySet props)
    {
        var visibleIds = props.GetKeys("visible_collections").ToList();
        if (visibleIds.Count == 0)
        {
            // Default to first collection
            if (_collections.Count > 0)
                SetVisibleIndices([1]);
            return;
        }

        var indices = visibleIds
            .Select(id => GetById(id))
            .Where(def => def != null)
            .Select(def => def!.Index);
        SetVisibleIndices(indices);
    }

    public static void SaveUserSettings(PropertySet props)
    {
        foreach (var index in _visibleIndices)
        {
            if (_byIndex.TryGetValue(index, out var def))
                props.SetString("visible_collections", def.Id, "");
        }
    }
}
