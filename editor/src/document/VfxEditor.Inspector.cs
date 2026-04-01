//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Linq;
using System.Numerics;

namespace NoZ.Editor;

internal partial class VfxEditor
{
    private static partial class FieldId
    {
        public static partial WidgetId VfxDuration { get; }
        public static partial WidgetId VfxLoop { get; }

        public static partial WidgetId EmitterRate { get; }
        public static partial WidgetId EmitterBurst { get; }
        public static partial WidgetId EmitterDuration { get; }
        public static partial WidgetId EmitterWorldSpace { get; }
        public static partial WidgetId EmitterParticle { get; }
        public static partial WidgetId EmitterSpawnShape { get; }
        public static partial WidgetId EmitterSpawnOffset { get; }
        public static partial WidgetId EmitterSpawnRadius { get; }
        public static partial WidgetId EmitterSpawnInnerRadius { get; }
        public static partial WidgetId EmitterSpawnSize { get; }
        public static partial WidgetId EmitterSpawnInnerSize { get; }
        public static partial WidgetId EmitterSpawnRotation { get; }
        public static partial WidgetId EmitterDirection { get; }  // +0..+2
        public static partial WidgetId EmitterSpread { get; }     // +0..+2
        public static partial WidgetId EmitterRadial { get; }

        public static partial WidgetId ParticleDuration { get; }
        public static partial WidgetId ParticleSize { get; }
        public static partial WidgetId ParticleSpeed { get; }
        public static partial WidgetId ParticleColor { get; }
        public static partial WidgetId ParticleOpacity { get; }
        public static partial WidgetId ParticleGravity { get; }
        public static partial WidgetId ParticleDrag { get; }
        public static partial WidgetId ParticleRotation { get; }
        public static partial WidgetId ParticleRotationSpeed { get; }

        // Addable section buttons
        public static partial WidgetId AddSize { get; }
        public static partial WidgetId AddSpeed { get; }
        public static partial WidgetId AddColor { get; }
        public static partial WidgetId AddOpacity { get; }
        public static partial WidgetId AddGravity { get; }
        public static partial WidgetId AddDrag { get; }
        public static partial WidgetId AddRotation { get; }
        public static partial WidgetId RemoveSize { get; }
        public static partial WidgetId RemoveSpeed { get; }
        public static partial WidgetId RemoveColor { get; }
        public static partial WidgetId RemoveOpacity { get; }
        public static partial WidgetId RemoveGravity { get; }
        public static partial WidgetId RemoveDrag { get; }
        public static partial WidgetId RemoveRotation { get; }
        public static partial WidgetId SpriteDropDown { get; }
    }

    private struct RandomToggleState
    {
        public byte Initialized;
        public byte Expanded;
    }

    public override void InspectorUI()
    {
        switch (Document.SelectedType)
        {
            case VfxSelectionType.Vfx:
                VfxInspectorUI();
                break;

            case VfxSelectionType.Emitter:
                if (Document.SelectedIndex >= 0 && Document.SelectedIndex < Document.Emitters.Count)
                    EmitterInspectorUI(Document.Emitters[Document.SelectedIndex]);
                break;

            case VfxSelectionType.Particle:
                if (Document.SelectedIndex >= 0 && Document.SelectedIndex < Document.Particles.Count)
                    ParticleInspectorUI(Document.Particles[Document.SelectedIndex]);
                break;
        }
    }

    private void VfxInspectorUI()
    {
        using (Inspector.BeginSection("VFX"))
        {
            if (Inspector.IsSectionCollapsed) return;

            var duration = Document.Duration;
            if (RangeField(FieldId.VfxDuration, "Duration", ref duration))
            {
                Undo.Record(Document);
                Document.Duration = duration;
                Document.ApplyChanges();
            }

            var loop = Document.Loop;
            using (Inspector.BeginProperty("Loop"))
            {
                if (UI.Toggle(FieldId.VfxLoop, "", loop, EditorStyle.Inspector.Toggle, EditorAssets.Sprites.IconCheck))
                {
                    Undo.Record(Document);
                    Document.Loop = !loop;
                    Document.ApplyChanges();
                }
            }
        }
    }

