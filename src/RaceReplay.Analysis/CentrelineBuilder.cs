using System.Globalization;

using RaceReplay.Domain;

namespace RaceReplay.Analysis;

/// <summary>Centreline parameters (docs/03 §9).</summary>
public sealed record CentrelineOptions
{
    /// <summary>Clean laps to average; fewer if the session has fewer.</summary>
    public int ReferenceLapCount { get; init; } = 5;

    /// <summary>Distance between centreline points, in metres: about 1,000 points for a 5 km lap.</summary>
    public double SpacingMetres { get; init; } = 5;

    /// <summary>Points in the circular moving average that removes residual jitter. Odd; 1 turns smoothing off.</summary>
    public int SmoothingWindow { get; init; } = 5;

    /// <summary>A reference lap longer or shorter than the median by more than this fraction is discarded.</summary>
    public double MaxLapLengthDeviation { get; init; } = 0.02;

    /// <summary>A reference lap may take at most this multiple of the median green-flag lap time: the 107 % rule.</summary>
    public double MaxLapTimeRatio { get; init; } = 1.07;

    /// <summary>A reference lap with fewer distinct positions can't describe the track.</summary>
    public int MinReferencePoints { get; init; } = 20;

    /// <summary>Track units per metre (docs/03 §3), for the spacing and the diagnostics.</summary>
    public double UnitsPerMetre { get; init; } = TrackModel.DefaultUnitsPerMetre;
}

/// <summary>A lap the centreline was built from, or one the length check discarded.</summary>
public sealed record ReferenceLap(DriverNumber Driver, int LapNumber, double DurationSeconds, double LengthMetres);

/// <summary>How many laps each reference-lap rule excluded (docs/03 §5.1). A lap counts under the first rule it fails.</summary>
/// <param name="LapOne">Lap 1, which starts on the grid.</param>
/// <param name="PitInOrOut">Pit-out laps, and in-laps: those followed by a pit-out lap.</param>
/// <param name="NoTiming">Laps without a start or a positive duration.</param>
/// <param name="Neutralised">Laps overlapping a safety car, virtual safety car or red-flag period.</param>
/// <param name="TooSlow">Laps over <see cref="CentrelineOptions.MaxLapTimeRatio"/> × the median green-flag lap.</param>
/// <param name="NotCovered">Laps no single cleaned run of positions covers.</param>
/// <param name="TooFewPositions">Laps with fewer than <see cref="CentrelineOptions.MinReferencePoints"/> distinct positions.</param>
public sealed record LapExclusions(int LapOne, int PitInOrOut, int NoTiming, int Neutralised, int TooSlow, int NotCovered, int TooFewPositions)
{
    public int Total => LapOne + PitInOrOut + NoTiming + Neutralised + TooSlow + NotCovered + TooFewPositions;
}

/// <summary>What building the centreline did, for the build report (docs/03 §5).</summary>
/// <param name="LapsChecked">Lap records in the session.</param>
/// <param name="Excluded">Laps each rule excluded; the rest were candidates.</param>
/// <param name="ReferenceLaps">The laps averaged, fastest first; the first is the base the others align to.</param>
/// <param name="DiscardedByLength">Chosen laps the length check discarded.</param>
/// <param name="Boundaries">Lap starts behind the start/finish estimate.</param>
/// <param name="BoundarySpreadMetres">Their median distance from it, round the loop.</param>
/// <param name="LapLengthMetres">The centreline's length.</param>
public sealed record CentrelineDiagnostics(
    int LapsChecked,
    LapExclusions Excluded,
    IReadOnlyList<ReferenceLap> ReferenceLaps,
    IReadOnlyList<ReferenceLap> DiscardedByLength,
    int Boundaries,
    double BoundarySpreadMetres,
    double LapLengthMetres);

public sealed record CentrelineResult(TrackModel Track, CentrelineDiagnostics Diagnostics);

/// <summary>
/// Builds the track outline from several clean laps (docs/03 §5): chooses reference laps, resamples each by arc
/// length, aligns them to the fastest, averages and smooths them, puts point 0 on the start/finish line, and lays the
/// circuit out landscape. Pure and deterministic (NFR-11): the result doesn't depend on the order of the inputs.
/// </summary>
public sealed class CentrelineBuilder
{
    private readonly CentrelineOptions _options;
    private readonly double _spacing; // track units

