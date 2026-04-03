//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal class UndoStack
{
    private struct UndoItem
    {
        public Document Doc;
        public Document Snapshot;
        public int GroupId;
    }

    private readonly List<UndoItem> _undoItems;
    private readonly List<UndoItem> _redoItems;
    private readonly int _maxItems;

    public bool CanUndo => _undoItems.Count > 0;
    public bool CanRedo => _redoItems.Count > 0;

    public UndoStack(int maxItems)
    {
        _maxItems = maxItems;
        _undoItems = new List<UndoItem>(maxItems);
        _redoItems = new List<UndoItem>(maxItems);
    }

    public void Record(Document doc, int groupId)
    {
        if (_undoItems.Count >= _maxItems)
        {
            _undoItems[0].Snapshot.Dispose();
            _undoItems.RemoveAt(0);
        }

        _undoItems.Add(new UndoItem
        {
            Doc = doc,
            Snapshot = Undo.CloneDocument(doc),
            GroupId = groupId
        });

        doc.IncrementVersion();
        ClearRedo();
    }

    public bool DoUndo(List<Document> pendingCallbacks)
    {
        if (_undoItems.Count == 0)
            return false;

        var lastItem = _undoItems[^1];
        var groupId = lastItem.GroupId;

        while (_undoItems.Count > 0)
        {
            var item = _undoItems[^1];
            if (groupId != -1 && item.GroupId != groupId)
                break;

            var currentState = Undo.CloneDocument(item.Doc);
            Undo.RestoreDocument(item.Doc, item.Snapshot);

            if (_redoItems.Count >= _maxItems)
            {
                _redoItems[0].Snapshot.Dispose();
                _redoItems.RemoveAt(0);
            }

            _redoItems.Add(new UndoItem
            {
                Doc = item.Doc,
                Snapshot = currentState,
                GroupId = item.GroupId
            });

            item.Doc.IncrementVersion();
            pendingCallbacks.Add(item.Doc);

            var itemGroupId = item.GroupId;
            item.Snapshot.Dispose();
            _undoItems.RemoveAt(_undoItems.Count - 1);

            if (itemGroupId == -1)
                break;
        }

        return true;
    }

    public bool DoRedo(List<Document> pendingCallbacks)
    {
        if (_redoItems.Count == 0)
            return false;

        var lastItem = _redoItems[^1];
        var groupId = lastItem.GroupId;

        while (_redoItems.Count > 0)
        {
            var item = _redoItems[^1];
            if (groupId != -1 && item.GroupId != groupId)
                break;

            var currentState = Undo.CloneDocument(item.Doc);
            Undo.RestoreDocument(item.Doc, item.Snapshot);

            if (_undoItems.Count >= _maxItems)
            {
                _undoItems[0].Snapshot.Dispose();
                _undoItems.RemoveAt(0);
            }

            _undoItems.Add(new UndoItem
            {
                Doc = item.Doc,
                Snapshot = currentState,
                GroupId = item.GroupId
            });

            item.Doc.IncrementVersion();
            pendingCallbacks.Add(item.Doc);

            var itemGroupId = item.GroupId;
            item.Snapshot.Dispose();
            _redoItems.RemoveAt(_redoItems.Count - 1);

            if (itemGroupId == -1)
                break;
        }

        return true;
    }

    public void Cancel(List<Document> pendingCallbacks)
    {
        if (_undoItems.Count == 0)
            return;

        var lastItem = _undoItems[^1];
        var groupId = lastItem.GroupId;

        while (_undoItems.Count > 0)
        {
            var item = _undoItems[^1];
            if (groupId != -1 && item.GroupId != groupId)
                break;

            Undo.RestoreDocument(item.Doc, item.Snapshot);

            item.Doc.IncrementVersion();
            pendingCallbacks.Add(item.Doc);

            var itemGroupId = item.GroupId;
            item.Snapshot.Dispose();
            _undoItems.RemoveAt(_undoItems.Count - 1);

            if (itemGroupId == -1)
                break;
        }
    }

    public void RemoveDocument(Document doc)
    {
        for (var i = _undoItems.Count - 1; i >= 0; i--)
        {
            if (_undoItems[i].Doc == doc)
            {
                _undoItems[i].Snapshot.Dispose();
                _undoItems.RemoveAt(i);
            }
        }

        for (var i = _redoItems.Count - 1; i >= 0; i--)
        {
            if (_redoItems[i].Doc == doc)
            {
                _redoItems[i].Snapshot.Dispose();
                _redoItems.RemoveAt(i);
            }
        }
    }

    public void Clear()
    {
        foreach (var item in _undoItems)
            item.Snapshot.Dispose();
        _undoItems.Clear();
        ClearRedo();
    }

    private void ClearRedo()
    {
        foreach (var item in _redoItems)
            item.Snapshot.Dispose();
        _redoItems.Clear();
    }
}

