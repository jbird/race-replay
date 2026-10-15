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