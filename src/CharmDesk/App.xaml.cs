using System;
using System.IO;
using System.Linq;
using System.Windows;
using CharmDesk.Core;
using CharmDesk.Native;
using CharmDesk.Persistence;
using CharmDesk.Tray;
using CharmDesk.Windows;

namespace CharmDesk;

public partial class App : Application
{
    private SettingsManager _settingsManager = null!;
    private CharmRegistry _registry = null!;
    private TrayIconManager _tray = null!;
    private CharmWindow? _charmWindow;
    private CharmLibraryWindow? _libraryWindow;
    private SettingsWindow? _settingsWindow;

    public CharmRegistry Registry => _registry;
    public SettingsManager Settings => _settingsManager;

    /// <summary>The graphics tier (0/1/2) WPF detected for this machine - purely a diagnostic
    /// breadcrumb, surfaced in Settings' "Copy Diagnostic Info". Added while chasing a flicker
    /// report that turned out to be on Tier 2 (full hardware acceleration) hardware, which
    /// disproved the "weak/no GPU acceleration" assumption a since-reverted fix here was built on
    /// (see git history) - keeping this around so the next report says what a driver actually
    /// offers instead of it being guessed at from a distance again.</summary>
    public static int GraphicsRenderTier { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        GraphicsRenderTier = System.Windows.Media.RenderCapability.Tier >> 16;

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CharmDesk");
        Logger.Initialize(dataDir);

        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Log("UI thread", args.Exception);
            // Keep the tray running rather than vanishing outright - a background utility
            // that disappears without a trace is worse than one that logs and carries on.
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Logger.Log("AppDomain (fatal)", ex);
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Log("Unobserved task", args.Exception);
            args.SetObserved();
        };

