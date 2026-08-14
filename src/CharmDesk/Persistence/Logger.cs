using System;
using System.IO;

namespace CharmDesk.Persistence;

/// <summary>Minimal file logger for crash diagnostics. Never throws - a logging failure must
/// never be the thing that brings the app down.</summary>
public static class Logger
{
    private static string? _logPath;

    public static void Initialize(string dataDirectory)
    {
        try
        {
            var logsDir = Path.Combine(dataDirectory, "logs");
            Directory.CreateDirectory(logsDir);
            _logPath = Path.Combine(logsDir, "charmdesk.log");
        }
        catch
        {
            _logPath = null;
        }
    }

    public static void Log(string context, Exception ex)
    {
        try
        {
            if (_logPath is null) return;
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}\n";
            File.AppendAllText(_logPath, line);
        }
        catch
        {
            // Logging is best-effort only.
        }
    }

    public static void Log(string message)
    {
        try
        {
            if (_logPath is null) return;
            File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch
        {
            // Logging is best-effort only.
        }
    }
}
