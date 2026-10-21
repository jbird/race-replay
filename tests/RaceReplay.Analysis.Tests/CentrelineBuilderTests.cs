using RaceReplay.Domain;
using RaceReplay.Synthetic;

using static RaceReplay.Analysis.Tests.CentrelineSessions;

namespace RaceReplay.Analysis.Tests;

/// <summary>
/// Analytic tests for the centreline (docs/03 §5). Speeds and radii keep a sample chord's sag under 0.5 m (radius
/// ≥ 100 m at ≤ 65 m/s: a 17.6 m chord sags 0.39 m), and the 5-point smoothing pulls a 100 m bend in by about 0.25 m.
/// </summary>
public sealed class CentrelineBuilderTests
{
    private const double Straight = 500; // m
    private const double Bend = 100;     // m
    private const double PathTolerance = 1.0;    // m: chord sag + smoothing + whole-unit rounding, with margin
    private const double LengthTolerance = 0.005; // 06 §5: lap length within 0.5 %

    private static readonly SyntheticTrack s_stadium = SyntheticTrack.Stadium(Straight, Bend);
    private static readonly SyntheticTrack s_circle = SyntheticTrack.Circle(500);

    // ---- Accuracy (03 §5.2) ----

    [Fact]
    public void Build_Circle_LapLengthIsTwoPiR()
    {
        RawSessionData raw = Race(Numbered(Car(s_circle, 60, laps: 4)));

        TrackModel track = Build(raw).Track;

        Assert.Equal(s_circle.LengthMetres, track.LapLength / track.UnitsPerMetre, s_circle.LengthMetres * LengthTolerance);
    }

    [Fact]
    public void Build_Stadium_LengthAndPathMatchTheTrack()
    {
        RawSessionData raw = Race(Numbered(Car(s_stadium, 55, laps: 3), Car(s_stadium, 60, laps: 3, start: -300), Car(s_stadium, 65, laps: 3, start: -700)));

        TrackModel track = Build(raw).Track;

        Assert.Equal(s_stadium.LengthMetres, track.LapLength / track.UnitsPerMetre, s_stadium.LengthMetres * LengthTolerance);
        Assert.All(track.Centreline.ToArray(), p => Assert.InRange(DistanceToStadium(p, Straight, Bend), 0, PathTolerance));
    }

    [Fact]
    public void Build_LapsOnDifferentLines_AverageToThePath()
    {
        // The base lap (driver 1, the lowest number among equal lap times) is on the path; the others are 2 and 4 m
        // either side, and their lengths stay within 2 % of it (2π × 4 m over 1,628 m is 1.5 %).
        double[] offsets = [0, -4, -2, 2, 4];
        SyntheticCar[] cars = Numbered([.. offsets.Select(offset => Car(s_stadium, 60, laps: 2).WithLateralOffset(offset))]);

        CentrelineResult result = Build(Race(cars));
        CentrelineResult outerAlone = Build(Race([cars[4]]));

        Assert.Equal(5, result.Diagnostics.ReferenceLaps.Count);
        Assert.All(result.Track.Centreline.ToArray(), p => Assert.InRange(DistanceToStadium(p, Straight, Bend), 0, PathTolerance));
        Assert.True(outerAlone.Track.Centreline.ToArray().Max(p => DistanceToStadium(p, Straight, Bend)) > 3, "one line alone is 4 m off the path");
    }

    [Fact]
    public void Build_LapStartsMisreported_AlignmentKeepsTheShape()
    {
        // Every lap starts up to 1 s (up to 60 m) away from where it really did; the durations stay true.
        double[] errors = [0.9, -0.6, 0.3, -1.0, 0.7, -0.2, 0.5, -0.8, 1.0];
        SyntheticCar[] cars = Numbered(Car(s_stadium, 60, laps: 4), Car(s_stadium, 62, laps: 4, start: -400), Car(s_stadium, 58, laps: 4, start: -900));
        RawLap[] laps = [.. cars.SelectMany(c => c.Laps(firstLap: 2).SkipLast(1)).Select((l, i) => l with { Start = l.Start!.Value.AddSeconds(errors[i % errors.Length]) })];

        TrackModel track = Build(Race(cars, laps)).Track;

        Assert.Equal(s_stadium.LengthMetres, track.LapLength / track.UnitsPerMetre, s_stadium.LengthMetres * LengthTolerance);
        Assert.All(track.Centreline.ToArray(), p => Assert.InRange(DistanceToStadium(p, Straight, Bend), 0, PathTolerance));
    }

