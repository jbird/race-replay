using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace RaceReplay.Domain;

/// <summary>
/// A replayable session: everything the player needs, on one timeline (docs/03 §2). The constructor enforces the
/// invariants the player relies on, so a bundle reader or builder can't hand it an inconsistent replay.
/// </summary>
public sealed class ReplaySession
{
    private readonly FrozenDictionary<DriverNumber, int> _indexByDriver;

    /// <remarks>
    /// Cars come one per driver, in driver order, each with one sample per timeline sample. Events are ordered by
    /// time. A synthetic session key needs synthetic provenance, and the reverse.
    /// </remarks>
    public ReplaySession(
        SessionInfo session,
        IReadOnlyList<Driver> drivers,
        TrackModel track,
        ReplayTimeline timeline,
        IReadOnlyList<CarChannels> cars,
        GapSeries gaps,
        IReadOnlyList<RaceEvent> events,
        Provenance provenance)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(drivers);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(cars);
        ArgumentNullException.ThrowIfNull(gaps);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(provenance);

        if (session.Key.IsSynthetic != provenance.IsSynthetic)
        {
            throw new ArgumentException(
                $"Session key {session.Key} and provenance source {provenance.Source} disagree about whether the data is synthetic.",
                nameof(provenance));
        }

        if (cars.Count != drivers.Count)
        {
            throw new ArgumentException($"There are {drivers.Count} drivers but {cars.Count} cars.", nameof(cars));
        }

        Dictionary<DriverNumber, int> indexByDriver = new(drivers.Count);
        for (int i = 0; i < drivers.Count; i++)
        {
            DriverNumber number = drivers[i].Number;
            if (!indexByDriver.TryAdd(number, i))
            {
                throw new ArgumentException($"Driver {number} appears more than once.", nameof(drivers));
            }

            if (cars[i].Driver != number)
            {
                throw new ArgumentException(
                    $"Cars must follow driver order: car {i} is driver {cars[i].Driver}, not {number}.", nameof(cars));
            }

            if (cars[i].SampleCount != timeline.SampleCount)
            {
                throw new ArgumentException(
                    $"Car {number} has {cars[i].SampleCount} samples; the timeline has {timeline.SampleCount}.", nameof(cars));
            }

            if (!gaps.IsEmpty && !gaps.TryGetGap(number, out _))
            {
                throw new ArgumentException($"There is no gap series for driver {number}.", nameof(gaps));
            }
        }

        for (int i = 1; i < events.Count; i++)
        {
            if (events[i].Time < events[i - 1].Time)
            {
                throw new ArgumentException($"Events must be ordered by time; event {i} comes before event {i - 1}.", nameof(events));
            }
        }

        Session = session;
        Drivers = Array.AsReadOnly(drivers.ToArray());
        Track = track;
        Timeline = timeline;
        Cars = Array.AsReadOnly(cars.ToArray());
        Gaps = gaps;
        Events = Array.AsReadOnly(events.ToArray());
        Provenance = provenance;
        _indexByDriver = indexByDriver.ToFrozenDictionary();
    }

    public SessionInfo Session { get; }

    /// <summary>Drivers in a fixed order; <see cref="Cars"/> uses the same order.</summary>
    public ReadOnlyCollection<Driver> Drivers { get; }

    public TrackModel Track { get; }

    public ReplayTimeline Timeline { get; }

    public ReadOnlyCollection<CarChannels> Cars { get; }

    public GapSeries Gaps { get; }

    public ReadOnlyCollection<RaceEvent> Events { get; }

    public Provenance Provenance { get; }

    /// <summary>The index of <paramref name="driver"/> in <see cref="Drivers"/> and <see cref="Cars"/>, or -1.</summary>
    public int IndexOf(DriverNumber driver) => _indexByDriver.TryGetValue(driver, out int index) ? index : -1;
}