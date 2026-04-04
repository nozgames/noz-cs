//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static class CommandManager
{
    private static Command[] _common = [];
    private static Command[] _workspace = [];
    private static Command[]? _editor;

    public static void Init()
    {
    }

    public static void Shutdown()
    {
        _common = [];
        _workspace = [];
        _editor = null;
    }

    public static void RegisterCommon(Command[] commands) => _common = commands;
    public static void RegisterWorkspace(Command[] commands) => _workspace = commands;
    public static void RegisterEditor(Command[]? commands) => _editor = commands;

    private static bool CheckFilter(Command cmd, string filter) =>
        string.IsNullOrEmpty(filter) || cmd.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<Command> Filtered(string filter)
    {
        foreach (var cmd in _common)
            if (CheckFilter(cmd, filter))
                yield return cmd;

        var commands = _editor ?? _workspace;
        foreach (var cmd in commands)
            if (CheckFilter(cmd, filter))
                yield return cmd;
    }

    private static bool ProcessShortcuts(Command cmd, bool shift, bool ctrl, bool alt)
    {
        if (cmd.Shortcuts == null)
            return false;

        foreach (var shortcut in cmd.Shortcuts)
        {
            if (shift != shortcut.Shift) continue;
            if (alt != shortcut.Alt) continue;
            if (ctrl != shortcut.Ctrl) continue;
            if (!Input.WasButtonPressed(shortcut.Key)) continue;

            Input.ConsumeButton(shortcut.Key);
            cmd.Handler();
            return true;
        }

        return false;
    }

    public static bool ProcessShortcuts()
    {
        var shift = Input.IsShiftDown();
        var ctrl = Input.IsCtrlDown();
        var alt = Input.IsAltDown();

        foreach (var cmd in _common)
            if (ProcessShortcuts(cmd, shift, ctrl, alt))
                return true;

        var commands = _editor ?? _workspace;
        foreach (var cmd in commands)
            if (ProcessShortcuts(cmd, shift, ctrl, alt))
                return true;

        return false;
    }
}
