using RaceReplay.Domain;

namespace RaceReplay.Analysis;

/// <summary>Cleaning parameters (docs/03 §4, §9).</summary>
public sealed record CleaningOptions
{
    /// <summary>Implied speed, in m/s, above which a sample may be a spike. S1: real speeds reach 107–113 m/s.</summary>
    public double MaxSpeed { get; init; } = 120;

    /// <summary>A longer gap between samples, in seconds, splits the trace into runs. S1: none while a car runs.</summary>
    public double MaxGapSeconds { get; init; } = 2.0;

    /// <summary>Track units per metre (docs/03 §3), to turn distances into speeds.</summary>
    public double UnitsPerMetre { get; init; } = TrackModel.DefaultUnitsPerMetre;
}

/// <summary>What cleaning one driver's trace did, for the build diagnostics (docs/03 §4, step 5).</summary>
/// <param name="SamplesIn">Samples received.</param>
/// <param name="OutOfOrder">Samples that arrived earlier in time than the one before them.</param>
/// <param name="Duplicates">Samples dropped because a later one had the same timestamp.</param>
/// <param name="Placeholders">OpenF1's <c>(0, 0, 0)</c> samples dropped.</param>
/// <param name="Spikes">Isolated position glitches dropped.</param>
/// <param name="Runs">Continuous runs left.</param>
public sealed record CleaningCounts(int SamplesIn, int OutOfOrder, int Duplicates, int Placeholders, int Spikes, int Runs);

/// <summary>
/// One driver's cleaned trace: runs of samples in strictly increasing time, none more than
/// <see cref="CleaningOptions.MaxGapSeconds"/> from the next. Later stages never interpolate between runs.
/// </summary>
public sealed record CleanedTrace(DriverNumber Driver, IReadOnlyList<IReadOnlyList<RawLocationSample>> Runs, CleaningCounts Counts);

/// <summary>
/// Turns a raw per-driver location stream into continuous runs (docs/03 §4): sort and de-duplicate, drop
/// placeholders, drop isolated spikes, and split on gaps. Pure and deterministic (NFR-11).
/// </summary>
public sealed class LocationCleaner
{
    private readonly CleaningOptions _options;
    private readonly double _maxUnitsPerSecond;

    public LocationCleaner(CleaningOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!(options.MaxSpeed > 0) || !double.IsFinite(options.MaxSpeed)
            || !(options.MaxGapSeconds > 0) || !double.IsFinite(options.MaxGapSeconds)
            || !(options.UnitsPerMetre > 0) || !double.IsFinite(options.UnitsPerMetre))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The maximum speed, maximum gap and units per metre must be positive.");
        }

        _options = options;
        _maxUnitsPerSecond = options.MaxSpeed * options.UnitsPerMetre;
    }

    /// <summary>Cleans one driver's samples. The input list is not modified.</summary>
    /// <exception cref="ArgumentException">A sample belongs to another driver.</exception>
    public CleanedTrace Clean(DriverNumber driver, IReadOnlyList<RawLocationSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        int outOfOrder = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].Driver != driver)
            {
                throw new ArgumentException($"Sample {i} belongs to driver {samples[i].Driver}, not {driver}.", nameof(samples));
            }

            if (i > 0 && samples[i].Timestamp < samples[i - 1].Timestamp)
            {
                outOfOrder++;
            }
        }

        // 1. Sort (stable, so samples with one timestamp keep their input order) and keep the last of each timestamp.
        IEnumerable<RawLocationSample> sorted = outOfOrder == 0 ? samples : samples.OrderBy(s => s.Timestamp);
        List<RawLocationSample> unique = new(samples.Count);
        foreach (RawLocationSample sample in sorted)
        {
            if (unique.Count > 0 && unique[^1].Timestamp == sample.Timestamp)
            {
                unique[^1] = sample;
            }
            else
            {
                unique.Add(sample);
            }
        }

        // 2. OpenF1 reports (0, 0, 0) before a car moves, after it stops, and for non-starters (S1).
        List<RawLocationSample> placed = unique.FindAll(s => s.X != 0 || s.Y != 0 || s.Z != 0);

        // 3. Isolated spikes, judged against the neighbours left after steps 1 and 2, so the decisions don't depend
        //    on each other. One fast pair alone is timestamp jitter, which S1 found to be common.
        List<RawLocationSample> kept = new(placed.Count);
        for (int i = 0; i < placed.Count; i++)
        {
            if (i == 0 || i == placed.Count - 1 || !IsSpike(placed[i - 1], placed[i], placed[i + 1]))
            {
                kept.Add(placed[i]);
            }
        }

        // 4. Split wherever consecutive samples are more than MaxGap apart.
        List<IReadOnlyList<RawLocationSample>> runs = [];
        List<RawLocationSample> run = [];
        foreach (RawLocationSample sample in kept)
        {
            if (run.Count > 0 && Seconds(run[^1], sample) > _options.MaxGapSeconds)
            {
                runs.Add(run);
                run = [];
            }

            run.Add(sample);
        }

        if (run.Count > 0)
        {
            runs.Add(run);
        }

        CleaningCounts counts = new(
            samples.Count, outOfOrder, samples.Count - unique.Count, unique.Count - placed.Count, placed.Count - kept.Count, runs.Count);
        return new CleanedTrace(driver, runs, counts);
    }

    private bool IsSpike(RawLocationSample previous, RawLocationSample sample, RawLocationSample next) =>
        IsTooFast(previous, sample) && IsTooFast(sample, next) && !IsTooFast(previous, next);

    // Distance over time against the speed limit, without dividing: Δt is always positive after de-duplication.
    private bool IsTooFast(RawLocationSample from, RawLocationSample to)
    {
        double dx = to.X - (double)from.X;
        double dy = to.Y - (double)from.Y;
        double limit = _maxUnitsPerSecond * Seconds(from, to);
        return (dx * dx) + (dy * dy) > limit * limit;
    }

    private static double Seconds(RawLocationSample from, RawLocationSample to) =>
        (to.Timestamp.UtcTicks - from.Timestamp.UtcTicks) / (double)TimeSpan.TicksPerSecond;
}