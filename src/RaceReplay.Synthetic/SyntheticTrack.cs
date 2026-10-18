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

    /// <summary>
    /// Two straights of <paramref name="straightMetres"/> along x, joined by semicircles of
    /// <paramref name="radiusMetres"/>, driven anticlockwise. Progress 0 is halfway along the lower straight.
    /// </summary>
    public static SyntheticTrack Stadium(double straightMetres, double radiusMetres)
    {
        if (!(straightMetres >= 0) || !double.IsFinite(straightMetres))
        {
            throw new ArgumentOutOfRangeException(nameof(straightMetres), straightMetres, "The straights must be finite and not negative.");
        }

        if (!(radiusMetres > 0) || !double.IsFinite(radiusMetres))
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMetres), radiusMetres, "The radius must be positive.");
        }

        return new StadiumTrack(straightMetres, radiusMetres);
    }

    /// <summary>The same track driven the other way round: mirrored in the x axis, so progress 0 stays put.</summary>
    public SyntheticTrack Clockwise() => new MirroredTrack(this);

    /// <summary>The same track turned <paramref name="degrees"/> anticlockwise about its centre.</summary>
    public SyntheticTrack Rotated(double degrees) =>
        double.IsFinite(degrees)
            ? new RotatedTrack(this, degrees * Math.PI / 180)
            : throw new ArgumentOutOfRangeException(nameof(degrees), degrees, "The angle must be finite.");

    /// <summary>
    /// The point <paramref name="metres"/> along the track, wrapping each lap, in track units. A
    /// <paramref name="lateralMetres"/> offset moves it sideways, positive to the left of the direction of travel.
    /// </summary>
    public (double X, double Y) PointAt(double metres, double lateralMetres = 0)
    {
        double along = metres % LengthMetres;
        Pose pose = PoseOnLap(along < 0 ? along + LengthMetres : along);
        double x = pose.X - (lateralMetres * Math.Sin(pose.Heading));
        double y = pose.Y + (lateralMetres * Math.Cos(pose.Heading));
        return ((CentreXMetres + x) * UnitsPerMetre, (CentreYMetres + y) * UnitsPerMetre);
    }

    /// <summary>The direction of travel <paramref name="metres"/> along the track, in radians anticlockwise from +x.</summary>
    public double HeadingAt(double metres)
    {
        double along = metres % LengthMetres;
        return PoseOnLap(along < 0 ? along + LengthMetres : along).Heading;
    }

    /// <summary>Where the car is <paramref name="metres"/> along one lap: metres from the centre, and the heading.</summary>
    private protected abstract Pose PoseOnLap(double metres);

    private protected readonly record struct Pose(double X, double Y, double Heading);

    private sealed class CircleTrack(double radius) : SyntheticTrack
    {
        public override double LengthMetres { get; } = 2 * Math.PI * radius;

        private protected override Pose PoseOnLap(double metres)
        {
            double angle = metres / radius;
            return new Pose(radius * Math.Cos(angle), radius * Math.Sin(angle), angle + (Math.PI / 2));
        }
    }

    private sealed class StadiumTrack(double straight, double radius) : SyntheticTrack
    {
        private readonly double _half = straight / 2;
        private readonly double _bend = Math.PI * radius;

        public override double LengthMetres { get; } = (2 * straight) + (2 * Math.PI * radius);

        private protected override Pose PoseOnLap(double metres)
        {
            // Lower straight (second half), right bend, upper straight, left bend, lower straight (first half).
            double m = metres;
            if (m < _half)
            {
                return new Pose(m, -radius, 0);
            }

            m -= _half;
            if (m < _bend)
            {
                double angle = (-Math.PI / 2) + (m / radius);
                return new Pose(_half + (radius * Math.Cos(angle)), radius * Math.Sin(angle), angle + (Math.PI / 2));
            }

            m -= _bend;
            if (m < straight)
            {
                return new Pose(_half - m, radius, Math.PI);
            }

            m -= straight;
            if (m < _bend)
            {
                double angle = (Math.PI / 2) + (m / radius);
                return new Pose(-_half + (radius * Math.Cos(angle)), radius * Math.Sin(angle), angle + (Math.PI / 2));
            }

            m -= _bend;
            return new Pose(-_half + m, -radius, 2 * Math.PI);
        }
    }

    private sealed class MirroredTrack(SyntheticTrack inner) : SyntheticTrack
    {
        public override double LengthMetres => inner.LengthMetres;

        private protected override Pose PoseOnLap(double metres)
        {
            Pose p = inner.PoseOnLap(metres);
            return new Pose(p.X, -p.Y, -p.Heading);
        }
    }

    private sealed class RotatedTrack(SyntheticTrack inner, double radians) : SyntheticTrack
    {
        private readonly double _cos = Math.Cos(radians);
        private readonly double _sin = Math.Sin(radians);

        public override double LengthMetres => inner.LengthMetres;

        private protected override Pose PoseOnLap(double metres)
        {
            Pose p = inner.PoseOnLap(metres);
            return new Pose((p.X * _cos) - (p.Y * _sin), (p.X * _sin) + (p.Y * _cos), p.Heading + radians);
        }
    }
}