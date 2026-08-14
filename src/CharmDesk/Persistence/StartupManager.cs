using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CharmDesk.Persistence;

/// <summary>The outcome of trying to read or change the "start with Windows" setting.</summary>
public enum StartupState
{
    /// <summary>Will launch at login.</summary>
    Enabled,

    /// <summary>Won't launch at login, and the app is free to turn it on.</summary>
    Disabled,

    /// <summary>The user turned it off in Windows' own Startup Apps UI (or Task Manager).
    /// Windows deliberately makes this stick - the app cannot re-enable it, so the UI has to
    /// explain that rather than silently failing or fighting the user's choice.</summary>
    DisabledByUser,

    /// <summary>Blocked by group policy - same as above, not something the app can override.</summary>
    DisabledByPolicy,
}

/// <summary>
/// "Start with Windows", handled two different ways depending on how CharmDesk is running:
///
/// - <b>Packaged (MSIX / Microsoft Store):</b> uses <c>Windows.ApplicationModel.StartupTask</c>,
///   which is the only mechanism the Store allows and the only one that appears correctly in
///   Windows' Startup Apps settings. It requires the matching <c>windows.startupTask</c>
///   extension in Package.appxmanifest.
/// - <b>Unpackaged (plain exe, e.g. a dev build or a portable copy):</b> the StartupTask API
///   throws without package identity, so this falls back to the classic HKCU Run key.
///
/// Callers don't need to care which path is in play.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "CharmDesk";

    /// <summary>Must match the TaskId in Package.appxmanifest's uap5:StartupTask element.</summary>
    private const string StartupTaskId = "CharmDeskStartupTask";

    private const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);

    private static bool? _isPackaged;

    /// <summary>True when running with MSIX package identity (Store install / sideloaded msix).</summary>
    public static bool IsPackaged
    {
        get
        {
            if (_isPackaged is not null) return _isPackaged.Value;
            try
            {
                var length = 0;
                var result = GetCurrentPackageFullName(ref length, null);
                _isPackaged = result != AppModelErrorNoPackage;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                _isPackaged = false;
            }
            return _isPackaged.Value;
        }
    }

    public static async Task<StartupState> GetStateAsync()
    {
        if (!IsPackaged)
            return IsRunKeyRegistered() ? StartupState.Enabled : StartupState.Disabled;

#if PACKAGED_BUILD
        try
        {
            var task = await global::Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
            return Map(task.State);
        }
        catch (Exception ex)
        {
            Logger.Log("StartupManager.GetStateAsync", ex);
            return StartupState.Disabled;
        }
#else
        // Unreachable in practice: a build without the WinRT projection is never packaged.
        await Task.CompletedTask;
        return IsRunKeyRegistered() ? StartupState.Enabled : StartupState.Disabled;
#endif
    }

    /// <summary>Applies the requested setting and returns the state actually achieved - which
    /// can differ from what was asked for (see <see cref="StartupState.DisabledByUser"/>).</summary>
    public static async Task<StartupState> SetEnabledAsync(bool enabled)
    {
        if (!IsPackaged)
        {
            SetRunKey(enabled);
            return enabled ? StartupState.Enabled : StartupState.Disabled;
        }

#if PACKAGED_BUILD
        try
        {
            var task = await global::Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
            if (enabled)
            {
                var newState = await task.RequestEnableAsync();
                return Map(newState);
            }

            task.Disable();
            return StartupState.Disabled;
        }
        catch (Exception ex)
        {
            Logger.Log($"StartupManager.SetEnabledAsync({enabled})", ex);
            return StartupState.Disabled;
        }
#else
        await Task.CompletedTask;
        SetRunKey(enabled);
        return enabled ? StartupState.Enabled : StartupState.Disabled;
#endif
    }

#if PACKAGED_BUILD
    private static StartupState Map(global::Windows.ApplicationModel.StartupTaskState state) => state switch
    {
        global::Windows.ApplicationModel.StartupTaskState.Enabled => StartupState.Enabled,
        global::Windows.ApplicationModel.StartupTaskState.EnabledByPolicy => StartupState.Enabled,
        global::Windows.ApplicationModel.StartupTaskState.DisabledByUser => StartupState.DisabledByUser,
        global::Windows.ApplicationModel.StartupTaskState.DisabledByPolicy => StartupState.DisabledByPolicy,
        _ => StartupState.Disabled,
    };
#endif

    // ---- Unpackaged fallback: the classic HKCU Run key ----------------------

    private static bool IsRunKeyRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            Logger.Log("StartupManager.IsRunKeyRegistered", ex);
            return false;
        }
    }

    private static void SetRunKey(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (enabled)
            {
                var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CharmDesk.exe");
                key.SetValue(RunValueName, $"\"{exePath}\"");
            }
            else if (key.GetValue(RunValueName) is not null)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            Logger.Log($"StartupManager.SetRunKey({enabled})", ex);
        }
    }
}
