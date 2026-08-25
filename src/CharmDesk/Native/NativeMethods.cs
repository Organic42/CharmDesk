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

    /// <summary>Applies the one-time flags a desktop overlay window needs: tool window (hidden
    /// from taskbar/alt-tab) and non-activating (never steals foreground focus).
    ///
    /// <paramref name="layered"/> adds WS_EX_LAYERED, which is required for WPF's
    /// AllowsTransparency path but must NOT be set for the DWM-composited path - a layered
    /// window is presented through UpdateLayeredWindow, which is exactly what that path exists
    /// to avoid.</summary>
    public static void ApplyOverlayWindowStyles(IntPtr hWnd, bool layered = true)
    {
        var style = GetExStyle(hWnd);
        style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        if (layered) style |= WS_EX_LAYERED;
        else style &= ~WS_EX_LAYERED;
        style &= ~WS_EX_APPWINDOW;
        SetExStyle(hWnd, style);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    /// <summary>Extends the DWM frame over the entire client area ("sheet of glass"), which makes
    /// the window's unpainted pixels transparent via the desktop compositor itself rather than
    /// via UpdateLayeredWindow.
    ///
    /// The point of the distinction: WPF's AllowsTransparency creates a layered window and
    /// presents it with UpdateLayeredWindow, a pre-DWM API that on modern Windows runs through a
    /// compatibility path. This route instead hands an ordinary, hardware-accelerated window to
    /// DWM and lets it do the alpha blending, which is the same path every normal window already
    /// takes. Returns false if DWM refused (it shouldn't on Win10/11, where composition is always
    /// on, but this must never take the app down).</summary>
    public static bool ExtendFrameIntoClientArea(IntPtr hWnd)
    {
        try
        {
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            return DwmExtendFrameIntoClientArea(hWnd, ref margins) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
