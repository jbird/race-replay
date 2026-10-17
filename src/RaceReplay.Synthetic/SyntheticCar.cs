using System.Diagnostics;
using System.Runtime.CompilerServices;

using RaceReplay.Domain;

namespace RaceReplay.Synthetic;

/// <summary>
/// One car's location samples for small analytic worlds (docs/06 §5): a car driven at constant speed round a
/// <see cref="SyntheticTrack"/>, sampled like OpenF1, with faults injected on request. The builder is immutable: each
/// <c>With…</c> returns a new car. Samples are a pure function of the settings, so tests can rebuild the clean trace
/// a faulty one came from.
/// </summary>
/// <remarks>
/// Fault positions are sample indices of the clean trace, before any fault is applied. Each sample takes at most one
/// fault, so faults never interfere and their order doesn't matter.
/// </remarks>
public sealed record SyntheticCar
{
    /// <summary>About OpenF1's location rate (docs/03 §7, spike S1).</summary>
    public const double DefaultSampleRateHz = 3.7;

    /// <summary>The <c>z</c> every sample reports, in track units: a constant, since the design never uses height.</summary>
    public const int Altitude = 100;

    /// <summary>The default displacement of a spike, far beyond anything a car can cover between samples.</summary>
    public const double DefaultSpikeMetres = 500;

    public static readonly DateTimeOffset DefaultStart = new(2026, 3, 1, 13, 0, 0, TimeSpan.Zero);

    private SyntheticCar(SyntheticTrack track, double speedMetresPerSecond, double startProgressMetres)
    {
        Track = track;
        SpeedMetresPerSecond = speedMetresPerSecond;
        StartProgressMetres = startProgressMetres;
    }

    public SyntheticTrack Track { get; }

    public double SpeedMetresPerSecond { get; }

    /// <summary>How far round the track the car is at the first sample, in metres.</summary>
    public double StartProgressMetres { get; }

    public DriverNumber Driver { get; private init; } = new(1);

    /// <summary>The time of the first sample.</summary>
    public DateTimeOffset Start { get; private init; } = DefaultStart;

    public double SampleRateHz { get; private init; } = DefaultSampleRateHz;

    /// <summary>Seconds from the first sample to the last.</summary>
    public double DurationSeconds { get; private init; } = 60;

    private Jitter? TimestampJitter { get; init; }

    private IReadOnlyList<Fault> Faults { get; init; } = [];

