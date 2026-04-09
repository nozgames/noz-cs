//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public abstract partial class SpriteEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId AddEdgesButton { get; }
        public static partial WidgetId RemoveEdgesButton { get; }
        public static partial WidgetId EditEdgesButton { get; }
        public static partial WidgetId EdgeTop { get; }
        public static partial WidgetId EdgeLeft { get; }
        public static partial WidgetId EdgeBottom { get; }
        public static partial WidgetId EdgeRight { get; }
    }

    private EditorMode? _preEdgeEditMode;

    public bool IsEditingEdges => Mode is EdgeEditMode;

    public void ToggleEdgeEditMode()
    {
        if (IsEditingEdges)
        {
            _preEdgeEditMode = null;
            ExitEdgeEditMode();
        }
        else
        {
            _preEdgeEditMode = Mode;
            SetMode(new EdgeEditMode());
        }
    }

    protected virtual void ExitEdgeEditMode()
    {
        SetMode(null);
    }

    protected void DrawEdges()
    {
        // EdgeEditMode draws its own (interactive) version of the edges.
        if (IsEditingEdges) return;
        if (Document.Edges.IsZero) return;

        var bounds = Document.Bounds;
        var edges = Document.Edges;

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(6);
            Gizmos.SetColor(new Color(0.2f, 0.9f, 0.2f, 0.8f));

            var lineWidth = EditorStyle.Workspace.DocumentBoundsLineWidth;

            if (edges.T > 0)
            {
                var y = bounds.Top + edges.T;
                Gizmos.DrawLine(new Vector2(bounds.Left, y), new Vector2(bounds.Right, y), lineWidth);
            }

            if (edges.B > 0)
            {
                var y = bounds.Bottom - edges.B;
                Gizmos.DrawLine(new Vector2(bounds.Left, y), new Vector2(bounds.Right, y), lineWidth);
            }

            if (edges.L > 0)
            {
                var x = bounds.Left + edges.L;
                Gizmos.DrawLine(new Vector2(x, bounds.Top), new Vector2(x, bounds.Bottom), lineWidth);
            }

            if (edges.R > 0)
            {
                var x = bounds.Right - edges.R;
                Gizmos.DrawLine(new Vector2(x, bounds.Top), new Vector2(x, bounds.Bottom), lineWidth);
            }
        }
    }

    private static bool EdgeIntInput(WidgetId id, float worldValue, int ppu, out float newWorldValue)
    {
        var pixelValue = (int)MathF.Round(worldValue * ppu);
        var text = pixelValue.ToString();
        newWorldValue = worldValue;
        using (UI.BeginFlex())
        {
            var result = UI.TextInput(id, text, EditorStyle.Inspector.TextBox, "0");
            if (result != text && int.TryParse(result, out var parsed) && parsed >= 0)
            {
                newWorldValue = parsed / (float)ppu;
                return true;
            }
        }
        return false;
    }

    protected void EdgesInspectorUI()
    {
        if (Document.Edges.IsZero)
        {
            using (Inspector.BeginSection("EDGES", content: EmptyContent, empty: true))
            return;
        }

        using (Inspector.BeginSection("EDGES", content: NonEmptyContent))
        {
            if (Inspector.IsSectionCollapsed) return;

            var edges = Document.Edges;
            var ppu = Document.PixelsPerUnit;
            var changed = false;

            using (Inspector.BeginProperty("Top"))
                if (EdgeIntInput(WidgetIds.EdgeTop, edges.T, ppu, out var v)) { edges = new EdgeInsets(v, edges.L, edges.B, edges.R); changed = true; }

            using (Inspector.BeginProperty("Left"))
                if (EdgeIntInput(WidgetIds.EdgeLeft, edges.L, ppu, out var v)) { edges = new EdgeInsets(edges.T, v, edges.B, edges.R); changed = true; }

            using (Inspector.BeginProperty("Bottom"))
                if (EdgeIntInput(WidgetIds.EdgeBottom, edges.B, ppu, out var v)) { edges = new EdgeInsets(edges.T, edges.L, v, edges.R); changed = true; }

            using (Inspector.BeginProperty("Right"))
                if (EdgeIntInput(WidgetIds.EdgeRight, edges.R, ppu, out var v)) { edges = new EdgeInsets(edges.T, edges.L, edges.B, v); changed = true; }

            if (changed)
            {
                Document.Edges = edges;
                UI.HandleChange(Document);
            }
        }

        void EmptyContent()
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            if (UI.Button(WidgetIds.AddEdgesButton, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
            {
                Undo.Record(Document);
                Document.Edges = new EdgeInsets(8f / Document.PixelsPerUnit);
                Document.UpdateBounds();
            }
            ElementTree.EndAlign();
        }

        void NonEmptyContent()
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            using (UI.BeginRow(EditorStyle.Control.Spacing))
            {
                if (UI.Button(WidgetIds.EditEdgesButton, EditorAssets.Sprites.IconEdit, EditorStyle.Inspector.SectionButton, isSelected: IsEditingEdges))
                    ToggleEdgeEditMode();

                if (UI.Button(WidgetIds.RemoveEdgesButton, EditorAssets.Sprites.IconDelete, EditorStyle.Inspector.SectionButton))
                {
                    if (IsEditingEdges)
                        ToggleEdgeEditMode();
                    Undo.Record(Document);
                    Document.Edges = EdgeInsets.Zero;
                }
            }
            ElementTree.EndAlign();
        }
    }
}
