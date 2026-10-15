using RaceReplay.Domain;

namespace RaceReplay.Synthetic.Tests;

public sealed class CircuitGeneratorTests
{
    public static TheoryData<int> Seeds => [.. Enumerable.Range(0, 25)];

    [Theory, MemberData(nameof(Seeds))]
    public void Generate_AnySeed_IsAClosedNonCrossingLoopOfRaceLength(int seed)
    {
        SyntheticCircuit circuit = CircuitGenerator.Generate(SyntheticRandom.For(seed, RandomChannel.Track));

        // Resampling at 5 m shortens the spline's length by a hair, never by more than 1 %.
        Assert.InRange(circuit.LapLengthMetres, 4_500 * 0.99, 6_000);
        Assert.False(CircuitGenerator.SelfIntersects(circuit.Track.Centreline.Span));
        Assert.Equal(TrackModel.DefaultUnitsPerMetre, circuit.Track.UnitsPerMetre);
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Generate_AnySeed_HasNoCornerTighterThanARealHairpin(int seed)
    {
        TrackModel track = CircuitGenerator.Generate(SyntheticRandom.For(seed, RandomChannel.Track)).Track;

        Assert.True(CircuitGenerator.TightestRadiusMetres(track.Centreline.Span) >= CircuitGenerator.MinCornerRadiusMetres);
    }

    [Fact]
    public void TightestRadiusMetres_Circle_IsItsRadius()
    {
        const int Points = 360;
        const double RadiusMetres = 100;
        TrackPoint[] circle = new TrackPoint[Points];
        for (int i = 0; i < Points; i++)
        {
            double angle = 2 * Math.PI * i / Points;
            circle[i] = new TrackPoint((float)(RadiusMetres * 10 * Math.Cos(angle)), (float)(RadiusMetres * 10 * Math.Sin(angle)));
        }

        Assert.Equal(RadiusMetres, CircuitGenerator.TightestRadiusMetres(circle), 0.5);
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Generate_AnySeed_PointsAreEvenlySpacedAboutFiveMetres(int seed)
    {
        TrackModel track = CircuitGenerator.Generate(SyntheticRandom.For(seed, RandomChannel.Track)).Track;
        ReadOnlySpan<TrackPoint> points = track.Centreline.Span;

        for (int i = 0; i < points.Length; i++)
        {
            TrackPoint a = points[i], b = points[(i + 1) % points.Length];
            double metres = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y))) / track.UnitsPerMetre;
            Assert.InRange(metres, 4.5, 5.1); // chords across curves are a little shorter than 5 m of arc
        }
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Generate_AnySeed_StartsOnAStraight(int seed)
    {
        TrackModel track = CircuitGenerator.Generate(SyntheticRandom.For(seed, RandomChannel.Track)).Track;

        // Total heading change over the 150 m before the line, where the grid stands: under 25°.
        double turning = 0;
        for (double s = -150; s < 0; s += 5)
        {
            turning += Math.Abs(HeadingChange(track, s * 10, (s + 5) * 10));
        }

        Assert.InRange(turning, 0, 25 * Math.PI / 180);
    }