    /// <summary>A car at <paramref name="speedMetresPerSecond"/> from <paramref name="startProgressMetres"/> round the track.</summary>
    public static SyntheticCar ConstantSpeed(SyntheticTrack track, double speedMetresPerSecond, double startProgressMetres = 0)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!(speedMetresPerSecond >= 0) || !double.IsFinite(speedMetresPerSecond))
        {
            throw new ArgumentOutOfRangeException(nameof(speedMetresPerSecond), speedMetresPerSecond, "The speed must be finite and not negative.");
        }

        if (!double.IsFinite(startProgressMetres))
        {
            throw new ArgumentOutOfRangeException(nameof(startProgressMetres), startProgressMetres, "The start must be finite.");
        }

        return new SyntheticCar(track, speedMetresPerSecond, startProgressMetres);
    }

    public SyntheticCar WithDriver(DriverNumber driver) => this with { Driver = driver };

    public SyntheticCar WithStart(DateTimeOffset start) => this with { Start = start };

    public SyntheticCar WithDuration(double seconds) =>
        double.IsFinite(seconds) && seconds >= 0
            ? this with { DurationSeconds = seconds }
            : throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "The duration must be finite and not negative.");

    public SyntheticCar WithSampleRate(double hz) =>
        double.IsFinite(hz) && hz > 0
            ? this with { SampleRateHz = hz }
            : throw new ArgumentOutOfRangeException(nameof(hz), hz, "The sample rate must be positive.");

    /// <summary>
    /// Shifts each reported timestamp by Gaussian noise (standard deviation <paramref name="sdSeconds"/>, clamped to
    /// ±<paramref name="maxSeconds"/>), while the position stays where the car truly was. That is how OpenF1's
    /// timestamps jitter (spike S1). <paramref name="maxSeconds"/> must stay under half the sample interval, so the
    /// samples keep their order.
    /// </summary>
    public SyntheticCar WithTimestampJitter(double sdSeconds, double maxSeconds, int seed)
    {
        if (!(sdSeconds >= 0) || !double.IsFinite(sdSeconds) || !(maxSeconds >= 0) || !(maxSeconds < 0.5 / SampleRateHz))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSeconds), "The jitter must be finite and not negative, and its bound under half the sample interval.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(seed);

        return this with { TimestampJitter = new Jitter(sdSeconds, maxSeconds, seed) };
    }

    /// <summary>Moves sample <paramref name="at"/> <paramref name="metres"/> away along x: an isolated position glitch.</summary>
    public SyntheticCar WithSpike(int at, double metres = DefaultSpikeMetres) => Add(new Spike(at, Distance(metres)));

    /// <summary>Replaces sample <paramref name="at"/> with OpenF1's <c>(0, 0, 0)</c> placeholder, keeping its timestamp.</summary>
    public SyntheticCar WithZeroPlaceholder(int at) => Add(new Placeholder(at));

    /// <summary>Removes every sample from <paramref name="fromSeconds"/> (inclusive) to <paramref name="toSeconds"/>
    /// (exclusive), in seconds after the first sample, before any jitter.</summary>
    public SyntheticCar WithDropout(double fromSeconds, double toSeconds) =>
        double.IsFinite(fromSeconds) && double.IsFinite(toSeconds) && fromSeconds < toSeconds
            ? Add(new Dropout(fromSeconds, toSeconds))
            : throw new ArgumentOutOfRangeException(nameof(toSeconds), "A dropout must end after it starts.");

    /// <summary>Adds a second sample with the same timestamp right after sample <paramref name="at"/>,
    /// <paramref name="metres"/> away along x.</summary>
    public SyntheticCar WithDuplicate(int at, double metres = 0) => Add(new Duplicate(at, Distance(metres)));

    /// <summary>Swaps sample <paramref name="at"/> with the one after it in the returned list.</summary>
    public SyntheticCar WithOutOfOrder(int at) => Add(new OutOfOrder(at));

    /// <summary>The number of samples in the clean trace.</summary>
    public int SampleCount => (int)Math.Floor((DurationSeconds * SampleRateHz) + 1e-9) + 1;

    /// <summary>The samples, in the order a provider would return them, faults included.</summary>
    /// <exception cref="InvalidOperationException">
    /// A fault refers to a sample the trace doesn't have, or two faults touch the same sample. Each sample takes at most
    /// one fault; an out-of-order fault takes its sample and the next, and a dropout every sample in its window.
    /// </exception>
    public IReadOnlyList<RawLocationSample> Samples()
    {
        int count = SampleCount;
        Jitter? jitter = TimestampJitter;
        if (jitter is not null && !(jitter.MaxSeconds < 0.5 / SampleRateHz))
        {
            throw new InvalidOperationException("The jitter bound must stay under half the sample interval; the sample rate changed after it was set.");
        }

        CheckFaults(count);
        Xoshiro256StarStar? random = jitter is null ? null : SyntheticRandom.For(jitter.Seed, RandomChannel.Observation);

        List<RawLocationSample>[] slots = new List<RawLocationSample>[count];
        for (int i = 0; i < count; i++)
        {
            double time = i / SampleRateHz;
            double reported = random is null
                ? time
                : time + Math.Clamp(random.NextGaussian() * jitter!.SdSeconds, -jitter.MaxSeconds, jitter.MaxSeconds);
            (double x, double y) = Track.PointAt(StartProgressMetres + (SpeedMetresPerSecond * time));
            slots[i] = [Sample(reported, x, y, Altitude)];
        }

        // CheckFaults guarantees every touched sample is still alone in its slot.
        foreach (Fault fault in Faults)
        {
            switch (fault)
            {
                case Spike spike:
                    RawLocationSample s = slots[spike.At][0];
                    slots[spike.At][0] = s with { X = s.X + Units(spike.Metres) };
                    break;
                case Placeholder placeholder:
                    slots[placeholder.At][0] = slots[placeholder.At][0] with { X = 0, Y = 0, Z = 0 };
                    break;
                case Duplicate duplicate:
                    RawLocationSample d = slots[duplicate.At][0];
                    slots[duplicate.At].Add(d with { X = d.X + Units(duplicate.Metres) });
                    break;
                case Dropout dropout:
                    foreach (int i in DropoutSamples(dropout, count))
                    {
                        slots[i].Clear();
                    }

                    break;
            }
        }

        List<RawLocationSample> samples = [.. slots.SelectMany(slot => slot)];
        foreach (OutOfOrder swap in Faults.OfType<OutOfOrder>())
        {
            int position = slots.Take(swap.At).Sum(slot => slot.Count); // after the duplicates and dropouts before it
            (samples[position], samples[position + 1]) = (samples[position + 1], samples[position]);
        }

        return samples;

        RawLocationSample Sample(double seconds, double x, double y, int z) => new(
            Start.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond)), Driver, (int)Math.Round(x), (int)Math.Round(y), z);
    }

    // Every fault must refer to samples the trace has, and no two may touch the same sample, so the result doesn't
    // depend on the order the faults were added in.
    private void CheckFaults(int count)
    {
        Dictionary<int, Fault> owners = [];
        foreach (Fault fault in Faults)
        {
            IEnumerable<int> touched = fault switch
            {
                Spike f => [f.At],
                Placeholder f => [f.At],
                Duplicate f => [f.At],
                OutOfOrder f => [f.At, f.At + 1],
                Dropout f => DropoutSamples(f, count),
                _ => throw new UnreachableException(),
            };
            foreach (int i in touched)
            {
                if (i < 0 || i >= count)
                {
                    throw new InvalidOperationException($"{fault}: the trace has samples 0 to {count - 1}.");
                }

                if (!owners.TryAdd(i, fault))
                {
                    throw new InvalidOperationException($"{owners[i]} and {fault} both touch sample {i}; give each sample one fault.");
                }
            }
        }
    }

    private IEnumerable<int> DropoutSamples(Dropout dropout, int count) =>
        Enumerable.Range(0, count).Where(i => i / SampleRateHz >= dropout.FromSeconds && i / SampleRateHz < dropout.ToSeconds);

    // A displacement a test can mean: finite, and well inside the int range of track units.
    private static double Distance(double metres, [CallerArgumentExpression(nameof(metres))] string? name = null) =>
        double.IsFinite(metres) && Math.Abs(metres) <= 100_000
            ? metres
            : throw new ArgumentOutOfRangeException(name, metres, "The distance must be finite and at most 100 km.");

    private static int Units(double metres) => (int)Math.Round(metres * SyntheticTrack.UnitsPerMetre);

    private SyntheticCar Add(Fault fault) => this with { Faults = [.. Faults, fault] };

    private sealed record Jitter(double SdSeconds, double MaxSeconds, int Seed);

    private abstract record Fault;

    private sealed record Spike(int At, double Metres) : Fault;

    private sealed record Placeholder(int At) : Fault;

    private sealed record Duplicate(int At, double Metres) : Fault;

    private sealed record Dropout(double FromSeconds, double ToSeconds) : Fault;

    private sealed record OutOfOrder(int At) : Fault;
}