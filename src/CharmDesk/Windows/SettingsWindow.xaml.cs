using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using CharmDesk.Commerce;
using CharmDesk.Native;
using CharmDesk.Persistence;

namespace CharmDesk.Windows;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private AppSettings Settings => _app.Settings.Current;
    private bool _initializing = true;
    private bool _startupToggleInFlight;
    private readonly Dictionary<Button, TipTier> _tipButtonTiers = new();

    private sealed record CharmComboItem(string Id, string Name);
    private sealed record MonitorComboItem(string? DeviceName, string Label);

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        // Fixed design sizes overflow small laptop screens once display scaling is on.
        WindowSizing.KeepOnScreen(this);

        StartWithWindowsCheck.IsChecked = Settings.StartWithWindows;
        AlwaysOnTopCheck.IsChecked = Settings.AlwaysOnTop;
        SoundEffectsCheck.IsChecked = Settings.SoundEffectsEnabled;
        Use24HourClockCheck.IsChecked = Settings.Use24HourClock;
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

        InitializeTipJar();
    }

    private void InitializeTipJar()
    {
        _tipButtonTiers[TipSmallButton] = TipTier.Small;
        _tipButtonTiers[TipMediumButton] = TipTier.Medium;
        _tipButtonTiers[TipLargeButton] = TipTier.Large;

        foreach (var (button, tier) in _tipButtonTiers)
            button.Content = TipJarManager.DefaultLabel(tier);

        if (TipJarManager.IsNativePurchaseAvailable)
        {
            NativeTipPanel.Visibility = Visibility.Visible;
            ExternalTipButton.Visibility = Visibility.Collapsed;
            _ = LoadTipPricesAsync();
        }
        else
        {
            NativeTipPanel.Visibility = Visibility.Collapsed;
            ExternalTipButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Best-effort: if the Store can't be reached (offline, not associated with a
    /// Partner Center listing yet, placeholder Store IDs), the buttons just keep their default
    /// "Small/Medium/Large" labels with no price shown.</summary>
    private async Task LoadTipPricesAsync()
    {
        var products = await TipJarManager.GetTipProductsAsync();
        foreach (var product in products)
        {
            var button = _tipButtonTiers.FirstOrDefault(kv => kv.Value == product.Tier).Key;
            if (button is not null && !string.IsNullOrWhiteSpace(product.FormattedPrice))
                button.Content = $"{TipJarManager.DefaultLabel(product.Tier)} ({product.FormattedPrice})";
        }
    }

    private async void TipButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !_tipButtonTiers.TryGetValue(button, out var tier)) return;

        foreach (var b in _tipButtonTiers.Keys) b.IsEnabled = false;
        TipStatusText.Text = "Opening the Store checkout...";

        var result = await TipJarManager.RequestTipAsync(tier);
        TipStatusText.Text = result.Outcome switch
        {
            TipOutcome.Succeeded => result.Message ?? "Thank you!",
            TipOutcome.Cancelled => "No worries - maybe another time.",
            TipOutcome.NotAvailable => result.Message ?? "Native tipping isn't available on this build.",
            _ => result.Message ?? "That didn't go through - try again in a moment.",
        };

        foreach (var b in _tipButtonTiers.Keys) b.IsEnabled = true;
    }

    private void ExternalTipButton_Click(object sender, RoutedEventArgs e)
    {
        TipJarManager.OpenExternalTipPage();
        TipStatusText.Text = "Opened the donation page in your browser - thank you!";
    }

    private async void OnAnyChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;

        Settings.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        Settings.SoundEffectsEnabled = SoundEffectsCheck.IsChecked == true;
        Settings.Use24HourClock = Use24HourClockCheck.IsChecked == true;
        Settings.EnablePhysics = EnablePhysicsCheck.IsChecked == true;

        var wantStartup = StartWithWindowsCheck.IsChecked == true;
        if (wantStartup != Settings.StartWithWindows && !_startupToggleInFlight)
        {
            // Guards against a second toggle racing this one while the await below is in
            // flight - StartupManager.SetEnabledAsync can hit the Store StartupTask API or a
            // slow registry write, and without this two overlapping calls could both read/write
            // Settings.StartWithWindows and leave the checkbox out of sync with reality.
            _startupToggleInFlight = true;
            StartWithWindowsCheck.IsEnabled = false;
            try
            {
                var state = await StartupManager.SetEnabledAsync(wantStartup);
                Settings.StartWithWindows = state == StartupState.Enabled;

                // Windows can refuse: once someone turns the app off in Startup Apps / Task
                // Manager, that choice sticks and the app can't override it. A write can also
                // just fail outright (e.g. the Run key is locked down). Say so instead of
                // leaving a checkbox that silently snaps back with no explanation.
                if (wantStartup && state is StartupState.DisabledByUser or StartupState.DisabledByPolicy)
                {
                    var reason = state == StartupState.DisabledByUser
                        ? "CharmDesk was turned off in Windows' Startup Apps settings, so it can't re-enable itself.\n\nTurn it back on there (Settings > Apps > Startup) to start CharmDesk with Windows."
                        : "Starting with Windows is blocked by your organization's policy.";
                    MessageBox.Show(this, reason, "CharmDesk", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (wantStartup && state == StartupState.Disabled)
                {
                    MessageBox.Show(this,
                        "CharmDesk couldn't register itself to start with Windows. Nothing was changed.",
                        "CharmDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                _initializing = true;
                StartWithWindowsCheck.IsChecked = Settings.StartWithWindows;
                _initializing = false;
            }
            finally
            {
                StartWithWindowsCheck.IsEnabled = true;
                _startupToggleInFlight = false;
            }
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

    /// <summary>Puts version info and the tail of the local log file on the clipboard, entirely
    /// on-device - CharmDesk makes no network calls, so this is the only way diagnostics ever
    /// leave the machine, and only when the user chooses to paste it somewhere themselves.</summary>
    private void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            var plus = version.IndexOf('+');
            if (plus > 0) version = version[..plus];

            var logTail = "(no log entries)";
            var logFile = Path.Combine(_app.Settings.DataDirectory, "logs", "charmdesk.log");
            if (File.Exists(logFile))
            {
                var lines = File.ReadAllLines(logFile);
                if (lines.Length > 0)
                    logTail = string.Join(Environment.NewLine, lines.TakeLast(60));
            }

            var report =
                $"CharmDesk diagnostic info{Environment.NewLine}" +
                $"Version: {version}{Environment.NewLine}" +
                $"OS: {Environment.OSVersion.VersionString}{Environment.NewLine}" +
                $".NET: {Environment.Version}{Environment.NewLine}" +
                $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}" +
                $"Recent log:{Environment.NewLine}{logTail}";

            System.Windows.Clipboard.SetText(report);
            DiagnosticsStatusText.Text = "Copied to clipboard - paste it wherever you're reporting the issue.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Logger.Log("SettingsWindow.CopyDiagnosticsButton_Click", ex);
            DiagnosticsStatusText.Text = "Couldn't copy diagnostics - try again in a moment.";
        }
    }

    private void Persist(bool reflow = false)
    {
        _app.Settings.Save();
        _app.ApplySettingsToCharm();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
