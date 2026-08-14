using System;
using System.Runtime.InteropServices;

namespace CharmDesk.Native;

/// <summary>
/// A low-level global mouse-move tap. Needed because WS_EX_TRANSPARENT excludes a window from
/// mouse hit-testing entirely and unconditionally (not just over transparent pixels) - so once
/// CharmWindow goes click-through, it stops receiving any WPF mouse events at all, and nothing
/// inside the window can ever detect "the cursor is back over the charm" to turn click-through
/// off again. This hook runs independently of that state, purely to catch that one transition;
/// everything afterwards (drag, release, hover-exit) runs through normal WPF input once the
/// window is non-transparent again.
/// </summary>
internal sealed class GlobalMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Keep a live reference to the delegate for the lifetime of the hook - otherwise the GC
    // can collect it while native code still holds a raw function pointer to it.
    private readonly LowLevelMouseProc _proc;
    private IntPtr _hookHandle = IntPtr.Zero;

    /// <summary>Fired for every global mouse move/left-button-down, with the raw screen-pixel position.</summary>
    public event Action<int, int>? MouseMoved;

    public GlobalMouseHook()
    {
        _proc = HookCallback;
    }

    /// <summary>True once the hook is actually installed. If this is false after <see cref="Start"/>,
    /// the click-through recovery it powers won't work for the rest of the session - most likely
    /// blocked by security software - and the caller should not assume the charm is reachable.</summary>
    public bool IsInstalled => _hookHandle != IntPtr.Zero;

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero) return;
        using var curModule = System.Diagnostics.Process.GetCurrentProcess().MainModule!;
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        if (_hookHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            CharmDesk.Persistence.Logger.Log($"GlobalMouseHook.Start failed, Win32 error {error}");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_MOUSEMOVE || wParam == (IntPtr)WM_LBUTTONDOWN || wParam == (IntPtr)WM_RBUTTONDOWN))
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            MouseMoved?.Invoke(data.pt.X, data.pt.Y);
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