    private void EmitterInspectorUI(VfxDocEmitter emitter)
    {
        using (Inspector.BeginSection("EMITTER"))
        {
            if (Inspector.IsSectionCollapsed) return;

            var changed = false;

            if (IntRangeField(FieldId.EmitterRate, "Rate", ref emitter.Def.Rate)) changed = true;
            if (IntRangeField(FieldId.EmitterBurst, "Burst", ref emitter.Def.Burst)) changed = true;
            if (RangeField(FieldId.EmitterDuration, "Duration", ref emitter.Def.Duration)) changed = true;

            using (Inspector.BeginProperty("WorldSpace"))
            {
                if (UI.Toggle(FieldId.EmitterWorldSpace, "", emitter.Def.WorldSpace, EditorStyle.Inspector.Toggle, EditorAssets.Sprites.IconCheck))
                {
                    emitter.Def.WorldSpace = !emitter.Def.WorldSpace;
                    changed = true;
                }
            }

            // Particle dropdown
            using (Inspector.BeginProperty("Particle"))
            {
                var currentName = emitter.ParticleRef;
                UI.DropDown(FieldId.EmitterParticle, () =>
                {
                    var items = new List<PopupMenuItem>();
                    foreach (var p in Document.Particles)
                    {
                        var name = p.Name;
                        items.Add(PopupMenuItem.Item(name, () =>
                        {
                            Undo.Record(Document);
                            emitter.ParticleRef = name;
                            Document.ApplyChanges();
                        }));
                    }
                    return [.. items];
                }, text: currentName);
            }

            if (changed)
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
        }

        using (Inspector.BeginSection("SPAWN"))
        {
            if (!Inspector.IsSectionCollapsed && SpawnDefFields(emitter))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
        }

        using (Inspector.BeginSection("DIRECTION"))
        {
            if (!Inspector.IsSectionCollapsed)
            {
                var changed = false;
                if (RangeField(FieldId.EmitterDirection, "Direction", ref emitter.Def.Direction)) changed = true;
                if (RangeField(FieldId.EmitterSpread, "Spread", ref emitter.Def.Spread)) changed = true;
                using (Inspector.BeginProperty("Radial"))
                    if (FloatInput(FieldId.EmitterRadial, ref emitter.Def.Radial)) changed = true;
                if (changed)
                {
                    Undo.Record(Document);
                    Document.ApplyChanges();
                }
            }
        }
    }

    // --- Spawn Shape Fields ---

    private static readonly (string Name, VfxSpawnShape Shape)[] SpawnShapeOptions =
    [
        ("Point", VfxSpawnShape.Point),
        ("Circle", VfxSpawnShape.Circle),
        ("Box", VfxSpawnShape.Box),
    ];

    private static bool _spawnShapeChanged;
    private static VfxSpawnShape _spawnShapeNewValue;
    private static WidgetId _spawnShapeChangedId;

