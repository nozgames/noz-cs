namespace NoZ;

public class CommandLineConfig
{
    public required string Name { get; init; }
    public Dictionary<string, (string Description, Action<string[]> Run)> Commands { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class CommandLineApplication
{
    public static int Run(CommandLineConfig config, string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage(config);
            return 0;
        }

        var command = args[0];
        var commandArgs = args[1..];

        if (!config.Commands.TryGetValue(command, out var entry))
        {
            Console.Error.WriteLine($"Unknown command: {command}");
            Console.Error.WriteLine();
            PrintUsage(config);
            return 1;
        }

        if (commandArgs.Length > 0 && commandArgs[0] is "-h" or "--help")
        {
            entry.Run(commandArgs);
            return 0;
        }

        try
        {
            entry.Run(commandArgs);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintUsage(CommandLineConfig config)
    {
        Console.WriteLine($"Usage: {config.Name} <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        foreach (var (name, (desc, _)) in config.Commands)
            Console.WriteLine($"  {name,-16} {desc}");
        Console.WriteLine();
        Console.WriteLine($"Run '{config.Name} <command> --help' for command-specific options.");
    }
}
