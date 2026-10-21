using RaceReplay.Analysis;
using RaceReplay.Domain;

namespace RaceReplay.Application;

/// <summary>Per-build settings.</summary>
/// <param name="Builder">Recorded in the provenance, e.g. <c>race-replay 0.1.0+3f2c1ab</c>.</param>
public sealed record BuildOptions(string Builder);

public sealed record BuildResult(ReplaySession Replay);

/// <summary>
/// Turns raw session data into a replay (docs/02 §7). Deliberately free of I/O. It cleans each car's positions
/// (docs/03 §4), outlines the track from several reference laps and resamples the cleaned positions onto the
/// timeline; progress, gaps, events and validators arrive with <c>SessionAnalyzer</c> and M3.
/// </summary>
public sealed class ReplayBuilder(LocationCleaner cleaner, CentrelineBuilder centreline, TimelineResampler resampler, TimeProvider time)
{
    /// <exception cref="InsufficientDataException">
    /// The data can't support a replay, including location samples filed under the wrong driver.
    /// </exception>
    public BuildResult Build(RawSessionData raw, BuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(options);
        if (raw.Drivers.Count == 0)
        {
            throw new InsufficientDataException("The session has no drivers.");
        }

        Dictionary<DriverNumber, CleanedTrace> traces = Clean(raw.Locations);
        TrackModel track = centreline.Build(raw, traces).Track;
        (DateTimeOffset t0, int sampleCount) = resampler.Grid(raw.Locations.Values);
        double duration = (sampleCount - 1) / resampler.TimelineHz;

        CarChannels[] cars =
        [
            .. raw.Drivers.Select(d => traces.TryGetValue(d.Number, out CleanedTrace? trace)
                ? resampler.Resample(trace, t0, sampleCount)
                : resampler.Resample(d.Number, [], t0, sampleCount)),
        ];

        double raceStart = Math.Clamp(Seconds(RaceStartUtc(raw) ?? t0, t0), 0, duration);
        double finish = Math.Clamp(Seconds(FinishUtc(raw) ?? t0.AddSeconds(duration), t0), raceStart, duration);
        ReplayTimeline timeline = new(t0, resampler.TimelineHz, sampleCount, raceStart, Segments(raceStart, finish, duration));

        SessionKey key = raw.Session.Key;
        Provenance provenance = key.IsSynthetic
            ? Provenance.ForSynthetic(key.SyntheticSeed, time.GetUtcNow(), options.Builder)
            : Provenance.ForOpenF1(time.GetUtcNow(), options.Builder);

        return new BuildResult(new ReplaySession(raw.Session, raw.Drivers, track, timeline, cars, GapSeries.Empty, [], provenance));
    }

    // Every car's samples cleaned into runs. A sample filed under another driver breaks the provider contract, so it's
    // a data error the CLI reports, not a crash.
    private Dictionary<DriverNumber, CleanedTrace> Clean(IReadOnlyDictionary<DriverNumber, IReadOnlyList<RawLocationSample>> locations)
    {
        foreach ((DriverNumber driver, IReadOnlyList<RawLocationSample> samples) in locations)
        {
            foreach (RawLocationSample sample in samples)
            {
                if (sample.Driver != driver)
                {
                    throw new InsufficientDataException($"The location samples for driver {driver} include one from driver {sample.Driver}.");
                }
            }
        }

        return locations.ToDictionary(p => p.Key, p => cleaner.Clean(p.Key, p.Value));
    }

    // Lap 1 starts with the race (docs/03 §6.3).
    private static DateTimeOffset? RaceStartUtc(RawSessionData raw) =>
        raw.Laps.Where(l => l.LapNumber == 1 && l.Start is not null).Select(l => l.Start).Min();

    // The winner's chequered flag: the end of their last lap.
    private static DateTimeOffset? FinishUtc(RawSessionData raw)
    {
        SessionResult? winner = raw.Results.FirstOrDefault(r => r.Position == 1);
        RawLap? last = winner is null
            ? null
            : raw.Laps.Where(l => l.Driver == winner.Driver && l.Start is not null && l.Duration is not null)
                .MaxBy(l => l.LapNumber);
        return last?.Start!.Value.AddSeconds(last.Duration!.Value);
    }

    private static TimelineSegment[] Segments(double raceStart, double finish, double duration)
    {
        List<TimelineSegment> segments = [];
        if (raceStart > 0)
        {
            segments.Add(new TimelineSegment(0, raceStart, SegmentKind.PreRace));
        }

        segments.Add(new TimelineSegment(raceStart, finish, SegmentKind.Racing));
        if (finish < duration)
        {
            segments.Add(new TimelineSegment(finish, duration, SegmentKind.PostRace));
        }

        return [.. segments];
    }

    private static double Seconds(DateTimeOffset at, DateTimeOffset t0) =>
        (at.UtcTicks - t0.UtcTicks) / (double)TimeSpan.TicksPerSecond;
}