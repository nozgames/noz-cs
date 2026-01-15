//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class Command
{
    public required string Name { get; init; }
    public required string ShortName { get; init; }
    public required Action Handler { get; init; }

    public InputCode Key { get; init; } = InputCode.None;
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Shift { get; init; }
}

public readonly struct ParsedCommand
{
    public const int MaxArgs = 4;
    public const int MaxArgSize = 128;

    public readonly string Name;
    public readonly string[] Args;

    public ParsedCommand(string name, string[] args)
    {
        Name = name;
        Args = args;
    }

    public string GetArg(int index) => index < Args.Length ? Args[index] : string.Empty;
    public int ArgCount => Args.Length;
}

public struct CommandInputOptions
{
    public string? Prefix;
    public string? Placeholder;
    public string? InitialText;
    public bool HideWhenEmpty;
}

public static class CommandPalette
{
    private const byte TextBoxId = 201;

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
        if (string.IsNullOrWhiteSpace(_text))
            return;

        var parsed = ParseCommand(_text);
        if (string.IsNullOrEmpty(parsed.Name))
            return;

        var command = CommandManager.FindCommand(parsed.Name);
        if (command != null)
            command.Handler();
        else
            Log.Error($"Unknown command: {parsed.Name}");
    }

    private static ParsedCommand ParseCommand(string input)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return new ParsedCommand(string.Empty, []);

        var name = parts[0].ToLowerInvariant();
        var args = parts.Length > 1 ? parts[1..] : [];

        return new ParsedCommand(name, args);
    }
}
