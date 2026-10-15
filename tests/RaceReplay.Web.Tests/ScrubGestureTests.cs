using RaceReplay.Playback;
using RaceReplay.Web.Presentation;

namespace RaceReplay.Web.Tests;

public sealed class ScrubGestureTests
{
    private readonly RecordingControl _control = new();
    private readonly ScrubGesture _gesture = new();

    [Fact]
    public void Drag_PointerDownInputsAndUp_BeginsSeeksEachInputAndEndsOnce()
    {
        _gesture.Begin(_control);
        ScrubGesture.Seek(_control, "120");
        ScrubGesture.Seek(_control, "121.5");
        _gesture.End(_control); // pointer up
        _gesture.End(_control); // the change event that follows

        Assert.Equal(["BeginScrub", "Seek 120", "Seek 121.5", "EndScrub"], _control.Calls);
        Assert.False(_gesture.IsActive);
    }

    [Fact]
    public void Begin_Twice_BeginsOneScrub()
    {
        _gesture.Begin(_control);
        _gesture.Begin(_control);

        Assert.Equal(["BeginScrub"], _control.Calls);
        Assert.True(_gesture.IsActive);
    }

    [Fact]
    public void Keyboard_InputAndChangeWithoutPointer_SeeksWithoutScrubbing()
    {
        ScrubGesture.Seek(_control, "30");
        _gesture.End(_control); // change

        Assert.Equal(["Seek 30"], _control.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Seek_NotAFiniteNumber_IsIgnored(string? value)
    {
        ScrubGesture.Seek(_control, value);

        Assert.Empty(_control.Calls);
    }

    [Fact]
    public void Drag_WhilePlaying_PausesThenResumesTheRealClock()
    {
        PlaybackClock clock = new(600, new PlaybackOptions());
        clock.Play();

        _gesture.Begin(clock);
        bool playingDuringDrag = clock.IsPlaying;
        ScrubGesture.Seek(clock, "300");
        _gesture.End(clock);

        Assert.False(playingDuringDrag);
        Assert.True(clock.IsPlaying);
        Assert.Equal(300, clock.Position);
    }

    private sealed class RecordingControl : IPlaybackControl
    {
        public List<string> Calls { get; } = [];

        public void Play() => Calls.Add("Play");

        public void Pause() => Calls.Add("Pause");

        public void Seek(double position) =>
            Calls.Add("Seek " + position.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public void SetRate(double rate) => Calls.Add("SetRate");

        public void BeginScrub() => Calls.Add("BeginScrub");

        public void EndScrub() => Calls.Add("EndScrub");
    }
}