using System.Globalization;

namespace RaceReplay.Web.Presentation;

/// <summary>Numbers in URLs and form values: invariant culture, finite only.</summary>
public static class InvariantNumber
{
    /// <summary>Parses <paramref name="value"/>'s text as a finite number, e.g. a query parameter or an input's value.</summary>
    public static bool TryParse(object? value, out double number) =>
        double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    public static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
}