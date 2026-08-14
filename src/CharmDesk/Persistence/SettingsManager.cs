using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace CharmDesk.Persistence;

public sealed class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "CharmDesk";

    public string DataDirectory { get; }
    public string SettingsPath { get; }
    public AppSettings Current { get; private set; }

    public SettingsManager(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(DataDirectory);
        SettingsPath = Path.Combine(DataDirectory, "settings.json");
        Current = Load();

        // The checkbox should reflect reality, not just whatever we last wrote: if the entry
        // was removed some other way (Windows' own Startup Apps settings, Task Manager, a
        // clean uninstall/reinstall), settings.json would otherwise keep claiming it's on.
        Current.StartWithWindows = IsStartWithWindowsRegistered();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                    return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall through to defaults; a corrupt settings file should never block startup.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log("SettingsManager.Save", ex);
        }
    }

    /// <summary>
    /// Adds or removes the HKCU Run-key entry that launches CharmDesk at login.
    ///
    /// This is the correct mechanism for an unpackaged Win32 app. Once CharmDesk ships as an
    /// MSIX (Microsoft Store) package, the sanctioned replacement is the
    /// <c>Windows.ApplicationModel.StartupTask</c> API - but that API requires a startup-task
    /// extension declared in the package manifest to have anything to look up, so it can only
    /// be wired up alongside that packaging work, not before it. Swap this out then.
    /// </summary>
    public static void ApplyStartWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CharmDesk.exe");
            key.SetValue(RunValueName, $"\"{exePath}\"");
        }
        else
        {
            if (key.GetValue(RunValueName) is not null)
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Reads back whether the Run-key entry actually exists right now, rather than
    /// trusting whatever settings.json last remembered.</summary>
    public static bool IsStartWithWindowsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            Logger.Log("SettingsManager.IsStartWithWindowsRegistered", ex);
            return false;
        }
    }
}
