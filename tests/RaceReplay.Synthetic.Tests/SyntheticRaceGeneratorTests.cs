using RaceReplay.Domain;

namespace RaceReplay.Synthetic.Tests;

/// <summary>One full-length default race, generated once and shared by the tests that inspect it.</summary>
public sealed class FullRaceFixture
{
    public SyntheticRace Race { get; } = new SyntheticRaceGenerator().Generate(1);
}

public sealed class SyntheticRaceGeneratorTests(FullRaceFixture fixture) : IClassFixture<FullRaceFixture>
{
    private static readonly SyntheticRaceOptions s_short = new() { Laps = 3, DriverCount = 6 };

    private RawSessionData Raw => fixture.Race.Raw;

    [Fact]
    public void Generate_Seed_DescribesASyntheticRace()
    {
        SessionInfo session = Raw.Session;

        Assert.Equal(SessionKey.Synthetic(1), session.Key);
        Assert.Equal("Race", session.Type);
        Assert.Equal(fixture.Race.Circuit.Name, session.Circuit);
        Assert.Equal(fixture.Race.Circuit.MeetingName, session.MeetingName);
        Assert.Equal(TimeSpan.Zero, session.StartUtc.Offset);
        Assert.Equal(session.StartUtc.Year, session.Year);
    }

    [Fact]
    public void Generate_DefaultOptions_RunsJustOverAGrandPrixDistance()
    {
        double distance = fixture.Race.RaceLaps * fixture.Race.Circuit.LapLengthMetres;

        Assert.InRange(distance, SyntheticRaceGenerator.RaceDistanceMetres, SyntheticRaceGenerator.RaceDistanceMetres + fixture.Race.Circuit.LapLengthMetres);
    }

    [Fact]
    public void Generate_Locations_AreStrictlyAscendingUtcPerDriver()
    {
        Assert.Equal(Raw.Drivers.Select(d => d.Number.Value).Order(), Raw.Locations.Keys.Select(k => k.Value).Order());
        foreach ((DriverNumber driver, IReadOnlyList<RawLocationSample> samples) in Raw.Locations)
        {
            Assert.NotEmpty(samples);
            Assert.All(samples, s => Assert.Equal(driver, s.Driver));
            Assert.All(samples, s => Assert.Equal(TimeSpan.Zero, s.Timestamp.Offset));
            for (int i = 1; i < samples.Count; i++)
            {
                Assert.True(samples[i].Timestamp > samples[i - 1].Timestamp);
            }
        }
    }

    [Fact]
    public void Generate_Locations_ArriveAtAboutThreePointSevenHertz()
    {
        IReadOnlyList<RawLocationSample> samples = Raw.Locations[Raw.Drivers[0].Number];

        double meanInterval = (samples[^1].Timestamp - samples[0].Timestamp).TotalSeconds / (samples.Count - 1);

        Assert.Equal(1 / 3.7, meanInterval, 1e-4);
    }

    [Fact]
    public void Generate_Locations_LieOnTheCircuit()
    {
        TrackModel track = fixture.Race.Circuit.Track;
        foreach (IReadOnlyList<RawLocationSample> samples in Raw.Locations.Values)
        {
            for (int i = 0; i < samples.Count; i += 97)
            {
                // Clean samples are centreline points rounded to whole units: at most √0.5 off.
                Assert.InRange(DistanceToCentreline(track, samples[i].X, samples[i].Y), 0, 0.75);
            }
        }
    }

    [Fact]
    public void Generate_Locations_NeverImplyImpossibleSpeeds()
    {
        double maxUnitsPerSecond = SpeedProfile.MaxSpeed * (1 + RaceSimulator.MaxLapNoise) * fixture.Race.Circuit.Track.UnitsPerMetre;
        const double RoundingUnits = 1.5; // each position is rounded to whole units: up to √2 between two samples
        foreach (IReadOnlyList<RawLocationSample> samples in Raw.Locations.Values)
        {
            for (int i = 1; i < samples.Count; i++)
            {
                double seconds = (samples[i].Timestamp - samples[i - 1].Timestamp).TotalSeconds;
                double units = Math.Sqrt(Math.Pow(samples[i].X - samples[i - 1].X, 2) + Math.Pow(samples[i].Y - samples[i - 1].Y, 2));
                Assert.True(units <= (maxUnitsPerSecond * seconds) + RoundingUnits, $"{units / seconds} units/s at sample {i}");
            }
        }
    }

    [Fact]
    public void Generate_BeforeTheStart_CarsWaitOnTheGrid()
    {
        foreach (IReadOnlyList<RawLocationSample> samples in Raw.Locations.Values)
        {
            RawLocationSample[] preRace = [.. samples.Where(s => s.Timestamp < Raw.Session.StartUtc)];

            Assert.NotEmpty(preRace);
            Assert.Single(preRace.Select(s => (s.X, s.Y)).Distinct());
        }
    }

