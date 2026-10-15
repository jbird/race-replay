using System.Collections.Frozen;

namespace RaceReplay.Domain;

/// <summary>
/// Each car's gap to the virtual leader in seconds, sampled at <see cref="SampleRateHz"/> from T0 (docs/03 §8.1).
/// <c>NaN</c> marks samples where the gap is undefined.
/// </summary>
public sealed class GapSeries
{
    private readonly FrozenDictionary<DriverNumber, ReadOnlyMemory<float>> _gaps;

    public GapSeries(double sampleRateHz, IReadOnlyDictionary<DriverNumber, ReadOnlyMemory<float>> gaps)
    {
        if (!double.IsFinite(sampleRateHz) || sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz), sampleRateHz, "The sample rate must be positive.");
        }

        ArgumentNullException.ThrowIfNull(gaps);

        int sampleCount = -1;
        foreach ((DriverNumber driver, ReadOnlyMemory<float> series) in gaps)
        {
            if (sampleCount >= 0 && series.Length != sampleCount)
            {
                throw new ArgumentException(
                    $"Every gap series needs the same length; driver {driver} has {series.Length}, not {sampleCount}.", nameof(gaps));
            }

            sampleCount = series.Length;
        }

        SampleRateHz = sampleRateHz;
        SampleCount = Math.Max(sampleCount, 0);
        _gaps = gaps.ToFrozenDictionary();
    }

    /// <summary>No gaps: what a replay carries before gaps are derived (M2).</summary>
    public static GapSeries Empty { get; } = new(1, new Dictionary<DriverNumber, ReadOnlyMemory<float>>());

    public double SampleRateHz { get; }

    public int SampleCount { get; }

    public bool IsEmpty => _gaps.Count == 0;

    /// <exception cref="KeyNotFoundException">There is no series for <paramref name="driver"/>.</exception>
    public ReadOnlyMemory<float> GapFor(DriverNumber driver) =>
        _gaps.TryGetValue(driver, out ReadOnlyMemory<float> gap)
            ? gap
            : throw new KeyNotFoundException($"There is no gap series for driver {driver}.");

    public bool TryGetGap(DriverNumber driver, out ReadOnlyMemory<float> gap) => _gaps.TryGetValue(driver, out gap);
}