    [Fact]
    public void Generate_SameSeed_SameCircuit()
    {
        SyntheticCircuit a = CircuitGenerator.Generate(SyntheticRandom.For(9, RandomChannel.Track));
        SyntheticCircuit b = CircuitGenerator.Generate(SyntheticRandom.For(9, RandomChannel.Track));

        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.Track.Centreline.ToArray(), b.Track.Centreline.ToArray());
    }

    [Fact]
    public void Generate_DifferentSeeds_DifferentLayouts() =>
        Assert.NotEqual(
            CircuitGenerator.Generate(SyntheticRandom.For(1, RandomChannel.Track)).Track.LapLength,
            CircuitGenerator.Generate(SyntheticRandom.For(2, RandomChannel.Track)).Track.LapLength);

    [Fact]
    public void Generate_Names_AreAFictionalCircuitAndItsGrandPrix()
    {
        SyntheticCircuit circuit = CircuitGenerator.Generate(SyntheticRandom.For(1, RandomChannel.Track));

        Assert.EndsWith(" Grand Prix", circuit.MeetingName, StringComparison.Ordinal);
        Assert.StartsWith(circuit.MeetingName[..^" Grand Prix".Length], circuit.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void SelfIntersects_BowTie_IsTrue() =>
        Assert.True(CircuitGenerator.SelfIntersects([new(0, 0), new(10, 10), new(10, 0), new(0, 10)]));

    [Fact]
    public void SelfIntersects_Square_IsFalse() =>
        Assert.False(CircuitGenerator.SelfIntersects([new(0, 0), new(10, 0), new(10, 10), new(0, 10)]));

    private static double HeadingChange(TrackModel track, double from, double to)
    {
        double before = Heading(track, from);
        double after = Heading(track, to);
        return Math.IEEERemainder(after - before, 2 * Math.PI);
    }

    private static double Heading(TrackModel track, double s)
    {
        TrackPoint a = track.PointAt(s), b = track.PointAt(s + 20);
        return Math.Atan2(b.Y - a.Y, b.X - a.X);
    }
}

public sealed class FieldGeneratorTests
{
    private static readonly Driver[] s_field = FieldGenerator.Generate(SyntheticRandom.For(1, RandomChannel.Field), 20);

    [Fact]
    public void Generate_FullField_HasUniqueNumbersAndAcronyms()
    {
        Assert.Equal(20, s_field.Length);
        Assert.Equal(20, s_field.Select(d => d.Number).Distinct().Count());
        Assert.All(s_field, d => Assert.InRange(d.Number.Value, 2, 99));
        Assert.Equal(20, s_field.Select(d => d.Acronym).Distinct().Count());
    }

    [Fact]
    public void Generate_EachDriver_HasAThreeLetterAcronymFromTheirSurname()
    {
        Assert.All(s_field, d =>
        {
            Assert.Matches("^[A-Z]{3}$", d.Acronym);
            string surname = d.FullName[(d.FullName.IndexOf(' ', StringComparison.Ordinal) + 1)..];
            Assert.Equal(surname.ToUpperInvariant(), surname);
            Assert.StartsWith(d.Acronym, surname, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Generate_Teams_HaveTwoDriversSharingNameAndColour()
    {
        IGrouping<string, Driver>[] teams = [.. s_field.GroupBy(d => d.TeamName)];

        Assert.Equal(10, teams.Length);
        Assert.All(teams, team =>
        {
            Assert.Equal(2, team.Count());
            Assert.Single(team.Select(d => d.TeamColour).Distinct());
        });
    }

    [Fact]
    public void Generate_TeamColours_AreClearlyDistinct()
    {
        RgbColour[] colours = [.. s_field.Select(d => d.TeamColour).Distinct()];

        Assert.Equal(10, colours.Length);
        for (int i = 0; i < colours.Length; i++)
        {
            for (int j = i + 1; j < colours.Length; j++)
            {
                Assert.True(Distance(colours[i], colours[j]) > 40, $"{colours[i]} and {colours[j]} are too alike");
            }
        }
    }

    [Theory]
    [InlineData(0, 1, 0.5, "FF0000")]
    [InlineData(120, 1, 0.5, "00FF00")]
    [InlineData(240, 1, 0.5, "0000FF")]
    [InlineData(60, 1, 0.5, "FFFF00")]
    [InlineData(300, 1, 0.25, "800080")]
    [InlineData(0, 0, 0.5, "808080")]
    public void FromHsl_KnownColours_ConvertExactly(double hue, double saturation, double lightness, string hex) =>
        Assert.Equal(RgbColour.FromHex(hex), TeamColours.FromHsl(hue, saturation, lightness));

    private static double Distance(RgbColour a, RgbColour b) =>
        Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));
}