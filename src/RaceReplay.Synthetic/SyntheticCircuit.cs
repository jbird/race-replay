using RaceReplay.Domain;

namespace RaceReplay.Synthetic;

/// <summary>A generated, fictional circuit: its name and its exact centreline in track units (decimetres).</summary>
public sealed record SyntheticCircuit(string Name, string MeetingName, TrackModel Track)
{
    public double LapLengthMetres => Track.LapLength / Track.UnitsPerMetre;
}

/// <summary>
/// Builds a closed circuit from seeded control points (a convex hull with displaced edge midpoints), joined by a
/// centripetal Catmull-Rom spline, scaled to 4.5–6 km and started on its straightest stretch (docs/04 §11.2).
/// </summary>
internal static class CircuitGenerator
{
    public const double UnitsPerMetre = TrackModel.DefaultUnitsPerMetre;

    private const double MinLapMetres = 4_500;
    private const double MaxLapMetres = 6_000;
    private const double SpacingMetres = 5;
    private const int SamplesPerSpan = 40;
    private const int MaxAttempts = 100;

    /// <summary>The tightest hairpins on real circuits are about this radius.</summary>
    internal const double MinCornerRadiusMetres = 15;

    public static SyntheticCircuit Generate(Xoshiro256StarStar random)
    {
        (string name, string meetingName) = NameLists.Circuit(random);
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            TrackPoint[] loop = SampleSpline(ControlPoints(random));
            if (SelfIntersects(loop))
            {
                continue;
            }

            double targetMetres = random.NextDouble(MinLapMetres, MaxLapMetres);
            double offsetX = random.NextDouble(-3_000, 3_000);
            double offsetY = random.NextDouble(-3_000, 3_000);
            bool reverse = random.NextBool(0.5);

            TrackModel unscaled = new(loop);
            double scale = targetMetres * UnitsPerMetre / unscaled.LapLength;
            int count = (int)Math.Round(targetMetres / SpacingMetres);
            TrackPoint[] centreline = new TrackPoint[count];
            for (int i = 0; i < count; i++)
            {
                TrackPoint p = unscaled.PointAt(unscaled.LapLength * i / count);
                centreline[i] = new TrackPoint((float)((p.X * scale) + offsetX), (float)((p.Y * scale) + offsetY));
            }

            if (TightestRadiusMetres(centreline) < MinCornerRadiusMetres)
            {
                continue; // a pinched corner no car could take; real hairpins are wider
            }

            if (reverse)
            {
                Array.Reverse(centreline);
            }

            return new SyntheticCircuit(name, meetingName, new TrackModel(StartOnStraight(centreline), UnitsPerMetre));
        }

