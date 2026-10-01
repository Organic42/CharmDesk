using CharmDesk.Power;

namespace CharmDesk.Tests;

/// <summary>The timing rules behind "shut down later" / "sleep later". Everything here runs
/// against a fixed clock, since getting any of it wrong means a PC turning off when it shouldn't.</summary>
public sealed class PowerScheduleTests
{
    private static readonly DateTime Evening = new(2026, 9, 26, 22, 0, 0);

    [Fact]
    public void NextOccurrence_TimeStillAheadToday_IsToday()
    {
        var due = PowerSchedule.NextOccurrence(Evening, new TimeSpan(23, 30, 0));
        Assert.Equal(new DateTime(2026, 9, 26, 23, 30, 0), due);
    }

    [Fact]
    public void NextOccurrence_TimeAlreadyPassed_IsTomorrow()
    {
        var due = PowerSchedule.NextOccurrence(Evening, new TimeSpan(7, 0, 0));
        Assert.Equal(new DateTime(2026, 9, 27, 7, 0, 0), due);
    }

    [Fact]
    public void NextOccurrence_ExactlyNow_IsTomorrowNotImmediate()
    {
        var due = PowerSchedule.NextOccurrence(Evening, new TimeSpan(22, 0, 0));
        Assert.Equal(new DateTime(2026, 9, 27, 22, 0, 0), due);
    }

    [Fact]
    public void NextOccurrence_RollsOverMonthEnd()
    {
        var lateOnLastDay = new DateTime(2026, 9, 30, 23, 0, 0);
        var due = PowerSchedule.NextOccurrence(lateOnLastDay, new TimeSpan(1, 0, 0));
        Assert.Equal(new DateTime(2026, 10, 1, 1, 0, 0), due);
    }

    [Fact]
    public void Evaluate_WellBeforeDue_Waits()
    {
        var due = Evening.AddMinutes(30);
        Assert.Equal(PowerSchedule.Phase.Waiting, PowerSchedule.Evaluate(Evening, due));
    }

    [Fact]
    public void Evaluate_OneMinuteBeforeDue_Warns()
    {
        var due = Evening.AddSeconds(60);
        Assert.Equal(PowerSchedule.Phase.Warn, PowerSchedule.Evaluate(Evening, due));
    }

    [Fact]
    public void Evaluate_JustPastDue_StillWarnsRatherThanActingSilently()
    {
        // e.g. the app was busy for a few seconds - the countdown must still be shown.
        var due = Evening.AddSeconds(-20);
        Assert.Equal(PowerSchedule.Phase.Warn, PowerSchedule.Evaluate(Evening, due));
    }

    [Fact]
    public void Evaluate_SleptThroughTheDueTime_IsMissedNotCarriedOut()
    {
        // Scheduled for 23:30, lid closed at 23:00, opened at 08:00 the next morning.
        var due = new DateTime(2026, 9, 26, 23, 30, 0);
        var nextMorning = new DateTime(2026, 9, 27, 8, 0, 0);
        Assert.Equal(PowerSchedule.Phase.Missed, PowerSchedule.Evaluate(nextMorning, due));
    }

    [Fact]
    public void Evaluate_MissedExactlyAtGraceBoundary()
    {
        var due = Evening;
        Assert.Equal(PowerSchedule.Phase.Missed, PowerSchedule.Evaluate(due + PowerSchedule.MissedGrace, due));
    }

    [Fact]
    public void WarningCountdown_NormalCase_RunsToTheDueTime()
    {
        var due = Evening.AddSeconds(60);
        Assert.Equal(TimeSpan.FromSeconds(60), PowerSchedule.WarningCountdown(Evening, due));
    }

    [Theory]
    [InlineData(10)]   // warning opened late
    [InlineData(0)]    // exactly due
    [InlineData(-45)]  // already past due, within grace
    public void WarningCountdown_NeverShorterThanTheMinimum(int secondsUntilDue)
    {
        var due = Evening.AddSeconds(secondsUntilDue);
        Assert.Equal(PowerSchedule.MinimumWarning, PowerSchedule.WarningCountdown(Evening, due));
    }

    [Theory]
    [InlineData(30, "under a minute")]
    [InlineData(45 * 60, "45 min")]
    [InlineData(44 * 60 + 30, "45 min")]
    [InlineData(60 * 60, "1 h")]
    [InlineData(80 * 60, "1 h 20 min")]
    [InlineData(120 * 60, "2 h")]
    public void DescribeRemaining_ReadsNaturally(int seconds, string expected)
    {
        Assert.Equal(expected, PowerSchedule.DescribeRemaining(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void FormatWhen_Today_12Hour()
    {
        Assert.Equal("11:30 PM", PowerSchedule.FormatWhen(Evening, new DateTime(2026, 9, 26, 23, 30, 0), use24Hour: false));
    }

    [Fact]
    public void FormatWhen_Today_24Hour()
    {
        Assert.Equal("23:30", PowerSchedule.FormatWhen(Evening, new DateTime(2026, 9, 26, 23, 30, 0), use24Hour: true));
    }

    [Fact]
    public void FormatWhen_PastMidnight_SaysTomorrow()
    {
        // "Sleep in 30 minutes" at 23:50 lands at 00:20 the next day.
        var now = new DateTime(2026, 9, 26, 23, 50, 0);
        Assert.Equal("tomorrow 12:20 AM", PowerSchedule.FormatWhen(now, now.AddMinutes(30), use24Hour: false));
    }

    [Fact]
    public void FormatWhen_UnaffectedByMachineCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("11:30 PM", PowerSchedule.FormatWhen(Evening, new DateTime(2026, 9, 26, 23, 30, 0), use24Hour: false));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
