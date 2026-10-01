using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CharmDesk.Persistence;

namespace CharmDesk.Power;

/// <summary>The only place CharmDesk asks Windows to shut down or sleep. Both paths are the
/// polite ones: nothing is ever forced closed.</summary>
internal static class PowerActions
{
    public static bool Run(PowerAction action) => action switch
    {
        PowerAction.ShutDown => ShutDown(),
        PowerAction.Sleep => Sleep(),
        _ => false,
    };

    /// <summary>
    /// "/t 0" is load-bearing. shutdown.exe treats ANY timeout above zero as an implied /f, which
    /// force-closes every running app and throws away unsaved work without asking. CharmDesk runs
    /// its own countdown instead and only calls Windows once it reaches zero, so the shut down is
    /// the ordinary kind: an app with unsaved work gets to hold it up and ask.
    /// Full path rather than a bare "shutdown.exe", so nothing earlier on PATH can stand in for it.
    /// </summary>
    private static bool ShutDown()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
                Arguments = "/s /t 0",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return false;

            // shutdown.exe returns straight away; a non-zero code means Windows refused - most
            // often a group policy that doesn't allow it, or another shutdown already underway.
            if (!process.WaitForExit(5000)) return true;
            if (process.ExitCode != 0) Logger.Log($"PowerActions.ShutDown: shutdown.exe exited with {process.ExitCode}");
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Logger.Log("PowerActions.ShutDown", ex);
            return false;
        }
    }

    /// <summary>force:false lets apps that are mid-task object; disableWakeEvent:false keeps
    /// scheduled wake-ups (alarms, updates) working.</summary>
    private static bool Sleep()
    {
        try
        {
            var ok = System.Windows.Forms.Application.SetSuspendState(
                System.Windows.Forms.PowerState.Suspend, force: false, disableWakeEvent: false);
            if (!ok) Logger.Log("PowerActions.Sleep: Windows declined to suspend.");
            return ok;
        }
        catch (Exception ex) when (ex is Win32Exception or PlatformNotSupportedException)
        {
            Logger.Log("PowerActions.Sleep", ex);
            return false;
        }
    }
}
