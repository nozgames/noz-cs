//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using NoZ;
using NoZ.Editor;

namespace NoZ.Editor.Graphics3D;

internal sealed class MeshEditor : DocumentEditor
{
    private const float OrbitSpeed = 0.01f;
    private const ushort MeshLayer = EditorLayer.PixelGrid + 1;

    private readonly Camera3D _camera = new() { FieldOfView = MathF.PI / 4f };
    private Mesh? _mesh;
    private float _yaw;
    private float _pitch;
    private float _distance;
    private float _radius;
    private Vector2 _lastMousePosition;
    private bool _orbiting;

    public new MeshDocument Document => (MeshDocument)base.Document;
    public override bool ShowInspector => true;
    public override bool RequiresDepth => true;

    public MeshEditor(MeshDocument document) : base(document)
    {
        Commands =
        [
            new Command("Exit Edit Mode", Workspace.EndEdit, [InputCode.KeyTab]),
            new Command("Frame Mesh", FrameView, [InputCode.KeyF]),
        ];

        Project.OnExported += OnDocumentExported;
        ReloadMesh();
        ResetView();
    }

    public override void PreUpdate()
    {
        var mousePosition = Input.MousePosition;
        var overScene = UI.IsHovered(Workspace.SceneWidgetId);

        if (overScene && Input.WasButtonPressed(InputCode.MouseLeft))
        {
            _orbiting = true;
            _lastMousePosition = mousePosition;
        }

        if (_orbiting)
        {
            if (!Input.IsButtonDownRaw(InputCode.MouseLeft))
            {
                _orbiting = false;
            }
            else
            {
                var delta = mousePosition - _lastMousePosition;
                _yaw -= delta.X * OrbitSpeed;
                _pitch = Math.Clamp(_pitch + delta.Y * OrbitSpeed, -1.45f, 1.45f);
                _lastMousePosition = mousePosition;
            }
        }
    }

    public override void Update()
    {
        var shader = MeshPreviewRenderer.GetShader();
        if (_mesh == null || shader == null || _mesh.RenderMesh.Handle == nuint.Zero)
            return;

        var cosPitch = MathF.Cos(_pitch);
        var orbit = new Vector3(
            cosPitch * MathF.Sin(_yaw),
            MathF.Sin(_pitch),
            cosPitch * MathF.Cos(_yaw));
        var target = _mesh.BoundsCenter;
        var eye = target + orbit * _distance;
        var near = MathF.Max(_radius * 0.01f, 0.001f);
        var far = MathF.Max(_distance + _radius * 4f, near + 1f);

        _camera.Position = eye;
        _camera.Target = target;
        _camera.NearClip = near;
        _camera.FarClip = far;
        var viewProjection = MeshWorkspaceProjection.Create(
            _camera, Workspace.Camera.ViewMatrix, Document.Bounds.Translate(Document.Position));

        using (Graphics.PushState())
        {
            Graphics.SetShader(shader);
            MeshPreviewRenderer.BindTexture();
            Graphics.SetBlendMode(BlendMode.None);
            Graphics.SetLayer(MeshLayer);
            global::NoZ.Graphics3D.Draw(_mesh, viewProjection);

            // The projection is global to the current pass rather than part of PushState.
            // Restore the workspace camera before any later editor draw commands are queued.
            Graphics.SetCamera(Workspace.Camera);
        }
    }

    public override void Dispose()
    {
        Project.OnExported -= OnDocumentExported;
        _mesh?.Dispose();
        _mesh = null;
        base.Dispose();
    }

    private void ResetView()
    {
        var size = _mesh?.BoundsSize ?? Vector3.One;
        _radius = MathF.Max(size.Length() * 0.5f, 0.01f);
        _distance = MathF.Max(_radius * 2.75f, 0.1f);
        _yaw = MathF.PI * 0.25f;
        _pitch = MathF.PI * 0.15f;
    }

    private void FrameView()
    {
        ResetView();
        Workspace.FrameRect(Document.Bounds.Translate(Document.Position));
    }

    private void ReloadMesh()
    {
        _mesh?.Dispose();
        _mesh = Asset.Load(Mesh.Type, Document.Name, useRegistry: false, libraryPath: Project.OutputPath) as Mesh;
    }

    private void OnDocumentExported(Document document)
    {
        if (document == Document)
        {
            ReloadMesh();
            _radius = MathF.Max((_mesh?.BoundsSize ?? Vector3.One).Length() * 0.5f, 0.01f);
        }
    }
}
