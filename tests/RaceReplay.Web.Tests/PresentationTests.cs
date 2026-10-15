using RaceReplay.Web.Presentation;

namespace RaceReplay.Web.Tests;

public sealed class DeepLinkTests
{
    [Theory]
    [InlineData("600", 600.0)]
    [InlineData("0", 0.0)]
    [InlineData("12.5", 12.5)]
    [InlineData("99999999", 99999999.0)] // past the end: the clock clamps it
    public void ParseTime_ValidSeconds_ReturnsThem(string value, double expected) =>
        Assert.Equal(expected, DeepLink.ParseTime(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("12,5")]
    public void ParseTime_MissingNegativeOrNotANumber_ReturnsNull(string? value) =>
        Assert.Null(DeepLink.ParseTime(value));
}

public sealed class TimeFormatTests
{
    [Theory]
    [InlineData(0, "0:00:00")]
    [InlineData(59.99, "0:00:59")]
    [InlineData(3734, "1:02:14")]
    [InlineData(36_000, "10:00:00")]
    [InlineData(-3, "0:00:00")]
    [InlineData(double.NaN, "0:00:00")]
    public void Clock_Seconds_FormatsAsHoursMinutesSeconds(double seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.Clock(seconds));

    [Theory]
    [InlineData(0, "0 seconds")]
    [InlineData(1, "1 second")]
    [InlineData(60, "1 minute")]
    [InlineData(3734, "1 hour 2 minutes 14 seconds")]
    [InlineData(7201, "2 hours 1 second")]
    public void Spoken_Seconds_ReadsNaturally(double seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.Spoken(seconds));
}