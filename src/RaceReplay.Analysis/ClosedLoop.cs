using RaceReplay.Domain;

namespace RaceReplay.Analysis;

/// <summary>A point in track units, in double precision for the averaging.</summary>
internal readonly record struct PlanePoint(double X, double Y)
{
    public static PlanePoint Of(TrackPoint p) => new(p.X, p.Y);

    public static double DistanceSquared(PlanePoint a, PlanePoint b) => Square(b.X - a.X) + Square(b.Y - a.Y);

    private static double Square(double v) => v * v;
}

/// <summary>
/// Geometry on closed loops for the centreline (docs/03 §5.2): alignment, smoothing and a full-search projection.
/// Arc lengths come from <see cref="TrackModel"/>.
/// </summary>
internal static class ClosedLoop
{
    /// <summary><paramref name="count"/> points evenly spaced by arc length round the loop, the first at <paramref name="startAt"/>.</summary>
    public static PlanePoint[] Resample(TrackModel loop, int count, double startAt = 0)
    {
        PlanePoint[] resampled = new PlanePoint[count];
        for (int j = 0; j < count; j++)
        {
            resampled[j] = PlanePoint.Of(loop.PointAt(startAt + (j * loop.LapLength / count)));
        }

        return resampled;
    }

    /// <summary>The arc length of the closest point on the loop to <paramref name="p"/> (docs/03 §6.1), by a full search.</summary>
    public static double Project(TrackModel loop, PlanePoint p)
    {
        ReadOnlySpan<TrackPoint> points = loop.Centreline.Span;
        double best = double.PositiveInfinity, bestS = 0;
        for (int i = 0; i < points.Length; i++)
        {
            PlanePoint a = PlanePoint.Of(points[i]);
            PlanePoint b = PlanePoint.Of(points[(i + 1) % points.Length]);
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double span2 = (abx * abx) + (aby * aby);
            double t = span2 > 0 ? Math.Clamp((((p.X - a.X) * abx) + ((p.Y - a.Y) * aby)) / span2, 0, 1) : 0;
            double d2 = PlanePoint.DistanceSquared(new PlanePoint(a.X + (t * abx), a.Y + (t * aby)), p);
            if (d2 < best)
            {
                best = d2;
                bestS = loop.ArcLengthAt(i) + (t * (loop.ArcLengthAt(i + 1) - loop.ArcLengthAt(i)));
            }
        }

        return bestS;
    }

    /// <summary>
    /// The circular shift <c>k</c> that best lays <paramref name="other"/> over <paramref name="reference"/>: the least
    /// Σ |reference[i] − other[(i + k) mod n]|² (docs/03 §5.2). O(n²), cut short once a shift can't win. The first
    /// best shift wins a tie.
    /// </summary>
    public static int BestShift(ReadOnlySpan<PlanePoint> reference, ReadOnlySpan<PlanePoint> other)
    {
        int n = reference.Length;
        double best = double.PositiveInfinity;
        int bestShift = 0;
        for (int k = 0; k < n; k++)
        {
            double sum = 0;
            for (int i = 0; i < n && sum < best; i++)
            {
                int j = i + k < n ? i + k : i + k - n;
                sum += PlanePoint.DistanceSquared(reference[i], other[j]);
            }

            if (sum < best)
            {
                best = sum;
                bestShift = k;
            }
        }

        return bestShift;
    }

    /// <summary>A circular moving average over <paramref name="window"/> points (odd), wrapping across point 0.</summary>
    public static PlanePoint[] Smooth(ReadOnlySpan<PlanePoint> points, int window)
    {
        int n = points.Length, half = window / 2;
        PlanePoint[] smoothed = new PlanePoint[n];
        for (int i = 0; i < n; i++)
        {
            double x = 0, y = 0;
            for (int j = -half; j <= half; j++)
            {
                PlanePoint p = points[(((i + j) % n) + n) % n];
                x += p.X;
                y += p.Y;
            }

            smoothed[i] = new PlanePoint(x / window, y / window);
        }

        return smoothed;
    }

    /// <summary><paramref name="s"/> wrapped into (−length / 2, length / 2]: the signed way round from 0.</summary>
    public static double Signed(double s, double length)
    {
        double wrapped = s % length;
        if (wrapped <= -length / 2)
        {
            wrapped += length;
        }
        else if (wrapped > length / 2)
        {
            wrapped -= length;
        }

        return wrapped;
    }
}