using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using CharmDesk.Core;

namespace CharmDesk.Windows;

/// <summary>The About page, reached from the Charm Library header.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow(CharmRegistry registry)
    {
        InitializeComponent();

        // Read the version from the assembly rather than hardcoding it, so this can't drift
        // out of step with the csproj / package manifest.
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Strip any build metadata suffix (e.g. "1.0.0+abc123") for display.
        var plus = version?.IndexOf('+') ?? -1;
        if (plus > 0) version = version![..plus];
        // Fall back through AssemblyVersion to a literal - an attribute that exists but is
        // blank would otherwise render a bare "version " with nothing after it.
        if (string.IsNullOrWhiteSpace(version))
            version = assembly.GetName().Version?.ToString(3);
        if (string.IsNullOrWhiteSpace(version))
            version = "1.0.0";
        VersionText.Text = $"version {version}";

        ShowDecorativeCharm(registry);
    }

    /// <summary>Hangs the active (or first available) charm at the top of the page. Purely
    /// decorative - if anything is missing the image just stays empty.</summary>
    private void ShowDecorativeCharm(CharmRegistry registry)
    {
        try
        {
            var package = registry.LoadAll().FirstOrDefault(p => p.Manifest.Enabled)
                          ?? registry.LoadAll().FirstOrDefault();
            if (package is null) return;

            var path = File.Exists(package.ImagePath) ? package.ImagePath : package.ThumbnailPath;
            if (File.Exists(path))
                CharmArt.Source = ImageLoader.TryLoad(path, "AboutWindow");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CharmDesk.Persistence.Logger.Log("AboutWindow.ShowDecorativeCharm", ex);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
