using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CharmDesk.Core;
using CharmDesk.Core.Interaction;
using CharmDesk.Core.Models;
using CharmDesk.Native;
using CharmDesk.Persistence;
using CharmDesk.Tray;

namespace CharmDesk.Windows;

/// <summary>
/// The desktop overlay window for a single active charm: transparent, borderless, topmost,
/// and click-through everywhere except the small circular regions covering the charm sprite
/// and its anchor handle - so it never blocks clicks on the desktop or apps beneath it.
/// </summary>
public partial class CharmWindow : Window
{
    private const double AttachPointFractionY = 0.11;
    private const double AnchorHitRadius = 14;
    private const int WM_DPICHANGED = 0x02E0;

    private readonly CharmPackage _package;
    private readonly SettingsManager _settingsManager;
    private AppSettings Settings => _settingsManager.Current;

    private readonly PhysicsEngine _engine;
    private readonly InteractionSystem _interaction;

    private DispatcherTimer? _timer;
    private readonly Stopwatch _clock = new();
    private double _lastTick;

    /// <summary>Ticks once a second to keep a live ClockFace charm's readout current. Kept
    /// entirely separate from the 60fps physics timer above: it has to keep running even while
    /// the charm is at rest and that timer has stopped, and a once-a-second string update is
    /// cheap enough to just always run rather than coupling it to render/idle state.</summary>
    private DispatcherTimer? _clockTimer;

    private IntPtr _hwnd = IntPtr.Zero;
    private double _localAnchorX;
    private double _localAnchorY;
    private double _displayWidth = 96;
    private double _displayHeight = 96;
    private double _attachOffsetPixels;

    private bool _anchorDragging;
    private double _anchorDragStartScreenX;
    private double _anchorDragStartLeft;

    private Point _mouseDownPos;
    private int _mouseDownClickCount;
    private bool? _clickThroughState;

    private GlobalMouseHook? _mouseHook;
    private double _dpiScale = 1.0;

    private DispatcherTimer? _idleTimer;
    private readonly Random _idleRng = new();

    private PathFigure? _stringFigure;
    private QuadraticBezierSegment? _stringSegment;

    private readonly TrayIconManager _tray;

    public CharmWindow(CharmPackage package, SettingsManager settingsManager, TrayIconManager tray)
    {
        _package = package;
        _settingsManager = settingsManager;
        _tray = tray;

        var physics = new CharmPhysicsSettings
        {
            StringLength = Math.Max(20, package.Manifest.Physics.StringLength * ScaleFactor()),
            Gravity = package.Manifest.Physics.Gravity,
            Damping = package.Manifest.Physics.Damping,
            Stiffness = package.Manifest.Physics.Stiffness,
            Mass = package.Manifest.Physics.Mass,
        };
        _engine = new PhysicsEngine(physics)
        {
            Intensity = Settings.PhysicsIntensity,
            Enabled = Settings.EnablePhysics,
        };
        _interaction = new InteractionSystem(_engine,
            new DefaultCharmBehavior(package.Manifest.ReactionStyle, () => Settings.SoundEffectsEnabled));
        _interaction.HoverChanged += hovering => EnsureTimerRunning();

        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private double ScaleFactor() =>
        Math.Clamp(_package.Manifest.DisplayScale * Settings.CharmScale, 0.3, 3.0);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ApplyOverlayWindowStyles(_hwnd);
        NativeMethods.SetClickThrough(_hwnd, true);
        _clickThroughState = true;

        var src = HwndSource.FromHwnd(_hwnd);
        src?.AddHook(WndProc);

        LayoutAndPosition();

        // WS_EX_TRANSPARENT excludes this window from mouse hit-testing entirely, always -
        // not just over transparent pixels. That means once it goes click-through, WPF never
        // gets another mouse message here to notice the cursor has come back over the charm.
        // This hook is the only thing that can see the cursor while we're click-through, purely
        // so it can flip click-through off; every other interaction runs through normal WPF
        // input once that happens.
        _mouseHook = new GlobalMouseHook();
        _mouseHook.MouseMoved += OnGlobalMouseMoved;
        _mouseHook.Start();
        Closed += (_, _) =>
        {
            _mouseHook?.Dispose();
            _timer?.Stop();
            _idleTimer?.Stop();
            _clockTimer?.Stop();
        };
    }

