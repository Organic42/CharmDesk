using System;
using System.Linq;
using System.Windows;
using CharmDesk.Power;

namespace CharmDesk.Windows;

/// <summary>"At a specific time..." from the tray. Returns the chosen action and the exact
/// moment it's due via <see cref="SelectedAction"/> and <see cref="Due"/> when DialogResult is true.</summary>
public partial class PowerScheduleWindow : Window
{
    private readonly bool _use24Hour;
    private bool _ready;

    public PowerAction SelectedAction { get; private set; }
    public DateTime Due { get; private set; }

    public PowerScheduleWindow(PowerAction initialAction, bool use24Hour)
    {
        _use24Hour = use24Hour;
        InitializeComponent();

        HourCombo.ItemsSource = use24Hour
            ? Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToList()
            : Enumerable.Range(1, 12).Select(h => h.ToString()).ToList();
        MinuteCombo.ItemsSource = Enumerable.Range(0, 12).Select(i => (i * 5).ToString("00")).ToList();
        PeriodCombo.ItemsSource = new[] { "AM", "PM" };
        PeriodCombo.Visibility = use24Hour ? Visibility.Collapsed : Visibility.Visible;

        // Default: an hour from now, rounded up to the next five minutes.
        var start = DateTime.Now.AddHours(1);
        var rounded = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0).AddMinutes(Math.Ceiling(start.Minute / 5.0) * 5);
        SelectTime(rounded.Hour, rounded.Minute);

        (initialAction == PowerAction.Sleep ? SleepRadio : ShutDownRadio).IsChecked = true;

        _ready = true;
        UpdatePreview();
        Loaded += (_, _) => HourCombo.Focus();
    }

    private void SelectTime(int hour24, int minute)
    {
        if (_use24Hour)
        {
            HourCombo.SelectedIndex = hour24;
        }
        else
        {
            var hour12 = hour24 % 12 == 0 ? 12 : hour24 % 12;
            HourCombo.SelectedIndex = hour12 - 1;
            PeriodCombo.SelectedIndex = hour24 >= 12 ? 1 : 0;
        }
        MinuteCombo.SelectedIndex = minute / 5;
    }

    private TimeSpan ChosenTimeOfDay()
    {
        var minute = MinuteCombo.SelectedIndex * 5;
        if (_use24Hour) return new TimeSpan(HourCombo.SelectedIndex, minute, 0);

        var hour12 = HourCombo.SelectedIndex + 1;
        var pm = PeriodCombo.SelectedIndex == 1;
        return new TimeSpan(hour12 % 12 + (pm ? 12 : 0), minute, 0);
    }

    private PowerAction ChosenAction() => SleepRadio.IsChecked == true ? PowerAction.Sleep : PowerAction.ShutDown;

    private void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_ready) UpdatePreview();
    }

    private void UpdatePreview()
    {
        var now = DateTime.Now;
        var due = PowerSchedule.NextOccurrence(now, ChosenTimeOfDay());
        var verb = ChosenAction() == PowerAction.Sleep ? "Sleeps" : "Shuts down";
        PreviewText.Text = $"{verb} in {PowerSchedule.DescribeRemaining(due - now)} - {PowerSchedule.FormatWhen(now, due, _use24Hour)}";
    }

    private void ScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        // Worked out at the moment of clicking, not when the dialog opened - it may have sat
        // open long enough for "later today" to have become "tomorrow".
        SelectedAction = ChosenAction();
        Due = PowerSchedule.NextOccurrence(DateTime.Now, ChosenTimeOfDay());
        DialogResult = true;
    }
}
