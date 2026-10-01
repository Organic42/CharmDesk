using System;
using System.Windows;
using System.Windows.Threading;
using CharmDesk.Power;

namespace CharmDesk.Windows;

/// <summary>The final countdown before a scheduled shut down or sleep. Every way out of this
/// window except the countdown finishing or "now" being clicked counts as Cancel - closing it,
/// Esc, Enter, or the Cancel button - so the safe outcome is always the easy one.</summary>
public partial class PowerCountdownWindow : Window
{
    private readonly DateTime _endsAt;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _finished;

    /// <summary>The countdown reached zero, or "now" was clicked.</summary>
    public event Action? Confirmed;

    /// <summary>Cancelled by any means, including closing the window.</summary>
    public event Action? Cancelled;

    public PowerCountdownWindow(PowerAction action, TimeSpan countdown)
    {
        _endsAt = DateTime.Now + countdown;
        InitializeComponent();

        var sleep = action == PowerAction.Sleep;
        EyebrowText.Text = sleep ? "S L E E P   T I M E R" : "S H U T   D O W N   T I M E R";
        HeadlineText.Text = sleep ? "Your PC is about to go to sleep" : "Your PC is about to shut down";
        DetailText.Text = sleep
            ? "When this reaches zero, your PC goes to sleep. Nothing is closed - everything will be where you left it."
            : "When this reaches zero, your PC shuts down. Apps with unsaved work will still ask before they close.";
        NowButton.Content = sleep ? "Sleep now" : "Shut down now";

        _timer.Tick += (_, _) => UpdateCountdown();
        UpdateCountdown();
        _timer.Start();

        Loaded += (_, _) => CancelButton.Focus();
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (!_finished)
            {
                _finished = true;
                Cancelled?.Invoke();
            }
        };
    }

    private void UpdateCountdown()
    {
        var left = _endsAt - DateTime.Now;
        if (left <= TimeSpan.Zero)
        {
            Finish(confirmed: true);
            return;
        }
        var seconds = (int)Math.Ceiling(left.TotalSeconds);
        CountdownText.Text = $"{seconds / 60}:{seconds % 60:00}";
    }

    private void Finish(bool confirmed)
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop();
        if (confirmed) Confirmed?.Invoke();
        else Cancelled?.Invoke();
        Close();
    }

    private void NowButton_Click(object sender, RoutedEventArgs e) => Finish(confirmed: true);

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Finish(confirmed: false);
}
