using RaceReplay.Domain;

namespace RaceReplay.Synthetic;

/// <summary>
/// The fastest a car can take each point of the lap: limited by grip in corners, then by acceleration out of them and
/// braking into them. Cars bunch in corners and stretch out on straights, as on a real track.
/// </summary>
internal sealed class SpeedProfile
{
    public const double MaxSpeed = 92;         // m/s ≈ 330 km/h
    public const double LateralGrip = 20;      // m/s²: effective, so lap times land near real circuits
    public const double Acceleration = 11;     // m/s²
    public const double Braking = 40;          // m/s²

    private readonly double[] _speed;
    private readonly double _spacingMetres;

    public SpeedProfile(TrackModel track)
    {
        ReadOnlySpan<TrackPoint> points = track.Centreline.Span;
        int n = points.Length;
        LapMetres = track.LapLength / track.UnitsPerMetre;
        _spacingMetres = LapMetres / n;

        double[] curvature = new double[n];
        for (int i = 0; i < n; i++)
        {
            curvature[i] = Curvature(points[(i + n - 1) % n], points[i], points[(i + 1) % n]) * track.UnitsPerMetre;
        }

        _speed = new double[n];
        for (int i = 0; i < n; i++)
        {
            double smoothed = 0;
            for (int k = -2; k <= 2; k++)
            {
                smoothed += curvature[((i + k) % n + n) % n];
            }

            _speed[i] = Math.Min(MaxSpeed, Math.Sqrt(LateralGrip / Math.Max(smoothed / 5, 1e-6)));
        }

        // Two laps each way, so the limits carry across the start/finish line.
        for (int pass = 0; pass < 2 * n; pass++)
        {
            int i = pass % n;
            int previous = (i + n - 1) % n;
            _speed[i] = Math.Min(_speed[i], Math.Sqrt((_speed[previous] * _speed[previous]) + (2 * Acceleration * _spacingMetres)));
        }

        for (int pass = 2 * n - 1; pass >= 0; pass--)
        {
            int i = pass % n;
            int next = (i + 1) % n;
            _speed[i] = Math.Min(_speed[i], Math.Sqrt((_speed[next] * _speed[next]) + (2 * Braking * _spacingMetres)));
        }
    }

    public double LapMetres { get; }

    /// <summary>A flying lap at full pace.</summary>
    public double LapTimeSeconds
    {
        get
        {
            double time = 0;
            foreach (double v in _speed)
            {
                time += _spacingMetres / v;
            }

            return time;
        }
    }

    /// <summary>Speed limit at <paramref name="sMetres"/> along the lap (wrapped).</summary>
    public double At(double sMetres)
    {
        double wrapped = sMetres % LapMetres;
        if (wrapped < 0)
        {
            wrapped += LapMetres;
        }

        double f = wrapped / _spacingMetres;
        int i = Math.Min((int)f, _speed.Length - 1);
        double a = f - i;
        return _speed[i] + ((_speed[(i + 1) % _speed.Length] - _speed[i]) * a);
    }

    // Menger curvature 1/R of the circle through three points, in 1/track unit.
    private static double Curvature(TrackPoint a, TrackPoint b, TrackPoint c)
    {
        double cross = ((double)(b.X - a.X) * (c.Y - a.Y)) - ((double)(b.Y - a.Y) * (c.X - a.X));
        double ab = Length(a, b), bc = Length(b, c), ca = Length(c, a);
        double denominator = ab * bc * ca;
        return denominator > 0 ? 2 * Math.Abs(cross) / denominator : 0;
    }

    private static double Length(TrackPoint a, TrackPoint b) =>
        Math.Sqrt(((double)(b.X - a.X) * (b.X - a.X)) + ((double)(b.Y - a.Y) * (b.Y - a.Y)));
}