    [Fact]
    public void Build_LapFromALongerTrack_IsDiscardedByTheLengthCheck()
    {
        // 5 % longer at the same speed is 5 % slower: inside the 107 % rule, so only the length check catches it.
        SyntheticCar[] cars = Numbered(Car(s_circle, 60, laps: 2), Car(s_circle, 61, laps: 2), Car(s_circle, 62, laps: 2), Car(s_circle, 59, laps: 2), Car(SyntheticTrack.Circle(525), 60, laps: 2));

        CentrelineResult result = Build(Race(cars));

        Assert.Equal([new DriverNumber(5)], result.Diagnostics.DiscardedByLength.Select(l => l.Driver));
        Assert.Equal(4, result.Diagnostics.ReferenceLaps.Count);
        Assert.Equal(s_circle.LengthMetres, result.Diagnostics.LapLengthMetres, s_circle.LengthMetres * LengthTolerance);
    }

    [Fact]
    public void Build_Circle_SpacingIsUniformAndTheSeamHasNoKink()
    {
        TrackModel track = Build(Race(Numbered(Car(s_circle, 60, laps: 3)))).Track;
        TrackPoint[] points = track.Centreline.ToArray();
        int n = points.Length;
        double segment = track.LapLength / n;

        Assert.InRange(n, (int)Math.Ceiling(track.LapLength / 50), (int)Math.Ceiling(track.LapLength / 50) + 1); // 5 m spacing, in track units
        Assert.NotEqual(points[0], points[^1]);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(segment, Length(points[i], points[(i + 1) % n]), segment * 0.01);
        }

