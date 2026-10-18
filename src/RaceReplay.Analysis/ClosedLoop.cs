namespace RaceReplay.Analysis;

/// <summary>A point in track units, in double precision for the geometry.</summary>
internal readonly record struct PlanePoint(double X, double Y)
{
    public static double DistanceSquared(PlanePoint a, PlanePoint b) => Square(b.X - a.X) + Square(b.Y - a.Y);

    public static double Distance(PlanePoint a, PlanePoint b) => Math.Sqrt(DistanceSquared(a, b));

    private static double Square(double v) => v * v;
}

/// <summary>
/// Geometry on a closed polyline: the last point joins back to the first. Arc length <c>s</c> runs from point 0.
/// </summary>
internal static class ClosedLoop
{
    /// <summary>cumulative[i] is the arc length to point i; the extra last entry closes the loop and is its length.</summary>
    public static double[] Cumulative(ReadOnlySpan<PlanePoint> points)
    {
        double[] cumulative = new double[points.Length + 1];
        for (int i = 0; i < points.Length; i++)
        {
            cumulative[i + 1] = cumulative[i] + PlanePoint.Distance(points[i], points[(i + 1) % points.Length]);
        }

        return cumulative;
    }

    /// <summary><paramref name="count"/> points evenly spaced by arc length, the first at <paramref name="startAt"/>.</summary>
    public static PlanePoint[] Resample(ReadOnlySpan<PlanePoint> points, double[] cumulative, int count, double startAt = 0)
    {
        double length = cumulative[^1];
        PlanePoint[] resampled = new PlanePoint[count];
        for (int j = 0; j < count; j++)
        {
            resampled[j] = PointAt(points, cumulative, startAt + (j * length / count));
        }

        return resampled;
    }

    /// <summary>The point at arc length <paramref name="s"/>, wrapped onto the loop.</summary>
    public static PlanePoint PointAt(ReadOnlySpan<PlanePoint> points, double[] cumulative, double s)
    {
        double length = cumulative[^1];
        double wrapped = s % length;
        if (wrapped < 0)
        {
            wrapped += length;
        }

        int segment = Array.BinarySearch(cumulative, wrapped);
        if (segment < 0)
        {
            segment = ~segment - 1;
        }

        segment = Math.Clamp(segment, 0, points.Length - 1);
        double span = cumulative[segment + 1] - cumulative[segment];
        double t = span > 0 ? (wrapped - cumulative[segment]) / span : 0;
        PlanePoint a = points[segment];
        PlanePoint b = points[(segment + 1) % points.Length];
        return new PlanePoint(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
    }

    /// <summary>The arc length of the closest point on the loop to <paramref name="p"/> (docs/03 §6.1), by a full search.</summary>
    public static double Project(ReadOnlySpan<PlanePoint> points, double[] cumulative, PlanePoint p)
    {
        double best = double.PositiveInfinity, bestS = 0;
        for (int i = 0; i < points.Length; i++)
        {
            PlanePoint a = points[i];
            PlanePoint b = points[(i + 1) % points.Length];
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double span2 = (abx * abx) + (aby * aby);
            double t = span2 > 0 ? Math.Clamp((((p.X - a.X) * abx) + ((p.Y - a.Y) * aby)) / span2, 0, 1) : 0;
            double d2 = PlanePoint.DistanceSquared(new PlanePoint(a.X + (t * abx), a.Y + (t * aby)), p);
            if (d2 < best)
            {
                best = d2;
                bestS = cumulative[i] + (t * (cumulative[i + 1] - cumulative[i]));
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