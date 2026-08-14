using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using CharmDesk.Core;

namespace CharmDesk.Tray;

/// <summary>Owns the system tray icon and its context menu. CharmDesk lives here quietly
/// whenever no window is open - closing the library/settings/manager windows never quits it.</summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _showItem;
    private readonly ToolStripMenuItem _hideItem;
    private readonly ToolStripMenuItem _changeCharmItem;

    public event Action? ShowCharmRequested;
    public event Action? HideCharmRequested;
    public event Action<string>? ChangeCharmRequested;
    public event Action? OpenLibraryRequested;
    public event Action? OpenSettingsRequested;
    public event Action? ResetPositionRequested;
    public event Action? ExitRequested;

    public TrayIconManager()
    {
        _showItem = new ToolStripMenuItem("Show Charm", null, (_, _) => ShowCharmRequested?.Invoke());
        _hideItem = new ToolStripMenuItem("Hide Charm", null, (_, _) => HideCharmRequested?.Invoke());
        _changeCharmItem = new ToolStripMenuItem("Change Charm");

        var libraryItem = new ToolStripMenuItem("Charm Library...", null, (_, _) => OpenLibraryRequested?.Invoke());
        var settingsItem = new ToolStripMenuItem("Settings...", null, (_, _) => OpenSettingsRequested?.Invoke());
        var resetItem = new ToolStripMenuItem("Reset Position", null, (_, _) => ResetPositionRequested?.Invoke());
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke());

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_showItem);
        _menu.Items.Add(_hideItem);
        _menu.Items.Add(_changeCharmItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(libraryItem);
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(resetItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        System.Drawing.Icon icon;
        try
        {
            var exePath = Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "CharmDesk.exe");
            icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath) ?? System.Drawing.SystemIcons.Application;
        }
        catch
        {
            icon = System.Drawing.SystemIcons.Application;
        }

        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "CharmDesk",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowCharmRequested?.Invoke();
    }

    public void SetCharmVisible(bool visible)
    {
        _showItem.Enabled = !visible;
        _hideItem.Enabled = visible;
    }

    public void RefreshCharmList(IEnumerable<CharmPackage> charms, string? activeId)
    {
        _changeCharmItem.DropDownItems.Clear();
        foreach (var charm in charms.Where(c => c.Manifest.Enabled))
        {
            var id = charm.Manifest.Id;
            var item = new ToolStripMenuItem(charm.Manifest.Name)
            {
                Checked = string.Equals(id, activeId, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => ChangeCharmRequested?.Invoke(id);
            _changeCharmItem.DropDownItems.Add(item);
        }
        if (_changeCharmItem.DropDownItems.Count == 0)
            _changeCharmItem.DropDownItems.Add(new ToolStripMenuItem("(no charms installed)") { Enabled = false });
    }

    /// <summary>Pops the same menu the tray icon uses at an arbitrary screen point - used for
    /// right-clicking the charm itself, not just the tray icon.</summary>
    public void ShowContextMenu(System.Drawing.Point screenPoint) => _menu.Show(screenPoint);

    public void ShowBalloon(string title, string text) =>
        _notifyIcon.ShowBalloonTip(2500, title, text, ToolTipIcon.None);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
