using System.Windows;
using CharmDesk.Windows;

namespace CharmDesk.Tests;

/// <summary>
/// Regression tests for the small-laptop window bug: the management windows are authored at
/// fixed sizes that only fit a large monitor, and on a 1366x768 laptop with display scaling they
/// were being centred with their title bars above the top of the screen - open, focused, and
/// impossible to see or drag back.
///
/// These exercise the arithmetic directly against screen geometries the development machine
/// doesn't have, which is the whole reason it was extracted into pure methods.
/// </summary>
public sealed class WindowSizingTests
{
    /// <summary>1366x768 at 125% scaling, minus a ~40px taskbar - the machine the bug was
    /// reported on. WPF reports the work area in DIP, so it's (1366/1.25) x ((768-40)/1.25).</summary>
    private static readonly Rect SmallLaptopWorkArea = new(0, 0, 1092.8, 582.4);

    /// <summary>A 2560x1440 desktop at 100%, where every window already fit.</summary>
    private static readonly Rect LargeDesktopWorkArea = new(0, 0, 2560, 1392);

    [Theory]
    [InlineData(760, 700)]  // Charm Library - the one that was reported invisible
    [InlineData(780, 640)]  // Charm Manager
    [InlineData(440, 620)]  // Settings
    [InlineData(460, 620)]  // About
    public void ClampSize_ShrinksOversizedWindowsToFitASmallLaptop(double width, double height)
    {
        var (w, h) = WindowSizing.ClampSize(width, height, SmallLaptopWorkArea);

        Assert.True(w <= SmallLaptopWorkArea.Width, $"width {w} still exceeds the work area");
        Assert.True(h <= SmallLaptopWorkArea.Height, $"height {h} still exceeds the work area");
        // Every one of these is taller than the work area, so the height must actually change.
        Assert.True(h < height, "height should have been reduced");
    }

    [Fact]
    public void ClampSize_LeavesWindowsAloneWhenTheyAlreadyFit()
    {
        var (w, h) = WindowSizing.ClampSize(760, 700, LargeDesktopWorkArea);

        Assert.Equal(760, w);
        Assert.Equal(700, h);
    }

    [Fact]
    public void ClampPosition_PullsACentredWindowBackOnScreen()
    {
        // What WPF's CenterScreen produced on the small laptop: a 700-tall window on a 582-tall
        // work area centres to a negative Top, putting the title bar off the top of the screen.
        var centredTop = (SmallLaptopWorkArea.Height - 700) / 2;
        Assert.True(centredTop < 0, "precondition: this is the off-screen case being fixed");

        var (_, top) = WindowSizing.ClampPosition(100, centredTop, 760, 700, SmallLaptopWorkArea);

        Assert.True(top >= SmallLaptopWorkArea.Top, "title bar must not sit above the work area");
    }

    [Fact]
    public void ClampPosition_KeepsTheTitleBarReachableWhenTheWindowIsStillTooBig()
    {
        // Even in the degenerate case (window larger than the screen), the top-left must win so
        // there is something to grab - clamping to the bottom-right would hide the title bar.
        var (left, top) = WindowSizing.ClampPosition(500, 500, 4000, 4000, SmallLaptopWorkArea);

        Assert.Equal(SmallLaptopWorkArea.Left, left);
        Assert.Equal(SmallLaptopWorkArea.Top, top);
    }

    [Fact]
    public void ClampPosition_PullsBackAWindowPushedOffTheRightEdgeByItsOwner()
    {
        // About and Preview use CenterOwner, and their owner is the charm overlay, which lives
        // against the top-right screen edge.
        var (left, _) = WindowSizing.ClampPosition(1000, 10, 460, 400, SmallLaptopWorkArea);

        Assert.True(left + 460 <= SmallLaptopWorkArea.Right, "window must end inside the work area");
    }

    [Fact]
    public void ClampSize_IgnoresAnUnsetSize()
    {
        // Width/Height default to NaN on a window that sizes to content; that must pass through
        // untouched rather than becoming a number.
        var (w, h) = WindowSizing.ClampSize(double.NaN, double.NaN, SmallLaptopWorkArea);

        Assert.True(double.IsNaN(w));
        Assert.True(double.IsNaN(h));
    }
}
