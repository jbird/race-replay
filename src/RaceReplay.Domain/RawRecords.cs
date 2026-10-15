namespace RaceReplay.Domain;

// Raw records: what a data source returns (the IRawSessionProvider port's output), before any analysis.
// Timestamps are absolute UTC here; derived data uses seconds since the timeline's T0 instead (docs/03 §2).

/// <summary>One location sample in track units. <c>Z</c> is carried through cleaning but never stored.</summary>
public readonly record struct RawLocationSample(DateTimeOffset Timestamp, DriverNumber Driver, int X, int Y, int Z);

/// <summary>A lap as reported by timing. <c>Start</c> is approximate (docs/03 §5.3).</summary>
public sealed record RawLap(DriverNumber Driver, int LapNumber, DateTimeOffset? Start, double? Duration, bool IsPitOutLap);

/// <summary>A pit stop. <c>StopDuration</c> is only reported from the 2024 US GP onwards.</summary>
public sealed record RawPitStop(
    DriverNumber Driver,
    int Lap,
    DateTimeOffset Timestamp,
    double? LaneDuration,
    double? StopDuration);

/// <summary>A race control message: flags, safety car, incidents.</summary>
public sealed record RaceControlMessage(
    DateTimeOffset Timestamp,
    string Category,
    string? Flag,
    string? Scope,
    int? Lap,
    DriverNumber? Driver,
    string Message);

/// <summary>An official interval sample. Used only to validate derived gaps.</summary>
public sealed record RawInterval(DateTimeOffset Timestamp, DriverNumber Driver, GapValue GapToLeader, GapValue Interval);

/// <summary>A change in official position. Used only to validate the derived running order.</summary>
public sealed record RawPositionChange(DateTimeOffset Timestamp, DriverNumber Driver, int Position);

/// <summary>A driver's classification.</summary>
public sealed record SessionResult(DriverNumber Driver, int? Position, bool Dnf, bool Dns, bool Dsq, int LapsCompleted);

/// <summary>
/// Everything a data source provides for one session. Providers guarantee UTC timestamps, strictly ascending
/// location samples per driver and laps ordered by (driver, lap); the provider contract tests check it.
/// </summary>
public sealed record RawSessionData(
    SessionInfo Session,
    IReadOnlyList<Driver> Drivers,
    IReadOnlyDictionary<DriverNumber, IReadOnlyList<RawLocationSample>> Locations,
    IReadOnlyList<RawLap> Laps,
    IReadOnlyList<RawPitStop> PitStops,
    IReadOnlyList<RaceControlMessage> RaceControl,
    IReadOnlyList<RawInterval> Intervals,
    IReadOnlyList<RawPositionChange> PositionChanges,
    IReadOnlyList<SessionResult> Results);