    private static bool SpawnDefFields(VfxDocEmitter emitter)
    {
        var changed = false;
        ref var spawn = ref emitter.Def.Spawn;

        // Shape dropdown
        using (Inspector.BeginProperty("Shape"))
        {
            var currentName = spawn.Shape.ToString();
            if (_spawnShapeChanged && _spawnShapeChangedId == FieldId.EmitterSpawnShape)
            {
                _spawnShapeChanged = false;
                spawn = new VfxSpawnDef { Shape = _spawnShapeNewValue };
                if (_spawnShapeNewValue == VfxSpawnShape.Circle)
                    spawn.Circle.Radius = 1f;
                else if (_spawnShapeNewValue == VfxSpawnShape.Box)
                    spawn.Box.Size = new Vector2(1f, 1f);
                changed = true;
            }

            UI.DropDown(FieldId.EmitterSpawnShape, () =>
            {
                var items = new PopupMenuItem[SpawnShapeOptions.Length];
                for (var i = 0; i < SpawnShapeOptions.Length; i++)
                {
                    var opt = SpawnShapeOptions[i];
                    items[i] = PopupMenuItem.Item(opt.Name, () =>
                    {
                        _spawnShapeChanged = true;
                        _spawnShapeChangedId = FieldId.EmitterSpawnShape;
                        _spawnShapeNewValue = opt.Shape;
                    });
                }
                return items;
            }, text: currentName);
        }

        // Offset (all shapes)
        using (Inspector.BeginProperty("Offset"))
        using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
        {
            if (FloatInput(FieldId.EmitterSpawnOffset, ref spawn.Offset.X)) changed = true;
            if (FloatInput(FieldId.EmitterSpawnOffset + 1, ref spawn.Offset.Y)) changed = true;
        }

        // Shape-specific fields
        switch (spawn.Shape)
        {
            case VfxSpawnShape.Circle:
                using (Inspector.BeginProperty("Radius"))
                    if (FloatInput(FieldId.EmitterSpawnRadius, ref spawn.Circle.Radius)) changed = true;
                using (Inspector.BeginProperty("Inner Radius"))
                    if (FloatInput(FieldId.EmitterSpawnInnerRadius, ref spawn.Circle.InnerRadius)) changed = true;
                break;

            case VfxSpawnShape.Box:
                using (Inspector.BeginProperty("Size"))
                using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
                {
                    if (FloatInput(FieldId.EmitterSpawnSize, ref spawn.Box.Size.X)) changed = true;
                    if (FloatInput(FieldId.EmitterSpawnSize + 1, ref spawn.Box.Size.Y)) changed = true;
                }
                using (Inspector.BeginProperty("Inner Size"))
                using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
                {
                    if (FloatInput(FieldId.EmitterSpawnInnerSize, ref spawn.Box.InnerSize.X)) changed = true;
                    if (FloatInput(FieldId.EmitterSpawnInnerSize + 1, ref spawn.Box.InnerSize.Y)) changed = true;
                }
                using (Inspector.BeginProperty("Rotation"))
                    if (FloatInput(FieldId.EmitterSpawnRotation, ref spawn.Box.Rotation)) changed = true;
                break;
        }

        return changed;
    }

    // --- Particle Inspector ---

    private void ParticleInspectorUI(VfxDocParticle particle)
    {
        using (Inspector.BeginSection("PARTICLE"))
        {
            if (Inspector.IsSectionCollapsed) return;

            if (RangeField(FieldId.ParticleDuration, "Duration", ref particle.Def.Duration))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }

            using (Inspector.BeginProperty("Sprite"))
            {
                particle.SpriteRef = EditorUI.SpriteButton(FieldId.SpriteDropDown, particle.SpriteRef);
                UI.HandleChange(Document);
            }
        }

