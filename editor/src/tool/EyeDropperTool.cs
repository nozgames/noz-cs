//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Threading.Tasks;

namespace NoZ.Editor;

public class EyeDropperTool : Tool
{
    private readonly GenSpriteEditor _editor;
    private Task<byte[]>? _readbackTask;
    private int _pixelX;
    private int _pixelY;
    private int _rtWidth;
    private bool _shift;

    public EyeDropperTool(GenSpriteEditor editor)
    {
        _editor = editor;
    }

    public override void Begin()
    {
        base.Begin();
        Cursor.SetCrosshair();
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape, Scope) ||
            Input.WasButtonPressed(InputCode.MouseRight, Scope))
        {
            Workspace.EndTool();
            return;
        }

        if (_readbackTask != null)
        {
            if (!_readbackTask.IsCompleted)
                return;

            if (_readbackTask.IsCompletedSuccessfully)
            {
                var data = _readbackTask.Result;
                int idx = (_pixelY * _rtWidth + _pixelX) * 4;

                if (idx + 3 < data.Length)
                {
                    var color = new Color32(data[idx], data[idx + 1], data[idx + 2], data[idx + 3]);
                    _editor.ApplyEyeDropperColor(color, _shift);
                }
            }

            Workspace.EndTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft, Scope))
        {
            if (!UI.TryGetSceneRenderInfo(Workspace.SceneWidgetId, out var info) || info.Handle == nuint.Zero)
                return;

            if (info.ScreenRect.Width <= 0 || info.ScreenRect.Height <= 0)
                return;

            var screenPos = Input.MousePosition;
            var nx = (screenPos.X - info.ScreenRect.X) / info.ScreenRect.Width;
            var ny = (screenPos.Y - info.ScreenRect.Y) / info.ScreenRect.Height;

            if (nx < 0 || nx > 1 || ny < 0 || ny > 1)
                return;

            _pixelX = int.Clamp((int)(nx * info.Width), 0, info.Width - 1);
            _pixelY = int.Clamp((int)(ny * info.Height), 0, info.Height - 1);
            _rtWidth = info.Width;
            _shift = Input.IsShiftDown(InputScope.All);

            _readbackTask = Graphics.Driver.ReadRenderTexturePixelsAsync(info.Handle);
        }
    }
}
