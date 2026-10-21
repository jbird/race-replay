using RaceReplay.Domain;

using static RaceReplay.Analysis.Tests.CentrelineSessions;

namespace RaceReplay.Analysis.Tests;

public sealed class NeutralisedPeriodsTests
{
    private static readonly DateTimeOffset s_t0 = Synthetic.SyntheticCar.DefaultStart;

    [Fact]
    public void From_EachKind_OpensAndClosesOnItsOwnMessages()
    {
        RaceControlMessage[] messages =
        [
            Message(10, "SAFETY CAR DEPLOYED"), Message(20, "SAFETY CAR IN THIS LAP"),
            Message(30, "VIRTUAL SAFETY CAR DEPLOYED"), Message(40, "VIRTUAL SAFETY CAR ENDING"),
            Message(50, "SESSION ABORTED"), Message(51, "RED FLAG"), Message(60, "SESSION STARTED"),
        ];

        IReadOnlyList<TimeWindow> periods = NeutralisedPeriods.From(messages, []);

        Assert.Equal([Window(10, 20), Window(30, 40), Window(50, 60)], periods);
    }

    [Fact]
    public void From_SafetyCarComesIn_RunsToTheEndOfTheLatestLapInProgress()
    {
        // When the message is shown the leader is 5 s into lap 7; a car 12 s back is still on lap 6.
        RawLap[] laps =
        [
            Lap(1, 7, start: 95, duration: 100),
            Lap(2, 6, start: 7, duration: 100),
            Lap(2, 7, start: 107, duration: 100),
            Lap(3, 9, start: 101, duration: 0), // no usable duration: ignored
        ];

        IReadOnlyList<TimeWindow> periods = NeutralisedPeriods.From([Message(10, "SAFETY CAR DEPLOYED"), Message(100, "SAFETY CAR IN THIS LAP")], laps);

        Assert.Equal([Window(10, 195)], periods);
    }

    [Fact]
    public void From_RedFlag_EndsTheSafetyCar_AndALaterSafetyCarIsItsOwnPeriod()
    {
        // São Paulo 2024 (S1): SC, red flag, restart, then a second SC.
        RaceControlMessage[] messages =
        [
            Message(0, "SAFETY CAR DEPLOYED"), Message(268, "SESSION ABORTED"), Message(269, "RED FLAG"),
            Message(1747, "SESSION STARTED"), Message(2421, "SAFETY CAR DEPLOYED"), Message(2711, "SAFETY CAR IN THIS LAP"),
        ];

        IReadOnlyList<TimeWindow> periods = NeutralisedPeriods.From(messages, []);

        Assert.Equal([Window(0, 268), Window(268, 1747), Window(2421, 2711)], periods);
    }

    [Fact]
    public void From_RedFlag_EndsAVirtualSafetyCar()
    {
        RaceControlMessage[] messages = [Message(10, "VIRTUAL SAFETY CAR DEPLOYED"), Message(30, "SESSION ABORTED"), Message(90, "SESSION STARTED")];

        Assert.Equal([Window(10, 30), Window(30, 90)], NeutralisedPeriods.From(messages, []));
    }

    [Fact]
    public void From_VirtualSafetyCarUpgradedToASafetyCar_EndsWhereTheSafetyCarStarts()
    {
        RaceControlMessage[] messages = [Message(10, "VIRTUAL SAFETY CAR DEPLOYED"), Message(25, "SAFETY CAR DEPLOYED"), Message(200, "SAFETY CAR IN THIS LAP")];

        Assert.Equal([Window(10, 25), Window(25, 200)], NeutralisedPeriods.From(messages, []));
    }

    [Fact]
    public void From_NeverClosed_RunsToTheEndOfTheSession()
    {
        Assert.Equal([new TimeWindow(s_t0.AddSeconds(10), DateTimeOffset.MaxValue)], NeutralisedPeriods.From([Message(10, "VIRTUAL SAFETY CAR DEPLOYED")], []));
    }

    [Fact]
    public void From_CloseWithoutOpen_AndRepeatedOpen_KeepTheFirstOpening()
    {
        RaceControlMessage[] messages =
        [
            Message(5, "SAFETY CAR IN THIS LAP"), Message(10, "SAFETY CAR DEPLOYED"), Message(15, "SAFETY CAR DEPLOYED"), Message(20, "SAFETY CAR IN THIS LAP"),
        ];

        Assert.Equal([Window(10, 20)], NeutralisedPeriods.From(messages, []));
    }

    [Fact]
    public void From_MessagesInAnyOrder_SamePeriods()
    {
        RaceControlMessage[] messages = [Message(10, "SAFETY CAR DEPLOYED"), Message(20, "SESSION ABORTED"), Message(40, "SESSION STARTED"), Message(50, "VIRTUAL SAFETY CAR DEPLOYED")];

        Assert.Equal(NeutralisedPeriods.From(messages, []), NeutralisedPeriods.From([.. messages.AsEnumerable().Reverse()], []));
    }

    [Theory]
    [InlineData(0, 10, false)]  // ends as the window opens
    [InlineData(50, 60, false)] // starts as it closes
    [InlineData(5, 15, true)]
    [InlineData(45, 55, true)]
    [InlineData(20, 30, true)]
    [InlineData(0, 60, true)]
    public void Overlaps_SharesTime_TouchingIsNotEnough(double start, double end, bool expected) =>
        Assert.Equal(expected, Window(10, 50).Overlaps(s_t0.AddSeconds(start), s_t0.AddSeconds(end)));

    private static TimeWindow Window(double from, double to) => new(s_t0.AddSeconds(from), s_t0.AddSeconds(to));

    private static RawLap Lap(int driver, int number, double start, double duration) =>
        new(new DriverNumber(driver), number, s_t0.AddSeconds(start), duration, false);
}