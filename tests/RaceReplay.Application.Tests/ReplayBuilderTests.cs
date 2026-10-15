using Microsoft.Extensions.Time.Testing;

using RaceReplay.Analysis;
using RaceReplay.Domain;
using RaceReplay.Synthetic;

namespace RaceReplay.Application.Tests;

public sealed class ReplayBuilderTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 15, 9, 30, 0, TimeSpan.Zero);
    private static readonly SyntheticRace s_race =
        new SyntheticRaceGenerator(new SyntheticRaceOptions { Laps = 3, DriverCount = 6 }).Generate(4);

    [Fact]
    public void Build_SyntheticRace_PutsEveryDriverOnOneFourHertzTimeline()
    {
        ReplaySession replay = Build(s_race.Raw);

        Assert.Equal(4, replay.Timeline.SampleRateHz);
        Assert.Equal(s_race.Raw.Drivers, replay.Drivers);
        Assert.Equal(s_race.Raw.Drivers.Select(d => d.Number), replay.Cars.Select(c => c.Driver));
        Assert.All(replay.Cars, car => Assert.True(car.Valid.CountValid() > replay.Timeline.SampleCount * 0.9));
    }

    [Fact]
    public void Build_SyntheticRace_TimelineStartsAtTheFirstWholeSecondOfData()
    {
        DateTimeOffset firstSample = s_race.Raw.Locations.Values.Min(samples => samples[0].Timestamp);

        ReplaySession replay = Build(s_race.Raw);

        Assert.Equal(0, replay.Timeline.T0Utc.Millisecond);
        Assert.InRange((firstSample - replay.Timeline.T0Utc).TotalSeconds, 0, 1);
    }

    [Fact]
    public void Build_SyntheticRace_RaceStartIsLapOneAndSegmentsSpanTheTimeline()
    {
        ReplaySession replay = Build(s_race.Raw);
        ReplayTimeline timeline = replay.Timeline;

        Assert.Equal((s_race.Raw.Session.StartUtc - timeline.T0Utc).TotalSeconds, timeline.RaceStart, 1e-6);
        Assert.Equal([SegmentKind.PreRace, SegmentKind.Racing, SegmentKind.PostRace], timeline.Segments.Select(s => s.Kind));
        Assert.Equal(0, timeline.Segments[0].Start);
        Assert.Equal(timeline.Duration, timeline.Segments[^1].End);

        RawLap winnersLast = s_race.Raw.Laps.Where(l => l.Driver == s_race.Raw.Results[0].Driver).MaxBy(l => l.LapNumber)!;
        double flag = (winnersLast.Start!.Value.AddSeconds(winnersLast.Duration!.Value) - timeline.T0Utc).TotalSeconds;
        Assert.Equal(flag, timeline.Segments[1].End, 1e-6);
    }

    [Fact]
    public void Build_SyntheticRace_PositionsFollowTheRawSamples()
    {
        ReplaySession replay = Build(s_race.Raw);
        CarChannels car = replay.Cars[0];
        IReadOnlyList<RawLocationSample> raw = s_race.Raw.Locations[car.Driver];

        // At each grid point the car lies between the raw samples either side of it (linear interpolation).
        for (int k = 0; k < replay.Timeline.SampleCount; k += 37)
        {
            DateTimeOffset at = replay.Timeline.T0Utc.AddSeconds(replay.Timeline.TimeAt(k));
            int after = raw.ToList().FindIndex(s => s.Timestamp >= at);
            if (!car.Valid[k] || after <= 0)
            {
                continue;
            }

            RawLocationSample a = raw[after - 1], b = raw[after];
            Assert.InRange(car.X.Span[k], Math.Min(a.X, b.X) - 0.01f, Math.Max(a.X, b.X) + 0.01f);
            Assert.InRange(car.Y.Span[k], Math.Min(a.Y, b.Y) - 0.01f, Math.Max(a.Y, b.Y) + 0.01f);
        }
    }

    [Fact]
    public void Build_SyntheticRace_RecordsSyntheticProvenanceAtTheInjectedTime()
    {
        ReplaySession replay = Build(s_race.Raw);

        Assert.Equal(DataSource.Synthetic, replay.Provenance.Source);
        Assert.Equal(4, replay.Provenance.Seed);
        Assert.Equal(s_now, replay.Provenance.RetrievedUtc);
        Assert.Equal("race-replay test", replay.Provenance.Builder);
        Assert.True(replay.Gaps.IsEmpty);
        Assert.Empty(replay.Events);
    }

    [Fact]
    public void Build_RealSessionKey_RecordsOpenF1Provenance()
    {
        RawSessionData real = s_race.Raw with { Session = Rekey(s_race.Raw.Session, new SessionKey(9839)) };

        ReplaySession replay = Build(real);

        Assert.Equal(DataSource.OpenF1, replay.Provenance.Source);
        Assert.Equal(Provenance.OpenF1Licence, replay.Provenance.Licence);
    }

    [Fact]
    public void Build_NoLaps_ThrowsInsufficientDataForTheTrackOutline() =>
        Assert.Throws<InsufficientDataException>(() => Build(s_race.Raw with { Laps = [] }));

    [Fact]
    public void Build_NoLapOneRecords_RacingStartsWithTheTimeline()
    {
        ReplaySession replay = Build(s_race.Raw with { Laps = [.. s_race.Raw.Laps.Where(l => l.LapNumber > 1)] });

        Assert.Equal(0, replay.Timeline.RaceStart);
        Assert.Equal(SegmentKind.Racing, replay.Timeline.Segments[0].Kind);
    }

    [Fact]
    public void Build_NoResults_RacingRunsToTheEndOfTheTimeline()
    {
        ReplaySession replay = Build(s_race.Raw with { Results = [] });

        Assert.Equal([SegmentKind.PreRace, SegmentKind.Racing], replay.Timeline.Segments.Select(s => s.Kind));
        Assert.Equal(replay.Timeline.Duration, replay.Timeline.Segments[^1].End);
    }

    [Fact]
    public void Build_NoDrivers_ThrowsInsufficientData() =>
        Assert.Throws<InsufficientDataException>(() => Build(s_race.Raw with { Drivers = [] }));

    [Fact]
    public void Build_DriverWithoutSamples_GetsAnAllInvalidCar()
    {
        Dictionary<DriverNumber, IReadOnlyList<RawLocationSample>> locations = new(s_race.Raw.Locations);
        DriverNumber missing = s_race.Raw.Drivers[^1].Number;
        locations.Remove(missing);

        ReplaySession replay = Build(s_race.Raw with { Locations = locations });

        Assert.Equal(0, replay.Cars[replay.IndexOf(missing)].Valid.CountValid());
    }

    [Fact]
    public void Build_SameInputTwice_SameReplay()
    {
        ReplaySession a = Build(s_race.Raw), b = Build(s_race.Raw);

        Assert.Equal(a.Timeline.SampleCount, b.Timeline.SampleCount);
        Assert.Equal(a.Track.Centreline.ToArray(), b.Track.Centreline.ToArray());
        Assert.Equal(a.Cars[2].X.ToArray(), b.Cars[2].X.ToArray());
    }

    private static ReplaySession Build(RawSessionData raw)
    {
        ReplayBuilder builder = new(
            new CentrelineBuilder(new CentrelineOptions()), new TimelineResampler(new ResamplingOptions()), new FakeTimeProvider(s_now));
        return builder.Build(raw, new BuildOptions("race-replay test")).Replay;
    }

    private static SessionInfo Rekey(SessionInfo s, SessionKey key) =>
        new(key, s.Year, s.Type, s.Name, s.MeetingName, s.Circuit, s.StartUtc);
}