    /// <summary>An occasional small nudge so the charm never looks like a static image over a
    /// long idle stretch. Reschedules itself on a randomized interval - deliberately a separate,
    /// rarely-firing timer rather than folding into the render loop, so it costs essentially
    /// nothing while the charm is just hanging there (a Tick every 20-40s, doing a couple of
    /// field checks, is negligible compared to running the 60fps render timer continuously).</summary>
    private void ScheduleIdleFlourish()
    {
        _idleTimer ??= new DispatcherTimer();
        _idleTimer.Stop();
        _idleTimer.Tick -= OnIdleFlourishTick;
        _idleTimer.Tick += OnIdleFlourishTick;
        _idleTimer.Interval = TimeSpan.FromSeconds(20 + _idleRng.NextDouble() * 20);
        _idleTimer.Start();
    }

    private void OnIdleFlourishTick(object? sender, EventArgs e)
    {
        if (IsVisible && _engine.IsAtRest && !_interaction.IsGrabbing && !_anchorDragging)
        {
            _engine.Nudge((_idleRng.NextDouble() - 0.5) * 0.09);
            EnsureTimerRunning();
        }
        ScheduleIdleFlourish();
    }

    private void OnGlobalMouseMoved(int screenX, int screenY)
    {
        if (_clickThroughState != true) return; // WPF's own input already covers this case

        var localX = screenX / _dpiScale - Left;
        var localY = screenY / _dpiScale - Top;

        if (_interaction.HitTest(localX, localY) || AnchorHitTest(new Point(localX, localY)))
        {
            SetClickThrough(false);
            Cursor = Cursors.Hand;
            _interaction.OnMouseMove(localX, localY);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DPICHANGED)
        {
            LayoutAndPosition();
        }
        return IntPtr.Zero;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        LoadCharmImage();
        RootCanvas.MouseMove += OnMouseMove;
        RootCanvas.MouseLeftButtonDown += OnMouseDown;
        RootCanvas.MouseLeftButtonUp += OnMouseUp;
        RootCanvas.MouseRightButtonDown += OnMouseRightButtonDown;
        RootCanvas.MouseWheel += OnMouseWheel;
        RootCanvas.LostMouseCapture += (_, _) => { _anchorDragging = false; };

        // A tiny arrival nudge so the charm doesn't look like a static image on launch.
        _engine.Nudge(0.22);
        RenderFrame();
        EnsureTimerRunning();
        ScheduleIdleFlourish();
    }

    private void LoadCharmImage()
    {
        var bmp = ImageLoader.TryLoad(_package.ImagePath, "CharmWindow.LoadCharmImage");
        CharmImage.Source = bmp;

        var scale = ScaleFactor();
        const double baseline = 96.0;
        var pixelWidth = bmp is { PixelWidth: > 0 } ? bmp.PixelWidth : 1;
        var aspect = bmp is { PixelWidth: > 0 } ? (double)bmp.PixelHeight / bmp.PixelWidth : 1.0;

        _displayWidth = baseline * scale;
        _displayHeight = _displayWidth * aspect;
        CharmVisual.Width = _displayWidth;
        CharmVisual.Height = _displayHeight;
        CharmImage.Width = _displayWidth;
        CharmImage.Height = _displayHeight;
        _attachOffsetPixels = _displayHeight * AttachPointFractionY;

        _interaction.HitRadius = Math.Max(_displayWidth, _displayHeight) * 0.5;

        // ClockFace coordinates are authored in the charm's native source-image pixels, so they
        // scale by the same factor the image itself was just scaled by - stays correctly
        // positioned at any DisplayScale/Charm Scale without the manifest needing to know either.
        SetupClockFace(_displayWidth / pixelWidth);
    }

