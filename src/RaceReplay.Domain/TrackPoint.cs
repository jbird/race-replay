namespace RaceReplay.Domain;

/// <summary>A position in the circuit's native coordinate frame ("track units", docs/03 §3).</summary>
public readonly record struct TrackPoint(float X, float Y);

/// <summary>An axis-aligned bounding box in track units.</summary>
public readonly record struct TrackBounds(float MinX, float MinY, float MaxX, float MaxY)
{
    public float Width => MaxX - MinX;

    public float Height => MaxY - MinY;

    /// <summary>The smallest box containing every point.</summary>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty.</exception>
    public static TrackBounds Of(ReadOnlySpan<TrackPoint> points)
    {
        if (points.IsEmpty)
        {
            throw new ArgumentException("Bounds need at least one point.", nameof(points));
        }

        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        foreach (TrackPoint p in points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return new TrackBounds(minX, minY, maxX, maxY);
    }
}