using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

using RaceReplay.Domain;
using RaceReplay.TestKit;

namespace RaceReplay.Bundle.Tests;

public sealed class RoundTripTests
{
    [Fact]
    public void Read_WrittenBundle_PreservesMetadataExactly()
    {
        ReplaySession original = TestReplays.Small();

        ReplaySession read = RoundTrip(original);

        Assert.Equal(original.Session, read.Session);
        Assert.Equal(original.Drivers, read.Drivers);
        Assert.Equal(original.Provenance, read.Provenance);
        Assert.Equal(original.Timeline.T0Utc, read.Timeline.T0Utc);
        Assert.Equal(original.Timeline.SampleRateHz, read.Timeline.SampleRateHz);
        Assert.Equal(original.Timeline.SampleCount, read.Timeline.SampleCount);
        Assert.Equal(original.Timeline.RaceStart, read.Timeline.RaceStart);
        Assert.Equal(original.Timeline.Segments, read.Timeline.Segments);
        Assert.Equal(original.Track.Centreline.ToArray(), read.Track.Centreline.ToArray());
        Assert.Equal(original.Track.UnitsPerMetre, read.Track.UnitsPerMetre);
        Assert.Equal(original.Track.DisplayRotationDegrees, read.Track.DisplayRotationDegrees);
        Assert.True(read.Gaps.IsEmpty);
        Assert.Empty(read.Events);
    }

    [Fact]
    public void Read_WrittenBundle_PositionsWithinHalfAQuantisationStep()
    {
        ReplaySession original = TestReplays.Small();
        ReplayBundle bundle = ReplayBundleWriter.Write(original);
        float scale = BinaryPrimitives.ReadSingleLittleEndian(bundle.Positions.Span[24..]);

        ReplaySession read = ReplayBundleReader.Read(bundle.ManifestJson.Span, bundle.Positions.Span);

        // Half a step, plus float rounding when the dequantised value is stored back as a float.
        double tolerance = (scale / 2) + 1e-3;
        for (int c = 0; c < original.Cars.Count; c++)
        {
            CarChannels expected = original.Cars[c], actual = read.Cars[c];
            for (int k = 0; k < expected.SampleCount; k++)
            {
                if (expected.Valid[k])
                {
                    Assert.Equal(expected.X.Span[k], actual.X.Span[k], tolerance);
                    Assert.Equal(expected.Y.Span[k], actual.Y.Span[k], tolerance);
                }
            }
        }

        Assert.InRange(scale, 0.1, 0.2); // the 10,000-unit circle spans 65,000 steps: ≈ 0.15 units (1.5 cm) each
    }

    [Fact]
    public void Read_WrittenBundle_ValidityIsExact()
    {
        ReplaySession original = TestReplays.Small();

        ReplaySession read = RoundTrip(original);

        for (int c = 0; c < original.Cars.Count; c++)
        {
            for (int k = 0; k < original.Timeline.SampleCount; k++)
            {
                Assert.Equal(original.Cars[c].Valid[k], read.Cars[c].Valid[k]);
            }
        }
    }

    [Fact]
    public void Read_WrittenBundleWithProgress_ProgressIsExact()
    {
        ReplaySession original = TestReplays.Small(withProgress: true);

        ReplaySession read = RoundTrip(original);

        Assert.All(read.Cars, car => Assert.True(car.HasProgress));
        for (int c = 0; c < original.Cars.Count; c++)
        {
            Assert.Equal(original.Cars[c].Progress.ToArray(), read.Cars[c].Progress.ToArray());
        }
    }

    [Fact]
    public void Read_WrittenBundleWithoutProgress_HasNoProgress() =>
        Assert.All(RoundTrip(TestReplays.Small()).Cars, car => Assert.False(car.HasProgress));

    [Fact]
    public void Read_NoValidSamples_RoundTripsWithUnitScale()
    {
        ReplayBundle bundle = ReplayBundleWriter.Write(TestReplays.Small(allInvalid: true));

        ReplaySession read = ReplayBundleReader.Read(bundle.ManifestJson.Span, bundle.Positions.Span);

        Assert.Equal(1f, BinaryPrimitives.ReadSingleLittleEndian(bundle.Positions.Span[24..]));
        Assert.All(read.Cars, car => Assert.Equal(0, car.Valid.CountValid()));
    }

    [Fact]
    public void Read_SingleSampleTimeline_RoundTrips()
    {
        ReplaySession read = RoundTrip(TestReplays.Small(cars: 1, samples: 1));

        Assert.Equal(1, read.Timeline.SampleCount);
    }

    [Fact]
    public void Write_SameReplayTwice_IsByteIdentical()
    {
        ReplaySession replay = TestReplays.Small(withProgress: true);

        ReplayBundle a = ReplayBundleWriter.Write(replay);
        ReplayBundle b = ReplayBundleWriter.Write(replay);

        Assert.Equal(a.ManifestJson.ToArray(), b.ManifestJson.ToArray());
        Assert.Equal(a.Positions.ToArray(), b.Positions.ToArray());
    }

