//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static class CommandManager
{
    private static readonly List<Command> _common = [];
    private static readonly List<Command> _workspace = [];
    private static Command[]? _editor;

    public static void Init()
    {
    }

    public static void Shutdown()
    {
        _common.Clear();
        _workspace.Clear();
        _editor = null;
    }

    public static void RegisterCommon(Command[] commands) => _common.AddRange(commands);
    public static void RegisterWorkspace(Command[] commands) => _workspace.AddRange(commands);
    public static void RegisterEditor(Command[]? commands) => _editor = commands;

    public static IEnumerable<Command> GetActiveCommands()
    {
        foreach (var cmd in _common)
            yield return cmd;

        if (_editor != null)
        {
            foreach (var cmd in _editor)
                yield return cmd;
        }
        else
        {
            foreach (var cmd in _workspace)
                yield return cmd;
        }
    }

    public static Command? FindCommand(string name)
    {
        foreach (var cmd in GetActiveCommands())
        {
            if (string.Equals(cmd.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cmd.ShortName, name, StringComparison.OrdinalIgnoreCase))
                return cmd;
        }
        return null;
    }

    public static bool ProcessShortcuts()
    {
        var shift = Input.IsShiftDown();
        var ctrl = Input.IsCtrlDown();
        var alt = Input.IsAltDown();

        foreach (var cmd in GetActiveCommands())
        {
            if (cmd.Key == InputCode.None)
                continue;

            if (!Input.WasButtonPressed(cmd.Key))
                continue;

            if (cmd.Ctrl != ctrl || cmd.Alt != alt || cmd.Shift != shift)
                continue;

            if (cmd.Ctrl)
            {
                Input.ConsumeButton(InputCode.KeyLeftCtrl);
                Input.ConsumeButton(InputCode.KeyRightCtrl);
            }

            if (cmd.Alt)
            {
                Input.ConsumeButton(InputCode.KeyLeftAlt);
                Input.ConsumeButton(InputCode.KeyRightAlt);
            }

            if (cmd.Shift)
            {
                Input.ConsumeButton(InputCode.KeyLeftShift);
                Input.ConsumeButton(InputCode.KeyRightShift);
            }

            Input.ConsumeButton(cmd.Key);

            cmd.Handler();
            return true;
        }

        return false;
    }
}
