namespace RaceReplay.Domain.Tests;

public sealed class TrackModelTests
{
    private const float Tolerance = 1e-3f;

    // A 100 × 100 square, anticlockwise from the origin: lap length 400.
    private static readonly TrackPoint[] s_square = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)];

    [Fact]
    public void Constructor_Square_DerivesLapLengthAndBounds()
    {
        TrackModel track = new(s_square, unitsPerMetre: 10, displayRotationDegrees: -12.5f);

        Assert.Equal(400, track.LapLength, 1e-9);
        Assert.Equal(new TrackBounds(0, 0, 100, 100), track.Bounds);
        Assert.Equal(10, track.UnitsPerMetre);
        Assert.Equal(-12.5f, track.DisplayRotationDegrees);
        Assert.Equal(4, track.Centreline.Length);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(50, 50, 0)]
    [InlineData(100, 100, 0)]
    [InlineData(150, 100, 50)]
    [InlineData(350, 0, 50)] // the closing segment back to the start
    [InlineData(450, 50, 0)] // wraps onto the next lap
    [InlineData(-50, 0, 50)] // wraps backwards
    [InlineData(800, 0, 0)]
    public void PointAt_ArcLength_InterpolatesAlongTheLoop(double s, float x, float y)
    {
        TrackModel track = new(s_square);

        TrackPoint point = track.PointAt(s);

        Assert.Equal(x, point.X, Tolerance);
        Assert.Equal(y, point.Y, Tolerance);
    }

    [Fact]
    public void Constructor_SourceArrayChangedAfterwards_ModelIsUnaffected()
    {
        TrackPoint[] points = [.. s_square];
        TrackModel track = new(points);

        points[1] = new TrackPoint(1000, 0);

        Assert.Equal(new TrackPoint(100, 0), track.Centreline.Span[1]);
        Assert.Equal(400, track.LapLength, 1e-9);
        Assert.Equal(50, track.PointAt(50).X, Tolerance);
    }

    [Fact]
    public void PointAt_TinyNegativeArcLength_StaysOnTheTrack()
    {
        TrackModel track = new(s_square);

        TrackPoint point = track.PointAt(-1e-15);

        Assert.Equal(0, point.X, Tolerance);
        Assert.Equal(0, point.Y, Tolerance);
    }

    [Fact]
    public void PointAt_ZeroLengthSegment_ReturnsTheRepeatedPoint()
    {
        TrackModel track = new(new TrackPoint[] { new(0, 0), new(100, 0), new(100, 0), new(100, 100), new(0, 100) });

        TrackPoint point = track.PointAt(100);

        Assert.Equal(100, point.X, Tolerance);
        Assert.Equal(0, point.Y, Tolerance);
        Assert.Equal(400, track.LapLength, 1e-9);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void PointAt_NonFiniteArcLength_Throws(double s) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrackModel(s_square).PointAt(s));

    [Fact]
    public void Constructor_FewerThanThreePoints_Throws() =>
        Assert.Throws<ArgumentException>(() => new TrackModel(new TrackPoint[] { new(0, 0), new(1, 1) }));

    [Fact]
    public void Constructor_NonFinitePoint_Throws() =>
        Assert.Throws<ArgumentException>(() => new TrackModel(new TrackPoint[] { new(0, 0), new(float.NaN, 0), new(1, 1) }));

    [Fact]
    public void Constructor_AllPointsEqual_Throws() =>
        Assert.Throws<ArgumentException>(() => new TrackModel(new TrackPoint[] { new(5, 5), new(5, 5), new(5, 5) }));

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    public void Constructor_UnitsPerMetreNotPositive_Throws(double unitsPerMetre) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrackModel(s_square, unitsPerMetre));

    [Fact]
    public void Constructor_NonFiniteRotation_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrackModel(s_square, displayRotationDegrees: float.NaN));
}