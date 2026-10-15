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

    [Theory]
    [InlineData(double.NaN)]
    public void SampleIndexAt_NaN_IsTheFirstSample(double time) =>
        Assert.Equal(0, new ReplayTimeline(s_t0, 4, 401, 0, []).SampleIndexAt(time));

    [Theory]
    [InlineData(401, 1, 101)]       // 100 s at 1 Hz: 0, 1, …, 100
    [InlineData(401, 4, 401)]       // the timeline's own rate
    [InlineData(400, 1, 100)]       // 99.75 s: the last whole second is 99
    [InlineData(28_797, 1, 7_200)]  // 7,199 s
    public void SampleCountAtRate_Rate_CoversTheTimelineFromT0(int timelineSamples, double rate, int expected) =>
        Assert.Equal(expected, new ReplayTimeline(s_t0, 4, timelineSamples, 0, []).SampleCountAtRate(rate));

    [Fact]
    public void SampleCountAtRate_RepresentationErrorJustBelowAWholeSample_StillCountsIt()
    {
        // 50 samples at 3 Hz span 49/3 s; × 15 is exactly 245 but evaluates to 244.99999999999997.
        ReplayTimeline timeline = new(s_t0, 3, 50, 0, []);

        Assert.Equal(246, timeline.SampleCountAtRate(15));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void SampleCountAtRate_RateNotPositive_Throws(double rate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayTimeline(s_t0, 4, 401, 0, []).SampleCountAtRate(rate));

    [Fact]
    public void Constructor_SegmentListChangesBetweenEnumerations_KeepsWhatItValidated()
    {
        TimelineSegment[] valid = [new(0, 50, SegmentKind.Racing), new(50, 100, SegmentKind.PostRace)];
        TimelineSegment[] overlapping = [new(0, 60, SegmentKind.Racing), new(50, 100, SegmentKind.PostRace)];
        InconsistentList<TimelineSegment> segments = new(valid, valid, overlapping);

        ReplayTimeline? timeline = null;
        Exception? error = Record.Exception(() => timeline = new ReplayTimeline(s_t0, 4, 401, 0, segments));

        Assert.True(error is ArgumentException || timeline!.Segments.SequenceEqual(valid));
    }

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