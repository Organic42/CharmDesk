using System;
using System.Linq;
using System.Windows;
using System.Windows.Forms;

namespace CharmDesk.Native;

/// <summary>Multi-monitor + per-monitor-DPI helpers built on top of WinForms' Screen class.</summary>
internal static class MonitorHelper
{
    public static Screen PrimaryScreen => Screen.PrimaryScreen ?? Screen.AllScreens[0];

    public static Screen[] AllScreens => Screen.AllScreens;

    public static Screen? FindByDeviceName(string? deviceName) =>
        string.IsNullOrEmpty(deviceName) ? null : Screen.AllScreens.FirstOrDefault(s => s.DeviceName == deviceName);

    public static double GetDpiScale(IntPtr hwnd)
    {
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        return dpi <= 0 ? 1.0 : dpi / 96.0;
    }

    /// <summary>Converts a screen's device-pixel bounds to WPF DIPs for a given DPI scale.</summary>
    public static Rect BoundsToDip(System.Drawing.Rectangle pixelBounds, double dpiScale)
    {
        if (dpiScale <= 0) dpiScale = 1.0;
        return new Rect(
            pixelBounds.X / dpiScale,
            pixelBounds.Y / dpiScale,
            pixelBounds.Width / dpiScale,
            pixelBounds.Height / dpiScale);
    }
}
