using RaceReplay.Domain;
using RaceReplay.Playback;
using RaceReplay.Web.State;

namespace RaceReplay.Web.Rendering;

/// <summary>
/// Owns the per-replay object graph: a fresh clock, sampler and render loop for each replay, torn down before the
/// next, so components never see stale replay state (docs/05 §2, §9.3).
/// </summary>
public sealed class ReplaySessionHost(
    ITrackMapRenderer map, IFrameScheduler scheduler, ReplayStore store, PlaybackOptions options) : IDisposable
{
    private PlaybackClock? _clock;

    public PlaybackOptions Options => options;

    /// <summary>The open replay, or <see langword="null"/>.</summary>
    public ReplaySession? Replay { get; private set; }

    /// <summary>The open replay's clock for controls, or <see langword="null"/>.</summary>
    public IPlaybackControl? Control => _clock;

    /// <summary>The open replay's clock for display, or <see langword="null"/>.</summary>
    public IPlaybackClockReader? Clock => _clock;

    /// <summary>Closes any open replay, then opens <paramref name="replay"/>, paused, and starts drawing frames.</summary>
    /// <param name="replay">The replay to open.</param>
    /// <param name="trackMapElementId">The element the track map's canvases go in.</param>
    /// <param name="startAt">Where to start, in seconds since T0 (clamped to the replay); <see langword="null"/> for the beginning.</param>
    public void Open(ReplaySession replay, string trackMapElementId, double? startAt)
    {
        ArgumentNullException.ThrowIfNull(replay);
        Close();

        PlaybackClock clock = new(replay.Timeline.Duration, options);
        if (startAt is double position)
        {
            clock.Seek(position);
        }

        UiPublisher publisher = new(clock, store);
        RenderLoop loop = new(clock, new FrameSampler(replay), map, publisher);
        map.Initialise(trackMapElementId, TrackMapScene.For(replay));

        Replay = replay;
        _clock = clock;
        publisher.PublishNow();
        scheduler.Attach(loop);
    }

    /// <summary>Stops drawing and removes the track map. Does nothing when no replay is open.</summary>
    public void Close()
    {
        if (Replay is null)
        {
            return;
        }

        scheduler.Detach();
        map.Clear();
        Replay = null;
        _clock = null;
        store.Publish(default);
    }

    public void Dispose() => Close();
}