    /// <summary>Positions and starts (or stops) the live digital time readout for charms whose
    /// manifest declares a ClockFace. A no-op that leaves the text collapsed for every ordinary
    /// static-image charm. Each line sits in its own Viewbox, which scales the text to fill
    /// whatever box it's given - so a new/wider font never overflows the screen bezel the way a
    /// hand-picked FontSize ratio would (that's exactly what broke switching fonts once already).</summary>
    private void SetupClockFace(double pixelScale)
    {
        var digital = _package.Manifest.ClockFace?.Digital;
        if (digital is null)
        {
            ClockTimeBox.Visibility = Visibility.Collapsed;
            ClockDateBox.Visibility = Visibility.Collapsed;
            _clockTimer?.Stop();
            return;
        }

        // A small horizontal inset so text never touches the inner bezel edge.
        var boxLeft = digital.X * pixelScale + digital.Width * pixelScale * 0.06;
        var boxTop = digital.Y * pixelScale;
        var boxWidth = digital.Width * pixelScale * 0.88;
        var boxHeight = digital.Height * pixelScale;

        ClockTimeText.Foreground = ParseBrush(digital.TimeColor);
        ClockTimeBox.Visibility = Visibility.Visible;

        ClockDateBox.Visibility = digital.ShowDate ? Visibility.Visible : Visibility.Collapsed;
        if (digital.ShowDate)
        {
            // Balanced top/gap/bottom margins around the two lines rather than an even split,
            // which left a lot of dead space below the date line.
            Canvas.SetLeft(ClockTimeBox, boxLeft);
            Canvas.SetTop(ClockTimeBox, boxTop + boxHeight * 0.08);
            ClockTimeBox.Width = boxWidth;
            ClockTimeBox.Height = boxHeight * 0.42;

            Canvas.SetLeft(ClockDateBox, boxLeft);
            Canvas.SetTop(ClockDateBox, boxTop + boxHeight * 0.56);
            ClockDateBox.Width = boxWidth;
            ClockDateBox.Height = boxHeight * 0.30;
            ClockDateText.Foreground = ParseBrush(digital.DateColor);
        }
        else
        {
            Canvas.SetLeft(ClockTimeBox, boxLeft);
            Canvas.SetTop(ClockTimeBox, boxTop + boxHeight * 0.25);
            ClockTimeBox.Width = boxWidth;
            ClockTimeBox.Height = boxHeight * 0.5;
        }

        UpdateClockText();
        _clockTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick -= OnClockTick;
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();
    }

    private void OnClockTick(object? sender, EventArgs e) => UpdateClockText();

    private void UpdateClockText()
    {
        var now = DateTime.Now;
        ClockTimeText.Text = now.ToString(Settings.Use24HourClock ? "HH:mm" : "h:mm tt");
        if (ClockDateText.Visibility == Visibility.Visible)
            ClockDateText.Text = now.ToString("ddd d").ToUpperInvariant();
    }

