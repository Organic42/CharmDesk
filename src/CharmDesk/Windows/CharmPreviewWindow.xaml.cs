using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CharmDesk.Core;
using CharmDesk.Core.Interaction;
using CharmDesk.Core.Models;

namespace CharmDesk.Windows;

/// <summary>Standalone draggable physics playground for a single charm package - the "Preview"
/// action on a Charm Library card.</summary>
public partial class CharmPreviewWindow : Window
{
    private readonly PhysicsEngine _engine;
    private readonly InteractionSystem _interaction;
    private DispatcherTimer? _timer;
    private readonly Stopwatch _clock = new();
    private double _lastTick;
    private double _displayHeight = 130;
    private const double AttachFractionY = 0.11;

    public CharmPreviewWindow(CharmPackage package)
    {
        var physics = new CharmPhysicsSettings
        {
            StringLength = package.Manifest.Physics.StringLength,
            Gravity = package.Manifest.Physics.Gravity,
            Damping = package.Manifest.Physics.Damping,
            Stiffness = package.Manifest.Physics.Stiffness,
            Mass = package.Manifest.Physics.Mass,
        };
        _engine = new PhysicsEngine(physics) { AnchorX = 145, AnchorY = 20 };
        _interaction = new InteractionSystem(_engine);

        InitializeComponent();
        TitleText.Text = package.Manifest.Name;

        BitmapImage? bmp = null;
        try
        {
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(package.ImagePath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or System.IO.FileFormatException)
        {
            CharmDesk.Persistence.Logger.Log($"CharmPreviewWindow ({package.ImagePath})", ex);
            bmp = null;
        }
        PreviewImage.Source = bmp;

        var aspect = bmp is { PixelWidth: > 0 } ? (double)bmp.PixelHeight / bmp.PixelWidth : 1.0;
        var width = 70 * Math.Clamp(package.Manifest.DisplayScale, 0.3, 3.0);
        PreviewImage.Width = width;
        _displayHeight = width * aspect;
        PreviewImage.Height = _displayHeight;
        _interaction.HitRadius = Math.Max(width, _displayHeight) * 0.55;

        PreviewCanvas.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(PreviewCanvas);
            _interaction.OnMouseMove(p.X, p.Y);
            EnsureTimerRunning();
        };
        PreviewCanvas.MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(PreviewCanvas);
            if (_interaction.TryBeginGrab(p.X, p.Y))
            {
                PreviewCanvas.CaptureMouse();
                EnsureTimerRunning();
            }
        };
        PreviewCanvas.MouseLeftButtonUp += (_, _) =>
        {
            if (_interaction.IsGrabbing)
            {
                PreviewCanvas.ReleaseMouseCapture();
                _interaction.EndGrab();
                EnsureTimerRunning();
            }
        };

        _engine.Nudge(0.4);
        RenderFrame();
        EnsureTimerRunning();
    }

    private void EnsureTimerRunning()
    {
        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) =>
            {
                var now = _clock.Elapsed.TotalSeconds;
                var dt = now - _lastTick;
                _lastTick = now;
                _engine.Step(dt);
                RenderFrame();
                if (_engine.IsAtRest && !_interaction.IsGrabbing)
                    _timer!.Stop();
            };
        }
        if (!_timer.IsEnabled)
        {
            _clock.Restart();
            _lastTick = 0;
            _timer.Start();
        }
    }

    private void RenderFrame()
    {
        var bobX = _engine.BobX;
        var bobY = _engine.BobY;
        var attachY = bobY - _displayHeight / 2 + _displayHeight * AttachFractionY;

        Canvas.SetLeft(PreviewImage, bobX - PreviewImage.Width / 2);
        Canvas.SetTop(PreviewImage, bobY - _displayHeight / 2);
        PreviewSpin.Angle = _engine.Spin * 180.0 / Math.PI;

        Canvas.SetLeft(PreviewAnchor, _engine.AnchorX - PreviewAnchor.Width / 2);
        Canvas.SetTop(PreviewAnchor, _engine.AnchorY - PreviewAnchor.Height / 2);

        var start = new Point(_engine.AnchorX, _engine.AnchorY);
        var end = new Point(bobX, attachY);
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new LineSegment(end, true));
        PreviewString.Data = new PathGeometry(new[] { figure });
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();
        base.OnClosed(e);
    }
}
