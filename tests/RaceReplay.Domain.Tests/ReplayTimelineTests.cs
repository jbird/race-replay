namespace RaceReplay.Domain.Tests;

public sealed class ReplayTimelineTests
{
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_Grid_DerivesDurationFromFirstToLastSample()
    {
        ReplayTimeline timeline = new(s_t0, sampleRateHz: 4, sampleCount: 401, raceStart: 10, segments: []);

        Assert.Equal(100, timeline.Duration, 1e-12);
        Assert.Equal(s_t0, timeline.T0Utc);
        Assert.Equal(10, timeline.RaceStart);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0.25)]
    [InlineData(400, 100)]
    public void TimeAt_Index_IsIndexOverRate(int index, double expected) =>
        Assert.Equal(expected, new ReplayTimeline(s_t0, 4, 401, 0, []).TimeAt(index), 1e-12);

    [Theory]
    [InlineData(-1)]
    [InlineData(401)]
    public void TimeAt_IndexOffTheGrid_Throws(int index) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayTimeline(s_t0, 4, 401, 0, []).TimeAt(index));

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0.3, 1.2)]
    [InlineData(100, 400)]
    [InlineData(250, 400)]
    public void SampleIndexAt_Time_IsFractionalAndClamped(double time, double expected) =>
        Assert.Equal(expected, new ReplayTimeline(s_t0, 4, 401, 0, []).SampleIndexAt(time), 1e-12);

    [Fact]
    public void Constructor_OrderedSegments_KeepsThem()
    {
        TimelineSegment[] segments =
        [
            new(0, 10, SegmentKind.PreRace),
            new(10, 90, SegmentKind.Racing),
            new(90, 100, SegmentKind.PostRace),
        ];

        ReplayTimeline timeline = new(s_t0, 4, 401, 10, segments);

        Assert.Equal(segments, timeline.Segments);
    }

    [Theory]
    [InlineData(0, 50, 40, 60)]   // overlap
    [InlineData(50, 60, 0, 10)]   // out of order
    [InlineData(0, 10, 20, 101)]  // past the end
    [InlineData(0, 10, 30, 20)]   // ends before it starts
    public void Constructor_BadSegments_Throws(double s1, double e1, double s2, double e2)
    {
        TimelineSegment[] segments = [new(s1, e1, SegmentKind.Racing), new(s2, e2, SegmentKind.Racing)];

        Assert.Throws<ArgumentException>(() => new ReplayTimeline(s_t0, 4, 401, 0, segments));
    }

    [Fact]
    public void Constructor_NonFiniteSegment_Throws() =>
        Assert.Throws<ArgumentException>(
            () => new ReplayTimeline(s_t0, 4, 401, 0, [new(0, double.NaN, SegmentKind.Racing)]));

    [Fact]
    public void Constructor_NonUtcT0_Throws() =>
        Assert.Throws<ArgumentException>(
            () => new ReplayTimeline(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(1)), 4, 401, 0, []));

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    [InlineData(double.PositiveInfinity)]
    public void Constructor_RateNotPositive_Throws(double rate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayTimeline(s_t0, rate, 401, 0, []));

    [Fact]
    public void Constructor_NoSamples_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayTimeline(s_t0, 4, 0, 0, []));

    [Theory]
    [InlineData(-1)]
    [InlineData(100.25)]
    [InlineData(double.NaN)]
    public void Constructor_RaceStartOffTheTimeline_Throws(double raceStart) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayTimeline(s_t0, 4, 401, raceStart, []));
}