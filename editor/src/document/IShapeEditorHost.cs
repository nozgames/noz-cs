//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public interface IShapeEditorHost
{
    Document Document { get; }
    Shape CurrentShape { get; }
    Color32 NewPathFillColor { get; }
    PathOperation NewPathOperation { get; }
    void OnSelectionChanged(bool hasSelection);
    void ClearAllSelections();
    void InvalidateMesh();
    bool SnapToPixelGrid { get; }
    Shape? GetShapeWithSelection();
    void ForEachEditableShape(Action<Shape> action);
}