public static class Undo
{
    private const int MaxDocumentUndo = 64;
    private const int MaxWorkspaceUndo = 128;

    private static readonly UndoStack _workspaceStack = new(MaxWorkspaceUndo);
    private static readonly List<Document> _pendingCallbacks = new(16);
    private static int _nextGroupId = 1;
    private static int _currentGroupId = -1;
    private static UndoStack? _lastRecordStack;

    public static bool CanUndo
    {
        get
        {
            if (Workspace.State == WorkspaceState.Edit)
                return Workspace.ActiveDocument?.UndoHistory?.CanUndo ?? false;
            return _workspaceStack.CanUndo;
        }
    }

    public static bool CanRedo
    {
        get
        {
            if (Workspace.State == WorkspaceState.Edit)
                return Workspace.ActiveDocument?.UndoHistory?.CanRedo ?? false;
            return _workspaceStack.CanRedo;
        }
    }

    public static void BeginGroup()
    {
        _currentGroupId = _nextGroupId++;
    }

    public static void EndGroup()
    {
        _currentGroupId = -1;
    }

    public static void Record(Document doc)
    {
        UndoStack stack;
        if (_currentGroupId != -1)
        {
            // Groups stay together on one stack
            if (Workspace.State == WorkspaceState.Edit)
            {
                var activeDoc = Workspace.ActiveDocument!;
                activeDoc.UndoHistory ??= new UndoStack(MaxDocumentUndo);
                stack = activeDoc.UndoHistory;
            }
            else
                stack = _workspaceStack;
        }
        else if (doc.IsEditing)
        {
            doc.UndoHistory ??= new UndoStack(MaxDocumentUndo);
            stack = doc.UndoHistory;
        }
        else
            stack = _workspaceStack;

        stack.Record(doc, _currentGroupId);
        _lastRecordStack = stack;
    }

    public static bool DoUndo()
    {
        var stack = GetActiveStack();
        if (stack == null)
            return false;

        var result = stack.DoUndo(_pendingCallbacks);
        if (result)
            CallPendingCallbacks();
        return result;
    }

    public static bool DoRedo()
    {
        var stack = GetActiveStack();
        if (stack == null)
            return false;

        var result = stack.DoRedo(_pendingCallbacks);
        if (result)
            CallPendingCallbacks();
        return result;
    }

    public static void Cancel()
    {
        // Cancel routes to whichever stack the last Record() went to
        var stack = _lastRecordStack;
        if (stack == null)
            return;

        stack.Cancel(_pendingCallbacks);
        CallPendingCallbacks();
        _lastRecordStack = null;
    }

    public static void RemoveDocument(Document doc)
    {
        _workspaceStack.RemoveDocument(doc);
        doc.UndoHistory?.Clear();
        doc.UndoHistory = null;
    }

    public static void Clear()
    {
        _workspaceStack.Clear();
        _pendingCallbacks.Clear();
        _currentGroupId = -1;
        _nextGroupId = 1;
        _lastRecordStack = null;
    }

    private static UndoStack? GetActiveStack()
    {
        if (Workspace.State == WorkspaceState.Edit)
            return Workspace.ActiveDocument?.UndoHistory;
        return _workspaceStack;
    }

    private static void CallPendingCallbacks()
    {
        foreach (var doc in _pendingCallbacks)
            doc.OnUndoRedo();

        Workspace.ActiveEditor?.OnUndoRedo();
        _pendingCallbacks.Clear();
    }

    internal static Document CloneDocument(Document source)
    {
        var clone = source.Def.Factory();
        clone.Def = source.Def;
        clone.Name = source.Name;
        clone.Path = source.Path;
        clone.Position = source.Position;
        clone.SavedPosition = source.SavedPosition;
        clone.Bounds = source.Bounds;
        clone.Clone(source);
        return clone;
    }

    internal static void RestoreDocument(Document target, Document snapshot)
    {
        var wasEditing = target.IsEditing;
        target.Position = snapshot.Position;
        target.SavedPosition = snapshot.SavedPosition;
        target.Bounds = snapshot.Bounds;
        target.Name = snapshot.Name;
        target.Path = snapshot.Path;
        target.Clone(snapshot);
        target.IsEditing = wasEditing;
    }
}
