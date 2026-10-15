namespace RaceReplay.Domain.Tests;

public sealed class SessionKeyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(SessionKey.MaxSyntheticSeed)]
    public void Synthetic_ValidSeed_RoundTripsThroughTheReservedRange(int seed)
    {
        SessionKey key = SessionKey.Synthetic(seed);

        Assert.True(key.IsSynthetic);
        Assert.Equal(seed, key.SyntheticSeed);
        Assert.True(key.Value >= SessionKey.SyntheticBase);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(SessionKey.MaxSyntheticSeed + 1)]
    public void Synthetic_SeedOutsideRange_Throws(int seed) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionKey.Synthetic(seed));

    [Fact]
    public void SyntheticSeed_OpenF1Key_Throws()
    {
        SessionKey key = new(9839);

        Assert.False(key.IsSynthetic);
        Assert.Throws<InvalidOperationException>(() => key.SyntheticSeed);
    }

    [Fact]
    public void ToString_AnyKey_IsTheInvariantNumber() =>
        Assert.Equal("1000000007", SessionKey.Synthetic(7).ToString());
}

public sealed class RgbColourTests
{
    [Theory]
    [InlineData("3671C6")]
    [InlineData("#3671C6")]
    [InlineData("3671c6")]
    public void FromHex_SixHexDigits_ParsesEachChannel(string hex)
    {
        RgbColour colour = RgbColour.FromHex(hex);

        Assert.Equal(new RgbColour(0x36, 0x71, 0xC6), colour);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("3671C")]
    [InlineData("3671C6F")]
    [InlineData("GG71C6")]
    [InlineData("+671C6")]
    [InlineData(" 3671C6")]
    public void FromHex_NotSixHexDigits_ThrowsFormatException(string hex)
    {
        Assert.Throws<FormatException>(() => RgbColour.FromHex(hex));
        Assert.False(RgbColour.TryFromHex(hex, out _));
    }

    [Fact]
    public void ToHex_AnyColour_IsSixUpperCaseDigitsWithoutHash()
    {
        RgbColour colour = new(0x0A, 0xFF, 0x00);

        Assert.Equal("0AFF00", colour.ToHex());
        Assert.Equal("0AFF00", colour.ToString());
        Assert.Equal(colour, RgbColour.FromHex(colour.ToHex()));
    }
}

public sealed class SmallValueTests
{
    [Fact]
    public void GapValue_Factories_SetKindAndValue()
    {
        Assert.Equal(new GapValue(GapKind.Seconds, 1.5, 0), GapValue.FromSeconds(1.5));
        Assert.Equal(new GapValue(GapKind.Laps, 0, 2), GapValue.FromLaps(2));
        Assert.Equal(GapKind.None, GapValue.None.Kind);
    }

    [Fact]
    public void DriverNumber_ToString_IsTheInvariantNumber() => Assert.Equal("44", new DriverNumber(44).ToString());

    [Theory]
    [InlineData(DriverNumber.MinValue)]
    [InlineData(DriverNumber.MaxValue)]
    public void DriverNumber_InRange_IsAccepted(int value) => Assert.Equal(value, new DriverNumber(value).Value);

    [Theory]
    [InlineData(-1)]
    [InlineData(DriverNumber.MaxValue + 1)]
    [InlineData(65_537)]
    public void DriverNumber_OutOfRange_Throws(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DriverNumber(value));

    [Fact]
    public void SessionInfo_NonUtcStart_Throws() =>
        Assert.Throws<ArgumentException>(() => new SessionInfo(
            new SessionKey(9839), 2025, "Race", "Race", "Abu Dhabi Grand Prix", "Yas Marina",
            new DateTimeOffset(2025, 12, 7, 17, 0, 0, TimeSpan.FromHours(4))));

    [Fact]
    public void SessionInfo_NullName_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new SessionInfo(
            new SessionKey(9839), 2025, "Race", null!, "Abu Dhabi Grand Prix", "Yas Marina", DateTimeOffset.UnixEpoch));

    [Fact]
    public void SessionInfo_UtcStart_KeepsEveryField()
    {
        DateTimeOffset start = new(2025, 12, 7, 13, 0, 0, TimeSpan.Zero);

        SessionInfo info = new(new SessionKey(9839), 2025, "Race", "Race", "Abu Dhabi Grand Prix", "Yas Marina", start);

        Assert.Equal(
            (new SessionKey(9839), 2025, "Race", "Race", "Abu Dhabi Grand Prix", "Yas Marina", start),
            (info.Key, info.Year, info.Type, info.Name, info.MeetingName, info.Circuit, info.StartUtc));
    }

    [Theory]
    [InlineData(10, 9.5)]
    [InlineData(10, double.NaN)]
    public void TrackStatusEvent_EndBeforeStart_Throws(double time, double end) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrackStatusEvent(time, end, TrackStatus.SafetyCar));

    [Theory]
    [InlineData(10, 10.0)]
    [InlineData(10, null)]
    public void TrackStatusEvent_EndAtOrAfterStartOrOpen_IsAccepted(double time, double? end)
    {
        TrackStatusEvent e = new(time, end, TrackStatus.VirtualSafetyCar);

        Assert.Equal(end, e.EndTime);
        Assert.Equal(TrackStatus.VirtualSafetyCar, e.Status);
    }

    [Fact]
    public void TrackBounds_Of_Points_IsTheTightBox()
    {
        TrackPoint[] points = [new(1, 5), new(-3, 2), new(4, -1)];

        TrackBounds bounds = TrackBounds.Of(points);

        Assert.Equal(new TrackBounds(-3, -1, 4, 5), bounds);
        Assert.Equal(7, bounds.Width);
        Assert.Equal(6, bounds.Height);
    }

    [Fact]
    public void TrackBounds_Of_NoPoints_Throws() =>
        Assert.Throws<ArgumentException>(() => TrackBounds.Of([]));
}