        // Addable particle groups
        if (AddableSection("SIZE", particle.Def.Size != VfxFloatCurve.One, FieldId.AddSize, FieldId.RemoveSize,
            () => { particle.Def.Size = new VfxFloatCurve { Type = VfxCurveType.EaseOut, Start = new VfxRange(0.5f, 0.5f), End = new VfxRange(0f, 0.1f) }; },
            () => { particle.Def.Size = VfxFloatCurve.One; }))
        {
            if (FloatCurveField(FieldId.ParticleSize, "Size", ref particle.Def.Size))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("SPEED", particle.Def.Speed != VfxFloatCurve.Zero, FieldId.AddSpeed, FieldId.RemoveSpeed,
            () => { particle.Def.Speed = new VfxFloatCurve { Type = VfxCurveType.Linear, Start = new VfxRange(10, 20), End = new VfxRange(0, 5) }; },
            () => { particle.Def.Speed = VfxFloatCurve.Zero; }))
        {
            if (FloatCurveField(FieldId.ParticleSpeed, "Speed", ref particle.Def.Speed))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("COLOR", particle.Def.Color != VfxColorCurve.White, FieldId.AddColor, FieldId.RemoveColor,
            () => { particle.Def.Color = new VfxColorCurve { Type = VfxCurveType.Linear, Start = new VfxColorRange(Color.White, Color.White), End = new VfxColorRange(Color.Yellow, Color.Yellow) }; },
            () => { particle.Def.Color = VfxColorCurve.White; }))
        {
            if (ColorCurveField(FieldId.ParticleColor, "Color", ref particle.Def.Color))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("OPACITY", particle.Def.Opacity != VfxFloatCurve.One, FieldId.AddOpacity, FieldId.RemoveOpacity,
            () => { particle.Def.Opacity = new VfxFloatCurve { Type = VfxCurveType.EaseOut, Start = VfxRange.One, End = VfxRange.Zero }; },
            () => { particle.Def.Opacity = VfxFloatCurve.One; }))
        {
            if (FloatCurveField(FieldId.ParticleOpacity, "Opacity", ref particle.Def.Opacity))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("GRAVITY", particle.Def.Gravity != VfxVec2Range.Zero, FieldId.AddGravity, FieldId.RemoveGravity,
            () => { particle.Def.Gravity = new VfxVec2Range(new(0, 10), new(0, 10)); },
            () => { particle.Def.Gravity = VfxVec2Range.Zero; }))
        {
            if (Vec2RangeField(FieldId.ParticleGravity, "Gravity", ref particle.Def.Gravity))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("DRAG", particle.Def.Drag != VfxRange.Zero, FieldId.AddDrag, FieldId.RemoveDrag,
            () => { particle.Def.Drag = new VfxRange(1, 1); },
            () => { particle.Def.Drag = VfxRange.Zero; }))
        {
            if (RangeField(FieldId.ParticleDrag, "Drag", ref particle.Def.Drag))
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("ROTATION", particle.Def.Rotation != VfxRange.Zero || particle.Def.RotationSpeed != VfxFloatCurve.Zero,
            FieldId.AddRotation, FieldId.RemoveRotation,
            () => { particle.Def.Rotation = new VfxRange(0, 360); },
            () => { particle.Def.Rotation = VfxRange.Zero; particle.Def.RotationSpeed = VfxFloatCurve.Zero; }))
        {
            var changed = false;
            if (RangeField(FieldId.ParticleRotation, "Initial", ref particle.Def.Rotation)) changed = true;
            if (FloatCurveField(FieldId.ParticleRotationSpeed, "Speed", ref particle.Def.RotationSpeed)) changed = true;
            if (changed)
            {
                Undo.Record(Document);
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

    }

    // --- Addable Section Helper ---

    private Inspector.AutoSection _addableSectionHandle;

    private bool AddableSection(string name, bool isActive, WidgetId addId, WidgetId removeId, Action onAdd, Action onRemove)
    {
        if (!isActive)
        {
            using (Inspector.BeginSection(name, content: () =>
            {
                ElementTree.BeginAlign(Align.Min, Align.Center);
                if (UI.Button(addId, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
                {
                    Undo.Record(Document);
                    onAdd();
                    Document.ApplyChanges();
                }
                ElementTree.EndAlign();
            }, empty: true)) { }
            return false;
        }

        _addableSectionHandle = Inspector.BeginSection(name, content: () =>
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            if (UI.Button(removeId, EditorAssets.Sprites.IconDelete, EditorStyle.Inspector.SectionButton))
            {
                Undo.Record(Document);
                onRemove();
                Document.ApplyChanges();
            }
            ElementTree.EndAlign();
        });

        if (Inspector.IsSectionCollapsed)
        {
            ((IDisposable)_addableSectionHandle).Dispose();
            return false;
        }

        return true;
    }

    private void EndAddableSection()
    {
        ((IDisposable)_addableSectionHandle).Dispose();
    }

    // --- Field Helpers ---

    private static readonly ContainerStyle ValueRowStyle = new() { Spacing = 4, Height = Size.Fit, MinHeight = EditorStyle.Control.Height };
    private static readonly ContainerStyle CurveRowStyle = new() { Spacing = 4, Height = Size.Fit, MinHeight = 22 };

    // Range field: [label] [min] [⇄] [max]  — no curve row
    private static bool RangeField(WidgetId baseId, string label, ref VfxRange value)
    {
        var changed = false;
        using (Inspector.BeginProperty(label))
        using (UI.BeginRow(ValueRowStyle))
        {
            if (FloatInput(baseId, ref value.Min)) changed = true;
            if (RandomToggle(baseId + 1, value.Min != value.Max, ref value.Min, ref value.Max, out var isRandom)) changed = true;
            if (isRandom)
            {
                if (FloatInput(baseId + 2, ref value.Max)) changed = true;
            }
            else
                using (UI.BeginFlex()) { } // reserve space
        }
        return changed;
    }

    // Int range field: [label] [min] [⇄] [max]  — no curve row
    private static bool IntRangeField(WidgetId baseId, string label, ref VfxIntRange value)
    {
        var changed = false;
        using (Inspector.BeginProperty(label))
        using (UI.BeginRow(ValueRowStyle))
        {
            if (IntInput(baseId, ref value.Min)) changed = true;
            if (RandomToggleInt(baseId + 1, value.Min != value.Max, ref value.Min, ref value.Max, out var isRandom)) changed = true;
            if (isRandom)
            {
                if (IntInput(baseId + 2, ref value.Max)) changed = true;
            }
            else
                using (UI.BeginFlex()) { }
        }
        return changed;
    }

    // Vec2 range field: two rows of min/max (no random toggle — too complex)
    private static bool Vec2RangeField(WidgetId baseId, string label, ref VfxVec2Range value)
    {
        var changed = false;
        using (Inspector.BeginProperty(label))
        using (UI.BeginColumn(new ContainerStyle { Spacing = 2 }))
        {
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                if (FloatInput(baseId, ref value.Min.X)) changed = true;
                if (FloatInput(baseId + 1, ref value.Min.Y)) changed = true;
            }
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                if (FloatInput(baseId + 2, ref value.Max.X)) changed = true;
                if (FloatInput(baseId + 3, ref value.Max.Y)) changed = true;
            }
        }
        return changed;
    }

