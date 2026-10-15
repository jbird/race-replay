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

        double previousEnd = 0;
        foreach (TimelineSegment segment in segments)
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
        Segments = Array.AsReadOnly(segments.ToArray());
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

    /// <summary>The fractional sample index at <paramref name="time"/>, clamped to the grid.</summary>
    public double SampleIndexAt(double time) => Math.Clamp(time * SampleRateHz, 0, SampleCount - 1);
}