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
        public static partial WidgetId ParticleAlignToDirection { get; }

        // Addable section buttons
        public static partial WidgetId AddSize { get; }
        public static partial WidgetId AddSpeed { get; }
        public static partial WidgetId AddColor { get; }
        public static partial WidgetId AddOpacity { get; }
        public static partial WidgetId AddGravity { get; }
        public static partial WidgetId AddDrag { get; }
        public static partial WidgetId AddRotation { get; }
        public static partial WidgetId AddRotationSpeed { get; }
        public static partial WidgetId RemoveSize { get; }
        public static partial WidgetId RemoveSpeed { get; }
        public static partial WidgetId RemoveColor { get; }
        public static partial WidgetId RemoveOpacity { get; }
        public static partial WidgetId RemoveGravity { get; }
        public static partial WidgetId RemoveDrag { get; }
        public static partial WidgetId RemoveRotation { get; }
        public static partial WidgetId RemoveRotationSpeed { get; }
        public static partial WidgetId SpriteDropDown { get; }
        public static partial WidgetId ParticleSort { get; }

        // Addable section persistent state
        public static partial WidgetId SectionSize { get; }
        public static partial WidgetId SectionSpeed { get; }
        public static partial WidgetId SectionColor { get; }
        public static partial WidgetId SectionOpacity { get; }
        public static partial WidgetId SectionGravity { get; }
        public static partial WidgetId SectionDrag { get; }
        public static partial WidgetId SectionRotation { get; }
        public static partial WidgetId SectionRotationSpeed { get; }
    }

    private struct AddableSectionState
    {
        public bool Initialized;
        public bool IsActive;
    }

    private struct CurveFieldState
    {
        public bool Initialized;
        public bool HasCurve;
        public bool HasRandomStart;
        public bool HasRandomEnd;
    }

    private static ref CurveFieldState BeginFieldState(WidgetId id, bool dataCurve, bool dataRandomStart, bool dataRandomEnd = false)
    {
        ElementTree.BeginTree();
        ref var state = ref ElementTree.BeginWidget<CurveFieldState>(id, interactive: false);
        if (!state.Initialized)
        {
            state.Initialized = true;
            state.HasCurve = dataCurve;
            state.HasRandomStart = dataRandomStart;
            state.HasRandomEnd = dataRandomEnd;
        }
        ElementTree.EndTree();
        return ref state;
    }

    public override void InspectorUI()
    {
        if (Document.HasMultiSelection)
        {
            UI.Text("Multiple items selected", EditorStyle.Text.Secondary);
            return;
        }

        var selectedType = Document.SingleSelectedType;
        var selectedIndex = Document.SingleSelectedIndex;

        switch (selectedType)
        {
            case VfxSelectionType.Vfx:
                VfxInspectorUI();
                break;

            case VfxSelectionType.Emitter:
                if (selectedIndex >= 0 && selectedIndex < Document.Emitters.Count)
                    EmitterInspectorUI(Document.Emitters[selectedIndex]);
                break;

            case VfxSelectionType.Particle:
                if (selectedIndex >= 0 && selectedIndex < Document.Particles.Count)
                    ParticleInspectorUI(Document.Particles[selectedIndex]);
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
                Document.Duration = duration;
                Document.ApplyChanges();
            }

            var loop = Document.Loop;
            using (Inspector.BeginProperty("Loop"))
            {
                if (UI.Toggle(FieldId.VfxLoop, loop, EditorStyle.Inspector.Toggle))
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
            var rate = emitter.Def.Rate;
            var burst = emitter.Def.Burst;
            var duration = emitter.Def.Duration;
            var worldSpace = emitter.Def.WorldSpace;

            if (FloatCurveField(FieldId.EmitterRate, "Rate", ref rate)) changed = true;
            if (IntRangeField(FieldId.EmitterBurst, "Burst", ref burst)) changed = true;
            if (RangeField(FieldId.EmitterDuration, "Duration", ref duration)) changed = true;

            using (Inspector.BeginProperty("WorldSpace"))
            {
                if (UI.Toggle(FieldId.EmitterWorldSpace, worldSpace, EditorStyle.Inspector.Toggle))
                {
                    worldSpace = !worldSpace;
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
                emitter.Def.Rate = rate;
                emitter.Def.Burst = burst;
                emitter.Def.Duration = duration;
                emitter.Def.WorldSpace = worldSpace;
                Document.ApplyChanges();
            }
        }

        using (Inspector.BeginSection("SPAWN"))
        {
            if (!Inspector.IsSectionCollapsed)
                SpawnDefFields(emitter);
        }

        using (Inspector.BeginSection("DIRECTION"))
        {
            if (!Inspector.IsSectionCollapsed)
            {
                var direction = emitter.Def.Direction;
                var spread = emitter.Def.Spread;
                var radial = emitter.Def.Radial;

                using (Inspector.BeginProperty("Direction"))
                    direction = FloatInput(FieldId.EmitterDirection, direction);
                using (Inspector.BeginProperty("Spread"))
                    spread = FloatInput(FieldId.EmitterSpread, spread);
                using (Inspector.BeginProperty("Radial"))
                    radial = FloatInput(FieldId.EmitterRadial, radial);
                if (direction != emitter.Def.Direction || spread != emitter.Def.Spread || radial != emitter.Def.Radial)
                {
                    emitter.Def.Direction = direction;
                    emitter.Def.Spread = spread;
                    emitter.Def.Radial = radial;
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

    private void SpawnDefFields(VfxDocEmitter emitter)
    {
        var spawn = emitter.Def.Spawn;
        var changed = false;

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
            spawn.Offset.X = FloatInput(FieldId.EmitterSpawnOffset, spawn.Offset.X);
            spawn.Offset.Y = FloatInput(FieldId.EmitterSpawnOffset + 2, spawn.Offset.Y);
        }

        // Shape-specific fields
        switch (spawn.Shape)
        {
            case VfxSpawnShape.Circle:
                using (Inspector.BeginProperty("Radius"))
                    spawn.Circle.Radius = FloatInput(FieldId.EmitterSpawnRadius, spawn.Circle.Radius);
                using (Inspector.BeginProperty("Inner Radius"))
                    spawn.Circle.InnerRadius = FloatInput(FieldId.EmitterSpawnInnerRadius, spawn.Circle.InnerRadius);
                break;

            case VfxSpawnShape.Box:
                using (Inspector.BeginProperty("Size"))
                using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
                {
                    spawn.Box.Size.X = FloatInput(FieldId.EmitterSpawnSize, spawn.Box.Size.X);
                    spawn.Box.Size.Y = FloatInput(FieldId.EmitterSpawnSize + 2, spawn.Box.Size.Y);
                }
                using (Inspector.BeginProperty("Inner Size"))
                using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
                {
                    spawn.Box.InnerSize.X = FloatInput(FieldId.EmitterSpawnInnerSize, spawn.Box.InnerSize.X);
                    spawn.Box.InnerSize.Y = FloatInput(FieldId.EmitterSpawnInnerSize + 2, spawn.Box.InnerSize.Y);
                }
                using (Inspector.BeginProperty("Rotation"))
                    spawn.Box.Rotation = FloatInput(FieldId.EmitterSpawnRotation, spawn.Box.Rotation);
                break;
        }

        var spawnChanged = spawn.Offset != emitter.Def.Spawn.Offset
            || spawn.Shape != emitter.Def.Spawn.Shape
            || spawn.Circle.Radius != emitter.Def.Spawn.Circle.Radius
            || spawn.Circle.InnerRadius != emitter.Def.Spawn.Circle.InnerRadius
            || spawn.Box.Size != emitter.Def.Spawn.Box.Size
            || spawn.Box.InnerSize != emitter.Def.Spawn.Box.InnerSize
            || spawn.Box.Rotation != emitter.Def.Spawn.Box.Rotation;
        if (changed || spawnChanged)
        {
            emitter.Def.Spawn = spawn;
            Document.ApplyChanges();
        }
    }

    // --- Particle Inspector ---

    private void ParticleInspectorUI(VfxDocParticle particle)
    {
        using (Inspector.BeginSection("PARTICLE"))
        {
            if (!Inspector.IsSectionCollapsed)
            {
                var duration = particle.Def.Duration;
                if (RangeField(FieldId.ParticleDuration, "Duration", ref duration))
                {
                    particle.Def.Duration = duration;
                    Document.ApplyChanges();
                }

                using (Inspector.BeginProperty("Sprite"))
                {
                    var newRef = EditorUI.SpriteField(FieldId.SpriteDropDown, particle.SpriteRef);
                    if (newRef.Value != particle.SpriteRef.Value)
                    {
                        Undo.Record(Document);
                        particle.SpriteRef = newRef;
                        Document.ApplyChanges();
                    }
                }

                using (Inspector.BeginProperty("Sort"))
                {
                    var sortVal = (int)particle.Def.Sort;
                    if (IntInput(FieldId.ParticleSort, ref sortVal))
                    {
                        Undo.Record(Document);
                        particle.Def.Sort = (ushort)Math.Clamp(sortVal, 0, ushort.MaxValue);
                        Document.ApplyChanges();
                    }
                }
            }
        }

        // Addable particle groups
        if (AddableSection("SIZE", particle.Def.Size != VfxFloatCurve.One, FieldId.SectionSize, FieldId.AddSize, FieldId.RemoveSize,
            () => { particle.Def.Size = new VfxFloatCurve { Type = VfxCurveType.EaseOut, Start = new VfxRange(0.5f, 0.5f), End = new VfxRange(0f, 0.1f) }; },
            () => { particle.Def.Size = VfxFloatCurve.One; }))
        {
            var size = particle.Def.Size;
            if (FloatCurveField(FieldId.ParticleSize, "Size", ref size))
            {
                particle.Def.Size = size;
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("SPEED", particle.Def.Speed != VfxFloatCurve.Zero, FieldId.SectionSpeed, FieldId.AddSpeed, FieldId.RemoveSpeed,
            () => { particle.Def.Speed = new VfxFloatCurve { Type = VfxCurveType.Linear, Start = new VfxRange(10, 20), End = new VfxRange(0, 5) }; },
            () => { particle.Def.Speed = VfxFloatCurve.Zero; }))
        {
            var speed = particle.Def.Speed;
            if (FloatCurveField(FieldId.ParticleSpeed, "Speed", ref speed))
            {
                particle.Def.Speed = speed;
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("COLOR", particle.Def.Color != VfxColorCurve.White, FieldId.SectionColor, FieldId.AddColor, FieldId.RemoveColor,
            () => { particle.Def.Color = new VfxColorCurve { Type = VfxCurveType.Linear, Start = new VfxColorRange(Color.White, Color.White), End = new VfxColorRange(Color.Yellow, Color.Yellow) }; },
            () => { particle.Def.Color = VfxColorCurve.White; }))
        {
            var color = particle.Def.Color;
            if (ColorCurveField(FieldId.ParticleColor, "Color", ref color))
            {
                Undo.Record(Document);
                particle.Def.Color = color;
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("OPACITY", particle.Def.Opacity != VfxFloatCurve.One, FieldId.SectionOpacity, FieldId.AddOpacity, FieldId.RemoveOpacity,
            () => { particle.Def.Opacity = new VfxFloatCurve { Type = VfxCurveType.EaseOut, Start = VfxRange.One, End = VfxRange.Zero }; },
            () => { particle.Def.Opacity = VfxFloatCurve.One; }))
        {
            var opacity = particle.Def.Opacity;
            if (FloatCurveField(FieldId.ParticleOpacity, "Opacity", ref opacity))
            {
                particle.Def.Opacity = opacity;
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("GRAVITY", particle.Def.Gravity != VfxVec2Range.Zero, FieldId.SectionGravity, FieldId.AddGravity, FieldId.RemoveGravity,
            () => { particle.Def.Gravity = new VfxVec2Range(new(0, 10), new(0, 10)); },
            () => { particle.Def.Gravity = VfxVec2Range.Zero; }))
        {
            var gravity = particle.Def.Gravity;
            if (Vec2RangeField(FieldId.ParticleGravity, "Gravity", ref gravity))
            {
                particle.Def.Gravity = gravity;
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("DRAG", particle.Def.Drag != VfxRange.Zero, FieldId.SectionDrag, FieldId.AddDrag, FieldId.RemoveDrag,
            () => { particle.Def.Drag = new VfxRange(1, 1); },
            () => { particle.Def.Drag = VfxRange.Zero; }))
        {
            var drag = particle.Def.Drag;
            if (RangeField(FieldId.ParticleDrag, "Drag", ref drag))
            {
                particle.Def.Drag = drag;
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("ROTATION", particle.Def.Rotation != VfxRange.Zero, FieldId.SectionRotation, FieldId.AddRotation, FieldId.RemoveRotation,
            () => { particle.Def.Rotation = new VfxRange(0, 360); },
            () => { particle.Def.Rotation = VfxRange.Zero; }))
        {
            var rotation = particle.Def.Rotation;
            if (RangeField(FieldId.ParticleRotation, "Rotation", ref rotation))
            {
                particle.Def.Rotation = rotation;
                Document.ApplyChanges();
            }

            using (Inspector.BeginProperty("Align to Direction"))
            {
                if (UI.Toggle(FieldId.ParticleAlignToDirection, particle.Def.AlignToDirection, EditorStyle.Inspector.Toggle))
                {
                    particle.Def.AlignToDirection = !particle.Def.AlignToDirection;
                    Document.ApplyChanges();
                }
            }
            EndAddableSection();
        }

        if (AddableSection("ROTATION SPEED", particle.Def.RotationSpeed != VfxFloatCurve.Zero, FieldId.SectionRotationSpeed, FieldId.AddRotationSpeed, FieldId.RemoveRotationSpeed,
            () => { particle.Def.RotationSpeed = new VfxFloatCurve { Type = VfxCurveType.Linear, Start = new VfxRange(-180, 180), End = new VfxRange(-180, 180) }; },
            () => { particle.Def.RotationSpeed = VfxFloatCurve.Zero; }))
        {
            var rotSpeed = particle.Def.RotationSpeed;
            if (FloatCurveField(FieldId.ParticleRotationSpeed, "Speed", ref rotSpeed))
            {
                particle.Def.RotationSpeed = rotSpeed;
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

    }

    // --- Addable Section Helper ---

    private Inspector.AutoSection _addableSectionHandle;

    private static bool _sectionToggled;
    private static WidgetId _sectionToggledId;
    private static bool _sectionToggledValue;

    private bool AddableSection(string name, bool dataIsActive, WidgetId stateId, WidgetId addId, WidgetId removeId, Action onAdd, Action onRemove)
    {
        // Persistent state: initialized from data once, then only changed by explicit button clicks
        ElementTree.BeginTree();
        ref var state = ref ElementTree.BeginWidget<AddableSectionState>(stateId, interactive: false);
        if (!state.Initialized)
        {
            state.Initialized = true;
            state.IsActive = dataIsActive;
        }
        // Apply deferred toggle from previous frame's button press
        if (_sectionToggled && _sectionToggledId == stateId)
        {
            _sectionToggled = false;
            state.IsActive = _sectionToggledValue;
        }
        ElementTree.EndTree();

        if (!state.IsActive)
        {
            using (Inspector.BeginSection(name, content: () =>
            {
                ElementTree.BeginAlign(Align.Min, Align.Center);
                if (UI.Button(addId, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
                {
                    Undo.Record(Document);
                    onAdd();
                    _sectionToggled = true;
                    _sectionToggledId = stateId;
                    _sectionToggledValue = true;
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
                _sectionToggled = true;
                _sectionToggledId = stateId;
                _sectionToggledValue = false;
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
    // baseId+0,+1: min float (scrub+text), +2: randomToggle, +3,+4: max float (scrub+text), +5: state
    private bool RangeField(WidgetId baseId, string label, ref VfxRange value)
    {
        var changed = false;
        ref var state = ref BeginFieldState(baseId + 5,
            dataCurve: false,
            dataRandomStart: value.Min != value.Max);
        using (Inspector.BeginProperty(label))
        using (UI.BeginRow(ValueRowStyle))
        {
            var min = FloatInput(baseId, value.Min);
            if (min != value.Min) { value.Min = min; changed = true; if (!state.HasRandomStart) value.Max = min; }
            if (RandomToggleButton(baseId + 2, ref state.HasRandomStart, out var pressed)) changed = true;
            if (pressed && !state.HasRandomStart) value.Max = value.Min;
            if (state.HasRandomStart)
            {
                var max = FloatInput(baseId + 3, value.Max);
                if (max != value.Max) { value.Max = max; changed = true; }
            }
            else
                using (UI.BeginFlex()) { } // reserve space
        }
        return changed;
    }

    // Int range field: [label] [min] [⇄] [max]  — no curve row
    // baseId+0: min, +1: randomToggle, +2: max, +3: state
    private static bool IntRangeField(WidgetId baseId, string label, ref VfxIntRange value)
    {
        var changed = false;
        ref var state = ref BeginFieldState(baseId + 3,
            dataCurve: false,
            dataRandomStart: value.Min != value.Max);
        using (Inspector.BeginProperty(label))
        using (UI.BeginRow(ValueRowStyle))
        {
            if (IntInput(baseId, ref value.Min)) { changed = true; if (!state.HasRandomStart) value.Max = value.Min; }
            if (RandomToggleButton(baseId + 1, ref state.HasRandomStart, out var pressed)) changed = true;
            if (pressed && !state.HasRandomStart) value.Max = value.Min;
            if (state.HasRandomStart)
            {
                if (IntInput(baseId + 2, ref value.Max)) changed = true;
            }
            else
                using (UI.BeginFlex()) { }
        }
        return changed;
    }

    // Vec2 range field: two rows of min/max (no random toggle — too complex)
    // baseId+0,+1: minX, +2,+3: minY, +4,+5: maxX, +6,+7: maxY
    private bool Vec2RangeField(WidgetId baseId, string label, ref VfxVec2Range value)
    {
        var changed = false;
        using (Inspector.BeginProperty(label))
        using (UI.BeginColumn(new ContainerStyle { Spacing = 2 }))
        {
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                var mx = FloatInput(baseId, value.Min.X);
                var my = FloatInput(baseId + 2, value.Min.Y);
                if (mx != value.Min.X) { value.Min.X = mx; changed = true; }
                if (my != value.Min.Y) { value.Min.Y = my; changed = true; }
            }
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                var mx = FloatInput(baseId + 4, value.Max.X);
                var my = FloatInput(baseId + 6, value.Max.Y);
                if (mx != value.Max.X) { value.Max.X = mx; changed = true; }
                if (my != value.Max.Y) { value.Max.Y = my; changed = true; }
            }
        }
        return changed;
    }

    // Float curve field with progressive disclosure
    // baseId+0: startMin, +1: startRandom, +2: startMax, +3: endMin, +4: endRandom, +5: endMax, +6: curveType, +7: state
    // baseId+0,+1: startMin float, +2: startRandom, +3,+4: startMax float,
    // +5,+6: endMin float, +7: endRandom, +8,+9: endMax float, +10: curveType, +11: state
    private bool FloatCurveField(WidgetId baseId, string label, ref VfxFloatCurve curve)
    {
        var changed = false;
        ref var state = ref BeginFieldState(baseId + 11,
            dataCurve: curve.Start != curve.End,
            dataRandomStart: curve.Start.Min != curve.Start.Max,
            dataRandomEnd: curve.End.Min != curve.End.Max);
        var startLabel = state.HasCurve ? "Start" : "Value";

        // Start/Value row
        using (Inspector.BeginProperty(startLabel))
        using (UI.BeginRow(ValueRowStyle))
        {
            var smin = FloatInput(baseId, curve.Start.Min);
            if (smin != curve.Start.Min) { curve.Start.Min = smin; changed = true; if (!state.HasRandomStart) curve.Start.Max = smin; }
            if (RandomToggleButton(baseId + 2, ref state.HasRandomStart, out var startPressed)) changed = true;
            if (startPressed && !state.HasRandomStart) curve.Start.Max = curve.Start.Min;
            if (state.HasRandomStart)
            {
                var smax = FloatInput(baseId + 3, curve.Start.Max);
                if (smax != curve.Start.Max) { curve.Start.Max = smax; changed = true; }
            }
            else
                using (UI.BeginFlex()) { }
        }

        // End row (only when curve active)
        if (state.HasCurve)
        {
            using (Inspector.BeginProperty("End"))
            using (UI.BeginRow(ValueRowStyle))
            {
                var emin = FloatInput(baseId + 5, curve.End.Min);
                if (emin != curve.End.Min) { curve.End.Min = emin; changed = true; if (!state.HasRandomEnd) curve.End.Max = emin; }
                if (RandomToggleButton(baseId + 7, ref state.HasRandomEnd, out var endPressed)) changed = true;
                if (endPressed && !state.HasRandomEnd) curve.End.Max = curve.End.Min;
                if (state.HasRandomEnd)
                {
                    var emax = FloatInput(baseId + 8, curve.End.Max);
                    if (emax != curve.End.Max) { curve.End.Max = emax; changed = true; }
                }
                else
                    using (UI.BeginFlex()) { }
            }
        }
        else if (changed)
        {
            curve.End = curve.Start;
        }

        // Curve type row (always visible)
        if (CurveRow(baseId + 10, ref state.HasCurve, ref curve.Type, ref curve.Start, ref curve.End))
            changed = true;

        return changed;
    }

    // Color curve field with progressive disclosure
    // baseId+0: startMin, +1: startRandom, +2: startMax, +3: endMin, +4: endRandom, +5: endMax, +6: curveType, +7: state
    private static bool ColorCurveField(WidgetId baseId, string label, ref VfxColorCurve curve)
    {
        var changed = false;
        ref var state = ref BeginFieldState(baseId + 7,
            dataCurve: curve.Start != curve.End,
            dataRandomStart: curve.Start.Min != curve.Start.Max,
            dataRandomEnd: curve.End.Min != curve.End.Max);
        var startLabel = state.HasCurve ? "Start" : "Value";

        // Start/Value row
        using (Inspector.BeginProperty(startLabel))
        using (UI.BeginRow(ValueRowStyle))
        {
            using (UI.BeginFlex())
                if (ColorInput(baseId, ref curve.Start.Min)) { changed = true; if (!state.HasRandomStart) curve.Start.Max = curve.Start.Min; }
            if (RandomToggleButton(baseId + 1, ref state.HasRandomStart, out var startPressed)) changed = true;
            if (startPressed && !state.HasRandomStart) curve.Start.Max = curve.Start.Min;
            if (state.HasRandomStart)
            {
                using (UI.BeginFlex())
                    if (ColorInput(baseId + 2, ref curve.Start.Max)) changed = true;
            }
            else
                using (UI.BeginFlex()) { }
        }

        // End row
        if (state.HasCurve)
        {
            using (Inspector.BeginProperty("End"))
            using (UI.BeginRow(ValueRowStyle))
            {
                using (UI.BeginFlex())
                    if (ColorInput(baseId + 3, ref curve.End.Min)) { changed = true; if (!state.HasRandomEnd) curve.End.Max = curve.End.Min; }
                if (RandomToggleButton(baseId + 4, ref state.HasRandomEnd, out var endPressed)) changed = true;
                if (endPressed && !state.HasRandomEnd) curve.End.Max = curve.End.Min;
                if (state.HasRandomEnd)
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
        if (ColorCurveRow(baseId + 6, ref state.HasCurve, ref curve.Type, ref curve.Start, ref curve.End))
            changed = true;

        return changed;
    }

    // --- Curve Row ---

    // Curve type dropdown row for float curves. Returns true if changed.
    // When "None" selected: copies start to end. When type selected from None: keeps end as-is (user will edit).
    private static bool CurveRow(WidgetId id, ref bool hasCurve, ref VfxCurveType type, ref VfxRange start, ref VfxRange end)
    {
        using (Inspector.BeginProperty(""))
        using (UI.BeginRow(CurveRowStyle))
        {
            if (CurveTypeDropdown(id, ref type, ref hasCurve, out var newType, out var newHasCurve))
            {
                type = newType;
                hasCurve = newHasCurve;
                if (!hasCurve)
                    end = start; // "None" selected — collapse curve
                return true;
            }
        }
        return false;
    }

    private static bool ColorCurveRow(WidgetId id, ref bool hasCurve, ref VfxCurveType type, ref VfxColorRange start, ref VfxColorRange end)
    {
        using (Inspector.BeginProperty(""))
        using (UI.BeginRow(CurveRowStyle))
        {
            if (CurveTypeDropdown(id, ref type, ref hasCurve, out var newType, out var newHasCurve))
            {
                type = newType;
                hasCurve = newHasCurve;
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

    // Random toggle button that reads/writes expanded state from parent CurveFieldState.
    // Returns true if pressed (state was toggled).
    private static bool RandomToggleButton(WidgetId id, ref bool isExpanded, out bool pressed)
    {
        var flags = WidgetFlags.None | (isExpanded ? WidgetFlags.Checked : WidgetFlags.None);

        ElementTree.BeginTree();
        ElementTree.BeginWidget(id);
        ElementTree.SetWidgetFlag(id, WidgetFlags.Checked, isExpanded);
        flags |= ElementTree.GetWidgetFlags();
        var style = EditorStyle.Button.ToggleIcon.Resolve!(EditorStyle.Button.ToggleIcon, flags);

        ElementTree.BeginSize(new Size2(style.Width, style.Height));
        ElementTree.BeginFill(style.Background, style.BorderRadius);
        ElementTree.BeginAlign(Align.Center);
        ElementTree.Image(EditorAssets.Sprites.IconRandomRange, style.IconSize, ImageStretch.Uniform, style.ContentColor);
        ElementTree.EndTree();

        pressed = flags.HasFlag(WidgetFlags.Pressed);
        if (pressed)
            isExpanded = !isExpanded;

        return pressed;
    }

    private float FloatInput(WidgetId id, float value)
    {
        float result;
        using (UI.BeginFlex())
            result = EditorUI.FloatInput(id, value, EditorStyle.Inspector.TextBox, step: 0.1f, fineStep: 0.01f);
        UI.HandleChange(Document);
        return result;
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
        color = EditorUI.ColorButton(id, color, fillWidth: true, hdr: true);
        return UI.WasChanged();
    }

    private static bool ApproximatelyEqual(VfxColorRange a, VfxColorRange b)
    {
        return ApproximatelyEqual(a.Min, b.Min) && ApproximatelyEqual(a.Max, b.Max);
    }

    private static bool ApproximatelyEqual(Color a, Color b)
    {
        return MathEx.Approximately(a.R, b.R) &&
               MathEx.Approximately(a.G, b.G) &&
               MathEx.Approximately(a.B, b.B) &&
               MathEx.Approximately(a.A, b.A);
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

    private static bool CurveTypeDropdown(WidgetId id, ref VfxCurveType curveType, ref bool hasCurve, out VfxCurveType newType, out bool newHasCurve)
    {
        // Check first: the popup handler fires AFTER this method during PopupMenu.UpdateUI()
        if (_curveChanged && _curveChangedId == id)
        {
            _curveChanged = false;
            newType = _curveNewType;
            newHasCurve = _curveNewHasCurve;
            return true;
        }

        newType = curveType;
        newHasCurve = hasCurve;

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
