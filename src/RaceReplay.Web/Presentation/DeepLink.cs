using System.Globalization;

namespace RaceReplay.Web.Presentation;

/// <summary>Query parameters of replay URLs, e.g. <c>/replay/demo/1?t=3600</c> (FR-11).</summary>
public static class DeepLink
{
    /// <summary>
    /// The start time from <c>?t=</c> in seconds, or <see langword="null"/> to start at the beginning when it is missing,
    /// negative or not a finite number. A time past the end is returned as is; the clock clamps it.
    /// </summary>
    public static double? ParseTime(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double t) && double.IsFinite(t) && t >= 0
            ? t
            : null;
}