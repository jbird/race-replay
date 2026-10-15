using RaceReplay.Domain;
using RaceReplay.Playback;
using RaceReplay.TestKit;
using RaceReplay.Web.Rendering;
using RaceReplay.Web.State;

namespace RaceReplay.Web.Tests;

public sealed class ReplaySessionHostTests : IDisposable
{
    private const string MapId = "track-map";

    private readonly FakeTrackMapRenderer _map = new();
    private readonly FakeFrameScheduler _scheduler = new();
    private readonly ReplayStore _store = new();
    private readonly ReplaySessionHost _host;

    public ReplaySessionHostTests() => _host = new ReplaySessionHost(_map, _scheduler, _store, new PlaybackOptions());

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Open_DrawsTheTrackAndStartsFramesPaused()
    {
        ReplaySession replay = TestReplays.Small(cars: 3);

        _host.Open(replay, MapId, startAt: null);
        _scheduler.Tick(1_000);

        Assert.Same(replay, _host.Replay);
        Assert.Equal(MapId, _map.HostElementId);
        Assert.Equal(3, _map.Scene!.Acronyms.Length);
        Assert.Equal(1, _scheduler.Attached);
        Assert.Equal(1, _map.FramesDrawn);
        Assert.Equal(new ReplayViewState(0, replay.Timeline.Duration, IsPlaying: false, Rate: 1), _store.State);
    }

    [Theory]
    [InlineData(10.0, false)]
    [InlineData(1e9, true)]
    public void Open_WithStartTime_StartsTherePausedClampedToTheEnd(double startAt, bool pastTheEnd)
    {
        ReplaySession replay = TestReplays.Small();

        _host.Open(replay, MapId, startAt);

        double position = pastTheEnd ? replay.Timeline.Duration : startAt;
        Assert.Equal(position, _host.Clock!.Position);
        Assert.Equal(position, _store.State.Position);
        Assert.False(_host.Clock.IsPlaying);
    }

    [Fact]
    public void Open_WhileAnotherIsOpen_TearsTheFirstDownBeforeTheSecond()
    {
        _host.Open(TestReplays.Small(cars: 3), MapId, startAt: null);
        IPlaybackControl first = _host.Control!;

        ReplaySession second = TestReplays.Small(cars: 5);
        _host.Open(second, MapId, startAt: null);
        _scheduler.Tick(1_000);

        Assert.Equal(1, _scheduler.Detached);
        Assert.Equal(1, _map.Cleared);
        Assert.Equal(2, _map.Initialised);
        Assert.Equal(5, _map.Scene!.Acronyms.Length);
        Assert.NotSame(first, _host.Control);
        Assert.Equal(5 * FrameSampler.Stride, _map.LastFrame.Length);
    }

    [Fact]
    public void Close_StopsFramesClearsTheMapAndResetsViewState()
    {
        _host.Open(TestReplays.Small(), MapId, startAt: 30);

        _host.Close();
        _scheduler.Tick(1_000);

        Assert.Null(_host.Replay);
        Assert.Null(_host.Control);
        Assert.Equal(1, _scheduler.Detached);
        Assert.Equal(1, _map.Cleared);
        Assert.Equal(0, _map.FramesDrawn);
        Assert.Equal(default, _store.State);
    }

    [Fact]
    public void Close_NothingOpen_DoesNothing()
    {
        _host.Close();

        Assert.Equal(0, _scheduler.Detached);
        Assert.Equal(0, _map.Cleared);
    }
}