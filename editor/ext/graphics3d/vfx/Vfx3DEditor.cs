using System.Numerics;

namespace NoZ.Editor.Graphics3D;

internal sealed partial class Vfx3DEditor : DocumentEditor
{
    private static partial class Id
    {
        public static partial WidgetId Fields { get; }
        public static partial WidgetId Emitters { get; }
        public static partial WidgetId Add { get; }
        public static partial WidgetId Remove { get; }
        public static partial WidgetId Play { get; }
        public static partial WidgetId Repeat { get; }
    }
    private readonly Camera3D _camera = new();
    private readonly VfxSystem3D _system = new(seed: 123);
    private readonly Vfx3D _effect = new();
    private readonly Dictionary<string, Texture?> _textures = [];
    private VfxHandle3D _handle;
    private Shader? _shader;
    private bool _assetsDirty = true, _playing = true, _repeat = true, _orbiting;
    private float _yaw = MathF.PI / 4, _pitch = .45f;
    private Vector2 _mouse;
    private int _revision = -1, _selected;
    private bool _changed;
    private int _field;
    public new Vfx3DDocument Document => (Vfx3DDocument)base.Document;
    public override bool ShowInspector => true;
    public override bool ShowOutliner => true;
    public override bool RequiresDepth => true;
    public override bool RunInBackground => _playing;
    public override PowerMode PowerMode => _playing ? PowerMode.Performance : PowerMode.Balanced;

    public Vfx3DEditor(Vfx3DDocument document) : base(document)
    {
        _system.TextureResolver = ResolveTexture;
        Project.OnExported += OnExported;
        Commands = [new Command("Play / Pause", () => _playing = !_playing, [InputCode.KeySpace]),
            new Command("Frame Effect", Frame, [InputCode.KeyF]),
            new Command("Exit Edit Mode", Workspace.EndEdit, [InputCode.KeyTab])];
    }

    public override void PreUpdate()
    {
        var mouse = Input.MousePosition;
        if (UI.IsHovered(Workspace.SceneWidgetId) && Input.WasButtonPressed(InputCode.MouseLeft))
        { _orbiting = true; _mouse = mouse; }
        if (!_orbiting) return;
        if (!Input.IsButtonDownRaw(InputCode.MouseLeft)) { _orbiting = false; return; }
        var delta = mouse - _mouse; _mouse = mouse;
        _yaw -= delta.X * .01f; _pitch = Math.Clamp(_pitch + delta.Y * .01f, -1.45f, 1.45f);
    }

