//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static partial class UI
{
    public static Vector2 MouseWorldPosition { get; private set; }

    private static void HandleInput()
    {
        if (ElementTree.ActivePopupCount > 0)
        {
            Input.ConsumeButton(InputCode.MouseLeft);
            Input.ConsumeButton(InputCode.MouseLeftDoubleClick);
            Input.ConsumeButton(InputCode.MouseRight);
        }
    }
}
