using System;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using CharmDesk.Native;
using CharmDesk.Persistence;

namespace CharmDesk.Windows;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private AppSettings Settings => _app.Settings.Current;
    private bool _initializing = true;

    private sealed record CharmComboItem(string Id, string Name);
    private sealed record MonitorComboItem(string? DeviceName, string Label);

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();

        StartWithWindowsCheck.IsChecked = Settings.StartWithWindows;
        AlwaysOnTopCheck.IsChecked = Settings.AlwaysOnTop;
        SoundEffectsCheck.IsChecked = Settings.SoundEffectsEnabled;
        EnablePhysicsCheck.IsChecked = Settings.EnablePhysics;

        IntensitySlider.Value = Settings.PhysicsIntensity;
        IntensityValue.Text = Settings.PhysicsIntensity.ToString("0.00");
        ScaleSlider.Value = Settings.CharmScale;
        ScaleValue.Text = Settings.CharmScale.ToString("0.00");

        var charms = _app.Registry.LoadAll()
            .Where(c => c.Manifest.Enabled)
            .Select(c => new CharmComboItem(c.Manifest.Id, c.Manifest.Name))
            .ToList();
        DefaultCharmCombo.ItemsSource = charms;
        DefaultCharmCombo.SelectedItem = charms.FirstOrDefault(c =>
            string.Equals(c.Id, Settings.DefaultCharmId, StringComparison.OrdinalIgnoreCase)) ?? charms.FirstOrDefault();

        var monitors = Screen.AllScreens.Select((s, i) =>
            new MonitorComboItem(s.DeviceName,
                $"Monitor {i + 1}{(s.Primary ? " (Primary)" : "")} - {s.Bounds.Width}x{s.Bounds.Height}")).ToList();
        MonitorCombo.ItemsSource = monitors;
        MonitorCombo.SelectedItem = monitors.FirstOrDefault(m => m.DeviceName == Settings.MonitorDeviceName) ?? monitors.FirstOrDefault();

        _initializing = false;
    }

    private async void OnAnyChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;

        Settings.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        Settings.SoundEffectsEnabled = SoundEffectsCheck.IsChecked == true;
        Settings.EnablePhysics = EnablePhysicsCheck.IsChecked == true;

        var wantStartup = StartWithWindowsCheck.IsChecked == true;
        if (wantStartup != Settings.StartWithWindows)
        {
            var state = await StartupManager.SetEnabledAsync(wantStartup);
            Settings.StartWithWindows = state == StartupState.Enabled;

            // Windows can refuse: once someone turns the app off in Startup Apps / Task Manager,
            // that choice sticks and the app can't override it. Say so instead of leaving a
            // checkbox that silently snaps back.
            if (wantStartup && state is StartupState.DisabledByUser or StartupState.DisabledByPolicy)
            {
                var reason = state == StartupState.DisabledByUser
                    ? "CharmDesk was turned off in Windows' Startup Apps settings, so it can't re-enable itself.\n\nTurn it back on there (Settings > Apps > Startup) to start CharmDesk with Windows."
                    : "Starting with Windows is blocked by your organization's policy.";
                MessageBox.Show(this, reason, "CharmDesk", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            _initializing = true;
            StartWithWindowsCheck.IsChecked = Settings.StartWithWindows;
            _initializing = false;
        }

        Persist();
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializing) return;
        IntensityValue.Text = IntensitySlider.Value.ToString("0.00");
        ScaleValue.Text = ScaleSlider.Value.ToString("0.00");
        Settings.PhysicsIntensity = IntensitySlider.Value;
        Settings.CharmScale = ScaleSlider.Value;
        Persist(reflow: true);
    }

    private void DefaultCharmCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (DefaultCharmCombo.SelectedItem is CharmComboItem item)
        {
            Settings.DefaultCharmId = item.Id;
            Persist();
        }
    }

    private void MonitorCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (MonitorCombo.SelectedItem is MonitorComboItem item)
        {
            Settings.MonitorDeviceName = item.DeviceName;
            Persist(reflow: true);
        }
    }

    private void ResetPositionButton_Click(object sender, RoutedEventArgs e) => _app.ResetActiveCharmPosition();

    private void Persist(bool reflow = false)
    {
        _app.Settings.Save();
        _app.ApplySettingsToCharm();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
