using System.Globalization;

namespace RaceReplay.Web.Presentation;

/// <summary>Replay positions (seconds since T0) as text for the readout and screen readers.</summary>
public static class TimeFormat
{
    /// <summary><c>h:mm:ss</c>, rounded down to the second, e.g. <c>1:02:14</c>.</summary>
    public static string Clock(double seconds)
    {
        long s = WholeSeconds(seconds);
        return string.Create(CultureInfo.InvariantCulture, $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}");
    }

    /// <summary>For <c>aria-valuetext</c>, e.g. "1 hour 2 minutes 14 seconds" (docs/05 §10).</summary>
    public static string Spoken(double seconds)
    {
        long s = WholeSeconds(seconds);
        long hours = s / 3600, minutes = s / 60 % 60, rest = s % 60;
        List<string> parts = new(3);
        if (hours > 0)
        {
            parts.Add(Unit(hours, "hour"));
        }

        if (minutes > 0)
        {
            parts.Add(Unit(minutes, "minute"));
        }

        if (rest > 0 || parts.Count == 0)
        {
            parts.Add(Unit(rest, "second"));
        }

        return string.Join(' ', parts);

        static string Unit(long n, string unit) =>
            string.Create(CultureInfo.InvariantCulture, $"{n} {unit}{(n == 1 ? string.Empty : "s")}");
    }

    private static long WholeSeconds(double seconds) => double.IsFinite(seconds) && seconds > 0 ? (long)seconds : 0;
}