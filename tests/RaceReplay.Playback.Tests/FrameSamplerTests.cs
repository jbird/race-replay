using RaceReplay.Domain;

namespace RaceReplay.Playback.Tests;

public sealed class FrameSamplerTests
{
    private const int Samples = 9; // 0–2 s at 4 Hz
    private const int X = 0, Y = 1, Visible = 2, Progress = 3;

    [Fact]
    public void Sample_OnAGridPoint_ReturnsThatSample()
    {
        FrameSampler sampler = new(Replay(Car(1)));
        double[] frame = new double[sampler.FrameLength];

        sampler.Sample(0.5, frame); // grid point 2

        Assert.Equal([20, -20, 1, double.NaN], frame);
    }

    [Fact]
    public void Sample_BetweenGridPoints_Interpolates()
    {
        FrameSampler sampler = new(Replay(Car(1, progress: true)));
        double[] frame = new double[sampler.FrameLength];

        sampler.Sample(0.625, frame); // halfway between points 2 and 3

        Assert.Equal(25, frame[X], 1e-9);
        Assert.Equal(-25, frame[Y], 1e-9);
        Assert.Equal(1, frame[Visible]);
        Assert.Equal(250, frame[Progress], 1e-9);
    }

    [Theory]
    [InlineData(0.875, 30)]  // between 3 (valid) and 4 (invalid): hold 3
    [InlineData(1.125, 50)]  // between 4 (invalid) and 5 (valid): hold 5
    public void Sample_AtTheEdgeOfAGap_HoldsTheNearestValidSample(double position, double x)
    {
        FrameSampler sampler = new(Replay(Car(1, invalid: [4])));
        double[] frame = new double[sampler.FrameLength];

        sampler.Sample(position, frame);

        Assert.Equal((x, 1.0), (frame[X], frame[Visible]));
    }

    [Fact]
    public void Sample_InsideAGap_HidesTheCar()
    {
        FrameSampler sampler = new(Replay(Car(1, invalid: [4, 5])));
        double[] frame = new double[sampler.FrameLength];

        sampler.Sample(1.125, frame);

        Assert.Equal(0, frame[Visible]);
        Assert.True(double.IsNaN(frame[X]) && double.IsNaN(frame[Y]) && double.IsNaN(frame[Progress]));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(2, 80)]
    [InlineData(99, 80)]
    public void Sample_OutsideTheTimeline_ClampsToItsEnds(double position, double x)
    {
        FrameSampler sampler = new(Replay(Car(1)));
        double[] frame = new double[sampler.FrameLength];

        sampler.Sample(position, frame);

        Assert.Equal(x, frame[X]);
        Assert.Equal(1, frame[Visible]);
    }

    [Fact]
    public void Sample_SeveralCars_FillsTheirRecordsInDriverOrder()
    {
        FrameSampler sampler = new(Replay(Car(44), Car(1, offset: 1_000), Car(16, invalid: [0, 1, 2])));
        double[] frame = new double[sampler.FrameLength];

        sampler.Sample(0.25, frame);

        Assert.Equal((3, 12), (sampler.CarCount, sampler.FrameLength));
        Assert.Equal(10, frame[(0 * FrameSampler.Stride) + X]);
        Assert.Equal(1_010, frame[(1 * FrameSampler.Stride) + X]);
        Assert.Equal(0, frame[(2 * FrameSampler.Stride) + Visible]);
    }

    [Fact]
    public void Sample_SingleSampleTimeline_Works()
    {
        FrameSampler sampler = new(Replay(samples: 1, Car(1, samples: 1)));
        double[] frame = new double[sampler.FrameLength];

        sampler.Sample(5, frame);

        Assert.Equal((0.0, 1.0), (frame[X], frame[Visible]));
    }

    [Fact]
    public void Sample_FrameTooShort_Throws()
    {
        FrameSampler sampler = new(Replay(Car(1), Car(2)));

        Assert.Throws<ArgumentException>(() => sampler.Sample(0, new double[FrameSampler.Stride]));
    }

    [Fact]
    public void Sample_ManyFrames_AllocatesNothing()
    {
        FrameSampler sampler = new(Replay([.. Enumerable.Range(1, 20).Select(n => Car(n, progress: true, invalid: [n % Samples]))]));
        double[] frame = new double[sampler.FrameLength];
        for (int i = 0; i < 1_000; i++)
        {
            sampler.Sample(i * 0.002, frame); // warm up (JIT tiers)
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            sampler.Sample((i % 2_000) * 0.001, frame);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); // NFR-01
    }

    // A car moving 40 units per second along x = -y; valid everywhere except the listed samples.
    private static CarChannels Car(int number, bool progress = false, int[]? invalid = null, float offset = 0, int samples = Samples)
    {
        float[] x = [.. Enumerable.Range(0, samples).Select(k => offset + (k * 10f))];
        float[] y = [.. Enumerable.Range(0, samples).Select(k => -k * 10f)];
        float[] p = [.. Enumerable.Range(0, samples).Select(k => k * 100f)];
        bool[] valid = [.. Enumerable.Range(0, samples).Select(k => invalid?.Contains(k) != true)];
        return new CarChannels(new DriverNumber(number), x, y, ValidityMask.FromBools(valid), progress ? p : default);
    }

    private static ReplaySession Replay(params CarChannels[] cars) => Replay(Samples, cars);

    private static ReplaySession Replay(int samples, params CarChannels[] cars)
    {
        DateTimeOffset t0 = new(2026, 3, 1, 13, 0, 0, TimeSpan.Zero);
        return new ReplaySession(
            new SessionInfo(SessionKey.Synthetic(1), 2026, "Race", "Race", "Test Grand Prix", "Test Park", t0),
            [.. cars.Select(c => new Driver(c.Driver, "TST", "Test DRIVER", "Team", new RgbColour(1, 2, 3)))],
            new TrackModel(new TrackPoint[] { new(0, 0), new(100, 0), new(100, 100) }),
            new ReplayTimeline(t0, 4, samples, 0, []),
            cars,
            GapSeries.Empty,
            [],
            Provenance.ForSynthetic(1, t0, "test"));
    }
}