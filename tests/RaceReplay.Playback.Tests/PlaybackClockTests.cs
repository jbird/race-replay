namespace RaceReplay.Playback.Tests;

public sealed class PlaybackClockTests
{
    private static readonly PlaybackOptions s_options = new();

    private static PlaybackClock Clock(double duration = 100) => new(duration, s_options);

    [Fact]
    public void NewClock_StartsPausedAtZeroInRealTime()
    {
        PlaybackClock clock = Clock();

        Assert.Equal((0.0, false, 1.0, 100.0), (clock.Position, clock.IsPlaying, clock.Rate, clock.Duration));
    }

    [Fact]
    public void Advance_FirstFrameAfterPlay_OnlyRecordsTheTimestamp()
    {
        PlaybackClock clock = Clock();
        clock.Play();

        Assert.Equal(0, clock.Advance(5_000));
        Assert.Equal(0.016, clock.Advance(5_016), 1e-12);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(1)]
    [InlineData(64)]
    public void Advance_Rate_ScalesElapsedTime(double rate)
    {
        PlaybackClock clock = Clock(10_000);
        clock.SetRate(rate);
        clock.Play();
        clock.Advance(0);

        for (int frame = 1; frame <= 60; frame++)
        {
            clock.Advance(frame * 1000.0 / 60);
        }

        Assert.Equal(rate, clock.Position, 1e-9); // one second of frames
    }

    [Fact]
    public void Advance_WhilePaused_DoesNotMove()
    {
        PlaybackClock clock = Clock();
        clock.Seek(10);

        clock.Advance(0);
        clock.Advance(1_000);

        Assert.Equal(10, clock.Position);
    }

    [Fact]
    public void Advance_LongGapBetweenFrames_IsCapped()
    {
        PlaybackClock clock = Clock();
        clock.Play();
        clock.Advance(0);

        clock.Advance(60_000); // a minute in a background tab

        Assert.Equal(s_options.MaxFrameDeltaMs / 1000, clock.Position, 1e-12);
    }

    [Fact]
    public void Advance_TimestampGoesBackwards_DoesNotRewind()
    {
        PlaybackClock clock = Clock();
        clock.Play();
        clock.Advance(1_000);
        clock.Advance(1_100);

        clock.Advance(900);

        Assert.Equal(0.1, clock.Position, 1e-12);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Advance_NonFiniteTimestamp_IsIgnored(double timestamp)
    {
        PlaybackClock clock = Clock();
        clock.Play();
        clock.Advance(0);

        clock.Advance(timestamp);

        Assert.Equal(0.016, clock.Advance(16), 1e-12); // measured from the last good frame
    }

    [Fact]
    public void Advance_ReachingTheEnd_StopsThere()
    {
        PlaybackClock clock = Clock(1);
        clock.Play();
        clock.Advance(0);

        for (int ms = 100; ms <= 2_000; ms += 100)
        {
            clock.Advance(ms);
        }

        Assert.Equal(1, clock.Position);
        Assert.False(clock.IsPlaying);
    }

    [Fact]
    public void Play_AtTheEnd_StartsAgainFromZero()
    {
        PlaybackClock clock = Clock();
        clock.Seek(100);

        clock.Play();

        Assert.Equal(0, clock.Position);
        Assert.True(clock.IsPlaying);
    }

    [Fact]
    public void Pause_WhilePlaying_FreezesPosition()
    {
        PlaybackClock clock = Clock();
        clock.Play();
        clock.Advance(0);
        clock.Advance(100);

        clock.Pause();
        clock.Advance(200);

        Assert.Equal(0.1, clock.Position, 1e-12);
        Assert.False(clock.IsPlaying);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(42.5, 42.5)]
    [InlineData(250, 100)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(double.PositiveInfinity, 100)]
    public void Seek_Position_IsClampedToTheReplay(double seek, double expected)
    {
        PlaybackClock clock = Clock();

        clock.Seek(seek);

        Assert.Equal(expected, clock.Position);
    }

    [Fact]
    public void Seek_NaN_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() => Clock().Seek(double.NaN));

    [Fact]
    public void Scrub_WhilePlaying_PausesThenResumes()
    {
        PlaybackClock clock = Clock();
        clock.Play();

        clock.BeginScrub();
        Assert.False(clock.IsPlaying);
        clock.Seek(50);
        clock.EndScrub();

        Assert.True(clock.IsPlaying);
        Assert.Equal(50, clock.Position);
        Assert.Equal(50, clock.Advance(10_000)); // resuming starts a fresh frame interval
    }

    [Fact]
    public void Scrub_WhilePaused_StaysPaused()
    {
        PlaybackClock clock = Clock();

        clock.BeginScrub();
        clock.Seek(20);
        clock.EndScrub();

        Assert.False(clock.IsPlaying);
    }

    [Fact]
    public void EndScrub_Twice_ResumesOnlyOnce()
    {
        PlaybackClock clock = Clock();
        clock.Play();
        clock.BeginScrub();
        clock.EndScrub();
        clock.Pause();

        clock.EndScrub();

        Assert.False(clock.IsPlaying);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(65)]
    [InlineData(double.NaN)]
    public void SetRate_OutsideTheOfferedRange_Throws(double rate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Clock().SetRate(rate));

    [Fact]
    public void Constructor_RatesWithoutRealTime_StartsAtTheNearestRate()
    {
        PlaybackClock clock = new(100, new PlaybackOptions { Rates = [2, 4, 8] });

        Assert.Equal(2, clock.Rate);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Constructor_InvalidDuration_Throws(double duration) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock(duration, s_options));

    [Fact]
    public void Constructor_InvalidOptions_Throw()
    {
        Assert.Throws<ArgumentException>(() => new PlaybackClock(10, new PlaybackOptions { Rates = [] }));
        Assert.Throws<ArgumentException>(() => new PlaybackClock(10, new PlaybackOptions { Rates = [1, -2] }));
        Assert.Throws<ArgumentException>(() => new PlaybackClock(10, new PlaybackOptions { MaxFrameDeltaMs = 0 }));
    }

    [Fact]
    public void Advance_ManyFrames_AllocatesNothing()
    {
        PlaybackClock clock = Clock(1_000_000);
        clock.Play();
        for (int frame = 0; frame < 1_000; frame++)
        {
            clock.Advance(frame * 16.0); // warm up (JIT tiers)
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 1_000; frame < 11_000; frame++)
        {
            clock.Advance(frame * 16.0);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}