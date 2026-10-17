using RaceReplay.Domain;
using RaceReplay.Synthetic;

namespace RaceReplay.Analysis.Tests;

public sealed class LocationCleanerTests
{
    private const double Speed = 80; // m/s, a fast but ordinary racing speed

    private static readonly DriverNumber s_driver = new(1);
    private static readonly DateTimeOffset s_base = SyntheticCar.DefaultStart;
    private static readonly LocationCleaner s_cleaner = new(new CleaningOptions());
    private static readonly SyntheticTrack s_circle = SyntheticTrack.Circle(500);

    private static SyntheticCar Car(double seconds = 10) => SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(seconds);

    [Fact]
    public void Clean_CleanTrace_KeepsEverySampleInOneRun()
    {
        IReadOnlyList<RawLocationSample> samples = Car().Samples();

        CleanedTrace trace = s_cleaner.Clean(s_driver, samples);

        Assert.Equal(s_driver, trace.Driver);
        Assert.Equal(samples, Assert.Single(trace.Runs));
        Assert.Equal(new CleaningCounts(samples.Count, 0, 0, 0, 0, 1), trace.Counts);
    }

    [Fact]
    public void Clean_SamplesOutOfOrder_ReturnsThemInTimeOrder()
    {
        IReadOnlyList<RawLocationSample> expected = Car().Samples();

        CleanedTrace trace = s_cleaner.Clean(s_driver, Car().WithOutOfOrder(4).WithOutOfOrder(20).Samples());

        Assert.Equal(expected, Assert.Single(trace.Runs));
        Assert.Equal(2, trace.Counts.OutOfOrder);
    }

    [Fact]
    public void Clean_ReversedInput_ReturnsTimeOrder()
    {
        IReadOnlyList<RawLocationSample> expected = Car().Samples();

        CleanedTrace trace = s_cleaner.Clean(s_driver, [.. expected.Reverse()]);

        Assert.Equal(expected, Assert.Single(trace.Runs));
        Assert.Equal(expected.Count - 1, trace.Counts.OutOfOrder);
    }

    [Fact]
    public void Clean_DuplicateTimestamp_KeepsTheLastAndCountsIt()
    {
        IReadOnlyList<RawLocationSample> clean = Car().Samples();
        IReadOnlyList<RawLocationSample> samples = Car().WithDuplicate(5, metres: 3).Samples();

        CleanedTrace trace = s_cleaner.Clean(s_driver, samples);

        IReadOnlyList<RawLocationSample> run = Assert.Single(trace.Runs);
        Assert.Equal(clean.Count, run.Count);
        Assert.Equal(samples[6], run[5]); // the second of the two, 3 m further along x
        Assert.Equal(1, trace.Counts.Duplicates);
    }

    [Fact]
    public void Clean_DuplicateArrivingFirst_StillKeepsTheLastInInputOrder()
    {
        RawLocationSample first = At(1, 100, 0);
        RawLocationSample second = At(1, 120, 0);

        CleanedTrace trace = s_cleaner.Clean(s_driver, [At(2, 300, 0), first, At(0, 0, 10), second]);

        Assert.Equal([At(0, 0, 10), second, At(2, 300, 0)], Assert.Single(trace.Runs));
        Assert.Equal(1, trace.Counts.Duplicates);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(37)] // the last of ⌊10 × 3.7⌋ + 1 = 38 samples
    public void Clean_Placeholder_DropsItWherever(int at)
    {
        IReadOnlyList<RawLocationSample> clean = Car().Samples();

        CleanedTrace trace = s_cleaner.Clean(s_driver, Car().WithZeroPlaceholder(at).Samples());

        Assert.Equal(clean.Where((_, i) => i != at), Assert.Single(trace.Runs));
        Assert.Equal(1, trace.Counts.Placeholders);
    }

    [Fact]
    public void Clean_ZeroXYWithHeight_IsNotAPlaceholder()
    {
        RawLocationSample[] samples = [At(0, 0, 0, z: 5), At(0.25, 10, 0)];

        CleanedTrace trace = s_cleaner.Clean(s_driver, samples);

        Assert.Equal(samples, Assert.Single(trace.Runs));
    }