    [Fact]
    public void Generate_Laps_AreContiguousFromTheStart()
    {
        foreach (IGrouping<DriverNumber, RawLap> laps in Raw.Laps.GroupBy(l => l.Driver))
        {
            RawLap[] ordered = [.. laps];
            Assert.Equal(Enumerable.Range(1, ordered.Length), ordered.Select(l => l.LapNumber));
            Assert.Equal(Raw.Session.StartUtc, ordered[0].Start);
            for (int i = 0; i < ordered.Length; i++)
            {
                Assert.InRange(ordered[i].Duration!.Value, 40, 200);
                Assert.False(ordered[i].IsPitOutLap);
                if (i > 0)
                {
                    TimeSpan expectedStart = ordered[i - 1].Start!.Value - Raw.Session.StartUtc + TimeSpan.FromSeconds(ordered[i - 1].Duration!.Value);
                    Assert.Equal(expectedStart.TotalSeconds, (ordered[i].Start!.Value - Raw.Session.StartUtc).TotalSeconds, 1e-6);
                }
            }
        }
    }

    [Fact]
    public void Generate_Results_ClassifyEveryDriverByLapsThenFinish()
    {
        SessionResult[] results = [.. Raw.Results];

        Assert.Equal(Enumerable.Range(1, Raw.Drivers.Count), results.Select(r => r.Position!.Value));
        Assert.Equal(fixture.Race.RaceLaps, results[0].LapsCompleted);
        Assert.Equal(results.Select(r => r.LapsCompleted).OrderDescending(), results.Select(r => r.LapsCompleted));
        Assert.All(results, r => Assert.InRange(r.LapsCompleted, fixture.Race.RaceLaps - 2, fixture.Race.RaceLaps));
        Assert.All(results, r => Assert.Equal(r.LapsCompleted, Raw.Laps.Count(l => l.Driver == r.Driver)));
        Assert.All(results, r => Assert.False(r.Dnf || r.Dns || r.Dsq));
    }

    [Fact]
    public void Generate_V0_HasNoPitStopsRaceControlOrValidationRecords()
    {
        Assert.Empty(Raw.PitStops);
        Assert.Empty(Raw.RaceControl);
        Assert.Empty(Raw.Intervals);
        Assert.Empty(Raw.PositionChanges);
    }

    [Fact]
    public void Generate_SameSeed_ProducesIdenticalData()
    {
        SyntheticRaceGenerator generator = new(s_short);

        RawSessionData a = generator.Generate(3).Raw;
        RawSessionData b = generator.Generate(3).Raw;

        Assert.Equal(a.Session, b.Session);
        Assert.Equal(a.Drivers, b.Drivers);
        Assert.Equal(a.Laps, b.Laps);
        Assert.Equal(a.Results, b.Results);
        foreach (DriverNumber driver in a.Locations.Keys)
        {
            Assert.Equal(a.Locations[driver], b.Locations[driver]);
        }
    }

    [Fact]
    public void Generate_DifferentSeeds_DifferentRaces()
    {
        SyntheticRaceGenerator generator = new(s_short);

        Assert.NotEqual(generator.Generate(3).Raw.Drivers, generator.Generate(4).Raw.Drivers);
    }

    [Fact]
    public void Generate_Options_SetLapsAndFieldSize()
    {
        SyntheticRace race = new SyntheticRaceGenerator(s_short).Generate(5);

        Assert.Equal(3, race.RaceLaps);
        Assert.Equal(6, race.Raw.Drivers.Count);
        Assert.Equal(3, race.Raw.Drivers.Select(d => d.TeamName).Distinct().Count());
        Assert.Equal(3, race.Raw.Results[0].LapsCompleted);
    }

    [Fact]
    public void Generate_NegativeSeed_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SyntheticRaceGenerator(s_short).Generate(-1));

    [Theory]
    [InlineData(0, 20)]
    [InlineData(201, 20)]
    [InlineData(3, 0)]
    [InlineData(3, 7)]
    [InlineData(3, 22)]
    public void Constructor_InvalidOptions_Throw(int laps, int drivers) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SyntheticRaceGenerator(new SyntheticRaceOptions { Laps = laps, DriverCount = drivers }));

    [Theory]
    [InlineData(0, 60, 60)]
    [InlineData(3.7, -1, 60)]
    [InlineData(3.7, 60, 3_601)]
    public void Constructor_InvalidTimings_Throw(double rate, double preRace, double coolDown) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SyntheticRaceGenerator(
            new SyntheticRaceOptions { SampleRateHz = rate, PreRaceSeconds = preRace, CoolDownSeconds = coolDown }));

    private static double DistanceToCentreline(TrackModel track, int x, int y)
    {
        ReadOnlySpan<TrackPoint> points = track.Centreline.Span;
        double best = double.PositiveInfinity;
        for (int i = 0; i < points.Length; i++)
        {
            TrackPoint a = points[i], b = points[(i + 1) % points.Length];
            double abx = b.X - a.X, aby = b.Y - a.Y;
            double t = Math.Clamp((((x - a.X) * abx) + ((y - a.Y) * aby)) / ((abx * abx) + (aby * aby)), 0, 1);
            double dx = a.X + (t * abx) - x, dy = a.Y + (t * aby) - y;
            best = Math.Min(best, Math.Sqrt((dx * dx) + (dy * dy)));
        }

        return best;
    }
}