using RaceReplay.Domain;
using RaceReplay.Playback;
using RaceReplay.TestKit;
using RaceReplay.Web.Rendering;
using RaceReplay.Web.State;

namespace RaceReplay.Web.Tests;

public sealed class RenderLoopTests
{
    private readonly ReplaySession _replay = TestReplays.Small(cars: 3);
    private readonly FakeTrackMapRenderer _map = new();
    private readonly ReplayStore _store = new();
    private readonly PlaybackClock _clock;
    private readonly FrameSampler _sampler;
    private readonly RenderLoop _loop;

    public RenderLoopTests()
    {
        _clock = new PlaybackClock(_replay.Timeline.Duration, new PlaybackOptions());
        _sampler = new FrameSampler(_replay);
        _loop = new RenderLoop(_clock, _sampler, _map, new UiPublisher(_clock, _store));
    }

    [Fact]
    public void Tick_Playing_DrawsOneFrameSampledAtTheClockPosition()
    {
        _clock.SetRate(4);
        _clock.Play();

        _loop.Tick(1_000);
        _loop.Tick(1_200); // 0.2 s at 4× = 0.8 s

        Assert.Equal(2, _map.FramesDrawn);
        Assert.Equal(FrameSampler.Stride, _map.LastStride);
        Assert.Equal(0.8, _clock.Position, 9);
        Assert.Equal(SampleAt(0.8), _map.LastFrame);
    }

    [Fact]
    public void Tick_Paused_KeepsDrawingTheSameFrame()
    {
        _clock.Seek(5);

        _loop.Tick(1_000);
        double[] first = [.. _map.LastFrame];
        _loop.Tick(2_000);

        Assert.Equal(2, _map.FramesDrawn);
        Assert.Equal(first, _map.LastFrame);
        Assert.Equal(SampleAt(5), _map.LastFrame);
    }

    [Fact]
    public void Tick_AfterSeek_DrawsTheNewPositionOnTheNextFrame()
    {
        _loop.Tick(1_000);

        _clock.Seek(12.5);
        _loop.Tick(1_016);

        Assert.Equal(SampleAt(12.5), _map.LastFrame); // NFR-02: within one frame
    }

    [Fact]
    public void Tick_PublishesViewState()
    {
        _clock.Play();

        _loop.Tick(1_000);

        Assert.True(_store.State.IsPlaying);
        Assert.Equal(_replay.Timeline.Duration, _store.State.Duration);
    }

    [Fact]
    public void Tick_AllocatesNothing()
    {
        // 1,000 frames of 16 ms is 16 s, inside the 25 s replay, so the clock keeps playing throughout.
        _clock.Play();
        double t = 0;
        for (int i = 0; i < 1_000; i++)
        {
            _loop.Tick(t += 16); // warm up (JIT tiers)
        }

        _clock.Seek(0);
        _clock.Play();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            _loop.Tick(t += 16); // includes view-state publishes at 10 Hz
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); // NFR-01
    }

    private double[] SampleAt(double position)
    {
        double[] frame = new double[_sampler.FrameLength];
        _sampler.Sample(position, frame);
        return frame;
    }
}