        throw new InvalidOperationException($"No valid circuit after {MaxAttempts} attempts.");
    }

    // The classic procedural-track recipe: the convex hull of random points gives the straights, and each hull edge
    // gets a midpoint pushed in or out, which adds corners, kinks and concave sections. Points that end up too
    // close are pushed apart so the spline doesn't pinch. The spline then rounds every vertex into a corner.
    private static TrackPoint[] ControlPoints(Xoshiro256StarStar random)
    {
        while (true)
        {
            int count = random.NextInt(10, 16);
            double aspect = random.NextDouble(1.0, 1.8);
            (double X, double Y)[] scattered = new (double, double)[count];
            for (int i = 0; i < count; i++)
            {
                scattered[i] = (random.NextDouble(0, aspect), random.NextDouble(0, 1));
            }

            List<(double X, double Y)> hull = ConvexHull(scattered);
            if (hull.Count < 5)
            {
                continue;
            }

            List<(double X, double Y)> points = new(hull.Count * 2);
            for (int i = 0; i < hull.Count; i++)
            {
                (double ax, double ay) = hull[i];
                (double bx, double by) = hull[(i + 1) % hull.Count];
                double dx = bx - ax, dy = by - ay;
                double length = Math.Sqrt((dx * dx) + (dy * dy));
                double push = random.NextDouble(-0.35, 0.25) * length; // inward (negative) more often than outward
                points.Add((ax, ay));
                points.Add((((ax + bx) / 2) + (dy / length * push), ((ay + by) / 2) - (dx / length * push)));
            }

            PushApart(points, minDistance: 0.12);
            return [.. points.Select(p => new TrackPoint((float)p.X, (float)p.Y))];
        }
    }

    // Andrew's monotone chain; returns the hull anticlockwise.
    private static List<(double X, double Y)> ConvexHull((double X, double Y)[] points)
    {
        (double X, double Y)[] sorted = [.. points.OrderBy(p => p.X).ThenBy(p => p.Y)];
        List<(double X, double Y)> hull = [];
        for (int pass = 0; pass < 2; pass++)
        {
            int start = hull.Count;
            foreach ((double X, double Y) p in sorted)
            {
                while (hull.Count >= start + 2 && Turn(hull[^2], hull[^1], p) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
            Array.Reverse(sorted);
        }

        return hull;
    }

    private static double Turn((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    private static void PushApart(List<(double X, double Y)> points, double minDistance)
    {
        for (int iteration = 0; iteration < 5; iteration++)
        {
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double dx = points[j].X - points[i].X, dy = points[j].Y - points[i].Y;
                    double distance = Math.Sqrt((dx * dx) + (dy * dy));
                    if (distance >= minDistance || distance == 0)
                    {
                        continue;
                    }

                    double shift = (minDistance - distance) / 2 / distance;
                    points[i] = (points[i].X - (dx * shift), points[i].Y - (dy * shift));
                    points[j] = (points[j].X + (dx * shift), points[j].Y + (dy * shift));
                }
            }
        }
    }

    // Closed centripetal Catmull-Rom (alpha = 0.5), which never forms cusps or self-loops within a span.
    private static TrackPoint[] SampleSpline(TrackPoint[] control)
    {
        int n = control.Length;
        TrackPoint[] samples = new TrackPoint[n * SamplesPerSpan];
        for (int i = 0; i < n; i++)
        {
            TrackPoint p0 = control[(i + n - 1) % n];
            TrackPoint p1 = control[i];
            TrackPoint p2 = control[(i + 1) % n];
            TrackPoint p3 = control[(i + 2) % n];
            for (int k = 0; k < SamplesPerSpan; k++)
            {
                samples[(i * SamplesPerSpan) + k] = CatmullRom(p0, p1, p2, p3, (double)k / SamplesPerSpan);
            }
        }

        return samples;
    }

    private static TrackPoint CatmullRom(TrackPoint p0, TrackPoint p1, TrackPoint p2, TrackPoint p3, double u)
    {
        double t0 = 0;
        double t1 = t0 + Math.Sqrt(Distance(p0, p1));
        double t2 = t1 + Math.Sqrt(Distance(p1, p2));
        double t3 = t2 + Math.Sqrt(Distance(p2, p3));
        double t = t1 + (u * (t2 - t1));

        (double x, double y) a1 = Lerp(p0, p1, (t - t0) / (t1 - t0));
        (double x, double y) a2 = Lerp(p1, p2, (t - t1) / (t2 - t1));
        (double x, double y) a3 = Lerp(p2, p3, (t - t2) / (t3 - t2));
        (double x, double y) b1 = Lerp(a1, a2, (t - t0) / (t2 - t0));
        (double x, double y) b2 = Lerp(a2, a3, (t - t1) / (t3 - t1));
        (double x, double y) c = Lerp(b1, b2, (t - t1) / (t2 - t1));
        return new TrackPoint((float)c.x, (float)c.y);
    }

    private static (double, double) Lerp(TrackPoint a, TrackPoint b, double t) => Lerp((a.X, a.Y), (b.X, b.Y), t);

    private static (double, double) Lerp((double x, double y) a, (double x, double y) b, double t) =>
        (a.x + ((b.x - a.x) * t), a.y + ((b.y - a.y) * t));

    private static double Distance(TrackPoint a, TrackPoint b) => Math.Sqrt(Square(b.X - a.X) + Square(b.Y - a.Y));

    private static double Square(double v) => v * v;

    /// <summary>
    /// Radius of the tightest bend in metres, from the circle through each point and its neighbours two points
    /// (≈ 10 m) either side, which smooths out float noise between adjacent points.
    /// </summary>
    internal static double TightestRadiusMetres(ReadOnlySpan<TrackPoint> loop)
    {
        int n = loop.Length;
        double tightest = double.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            TrackPoint a = loop[(i + n - 2) % n], b = loop[i], c = loop[(i + 2) % n];
            double cross = Math.Abs(Cross(a, b, c));
            if (cross == 0)
            {
                continue; // straight
            }

            double radius = Distance(a, b) * Distance(b, c) * Distance(c, a) / (2 * cross);
            tightest = Math.Min(tightest, radius / UnitsPerMetre);
        }

        return tightest;
    }

    /// <summary>Whether any two non-adjacent edges of the closed polyline cross.</summary>
    internal static bool SelfIntersects(ReadOnlySpan<TrackPoint> loop)
    {
        int n = loop.Length;
        for (int i = 0; i < n; i++)
        {
            TrackPoint a = loop[i];
            TrackPoint b = loop[(i + 1) % n];
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1)
                {
                    continue; // the closing edge shares point 0 with the first edge
                }

                if (SegmentsCross(a, b, loop[j], loop[(j + 1) % n]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsCross(TrackPoint p1, TrackPoint p2, TrackPoint q1, TrackPoint q2)
    {
        double d1 = Cross(q1, q2, p1);
        double d2 = Cross(q1, q2, p2);
        double d3 = Cross(p1, p2, q1);
        double d4 = Cross(p1, p2, q2);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double Cross(TrackPoint a, TrackPoint b, TrackPoint c) =>
        ((double)(b.X - a.X) * (c.Y - a.Y)) - ((double)(b.Y - a.Y) * (c.X - a.X));

    // Puts point 0 just after the straightest stretch, so the grid lines up on a straight as it does on real circuits.
    private static TrackPoint[] StartOnStraight(TrackPoint[] centreline)
    {
        const int Behind = 40; // ≈ 200 m of grid and run-up before the line
        const int Ahead = 10;
        int n = centreline.Length;
        double[] turn = new double[n];
        for (int i = 0; i < n; i++)
        {
            turn[i] = Math.Abs(TurnAngle(centreline[(i + n - 1) % n], centreline[i], centreline[(i + 1) % n]));
        }

        int best = 0;
        double bestTurning = double.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            double turning = 0;
            for (int k = -Behind; k <= Ahead; k++)
            {
                turning += turn[((i + k) % n + n) % n];
            }

            if (turning < bestTurning)
            {
                bestTurning = turning;
                best = i;
            }
        }

        TrackPoint[] rotated = new TrackPoint[n];
        for (int i = 0; i < n; i++)
        {
            rotated[i] = centreline[(best + i) % n];
        }

        return rotated;
    }

    private static double TurnAngle(TrackPoint a, TrackPoint b, TrackPoint c)
    {
        double heading1 = Math.Atan2(b.Y - a.Y, b.X - a.X);
        double heading2 = Math.Atan2(c.Y - b.Y, c.X - b.X);
        double delta = heading2 - heading1;
        return Math.IEEERemainder(delta, 2 * Math.PI);
    }
}