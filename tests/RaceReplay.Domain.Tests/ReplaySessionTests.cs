namespace RaceReplay.Domain.Tests;

public sealed class ReplaySessionTests
{
    private const int Samples = 5;

    private static readonly DateTimeOffset s_t0 = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TrackModel s_track = new(new TrackPoint[] { new(0, 0), new(100, 0), new(100, 100), new(0, 100) });
    private static readonly ReplayTimeline s_timeline = new(s_t0, 4, Samples, 0, []);

    [Fact]
    public void Constructor_ConsistentParts_ExposesThemInDriverOrder()
    {
        Driver[] drivers = [Driver(44), Driver(1), Driver(16)];
        RaceEvent[] events = [new ChequeredFlagEvent(0.25), new ChequeredFlagEvent(0.5)];

        ReplaySession replay = new(
            Session(SessionKey.Synthetic(1)), drivers, s_track, s_timeline,
            [Car(44), Car(1), Car(16)], GapSeries.Empty, events, SyntheticProvenance());

        Assert.Equal(drivers, replay.Drivers);
        Assert.Equal([new DriverNumber(44), new DriverNumber(1), new DriverNumber(16)], replay.Cars.Select(c => c.Driver));
        Assert.Equal(events, replay.Events);
        Assert.Same(s_track, replay.Track);
        Assert.Same(s_timeline, replay.Timeline);
        Assert.Same(GapSeries.Empty, replay.Gaps);
        Assert.True(replay.Provenance.IsSynthetic);
        Assert.Equal(1, replay.IndexOf(new DriverNumber(1)));
        Assert.Equal(2, replay.IndexOf(new DriverNumber(16)));
        Assert.Equal(-1, replay.IndexOf(new DriverNumber(99)));
    }

    [Fact]
    public void Constructor_CarsNotInDriverOrder_Throws() =>
        Assert.Throws<ArgumentException>(() => Build(drivers: [Driver(1), Driver(4)], cars: [Car(4), Car(1)]));

    [Fact]
    public void Constructor_MoreCarsThanDrivers_Throws() =>
        Assert.Throws<ArgumentException>(() => Build(drivers: [Driver(1)], cars: [Car(1), Car(4)]));

    [Fact]
    public void Constructor_DuplicateDriver_Throws() =>
        Assert.Throws<ArgumentException>(() => Build(drivers: [Driver(1), Driver(1)], cars: [Car(1), Car(1)]));

    [Fact]
    public void Constructor_CarOffTheTimelineGrid_Throws() =>
        Assert.Throws<ArgumentException>(() => Build(drivers: [Driver(1)], cars: [Car(1, Samples + 1)]));

    [Fact]
    public void Constructor_GapsMissingADriver_Throws()
    {
        GapSeries gaps = new(1, new Dictionary<DriverNumber, ReadOnlyMemory<float>> { [new DriverNumber(1)] = new float[2] });

        Assert.Throws<ArgumentException>(() => Build(drivers: [Driver(1), Driver(4)], cars: [Car(1), Car(4)], gaps: gaps));
    }

    [Fact]
    public void Constructor_GapsForEveryDriver_IsAccepted()
    {
        GapSeries gaps = new(1, new Dictionary<DriverNumber, ReadOnlyMemory<float>>
        {
            [new DriverNumber(1)] = new float[2],
            [new DriverNumber(4)] = new float[2],
        });

        ReplaySession replay = Build(drivers: [Driver(1), Driver(4)], cars: [Car(1), Car(4)], gaps: gaps);

        Assert.Same(gaps, replay.Gaps);
    }

    [Fact]
    public void Constructor_EventsOutOfOrder_Throws() =>
        Assert.Throws<ArgumentException>(() => Build(events: [new ChequeredFlagEvent(1), new ChequeredFlagEvent(0.5)]));

    [Fact]
    public void Constructor_SyntheticKeyWithOpenF1Provenance_Throws() =>
        Assert.Throws<ArgumentException>(() => Build(provenance: Provenance.ForOpenF1(s_t0, "b")));

    [Fact]
    public void Constructor_OpenF1KeyWithSyntheticProvenance_Throws() =>
        Assert.Throws<ArgumentException>(() => Build(key: new SessionKey(9839)));

    [Fact]
    public void Constructor_OpenF1KeyWithOpenF1Provenance_IsAccepted()
    {
        ReplaySession replay = Build(key: new SessionKey(9839), provenance: Provenance.ForOpenF1(s_t0, "b"));

        Assert.False(replay.Provenance.IsSynthetic);
    }

    [Fact]
    public void Constructor_InputListsChangedAfterwards_DoesNotChangeTheReplay()
    {
        List<Driver> drivers = [Driver(1)];
        List<CarChannels> cars = [Car(1)];

        ReplaySession replay = Build(drivers: drivers, cars: cars);
        drivers.Add(Driver(4));
        cars.Clear();

        Assert.Single(replay.Drivers);
        Assert.Single(replay.Cars);
    }

    private static ReplaySession Build(
        IReadOnlyList<Driver>? drivers = null,
        IReadOnlyList<CarChannels>? cars = null,
        GapSeries? gaps = null,
        IReadOnlyList<RaceEvent>? events = null,
        SessionKey? key = null,
        Provenance? provenance = null) =>
        new(
            Session(key ?? SessionKey.Synthetic(1)),
            drivers ?? [Driver(1)],
            s_track,
            s_timeline,
            cars ?? [Car(1)],
            gaps ?? GapSeries.Empty,
            events ?? [],
            provenance ?? SyntheticProvenance());

    private static SessionInfo Session(SessionKey key) =>
        new(key, 2026, "Race", "Race", "Demo Grand Prix", "Kestrel Park", s_t0);

    private static Driver Driver(int number) =>
        new(new DriverNumber(number), "D" + number, "Driver " + number, "Team", new RgbColour(10, 20, 30));

    private static CarChannels Car(int number, int samples = Samples) =>
        new(new DriverNumber(number), new float[samples], new float[samples], ValidityMask.FromBools(new bool[samples]));

    private static Provenance SyntheticProvenance() => Provenance.ForSynthetic(1, s_t0, "race-replay test");
}