using RaceReplay.Domain;

namespace RaceReplay.Web.Rendering;

/// <summary>
/// What the track map needs once per replay, flattened for JS interop (docs/05 §6). Arrays are in driver order, the
/// same order as frame records.
/// </summary>
/// <param name="Centreline">Interleaved x, y in track units; point 0 is the start/finish line.</param>
/// <param name="RotationDegrees">Display rotation; positive turns the map anticlockwise.</param>
/// <param name="Acronyms">Driver labels.</param>
/// <param name="Colours">Team colours as <c>#RRGGBB</c>.</param>
/// <param name="Outlines">A colour that contrasts with each team colour.</param>
/// <param name="Dashed">1 for a team's second and later cars, which get a dashed outline so colour isn't the only signal.</param>
public sealed record TrackMapScene(
    double[] Centreline,
    double RotationDegrees,
    string[] Acronyms,
    string[] Colours,
    string[] Outlines,
    int[] Dashed)
{
    public const string DarkOutline = "#111111";
    public const string LightOutline = "#FFFFFF";

    public static TrackMapScene For(ReplaySession replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ReadOnlySpan<TrackPoint> points = replay.Track.Centreline.Span;
        double[] centreline = new double[points.Length * 2];
        for (int i = 0; i < points.Length; i++)
        {
            centreline[2 * i] = points[i].X;
            centreline[(2 * i) + 1] = points[i].Y;
        }

        HashSet<string> teamsSeen = new(StringComparer.Ordinal);
        return new TrackMapScene(
            centreline,
            replay.Track.DisplayRotationDegrees,
            [.. replay.Drivers.Select(d => d.Acronym)],
            [.. replay.Drivers.Select(d => "#" + d.TeamColour.ToHex())],
            [.. replay.Drivers.Select(d => OutlineFor(d.TeamColour))],
            [.. replay.Drivers.Select(d => teamsSeen.Add(d.TeamName) ? 0 : 1)]);
    }

    /// <summary>Dark on light colours, light on dark, by relative luminance (WCAG 2), so every car stands out.</summary>
    public static string OutlineFor(RgbColour colour)
    {
        double luminance = (0.2126 * Linear(colour.R)) + (0.7152 * Linear(colour.G)) + (0.0722 * Linear(colour.B));

        // 0.179 is where black and white give equal contrast.
        return luminance > 0.179 ? DarkOutline : LightOutline;

        static double Linear(byte channel)
        {
            double c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }
}