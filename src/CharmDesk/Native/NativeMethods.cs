using System;
using System.Collections.Generic;
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

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>The window's bounds in real screen pixels. Worth preferring over WPF's
    /// Window.Left/Top wherever a value has to be compared against a raw Win32 coordinate:
    /// WPF reports those in device-independent units whose relationship to screen pixels stops
    /// being a single scale factor once monitors with different DPI are in play, which is the
    /// normal case for a laptop with an external display attached.</summary>
    public static bool TryGetWindowRect(IntPtr hWnd, out RECT rect) => GetWindowRect(hWnd, out rect);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    private const int RGN_OR = 2;

    /// <summary>
    /// Restricts the window to the given rectangles (window-relative, in real pixels): everything
    /// outside them stops being part of the window at all, so the desktop and other applications
    /// underneath receive those clicks directly.
    ///
    /// This is the click-through mechanism. The obvious-looking alternatives do not survive
    /// contact with a real desktop: WS_EX_TRANSPARENT excludes the window from hit-testing
    /// wholesale, so nothing inside it can ever be clicked without some outside agent toggling the
    /// style back off, and answering WM_NCHITTEST with HTTRANSPARENT only forwards the hit to
    /// windows on the same thread - which makes it useless for letting a click reach another
    /// process. A window region is enforced by the window manager itself, so it works across
    /// processes, needs no hook, and behaves identically no matter how the window is composited
    /// or how fast the machine renders.
    /// </summary>
    public static void SetClickableRegion(IntPtr hWnd, IReadOnlyList<RECT> parts)
    {
        if (parts.Count == 0)
        {
            SetWindowRgn(hWnd, IntPtr.Zero, false);
            return;
        }

        var combined = CreateRectRgn(parts[0].Left, parts[0].Top, parts[0].Right, parts[0].Bottom);
        for (var i = 1; i < parts.Count; i++)
        {
            var next = CreateRectRgn(parts[i].Left, parts[i].Top, parts[i].Right, parts[i].Bottom);
            CombineRgn(combined, combined, next, RGN_OR);
            DeleteObject(next);
        }

        // On success the window manager takes ownership of the region handle and it must not be
        // deleted here; on failure nothing took it and it would otherwise leak a GDI object.
        if (SetWindowRgn(hWnd, combined, false) == 0)
            DeleteObject(combined);
    }

    /// <summary>Drops any region set by <see cref="SetClickableRegion"/>, restoring the window to
    /// its full rectangle.</summary>
    public static void ClearClickableRegion(IntPtr hWnd) => SetWindowRgn(hWnd, IntPtr.Zero, false);

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
