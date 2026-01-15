//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public readonly struct Command
{
    public const int MaxArgs = 4;
    public const int MaxArgSize = 128;

    public readonly string Name;
    public readonly string[] Args;

    public Command(string name, string[] args)
    {
        Name = name;
        Args = args;
    }

    public string GetArg(int index) => index < Args.Length ? Args[index] : string.Empty;
    public int ArgCount => Args.Length;
}

public class CommandHandler
{
    public required string ShortName { get; init; }
    public required string Name { get; init; }
    public required Action<Command> Handler { get; init; }
}

public struct CommandInputOptions
{
    public CommandHandler[]? Commands;
    public string? Prefix;
    public string? Placeholder;
    public string? InitialText;
    public bool HideWhenEmpty;
}

public static class CommandPalette
{
    private const byte TextBoxId = 201;

    private static CommandHandler[]? _commands;
    private static string? _prefix;
    private static string? _placeholder;
    private static bool _hideWhenEmpty;
    private static bool _enabled;
    private static string _text = string.Empty;
    private static InputSet _input = null!;
    private static bool _popInput;

    public static bool IsEnabled => _enabled;

    public static void Init()
    {
        _input = new InputSet("CommandPalette");
    }

    public static void Shutdown()
    {
    }

    public static void Begin(CommandInputOptions options)
    {
        _enabled = true;
        _commands = options.Commands;
        _prefix = options.Prefix;
        _placeholder = options.Placeholder;
        _hideWhenEmpty = options.HideWhenEmpty;
        _text = options.InitialText ?? string.Empty;

        Input.PushInputSet(_input);
        _popInput = true;

        UI.SetFocus(TextBoxId, EditorStyle.CanvasId.CommandPalette);
    }

    public static void End()
    {
        if (!_enabled)
            return;

        if (_popInput)
        {
            Input.PopInputSet();
            _popInput = false;
        }

        UI.ClearFocus();

        _enabled = false;
        _commands = null;
        _prefix = null;
        _placeholder = null;
        _text = string.Empty;
    }

    public static void Update()
    {
        if (!_enabled)
            return;

        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            End();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter))
        {
            ExecuteCommand();
            End();
        }
    }

    public static void UpdateUI()
    {
        if (!_enabled)
                return;

        if (_hideWhenEmpty && string.IsNullOrEmpty(_text))
            return;

        UI.BeginCanvas(new CanvasStyle { Id = EditorStyle.CanvasId.CommandPalette });
        UI.BeginContainer(new ContainerStyle
        {
            Width = EditorStyle.CommandPalette.Width,
            Align = Align.Center,
            Padding = EdgeInsets.All(10),
            Color = Color.Red
        });

        UI.BeginColumn();
        UI.TextBox(ref _text, new TextBoxStyle
        {
            Height = EditorStyle.CommandPalette.Height,
            FontSize = EditorStyle.CommandPalette.FontSize,
            BackgroundColor = EditorStyle.OverlayBackgroundColor,
            TextColor = EditorStyle.OverlayTextColor,
            Border = new BorderStyle { Radius = 10, Width = 2, Color = EditorStyle.OverlayBackgroundColor },
            FocusBorder = new BorderStyle { Radius = 10, Width = 2, Color = EditorStyle.SelectionColor },
            Id = TextBoxId
        });
        
        UI.EndColumn();
        UI.EndContainer();
        UI.EndCanvas();
    }

    private static void ExecuteCommand()
    {
        if (string.IsNullOrWhiteSpace(_text) || _commands == null)
            return;

        var command = ParseCommand(_text);
        if (command.Name == null)
            return;

        var handler = FindHandler(command.Name);
        if (handler != null)
            handler.Handler(command);
        else
            Log.Error($"Unknown command: {command.Name}");
    }

    private static Command ParseCommand(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return new Command(string.Empty, []);

        var name = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1..] : [];

        return new Command(name, args);
    }

    private static CommandHandler? FindHandler(string name)
    {
        if (_commands == null)
            return null;

        foreach (var cmd in _commands)
        {
            if (string.IsNullOrEmpty(cmd.Name))
                continue;

            if (string.Equals(cmd.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cmd.ShortName, name, StringComparison.OrdinalIgnoreCase))
                return cmd;
        }

        return null;
    }
}
