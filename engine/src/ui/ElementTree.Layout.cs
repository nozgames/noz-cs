//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static unsafe partial class ElementTree
{
    private static int _layoutDepth;
    private static bool _layoutCycleLogged;

    private static float EdgeInset(in EdgeInsets ei, int axis) => axis == 0 ? ei.Horizontal : ei.Vertical;
    private static float EdgeMin(in EdgeInsets ei, int axis) => axis == 0 ? ei.L : ei.T;
    private static EdgeInsets GetInsets(ref Element e) => e.Type == ElementType.Padding ? e.Data.Padding : e.Data.Margin;

    private static float FitMaxChildren(ref Element e, int axis, int layoutAxis)
    {
        var max = 0f;
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            max = Math.Max(max, FitAxis(childOffset, axis, layoutAxis));
            ref var child = ref GetElement(childOffset);
            childOffset = child.NextSibling;
        }
        return max;
    }

    private static float FitAxis(int offset, int axis, int layoutAxis)
    {
        ref var e = ref GetElement(offset);
        switch (e.Type)
        {
            case ElementType.Size:
            {
                var mode = e.Data.Size.Size[axis].Mode;
                if (mode == SizeMode.Default)
                    mode = SizeMode.Fit;
                var fit = mode switch
                {
                    SizeMode.Fixed => e.Data.Size.Size[axis].Value,
                    SizeMode.Fit => e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0,
                    SizeMode.Percent => 0,
                    _ => 0
                };
                var fitMin = axis == 0 ? e.Data.Size.MinWidth : e.Data.Size.MinHeight;
                var fitMax = axis == 0 ? e.Data.Size.MaxWidth : e.Data.Size.MaxHeight;
                return Math.Clamp(fit, fitMin, fitMax);
            }

            case ElementType.Padding:
            case ElementType.Margin:
            {
                var child = e.ChildCount > 0 ? FitMaxChildren(ref e, axis, layoutAxis) : 0;
                return child + EdgeInset(GetInsets(ref e), axis);
            }

            case ElementType.Row:
                return FitRowColumn(ref e, axis, 0, e.Data.Spacing);

            case ElementType.Column:
                return FitRowColumn(ref e, axis, 1, e.Data.Spacing);

            case ElementType.Flex:
                return e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0;

            case ElementType.Grid:
            case ElementType.Collection:
            case ElementType.Popup:
                return 0;

            case ElementType.Spacer:
                return e.Data.Spacing;

            case ElementType.Text:
            {
                ref var d = ref e.Data.Text;
                var font = (Font)_assets[d.Font]!;
                if (axis == 1 && d.Overflow == TextOverflow.Wrap && e.Rect.Width > 0)
                    return TextRender.MeasureWrapped(d.Text.AsReadOnlySpan(), font, d.FontSize, e.Rect.Width).Y;
                var measure = TextRender.Measure(d.Text.AsReadOnlySpan(), font, d.FontSize);
                return measure[axis];
            }

            case ElementType.Image:
            {
                ref var d = ref e.Data.Image;
                if (d.Size[axis].IsFixed) return d.Size[axis].Value;
                if (d.Size[axis].IsPercent) return 0;
                return (axis == 0 ? d.Width : d.Height) * d.Scale;
            }

            case ElementType.Scene:
            {
                ref var d = ref e.Data.Scene;
                return d.Size[axis].IsFixed ? d.Size[axis].Value : 0;
            }

            case ElementType.EditableText:
                return FitEditableTextAxis(ref e, axis);

            default:
                return e.ChildCount > 0 ? FitMaxChildren(ref e, axis, layoutAxis) : 0;
        }
    }

    private static float FitRowColumn(ref Element e, int axis, int containerAxis, float spacing)
    {
        var fit = 0f;
        var childCount = 0;
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            if (child.Type == ElementType.Popup)
            {
                childOffset = child.NextSibling;
                continue;
            }

            if (child.Type == ElementType.Flex)
            {
                if (axis != containerAxis)
                    fit = Math.Max(fit, FitAxis(childOffset, axis, containerAxis));
            }
            else
            {
                var childFit = FitAxis(childOffset, axis, containerAxis);
                if (axis == containerAxis)
                    fit += childFit;
                else
                    fit = Math.Max(fit, childFit);
            }
            childCount++;
            childOffset = child.NextSibling;
        }
        if (axis == containerAxis && childCount > 1)
            fit += (childCount - 1) * spacing;
        return fit;
    }


    private static void LayoutAxis(int offset, float position, float available, int axis, int layoutAxis)
    {
        if (_layoutDepth > 200)
        {
            if (!_layoutCycleLogged)
            {
                _layoutCycleLogged = true;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"ElementTree: LayoutAxis depth > 200 at offset {offset}, axis={axis}, layoutAxis={layoutAxis}");
                sb.AppendLine($"Tree has {_elements.Length} elements. Dump:");
                DebugDumpTree(sb, 0, 0);
                Log.Error(sb.ToString());
            }
            return;
        }
        _layoutDepth++;
        LayoutAxisImpl(offset, position, available, axis, layoutAxis);
        _layoutDepth--;
    }

    private static void DebugDumpTree(System.Text.StringBuilder sb, int offset, int depth)
    {
        if (depth > 100) { sb.AppendLine($"{new string(' ', depth * 2)}... (depth limit)"); return; }
        ref var e = ref GetElement(offset);
        var indent = new string(' ', depth * 2);
        sb.Append($"{indent}[{offset}] {e.Type} parent={e.Parent} first={e.FirstChild} next={e.NextSibling} children={e.ChildCount}");
        if (e.Type == ElementType.Widget)
            sb.Append($" id={e.Data.Widget.Id}");
        sb.AppendLine();
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            DebugDumpTree(sb, childOffset, depth + 1);
            ref var child = ref GetElement(childOffset);
            childOffset = child.NextSibling;
        }
    }

    private static void LayoutAxisImpl(int offset, float position, float available, int axis, int layoutAxis)
    {
        ref var e = ref GetElement(offset);
        float size;

        switch (e.Type)
        {
            case ElementType.Size:
            {
                var mode = e.Data.Size.Size[axis].Mode;
                var isDefault = mode == SizeMode.Default;
                if (isDefault)
                    mode = (layoutAxis == axis) ? SizeMode.Fit : SizeMode.Percent;
                size = mode switch
                {
                    SizeMode.Fixed => e.Data.Size.Size[axis].Value,
                    SizeMode.Percent => available * (isDefault ? 1.0f : e.Data.Size.Size[axis].Value),
                    SizeMode.Fit => e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, layoutAxis) : 0,
                    _ => 0
                };
                var min = axis == 0 ? e.Data.Size.MinWidth : e.Data.Size.MinHeight;
                var max = axis == 0 ? e.Data.Size.MaxWidth : e.Data.Size.MaxHeight;
                size = Math.Clamp(size, min, max);
                break;
            }

            case ElementType.Padding:
            case ElementType.Margin:
            {
                var inset = EdgeInset(GetInsets(ref e), axis);
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitMaxChildren(ref e, axis, layoutAxis) : 0) + inset;
                break;
            }

            case ElementType.Row:
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(offset, axis, 0) : 0);
                break;

            case ElementType.Column:
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitAxis(offset, axis, 1) : 0);
                break;

            case ElementType.Flex:
                size = available;
                break;

            case ElementType.Spacer:
                size = e.Data.Spacing;
                break;

            case ElementType.Text:
            {
                ref var d = ref e.Data.Text;
                var font = (Font)_assets[d.Font]!;
                float contentSize;
                if (axis == 1 && d.Overflow == TextOverflow.Wrap && e.Rect.Width > 0)
                    contentSize = TextRender.MeasureWrapped(d.Text.AsReadOnlySpan(), font, d.FontSize, e.Rect.Width).Y;
                else
                    contentSize = TextRender.Measure(d.Text.AsReadOnlySpan(), font, d.FontSize)[axis];
                size = Math.Max(contentSize, available);
                break;
            }

            case ElementType.Image:
            {
                ref var d = ref e.Data.Image;
                if (d.Size[axis].IsFixed)
                    size = d.Size[axis].Value;
                else if (d.Size[axis].IsPercent)
                    size = available;
                else
                    size = available > 0 ? available : (axis == 0 ? d.Width : d.Height) * d.Scale;
                break;
            }

            case ElementType.Scene:
            {
                ref var d = ref e.Data.Scene;
                var mode = d.Size[axis].Mode;
                if (mode == SizeMode.Default) mode = SizeMode.Percent;
                size = mode switch
                {
                    SizeMode.Fixed => d.Size[axis].Value,
                    SizeMode.Percent => available * (d.Size[axis].Mode == SizeMode.Default ? 1.0f : d.Size[axis].Value),
                    _ => 0
                };
                break;
            }

            case ElementType.EditableText:
                size = LayoutEditableTextAxis(ref e, axis, available);
                break;

            case ElementType.Popup:
                size = e.ChildCount > 0 ? FitAxis(e.FirstChild, axis, -1) : 0;
                break;

            case ElementType.Grid:
            {
                ref var d = ref e.Data.Grid;
                if (axis == 0)
                {
                    size = available;
                }
                else
                {
                    var (cols, cw, ch) = UI.ResolveGridCellSize(
                        d.Columns, d.CellWidth, d.CellHeight,
                        d.CellMinWidth, d.CellHeightOffset,
                        d.Spacing, e.Rect.Width);
                    var totalItems = d.VirtualCount > 0 ? d.VirtualCount : e.ChildCount;
                    var rowCount = (totalItems + cols - 1) / cols;
                    size = rowCount * ch + Math.Max(0, rowCount - 1) * d.Spacing;
                }
                break;
            }

            case ElementType.Collection:
            {
                ref var d = ref e.Data.Collection;
                if (axis == 0)
                {
                    size = available;
                }
                else
                {
                    var cols = Math.Max(1, d.Columns);
                    var totalItems = d.VirtualCount > 0 ? d.VirtualCount : e.ChildCount;
                    var rowCount = (totalItems + cols - 1) / cols;
                    size = rowCount * d.ItemHeight + Math.Max(0, rowCount - 1) * d.Spacing;
                }
                break;
            }

            default:
                size = layoutAxis != axis
                    ? available
                    : (e.ChildCount > 0 ? FitMaxChildren(ref e, axis, layoutAxis) : 0);
                break;
        }

        e.Rect[axis] = position;
        e.Rect[axis + 2] = size;

        // Recurse children
        switch (e.Type)
        {
            case ElementType.Row when axis == 0:
                LayoutRowColumnAxis(ref e, axis, 0);
                break;
            case ElementType.Row when axis == 1:
                LayoutCrossAxis(ref e, axis);
                break;
            case ElementType.Column when axis == 1:
                LayoutRowColumnAxis(ref e, axis, 1);
                break;
            case ElementType.Column when axis == 0:
                LayoutCrossAxis(ref e, axis);
                break;
            case ElementType.Align:
                LayoutAlignAxis(ref e, axis);
                break;
            case ElementType.Padding:
            case ElementType.Margin:
            {
                var insets = GetInsets(ref e);
                var inset = EdgeInset(insets, axis);
                var childPos = e.Rect[axis] + EdgeMin(insets, axis);
                var childAvail = Math.Max(0, size - inset);
                LayoutChildrenAxis(ref e, childPos, childAvail, axis, layoutAxis);
                break;
            }
            case ElementType.Size:
            {
                var mode = e.Data.Size.Size[axis].Mode;
                var isFit = mode == SizeMode.Fit || (mode == SizeMode.Default && layoutAxis == axis);
                var childLayoutAxis = isFit ? layoutAxis : -1;
                // If min/max clamping expanded beyond content fit, stretch children to fill
                if (isFit && e.ChildCount > 0)
                {
                    var fitSize = FitAxis(e.FirstChild, axis, layoutAxis);
                    if (size > fitSize)
                        childLayoutAxis = -1;
                }
                LayoutChildrenAxis(ref e, e.Rect[axis], size, axis, childLayoutAxis);
                break;
            }
            case ElementType.Flex:
                LayoutChildrenAxis(ref e, e.Rect[axis], size, axis, -1);
                break;
            case ElementType.Popup:
                AdjustPopupPosition(ref e, axis, size);
                LayoutChildrenAxis(ref e, e.Rect[axis], e.Rect.GetSize(axis), axis, -1);
                break;
            case ElementType.Grid:
                LayoutGridAxis(ref e, axis);
                break;
            case ElementType.Collection:
                LayoutCollectionAxis(ref e, axis);
                break;
            case ElementType.Scroll:
                LayoutChildrenAxis(ref e, e.Rect[axis], size, axis, axis == 1 ? 1 : layoutAxis);
                AdjustScrollLayout(ref e, axis, size);
                break;
            default:
                LayoutChildrenAxis(ref e, e.Rect[axis], size, axis, layoutAxis);
                break;
        }

    }

    private static void AdjustPopupPosition(ref Element e, int axis, float size)
    {
        ref var pd = ref e.Data.Popup;
        if (pd.AnchorRect == Rect.Zero)
        {
            e.Rect[axis] = 0;
            e.Rect[axis + 2] = ScreenSize[axis];
        }
        else
        {
            var anchorPos = pd.AnchorRect[axis] + pd.AnchorRect[axis + 2] * (axis == 0 ? pd.AnchorFactorX : pd.AnchorFactorY);
            var popupAlignFactor = axis == 0 ? pd.PopupAlignFactorX : pd.PopupAlignFactorY;
            var anchorFactor = axis == 0 ? pd.AnchorFactorX : pd.AnchorFactorY;
            e.Rect[axis] = anchorPos - size * popupAlignFactor;
            if (anchorFactor != popupAlignFactor)
                e.Rect[axis] += pd.Spacing * (1f - 2f * popupAlignFactor);
            if (pd.ClampToScreen)
            {
                var maxPos = ScreenSize[axis] - size;
                e.Rect[axis] = maxPos >= 0 ? Math.Clamp(e.Rect[axis], 0, maxPos) : 0;
            }
        }
    }

    private static void AdjustScrollLayout(ref Element e, int axis, float size)
    {
        if (axis != 1) return;
        ref var sd = ref e.Data.Scroll;
        if (sd.State == null) return;
        ref var state = ref *sd.State;
        state.ContentHeight = ScrollContentBottom(ref e, e.Rect[axis]);
        var maxScroll = Math.Max(0, state.ContentHeight - size);
        if (state.Offset > maxScroll)
            state.Offset = maxScroll;
        if (state.Offset != 0)
            ApplyScrollOffset(ref e, state.Offset);
    }

    private static void LayoutGridAxis(ref Element e, int axis)
    {
        ref var d = ref e.Data.Grid;
        var (columns, cellWidth, cellHeight) = UI.ResolveGridCellSize(
            d.Columns, d.CellWidth, d.CellHeight,
            d.CellMinWidth, d.CellHeightOffset,
            d.Spacing, e.Rect.Width);

        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            var virtualIndex = d.StartIndex + i;
            var col = virtualIndex % columns;
            var row = virtualIndex / columns;
            var childPos = axis == 0
                ? e.Rect.X + col * (cellWidth + d.Spacing)
                : e.Rect.Y + row * (cellHeight + d.Spacing);
            var childAvail = axis == 0 ? cellWidth : cellHeight;

            ref var child = ref GetElement(childOffset);
            LayoutAxis(childOffset, childPos, childAvail, axis, -1);
            childOffset = child.NextSibling;
        }
    }

    private static void LayoutCollectionAxis(ref Element e, int axis)
    {
        ref var d = ref e.Data.Collection;
        var columns = Math.Max(1, d.Columns);
        var itemWidth = d.ItemWidth > 0 ? d.ItemWidth
            : (e.Rect.Width - Math.Max(0, columns - 1) * d.Spacing) / columns;

        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            var virtualIndex = d.StartIndex + i;
            var col = virtualIndex % columns;
            var row = virtualIndex / columns;
            var childPos = axis == 0
                ? e.Rect.X + col * (itemWidth + d.Spacing)
                : e.Rect.Y + row * (d.ItemHeight + d.Spacing);
            var childAvail = axis == 0 ? itemWidth : d.ItemHeight;

            ref var child = ref GetElement(childOffset);
            LayoutAxis(childOffset, childPos, childAvail, axis, -1);
            childOffset = child.NextSibling;
        }
    }

    private static void LayoutChildrenAxis(ref Element e, float childPos, float childAvail, int axis, int layoutAxis)
    {
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            LayoutAxis(childOffset, childPos, childAvail, axis, layoutAxis);
            childOffset = child.NextSibling;
        }
    }

    private static void LayoutAlignAxis(ref Element e, int axis)
    {
        if (e.ChildCount == 0) return;
        var align = e.Data.Align;
        var alignFactor = (axis == 0 ? align.X : align.Y).ToFactor();
        var parentSize = e.Rect.GetSize(axis);
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            var childFit = FitAxis(childOffset, axis, -1);
            var childAvail = childFit > 0 ? childFit : parentSize;
            var childPos = e.Rect[axis] + (parentSize - childAvail) * alignFactor;
            LayoutAxis(childOffset, childPos, childAvail, axis, -1);
            childOffset = child.NextSibling;
        }
    }

    private static void LayoutCrossAxis(ref Element e, int axis)
    {
        var pos = e.Rect[axis];
        var avail = e.Rect.GetSize(axis);
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            LayoutAxis(childOffset, pos, avail, axis, axis == 0 ? 1 : 0);

            // Stretch cross-axis children to fill available space
            // This lets leaf elements (Image, Text) use draw-time alignment for centering
            if (child.Rect.GetSize(axis) < avail)
            {
                child.Rect[axis] = pos;
                child.Rect[axis + 2] = avail;
            }

            childOffset = child.NextSibling;
        }
    }

    private static void LayoutRowColumnAxis(ref Element e, int axis, int containerAxis)
    {
        var spacing = e.Data.Spacing;
        var fixedTotal = 0f;
        var flexTotal = 0f;
        var fixedFlexTotal = 0f;
        var childCount = 0;

        // Pass 1: measure totals + resolve FlexSplitter overrides in-place.
        // When a FlexSplitter is found, we write resolved weights directly into
        // the neighboring Flex elements' Data.Flex (safe since the tree is rebuilt each frame).
        var childOffset = (int)e.FirstChild;
        var prevFlexOffset = -1;
        var pendingOverride = -1f;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            if (child.Type == ElementType.Popup)
            {
                childOffset = child.NextSibling;
                continue;
            }

            if (child.Type == ElementType.Flex)
            {
                if (pendingOverride != -1f)
                {
                    child.Data.Flex = pendingOverride;
                    pendingOverride = -1f;
                }
                if (child.Data.Flex < 0)
                    fixedFlexTotal += -child.Data.Flex;
                else
                    flexTotal += child.Data.Flex;
                prevFlexOffset = childOffset;
            }
            else
            {
                ResolveFlexSplitterOverrides(ref child, prevFlexOffset, containerAxis, ref flexTotal, ref fixedFlexTotal, ref pendingOverride);
                fixedTotal += FitAxis(childOffset, axis, containerAxis);
            }

            childCount++;
            childOffset = child.NextSibling;
        }
        if (childCount > 1)
            fixedTotal += (childCount - 1) * spacing;

        // Pass 2: layout children. Data.Flex already has resolved weights.
        var offset = 0f;
        var isFirst = true;
        var remaining = e.Rect.GetSize(axis) - fixedTotal - fixedFlexTotal;
        childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);

            if (child.Type == ElementType.Popup)
            {
                LayoutAxis(childOffset, e.Rect[axis], 0, axis, containerAxis);
                childOffset = child.NextSibling;
                continue;
            }

            if (!isFirst) offset += spacing;
            isFirst = false;

            var childPos = e.Rect[axis] + offset;

            if (child.Type == ElementType.Flex)
            {
                var flexSize = child.Data.Flex < 0
                    ? -child.Data.Flex
                    : flexTotal > 0 ? (child.Data.Flex / flexTotal) * remaining : 0;
                LayoutAxis(childOffset, childPos, flexSize, axis, containerAxis);
                offset += flexSize;
            }
            else
            {
                var childFit = FitAxis(childOffset, axis, containerAxis);
                LayoutAxis(childOffset, childPos, childFit, axis, containerAxis);
                offset += child.Rect.GetSize(axis);
            }

            childOffset = child.NextSibling;
        }
    }

    private static bool FindFlexSplitterElement(ref Element e, out int splitterOffset)
    {
        if (e.Type == ElementType.FlexSplitter)
        {
            splitterOffset = e.Index;
            return true;
        }
        if (e.Type == ElementType.Widget && e.ChildCount > 0)
        {
            ref var firstChild = ref GetElement(e.FirstChild);
            if (firstChild.Type == ElementType.FlexSplitter)
            {
                splitterOffset = firstChild.Index;
                return true;
            }
        }
        splitterOffset = 0;
        return false;
    }

    private static void ResolveFlexSplitterOverrides(ref Element child, int prevFlexOffset, int containerAxis, ref float flexTotal, ref float fixedFlexTotal, ref float pendingOverride)
    {
        if (!FindFlexSplitterElement(ref child, out var splitterOffset))
            return;

        ref var sd = ref GetElement(splitterOffset).Data.FlexSplitter;
        if (sd.State == null)
            return;

        ref var state = ref *sd.State;
        sd.Axis = (byte)containerAxis;
        sd.PrevFlex = (ushort)Math.Max(prevFlexOffset, 0);

        // Encode the fixed pane as a negative flex value (exact pixel size).
        // The non-fixed pane keeps its default flex weight and gets remaining space.
        var fixedSize = -Math.Max(state.FixedSize, 1f);
        if (state.FixedPane == 1 && prevFlexOffset >= 0)
        {
            ref var prev = ref GetElement(prevFlexOffset);
            flexTotal -= prev.Data.Flex;
            fixedFlexTotal += -fixedSize;
            prev.Data.Flex = fixedSize;
        }
        else if (state.FixedPane == 2)
        {
            pendingOverride = fixedSize;
        }
        sd.NextFlex = 0;
        var nextOffset = (int)child.NextSibling;
        while (nextOffset != 0)
        {
            ref var next = ref GetElement(nextOffset);
            if (next.Type == ElementType.Flex) { sd.NextFlex = (ushort)nextOffset; break; }
            if (next.Type != ElementType.Popup) break;
            nextOffset = next.NextSibling;
        }
    }

    private static void UpdateTransforms(int offset, in Matrix3x2 parentTransform, in Matrix3x2 parentLayoutTransform, Vector2 parentSize)
    {
        ref var e = ref GetElement(offset);

        Matrix3x2 localTransform;
        Matrix3x2 worldTransform;
        Matrix3x2 layoutTransform; // Tracks only layout positioning, excludes Transform visual offsets
        if (e.Type == ElementType.Transform)
        {
            ref var d = ref e.Data.Transform;
            var pivot = new Vector2(e.Rect.Width * d.Pivot.X, e.Rect.Height * d.Pivot.Y);
            localTransform =
                Matrix3x2.CreateTranslation(-pivot) *
                Matrix3x2.CreateScale(d.Scale) *
                Matrix3x2.CreateRotation(MathEx.Deg2Rad * d.Rotate) *
                Matrix3x2.CreateTranslation(e.Rect.X + pivot.X + d.Translate.X,
                                             e.Rect.Y + pivot.Y + d.Translate.Y);
            worldTransform = localTransform * parentTransform;
            e.Transform = worldTransform;

            // Layout transform uses only the layout position (no pivot/translate)
            layoutTransform = Matrix3x2.CreateTranslation(e.Rect.X, e.Rect.Y) * parentLayoutTransform;

            // Rect becomes element-local (relative to pivot)
            e.Rect.X = -e.Rect.Width * d.Pivot.X;
            e.Rect.Y = -e.Rect.Height * d.Pivot.Y;
        }
        else
        {
            localTransform = Matrix3x2.CreateTranslation(e.Rect.X, e.Rect.Y);
            worldTransform = localTransform * parentTransform;
            layoutTransform = localTransform * parentLayoutTransform;

            e.Transform = worldTransform;
            e.Rect.X = 0;
            e.Rect.Y = 0;
        }

        // Copy rect and transform to widget state when ready
        if (e.Type == ElementType.Widget)
        {
            ref var state = ref GetWidgetState(e.Data.Widget.Id);
            state.Transform = worldTransform;
            state.Rect = e.Rect;
        }

        var rectSize = e.Rect.Size;
        var childOffset = (int)e.FirstChild;
        for (int i = 0; i < e.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            var absPos = child.Rect.Position;
            child.Rect.X = absPos.X - layoutTransform.M31;
            child.Rect.Y = absPos.Y - layoutTransform.M32;
            UpdateTransforms(childOffset, worldTransform, layoutTransform, rectSize);
            childOffset = child.NextSibling;
        }
    }

    private static void ApplyScrollOffset(ref Element parent, float offset)
    {
        var childOffset = (int)parent.FirstChild;
        for (int i = 0; i < parent.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            if (child.Type != ElementType.Popup)
            {
                child.Rect.Y -= offset;
                ApplyScrollOffset(ref child, offset);
            }
            childOffset = child.NextSibling;
        }
    }

    private static float ScrollContentBottom(ref Element parent, float scrollY)
    {
        float max = 0;
        var childOffset = (int)parent.FirstChild;
        for (int i = 0; i < parent.ChildCount; i++)
        {
            ref var child = ref GetElement(childOffset);
            if (child.Type != ElementType.Popup)
            {
                var bottom = child.Rect.Y + child.Rect.Height - scrollY;
                max = Math.Max(max, bottom);
                max = Math.Max(max, ScrollContentBottom(ref child, scrollY));
            }
            childOffset = child.NextSibling;
        }
        return max;
    }
}
