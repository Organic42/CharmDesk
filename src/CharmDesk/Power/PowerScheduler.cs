using System;
using System.Windows.Threading;
using CharmDesk.Persistence;
using CharmDesk.Windows;

namespace CharmDesk.Power;

/// <summary>
/// Holds at most one pending shut down or sleep and carries it out. The schedule lives only in
/// memory: if CharmDesk closes or crashes, nothing happens - the failure mode is "the PC stayed
/// on", never "the PC turned off unexpectedly".
///
/// Checked against the wall clock once a second rather than as one long timer, because a timer
/// counts elapsed time and would drift across sleep; the wall clock is what "at 11:30" means.
/// </summary>
public sealed class PowerScheduler : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Func<bool> _use24Hour;
    private PowerCountdownWindow? _countdown;

    public PowerAction? Action { get; private set; }
    public DateTime? Due { get; private set; }

    /// <summary>Raised whenever the pending schedule starts, changes, or ends.</summary>
    public event Action? Changed;

    /// <summary>Something the user should be told about that happened without them - a schedule
    /// skipped because the PC was asleep, or Windows refusing the request.</summary>
    public event Action<string>? Notice;

    /// <param name="use24Hour">Read each time a message is shown, so a change in Settings applies
    /// without rebuilding the scheduler.</param>
    public PowerScheduler(Func<bool> use24Hour)
    {
        _use24Hour = use24Hour;
        _timer.Tick += (_, _) => Check();
    }

    /// <summary>Replaces any existing schedule - there is only ever one.</summary>
    public void Schedule(PowerAction action, DateTime due)
    {
        CloseCountdown();
        Action = action;
        Due = due;
        Logger.Log($"PowerScheduler: {PowerSchedule.Verb(action)} scheduled for {due:yyyy-MM-dd HH:mm}");
        _timer.Start();
        Changed?.Invoke();
        Check();
    }

    public void Cancel()
    {
        if (Action is null) return;
        Logger.Log($"PowerScheduler: {PowerSchedule.Verb(Action.Value)} cancelled");
        Clear();
    }

    private void Clear()
    {
        _timer.Stop();
        CloseCountdown();
        Action = null;
        Due = null;
        Changed?.Invoke();
    }

    private void Check()
    {
        if (Action is not { } action || Due is not { } due)
        {
            _timer.Stop();
            return;
        }
        if (_countdown is not null) return; // the countdown window owns the last stretch

        var now = DateTime.Now;
        switch (PowerSchedule.Evaluate(now, due))
        {
            case PowerSchedule.Phase.Missed:
                Logger.Log($"PowerScheduler: {PowerSchedule.Verb(action)} for {due:HH:mm} missed, dropping it");
                Clear();
                Notice?.Invoke($"The {PowerSchedule.Verb(action)} planned for {PowerSchedule.FormatWhen(now, due, _use24Hour())} was skipped - your PC was asleep or busy at that time.");
                break;

            case PowerSchedule.Phase.Warn:
                OpenCountdown(action, PowerSchedule.WarningCountdown(now, due));
                break;
        }
    }

    private void OpenCountdown(PowerAction action, TimeSpan countdown)
    {
        var window = new PowerCountdownWindow(action, countdown);
        window.Confirmed += () => Execute(action);
        window.Cancelled += () =>
        {
            // Closing the countdown is how the user cancels; but this also fires when the window is
            // closed as part of Execute/Clear, which have already dealt with the schedule.
            if (ReferenceEquals(_countdown, window))
            {
                _countdown = null;
                Cancel();
            }
        };
        _countdown = window;
        window.Show();
    }

    private void CloseCountdown()
    {
        var window = _countdown;
        _countdown = null; // first, so the window's own Cancelled handler knows this isn't a user cancel
        window?.Close();
    }

    private void Execute(PowerAction action)
    {
        _countdown = null;
        Clear();
        Logger.Log($"PowerScheduler: carrying out {PowerSchedule.Verb(action)}");
        if (!PowerActions.Run(action))
        {
            Notice?.Invoke(action == PowerAction.Sleep
                ? "Windows didn't allow CharmDesk to put this PC to sleep. Sleep may be turned off in your power settings."
                : "Windows didn't allow CharmDesk to shut down this PC. It may be blocked by a policy on this device.");
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        CloseCountdown();
    }
}
