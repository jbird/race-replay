using RaceReplay.Domain;
using RaceReplay.Synthetic;

using static RaceReplay.Analysis.Tests.CentrelineSessions;

namespace RaceReplay.Analysis.Tests;

/// <summary>
/// Which laps become references (docs/03 §5.1). One car laps a 300 m circle at 50 m/s from 150 m before the line:
/// lap n starts at 3 s + (n − 2) × 37.7 s. Each scenario spoils some laps and checks exactly which are left.
/// </summary>
public sealed class CentrelineReferenceLapTests
{
    private const double Speed = 50; // m/s
    private const double Before = 150; // m before the line at the first sample

    private static readonly SyntheticTrack s_circle = SyntheticTrack.Circle(300);
    private static readonly double s_lapSeconds = s_circle.LengthMetres / Speed;
    private static readonly CentrelineOptions s_all = new() { ReferenceLapCount = 10 };

    public static TheoryData<string> Scenarios =>
    [
        "lap 1",
        "pit out and in",
        "no timing",
        "pit stop without a pit-out flag",
        "retirement",
        "safety car",
        "safety car comes in as a lap starts",
        "safety car never comes in",
        "virtual safety car",
        "messages in any case",
        "red flag",
        "slow lap",
        "dropout",
    ];

    [Theory, MemberData(nameof(Scenarios))]
    public void Build_ReferenceLapRules_LeaveExactlyTheCleanLaps(string scenario)
    {
        SyntheticCar car = Car(laps: 6);
        RawLap[] laps = [.. car.Laps(firstLap: 2)]; // laps 2 to 7
        RaceControlMessage[] messages = [];
        RawPitStop[] pitStops = [];
        SessionResult[] results = [];
        int[] expected;
        LapExclusions excluded;
        switch (scenario)
        {
            case "lap 1":
                laps = [.. car.Laps(firstLap: 1)];
                (expected, excluded) = ([2, 3, 4, 5, 6], new(1, 0, 0, 0, 0, 0, 0));
                break;
            case "pit out and in": // lap 4 leaves the pits, so lap 3 entered them
                laps = Change(laps, 4, l => l with { IsPitOutLap = true });
                (expected, excluded) = ([2, 5, 6, 7], new(0, 2, 0, 0, 0, 0, 0));
                break;
            case "pit stop without a pit-out flag": // 10 s in the pit lane, all inside lap 5
                pitStops = [new RawPitStop(car.Driver, 5, SyntheticCar.DefaultStart.AddSeconds(Mid(5)), LaneDuration: 10, StopDuration: null)];
                (expected, excluded) = ([2, 3, 4, 6, 7], new(0, 1, 0, 0, 0, 0, 0));
                break;
            case "retirement": // a driver who didn't finish may have ended their last lap in the pits
                results = [new SessionResult(car.Driver, Position: null, Dnf: true, Dns: false, Dsq: false, LapsCompleted: 6)];
                (expected, excluded) = ([2, 3, 4, 5, 6], new(0, 1, 0, 0, 0, 0, 0));
                break;
            case "no timing":
                laps = Change(Change(laps, 5, l => l with { Duration = null }), 6, l => l with { Start = null });
                (expected, excluded) = ([2, 3, 4, 7], new(0, 0, 2, 0, 0, 0, 0));
                break;
            case "safety car": // it runs to the end of the lap it comes in on; lap 5 starts as it ends
                messages = [Message(Mid(3), "SAFETY CAR DEPLOYED"), Message(Mid(4), "SAFETY CAR IN THIS LAP")];
                (expected, excluded) = ([2, 5, 6, 7], new(0, 0, 0, 2, 0, 0, 0));
                break;
            case "safety car comes in as a lap starts": // lap 5 is the one under way, so it's the last neutralised
                messages = [Message(Mid(3), "SAFETY CAR DEPLOYED"), new(laps.Single(l => l.LapNumber == 5).Start!.Value, "SafetyCar", null, null, null, null, "SAFETY CAR IN THIS LAP")];
                (expected, excluded) = ([2, 6, 7], new(0, 0, 0, 3, 0, 0, 0));
                break;
            case "safety car never comes in":
                messages = [Message(Mid(5), "SAFETY CAR DEPLOYED")];
                (expected, excluded) = ([2, 3, 4], new(0, 0, 0, 3, 0, 0, 0));
                break;
            case "virtual safety car": // read as an SC, it would never close and take laps 3 to 7
                messages = [Message(Mid(3) - 5, "VIRTUAL SAFETY CAR DEPLOYED"), Message(Mid(3) + 5, "VIRTUAL SAFETY CAR ENDING")];
                (expected, excluded) = ([2, 4, 5, 6, 7], new(0, 0, 0, 1, 0, 0, 0));
                break;
            case "messages in any case":
                messages = [Message(Mid(3), " Safety Car Deployed "), Message(Mid(3) + 1, "safety car in this lap")];
                (expected, excluded) = ([2, 4, 5, 6, 7], new(0, 0, 0, 1, 0, 0, 0));
                break;
            case "red flag": // the race start's SESSION STARTED closes nothing
                messages = [Message(1, "SESSION STARTED"), Message(Mid(4), "SESSION ABORTED"), Message(Mid(5), "SESSION STARTED")];
                (expected, excluded) = ([2, 3, 6, 7], new(0, 0, 0, 2, 0, 0, 0));
                break;
            case "slow lap": // 108 % of the median
                laps = Change(laps, 6, l => l with { Duration = l.Duration * 1.08 });
                (expected, excluded) = ([2, 3, 4, 5, 7], new(0, 0, 0, 0, 1, 0, 0));
                break;
            case "dropout": // 3 s without positions splits the run inside lap 5
                car = car.WithDropout(Mid(5) - 1.5, Mid(5) + 1.5);
                (expected, excluded) = ([2, 3, 4, 6, 7], new(0, 0, 0, 0, 0, 1, 0));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        CentrelineDiagnostics d = Build(Race([car], laps, messages, pitStops, results), s_all).Diagnostics;

        Assert.Equal(expected, d.ReferenceLaps.Select(l => l.LapNumber).Order());
        Assert.Equal(excluded, d.Excluded);
        Assert.Equal(laps.Length, d.LapsChecked);
    }

    [Fact]
    public void Build_SafetyCarComesIn_CarsFurtherBackHaveTheirLastSafetyCarLapNeutralisedToo()
    {
        // Driver 2 runs 9 s behind driver 1. The safety car comes in 3 s into driver 1's lap 4, while driver 2 is
        // still on lap 3: driver 2's lap 4, started after the message, is still behind the safety car.
        SyntheticCar leader = Car(laps: 6).WithDriver(new DriverNumber(1));
        SyntheticCar behind = CentrelineBuilderTests.Car(s_circle, Speed, laps: 6, start: -Before - (9 * Speed)).WithDriver(new DriverNumber(2));
        RawLap[] laps = [.. leader.Laps(firstLap: 2), .. behind.Laps(firstLap: 2)];
        DateTimeOffset lap4 = laps.Single(l => l.Driver == leader.Driver && l.LapNumber == 4).Start!.Value;
        RaceControlMessage[] messages =
        [
            Message(Mid(3), "SAFETY CAR DEPLOYED"),
            new(lap4.AddSeconds(3), "SafetyCar", null, null, null, null, "SAFETY CAR IN THIS LAP"),
        ];

        CentrelineDiagnostics d = Build(Race([leader, behind], laps, messages), s_all).Diagnostics;

        Assert.DoesNotContain(d.ReferenceLaps, l => l.Driver == behind.Driver && l.LapNumber == 4);
        Assert.Equal(4, d.Excluded.Neutralised); // laps 3 and 4 of both
    }

    [Fact]
    public void Build_DuplicateLapRecords_ChosenWhateverTheirOrder()
    {
        // Lap 2, the one chosen, reported twice, the copy starting 0.4 s late: the earlier start wins either way round.
        SyntheticCar car = Car(laps: 3);
        RawLap[] laps = [.. car.Laps(firstLap: 2)];
        RawLap copy = laps[0] with { Start = laps[0].Start!.Value.AddSeconds(0.4) };
        CentrelineOptions one = new() { ReferenceLapCount = 1 };

        CentrelineResult a = Build(Race([car], [.. laps, copy]), one);
        CentrelineResult b = Build(Race([car], [copy, .. laps.AsEnumerable().Reverse()]), one);

        Assert.Equal(a.Track.Centreline.ToArray(), b.Track.Centreline.ToArray());
        Assert.Equal(a.Diagnostics.ReferenceLaps, b.Diagnostics.ReferenceLaps);
    }

    [Fact]
    public void Build_ParkedCar_HasTooFewPositions()
    {
        SyntheticCar moving = Car(laps: 6);
        SyntheticCar parked = SyntheticCar.ConstantSpeed(s_circle, 0, 50).WithDuration(moving.DurationSeconds).WithDriver(new DriverNumber(2));
        RawLap[] parkedLaps = [.. moving.Laps(firstLap: 2).Take(3).Select(l => l with { Driver = parked.Driver })];

        CentrelineDiagnostics d = Build(Race([moving, parked], [.. moving.Laps(firstLap: 2), .. parkedLaps]), s_all).Diagnostics;

        Assert.Equal(new LapExclusions(0, 0, 0, 0, 0, 0, 3), d.Excluded);
        Assert.All(d.ReferenceLaps, l => Assert.Equal(moving.Driver, l.Driver));
    }

    [Fact]
    public void Build_LapsOfADriverWithoutPositions_AreNotCovered()
    {
        SyntheticCar car = Car(laps: 4);
        RawLap[] ghost = [.. car.Laps(firstLap: 2).Select(l => l with { Driver = new DriverNumber(9) })];

        CentrelineDiagnostics d = Build(Race([car], [.. car.Laps(firstLap: 2), .. ghost]), s_all).Diagnostics;

        Assert.Equal(new LapExclusions(0, 0, 0, 0, 0, 4, 0), d.Excluded);
    }

    [Fact]
    public void Build_Choice_FastestLapOfEachDriverBeforeAnySecondLap()
    {
        // Within 7 % of each other, so the 107 % rule keeps them all; driver 1 is fastest, then 3, then 2.
        SyntheticCar[] cars = Numbered(Car(laps: 3, speed: 60), Car(laps: 3, speed: 57), Car(laps: 3, speed: 58.5));

        CentrelineDiagnostics d = Build(Race(cars), new CentrelineOptions { ReferenceLapCount = 4 }).Diagnostics;

        // Round one: each driver's fastest. Round two: the next fastest of anyone, driver 1's equal-time laps by number.
        Assert.Equal([(1, 2), (1, 3), (3, 2), (2, 2)], d.ReferenceLaps.Select(l => (l.Driver.Value, l.LapNumber)));
    }

    [Fact]
    public void Build_EqualLapTimes_LowerDriverNumberThenLapNumberWins()
    {
        SyntheticCar seven = Car(laps: 3).WithDriver(new DriverNumber(7));
        SyntheticCar three = Car(laps: 3).WithDriver(new DriverNumber(3));

        CentrelineDiagnostics d = Build(Race([seven, three], [.. seven.Laps(firstLap: 2).Reverse(), .. three.Laps(firstLap: 2).Reverse()]), new CentrelineOptions { ReferenceLapCount = 1 }).Diagnostics;

        Assert.Equal([(3, 2)], d.ReferenceLaps.Select(l => (l.Driver.Value, l.LapNumber)));
    }

    [Fact]
    public void Build_FewerCandidatesThanReferenceLapCount_UsesThemAll()
    {
        CentrelineDiagnostics d = Build(Race([Car(laps: 2)])).Diagnostics;

        Assert.Equal([2, 3], d.ReferenceLaps.Select(l => l.LapNumber));
    }

    [Fact]
    public void Build_NoCandidates_ThrowsSayingWhichRulesExcludedTheLaps()
    {
        SyntheticCar car = Car(laps: 2);
        RawLap[] laps = [.. car.Laps(firstLap: 1)];
        laps[1] = laps[1] with { IsPitOutLap = true };

        InsufficientDataException e = Assert.Throws<InsufficientDataException>(() => Build(Race([car], laps)));

        Assert.Contains("Of 2 laps: 1 lap 1, 1 in or out of the pits", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NoLaps_ThrowsInsufficientData() =>
        Assert.Throws<InsufficientDataException>(() => Build(Race([Car(laps: 2)], [])));

    private static SyntheticCar Car(int laps, double speed = Speed) => CentrelineBuilderTests.Car(s_circle, speed, laps, start: -Before);

    private static double Mid(int lap) => (Before / Speed) + ((lap - 2 + 0.5) * s_lapSeconds);

    private static RawLap[] Change(RawLap[] laps, int lapNumber, Func<RawLap, RawLap> change) =>
        [.. laps.Select(l => l.LapNumber == lapNumber ? change(l) : l)];
}