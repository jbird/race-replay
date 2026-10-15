using RaceReplay.Domain;

namespace RaceReplay.Analysis;

/// <summary>Resampling parameters (docs/03 §9).</summary>
public sealed record ResamplingOptions
{
    /// <summary>Grid rate; just above the raw ~3.7 Hz to avoid aliasing.</summary>
    public double TimelineHz { get; init; } = 4;

    /// <summary>Never interpolate across a longer gap between raw samples.</summary>
    public double MaxGapSeconds { get; init; } = 2.0;
}

/// <summary>
/// Puts every car on one fixed timeline (docs/03 §7). v0 resamples positions only; race progress joins in M2.
/// </summary>
public sealed class TimelineResampler
{
    private readonly ResamplingOptions _options;

    public TimelineResampler(ResamplingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!(options.TimelineHz > 0) || !double.IsFinite(options.TimelineHz) || !(options.MaxGapSeconds > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The timeline rate and maximum gap must be positive.");
        }

        _options = options;
    }

    public double TimelineHz => _options.TimelineHz;

    /// <summary>
    /// The grid covering every sample: T0 is the earliest sample rounded down to a whole second, and the last grid
    /// point is at or after the latest sample (docs/03 §7.1).
    /// </summary>
    /// <exception cref="InsufficientDataException">There are no samples at all.</exception>
    public (DateTimeOffset T0, int SampleCount) Grid(IEnumerable<IReadOnlyList<RawLocationSample>> cars)
    {
        ArgumentNullException.ThrowIfNull(cars);
        long earliest = long.MaxValue, latest = long.MinValue;
        foreach (IReadOnlyList<RawLocationSample> samples in cars)
        {
            if (samples.Count > 0)
            {
                earliest = Math.Min(earliest, samples[0].Timestamp.UtcTicks);
                latest = Math.Max(latest, samples[^1].Timestamp.UtcTicks);
            }
        }

        if (earliest == long.MaxValue)
        {
            throw new InsufficientDataException("The session has no location samples.");
        }

        DateTimeOffset t0 = new(earliest - (earliest % TimeSpan.TicksPerSecond), TimeSpan.Zero);
        double span = (latest - t0.UtcTicks) / (double)TimeSpan.TicksPerSecond;
        return (t0, checked((int)Math.Ceiling(span * _options.TimelineHz) + 1));
    }

    /// <summary>
    /// Linear interpolation of one car's positions onto the grid, with a two-pointer sweep (O(samples + grid)).
    /// A grid point is valid only between two raw samples at most <see cref="ResamplingOptions.MaxGapSeconds"/>
    /// apart, or exactly on one; elsewhere it holds the last valid value (which carries no meaning).
    /// </summary>
    /// <remarks>Samples must be in ascending time order, as providers guarantee.</remarks>
    public CarChannels Resample(DriverNumber driver, IReadOnlyList<RawLocationSample> samples, DateTimeOffset t0, int sampleCount)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCount);

        float[] x = new float[sampleCount];
        float[] y = new float[sampleCount];
        bool[] valid = new bool[sampleCount];
        double[] times = new double[samples.Count];
        for (int i = 0; i < times.Length; i++)
        {
            times[i] = (samples[i].Timestamp.UtcTicks - t0.UtcTicks) / (double)TimeSpan.TicksPerSecond;
        }

        float lastX = 0, lastY = 0;
        int next = 0; // first raw sample at or after the grid time
        for (int k = 0; k < sampleCount; k++)
        {
            double t = k / _options.TimelineHz;
            while (next < times.Length && times[next] < t)
            {
                next++;
            }

            if (next < times.Length && times[next] == t)
            {
                (lastX, lastY) = (samples[next].X, samples[next].Y);
                valid[k] = true;
            }
            else if (next > 0 && next < times.Length && times[next] - times[next - 1] <= _options.MaxGapSeconds)
            {
                RawLocationSample a = samples[next - 1], b = samples[next];
                double alpha = (t - times[next - 1]) / (times[next] - times[next - 1]);
                lastX = (float)(a.X + ((b.X - a.X) * alpha));
                lastY = (float)(a.Y + ((b.Y - a.Y) * alpha));
                valid[k] = true;
            }

            x[k] = lastX;
            y[k] = lastY;
        }

        return new CarChannels(driver, x, y, ValidityMask.FromBools(valid));
    }
}