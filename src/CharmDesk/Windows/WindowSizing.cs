using System;
using System.Windows;

namespace CharmDesk.Windows;

/// <summary>
/// Keeps the management windows inside the screen they open on.
///
/// Their sizes are authored as fixed values that comfortably fit a large desktop monitor, which
/// silently assumes every user has one. On a 1366x768 laptop - the resolution that ships with
/// most budget machines - at Windows' common 125% scaling, the usable work area is only about
/// 1092x582 DIP. A 700-DIP-tall Charm Library centred on that lands with its title bar roughly
/// 60px *above* the top of the screen: the window is open and focused, but it can't be seen
/// properly and can't be dragged back into view, because the bar you'd grab isn't on screen.
///
/// <see cref="SystemParameters.WorkArea"/> is already in DIP and already excludes the taskbar,
/// so it's the right thing to measure against - comparing against raw pixel dimensions would get
/// scaling backwards and "fix" nothing on exactly the machines that need it.
///
/// The arithmetic lives in the two pure Clamp* methods so it can be tested against screen sizes
/// the development machine doesn't have.
/// </summary>
public static class WindowSizing
{
    /// <summary>Shrinks a desired window size to fit a work area, leaving a small margin.
    /// Returns the size unchanged when it already fits.</summary>
    public static (double Width, double Height) ClampSize(
        double desiredWidth, double desiredHeight, Rect workArea, double margin = 24)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) return (desiredWidth, desiredHeight);

        // The floor stops a pathologically small work area (or a large margin) from collapsing a
        // window to nothing; being slightly too big beats being unusable.
        var maxWidth = Math.Max(320, workArea.Width - margin);
        var maxHeight = Math.Max(320, workArea.Height - margin);

        var width = double.IsNaN(desiredWidth) ? desiredWidth : Math.Min(desiredWidth, maxWidth);
        var height = double.IsNaN(desiredHeight) ? desiredHeight : Math.Min(desiredHeight, maxHeight);
        return (width, height);
    }

    /// <summary>Moves a window's top-left so the whole window sits inside the work area.</summary>
    public static (double Left, double Top) ClampPosition(
        double left, double top, double width, double height, Rect workArea)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) return (left, top);

        // Max() applied last on purpose: when the window is still larger than the work area, this
        // pins it to the top-left, keeping the title bar reachable. Clamping the other way round
        // would push the title bar off the top - the exact failure this class exists to prevent.
        var clampedLeft = Math.Max(workArea.Left, Math.Min(left, workArea.Right - width));
        var clampedTop = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - height));
        return (clampedLeft, clampedTop);
    }

    /// <summary>Caps a window's size to what actually fits, before it is shown. Also lowers
    /// MinWidth/MinHeight where those alone would exceed the screen, since a minimum larger than
    /// the work area would otherwise win and put the size straight back over the edge.</summary>
    public static void FitToWorkArea(Window window)
    {
        var area = SystemParameters.WorkArea;
        var (width, height) = ClampSize(window.Width, window.Height, area);

        var (minWidth, minHeight) = ClampSize(window.MinWidth, window.MinHeight, area);
        window.MinWidth = minWidth;
        window.MinHeight = minHeight;

        if (!double.IsNaN(width)) window.Width = width;
        if (!double.IsNaN(height)) window.Height = height;
    }

    /// <summary>Nudges an already-positioned window fully back onto the work area.
    ///
    /// Runs after WPF has applied CenterScreen/CenterOwner rather than instead of it: those modes
    /// position the window themselves at show time, so setting Left/Top any earlier would simply
    /// be overwritten. CenterOwner needs this most - About and Preview centre on an owner that
    /// can sit against the top-right screen edge, which pushes even a well-sized child off the
    /// display.</summary>
    public static void EnsureOnScreen(Window window)
    {
        var width = double.IsNaN(window.Width) ? window.ActualWidth : window.Width;
        var height = double.IsNaN(window.Height) ? window.ActualHeight : window.Height;
        if (width <= 0 || height <= 0) return;

        var (left, top) = ClampPosition(window.Left, window.Top, width, height, SystemParameters.WorkArea);
        window.Left = left;
        window.Top = top;
    }

    /// <summary>Applies both halves: clamp the size now, and correct the position once WPF has
    /// finished placing the window.</summary>
    public static void KeepOnScreen(Window window)
    {
        FitToWorkArea(window);
        window.SourceInitialized += (_, _) => EnsureOnScreen(window);
    }
}
