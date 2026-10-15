using System.Collections.ObjectModel;

namespace RaceReplay.Domain;

/// <summary>
/// The fixed sample grid every car channel shares (docs/03 §7.1). Sample <c>k</c> is at
/// <c>k / SampleRateHz</c> seconds after <see cref="T0Utc"/>; all derived times are seconds since T0.
/// </summary>
public sealed class ReplayTimeline
{
    public ReplayTimeline(
        DateTimeOffset t0Utc,
        double sampleRateHz,
        int sampleCount,
        double raceStart,
        IReadOnlyList<TimelineSegment> segments)
    {
        if (t0Utc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("T0 must be a UTC timestamp.", nameof(t0Utc));
        }

        if (!double.IsFinite(sampleRateHz) || sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz), sampleRateHz, "The sample rate must be positive.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCount);
        ArgumentNullException.ThrowIfNull(segments);

        double duration = (sampleCount - 1) / sampleRateHz;
        if (!double.IsFinite(raceStart) || raceStart < 0 || raceStart > duration)
        {
            throw new ArgumentOutOfRangeException(nameof(raceStart), raceStart, $"The race start must lie within [0, {duration}].");
        }

        // Copy first, then validate the copy: what is checked is exactly what is kept.
        TimelineSegment[] owned = [.. segments];
        double previousEnd = 0;
        foreach (TimelineSegment segment in owned)
        {
            if (!double.IsFinite(segment.Start) || !double.IsFinite(segment.End)
                || segment.Start < previousEnd || segment.End < segment.Start || segment.End > duration)
            {
                throw new ArgumentException(
                    $"Segments must be ordered, non-overlapping and within [0, {duration}]; {segment} is not.", nameof(segments));
            }

            previousEnd = segment.End;
        }

        T0Utc = t0Utc;
        SampleRateHz = sampleRateHz;
        SampleCount = sampleCount;
        RaceStart = raceStart;
        Duration = duration;
        Segments = Array.AsReadOnly(owned);
    }

    public DateTimeOffset T0Utc { get; }

    public double SampleRateHz { get; }

    public int SampleCount { get; }

    /// <summary>Seconds from T0 to the start of lap 1.</summary>
    public double RaceStart { get; }

    /// <summary>Seconds from the first sample to the last.</summary>
    public double Duration { get; }

    public ReadOnlyCollection<TimelineSegment> Segments { get; }

    /// <summary>Seconds since T0 of sample <paramref name="index"/>.</summary>
    public double TimeAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, SampleCount);
        return index / SampleRateHz;
    }

    /// <summary>
    /// The fractional sample index at <paramref name="time"/>, clamped to the grid. NaN maps to 0 rather than
    /// passing through, because callers cast the result to an array index.
    /// </summary>
    public double SampleIndexAt(double time) =>
        double.IsNaN(time) ? 0 : Math.Clamp(time * SampleRateHz, 0, SampleCount - 1);

    /// <summary>
    /// How many samples a series at <paramref name="rateHz"/> needs to span this timeline from T0:
    /// ⌊Duration × rate⌋ + 1 (docs/03 §8.1). Builders and readers both use this, so they agree on gap lengths.
    /// </summary>
    public int SampleCountAtRate(double rateHz)
    {
        if (!double.IsFinite(rateHz) || rateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rateHz), rateHz, "The sample rate must be positive.");
        }

        // The epsilon absorbs representation error, e.g. a 1 Hz series over 7,199.999999… s still gets 7,201 samples.
        return checked((int)Math.Floor((Duration * rateHz) + 1e-9) + 1);
    }
}