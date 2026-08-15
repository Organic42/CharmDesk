using System;
using System.Runtime.InteropServices;
using CharmDesk.Persistence;

namespace CharmDesk.Native;

/// <summary>
/// Returns memory to the OS after the app has been doing something unusually heavy.
///
/// CharmDesk spends almost all of its life idle in the tray with one small window on screen, but
/// its working set is set by its *peak* - opening the Charm Library decodes every charm's
/// thumbnail, and Windows has no reason to shrink the working set again afterwards. The process
/// then sits at that peak indefinitely, which is what people see when they check Task Manager and
/// conclude a desktop ornament is using a few hundred MB.
/// </summary>
internal static class MemoryHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minSize, IntPtr maxSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// Collects, then asks Windows to trim this process's working set.
    ///
    /// Passing -1 for both bounds is the documented "trim as much as you can" signal. This is not
    /// a leak fix and it doesn't make the app need less memory - pages the app genuinely still
    /// needs fault straight back in on next touch. What it does is stop a one-off spike (opening
    /// the Library) from being held forever, which for a background tray app is the difference
    /// between reading as well-behaved and reading as bloated.
    ///
    /// Only worth calling after a known-heavy moment finishes, never on a timer or in a loop:
    /// trimming pages that are about to be touched again just forces them to be re-faulted.
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Purely an optimisation - never worth taking the app down over.
            Logger.Log("MemoryHelper.TrimWorkingSet", ex);
        }
    }
}