    [Fact]
    public void Clean_Spike_DropsItAndCountsIt()
    {
        IReadOnlyList<RawLocationSample> clean = Car().Samples();

        CleanedTrace trace = s_cleaner.Clean(s_driver, Car().WithSpike(12).Samples());

        Assert.Equal(clean.Where((_, i) => i != 12), Assert.Single(trace.Runs));
        Assert.Equal(1, trace.Counts.Spikes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    public void Clean_SpikeAtEitherEnd_KeepsIt(int at)
    {
        IReadOnlyList<RawLocationSample> samples = Car().WithSpike(at).Samples();

        CleanedTrace trace = s_cleaner.Clean(s_driver, samples);

        Assert.Equal(samples, Assert.Single(trace.Runs)); // one neighbour can't tell a glitch from a jump
        Assert.Equal(0, trace.Counts.Spikes);
    }

    [Fact]
    public void Clean_SpikeNextToAPlaceholder_IsJudgedAgainstTheSamplesLeft()
    {
        IReadOnlyList<RawLocationSample> clean = Car().Samples();

        CleanedTrace trace = s_cleaner.Clean(s_driver, Car().WithZeroPlaceholder(11).WithSpike(12).Samples());

        Assert.Equal(clean.Where((_, i) => i is not (11 or 12)), Assert.Single(trace.Runs));
        Assert.Equal((1, 1), (trace.Counts.Placeholders, trace.Counts.Spikes));
    }

    [Fact]
    public void Clean_TwoConsecutiveGlitches_KeepsBoth()
    {
        IReadOnlyList<RawLocationSample> samples = Car().WithSpike(12).WithSpike(13).Samples();

        CleanedTrace trace = s_cleaner.Clean(s_driver, samples);

        // The documented limit of the rule: each glitch has one plausible neighbour, the other glitch. The projector's
        // plausibility gate catches what is left (docs/03 §6.3, RR-033).
        Assert.Equal(samples, Assert.Single(trace.Runs));
        Assert.Equal(0, trace.Counts.Spikes);
    }

    [Fact]
    public void Clean_OneImplausiblePair_KeepsBothSamples()
    {
        // 80 m/s along x, sampled every 0.27 s, but sample 2 is stamped 0.24 s early: 21.6 m in 0.03 s is 720 m/s.
        RawLocationSample[] samples = [At(0, 0, 0, z: 1), At(0.27, 216, 0), At(0.30, 432, 0), At(0.81, 648, 0), At(1.08, 864, 0)];

        CleanedTrace trace = s_cleaner.Clean(s_driver, samples);

        Assert.Equal(samples, Assert.Single(trace.Runs));
    }

    [Fact]
    public void Clean_S1LikeTimestampJitter_DropsNothing()
    {
        // An hour at 80 m/s with S1's kind of noise: timestamps shift, positions don't.
        IReadOnlyList<RawLocationSample> samples = Car(3_600).WithTimestampJitter(0.03, 0.12, seed: 1).Samples();
        int fastPairs = samples.Zip(samples.Skip(1)).Count(pair => SpeedBetween(pair.First, pair.Second) > 120);

        CleanedTrace trace = s_cleaner.Clean(s_driver, samples);

        Assert.InRange(fastPairs, 100, 1_000); // S1: 150–350 consecutive pairs over 120 m/s per car-hour
        Assert.Equal(samples, Assert.Single(trace.Runs));
    }

    [Fact]
    public void Clean_HeightJump_IsIgnored()
    {
        RawLocationSample[] samples = [At(0, 0, 0, z: 1), At(0.25, 200, 0, z: 90_000), At(0.5, 400, 0, z: 1)];

        CleanedTrace trace = s_cleaner.Clean(s_driver, samples);

        Assert.Equal(samples, Assert.Single(trace.Runs));
    }

    [Fact]
    public void Clean_Dropout_SplitsIntoTwoRuns()
    {
        IReadOnlyList<RawLocationSample> clean = Car().Samples();

        CleanedTrace trace = s_cleaner.Clean(s_driver, Car().WithDropout(2, 5).Samples());

        // Samples 8 to 18 fall in [2 s, 5 s), leaving 7 (1.89 s) to 19 (5.14 s): 3.24 s apart.
        Assert.Equal(2, trace.Runs.Count);
        Assert.Equal(clean.Take(8), trace.Runs[0]);
        Assert.Equal(clean.Skip(19), trace.Runs[1]);
        Assert.Equal(2, trace.Counts.Runs);
    }

    [Fact]
    public void Clean_GapOfExactlyMaxGap_StaysOneRun()
    {
        RawLocationSample[] samples = [At(0, 0, 0, z: 1), At(2.0, 100, 0)];

        CleanedTrace trace = s_cleaner.Clean(s_driver, samples);

        Assert.Single(trace.Runs);
    }

    [Fact]
    public void Clean_GapJustOverMaxGap_Splits()
    {
        RawLocationSample[] samples = [At(0, 0, 0, z: 1), At(2.001, 100, 0)];

        CleanedTrace trace = s_cleaner.Clean(s_driver, samples);

        Assert.Equal(2, trace.Runs.Count);
        Assert.Equal([samples[0]], trace.Runs[0]);
        Assert.Equal([samples[1]], trace.Runs[1]);
    }

    [Fact]
    public void Clean_EmptyInput_NoRuns()
    {
        CleanedTrace trace = s_cleaner.Clean(s_driver, []);

        Assert.Empty(trace.Runs);
        Assert.Equal(new CleaningCounts(0, 0, 0, 0, 0, 0), trace.Counts);
    }

    [Fact]
    public void Clean_OnlyPlaceholders_NoRuns()
    {
        CleanedTrace trace = s_cleaner.Clean(s_driver, [At(0, 0, 0), At(1, 0, 0), At(2, 0, 0)]);

        Assert.Empty(trace.Runs);
        Assert.Equal(new CleaningCounts(3, 0, 0, 3, 0, 0), trace.Counts);
    }

    [Fact]
    public void Clean_OneSample_OneRun()
    {
        RawLocationSample sample = At(5, 10, 20);

        CleanedTrace trace = s_cleaner.Clean(s_driver, [sample]);

        Assert.Equal([sample], Assert.Single(trace.Runs));
    }

    [Fact]
    public void Clean_DoesNotModifyTheInput()
    {
        RawLocationSample[] samples = [.. Car().WithOutOfOrder(3).WithDuplicate(9).WithSpike(15).Samples()];
        RawLocationSample[] copy = [.. samples];

        _ = s_cleaner.Clean(s_driver, samples);

        Assert.Equal(copy, samples);
    }

    [Fact]
    public void Clean_SameInputTwice_SameOutput()
    {
        IReadOnlyList<RawLocationSample> samples = Car(60).WithTimestampJitter(0.03, 0.12, 3).WithSpike(30).WithDropout(20, 25).Samples();

        CleanedTrace first = s_cleaner.Clean(s_driver, samples);
        CleanedTrace second = s_cleaner.Clean(s_driver, samples);

        Assert.Equal(first.Counts, second.Counts);
        Assert.Equal(first.Runs.Count, second.Runs.Count);
        Assert.All(first.Runs.Zip(second.Runs), pair => Assert.Equal(pair.First, pair.Second));
    }

    [Fact]
    public void Clean_SampleOfAnotherDriver_Throws()
    {
        RawLocationSample[] samples = [At(0, 0, 10), At(1, 10, 10) with { Driver = new DriverNumber(44) }];

        Assert.Throws<ArgumentException>(() => s_cleaner.Clean(s_driver, samples));
    }

    [Fact]
    public void Clean_LowerSpeedLimit_DropsWhatItMakesTooFast()
    {
        RawLocationSample[] samples = [At(0, 0, 0, z: 1), At(1, 300, 0), At(2, 0, 0, z: 1)]; // out at 30 m/s and back

        CleanedTrace standard = s_cleaner.Clean(s_driver, samples);
        CleanedTrace strict = new LocationCleaner(new CleaningOptions { MaxSpeed = 20 }).Clean(s_driver, samples);

        Assert.Equal(samples, Assert.Single(standard.Runs));
        Assert.Equal([samples[0], samples[2]], Assert.Single(strict.Runs));
    }

    [Fact]
    public void Clean_UnitsPerMetre_ScalesTheSpeedLimit()
    {
        // 300 units each way in 0.25 s: 120 m/s at 10 units per metre, but 240 m/s at 5.
        RawLocationSample[] samples = [At(0, 0, 0, z: 1), At(0.25, 300, 0), At(0.5, 0, 0, z: 1)];

        Assert.Single(new LocationCleaner(new CleaningOptions()).Clean(s_driver, samples).Runs[0], s => s.X == 300);
        Assert.DoesNotContain(
            new LocationCleaner(new CleaningOptions { UnitsPerMetre = 5 }).Clean(s_driver, samples).Runs[0], s => s.X == 300);
    }

    public static TheoryData<CleaningOptions> InvalidOptions =>
    [
        new CleaningOptions { MaxSpeed = 0 },
        new CleaningOptions { MaxSpeed = double.PositiveInfinity },
        new CleaningOptions { MaxGapSeconds = -1 },
        new CleaningOptions { MaxGapSeconds = double.NaN },
        new CleaningOptions { UnitsPerMetre = 0 },
    ];

    [Theory, MemberData(nameof(InvalidOptions))]
    public void Constructor_InvalidOptions_Throws(CleaningOptions options) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocationCleaner(options));

    private static RawLocationSample At(double seconds, int x, int y, int z = 0) =>
        new(s_base.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond)), s_driver, x, y, z);

    private static double SpeedBetween(RawLocationSample a, RawLocationSample b) =>
        Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2)) / SyntheticTrack.UnitsPerMetre / (b.Timestamp - a.Timestamp).TotalSeconds;
}