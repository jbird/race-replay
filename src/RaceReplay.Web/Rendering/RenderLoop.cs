using RaceReplay.Playback;
using RaceReplay.Web.State;

namespace RaceReplay.Web.Rendering;

/// <summary>
/// One display frame: advance the clock, sample every car, draw, and maybe publish view state (docs/05 §5.3). The frame
/// buffer is allocated once, so a tick allocates nothing (NFR-01).
/// </summary>
public sealed class RenderLoop
{
    private readonly IPlaybackClock _clock;
    private readonly FrameSampler _sampler;
    private readonly ITrackMapRenderer _map;
    private readonly UiPublisher _publisher;
    private readonly double[] _frame;

    public RenderLoop(IPlaybackClock clock, FrameSampler sampler, ITrackMapRenderer map, UiPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(sampler);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(publisher);
        _clock = clock;
        _sampler = sampler;
        _map = map;
        _publisher = publisher;
        _frame = new double[sampler.FrameLength];
    }

    /// <param name="timestampMs">The <c>requestAnimationFrame</c> timestamp.</param>
    public void Tick(double timestampMs)
    {
        double position = _clock.Advance(timestampMs);
        _sampler.Sample(position, _frame);
        _map.DrawFrame(_frame, FrameSampler.Stride);
        _publisher.MaybePublish(timestampMs);
    }
}