    // Float curve field with progressive disclosure
    // baseId+0: startMin, +1: startRandom, +2: startMax, +3: endMin, +4: endRandom, +5: endMax, +6: curveType
    private static bool FloatCurveField(WidgetId baseId, string label, ref VfxFloatCurve curve)
    {
        var changed = false;
        var hasCurve = curve.Start != curve.End;
        var startLabel = hasCurve ? "Start" : "Value";

        // Start/Value row
        using (Inspector.BeginProperty(startLabel))
        using (UI.BeginRow(ValueRowStyle))
        {
            if (FloatInput(baseId, ref curve.Start.Min)) changed = true;
            if (RandomToggle(baseId + 1, curve.Start.Min != curve.Start.Max, ref curve.Start.Min, ref curve.Start.Max, out var startRandom)) changed = true;
            if (startRandom)
            {
                if (FloatInput(baseId + 2, ref curve.Start.Max)) changed = true;
            }
            else
                using (UI.BeginFlex()) { }
        }

        // End row (only when curve active)
        if (hasCurve)
        {
            using (Inspector.BeginProperty("End"))
            using (UI.BeginRow(ValueRowStyle))
            {
                if (FloatInput(baseId + 3, ref curve.End.Min)) changed = true;
                if (RandomToggle(baseId + 4, curve.End.Min != curve.End.Max, ref curve.End.Min, ref curve.End.Max, out var endRandom)) changed = true;
                if (endRandom)
                {
                    if (FloatInput(baseId + 5, ref curve.End.Max)) changed = true;
                }
                else
                    using (UI.BeginFlex()) { }
            }
        }
        else if (changed)
        {
            // No curve active — keep End synced with Start so editing Start doesn't auto-enable the curve
            curve.End = curve.Start;
        }

        // Curve type row (always visible)
        if (CurveRow(baseId + 6, hasCurve, ref curve.Type, ref curve.Start, ref curve.End))
            changed = true;

        return changed;
    }

