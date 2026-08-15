using System.Collections.Generic;

namespace CharmDesk.Persistence;

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = true;
    public bool EnablePhysics { get; set; } = true;
    public double PhysicsIntensity { get; set; } = 1.0;
    public double CharmScale { get; set; } = 1.0;
    public bool CharmVisible { get; set; } = true;
    public bool SoundEffectsEnabled { get; set; } = true;
    public bool HasShownOnboarding { get; set; } = false;
    public bool Use24HourClock { get; set; } = true;

    public string? DefaultCharmId { get; set; }
    public string? SelectedCharmId { get; set; }

    /// <summary>Ids the user has explicitly deleted via the Charm Manager. Checked by
    /// CharmRegistry.SyncBundledCharms so a deleted bundled charm doesn't silently reappear the
    /// next time the app launches and re-syncs the bundle.</summary>
    public List<string> DeletedCharmIds { get; set; } = new();

    /// <summary>WinForms Screen.DeviceName of the chosen monitor; null = always use primary.</summary>
    public string? MonitorDeviceName { get; set; }

    /// <summary>Anchor X position as a fraction (0..1) of the chosen monitor's width, so it
    /// survives resolution changes. Defaults to top-right with a small margin.</summary>
    public double AnchorXFraction { get; set; } = 0.92;

    public double AnchorTopMargin { get; set; } = 8;
}
