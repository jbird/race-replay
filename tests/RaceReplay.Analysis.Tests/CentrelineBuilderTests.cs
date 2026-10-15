using RaceReplay.Domain;
using RaceReplay.Synthetic;

namespace RaceReplay.Analysis.Tests;

public sealed class CentrelineBuilderTests
{
    private static readonly DateTimeOffset s_start = new(2026, 3, 1, 13, 0, 0, TimeSpan.Zero);
    private static readonly CentrelineBuilder s_builder = new(new CentrelineOptions());

    [Fact]
    public void Build_SyntheticRace_OutlinesTheCircuit()
    {
        SyntheticRace race = new SyntheticRaceGenerator(new SyntheticRaceOptions { Laps = 4, DriverCount = 4 }).Generate(2);

        TrackModel track = s_builder.Build(race.Raw);

        // One lap of ~3.7 Hz positions: its length is within 2 % of the true lap (chords cut corners slightly).
        Assert.Equal(race.Circuit.Track.LapLength, track.LapLength, race.Circuit.Track.LapLength * 0.02);
        Assert.InRange(track.Centreline.Length, 150, 2_000);
    }

    [Fact]
    public void Build_SeveralLaps_UsesTheFastestCleanLap()
    {
        // Driver 1's lap 2 is fastest but a pit-out lap; driver 2's lap 3 is next fastest and clean.
        RawSessionData raw = Session(
            laps:
            [
                new RawLap(D(1), 1, s_start, 10, false),
                new RawLap(D(1), 2, s_start.AddSeconds(10), 5, IsPitOutLap: true),
                new RawLap(D(2), 1, s_start, 12, false),
                new RawLap(D(2), 2, s_start.AddSeconds(12), 9, false),
                new RawLap(D(2), 3, s_start.AddSeconds(21), 8, false),
            ],
            locations: new()
            {
                [D(1)] = Lap(D(1), s_start, 30, radius: 100),
                [D(2)] = [.. Lap(D(2), s_start.AddSeconds(21), 8, radius: 300)],
            });

        TrackModel track = s_builder.Build(raw);

        Assert.Equal(2 * Math.PI * 300, track.LapLength, 2 * Math.PI * 300 * 0.01);
    }

    [Fact]
    public void Build_LapBeforeAPitOut_IsAnInLapAndSkipped()
    {
        RawSessionData raw = Session(
            laps:
            [
                new RawLap(D(1), 2, s_start, 5, false),
                new RawLap(D(1), 3, s_start.AddSeconds(5), 9, IsPitOutLap: true),
                new RawLap(D(1), 4, s_start.AddSeconds(14), 8, false),
            ],
            locations: new()
            {
                [D(1)] = [.. Lap(D(1), s_start, 5, radius: 100), .. Lap(D(1), s_start.AddSeconds(14), 8, radius: 200)],
            });

        TrackModel track = s_builder.Build(raw);

        Assert.Equal(2 * Math.PI * 200, track.LapLength, 2 * Math.PI * 200 * 0.01);
    }

    [Fact]
    public void Build_OnlyLapOne_ThrowsInsufficientData() =>
        Assert.Throws<InsufficientDataException>(() => s_builder.Build(Session(
            laps: [new RawLap(D(1), 1, s_start, 10, false)],
            locations: new() { [D(1)] = Lap(D(1), s_start, 10, radius: 100) })));

    [Fact]
    public void Build_LapWithoutPositions_ThrowsInsufficientData() =>
        Assert.Throws<InsufficientDataException>(() => s_builder.Build(Session(
            laps: [new RawLap(D(1), 2, s_start, 10, false)],
            locations: new() { [D(2)] = Lap(D(2), s_start, 10, radius: 100) })));

    [Fact]
    public void Build_TooFewDistinctPositions_ThrowsInsufficientData()
    {
        RawLocationSample[] parked = [.. Enumerable.Range(0, 50).Select(i => new RawLocationSample(s_start.AddSeconds(i * 0.2), D(1), 5, 5, 0))];

        Assert.Throws<InsufficientDataException>(() => s_builder.Build(Session(
            laps: [new RawLap(D(1), 2, s_start, 10, false)],
            locations: new() { [D(1)] = parked })));
    }

    [Fact]
    public void Constructor_FewerThanThreeReferencePoints_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CentrelineBuilder(new CentrelineOptions { MinReferencePoints = 2 }));

    private static DriverNumber D(int n) => new(n);

    // One lap around a circle, starting at (radius, 0), sampled every 0.27 s over the lap's duration.
    private static RawLocationSample[] Lap(DriverNumber driver, DateTimeOffset start, double seconds, double radius)
    {
        int count = (int)(seconds / 0.27);
        return
        [
            .. Enumerable.Range(0, count).Select(i =>
            {
                double angle = 2 * Math.PI * i / count;
                return new RawLocationSample(
                    start.AddSeconds(i * 0.27), driver, (int)Math.Round(radius * Math.Cos(angle)), (int)Math.Round(radius * Math.Sin(angle)), 0);
            }),
        ];
    }

    private static RawSessionData Session(RawLap[] laps, Dictionary<DriverNumber, IReadOnlyList<RawLocationSample>> locations) =>
        new(
            new SessionInfo(SessionKey.Synthetic(1), 2026, "Race", "Race", "Test Grand Prix", "Test Park", s_start),
            [.. locations.Keys.Concat(laps.Select(l => l.Driver)).Distinct().Select(d => new Driver(d, "D" + d.Value + "X", "Test D", "T", new RgbColour(1, 2, 3)))],
            locations,
            laps,
            [], [], [], [], []);
}