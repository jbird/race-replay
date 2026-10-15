using RaceReplay.Playback;

namespace RaceReplay.Web.State;

/// <summary>
/// Turns the per-frame clock into view-state updates at most <c>refreshHz</c> times a second, so Blazor diffs about
/// 10 times a second rather than 60 (docs/05 §8). Play/pause and rate changes publish on the next frame. Allocation-free.
/// </summary>
public sealed class UiPublisher
{
    public const double DefaultRefreshHz = 10;

    private readonly IPlaybackClockReader _clock;
    private readonly ReplayStore _store;
    private readonly double _intervalMs;
    private double _lastPublishMs = double.NegativeInfinity;

    public UiPublisher(IPlaybackClockReader clock, ReplayStore store, double refreshHz = DefaultRefreshHz)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(refreshHz);
        _clock = clock;
        _store = store;
        _intervalMs = 1000 / refreshHz;
    }

    /// <summary>Publishes the clock's state now, e.g. when a replay opens.</summary>
    public void PublishNow() => _store.Publish(Snapshot());

    /// <summary>Called every frame with the <c>requestAnimationFrame</c> timestamp (ms).</summary>
    public void MaybePublish(double timestampMs)
    {
        ReplayViewState last = _store.State;
        ReplayViewState now = Snapshot();
        if (now == last)
        {
            return;
        }

        bool discrete = now.IsPlaying != last.IsPlaying || now.Rate != last.Rate || now.Duration != last.Duration;
        bool due = timestampMs - _lastPublishMs >= _intervalMs || timestampMs < _lastPublishMs;
        if (discrete || due)
        {
            _lastPublishMs = timestampMs;
            _store.Publish(now);
        }
    }

    private ReplayViewState Snapshot() => new(_clock.Position, _clock.Duration, _clock.IsPlaying, _clock.Rate);
}