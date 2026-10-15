using RaceReplay.Playback;
using RaceReplay.Web.State;

namespace RaceReplay.Web.Tests;

public sealed class UiPublisherTests
{
    private const double Duration = 600;

    private readonly PlaybackClock _clock = new(Duration, new PlaybackOptions());
    private readonly ReplayStore _store = new();
    private readonly UiPublisher _publisher;
    private int _publishes;

    public UiPublisherTests()
    {
        _publisher = new UiPublisher(_clock, _store, refreshHz: 10);
        _store.Changed += () => _publishes++;
    }

    [Fact]
    public void PublishNow_PublishesTheClockState()
    {
        _clock.Seek(42);
        _clock.SetRate(8);

        _publisher.PublishNow();

        Assert.Equal(new ReplayViewState(42, Duration, IsPlaying: false, Rate: 8), _store.State);
        Assert.Equal(1, _publishes);
    }

    [Fact]
    public void MaybePublish_Playing_PublishesAtMostTenTimesASecond()
    {
        _publisher.PublishNow();
        _clock.Play();
        _publishes = 0;

        for (double t = 0; t < 1_000; t += 1_000.0 / 60)
        {
            _clock.Advance(t);
            _publisher.MaybePublish(t);
        }

        Assert.InRange(_publishes, 9, 11);
    }

    [Fact]
    public void MaybePublish_NothingChanged_DoesNotPublish()
    {
        _publisher.PublishNow();
        _publishes = 0;

        _publisher.MaybePublish(1_000);
        _publisher.MaybePublish(2_000);

        Assert.Equal(0, _publishes);
    }

    [Fact]
    public void MaybePublish_PauseRightAfterAPublish_PublishesImmediately()
    {
        _clock.Play();
        _publisher.MaybePublish(1_000);
        _publishes = 0;

        _clock.Pause();
        _publisher.MaybePublish(1_016);

        Assert.Equal(1, _publishes);
        Assert.False(_store.State.IsPlaying);
    }

    [Fact]
    public void MaybePublish_RateChangeRightAfterAPublish_PublishesImmediately()
    {
        _publisher.MaybePublish(1_000);
        _publishes = 0;

        _clock.SetRate(2);
        _publisher.MaybePublish(1_016);

        Assert.Equal(2, _store.State.Rate);
    }

    [Fact]
    public void MaybePublish_SeekRightAfterAPublish_WaitsForTheInterval()
    {
        _publisher.MaybePublish(1_000);
        _publishes = 0;

        _clock.Seek(100);
        _publisher.MaybePublish(1_016);
        int early = _publishes;
        _publisher.MaybePublish(1_100);

        Assert.Equal(0, early);
        Assert.Equal(100, _store.State.Position);
    }
}