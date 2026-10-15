namespace RaceReplay.Domain;

// A closed hierarchy consumed with pattern matching; a candidate for C# 15 union types (docs/03 §2.3).
// Every Time is in seconds since ReplayTimeline.T0Utc.

/// <summary>Something that happened at a point in the replay.</summary>
public abstract record RaceEvent(double Time);

public sealed record PitStopEvent(double Time, DriverNumber Driver, int Lap, double? LaneDuration, double? StopDuration)
    : RaceEvent(Time);

/// <summary>A change of track status lasting until <see cref="EndTime"/> (open-ended when null).</summary>
public sealed record TrackStatusEvent : RaceEvent
{
    public TrackStatusEvent(double time, double? endTime, TrackStatus status)
        : base(time)
    {
        // Written as !(end >= time) so a NaN end is rejected too.
        if (endTime is double end && !(end >= time))
        {
            throw new ArgumentOutOfRangeException(nameof(endTime), endTime, $"A track status can't end before it starts ({time}).");
        }

        EndTime = endTime;
        Status = status;
    }

    public double? EndTime { get; }

    public TrackStatus Status { get; }
}

public sealed record RetirementEvent(double Time, DriverNumber Driver, int Lap) : RaceEvent(Time);

public sealed record ChequeredFlagEvent(double Time) : RaceEvent(Time);

public enum TrackStatus
{
    Green,
    Yellow,
    SafetyCar,
    VirtualSafetyCar,
    RedFlag,
}

/// <summary>A span of the timeline, in seconds since T0.</summary>
public readonly record struct TimelineSegment(double Start, double End, SegmentKind Kind);

public enum SegmentKind
{
    PreRace,
    Racing,
    Suspended,
    PostRace,
}