using System;
using System.IO;
using System.Text.Json;

namespace CharmDesk.Persistence;

public sealed class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string DataDirectory { get; }
    public string SettingsPath { get; }
    public AppSettings Current { get; private set; }

    public SettingsManager(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        Directory.CreateDirectory(DataDirectory);
        SettingsPath = Path.Combine(DataDirectory, "settings.json");
        Current = Load();
    }

    /// <summary>Reconciles the persisted "start with Windows" flag against what Windows
    /// actually has registered - the user can change it outside the app (Startup Apps settings,
    /// Task Manager), and settings.json would otherwise keep claiming whatever it last wrote.
    /// Async because the packaged StartupTask API is; call once at startup.</summary>
    public async System.Threading.Tasks.Task SyncStartWithWindowsAsync()
    {
        var state = await StartupManager.GetStateAsync();
        Current.StartWithWindows = state == StartupState.Enabled;
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

}
