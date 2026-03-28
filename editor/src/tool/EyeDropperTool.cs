//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Threading.Tasks;

namespace NoZ.Editor;

public class EyeDropperTool : Tool
{
    private readonly SpriteEditor _editor;
    private Task<byte[]>? _readbackTask;
    private Vector2 _readbackMouseScreen;
    private SceneRenderInfo _readbackSceneInfo;
    private readonly List<SpritePath> _hitResults = new();

    public EyeDropperTool(SpriteEditor editor)
    {
        _editor = editor;
    }

    public override void Begin()
    {
        base.Begin();
    }

    public override void Update()
    {
        EditorCursor.SetDropper();

        // Check for pending framebuffer readback result
        if (_readbackTask != null)
        {
            if (!_readbackTask.IsCompleted)
                return;

            ApplyReadbackResult();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEscape, Scope) ||
            Input.WasButtonPressed(InputCode.MouseRight, Scope))
        {
            Workspace.EndTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft, Scope))
        {
            var alt = Input.IsAltDown(InputScope.All);

            // If alt is not held, try path hit test first (existing behavior)
            if (!alt)
            {
                Matrix3x2.Invert(_editor.Document.Transform, out var invTransform);
                var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
                _hitResults.Clear();
                _editor.Document.RootLayer.HitTestPath(localMousePos, _hitResults);

                // Find the first path with a visible fill color
                foreach (var path in _hitResults)
                {
                    if (path.FillColor.A > 0)
                    {
                        _editor.ApplyEyeDropperColor(path, false);
                        Input.ConsumeButton(InputCode.MouseLeft);
                        Workspace.EndTool();
                        return;
                    }
                }
            }

            // Path miss or alt held: initiate framebuffer readback
            InitiateReadback();
        }
    }

    private void InitiateReadback()
    {
        if (!UI.TryGetSceneRenderInfo(Workspace.SceneWidgetId, out var info) || info.Handle == 0)
            return;

        _readbackTask = Graphics.Driver.ReadRenderTexturePixelsAsync(info.Handle);
        _readbackMouseScreen = Workspace.MousePosition;
        _readbackSceneInfo = info;
        Input.ConsumeButton(InputCode.MouseLeft);
    }

    private void ApplyReadbackResult()
    {
        var pixels = _readbackTask!.Result;
        _readbackTask = null;

        if (pixels.Length == 0)
        {
            Workspace.EndTool();
            return;
        }

        var relX = (_readbackMouseScreen.X - _readbackSceneInfo.ScreenRect.X) / _readbackSceneInfo.ScreenRect.Width;
        var relY = (_readbackMouseScreen.Y - _readbackSceneInfo.ScreenRect.Y) / _readbackSceneInfo.ScreenRect.Height;
        var px = Math.Clamp((int)(relX * _readbackSceneInfo.Width), 0, _readbackSceneInfo.Width - 1);
        var py = Math.Clamp((int)(relY * _readbackSceneInfo.Height), 0, _readbackSceneInfo.Height - 1);
        var idx = (py * _readbackSceneInfo.Width + px) * 4;

        if (idx >= 0 && idx + 3 < pixels.Length)
        {
            var color = new Color32(pixels[idx], pixels[idx + 1], pixels[idx + 2], 255);
            _editor.ApplyEyeDropperColor(color);
        }

        Workspace.EndTool();
    }
}
