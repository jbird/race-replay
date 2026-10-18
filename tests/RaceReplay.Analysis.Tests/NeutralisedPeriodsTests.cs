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

        IReadOnlyList<TimeWindow> periods = NeutralisedPeriods.From(messages);

        Assert.Equal([Window(10, 20), Window(30, 40), Window(50, 60)], periods);
    }

    [Fact]
    public void From_NeverClosed_RunsToTheEndOfTheSession()
    {
        Assert.Equal([new TimeWindow(s_t0.AddSeconds(10), DateTimeOffset.MaxValue)], NeutralisedPeriods.From([Message(10, "VIRTUAL SAFETY CAR DEPLOYED")]));
    }

    [Fact]
    public void From_CloseWithoutOpen_AndRepeatedOpen_KeepTheFirstOpening()
    {
        RaceControlMessage[] messages =
        [
            Message(5, "SAFETY CAR IN THIS LAP"), Message(10, "SAFETY CAR DEPLOYED"), Message(15, "SAFETY CAR DEPLOYED"), Message(20, "SAFETY CAR IN THIS LAP"),
        ];

        Assert.Equal([Window(10, 20)], NeutralisedPeriods.From(messages));
    }

    [Fact]
    public void From_MessagesInAnyOrder_SamePeriods()
    {
        RaceControlMessage[] messages = [Message(10, "SESSION ABORTED"), Message(20, "SESSION STARTED"), Message(30, "SESSION ABORTED"), Message(40, "SESSION STARTED")];

        Assert.Equal(NeutralisedPeriods.From(messages), NeutralisedPeriods.From([.. messages.AsEnumerable().Reverse()]));
    }

    private static TimeWindow Window(double from, double to) => new(s_t0.AddSeconds(from), s_t0.AddSeconds(to));
}