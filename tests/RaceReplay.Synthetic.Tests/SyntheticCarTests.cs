using RaceReplay.Domain;

namespace RaceReplay.Synthetic.Tests;

public sealed class SyntheticCarTests
{
    private const double Radius = 500;  // m
    private const double Speed = 80;    // m/s
    private const double PositionTolerance = 0.5; // units; samples are rounded to whole track units

    private static readonly SyntheticTrack s_circle = SyntheticTrack.Circle(Radius);

    [Fact]
    public void Circle_Length_IsTwoPiR()
    {
        Assert.Equal(2 * Math.PI * Radius, s_circle.LengthMetres, 1e-9);
    }

    [Fact]
    public void Circle_PointAt_LiesOnTheCircleAroundTheCentre()
    {
        foreach (double metres in new[] { 0.0, 123.4, 1_000, -50, 10_000 })
        {
            (double x, double y) = s_circle.PointAt(metres);

            double dx = (x / SyntheticTrack.UnitsPerMetre) - SyntheticTrack.CentreXMetres;
            double dy = (y / SyntheticTrack.UnitsPerMetre) - SyntheticTrack.CentreYMetres;
            Assert.Equal(Radius, Math.Sqrt((dx * dx) + (dy * dy)), 1e-6);
        }
    }

    [Fact]
    public void Circle_PointAt_RepeatsEveryLap()
    {
        Assert.Equal(s_circle.PointAt(100).X, s_circle.PointAt(100 + s_circle.LengthMetres).X, 1e-6);
        Assert.Equal(s_circle.PointAt(100).Y, s_circle.PointAt(100 - s_circle.LengthMetres).Y, 1e-6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Circle_InvalidRadius_Throws(double radius) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticTrack.Circle(radius));

    [Fact]
    public void ConstantSpeed_Samples_AreEvenlySpacedAtTheRateAndFollowTheTrack()
    {
        SyntheticCar car = SyntheticCar.ConstantSpeed(s_circle, Speed, startProgressMetres: 25).WithDuration(10);

        IReadOnlyList<RawLocationSample> samples = car.Samples();

        Assert.Equal(38, samples.Count); // ⌊10 × 3.7⌋ + 1
        for (int i = 0; i < samples.Count; i++)
        {
            double seconds = i / SyntheticCar.DefaultSampleRateHz;
            (double x, double y) = s_circle.PointAt(25 + (Speed * seconds));
            Assert.Equal(SyntheticCar.DefaultStart.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond)), samples[i].Timestamp);
            Assert.Equal(x, samples[i].X, PositionTolerance);
            Assert.Equal(y, samples[i].Y, PositionTolerance);
            Assert.Equal(SyntheticCar.Altitude, samples[i].Z);
            Assert.Equal(new DriverNumber(1), samples[i].Driver);
        }
    }

    [Fact]
    public void ConstantSpeed_Settings_AreUsed()
    {
        DateTimeOffset start = new(2025, 12, 7, 13, 0, 0, TimeSpan.Zero);
        SyntheticCar car = SyntheticCar.ConstantSpeed(s_circle, Speed)
            .WithDriver(new DriverNumber(44))
            .WithStart(start)
            .WithSampleRate(2)
            .WithDuration(3);

        IReadOnlyList<RawLocationSample> samples = car.Samples();

        Assert.Equal([start, start.AddSeconds(0.5), start.AddSeconds(1), start.AddSeconds(1.5), start.AddSeconds(2), start.AddSeconds(2.5), start.AddSeconds(3)],
            samples.Select(s => s.Timestamp));
        Assert.All(samples, s => Assert.Equal(new DriverNumber(44), s.Driver));
    }

    [Fact]
    public void With_ReturnsANewCar_AndLeavesTheOriginalUnchanged()
    {
        SyntheticCar clean = SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(5);

        SyntheticCar faulty = clean.WithSpike(3);

        Assert.NotSame(clean, faulty);
        Assert.Equal(clean.Samples(), SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(5).Samples());
        Assert.NotEqual(clean.Samples(), faulty.Samples());
    }

    [Fact]
    public void WithTimestampJitter_MovesTimestampsWithinTheBoundAndKeepsPositionsAndOrder()
    {
        SyntheticCar clean = SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(120);
        IReadOnlyList<RawLocationSample> expected = clean.Samples();

        IReadOnlyList<RawLocationSample> jittered = clean.WithTimestampJitter(0.03, 0.12, seed: 5).Samples();

        Assert.Equal(expected.Count, jittered.Count);
        int moved = 0;
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal((expected[i].X, expected[i].Y), (jittered[i].X, jittered[i].Y)); // positions are where the car was
            double shift = (jittered[i].Timestamp - expected[i].Timestamp).TotalSeconds;
            Assert.InRange(shift, -0.12 - 1e-7, 0.12 + 1e-7);
            moved += shift == 0 ? 0 : 1;
            if (i > 0)
            {
                Assert.True(jittered[i].Timestamp > jittered[i - 1].Timestamp, $"sample {i} out of order");
            }
        }

        Assert.True(moved > expected.Count * 0.9, $"only {moved} of {expected.Count} timestamps moved");
    }

    [Fact]
    public void WithTimestampJitter_SameSeed_SameSamples_OtherSeed_Different()
    {
        SyntheticCar car = SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(30);

        Assert.Equal(car.WithTimestampJitter(0.03, 0.1, 1).Samples(), car.WithTimestampJitter(0.03, 0.1, 1).Samples());
        Assert.NotEqual(car.WithTimestampJitter(0.03, 0.1, 1).Samples(), car.WithTimestampJitter(0.03, 0.1, 2).Samples());
    }

    [Theory]
    [InlineData(-0.01, 0.1, 1)]
    [InlineData(0.03, 0.14, 1)] // half the 0.27 s interval or more would let samples swap
    [InlineData(0.03, 0.1, -1)]
    public void WithTimestampJitter_InvalidSettings_Throw(double sd, double max, int seed) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticCar.ConstantSpeed(s_circle, Speed).WithTimestampJitter(sd, max, seed));

    [Fact]
    public void WithTimestampJitter_SampleRateRaisedAfterwards_ThrowsOnSamples()
    {
        SyntheticCar car = SyntheticCar.ConstantSpeed(s_circle, Speed).WithTimestampJitter(0.03, 0.1, 1).WithSampleRate(10);

        Assert.Throws<InvalidOperationException>(() => car.Samples());
    }

    [Fact]
    public void WithSpike_MovesOnlyThatSampleAlongX()
    {
        SyntheticCar clean = SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(5);
        IReadOnlyList<RawLocationSample> expected = clean.Samples();

        IReadOnlyList<RawLocationSample> samples = clean.WithSpike(7, metres: 300).Samples();

        Assert.Equal(expected.Count, samples.Count);
        Assert.Equal(expected[7] with { X = expected[7].X + 3_000 }, samples[7]);
        Assert.Equal(expected.Where((_, i) => i != 7), samples.Where((_, i) => i != 7));
    }

    [Fact]
    public void WithZeroPlaceholder_ZeroesThatSampleAndKeepsItsTimestamp()
    {
        SyntheticCar clean = SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(5);
        IReadOnlyList<RawLocationSample> expected = clean.Samples();

        IReadOnlyList<RawLocationSample> samples = clean.WithZeroPlaceholder(4).Samples();

        Assert.Equal(expected[4] with { X = 0, Y = 0, Z = 0 }, samples[4]);
        Assert.Equal(expected.Where((_, i) => i != 4), samples.Where((_, i) => i != 4));
    }

    [Fact]
    public void WithDropout_RemovesTheSamplesInTheWindow()
    {
        SyntheticCar clean = SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(10);
        IReadOnlyList<RawLocationSample> expected = clean.Samples();

        IReadOnlyList<RawLocationSample> samples = clean.WithDropout(2, 5).Samples();

        // Samples 8 to 18 fall in [2 s, 5 s): 8 / 3.7 = 2.16 s and 18 / 3.7 = 4.86 s; 7 is at 1.89 s and 19 at 5.14 s.
        Assert.Equal(expected.Where((_, i) => i is < 8 or > 18), samples);
    }

    [Fact]
    public void WithDuplicate_AddsASampleWithTheSameTimestampRightAfter()
    {
        SyntheticCar clean = SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(5);
        IReadOnlyList<RawLocationSample> expected = clean.Samples();

        IReadOnlyList<RawLocationSample> samples = clean.WithDuplicate(6, metres: 2).Samples();

        Assert.Equal(expected.Count + 1, samples.Count);
        Assert.Equal(expected[6], samples[6]);
        Assert.Equal(expected[6] with { X = expected[6].X + 20 }, samples[7]);
        Assert.Equal(expected.Skip(7), samples.Skip(8));
    }

    [Fact]
    public void WithOutOfOrder_SwapsThatSampleWithTheNext()
    {
        SyntheticCar clean = SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(5);
        IReadOnlyList<RawLocationSample> expected = clean.Samples();

        IReadOnlyList<RawLocationSample> samples = clean.WithOutOfOrder(3).Samples();

        Assert.Equal((expected[4], expected[3]), (samples[3], samples[4]));
        Assert.Equal(expected.Where((_, i) => i is not (3 or 4)), samples.Where((_, i) => i is not (3 or 4)));
    }

    [Fact]
    public void WithOutOfOrder_AfterADuplicate_SwapsWhereTheSampleEndedUp()
    {
        SyntheticCar clean = SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(5);
        IReadOnlyList<RawLocationSample> expected = clean.Samples();

        IReadOnlyList<RawLocationSample> samples = clean.WithDuplicate(1).WithOutOfOrder(5).Samples();

        Assert.Equal((expected[6], expected[5]), (samples[6], samples[7])); // one place later, after the extra sample
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(19)] // ⌊5 × 3.7⌋ + 1 = 19 samples: 0 to 18
    public void Fault_IndexOutsideTheTrace_ThrowsOnSamples(int at)
    {
        SyntheticCar car = SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(5).WithSpike(at);

        InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => car.Samples());
        Assert.Contains("Spike", e.Message, StringComparison.Ordinal); // names the fault, not a parameter of Samples()
    }

    public static TheoryData<string> OverlappingFaults => ["placeholder in a dropout", "dropout over a placeholder", "spike on a duplicate", "adjacent out of order", "out of order onto a spike"];

    [Theory, MemberData(nameof(OverlappingFaults))]
    public void Faults_TouchingTheSameSample_ThrowOnSamples_WhateverTheOrder(string scenario)
    {
        SyntheticCar car = SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(5);
        car = scenario switch
        {
            "placeholder in a dropout" => car.WithDropout(1, 2).WithZeroPlaceholder(5), // sample 5 is at 1.35 s
            "dropout over a placeholder" => car.WithZeroPlaceholder(5).WithDropout(1, 2),
            "spike on a duplicate" => car.WithDuplicate(10).WithSpike(10),
            "adjacent out of order" => car.WithOutOfOrder(3).WithOutOfOrder(4),
            _ => car.WithSpike(8).WithOutOfOrder(7),
        };

        InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => car.Samples());
        Assert.Contains("one fault", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithOutOfOrder_LastSample_ThrowsOnSamples()
    {
        SyntheticCar car = SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(5).WithOutOfOrder(18);

        Assert.Throws<InvalidOperationException>(() => car.Samples());
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e6)] // 1,000 km would overflow the track units
    public void FaultDistance_Invalid_Throws(double metres)
    {
        SyntheticCar car = SyntheticCar.ConstantSpeed(s_circle, Speed);

        Assert.Throws<ArgumentOutOfRangeException>(() => car.WithSpike(3, metres));
        Assert.Throws<ArgumentOutOfRangeException>(() => car.WithDuplicate(3, metres));
    }

    [Fact]
    public void WithLateralOffset_DrivesAParallelLine_OnTheSameTimes()
    {
        SyntheticCar clean = SyntheticCar.ConstantSpeed(s_circle, Speed).WithDuration(10);

        IReadOnlyList<RawLocationSample> offset = clean.WithLateralOffset(4).Samples();

        // Anticlockwise, left of travel is towards the centre: 4 m inside the circle, at the same progress.
        IReadOnlyList<RawLocationSample> expected = clean.Samples();
        Assert.Equal(expected.Select(s => s.Timestamp), offset.Select(s => s.Timestamp));
        Assert.All(offset, s => Assert.Equal((Radius - 4) * SyntheticTrack.UnitsPerMetre, RadiusInUnits(s), 1.0));
    }

    [Fact]
    public void Laps_StartAtEachTrueLineCrossing_AndLastOneLap()
    {
        // From 100 m before the line: crossings at 1.25 s, then every 2π × 500 / 80 ≈ 39.27 s.
        SyntheticCar car = SyntheticCar.ConstantSpeed(s_circle, Speed, startProgressMetres: -100).WithDuration(130).WithDriver(new DriverNumber(7));
        double lapSeconds = s_circle.LengthMetres / Speed;

        IReadOnlyList<RawLap> laps = car.Laps(firstLap: 2);

        Assert.Equal([2, 3, 4], laps.Select(l => l.LapNumber)); // the lap starting at 119.1 s doesn't finish by 130 s
        for (int i = 0; i < laps.Count; i++)
        {
            double start = 1.25 + (i * lapSeconds);
            Assert.Equal(start, (laps[i].Start!.Value - SyntheticCar.DefaultStart).TotalSeconds, 1e-6); // whole ticks
            Assert.Equal(lapSeconds, laps[i].Duration!.Value, 1e-7);
            Assert.Equal(new DriverNumber(7), laps[i].Driver);
            Assert.False(laps[i].IsPitOutLap);
        }
    }

    [Fact]
    public void Laps_EachStartsExactlyWhereTheLastEnds()
    {
        IReadOnlyList<RawLap> laps = SyntheticCar.ConstantSpeed(s_circle, Speed, startProgressMetres: -100).WithDuration(400).Laps();

        for (int i = 1; i < laps.Count; i++)
        {
            Assert.Equal(laps[i - 1].Start!.Value.AddTicks((long)Math.Round(laps[i - 1].Duration!.Value * TimeSpan.TicksPerSecond)), laps[i].Start);
        }
    }

    [Fact]
    public void Laps_StartingOnTheLine_FirstLapStartsWithTheTrace()
    {
        IReadOnlyList<RawLap> laps = SyntheticCar.ConstantSpeed(s_circle, Speed, startProgressMetres: s_circle.LengthMetres).WithDuration(80).Laps();

        Assert.Equal(SyntheticCar.DefaultStart, laps[0].Start);
        Assert.Equal([1, 2], laps.Select(l => l.LapNumber));
    }

    [Fact]
    public void Laps_StationaryCar_HasNone() =>
        Assert.Empty(SyntheticCar.ConstantSpeed(s_circle, 0).WithDuration(100).Laps());

    [Fact]
    public void Laps_FirstLapBelowOne_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticCar.ConstantSpeed(s_circle, Speed).Laps(firstLap: 0));

    [Theory]
    [InlineData(-1)]
    [InlineData(double.PositiveInfinity)]
    public void ConstantSpeed_InvalidSpeed_Throws(double speed) =>
        Assert.Throws<ArgumentOutOfRangeException>("speedMetresPerSecond", () => SyntheticCar.ConstantSpeed(s_circle, speed));

    [Fact]
    public void ConstantSpeed_InvalidStart_NamesTheStart() =>
        Assert.Throws<ArgumentOutOfRangeException>("startProgressMetres", () => SyntheticCar.ConstantSpeed(s_circle, Speed, double.NaN));

    [Fact]
    public void Settings_InvalidValues_Throw()
    {
        SyntheticCar car = SyntheticCar.ConstantSpeed(s_circle, Speed);

        Assert.Throws<ArgumentOutOfRangeException>(() => car.WithDuration(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => car.WithSampleRate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => car.WithDropout(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => car.WithLateralOffset(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => car.WithLateralOffset(Radius));  // through the circle's centre
        Assert.Throws<ArgumentOutOfRangeException>(() => car.WithLateralOffset(-Radius));
        Assert.Equal(Radius - 1, car.WithLateralOffset(Radius - 1).LateralOffsetMetres);
    }

    private static double RadiusInUnits(RawLocationSample s) => Math.Sqrt(
        Math.Pow(s.X - (SyntheticTrack.CentreXMetres * SyntheticTrack.UnitsPerMetre), 2)
        + Math.Pow(s.Y - (SyntheticTrack.CentreYMetres * SyntheticTrack.UnitsPerMetre), 2));
}