namespace RaceReplay.Domain.Tests;

public sealed class CarChannelsTests
{
    private static readonly DriverNumber s_driver = new(44);

    [Fact]
    public void Constructor_MatchingLengths_ExposesChannels()
    {
        float[] x = [1, 2, 3];
        float[] y = [4, 5, 6];
        ValidityMask valid = ValidityMask.FromBools([true, false, true]);

        CarChannels car = new(s_driver, x, y, valid);

        Assert.Equal(s_driver, car.Driver);
        Assert.Equal(3, car.SampleCount);
        Assert.Equal(x, car.X.ToArray());
        Assert.Equal(y, car.Y.ToArray());
        Assert.Same(valid, car.Valid);
        Assert.False(car.HasProgress);
    }

    [Fact]
    public void Constructor_WithProgress_HasProgress()
    {
        CarChannels car = new(s_driver, new float[3], new float[3], ValidityMask.FromBools([true, true, true]), new float[] { 0, 5, 10 });

        Assert.True(car.HasProgress);
        Assert.Equal(10, car.Progress.Span[2]);
    }

    [Theory]
    [InlineData(2, 3, 0)]
    [InlineData(3, 2, 0)]
    [InlineData(3, 3, 2)]
    public void Constructor_LengthMismatch_Throws(int xLength, int yLength, int progressLength) =>
        Assert.Throws<ArgumentException>(() => new CarChannels(
            s_driver, new float[xLength], new float[yLength], ValidityMask.FromBools([true, true, true]), new float[progressLength]));
}

public sealed class GapSeriesTests
{
    [Fact]
    public void GapFor_KnownDriver_ReturnsItsSeries()
    {
        Dictionary<DriverNumber, ReadOnlyMemory<float>> gaps = new()
        {
            [new DriverNumber(1)] = new float[] { 0, 0, 0 },
            [new DriverNumber(4)] = new float[] { float.NaN, 1.5f, 1.75f },
        };

        GapSeries series = new(1, gaps);

        Assert.Equal(1, series.SampleRateHz);
        Assert.Equal(3, series.SampleCount);
        Assert.False(series.IsEmpty);
        Assert.Equal(1.75f, series.GapFor(new DriverNumber(4)).Span[2]);
        Assert.True(series.TryGetGap(new DriverNumber(1), out ReadOnlyMemory<float> leader));
        Assert.Equal(3, leader.Length);
    }

    [Fact]
    public void GapFor_UnknownDriver_Throws()
    {
        GapSeries series = new(1, new Dictionary<DriverNumber, ReadOnlyMemory<float>> { [new DriverNumber(1)] = new float[2] });

        Assert.Throws<KeyNotFoundException>(() => series.GapFor(new DriverNumber(99)));
        Assert.False(series.TryGetGap(new DriverNumber(99), out _));
    }

    [Fact]
    public void Constructor_SeriesOfDifferentLengths_Throws() =>
        Assert.Throws<ArgumentException>(() => new GapSeries(1, new Dictionary<DriverNumber, ReadOnlyMemory<float>>
        {
            [new DriverNumber(1)] = new float[3],
            [new DriverNumber(4)] = new float[2],
        }));

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    public void Constructor_RateNotPositive_Throws(double rate) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GapSeries(rate, new Dictionary<DriverNumber, ReadOnlyMemory<float>>()));

    [Fact]
    public void Empty_HasNoSeries()
    {
        Assert.True(GapSeries.Empty.IsEmpty);
        Assert.Equal(0, GapSeries.Empty.SampleCount);
    }
}

public sealed class ProvenanceTests
{
    private static readonly DateTimeOffset s_when = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ForSynthetic_Seed_IsSyntheticWithoutLicence()
    {
        Provenance provenance = Provenance.ForSynthetic(7, s_when, "race-replay 0.1.0");

        Assert.True(provenance.IsSynthetic);
        Assert.Equal(DataSource.Synthetic, provenance.Source);
        Assert.Equal(7, provenance.Seed);
        Assert.Null(provenance.Licence);
        Assert.Equal(s_when, provenance.RetrievedUtc);
        Assert.Equal("race-replay 0.1.0", provenance.Builder);
    }

    [Fact]
    public void ForOpenF1_Always_CarriesAttributionAndLicence()
    {
        Provenance provenance = Provenance.ForOpenF1(s_when, "race-replay 0.1.0");

        Assert.False(provenance.IsSynthetic);
        Assert.Equal(Provenance.OpenF1Attribution, provenance.Attribution);
        Assert.Equal(Provenance.OpenF1Licence, provenance.Licence);
        Assert.Null(provenance.Seed);
    }

    [Fact]
    public void Constructor_SyntheticWithoutSeed_Throws() =>
        Assert.Throws<ArgumentException>(() => new Provenance(DataSource.Synthetic, s_when, "b"));

    [Fact]
    public void Constructor_OpenF1WithSeed_Throws() =>
        Assert.Throws<ArgumentException>(
            () => new Provenance(DataSource.OpenF1, s_when, "b", Provenance.OpenF1Attribution, Provenance.OpenF1Licence, seed: 1));

    [Theory]
    [InlineData(null, "CC BY-NC-SA 4.0")]
    [InlineData("Data: OpenF1", " ")]
    public void Constructor_OpenF1WithoutAttributionOrLicence_Throws(string? attribution, string? licence) =>
        Assert.Throws<ArgumentException>(() => new Provenance(DataSource.OpenF1, s_when, "b", attribution, licence));

    [Theory]
    [InlineData(DataSource.Unknown)]
    [InlineData((DataSource)42)]
    public void Constructor_UnknownSource_Throws(DataSource source) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Provenance(source, s_when, "b", seed: 1));

    [Fact]
    public void Constructor_NoBuilder_Throws() =>
        Assert.Throws<ArgumentException>(() => Provenance.ForSynthetic(1, s_when, " "));
}