    // Color curve field with progressive disclosure
    private static bool ColorCurveField(WidgetId baseId, string label, ref VfxColorCurve curve)
    {
        var changed = false;
        var hasCurve = curve.Start != curve.End;
        var startLabel = hasCurve ? "Start" : "Value";

        // Start/Value row
        using (Inspector.BeginProperty(startLabel))
        using (UI.BeginRow(ValueRowStyle))
        {
            using (UI.BeginFlex())
                if (ColorInput(baseId, ref curve.Start.Min)) changed = true;
            if (ColorRandomToggle(baseId + 1, curve.Start.Min != curve.Start.Max, ref curve.Start.Min, ref curve.Start.Max, out var startRandom)) changed = true;
            if (startRandom)
            {
                using (UI.BeginFlex())
                    if (ColorInput(baseId + 2, ref curve.Start.Max)) changed = true;
            }
            else
                using (UI.BeginFlex()) { }
        }

        // End row
        if (hasCurve)
        {
            using (Inspector.BeginProperty("End"))
            using (UI.BeginRow(ValueRowStyle))
            {
                using (UI.BeginFlex())
                    if (ColorInput(baseId + 3, ref curve.End.Min)) changed = true;
                if (ColorRandomToggle(baseId + 4, curve.End.Min != curve.End.Max, ref curve.End.Min, ref curve.End.Max, out var endRandom)) changed = true;
                if (endRandom)
                {
                    using (UI.BeginFlex())
                        if (ColorInput(baseId + 5, ref curve.End.Max)) changed = true;
                }
                else
                    using (UI.BeginFlex()) { }
            }
        }
        else if (changed)
        {
            curve.End = curve.Start;
        }

        // Curve type row
        if (ColorCurveRow(baseId + 6, hasCurve, ref curve.Type, ref curve.Start, ref curve.End))
            changed = true;

        return changed;
    }

    // --- Curve Row ---

    // Curve type dropdown row for float curves. Returns true if changed.
    // When "None" selected: copies start to end. When type selected from None: keeps end as-is (user will edit).
    private static bool CurveRow(WidgetId id, bool hasCurve, ref VfxCurveType type, ref VfxRange start, ref VfxRange end)
    {
        using (Inspector.BeginProperty(""))
        using (UI.BeginRow(CurveRowStyle))
        {
            var oldHasCurve = hasCurve;
            var oldType = type;

            if (CurveTypeDropdown(id, ref type, ref hasCurve))
            {
                if (!hasCurve)
                    end = start; // "None" selected — collapse curve
                return true;
            }
        }
        return false;
    }

    private static bool ColorCurveRow(WidgetId id, bool hasCurve, ref VfxCurveType type, ref VfxColorRange start, ref VfxColorRange end)
    {
        using (Inspector.BeginProperty(""))
        using (UI.BeginRow(CurveRowStyle))
        {
            if (CurveTypeDropdown(id, ref type, ref hasCurve))
            {
                if (!hasCurve)
                    end = start;
                return true;
            }
        }
        return false;
    }

    // --- Random Toggle ---

    private static readonly ButtonStyle RandomButtonStyle = new()
    {
        Width = 21,
        Height = 21,
        Background = EditorStyle.Palette.Canvas,
        ContentColor = EditorStyle.Palette.SecondaryText,
        IconSize = 11,
        BorderRadius = EditorStyle.Control.BorderRadius,
        Resolve = (s, f) =>
        {
            if ((f & WidgetFlags.Hovered) != 0) s.Background = EditorStyle.Palette.Active;
            return s;
        },
    };

    private static readonly ButtonStyle RandomButtonActiveStyle = RandomButtonStyle with
    {
        Background = EditorStyle.Palette.Active,
        ContentColor = EditorStyle.Palette.Content,
    };

    private static bool RandomToggle(WidgetId id, bool dataIsRandom, ref float min, ref float max, out bool isExpanded)
    {
        isExpanded = RandomToggleButton(id, dataIsRandom, out var pressed);
        if (pressed)
        {
            if (isExpanded)
            {
                max = min; // collapse
                isExpanded = false;
            }
            else
            {
                max = min;
                isExpanded = true;
            }
            return true;
        }
        return false;
    }

    private static bool RandomToggleInt(WidgetId id, bool dataIsRandom, ref int min, ref int max, out bool isExpanded)
    {
        isExpanded = RandomToggleButton(id, dataIsRandom, out var pressed);
        if (pressed)
        {
            if (isExpanded)
            {
                max = min;
                isExpanded = false;
            }
            else
            {
                max = min;
                isExpanded = true;
            }
            return true;
        }
        return false;
    }

    private static bool ColorRandomToggle(WidgetId id, bool dataIsRandom, ref Color min, ref Color max, out bool isExpanded)
    {
        isExpanded = RandomToggleButton(id, dataIsRandom, out var pressed);
        if (pressed)
        {
            if (isExpanded)
            {
                max = min;
                isExpanded = false;
            }
            else
            {
                isExpanded = true;
            }
            return true;
        }
        return false;
    }

