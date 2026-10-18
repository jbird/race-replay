namespace RaceReplay.Synthetic.Tests;

public sealed class SyntheticTrackTests
{
    private const double Straight = 400; // m
    private const double Radius = 100;   // m
    private const double Tolerance = 1e-6; // m; the shapes are exact

    private static readonly SyntheticTrack s_stadium = SyntheticTrack.Stadium(Straight, Radius);

    [Fact]
    public void Stadium_Length_IsTwoStraightsAndACircle()
    {
        Assert.Equal((2 * Straight) + (2 * Math.PI * Radius), s_stadium.LengthMetres, 1e-9);
    }

    [Fact]
    public void Stadium_ProgressZero_IsHalfwayAlongTheLowerStraight_HeadingAlongX()
    {
        Assert.Equal((0, -Radius), Metres(s_stadium.PointAt(0)), new PointComparer());
        Assert.Equal(0, s_stadium.HeadingAt(0), Tolerance);
    }

    [Theory]
    [InlineData(0.0, 0.0, -Radius)]
    [InlineData(Straight / 2, Straight / 2, -Radius)]                                     // into the right bend
    [InlineData((Straight / 2) + (Math.PI * Radius / 2), (Straight / 2) + Radius, 0.0)]   // apex of the right bend
    [InlineData((Straight / 2) + (Math.PI * Radius), Straight / 2, Radius)]               // onto the upper straight
    [InlineData(Straight + (Math.PI * Radius), 0.0, Radius)]                              // halfway along it
    [InlineData((1.5 * Straight) + (1.5 * Math.PI * Radius), -(Straight / 2) - Radius, 0.0)] // apex of the left bend
    [InlineData((1.5 * Straight) + (2 * Math.PI * Radius), -Straight / 2, -Radius)]       // back on the lower straight
    public void Stadium_PointAt_FollowsTheShape(double metres, double x, double y)
    {
        Assert.Equal((x, y), Metres(s_stadium.PointAt(metres)), new PointComparer());
    }

    [Fact]
    public void Stadium_IsContinuous_StepsAreAsLongAsTheProgress()
    {
        // 1 m steps: chords on the 100 m bends are shorter than the arc by 1 / (24 × 100²) of a metre at most.
        for (double m = 0; m < s_stadium.LengthMetres; m += 1)
        {
            (double ax, double ay) = Metres(s_stadium.PointAt(m));
            (double bx, double by) = Metres(s_stadium.PointAt(m + 1));
            Assert.Equal(1, Math.Sqrt(Square(bx - ax) + Square(by - ay)), 1e-5);
        }
    }

    [Fact]
    public void Stadium_ZeroStraights_IsACircle()
    {
        SyntheticTrack stadium = SyntheticTrack.Stadium(0, Radius);

        Assert.Equal(2 * Math.PI * Radius, stadium.LengthMetres, 1e-9);
        Assert.Equal(Radius, Length(Metres(stadium.PointAt(123))), Tolerance);
    }

    [Theory]
    [InlineData(-1, Radius)]
    [InlineData(double.NaN, Radius)]
    [InlineData(Straight, 0)]
    [InlineData(Straight, double.PositiveInfinity)]
    public void Stadium_InvalidSize_Throws(double straight, double radius) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticTrack.Stadium(straight, radius));

    [Fact]
    public void Clockwise_DrivesTheMirrorImage_FromTheSameStart()
    {
        SyntheticTrack clockwise = s_stadium.Clockwise();

        Assert.Equal(s_stadium.LengthMetres, clockwise.LengthMetres);
        foreach (double m in new[] { 0.0, 150, 333, 1_000 })
        {
            (double x, double y) = Metres(s_stadium.PointAt(m));
            Assert.Equal((x, -y), Metres(clockwise.PointAt(m)), new PointComparer());
        }

        // Turning right: the heading falls through the first bend.
        Assert.True(clockwise.HeadingAt(Straight) < clockwise.HeadingAt(Straight / 2));
    }

    [Fact]
    public void Rotated_TurnsTheTrackAboutItsCentre()
    {
        SyntheticTrack rotated = s_stadium.Rotated(90);

        Assert.Equal(s_stadium.LengthMetres, rotated.LengthMetres);
        foreach (double m in new[] { 0.0, 150, 333, 1_000 })
        {
            (double x, double y) = Metres(s_stadium.PointAt(m));
            Assert.Equal((-y, x), Metres(rotated.PointAt(m)), new PointComparer());
            Assert.Equal(s_stadium.HeadingAt(m) + (Math.PI / 2), rotated.HeadingAt(m), Tolerance);
        }
    }

    [Fact]
    public void Rotated_NotFinite_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => s_stadium.Rotated(double.NaN));

    [Fact]
    public void PointAt_LateralOffset_MovesToTheLeftOfTravel()
    {
        // On the lower straight, heading +x, left is +y; on the anticlockwise circle, left is towards the centre.
        Assert.Equal((0, -Radius + 3), Metres(s_stadium.PointAt(0, lateralMetres: 3)), new PointComparer());
        Assert.Equal((0, -Radius - 3), Metres(s_stadium.PointAt(0, lateralMetres: -3)), new PointComparer());
        Assert.Equal(Radius - 3, Length(Metres(SyntheticTrack.Circle(Radius).PointAt(77, lateralMetres: 3))), Tolerance);
    }

    [Fact]
    public void PointAt_LateralOffset_OnAClockwiseTrack_IsStillLeftOfTravel()
    {
        // Clockwise, the lower straight runs the same way (+x) but the track lies below it: left is now outside.
        Assert.Equal((0, Radius + 3), Metres(s_stadium.Clockwise().PointAt(0, lateralMetres: 3)), new PointComparer());
    }

    // Metres from the track's centre.
    private static (double X, double Y) Metres((double X, double Y) units) =>
        ((units.X / SyntheticTrack.UnitsPerMetre) - SyntheticTrack.CentreXMetres, (units.Y / SyntheticTrack.UnitsPerMetre) - SyntheticTrack.CentreYMetres);

    private static double Length((double X, double Y) p) => Math.Sqrt(Square(p.X) + Square(p.Y));

    private static double Square(double v) => v * v;

    private sealed class PointComparer : IEqualityComparer<(double X, double Y)>
    {
        public bool Equals((double X, double Y) a, (double X, double Y) b) =>
            Math.Abs(a.X - b.X) < Tolerance && Math.Abs(a.Y - b.Y) < Tolerance;

        public int GetHashCode((double X, double Y) obj) => 0;
    }
}