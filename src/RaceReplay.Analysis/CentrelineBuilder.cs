using RaceReplay.Domain;

namespace RaceReplay.Analysis;

/// <summary>Centreline parameters (docs/03 §9). M2 adds reference-lap count, spacing and smoothing.</summary>
public sealed record CentrelineOptions
{
    /// <summary>A reference lap with fewer distinct positions can't describe the track.</summary>
    public int MinReferencePoints { get; init; } = 20;
}

/// <summary>
/// The track outline. v0 takes the positions of one clean reference lap: the fastest lap that isn't lap 1, a pit-out
/// lap or an in-lap. M2 replaces this with the multi-lap averaged, aligned and smoothed centreline of docs/03 §5.
/// Point 0 is where the reference lap started, which is the start/finish line.
/// </summary>
public sealed class CentrelineBuilder
{
    private readonly CentrelineOptions _options;

    public CentrelineBuilder(CentrelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MinReferencePoints, 3);
        _options = options;
    }

    /// <exception cref="InsufficientDataException">No lap qualifies as a reference.</exception>
    public TrackModel Build(RawSessionData raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        HashSet<(DriverNumber, int)> pitOut = [.. raw.Laps.Where(l => l.IsPitOutLap).Select(l => (l.Driver, l.LapNumber))];

        IEnumerable<RawLap> candidates = raw.Laps
            .Where(l => l.LapNumber > 1 && !l.IsPitOutLap && l.Start is not null && l.Duration is > 0)
            .Where(l => !pitOut.Contains((l.Driver, l.LapNumber + 1))) // an in-lap ends in the pit lane
            .OrderBy(l => l.Duration)
            .ThenBy(l => l.Driver.Value)
            .ThenBy(l => l.LapNumber);

        foreach (RawLap lap in candidates)
        {
            if (!raw.Locations.TryGetValue(lap.Driver, out IReadOnlyList<RawLocationSample>? samples))
            {
                continue;
            }

            TrackPoint[] points = LapPoints(samples, lap.Start!.Value, lap.Start.Value.AddSeconds(lap.Duration!.Value));
            if (points.Length >= _options.MinReferencePoints)
            {
                return new TrackModel(points);
            }
        }

        throw new InsufficientDataException("No clean lap with enough positions to outline the track.");
    }

    // The lap's positions in order, skipping repeats (a stationary car adds nothing to the outline).
    private static TrackPoint[] LapPoints(IReadOnlyList<RawLocationSample> samples, DateTimeOffset start, DateTimeOffset end)
    {
        List<TrackPoint> points = [];
        foreach (RawLocationSample s in samples)
        {
            if (s.Timestamp < start || s.Timestamp >= end)
            {
                continue;
            }

            TrackPoint p = new(s.X, s.Y);
            if (points.Count == 0 || points[^1] != p)
            {
                points.Add(p);
            }
        }

        return [.. points];
    }
}