    private static bool RandomToggleButton(WidgetId id, bool dataIsRandom, out bool pressed)
    {
        ElementTree.BeginTree();
        ref var state = ref ElementTree.BeginWidget<RandomToggleState>(id);

        // Seed from data only on first render
        if (state.Initialized == 0)
        {
            state.Initialized = 1;
            state.Expanded = (byte)(dataIsRandom ? 1 : 0);
        }

        var isExpanded = state.Expanded != 0;
        var flags = ElementTree.GetWidgetFlags() | (isExpanded ? WidgetFlags.Checked : WidgetFlags.None);
        var style = EditorStyle.Button.ToggleIcon.Resolve!(EditorStyle.Button.ToggleIcon, flags);

        ElementTree.BeginSize(new Size2(style.Width, style.Height));
        ElementTree.BeginFill(style.Background, style.BorderRadius);
        ElementTree.BeginAlign(Align.Center);
        ElementTree.Image(EditorAssets.Sprites.IconRandomRange, style.IconSize, ImageStretch.Uniform, style.ContentColor);
        ElementTree.EndTree();

        pressed = flags.HasFlag(WidgetFlags.Pressed);
        if (pressed)
            state.Expanded = (byte)(isExpanded ? 0 : 1);

        return isExpanded;
    }

    private static bool FloatInput(WidgetId id, ref float value)
    {
        var text = VfxDocument.FormatFloat(value);
        bool changed = false;
        using (UI.BeginFlex())
        {
            var result = UI.TextInput(id, text, EditorStyle.Inspector.TextBox, "0");
            if (result != text && float.TryParse(result, out var parsed))
            {
                value = parsed;
                changed = true;
            }
        }
        return changed;
    }

    private static bool IntInput(WidgetId id, ref int value)
    {
        var text = value.ToString();
        bool changed = false;
        using (UI.BeginFlex())
        {
            var result = UI.TextInput(id, text, EditorStyle.Inspector.TextBox, "0");
            if (result != text && int.TryParse(result, out var parsed))
            {
                value = parsed;
                changed = true;
            }
        }
        return changed;
    }

    private static bool ColorInput(WidgetId id, ref Color color)
    {
        var color32 = color.ToColor32();
        if (EditorUI.ColorButton(id, ref color32, fillWidth: true))
        {
            color = color32.ToColor();
            return true;
        }
        return false;
    }

    // --- Curve Type Dropdown ---

    private static readonly (string Name, VfxCurveType Type)[] CurveTypeOptions =
        Enum.GetValues<VfxCurveType>()
            .Where(t => t != VfxCurveType.CubicBezier)
            .Select(t => (Enum.GetName(t)!, t))
            .ToArray();

    private static bool _curveChanged;
    private static VfxCurveType _curveNewType;
    private static bool _curveNewHasCurve;
    private static WidgetId _curveChangedId;

    private static bool CurveTypeDropdown(WidgetId id, ref VfxCurveType curveType, ref bool hasCurve)
    {
        // Check first: the popup handler fires AFTER this method during PopupMenu.UpdateUI()
        if (_curveChanged && _curveChangedId == id)
        {
            _curveChanged = false;
            curveType = _curveNewType;
            hasCurve = _curveNewHasCurve;
            return true;
        }

        var currentName = "None";
        if (hasCurve)
        {
            foreach (var opt in CurveTypeOptions)
                if (opt.Type == curveType) { currentName = opt.Name; break; }
        }

        UI.DropDown(id, () =>
        {
            var items = new PopupMenuItem[CurveTypeOptions.Length + 1];
            items[0] = PopupMenuItem.Item("None", () =>
            {
                _curveChanged = true;
                _curveChangedId = id;
                _curveNewHasCurve = false;
                _curveNewType = VfxCurveType.Linear;
            });
            for (var i = 0; i < CurveTypeOptions.Length; i++)
            {
                var opt = CurveTypeOptions[i];
                items[i + 1] = PopupMenuItem.Item(opt.Name, () =>
                {
                    _curveChanged = true;
                    _curveChangedId = id;
                    _curveNewHasCurve = true;
                    _curveNewType = opt.Type;
                });
            }
            return items;
        }, text: currentName);

        return false;
    }

}
