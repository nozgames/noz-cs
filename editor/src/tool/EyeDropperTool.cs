//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class EyeDropperTool : Tool
{
    private readonly SpriteEditor _editor;

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
        if (Input.WasButtonPressed(InputCode.KeyEscape, Scope) ||
            Input.WasButtonPressed(InputCode.MouseRight, Scope))
        {
            Workspace.EndTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft, Scope))
        {
            Matrix3x2.Invert(_editor.Document.Transform, out var invTransform);
            var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
            var path = _editor.Document.RootLayer.HitTestPath(localMousePos);
            if (path == null)
                return;

            var alt = Input.IsAltDown(InputScope.All);
            _editor.ApplyEyeDropperColor(path, alt);
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
        }
    }
}