    private static SolidColorBrush ParseBrush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch (FormatException) { return new SolidColorBrush(Colors.White); }
    }

    /// <summary>Sizes the overlay to fit the full swing arc and places it over the configured
    /// monitor so the local anchor point lands at the saved top-edge fraction.</summary>
    private void LayoutAndPosition()
    {
        var screen = MonitorHelper.FindByDeviceName(Settings.MonitorDeviceName) ?? MonitorHelper.PrimaryScreen;
        var dpiScale = _hwnd == IntPtr.Zero ? 1.0 : MonitorHelper.GetDpiScale(_hwnd);
        _dpiScale = dpiScale;
        var boundsDip = MonitorHelper.BoundsToDip(screen.Bounds, dpiScale);

        var maxRadius = _engine.Settings.StringLength * 1.4;
        var windowWidth = 2 * maxRadius + _displayWidth + 40;
        var windowHeight = Settings.AnchorTopMargin + maxRadius + _displayHeight + 60;

        Width = windowWidth;
        Height = windowHeight;

        _localAnchorX = windowWidth / 2;
        _localAnchorY = Settings.AnchorTopMargin + 6;
        _engine.AnchorX = _localAnchorX;
        _engine.AnchorY = _localAnchorY;

        var desiredAnchorScreenX = boundsDip.Left + boundsDip.Width * Settings.AnchorXFraction;
        var left = desiredAnchorScreenX - _localAnchorX;
        var minLeft = boundsDip.Left - maxRadius;
        var maxLeft = boundsDip.Left + boundsDip.Width - windowWidth + maxRadius;
        Left = Math.Clamp(left, Math.Min(minLeft, maxLeft), Math.Max(minLeft, maxLeft));
        Top = boundsDip.Top;

        RenderFrame();
    }

    // ---- Input ---------------------------------------------------------

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(RootCanvas);

        if (_anchorDragging)
        {
            UpdateAnchorDrag();
            return;
        }

        _interaction.OnMouseMove(pos.X, pos.Y);

        if (!_interaction.IsGrabbing)
        {
            var overAnchor = AnchorHitTest(pos);
            var shouldCapture = _interaction.IsHovering || overAnchor;
            SetClickThrough(!shouldCapture);
            Cursor = shouldCapture ? Cursors.Hand : Cursors.Arrow;
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(RootCanvas);
        _mouseDownPos = pos;
        _mouseDownClickCount = e.ClickCount;

        if (AnchorHitTest(pos))
        {
            _anchorDragging = true;
            RootCanvas.CaptureMouse();
            SetClickThrough(false);
            _anchorDragStartScreenX = PointToScreenX(e);
            _anchorDragStartLeft = Left;
            e.Handled = true;
            return;
        }

        if (_interaction.TryBeginGrab(pos.X, pos.Y))
        {
            RootCanvas.CaptureMouse();
            SetClickThrough(false);
            EnsureTimerRunning();
            e.Handled = true;
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_anchorDragging)
        {
            _anchorDragging = false;
            RootCanvas.ReleaseMouseCapture();
            PersistAnchorPosition();
            return;
        }

        if (_interaction.IsGrabbing)
        {
            var pos = e.GetPosition(RootCanvas);
            var moved = (pos - _mouseDownPos).Length;
            RootCanvas.ReleaseMouseCapture();
            _interaction.EndGrab();
            if (moved < 6)
                _interaction.RegisterClick(_mouseDownClickCount);
            EnsureTimerRunning();
        }
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(RootCanvas);
        if (!_interaction.HitTest(pos.X, pos.Y) && !AnchorHitTest(pos))
            return;

        // Cancel any in-progress grab before handing off to the menu - Show() below pumps its
        // own modal loop, so we'd otherwise never see the matching mouse-up.
        if (_interaction.IsGrabbing)
        {
            RootCanvas.ReleaseMouseCapture();
            _interaction.EndGrab();
        }

        var screenPoint = PointToScreen(e.GetPosition(this));
        _tray.ShowContextMenu(new System.Drawing.Point((int)screenPoint.X, (int)screenPoint.Y));
        e.Handled = true;
    }

    /// <summary>Scroll-to-resize while hovering the charm - only reachable once the window is
    /// already non-transparent, i.e. the cursor is confirmed over the charm.</summary>
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(RootCanvas);
        if (!_interaction.HitTest(pos.X, pos.Y)) return;

        var step = 0.06 * Math.Sign(e.Delta);
        Settings.CharmScale = Math.Clamp(Settings.CharmScale + step, 0.4, 2.5);
        _settingsManager.Save();
        ApplySettingsChanged();
        e.Handled = true;
    }

    private double PointToScreenX(MouseButtonEventArgs e) => PointToScreen(e.GetPosition(this)).X;

    private bool AnchorHitTest(Point local)
    {
        var dx = local.X - _localAnchorX;
        var dy = local.Y - _localAnchorY;
        return dx * dx + dy * dy <= AnchorHitRadius * AnchorHitRadius;
    }

    private void UpdateAnchorDrag()
    {
        var screenX = PointToScreen(Mouse.GetPosition(this)).X;
        var dx = screenX - _anchorDragStartScreenX;
        var screen = MonitorHelper.FindByDeviceName(Settings.MonitorDeviceName) ?? MonitorHelper.PrimaryScreen;
        var dpiScale = MonitorHelper.GetDpiScale(_hwnd);
        var boundsDip = MonitorHelper.BoundsToDip(screen.Bounds, dpiScale);

        var newLeft = _anchorDragStartLeft + dx;
        var min = boundsDip.Left - (_engine.Settings.StringLength * 1.4);
        var max = boundsDip.Left + boundsDip.Width - Width + (_engine.Settings.StringLength * 1.4);
        Left = Math.Clamp(newLeft, Math.Min(min, max), Math.Max(min, max));
    }

    private void PersistAnchorPosition()
    {
        var screen = MonitorHelper.FindByDeviceName(Settings.MonitorDeviceName) ?? MonitorHelper.PrimaryScreen;
        var dpiScale = MonitorHelper.GetDpiScale(_hwnd);
        var boundsDip = MonitorHelper.BoundsToDip(screen.Bounds, dpiScale);

        var anchorScreenX = Left + _localAnchorX;
        var fraction = boundsDip.Width > 0 ? (anchorScreenX - boundsDip.Left) / boundsDip.Width : Settings.AnchorXFraction;
        Settings.AnchorXFraction = Math.Clamp(fraction, 0.0, 1.0);
        _settingsManager.Save();
    }

    private void SetClickThrough(bool clickThrough)
    {
        if (_clickThroughState == clickThrough) return;
        _clickThroughState = clickThrough;
        NativeMethods.SetClickThrough(_hwnd, clickThrough);
        // The hook only has a job to do while we're click-through (see GlobalMouseHook's
        // summary) - disabling it while hovering/dragging turns the busiest moment (mouse
        // moving a lot, right as the user grabs the charm) into the cheapest one.
        if (_mouseHook is not null) _mouseHook.Enabled = clickThrough;
    }

    // ---- Render loop -----------------------------------------------------

    private void EnsureTimerRunning()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += OnTick;
        }
        if (!_timer.IsEnabled)
        {
            _clock.Restart();
            _lastTick = 0;
            _timer.Start();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = now - _lastTick;
        _lastTick = now;

        _engine.Step(dt);
        RenderFrame();

        if (_engine.IsAtRest && !_interaction.IsGrabbing && !_anchorDragging)
        {
            _timer!.Stop();
        }
    }

    private void RenderFrame()
    {
        var bobX = _engine.BobX;
        var bobY = _engine.BobY;
        var attachY = bobY - _displayHeight / 2 + _attachOffsetPixels;

        Canvas.SetLeft(CharmVisual, bobX - _displayWidth / 2);
        Canvas.SetTop(CharmVisual, bobY - _displayHeight / 2);
        SpinTransform.Angle = _engine.Spin * 180.0 / Math.PI;

        Canvas.SetLeft(AnchorDot, _localAnchorX - AnchorDot.Width / 2);
        Canvas.SetTop(AnchorDot, _localAnchorY - AnchorDot.Height / 2);

        var start = new Point(_localAnchorX, _localAnchorY);
        var end = new Point(bobX, attachY);
        var mid = new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2);

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        var sag = Math.Clamp(_engine.ThetaVelocity * _engine.Radius * 0.02, -14, 14);
        var control = len > 0.001
            ? new Point(mid.X + (-dy / len) * sag, mid.Y + (dx / len) * sag)
            : mid;

        // The string geometry is rebuilt every rendered frame while the charm is moving (up to
        // 60 times/sec) - reusing one PathFigure/Segment/Geometry set and just moving its points
        // avoids allocating three new objects per frame for the GC to clean up later.
        if (_stringSegment is null)
        {
            _stringSegment = new QuadraticBezierSegment(control, end, true);
            _stringFigure = new PathFigure(start, new PathSegment[] { _stringSegment }, false);
            StringPath.Data = new PathGeometry(new[] { _stringFigure });
        }
        else
        {
            _stringFigure!.StartPoint = start;
            _stringSegment.Point1 = control;
            _stringSegment.Point2 = end;
        }
    }

    // ---- Public control surface (tray menu) ------------------------------

    public string CharmId => _package.Manifest.Id;

    public void ShowCharm()
    {
        Show();
        EnsureTimerRunning();
        ScheduleIdleFlourish();
        _clockTimer?.Start();
    }

    public void HideCharm()
    {
        _timer?.Stop();
        _idleTimer?.Stop();
        _clockTimer?.Stop();
        Hide();
    }

    public void ApplySettingsChanged()
    {
        _engine.Intensity = Settings.PhysicsIntensity;
        _engine.Enabled = Settings.EnablePhysics;
        Topmost = Settings.AlwaysOnTop;

        // Scale affects both the sprite size and the resting string length, so both the
        // image and the window's swing-arc bounds need to be recomputed.
        _engine.Settings.StringLength = Math.Max(20, _package.Manifest.Physics.StringLength * ScaleFactor());
        LoadCharmImage();
        LayoutAndPosition();

        EnsureTimerRunning();
    }

    public void ResetPosition()
    {
        Settings.AnchorXFraction = 0.92;
        _settingsManager.Save();
        LayoutAndPosition();
        _engine.Nudge(0.2);
        EnsureTimerRunning();
    }
}
