using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

using RaceReplay.Bundle.Manifest;
using RaceReplay.TestKit;

namespace RaceReplay.Bundle.Tests;

/// <summary>Bundles are untrusted input: every corruption must surface as BundleFormatException (docs/04 §7.5).</summary>
public sealed class HardeningTests
{
    private static readonly ReplayBundle s_bundle = ReplayBundleWriter.Write(TestReplays.Small(cars: 2, samples: 41));

    // Entry layout: driver u16, channel u16, encoding u16, reserved u16, offset u32, length u32.
    private const int FirstEntry = 64;

    public static TheoryData<string, Action<byte[]>> PositionCorruptions => new()
    {
        { "wrong magic", p => p[0] = (byte)'X' },
        { "unknown kind", p => Write16(p, 8, 2) },
        { "absurd car count", p => Write16(p, 10, 41) },
        { "sample count differs from manifest", p => Write32(p, 12, 42) },
        { "sample count zero", p => Write32(p, 12, 0) },
        { "sample rate differs from manifest", p => BinaryPrimitives.WriteDoubleLittleEndian(p.AsSpan(16), 5) },
        { "zero scale", p => BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(24), 0) },
        { "NaN origin", p => BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(28), float.NaN) },
        { "unknown flags", p => Write32(p, 36, 1) },
        { "directory past the end", p => Write32(p, 40, 1_000_000) },
        { "directory inside the header", p => Write32(p, 40, 8) },
        { "absurd entry count", p => Write32(p, 44, 1_000) },
        { "block past the end", p => Write32(p, FirstEntry + 8, uint.MaxValue - 4) },
        { "block length wrong", p => Write32(p, FirstEntry + 12, 10) },
        { "driver not in manifest", p => Write16(p, FirstEntry, 99) },
        { "driver number out of range", p => Write16(p, FirstEntry, 60_000) },
        { "unknown channel", p => Write16(p, FirstEntry + 2, 7) },
        { "wrong encoding", p => Write16(p, FirstEntry + 4, 2) },
        { "duplicate channel", p => Write16(p, FirstEntry + 16 + 2, 1) }, // car 0's Y entry relabelled X
        { "missing channel", p => Write32(p, 44, 5) },                     // drop car 1's validity entry
    };

    public static TheoryData<string, Action<JsonNode>> ManifestCorruptions => new()
    {
        { "wrong format", m => m["format"] = "something-else" },
        { "unreadable schema version", m => m["schemaVersion"] = "one" },
        { "unknown data source", m => m["provenance"]!["source"] = "somewhere" },
        { "non-UTC timestamp", m => m["timeline"]!["t0Utc"] = "2026-03-02T13:59:00+01:00" },
        { "missing required property", m => m["session"]!.AsObject().Remove("circuit") },
        { "null where a value is required", m => m["session"]!["name"] = null },
        { "driver index out of order", m => m["drivers"]![0]!["index"] = 1 },
        { "invalid colour", m => m["drivers"]![0]!["colour"] = "red" },
        { "driver number out of range", m => m["drivers"]![0]!["number"] = 5_000 },
        { "duplicate driver", m => m["drivers"]![1]!["number"] = m["drivers"]![0]!["number"]!.GetValue<int>() },
        { "null driver entry", m => m["drivers"]![0] = null },
        { "centreline point of three", m => m["track"]!["centreline"]![0] = new JsonArray(1, 2, 3) },
        { "null centreline point", m => m["track"]!["centreline"]![0] = null },
        { "centreline too short", m => m["track"]!["centreline"] = new JsonArray(new JsonArray(0, 0), new JsonArray(1, 1)) },
        { "unknown segment kind", m => m["timeline"]!["segments"]![0]!["kind"] = "lunch" },
        { "null segment", m => m["timeline"]!["segments"]![0] = null },
        { "overlapping segments", m => m["timeline"]!["segments"]![1]!["start"] = 0 },
        { "race start past the end", m => m["timeline"]!["raceStart"] = 1_000 },
        { "absurd sample count", m => m["timeline"]!["sampleCount"] = 10_000_000 },
        { "positions size differs", m => m["channels"]!["positions"]!["bytes"] = 1 },
        { "synthetic key with real-data provenance", m => m["provenance"]!["source"] = "openf1" },
        { "seed differs from key", m => m["provenance"]!["seed"] = 8 },
        { "number too large for int", m => m["timeline"]!["sampleCount"] = 1e12 },
    };

    [Theory, MemberData(nameof(PositionCorruptions))]
    public void Read_CorruptPositions_ThrowsBundleFormatException(string corruption, Action<byte[]> corrupt)
    {
        byte[] positions = s_bundle.Positions.ToArray();
        corrupt(positions);

        BundleFormatException error = Assert.Throws<BundleFormatException>(
            () => ReplayBundleReader.Read(s_bundle.ManifestJson.Span, positions));

        Assert.False(error.IsNewerVersion, corruption);
    }

    [Theory, MemberData(nameof(ManifestCorruptions))]
    public void Read_CorruptManifest_ThrowsBundleFormatException(string corruption, Action<JsonNode> corrupt)
    {
        JsonNode manifest = JsonNode.Parse(s_bundle.ManifestJson.Span)!;
        corrupt(manifest);
        byte[] json = Encoding.UTF8.GetBytes(manifest.ToJsonString());

        Exception? error = Record.Exception(() => ReplayBundleReader.Read(json, s_bundle.Positions.Span));

        Assert.True(error is BundleFormatException, $"{corruption}: {error?.GetType().Name ?? "no exception"}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"format\":\"race-replay-bundle\"}")]
    public void ReadManifest_NotAManifest_ThrowsBundleFormatException(string json) =>
        Assert.Throws<BundleFormatException>(() => ReplayBundleReader.ReadManifest(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Read_NewerBinaryMajorVersion_SaysTheAppIsTooOld()
    {
        byte[] positions = s_bundle.Positions.ToArray();
        Write16(positions, 4, 2);

        BundleFormatException error = Assert.Throws<BundleFormatException>(
            () => ReplayBundleReader.Read(s_bundle.ManifestJson.Span, positions));

        Assert.True(error.IsNewerVersion);
    }

    [Fact]
    public void ReadManifest_NewerSchemaMajorVersion_SaysTheAppIsTooOld()
    {
        JsonNode manifest = JsonNode.Parse(s_bundle.ManifestJson.Span)!;
        manifest["schemaVersion"] = "2.0";

        BundleFormatException error = Assert.Throws<BundleFormatException>(
            () => ReplayBundleReader.ReadManifest(Encoding.UTF8.GetBytes(manifest.ToJsonString())));

        Assert.True(error.IsNewerVersion);
    }

    [Fact]
    public void ReadManifest_NewerSchemaMinorVersion_IsAccepted()
    {
        JsonNode manifest = JsonNode.Parse(s_bundle.ManifestJson.Span)!;
        manifest["schemaVersion"] = "1.7";
        manifest["somethingNew"] = "ignored by older readers";

        BundleManifestDto read = ReplayBundleReader.ReadManifest(Encoding.UTF8.GetBytes(manifest.ToJsonString()));

        Assert.Equal("1.7", read.SchemaVersion);
    }

    [Fact]
    public void Read_RandomlyMutatedBundles_OnlyEverThrowBundleFormatException()
    {
        // Seeded, so a failure is reproducible. Mutations: flip a byte, overwrite a byte, or truncate.
        Random random = new(20261015);
        for (int i = 0; i < 2_000; i++)
        {
            bool mutateManifest = random.Next(2) == 0;
            byte[] manifest = s_bundle.ManifestJson.ToArray();
            byte[] positions = s_bundle.Positions.ToArray();
            byte[] target = mutateManifest ? manifest : positions;
            int at = random.Next(target.Length);
            switch (random.Next(3))
            {
                case 0: target[at] ^= (byte)(1 << random.Next(8)); break;
                case 1: target[at] = (byte)random.Next(256); break;
                default: target = target[..at]; break;
            }

            if (mutateManifest)
            {
                manifest = target;
            }
            else
            {
                positions = target;
            }

            Exception? error = Record.Exception(() => ReplayBundleReader.Read(manifest, positions));
            Assert.True(error is null or BundleFormatException, $"mutation {i}: {error?.GetType().Name}: {error?.Message}");
        }
    }

    private static void Write16(byte[] p, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(offset), (ushort)value);

    private static void Write32(byte[] p, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(offset), value);
}