    public override void Update()
    {
        if (_assetsDirty)
        {
            ReleaseAssets(); _assetsDirty = false;
            if (File.Exists(System.IO.Path.Combine(Project.OutputPath, "shader", "vfx3d")))
                _shader = Asset.Load(AssetType.Shader, "vfx3d", useRegistry: false, libraryPath: Project.OutputPath) as Shader;
        }
        if (_revision != Document.PreviewRevision) Restart();
        if (_playing)
        {
            _system.Update(Time.DeltaTime);
            if (_repeat && !_system.IsPlaying(_handle)) _handle = _system.Play(_effect, Vector3.Zero);
        }
        if (_shader == null) return;
        var radius = Document.Source.PreviewRadius;
        _camera.Position = new Vector3(MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch), MathF.Cos(_pitch) * MathF.Cos(_yaw)) * radius * 2.75f;
        _camera.Target = Vector3.Zero; _camera.NearClip = Math.Max(.001f, radius * .01f); _camera.FarClip = radius * 10;
        var projection = MeshWorkspaceProjection.Create(_camera, Workspace.Camera.ViewMatrix, Document.Bounds.Translate(Document.Position));
        using var state = Graphics.PushState();
        Graphics.SetLayer(EditorLayer.PixelGrid + 1);
        _system.Draw(_camera, projection, _shader);
        Graphics.SetCamera(Workspace.Camera);
    }

    private void Restart()
    {
        _system.Clear(); _effect.Emitters = Document.Source.Bake(); _effect.Loop = Document.Source.Loop;
        _handle = _system.Play(_effect, Vector3.Zero); _revision = Document.PreviewRevision;
    }
    private void Frame()
    {
        _yaw = MathF.PI / 4; _pitch = .45f;
        Workspace.FrameRect(Document.Bounds.Translate(Document.Position));
    }
    public override void UpdateOverlayUI()
    {
        using var toolbar = FloatingToolbar.Begin();
        if (FloatingToolbar.Button(Id.Play, EditorAssets.Sprites.IconPlay, isSelected: _playing))
        {
            _playing = !_playing;
            if (_playing && !_system.IsPlaying(_handle)) Restart();
        }
        if (FloatingToolbar.Button(Id.Repeat, EditorAssets.Sprites.IconLoop, isSelected: _repeat)) _repeat = !_repeat;
    }
    public override void OutlinerUI()
    {
        for (var i = 0; i < Document.Source.Emitters.Count; i++)
            if (UI.Button(Id.Emitters + i, (_selected == i ? "• " : "") + Document.Source.Emitters[i].Name,
                EditorStyle.Button.Secondary)) _selected = i;
        if (Document.Source.Emitters.Count < Vfx3D.MaxEmitters && UI.Button(Id.Add, "Add emitter", EditorStyle.Button.Secondary))
        {
            Undo.Record(Document); Document.Source.Emitters.Add(new() { Name = $"Emitter {Document.Source.Emitters.Count + 1}" });
            _selected = Document.Source.Emitters.Count - 1; Document.ApplyChanges();
        }
        if (Document.Source.Emitters.Count > 0 && UI.Button(Id.Remove, "Remove emitter", EditorStyle.Button.Secondary))
        {
            Undo.Record(Document); Document.Source.Emitters.RemoveAt(Math.Clamp(_selected, 0, Document.Source.Emitters.Count - 1));
            _selected = Math.Max(0, _selected - 1); Document.ApplyChanges();
        }
    }

    public override void InspectorUI()
    {
        _field = 0; _changed = false;
        using (EditorInspector.BeginSection("EFFECT"))
        {
            Toggle("Loop effect", ref Document.Source.Loop);
            Number("Preview radius", ref Document.Source.PreviewRadius, .01f);
            UI.Text("Drag to orbit · Scroll to zoom · F to frame", EditorStyle.Text.Secondary);
            if (_shader == null) UI.Text("Import the graphics3d shader assets to preview this effect.", EditorStyle.Text.Secondary);
        }
        if (Document.Source.Emitters.Count == 0) { if (_changed) Document.ApplyChanges(); return; }
        _field = 10;
        _selected = Math.Clamp(_selected, 0, Document.Source.Emitters.Count - 1);
        var e = Document.Source.Emitters[_selected];
        using (EditorInspector.BeginSection("EMITTER"))
        {
            using (EditorInspector.BeginProperty("Name")) Set(ref e.Name, EditorInspector.TextField(Next(), e.Name, Document));
            Range("Duration", ref e.Duration, .001f);
            var burst = new VfxRange(e.Burst.Min, e.Burst.Max);
            Range("Burst min / max", ref burst, 0, 1_000_000);
            e.Burst = new((int)burst.Min, (int)burst.Max);
            Toggle("World space", ref e.WorldSpace);
        }
        _field = 20;
        Curve("EMISSION RATE", ref e.Rate, 0);
        _field = 30;
        using (EditorInspector.BeginSection("SPAWN"))
        {
            Choice("Shape", e.Spawn.Shape, value => e.Spawn.Shape = value);
            Vector("Offset", ref e.Spawn.Offset);
            if (e.Spawn.Shape == VfxSpawnShape3D.Sphere)
            {
                Number("Radius", ref e.Spawn.Radius, 0);
                Number("Inner radius", ref e.Spawn.InnerRadius, 0, e.Spawn.Radius);
            }
            else if (e.Spawn.Shape == VfxSpawnShape3D.Box) Vector("Size", ref e.Spawn.Size, 0);
            _field = 36;
            Vector("Direction", ref e.Direction);
            Number("Cone half-angle", ref e.Spread, 0, 180);
        }
        _field = 40;
        using (EditorInspector.BeginSection("PARTICLE"))
        {
            Range("Lifetime", ref e.Lifetime, .001f);
            Range("Rotation (degrees)", ref e.Rotation);
            Choice("Blend", e.Blend, value => e.Blend = value);
            using (EditorInspector.BeginProperty("Texture asset"))
                Set(ref e.Texture, EditorInspector.TextField(Next(), e.Texture, Document));
            UI.Text("Empty texture draws a white quad.", EditorStyle.Text.Secondary);
            var columns = (float)e.Columns; var rows = (float)e.Rows;
            Number("Frame columns", ref columns, 1, 256); Number("Frame rows", ref rows, 1, 256);
            e.Columns = (int)columns; e.Rows = (int)rows;
            Choice("Frames", e.FrameMode, value => e.FrameMode = value);
        }
        _field = 50;
        Curve("SIZE", ref e.Size, 0); Curve("SPEED", ref e.Speed); Curve("GRAVITY", ref e.Gravity);
        Curve("OPACITY", ref e.Opacity, 0); Curve("ROTATION SPEED", ref e.RotationSpeed);
        using (EditorInspector.BeginSection("COLOR"))
            if (!EditorInspector.IsSectionCollapsed) Set(ref e.Color, EditorInspector.VfxColorCurveField(Next(), e.Color, Document));
        if (_changed) Document.ApplyChanges();
        if (UI.WasChangeCancelled()) Undo.Cancel();
    }
    private WidgetId Next() => Id.Fields + _field++ * 32;
    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; _changed = true;
    }
    private void Number(string label, ref float value, float min = float.MinValue, float max = float.MaxValue)
    {
        using var property = EditorInspector.BeginProperty(label);
        Set(ref value, EditorInspector.FloatField(Next(), value, Document, .1f, .01f, min, max));
    }
    private void Vector(string label, ref Vector3 value, float min = float.MinValue)
    {
        using var property = EditorInspector.BeginProperty(label);
        Set(ref value, EditorInspector.Vector3Field(Next(), value, Document, min: min));
    }
    private void Range(string label, ref VfxRange value, float min = float.MinValue, float max = float.MaxValue)
    {
        using var property = EditorInspector.BeginProperty(label);
        var next = EditorInspector.VfxRangeField(Next(), value, Document, min);
        next.Min = Math.Min(next.Min, max); next.Max = Math.Min(next.Max, max); Set(ref value, next);
    }
    private void Toggle(string label, ref bool value)
    {
        using var property = EditorInspector.BeginProperty(label);
        var next = UI.Toggle(Next(), value, EditorStyle.Inspector.Toggle);
        if (next == value) return;
        Undo.Record(Document); Set(ref value, next);
    }
    private void Curve(string label, ref VfxDocFloatCurve curve, float min = float.MinValue)
    {
        var id = Next();
        using var section = EditorInspector.BeginSection(label);
        if (!EditorInspector.IsSectionCollapsed) Set(ref curve, EditorInspector.VfxCurveField(id, curve, Document, min));
    }
    private void Choice<T>(string label, T value, Action<T> set) where T : struct, Enum
    {
        using var property = EditorInspector.BeginProperty(label);
        UI.DropDown(Next(), () => Enum.GetValues<T>().Select(option => new PopupMenuItem
        {
            Label = option.ToString(), Handler = () => { Undo.Record(Document); set(option); Document.ApplyChanges(); }
        }).ToArray(), value.ToString());
    }
    private Texture? ResolveTexture(string name)
    {
        if (_textures.TryGetValue(name, out var texture)) return texture;
        texture = File.Exists(System.IO.Path.Combine(Project.OutputPath, "texture", name))
            ? Asset.Load(AssetType.Texture, name, useRegistry: false, libraryPath: Project.OutputPath) as Texture : null;
        _textures[name] = texture;
        return texture;
    }
    private void OnExported(Document document)
    {
        if (document == Document) _revision = -1;
        if (document.Def.Type == AssetType.Texture || document.Def.Type == AssetType.Shader) _assetsDirty = true;
    }
    private void ReleaseAssets()
    {
        _shader?.Dispose(); _shader = null;
        foreach (var texture in _textures.Values) texture?.Dispose();
        _textures.Clear();
    }
    public override void Dispose()
    {
        Project.OnExported -= OnExported;
        _system.Dispose(); _effect.Dispose(); ReleaseAssets(); base.Dispose();
    }
}
