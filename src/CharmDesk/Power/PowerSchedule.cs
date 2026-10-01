using System;
using System.Globalization;

namespace CharmDesk.Power;

public enum PowerAction
{
    ShutDown,
    Sleep,
}

/// <summary>
/// The timing rules for a scheduled shut down or sleep, kept free of timers, windows and
/// Windows calls so every edge case can be unit-tested against a fixed clock.
/// </summary>
public static class PowerSchedule
{
    /// <summary>How far ahead of the due time the countdown warning opens.</summary>
    public static readonly TimeSpan WarningLead = TimeSpan.FromSeconds(60);

    /// <summary>The shortest countdown anyone ever gets. If the warning opens late - the app was
    /// busy, or the PC woke up just after the due time - it still runs at least this long, so the
    /// PC never shuts down or sleeps the instant someone starts using it.</summary>
    public static readonly TimeSpan MinimumWarning = TimeSpan.FromSeconds(30);

    /// <summary>A schedule this far past due is dropped rather than carried out. Laptops sleep
    /// through their due time with the lid closed; shutting down the moment someone opens it the
    /// next morning would be the worst possible behaviour.</summary>
    public static readonly TimeSpan MissedGrace = TimeSpan.FromMinutes(2);

    public enum Phase
    {
        Waiting,
        Warn,
        Missed,
    }

    /// <summary>The next time the clock reads <paramref name="timeOfDay"/>: later today if that is
    /// still ahead, otherwise tomorrow. Exactly now counts as passed, so picking the current minute
    /// means tomorrow rather than an immediate shut down.</summary>
    public static DateTime NextOccurrence(DateTime now, TimeSpan timeOfDay)
    {
        var today = now.Date + timeOfDay;
        return today > now ? today : today.AddDays(1);
    }

    public static Phase Evaluate(DateTime now, DateTime due)
    {
        if (now >= due + MissedGrace) return Phase.Missed;
        if (now >= due - WarningLead) return Phase.Warn;
        return Phase.Waiting;
    }

    /// <summary>How long the countdown should run when the warning opens now.</summary>
    public static TimeSpan WarningCountdown(DateTime now, DateTime due)
    {
        var left = due - now;
        return left < MinimumWarning ? MinimumWarning : left;
    }

    /// <summary>"45 min", "1 h 20 min", "2 h", or "under a minute".</summary>
    public static string DescribeRemaining(TimeSpan span)
    {
        var minutes = (int)Math.Ceiling(span.TotalMinutes);
        if (span < TimeSpan.FromMinutes(1)) return "under a minute";
        var h = minutes / 60;
        var m = minutes % 60;
        if (h == 0) return $"{m} min";
        return m == 0 ? $"{h} h" : $"{h} h {m} min";
    }

    /// <summary>"11:30 PM" or "23:30", prefixed "tomorrow" when it isn't today. Invariant culture
    /// on purpose: the rest of CharmDesk's UI is English, and a clock that suddenly switches
    /// format on a machine set to another locale would read as a bug.</summary>
    public static string FormatWhen(DateTime now, DateTime when, bool use24Hour)
    {
        var time = when.ToString(use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture);
        if (when.Date == now.Date) return time;
        if (when.Date == now.Date.AddDays(1)) return $"tomorrow {time}";
        return when.ToString(use24Hour ? "ddd HH:mm" : "ddd h:mm tt", CultureInfo.InvariantCulture);
    }

    public static string Verb(PowerAction action) => action == PowerAction.Sleep ? "sleep" : "shut down";
}
