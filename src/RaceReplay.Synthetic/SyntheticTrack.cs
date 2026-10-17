using RaceReplay.Domain;

namespace RaceReplay.Synthetic;

/// <summary>
/// A closed track for small analytic worlds (docs/06 §5): the position at any distance along it, in track units.
/// Unlike <see cref="SyntheticCircuit"/>, its shape is exact, so tests can compute expected results from it.
/// </summary>
public abstract class SyntheticTrack
{
    /// <summary>Track units per metre, as in OpenF1 (docs/03 §3).</summary>
    public const double UnitsPerMetre = TrackModel.DefaultUnitsPerMetre;

    /// <summary>Where the track's frame is centred, in metres. Off the origin, as real circuits are, so a car on it
    /// never reports OpenF1's (0, 0, 0) placeholder by accident.</summary>
    public const double CentreXMetres = 2_000;

    /// <inheritdoc cref="CentreXMetres"/>
    public const double CentreYMetres = -1_500;

    private protected SyntheticTrack()
    {
    }

    /// <summary>The length of one lap, in metres.</summary>
    public abstract double LengthMetres { get; }

    /// <summary>A circle of <paramref name="radiusMetres"/>, driven anticlockwise from its rightmost point.</summary>
    public static SyntheticTrack Circle(double radiusMetres)
    {
        if (!(radiusMetres > 0) || !double.IsFinite(radiusMetres))
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMetres), radiusMetres, "The radius must be positive.");
        }

        return new CircleTrack(radiusMetres);
    }

    /// <summary>The point <paramref name="metres"/> along the track, wrapping each lap, in track units.</summary>
    public (double X, double Y) PointAt(double metres)
    {
        double along = metres % LengthMetres;
        (double x, double y) = PointOnLap(along < 0 ? along + LengthMetres : along);
        return ((CentreXMetres + x) * UnitsPerMetre, (CentreYMetres + y) * UnitsPerMetre);
    }

    /// <summary>The point <paramref name="metres"/> along one lap, in metres from the centre.</summary>
    private protected abstract (double X, double Y) PointOnLap(double metres);

    private sealed class CircleTrack(double radius) : SyntheticTrack
    {
        public override double LengthMetres { get; } = 2 * Math.PI * radius;

        private protected override (double X, double Y) PointOnLap(double metres) =>
            (radius * Math.Cos(metres / radius), radius * Math.Sin(metres / radius));
    }
}