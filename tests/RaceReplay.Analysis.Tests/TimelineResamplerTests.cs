using RaceReplay.Domain;

namespace RaceReplay.Analysis.Tests;

public sealed class TimelineResamplerTests
{
    private static readonly DateTimeOffset s_base = new(2026, 3, 1, 13, 0, 0, TimeSpan.Zero);
    private static readonly DriverNumber s_driver = new(44);
    private static readonly TimelineResampler s_resampler = new(new ResamplingOptions());

    [Fact]
    public void Grid_Samples_StartsAtTheEarliestWholeSecondAndCoversTheLatest()
    {
        IReadOnlyList<RawLocationSample>[] cars =
        [
            [At(1.3, 0, 0), At(5.0, 0, 0)],
            [At(0.7, 0, 0), At(9.1, 0, 0)],
        ];

        (DateTimeOffset t0, int count) = s_resampler.Grid(cars);

        Assert.Equal(s_base, t0);                               // 0.7 s rounds down to the whole second
        Assert.Equal(38, count);                                // ⌈9.1 × 4⌉ + 1: the last grid point (9.25 s) covers 9.1 s
    }

    [Fact]
    public void Grid_NoSamples_ThrowsInsufficientData() =>
        Assert.Throws<InsufficientDataException>(() => s_resampler.Grid([[], []]));

    [Fact]
    public void Resample_UniformMotion_InterpolatesExactly()
    {
        // 10 units per second along x, sampled every 0.27 s (as OpenF1's ~3.7 Hz).
        RawLocationSample[] samples = [.. Enumerable.Range(0, 20).Select(i => At(i * 0.27, (int)Math.Round(i * 2.7 * 10), 500))];

        CarChannels car = s_resampler.Resample(s_driver, samples, s_base, 21);

        for (int k = 0; k < 21; k++)
        {
            Assert.True(car.Valid[k], $"sample {k}");
            Assert.Equal(k * 0.25 * 100, car.X.Span[k], 1e-3);
            Assert.Equal(500, car.Y.Span[k]);
        }
    }

    [Fact]
    public void Resample_SampleExactlyOnAGridPoint_TakesItAsIs()
    {
        CarChannels car = s_resampler.Resample(s_driver, [At(0.5, 123, 456)], s_base, 4);

        Assert.True(car.Valid[2]);
        Assert.Equal((123f, 456f), (car.X.Span[2], car.Y.Span[2]));
        Assert.False(car.Valid[1]); // before the only sample
        Assert.False(car.Valid[3]); // after it
    }

    [Fact]
    public void Resample_GapLongerThanMax_LeavesItInvalidAndHoldsTheLastValue()
    {
        RawLocationSample[] samples = [At(0, 0, 0), At(1, 10, 0), At(4, 40, 0), At(5, 50, 0)]; // 3 s gap after 1 s

        CarChannels car = s_resampler.Resample(s_driver, samples, s_base, 21);

        Assert.True(car.Valid[4]);  // 1.0 s, on a sample
        Assert.False(car.Valid[5]); // 1.25 s, inside the gap
        Assert.False(car.Valid[15]); // 3.75 s, inside the gap
        Assert.True(car.Valid[16]); // 4.0 s, on a sample
        Assert.Equal(10, car.X.Span[10]); // holds the last valid value
    }

    [Fact]
    public void Resample_GapExactlyMax_StillInterpolates()
    {
        CarChannels car = s_resampler.Resample(s_driver, [At(0, 0, 0), At(2, 20, 0)], s_base, 9);

        Assert.True(car.Valid[4]);
        Assert.Equal(10, car.X.Span[4], 1e-4);
    }

    [Fact]
    public void Resample_NoSamples_AllInvalid()
    {
        CarChannels car = s_resampler.Resample(s_driver, [], s_base, 10);

        Assert.Equal(0, car.Valid.CountValid());
        Assert.Equal(s_driver, car.Driver);
        Assert.False(car.HasProgress);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(4, 0)]
    [InlineData(double.NaN, 2)]
    public void Constructor_NonPositiveOptions_Throw(double hz, double maxGap) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TimelineResampler(new ResamplingOptions { TimelineHz = hz, MaxGapSeconds = maxGap }));

    [Fact]
    public void Resample_CleanedTrace_NeverInterpolatesBetweenRuns()
    {
        // A cleaner with a 1 s limit splits this 1.5 s gap; the resampler alone (2 s) would bridge it.
        RawLocationSample[] samples = [At(0, 0, 10), At(0.5, 50, 10), At(2, 200, 10), At(2.5, 250, 10)]; // y = 10: never a placeholder
        CleanedTrace trace = new LocationCleaner(new CleaningOptions { MaxGapSeconds = 1 }).Clean(s_driver, samples);

        CarChannels raw = s_resampler.Resample(s_driver, samples, s_base, 11);
        CarChannels cleaned = s_resampler.Resample(trace, s_base, 11);

        Assert.Equal(2, trace.Runs.Count);
        Assert.True(raw.Valid[4]);                                  // 1.0 s, inside the gap
        Assert.Equal([true, true, true, false, false, false, false, false, true, true, true],
            Enumerable.Range(0, 11).Select(k => cleaned.Valid[k])); // 0.75–1.75 s fall between the runs
        Assert.Equal(200, cleaned.X.Span[8]);
    }

    [Fact]
    public void Resample_CleanedTraceWithOneRun_MatchesTheRawSamples()
    {
        RawLocationSample[] samples = [.. Enumerable.Range(0, 20).Select(i => At(i * 0.27, i * 27, 500))];
        CleanedTrace trace = new LocationCleaner(new CleaningOptions()).Clean(s_driver, samples);

        CarChannels raw = s_resampler.Resample(s_driver, samples, s_base, 21);
        CarChannels cleaned = s_resampler.Resample(trace, s_base, 21);

        Assert.Equal(raw.X.ToArray(), cleaned.X.ToArray());
        Assert.Equal(raw.Valid.Bits.ToArray(), cleaned.Valid.Bits.ToArray());
    }

    [Fact]
    public void TimelineHz_Default_IsFour() => Assert.Equal(4, s_resampler.TimelineHz);

    private static RawLocationSample At(double seconds, int x, int y) =>
        new(s_base.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond)), s_driver, x, y, 0);
}