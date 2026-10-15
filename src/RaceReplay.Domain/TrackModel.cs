namespace RaceReplay.Domain;

/// <summary>
/// The circuit as a closed centreline. Arc length <c>s</c> runs from 0 at the start/finish line (point 0) to
/// <see cref="LapLength"/>, in the direction of travel; the last point joins back to the first (docs/03 §5).
/// </summary>
public sealed class TrackModel
{
    /// <summary>OpenF1 coordinates are believed to be decimetres (docs/03 §3); confirmed in spike S1.</summary>
    public const double DefaultUnitsPerMetre = 10;

    // _cumulative[i] = arc length from point 0 to point i; the final entry closes the loop and equals LapLength.
    private readonly double[] _cumulative;

    public TrackModel(
        ReadOnlyMemory<TrackPoint> centreline,
        double unitsPerMetre = DefaultUnitsPerMetre,
        float displayRotationDegrees = 0)
    {
        ReadOnlySpan<TrackPoint> points = centreline.Span;
        if (points.Length < 3)
        {
            throw new ArgumentException("A centreline needs at least three points.", nameof(centreline));
        }

        if (!double.IsFinite(unitsPerMetre) || unitsPerMetre <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitsPerMetre), unitsPerMetre, "Units per metre must be positive.");
        }

        if (!float.IsFinite(displayRotationDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(displayRotationDegrees), displayRotationDegrees, "Rotation must be finite.");
        }

        _cumulative = new double[points.Length + 1];
        for (int i = 0; i < points.Length; i++)
        {
            TrackPoint a = points[i];
            if (!float.IsFinite(a.X) || !float.IsFinite(a.Y))
            {
                throw new ArgumentException($"Centreline point {i} is not finite.", nameof(centreline));
            }

            TrackPoint b = points[(i + 1) % points.Length];
            _cumulative[i + 1] = _cumulative[i] + Math.Sqrt(Square(b.X - a.X) + Square(b.Y - a.Y));
        }

        if (_cumulative[^1] <= 0)
        {
            throw new ArgumentException("The centreline has zero length.", nameof(centreline));
        }

        Centreline = centreline;
        LapLength = _cumulative[^1];
        Bounds = TrackBounds.Of(points);
        UnitsPerMetre = unitsPerMetre;
        DisplayRotationDegrees = displayRotationDegrees;
    }

    public ReadOnlyMemory<TrackPoint> Centreline { get; }

    /// <summary>Length of one lap in track units.</summary>
    public double LapLength { get; }

    public TrackBounds Bounds { get; }

    public double UnitsPerMetre { get; }

    /// <summary>Rotation applied when drawing, so the circuit appears in its familiar orientation.</summary>
    public float DisplayRotationDegrees { get; }

    /// <summary>The point at arc length <paramref name="s"/>, wrapped onto one lap.</summary>
    public TrackPoint PointAt(double s)
    {
        if (!double.IsFinite(s))
        {
            throw new ArgumentOutOfRangeException(nameof(s), s, "Arc length must be finite.");
        }

        double wrapped = s % LapLength;
        if (wrapped < 0)
        {
            wrapped += LapLength;
        }

        ReadOnlySpan<TrackPoint> points = Centreline.Span;
        int segment = Array.BinarySearch(_cumulative, wrapped);
        if (segment < 0)
        {
            segment = ~segment - 1;
        }

        // A tiny negative s can wrap to exactly LapLength, which lands on the closing entry.
        segment = Math.Min(segment, points.Length - 1);

        double length = _cumulative[segment + 1] - _cumulative[segment];
        double t = length > 0 ? (wrapped - _cumulative[segment]) / length : 0;
        TrackPoint a = points[segment];
        TrackPoint b = points[(segment + 1) % points.Length];
        return new TrackPoint((float)(a.X + ((b.X - a.X) * t)), (float)(a.Y + ((b.Y - a.Y) * t)));
    }

    private static double Square(double v) => v * v;
}