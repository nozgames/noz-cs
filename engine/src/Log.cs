//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static class Log
{
    public static void Info(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[INFO] {message}");
    }

    public static void Debug(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[DEBUG] {message}");
    }

    public static void Warning(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[WARNING] {message}");
    }

    public static void Error(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[ERROR] {message}");
    }
}
