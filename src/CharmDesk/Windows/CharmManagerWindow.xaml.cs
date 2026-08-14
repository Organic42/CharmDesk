using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CharmDesk.Core;
using CharmDesk.Core.Interaction;
using CharmDesk.Core.Models;
using CharmDesk.Persistence;
using Microsoft.Win32;

namespace CharmDesk.Windows;

/// <summary>Local Charm Manager / admin panel: import new charm packages or edit existing
/// ones, with a live physics preview, without ever touching engine source code.</summary>
public partial class CharmManagerWindow : Window
{
    private readonly App _app;
    private readonly CharmPackage? _editing;

    private string? _selectedImagePath;
    private string? _selectedThumbPath;

    private readonly PhysicsEngine _previewEngine;
    private readonly InteractionSystem _previewInteraction;
    private DispatcherTimer? _previewTimer;
    private readonly Stopwatch _clock = new();
    private double _lastTick;
    private double _previewDisplaySize = 120;
    private const double PreviewAttachFractionY = 0.11;

    public CharmManagerWindow(App app, CharmPackage? editing = null)
    {
        _app = app;
        _editing = editing;

        var physics = new CharmPhysicsSettings();
        _previewEngine = new PhysicsEngine(physics) { AnchorX = 133, AnchorY = 20 };
        _previewInteraction = new InteractionSystem(_previewEngine);

        InitializeComponent();
        Closed += (_, _) => _previewTimer?.Stop();

        PreviewCanvas.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(PreviewCanvas);
            _previewInteraction.OnMouseMove(p.X, p.Y);
            EnsurePreviewTimerRunning();
        };
        PreviewCanvas.MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(PreviewCanvas);
            if (_previewInteraction.TryBeginGrab(p.X, p.Y))
            {
                PreviewCanvas.CaptureMouse();
                EnsurePreviewTimerRunning();
            }
        };
        PreviewCanvas.MouseLeftButtonUp += (_, _) =>
        {
            if (_previewInteraction.IsGrabbing)
            {
                PreviewCanvas.ReleaseMouseCapture();
                _previewInteraction.EndGrab();
                EnsurePreviewTimerRunning();
            }
        };

        if (editing is not null)
        {
            HeaderText.Text = "Edit Charm";
            Title = $"Charm Manager - {editing.Manifest.Name}";
            DeleteButton.Visibility = Visibility.Visible;

            NameBox.Text = editing.Manifest.Name;
            DescriptionBox.Text = editing.Manifest.Description;
            CategoryBox.Text = editing.Manifest.Category;
            EnabledCheck.IsChecked = editing.Manifest.Enabled;
            DisplayScaleSlider.Value = editing.Manifest.DisplayScale;
            StringLengthSlider.Value = editing.Manifest.Physics.StringLength;
            GravitySlider.Value = editing.Manifest.Physics.Gravity;
            DampingSlider.Value = editing.Manifest.Physics.Damping;
            StiffnessSlider.Value = editing.Manifest.Physics.Stiffness;
            MassSlider.Value = editing.Manifest.Physics.Mass;

            _selectedImagePath = editing.ImagePath;
            _selectedThumbPath = File.Exists(editing.ThumbnailPath) ? editing.ThumbnailPath : null;
        }
        else
        {
            Title = "Charm Manager - Import Charm";
        }

        ApplyPhysicsFromSliders();
        UpdateAllValueLabels();
        LoadPreviewImageIfAny();
    }

    // ---- Preview -----------------------------------------------------

    private void LoadPreviewImageIfAny()
    {
        if (string.IsNullOrEmpty(_selectedImagePath) || !File.Exists(_selectedImagePath))
        {
            PngPathText.Text = "No file selected";
            return;
        }

        PngPathText.Text = Path.GetFileName(_selectedImagePath);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(_selectedImagePath, UriKind.Absolute);
        bmp.EndInit();
        PreviewImage.Source = bmp;

        var aspect = bmp.PixelWidth > 0 ? (double)bmp.PixelHeight / bmp.PixelWidth : 1.0;
        _previewDisplaySize = 60 * Math.Clamp(DisplayScaleSlider.Value, 0.3, 3.0);
        PreviewImage.Width = _previewDisplaySize;
        PreviewImage.Height = _previewDisplaySize * aspect;
        _previewInteraction.HitRadius = Math.Max(PreviewImage.Width, PreviewImage.Height) * 0.55;

        RenderPreview();
    }

    private void EnsurePreviewTimerRunning()
    {
        if (_previewTimer is null)
        {
            _previewTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _previewTimer.Tick += (_, _) =>
            {
                var now = _clock.Elapsed.TotalSeconds;
                var dt = now - _lastTick;
                _lastTick = now;
                _previewEngine.Step(dt);
                RenderPreview();
                if (_previewEngine.IsAtRest && !_previewInteraction.IsGrabbing)
                    _previewTimer!.Stop();
            };
        }
        if (!_previewTimer.IsEnabled)
        {
            _clock.Restart();
            _lastTick = 0;
            _previewTimer.Start();
        }
    }

    private void RenderPreview()
    {
        var bobX = _previewEngine.BobX;
        var bobY = _previewEngine.BobY;
        var h = PreviewImage.Height > 0 ? PreviewImage.Height : _previewDisplaySize;
        var attachY = bobY - h / 2 + h * PreviewAttachFractionY;

        Canvas.SetLeft(PreviewImage, bobX - PreviewImage.Width / 2);
        Canvas.SetTop(PreviewImage, bobY - h / 2);
        PreviewSpin.Angle = _previewEngine.Spin * 180.0 / Math.PI;

        Canvas.SetLeft(PreviewAnchor, _previewEngine.AnchorX - PreviewAnchor.Width / 2);
        Canvas.SetTop(PreviewAnchor, _previewEngine.AnchorY - PreviewAnchor.Height / 2);

        var start = new Point(_previewEngine.AnchorX, _previewEngine.AnchorY);
        var end = new Point(bobX, attachY);
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new LineSegment(end, true));
        PreviewString.Data = new PathGeometry(new[] { figure });
    }

    private void PhysicsTestButton_Click(object sender, RoutedEventArgs e)
    {
        _previewEngine.Nudge(0.5);
        EnsurePreviewTimerRunning();
    }

    // ---- File pickers --------------------------------------------------

    private void SelectPngButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "PNG image (*.png)|*.png", Title = "Select charm image" };
        if (dlg.ShowDialog(this) == true)
        {
            _selectedImagePath = dlg.FileName;
            LoadPreviewImageIfAny();
        }
    }

    private void SelectThumbButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "PNG image (*.png)|*.png", Title = "Select thumbnail image" };
        if (dlg.ShowDialog(this) == true)
        {
            _selectedThumbPath = dlg.FileName;
            ThumbPathText.Text = Path.GetFileName(_selectedThumbPath);
        }
    }

    // ---- Sliders ---------------------------------------------------------

    private void DisplayScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DisplayScaleValue is null) return;
        DisplayScaleValue.Text = e.NewValue.ToString("0.00");
        LoadPreviewImageIfAny();
    }

    private void PhysicsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ApplyPhysicsFromSliders();
        UpdateAllValueLabels();
    }

    private void ApplyPhysicsFromSliders()
    {
        if (StringLengthSlider is null) return;
        _previewEngine.Settings.StringLength = StringLengthSlider.Value;
        _previewEngine.Settings.Gravity = GravitySlider.Value;
        _previewEngine.Settings.Damping = DampingSlider.Value;
        _previewEngine.Settings.Stiffness = StiffnessSlider.Value;
        _previewEngine.Settings.Mass = MassSlider.Value;
    }

    private void UpdateAllValueLabels()
    {
        if (DisplayScaleValue is null) return;
        DisplayScaleValue.Text = DisplayScaleSlider.Value.ToString("0.00");
        StringLengthValue.Text = StringLengthSlider.Value.ToString("0");
        GravityValue.Text = GravitySlider.Value.ToString("0");
        DampingValue.Text = DampingSlider.Value.ToString("0.00");
        StiffnessValue.Text = StiffnessSlider.Value.ToString("0.00");
        MassValue.Text = MassSlider.Value.ToString("0.0");
    }

    // ---- Save / Delete -----------------------------------------------------

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "Please enter a name for this charm.", "CharmDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(_selectedImagePath) || !File.Exists(_selectedImagePath))
        {
            MessageBox.Show(this, "Please select a transparent PNG for this charm.", "CharmDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var manifest = _editing?.Manifest ?? new CharmManifest();
        manifest.Name = NameBox.Text.Trim();
        manifest.Description = DescriptionBox.Text.Trim();
        manifest.Category = string.IsNullOrWhiteSpace(CategoryBox.Text) ? "Uncategorized" : CategoryBox.Text.Trim();
        manifest.Enabled = EnabledCheck.IsChecked == true;
        manifest.DisplayScale = DisplayScaleSlider.Value;
        manifest.Physics.StringLength = StringLengthSlider.Value;
        manifest.Physics.Gravity = GravitySlider.Value;
        manifest.Physics.Damping = DampingSlider.Value;
        manifest.Physics.Stiffness = StiffnessSlider.Value;
        manifest.Physics.Mass = MassSlider.Value;

        try
        {
            if (_editing is null)
            {
                manifest.Id = GenerateUniqueId(manifest.Name);
                _app.Registry.Import(manifest, _selectedImagePath, _selectedThumbPath);
            }
            else
            {
                var package = _editing;
                if (!string.Equals(_selectedImagePath, package.ImagePath, StringComparison.OrdinalIgnoreCase))
                {
                    manifest.Image = "charm" + Path.GetExtension(_selectedImagePath);
                    File.Copy(_selectedImagePath, Path.Combine(package.Directory, manifest.Image), overwrite: true);
                }
                if (!string.IsNullOrEmpty(_selectedThumbPath) &&
                    !string.Equals(_selectedThumbPath, package.ThumbnailPath, StringComparison.OrdinalIgnoreCase))
                {
                    manifest.Thumbnail = "thumbnail" + Path.GetExtension(_selectedThumbPath);
                    File.Copy(_selectedThumbPath, Path.Combine(package.Directory, manifest.Thumbnail), overwrite: true);
                }
                _app.Registry.Save(package);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log("CharmManagerWindow.Save", ex);
            MessageBox.Show(this, $"Couldn't save this charm: {ex.Message}", "CharmDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _app.RefreshAfterLibraryChange();
        _app.ApplySettingsToCharm();
        Close();
    }

    private string GenerateUniqueId(string name)
    {
        var slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        if (string.IsNullOrEmpty(slug)) slug = "charm";

        var candidate = slug;
        var n = 1;
        while (_app.Registry.Find(candidate) is not null)
            candidate = $"{slug}-{++n}";
        return candidate;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        var result = MessageBox.Show(this, $"Delete '{_editing.Manifest.Name}'? This cannot be undone.",
            "CharmDesk", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            _app.Registry.Delete(_editing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log("CharmManagerWindow.Delete", ex);
            MessageBox.Show(this, $"Couldn't delete this charm: {ex.Message}", "CharmDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _app.RefreshAfterLibraryChange();
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
