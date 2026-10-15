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

        if (provenance.IsSynthetic && provenance.Seed != session.Key.SyntheticSeed)
        {
            throw new ArgumentException(
                $"Synthetic session {session.Key} is seed {session.Key.SyntheticSeed}, but its provenance records seed {provenance.Seed}.",
                nameof(provenance));
        }

        // Copy first, then validate the copies: what is checked is exactly what is kept.
        Driver[] ownedDrivers = [.. drivers];
        CarChannels[] ownedCars = [.. cars];
        RaceEvent[] ownedEvents = [.. events];

        if (ownedCars.Length != ownedDrivers.Length)
        {
            throw new ArgumentException($"There are {ownedDrivers.Length} drivers but {ownedCars.Length} cars.", nameof(cars));
        }

        ValidateGaps(gaps, ownedDrivers.Length, timeline);
        Dictionary<DriverNumber, int> indexByDriver = new(ownedDrivers.Length);
        for (int i = 0; i < ownedDrivers.Length; i++)
        {
            Driver driver = ownedDrivers[i] ?? throw new ArgumentException($"Driver {i} is null.", nameof(drivers));
            CarChannels car = ownedCars[i] ?? throw new ArgumentException($"Car {i} is null.", nameof(cars));
            DriverNumber number = driver.Number;
            if (!indexByDriver.TryAdd(number, i))
            {
                throw new ArgumentException($"Driver {number} appears more than once.", nameof(drivers));
            }

            if (car.Driver != number)
            {
                throw new ArgumentException(
                    $"Cars must follow driver order: car {i} is driver {car.Driver}, not {number}.", nameof(cars));
            }

            if (car.SampleCount != timeline.SampleCount)
            {
                throw new ArgumentException(
                    $"Car {number} has {car.SampleCount} samples; the timeline has {timeline.SampleCount}.", nameof(cars));
            }

            if (!gaps.IsEmpty && !gaps.TryGetGap(number, out _))
            {
                throw new ArgumentException($"There is no gap series for driver {number}.", nameof(gaps));
            }
        }

        ValidateEvents(ownedEvents, timeline.Duration);

        Session = session;
        Drivers = Array.AsReadOnly(ownedDrivers);
        Track = track;
        Timeline = timeline;
        Cars = Array.AsReadOnly(ownedCars);
        Gaps = gaps;
        Events = Array.AsReadOnly(ownedEvents);
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

    // Gaps are all-or-nothing: either none (not yet derived) or one series per driver, each spanning the timeline.
    // Together with the per-driver check in the constructor, the count check rules out series for unknown drivers.
    private static void ValidateGaps(GapSeries gaps, int driverCount, ReplayTimeline timeline)
    {
        if (gaps.IsEmpty)
        {
            return;
        }

        if (gaps.DriverCount != driverCount)
        {
            throw new ArgumentException(
                $"There are {gaps.DriverCount} gap series for {driverCount} drivers; every series must belong to a session driver.",
                nameof(gaps));
        }

        int expected = timeline.SampleCountAtRate(gaps.SampleRateHz);
        if (gaps.SampleCount != expected)
        {
            throw new ArgumentException(
                $"Gap series have {gaps.SampleCount} samples, but {expected} at {gaps.SampleRateHz} Hz span the timeline.",
                nameof(gaps));
        }
    }

    private static void ValidateEvents(RaceEvent[] events, double duration)
    {
        for (int i = 0; i < events.Length; i++)
        {
            RaceEvent e = events[i] ?? throw new ArgumentException($"Event {i} is null.", nameof(events));

            // Written as !(…) so NaN times are rejected too.
            if (!(e.Time >= 0 && e.Time <= duration))
            {
                throw new ArgumentException($"Event {i} at {e.Time} s lies outside the timeline [0, {duration}].", nameof(events));
            }

            if (e is TrackStatusEvent { EndTime: double end } && end > duration)
            {
                throw new ArgumentException($"Event {i} ends at {end} s, after the timeline ends ({duration}).", nameof(events));
            }

            if (i > 0 && e.Time < events[i - 1].Time)
            {
                throw new ArgumentException($"Events must be ordered by time; event {i} comes before event {i - 1}.", nameof(events));
            }
        }
    }
}