        try
        {
            _settingsManager = new SettingsManager(dataDir);

            var charmsDir = Path.Combine(dataDir, "charms");
            _registry = new CharmRegistry(charmsDir);
            var bundledCharmsDir = Path.Combine(AppContext.BaseDirectory, "charms");
            _registry.SyncBundledCharms(bundledCharmsDir, _settingsManager.Current.DeletedCharmIds);

            _tray = new TrayIconManager();
            _tray.ShowCharmRequested += OnShowCharmRequested;
            _tray.HideCharmRequested += OnHideCharmRequested;
            _tray.ChangeCharmRequested += ActivateCharm;
            _tray.OpenLibraryRequested += OpenLibrary;
            _tray.OpenSettingsRequested += OpenSettings;
            _tray.ResetPositionRequested += () => _charmWindow?.ResetPosition();
            _tray.ExitRequested += () => Shutdown();

            LaunchInitialCharm();

            // Reconcile the persisted "start with Windows" flag with what Windows actually has
            // registered. Fire-and-forget: it only affects what the Settings checkbox shows, so
            // it must never delay the charm appearing.
            _ = _settingsManager.SyncStartWithWindowsAsync();
        }
        catch (Exception ex)
        {
            Logger.Log("Startup", ex);
            MessageBox.Show(
                "CharmDesk couldn't start up correctly. Check %AppData%\\CharmDesk\\logs\\charmdesk.log for details.",
                "CharmDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void LaunchInitialCharm()
    {
        var all = _registry.LoadAll();
        var enabled = all.Where(c => c.Manifest.Enabled).ToList();
        var chosen =
            enabled.FirstOrDefault(c => string.Equals(c.Manifest.Id, _settingsManager.Current.SelectedCharmId, StringComparison.OrdinalIgnoreCase)) ??
            enabled.FirstOrDefault(c => string.Equals(c.Manifest.Id, _settingsManager.Current.DefaultCharmId, StringComparison.OrdinalIgnoreCase)) ??
            enabled.FirstOrDefault();

        _tray.RefreshCharmList(all, chosen?.Manifest.Id);

        if (chosen is null)
        {
            // Two very different situations produce "no charm on screen", and a balloon tip that
            // disappears after a few seconds explains neither. The common one by far, for anyone
            // handed the share zip, is running CharmDesk.exe straight out of Windows' zip
            // preview: Explorer copies just the exe to a temp folder, leaving the charms behind,
            // so the app starts, sits in the tray, and appears to do nothing at all.
            if (!Directory.Exists(Path.Combine(AppContext.BaseDirectory, "charms")))
            {
                MessageBox.Show(
                    "CharmDesk can't find its charm files, so there's nothing to hang on your desktop.\n\n" +
                    "This usually means it was opened from inside the .zip. Extract the whole folder " +
                    "somewhere first (your Desktop is fine), then run CharmDesk.exe from the extracted " +
                    "folder - keeping it next to the 'charms' and 'Sounds' folders.",
                    "CharmDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _tray.ShowBalloon("CharmDesk", "No charms installed yet - open the Charm Library to add one.");
            return;
        }

        _settingsManager.Current.SelectedCharmId = chosen.Manifest.Id;
        _settingsManager.Current.DefaultCharmId ??= chosen.Manifest.Id;
        _settingsManager.Save();

        CreateCharmWindow(chosen);
        if (!_settingsManager.Current.CharmVisible)
            _charmWindow?.HideCharm();
        _tray.SetCharmVisible(_settingsManager.Current.CharmVisible);

        ScheduleOnboardingHintIfNeeded();
        ScheduleStartupTrim();
    }

    /// <summary>Startup touches a lot of pages it never needs again (JIT, XAML parsing, charm
    /// enumeration). Trimming once the app has settled into its steady state stops it from
    /// holding that launch peak for the rest of the session. Delayed rather than immediate so
    /// it can't trim pages the first few frames are still using.</summary>
    private void ScheduleStartupTrim()
    {
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(8),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            MemoryHelper.TrimWorkingSet();
        };
        timer.Start();
    }

    /// <summary>Shows a one-time balloon tip pointing out right-click and drag, since neither
    /// is discoverable from the tray icon alone. Delayed so it doesn't compete with the charm's
    /// own arrival animation for attention, and only ever fires once per install.</summary>
    private void ScheduleOnboardingHintIfNeeded()
    {
        if (_settingsManager.Current.HasShownOnboarding) return;

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _tray.ShowBalloon("Welcome to CharmDesk",
                "Drag your charm to swing it, or right-click it for options and more charms.");
            _settingsManager.Current.HasShownOnboarding = true;
            _settingsManager.Save();
        };
        timer.Start();
    }

    private void CreateCharmWindow(CharmPackage package)
    {
        // HideCharm() stops the render timer before Close(); without it, switching charms
        // mid-swing leaves the old window's DispatcherTimer (and the window itself) alive
        // until its physics happens to decay to rest.
        _charmWindow?.HideCharm();
        _charmWindow?.Close();
        _charmWindow = new CharmWindow(package, _settingsManager, _tray);
        _charmWindow.Show();
    }

    public void ActivateCharm(string id)
    {
        var pkg = _registry.Find(id);
        if (pkg is null) return;

        _settingsManager.Current.SelectedCharmId = id;
        _settingsManager.Save();
        CreateCharmWindow(pkg);
        _settingsManager.Current.CharmVisible = true;
        _tray.SetCharmVisible(true);
        _tray.RefreshCharmList(_registry.LoadAll(), id);
    }

    private void OnShowCharmRequested()
    {
        _settingsManager.Current.CharmVisible = true;
        _settingsManager.Save();
        if (_charmWindow is null) { LaunchInitialCharm(); return; }
        _charmWindow.ShowCharm();
        _tray.SetCharmVisible(true);
    }

    private void OnHideCharmRequested()
    {
        _settingsManager.Current.CharmVisible = false;
        _settingsManager.Save();
        _charmWindow?.HideCharm();
        _tray.SetCharmVisible(false);
    }

    /// <summary>Called by the Library/Manager windows after charms are imported, edited, or deleted.</summary>
    public void RefreshAfterLibraryChange()
    {
        var all = _registry.LoadAll();
        _tray.RefreshCharmList(all, _settingsManager.Current.SelectedCharmId);

        // If the active charm was deleted or disabled out from under us, fall back gracefully.
        var stillValid = all.Any(c => c.Manifest.Enabled &&
            string.Equals(c.Manifest.Id, _settingsManager.Current.SelectedCharmId, StringComparison.OrdinalIgnoreCase));
        if (!stillValid)
            LaunchInitialCharm();
    }

    public void ApplySettingsToCharm() => _charmWindow?.ApplySettingsChanged();

    public void ResetActiveCharmPosition() => _charmWindow?.ResetPosition();

    private void OpenLibrary()
    {
        if (_libraryWindow is { IsVisible: true }) { _libraryWindow.Activate(); return; }
        _libraryWindow = new CharmLibraryWindow(this);
        // The Library is the app's heaviest moment - it decodes a thumbnail per installed charm.
        // Hand that memory back rather than sitting at the peak for the rest of the session.
        _libraryWindow.Closed += (_, _) => MemoryHelper.TrimWorkingSet();
        _libraryWindow.Show();
    }

    public void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true }) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => MemoryHelper.TrimWorkingSet();
        _settingsWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