        // On a circle every turn is 2π / n; float coordinates near 20,000 units resolve that to well under 1 %.
        Assert.Equal(2 * Math.PI / n, Turn(points, 0), 2 * Math.PI / n * 0.1);
        Assert.Equal(2 * Math.PI / n, Turn(points, n - 1), 2 * Math.PI / n * 0.1);
    }

    // ---- Start/finish, direction and orientation (03 §5.3) ----

    [Fact]
    public void Build_ExactLapStarts_PointZeroIsOnTheLine()
    {
        RawSessionData raw = Race(Numbered(Car(s_stadium, 55, laps: 3), Car(s_stadium, 60, laps: 3, start: -300), Car(s_stadium, 65, laps: 3, start: -700)));

        TrackModel track = Build(raw).Track;

        Assert.InRange(Distance(Metres(track.Centreline.Span[0]), Metres(s_stadium.PointAt(0))), 0, 1);
    }

    [Fact]
    public void Build_ThirtyPercentOfBoundariesElsewhere_PointZeroStaysOnTheLine()
    {
        // 12 true lap starts and 5 spurious records (no duration, so never references) spread round the lap: 29 %.
        SyntheticCar[] cars = Numbered(Car(s_stadium, 60, laps: 4), Car(s_stadium, 62, laps: 4, start: -400), Car(s_stadium, 58, laps: 4, start: -900));
        RawLap[] laps = [.. cars.SelectMany(c => c.Laps(firstLap: 2))];
        double lapSeconds = s_stadium.LengthMetres / 60;
        double[] fractions = [0.2, 0.35, 0.5, 0.65, 0.8];
        RawLap[] spurious = [.. fractions.Select((f, i) => new RawLap(cars[0].Driver, 100 + i, laps[0].Start!.Value.AddSeconds(f * lapSeconds), null, false))];

        CentrelineResult result = Build(Race(cars, [.. laps, .. spurious]));

        Assert.Equal(17, result.Diagnostics.Boundaries);
        Assert.InRange(Distance(Metres(result.Track.Centreline.Span[0]), Metres(s_stadium.PointAt(0))), 0, 1);
    }

    [Fact]
    public void Build_BoundariesStraddleTheBaseSeam_PointZeroIsOnTheLineNotHalfwayRound()
    {
        // Lap starts alternately 0.1 s late and early (6 m either side). The base lap starts late, so half the
        // boundaries sit near s = 0 and half near s = L − 12 m; their circular median is the line.
        SyntheticCar[] cars = Numbered(Car(s_stadium, 60, laps: 4), Car(s_stadium, 60, laps: 4, start: -500));
        RawLap[] laps = [.. cars.SelectMany(c => c.Laps(firstLap: 2).Take(4)).Select((l, i) => l with { Start = l.Start!.Value.AddSeconds(i % 2 == 0 ? 0.1 : -0.1) })];

        CentrelineResult result = Build(Race(cars, laps));

        Assert.Equal(8, result.Diagnostics.Boundaries);
        Assert.InRange(Distance(Metres(result.Track.Centreline.Span[0]), Metres(s_stadium.PointAt(0))), 0, 1);
        Assert.Equal(6, result.Diagnostics.BoundarySpreadMetres, 0.5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_EitherDirection_ArcLengthIncreasesWithTravel(bool clockwise)
    {
        SyntheticTrack track = clockwise ? s_stadium.Clockwise() : s_stadium;

        TrackPoint[] points = Build(Race(Numbered(Car(track, 60, laps: 3)))).Track.Centreline.ToArray();

        // Point 1 lies ahead of point 0 along the direction of travel, and the loop turns the way the car does.
        (double x0, double y0) = Metres(points[0]);
        (double x1, double y1) = Metres(points[1]);
        double heading = track.HeadingAt(0);
        Assert.True(((x1 - x0) * Math.Cos(heading)) + ((y1 - y0) * Math.Sin(heading)) > 0);
        Assert.Equal(clockwise ? -1 : 1, Math.Sign(SignedArea(points)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(-60)]
    [InlineData(120)]
    [InlineData(-150)]
    public void Build_Stadium_DisplayRotationTurnsTheStraightsHorizontal(double degrees)
    {
        // The turn undoes the track's own, up to half a turn either way, so point 0 (halfway along the lower straight)
        // is back below the centre whichever way the track was turned.
        TrackModel track = Build(Race(Numbered(Car(s_stadium.Rotated(degrees), 60, laps: 3)))).Track;

        Assert.Equal(-degrees, track.DisplayRotationDegrees, 1.0);
    }

    [Fact]
    public void Build_LongAxisEitherSideOfVertical_DrawnTheSameWayUp()
    {
        // Long axes at 89° and 91° need nearly the same turn, not turns half a turn apart, so the map doesn't turn
        // upside down as the axis passes vertical. Point 0, on the lower straight, is drawn below the centre both times.
        double[] axes = [89, 91];
        TrackModel[] tracks = [.. axes.Select(d => Build(Race(Numbered(Car(s_stadium.Rotated(d), 60, laps: 3)))).Track)];

        Assert.Equal(-89, tracks[0].DisplayRotationDegrees, 1.0);
        Assert.Equal(-91, tracks[1].DisplayRotationDegrees, 1.0);
        Assert.All(tracks, t => Assert.Equal(-Bend, Drawn(t, 0).Y, PathTolerance));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(150)]
    public void Build_NearlyRoundShapes_RotationFadesInWithTheLongAxis(double degrees)
    {
        // Stadiums turned 30° and 150°, from round to clearly long: the rotation grows steadily from 0 to the full
        // turn back, half turn included, instead of jumping, because a nearly round shape's axis is unreliable.
        double[] straights = [0, 4, 8, 12, 16, 400];
        float[] rotations = [.. straights.Select(s => Build(Race(Numbered(Car(SyntheticTrack.Stadium(s, Bend).Rotated(degrees), 60, laps: 3)))).Track.DisplayRotationDegrees)];

        Assert.Equal(0, rotations[0]);
        Assert.Equal(-degrees, rotations[^1], 1.0);
        Assert.Contains(rotations, r => r < -1 && r > 1 - degrees); // part-way somewhere on the ramp
        for (int i = 1; i < rotations.Length; i++)
        {
            Assert.True(rotations[i] <= rotations[i - 1] + 0.5f, $"straights {straights[i]} m turn less than {straights[i - 1]} m");
        }
    }

    [Fact]
    public void Build_Circle_HasNoLongAxis_NoRotation()
    {
        Assert.Equal(0, Build(Race(Numbered(Car(s_circle.Rotated(40), 60, laps: 3)))).Track.DisplayRotationDegrees);
    }

    // ---- Diagnostics and determinism ----

    [Fact]
    public void Build_Diagnostics_DescribeTheReferenceLapsAndTheLine()
    {
        SyntheticCar[] cars = Numbered(Car(s_circle, 60, laps: 3), Car(s_circle, 62, laps: 3, start: -800));

        CentrelineDiagnostics d = Build(Race(cars)).Diagnostics;

        Assert.Equal(6, d.LapsChecked);
        Assert.Equal(new LapExclusions(0, 0, 0, 0, 0, 0, 0), d.Excluded);
        Assert.Equal([(2, 2), (2, 3), (2, 4), (1, 2), (1, 3)], d.ReferenceLaps.Select(l => (l.Driver.Value, l.LapNumber)));
        Assert.All(d.ReferenceLaps, l => Assert.Equal(s_circle.LengthMetres, l.LengthMetres, s_circle.LengthMetres * LengthTolerance));
        Assert.Equal(s_circle.LengthMetres / 62, d.ReferenceLaps[0].DurationSeconds, 1e-6);
        Assert.Empty(d.DiscardedByLength);
        Assert.Equal(6, d.Boundaries);
        Assert.InRange(d.BoundarySpreadMetres, 0, 0.5);
    }

    [Fact]
    public void Build_InputsInAnyOrder_SameCentreline()
    {
        SyntheticCar[] cars = Numbered(Car(s_stadium, 55, laps: 3), Car(s_stadium, 60, laps: 3, start: -300), Car(s_stadium, 65, laps: 3, start: -700));
        RaceControlMessage[] messages = [Message(5, "VIRTUAL SAFETY CAR DEPLOYED"), Message(10, "VIRTUAL SAFETY CAR ENDING"), Message(10, "TRACK CLEAR")];
        RawSessionData raw = Race(cars, raceControl: messages);
        RawSessionData shuffled = raw with
        {
            Laps = [.. raw.Laps.Reverse()],
            RaceControl = [.. messages.AsEnumerable().Reverse()],
            Locations = raw.Locations.Reverse().ToDictionary(p => p.Key, p => p.Value),
        };
        Dictionary<DriverNumber, CleanedTrace> traces = Clean(raw);
        Dictionary<DriverNumber, CleanedTrace> reversed = traces.Reverse().ToDictionary(p => p.Key, p => p.Value);
        CentrelineBuilder builder = new(new CentrelineOptions());

        CentrelineResult a = builder.Build(raw, traces);
        CentrelineResult b = builder.Build(shuffled, reversed);

        Assert.Equal(a.Track.Centreline.ToArray(), b.Track.Centreline.ToArray());
        Assert.Equal(a.Track.DisplayRotationDegrees, b.Track.DisplayRotationDegrees);
        Assert.Equal(a.Diagnostics.ReferenceLaps, b.Diagnostics.ReferenceLaps);
    }

    [Fact]
    public void Build_SyntheticRace_OutlinesTheCircuit()
    {
        SyntheticRace race = new SyntheticRaceGenerator(new SyntheticRaceOptions { Laps = 4, DriverCount = 4 }).Generate(2);

        TrackModel track = Build(race.Raw).Track;

        // ~3.7 Hz positions: within 2 % of the true lap (chords and smoothing cut the tightest corners slightly).
        Assert.Equal(race.Circuit.Track.LapLength, track.LapLength, race.Circuit.Track.LapLength * 0.02);
        Assert.InRange(track.Centreline.Length, 150, 2_000);
    }

    // ---- Options ----

    public static TheoryData<CentrelineOptions> InvalidOptions =>
    [
        new CentrelineOptions { ReferenceLapCount = 0 },
        new CentrelineOptions { SpacingMetres = 0 },
        new CentrelineOptions { SpacingMetres = double.NaN },
        new CentrelineOptions { SmoothingWindow = 0 },
        new CentrelineOptions { SmoothingWindow = 4 },
        new CentrelineOptions { MaxLapLengthDeviation = -0.01 },
        new CentrelineOptions { MaxLapTimeRatio = 0.99 },
        new CentrelineOptions { MaxLapTimeRatio = double.PositiveInfinity },
        new CentrelineOptions { MinReferencePoints = 2 },
        new CentrelineOptions { UnitsPerMetre = 0 },
    ];

    [Theory, MemberData(nameof(InvalidOptions))]
    public void Constructor_InvalidOptions_Throws(CentrelineOptions options) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CentrelineBuilder(options));

    [Fact]
    public void Constructor_SmoothingWindowOne_TurnsSmoothingOff()
    {
        RawSessionData raw = Race(Numbered(Car(s_stadium, 60, laps: 3)));

        TrackModel smoothed = Build(raw).Track;
        TrackModel unsmoothed = Build(raw, new CentrelineOptions { SmoothingWindow = 1 }).Track;

        // Smoothing pulls the bends in; without it the centreline sits closer to the sampled line, a little longer.
        Assert.True(unsmoothed.LapLength > smoothed.LapLength);
    }

    /// <summary>A car on <paramref name="track"/> long enough for <paramref name="laps"/> laps from lap 2, starting
    /// <paramref name="start"/> metres round (before the line when negative).</summary>
    internal static SyntheticCar Car(SyntheticTrack track, double speed, int laps, double start = -150) =>
        SyntheticCar.ConstantSpeed(track, speed, start).WithDuration((((laps + 0.5) * track.LengthMetres) - start) / speed);

    // Centreline point i in metres from the track's centre, turned by the display rotation as the map draws it.
    private static (double X, double Y) Drawn(TrackModel track, int i)
    {
        (double x, double y) = Metres(track.Centreline.Span[i]);
        double radians = track.DisplayRotationDegrees * Math.PI / 180;
        return ((x * Math.Cos(radians)) - (y * Math.Sin(radians)), (x * Math.Sin(radians)) + (y * Math.Cos(radians)));
    }

    private static double Length(TrackPoint a, TrackPoint b) => Math.Sqrt(Math.Pow(b.X - (double)a.X, 2) + Math.Pow(b.Y - (double)a.Y, 2));

    // The turn at point i, in radians, between the segments into and out of it.
    private static double Turn(TrackPoint[] p, int i)
    {
        int n = p.Length;
        TrackPoint a = p[(i + n - 1) % n], b = p[i], c = p[(i + 1) % n];
        double inAngle = Math.Atan2(b.Y - (double)a.Y, b.X - (double)a.X);
        double outAngle = Math.Atan2(c.Y - (double)b.Y, c.X - (double)b.X);
        double turn = outAngle - inAngle;
        return Math.Abs(Math.Atan2(Math.Sin(turn), Math.Cos(turn)));
    }

    // Positive when the loop runs anticlockwise (the shoelace formula, about the first point to keep it exact).
    private static double SignedArea(TrackPoint[] p)
    {
        double area = 0;
        for (int i = 0; i < p.Length; i++)
        {
            TrackPoint a = p[i], b = p[(i + 1) % p.Length];
            area += ((a.X - (double)p[0].X) * (b.Y - (double)p[0].Y)) - ((b.X - (double)p[0].X) * (a.Y - (double)p[0].Y));
        }

        return area / 2;
    }
}