using System;
using System.Runtime.InteropServices;

namespace CharmDesk.Native;

internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;

    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_APPWINDOW = 0x00040000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    public static int GetExStyle(IntPtr hWnd) => (int)GetWindowLongPtrSafe(hWnd, GWL_EXSTYLE);

    public static void SetExStyle(IntPtr hWnd, int style) => SetWindowLongPtrSafe(hWnd, GWL_EXSTYLE, (IntPtr)style);

    private static IntPtr GetWindowLongPtrSafe(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : (IntPtr)GetWindowLong32(hWnd, nIndex);

    private static IntPtr SetWindowLongPtrSafe(IntPtr hWnd, int nIndex, IntPtr newValue) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, newValue) : (IntPtr)SetWindowLong32(hWnd, nIndex, (int)newValue);

    /// <summary>
    /// Toggles WS_EX_TRANSPARENT (click-through) on the given window handle. When set, mouse
    /// input passes straight through to whatever is beneath the desktop overlay - this is what
    /// lets a full-screen-sized charm window coexist with normal apps: only the small painted
    /// region (string + charm) actually intercepts clicks.
    /// </summary>
    public static void SetClickThrough(IntPtr hWnd, bool clickThrough)
    {
        var style = GetExStyle(hWnd);
        var withoutTransparent = style & ~WS_EX_TRANSPARENT;
        var newStyle = clickThrough ? (withoutTransparent | WS_EX_TRANSPARENT) : withoutTransparent;
        if (newStyle != style)
            SetExStyle(hWnd, newStyle);
    }

    /// <summary>Applies the one-time flags a desktop overlay window needs: layered, tool window
    /// (hidden from taskbar/alt-tab), and non-activating (never steals foreground focus).</summary>
    public static void ApplyOverlayWindowStyles(IntPtr hWnd)
    {
        var style = GetExStyle(hWnd);
        style |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        style &= ~WS_EX_APPWINDOW;
        SetExStyle(hWnd, style);
    }
}