    [Fact]
    public void Write_InvalidSamples_RepeatTheLastValidValue()
    {
        ReplaySession replay = TestReplays.Small(cars: 1);
        ReadOnlySpan<byte> positions = ReplayBundleWriter.Write(replay).Positions.Span;
        int xOffset = BinaryPrimitives.ReadInt32LittleEndian(positions[(64 + 8)..]); // first entry: car 0, X

        for (int k = 1; k < replay.Timeline.SampleCount; k++)
        {
            if (!replay.Cars[0].Valid[k])
            {
                Assert.Equal(
                    BinaryPrimitives.ReadInt16LittleEndian(positions[(xOffset + ((k - 1) * 2))..]),
                    BinaryPrimitives.ReadInt16LittleEndian(positions[(xOffset + (k * 2))..]));
            }
        }
    }

    [Fact]
    public void Write_Header_FollowsTheDocumentedLayout()
    {
        ReplaySession replay = TestReplays.Small(cars: 3, samples: 101);

        ReadOnlySpan<byte> p = ReplayBundleWriter.Write(replay).Positions.Span;

        Assert.True(p[..4].SequenceEqual("RRB1"u8));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(p[4..]));      // major
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(p[6..]));      // minor
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(p[8..]));      // kind: positions
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(p[10..]));     // cars
        Assert.Equal(101u, BinaryPrimitives.ReadUInt32LittleEndian(p[12..]));  // samples
        Assert.Equal(TestReplays.RateHz, BinaryPrimitives.ReadDoubleLittleEndian(p[16..]));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(p[36..]));    // flags
        Assert.Equal(64u, BinaryPrimitives.ReadUInt32LittleEndian(p[40..]));   // directory offset
        Assert.Equal(9u, BinaryPrimitives.ReadUInt32LittleEndian(p[44..]));    // X, Y, validity per car
        Assert.True(p[48..64].IndexOfAnyExcept((byte)0) < 0);                  // reserved
    }

    [Fact]
    public void Write_Blocks_AreAlignedAndDoNotOverlap()
    {
        ReadOnlySpan<byte> p = ReplayBundleWriter.Write(TestReplays.Small(withProgress: true)).Positions.Span;
        int entries = (int)BinaryPrimitives.ReadUInt32LittleEndian(p[44..]);
        int previousEnd = 64 + (entries * 16);

        for (int i = 0; i < entries; i++)
        {
            ReadOnlySpan<byte> entry = p[(64 + (i * 16))..];
            int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            Assert.Equal(0, offset % 8);
            Assert.True(offset >= previousEnd);
            previousEnd = offset + length;
        }

        Assert.True(previousEnd <= p.Length);
    }

    [Fact]
    public void Write_Manifest_DescribesTheBundle()
    {
        ReplayBundle bundle = ReplayBundleWriter.Write(TestReplays.Small());

        using JsonDocument json = JsonDocument.Parse(bundle.ManifestJson);
        JsonElement root = json.RootElement;

        Assert.Equal("race-replay-bundle", root.GetProperty("format").GetString());
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("synthetic", root.GetProperty("provenance").GetProperty("source").GetString());
        Assert.Equal(7, root.GetProperty("provenance").GetProperty("seed").GetInt32());
        Assert.EndsWith("Z", root.GetProperty("timeline").GetProperty("t0Utc").GetString(), StringComparison.Ordinal);
        Assert.Equal("preRace", root.GetProperty("timeline").GetProperty("segments")[0].GetProperty("kind").GetString());
        Assert.Equal("3671C6".Length, root.GetProperty("drivers")[0].GetProperty("colour").GetString()!.Length);

        JsonElement positions = root.GetProperty("channels").GetProperty("positions");
        Assert.Equal(BundleFiles.Positions, positions.GetProperty("file").GetString());
        Assert.Equal(bundle.Positions.Length, positions.GetProperty("bytes").GetInt64());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bundle.Positions.Span)), positions.GetProperty("sha256").GetString());
    }

    [Fact]
    public void Write_OpenF1Replay_CarriesAttributionAndLicence()
    {
        ReplaySession synthetic = TestReplays.Small(cars: 1);
        ReplaySession real = new(
            new SessionInfo(new SessionKey(9839), 2025, "Race", "Race", "Abu Dhabi Grand Prix", "Yas Marina", TestReplays.T0),
            synthetic.Drivers, synthetic.Track, synthetic.Timeline, synthetic.Cars, GapSeries.Empty, [],
            Provenance.ForOpenF1(TestReplays.T0, "race-replay test"));

        ReplaySession read = RoundTrip(real);

        Assert.Equal(DataSource.OpenF1, read.Provenance.Source);
        Assert.Equal(Provenance.OpenF1Licence, read.Provenance.Licence);
        Assert.Equal(Provenance.OpenF1Attribution, read.Provenance.Attribution);
    }

    private static ReplaySession RoundTrip(ReplaySession replay)
    {
        ReplayBundle bundle = ReplayBundleWriter.Write(replay);
        return ReplayBundleReader.Read(bundle.ManifestJson.Span, bundle.Positions.Span);
    }
}