    public CentrelineBuilder(CentrelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ReferenceLapCount < 1
            || !(options.SpacingMetres > 0) || !double.IsFinite(options.SpacingMetres)
            || options.SmoothingWindow < 1 || options.SmoothingWindow % 2 == 0
            || !(options.MaxLapLengthDeviation >= 0) || !double.IsFinite(options.MaxLapLengthDeviation)
            || !(options.MaxLapTimeRatio >= 1) || !double.IsFinite(options.MaxLapTimeRatio)
            || options.MinReferencePoints < 3
            || !(options.UnitsPerMetre > 0) || !double.IsFinite(options.UnitsPerMetre))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Centreline options: at least one reference lap and three reference points, a positive spacing and units per metre, an odd smoothing window, a lap-time ratio of at least 1 and a length deviation of at least 0.");
        }

        _options = options;
        _spacing = options.SpacingMetres * options.UnitsPerMetre;
    }

    /// <summary>Builds the centreline from the session's laps and race control, and each car's cleaned positions.</summary>
    /// <exception cref="InsufficientDataException">No lap qualifies as a reference.</exception>
    public CentrelineResult Build(RawSessionData raw, IReadOnlyDictionary<DriverNumber, CleanedTrace> traces)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(traces);

        (List<Candidate> candidates, LapExclusions excluded) = Candidates(raw, traces);
        if (candidates.Count == 0)
        {
            throw new InsufficientDataException(NoReferenceMessage(raw.Laps.Count, excluded));
        }

        // §5.1: up to ReferenceLapCount, fastest first, then the length check against the median.
        List<Candidate> chosen = Choose(candidates);
        double medianLength = chosen.Select(c => c.Length).Order().ElementAt((chosen.Count - 1) / 2); // the lower middle: one lap always survives
        List<Candidate> kept = [.. chosen.Where(c => Math.Abs(c.Length - medianLength) <= _options.MaxLapLengthDeviation * medianLength)];
        List<Candidate> discarded = [.. chosen.Except(kept)];

        // §5.2: resample each lap into M points by its own arc length, align to the base, average, smooth.
        int m = Math.Max(3, (int)Math.Ceiling(kept[0].Length / _spacing));
        PlanePoint[] baseLap = Resample(kept[0].Positions, m);
        double[] sumX = new double[m], sumY = new double[m];
        foreach (Candidate lap in kept)
        {
            bool isBase = ReferenceEquals(lap, kept[0]);
            PlanePoint[] points = isBase ? baseLap : Resample(lap.Positions, m);
            int shift = isBase ? 0 : ClosedLoop.BestShift(baseLap, points);
            for (int i = 0; i < m; i++)
            {
                PlanePoint p = points[(i + shift) % m];
                sumX[i] += p.X;
                sumY[i] += p.Y;
            }
        }

        PlanePoint[] mean = [.. Enumerable.Range(0, m).Select(i => new PlanePoint(sumX[i] / kept.Count, sumY[i] / kept.Count))];
        PlanePoint[] smoothed = _options.SmoothingWindow > 1 ? ClosedLoop.Smooth(mean, _options.SmoothingWindow) : mean;
        double[] cumulative = ClosedLoop.Cumulative(smoothed);
        double length = cumulative[^1];

        // §5.3: point 0 at the circular median of every car's lap starts, then the final resampling from there.
        List<double> boundaries = Boundaries(raw, traces, smoothed, cumulative);
        (double startFinish, double spread) = CircularMedian(boundaries, length);
        PlanePoint[] centreline = ClosedLoop.Resample(smoothed, cumulative, Math.Max(3, (int)Math.Ceiling(length / _spacing)), startFinish);

        TrackPoint[] outline = [.. centreline.Select(p => new TrackPoint((float)p.X, (float)p.Y))];
        TrackModel track = new(outline, _options.UnitsPerMetre, LandscapeRotation(centreline));
        CentrelineDiagnostics diagnostics = new(
            raw.Laps.Count,
            excluded,
            [.. kept.Select(Describe)],
            [.. discarded.Select(Describe)],
            boundaries.Count,
            spread / _options.UnitsPerMetre,
            track.LapLength / _options.UnitsPerMetre);
        return new CentrelineResult(track, diagnostics);
    }

    // Every lap passing the rules of docs/03 §5.1, with its positions, and how many each rule excluded.
    private (List<Candidate> Candidates, LapExclusions Excluded) Candidates(RawSessionData raw, IReadOnlyDictionary<DriverNumber, CleanedTrace> traces)
    {
        HashSet<(DriverNumber, int)> pitOut = [.. raw.Laps.Where(l => l.IsPitOutLap).Select(l => (l.Driver, l.LapNumber))];
        IReadOnlyList<TimeWindow> neutralised = NeutralisedPeriods.From(raw.RaceControl);
        int lapOne = 0, pit = 0, noTiming = 0, periods = 0, slow = 0, notCovered = 0, fewPositions = 0;

        List<(RawLap Lap, DateTimeOffset Start, DateTimeOffset End)> green = [];
        foreach (RawLap lap in raw.Laps)
        {
            if (lap.LapNumber <= 1)
            {
                lapOne++;
            }
            else if (lap.IsPitOutLap || pitOut.Contains((lap.Driver, lap.LapNumber + 1)))
            {
                pit++;
            }
            else if (lap.Start is not DateTimeOffset start || lap.Duration is not double duration || !(duration > 0) || !double.IsFinite(duration))
            {
                noTiming++;
            }
            else if (neutralised.Any(p => p.Overlaps(start, After(start, duration))))
            {
                periods++;
            }
            else
            {
                green.Add((lap, start, After(start, duration)));
            }
        }

        List<Candidate> candidates = [];
        double medianTime = Median([.. green.Select(g => g.Lap.Duration!.Value)]);
        foreach ((RawLap lap, DateTimeOffset start, DateTimeOffset end) in green)
        {
            if (lap.Duration!.Value > _options.MaxLapTimeRatio * medianTime)
            {
                slow++;
            }
            else if (!traces.TryGetValue(lap.Driver, out CleanedTrace? trace) || RunCovering(trace, start, end) is not { } run)
            {
                notCovered++;
            }
            else if (DistinctPositions(run, start, end) < _options.MinReferencePoints)
            {
                fewPositions++;
            }
            else
            {
                PlanePoint[] positions = LapPositions(run, start, end);
                candidates.Add(new Candidate(lap, positions, ClosedLoop.Cumulative(positions)[^1]));
            }
        }

        return (candidates, new LapExclusions(lapOne, pit, noTiming, periods, slow, notCovered, fewPositions));
    }

    // Fastest first, one lap per driver before anyone gets a second; ties to the lower driver number, then lap number.
    private List<Candidate> Choose(List<Candidate> candidates)
    {
        Candidate[] ranked = [.. candidates.OrderBy(c => c.Lap.Duration).ThenBy(c => c.Lap.Driver.Value).ThenBy(c => c.Lap.LapNumber)];
        List<Candidate> chosen = [];
        HashSet<DriverNumber> drivers = [];
        foreach (Candidate c in ranked)
        {
            if (chosen.Count < _options.ReferenceLapCount && drivers.Add(c.Lap.Driver))
            {
                chosen.Add(c);
            }
        }

        foreach (Candidate c in ranked)
        {
            if (chosen.Count < _options.ReferenceLapCount && !chosen.Contains(c))
            {
                chosen.Add(c);
            }
        }

        return [.. chosen.OrderBy(c => Array.IndexOf(ranked, c))];
    }

    // Each car's position at the start of every lap from lap 2, projected onto the loop. Lap 1 starts on the grid,
    // behind the line (docs/03 §10). Drivers in number order, so the result doesn't depend on the dictionary's order.
    private static List<double> Boundaries(RawSessionData raw, IReadOnlyDictionary<DriverNumber, CleanedTrace> traces, PlanePoint[] loop, double[] cumulative)
    {
        List<double> boundaries = [];
        foreach (IGrouping<DriverNumber, RawLap> laps in raw.Laps.Where(l => l.LapNumber >= 2 && l.Start is not null).GroupBy(l => l.Driver).OrderBy(g => g.Key.Value))
        {
            if (!traces.TryGetValue(laps.Key, out CleanedTrace? trace))
            {
                continue;
            }

            foreach (RawLap lap in laps)
            {
                if (RunCovering(trace, lap.Start!.Value, lap.Start.Value) is { } run)
                {
                    boundaries.Add(ClosedLoop.Project(loop, cumulative, PositionAt(run, lap.Start.Value)));
                }
            }
        }

        return boundaries;
    }

    // The circular median: unwrap round the circular mean, take the ordinary median, wrap back. A minority of
    // outliers anywhere on the loop can't move it far. The spread is the median distance from it, round the loop.
    private static (double Median, double Spread) CircularMedian(List<double> values, double length)
    {
        if (values.Count == 0)
        {
            return (0, 0);
        }

        double[] sorted = [.. values.Order()];
        double sx = 0, sy = 0;
        foreach (double v in sorted)
        {
            double angle = 2 * Math.PI * v / length;
            sx += Math.Cos(angle);
            sy += Math.Sin(angle);
        }

        double centre = sx == 0 && sy == 0 ? 0 : Math.Atan2(sy, sx) * length / (2 * Math.PI);
        double median = Median([.. sorted.Select(v => centre + ClosedLoop.Signed(v - centre, length))]);
        double spread = Median([.. sorted.Select(v => Math.Abs(ClosedLoop.Signed(v - median, length)))]);
        median %= length;
        return (median < 0 ? median + length : median, spread);
    }

    // The rotation, in degrees anticlockwise and in (−90, 90], that turns the principal axis horizontal. A shape with
    // no clear long axis (principal variances within 1 %) stays as it is.
    private static float LandscapeRotation(PlanePoint[] points)
    {
        double mx = points.Average(p => p.X), my = points.Average(p => p.Y);
        double sxx = 0, syy = 0, sxy = 0;
        foreach (PlanePoint p in points)
        {
            sxx += (p.X - mx) * (p.X - mx);
            syy += (p.Y - my) * (p.Y - my);
            sxy += (p.X - mx) * (p.Y - my);
        }

        double half = (sxx - syy) / 2, r = Math.Sqrt((half * half) + (sxy * sxy));
        double major = ((sxx + syy) / 2) + r;
        if (2 * r <= 0.01 * major)
        {
            return 0;
        }

        double rotation = -0.5 * Math.Atan2(2 * sxy, sxx - syy) * 180 / Math.PI;
        return (float)(rotation <= -90 ? rotation + 180 : rotation);
    }

    private static PlanePoint[] Resample(PlanePoint[] positions, int count) =>
        ClosedLoop.Resample(positions, ClosedLoop.Cumulative(positions), count);

    // The run with samples at or before the start and at or after the end, if there is one.
    private static IReadOnlyList<RawLocationSample>? RunCovering(CleanedTrace trace, DateTimeOffset start, DateTimeOffset end) =>
        trace.Runs.FirstOrDefault(r => r.Count > 0 && r[0].Timestamp <= start && r[^1].Timestamp >= end);

    // The lap's positions: the ends interpolated at its start and end, with the samples in between.
    private static PlanePoint[] LapPositions(IReadOnlyList<RawLocationSample> run, DateTimeOffset start, DateTimeOffset end)
    {
        List<PlanePoint> positions = [PositionAt(run, start)];
        for (int i = FirstAfter(run, start); i < run.Count && run[i].Timestamp < end; i++)
        {
            positions.Add(new PlanePoint(run[i].X, run[i].Y));
        }

        positions.Add(PositionAt(run, end));
        return [.. positions];
    }

    // Distinct consecutive positions from the lap's start to its end: a parked car adds nothing.
    private static int DistinctPositions(IReadOnlyList<RawLocationSample> run, DateTimeOffset start, DateTimeOffset end)
    {
        int distinct = 0;
        RawLocationSample? previous = null;
        for (int i = FirstAtOrAfter(run, start); i < run.Count && run[i].Timestamp <= end; i++)
        {
            if (previous is not { } p || p.X != run[i].X || p.Y != run[i].Y)
            {
                distinct++;
            }

            previous = run[i];
        }

        return distinct;
    }

    // Linear interpolation inside a run that covers the time.
    private static PlanePoint PositionAt(IReadOnlyList<RawLocationSample> run, DateTimeOffset at)
    {
        int next = FirstAtOrAfter(run, at);
        RawLocationSample b = run[next];
        if (b.Timestamp == at || next == 0)
        {
            return new PlanePoint(b.X, b.Y);
        }

        RawLocationSample a = run[next - 1];
        double t = (at.UtcTicks - a.Timestamp.UtcTicks) / (double)(b.Timestamp.UtcTicks - a.Timestamp.UtcTicks);
        return new PlanePoint(a.X + ((b.X - (double)a.X) * t), a.Y + ((b.Y - (double)a.Y) * t));
    }

    private static int FirstAtOrAfter(IReadOnlyList<RawLocationSample> run, DateTimeOffset at) => Search(run, at, inclusive: true);

    private static int FirstAfter(IReadOnlyList<RawLocationSample> run, DateTimeOffset at) => Search(run, at, inclusive: false);

    // The first index whose time is at or after (inclusive) or after (exclusive) the given time; run is in time order.
    private static int Search(IReadOnlyList<RawLocationSample> run, DateTimeOffset at, bool inclusive)
    {
        int lo = 0, hi = run.Count;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) / 2);
            bool before = inclusive ? run[mid].Timestamp < at : run[mid].Timestamp <= at;
            if (before)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private ReferenceLap Describe(Candidate c) =>
        new(c.Lap.Driver, c.Lap.LapNumber, c.Lap.Duration!.Value, c.Length / _options.UnitsPerMetre);

    private static DateTimeOffset After(DateTimeOffset start, double seconds) =>
        start.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));

    private static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return double.NaN;
        }

        Array.Sort(values);
        int middle = values.Length / 2;
        return values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }

    private static string NoReferenceMessage(int laps, LapExclusions e) => string.Create(
        CultureInfo.InvariantCulture,
        $"No lap can outline the track. Of {laps} laps: {e.LapOne} lap 1, {e.PitInOrOut} in or out of the pits, {e.NoTiming} without timing, {e.Neutralised} under a safety car, VSC or red flag, {e.TooSlow} too slow, {e.NotCovered} without continuous positions, {e.TooFewPositions} with too few positions.");

    private sealed record Candidate(RawLap Lap, PlanePoint[] Positions, double Length);
}