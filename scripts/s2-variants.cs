#!/usr/bin/env dotnet
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false
#:property TreatWarningsAsErrors=false
#:project ../src/RaceReplay.Application/RaceReplay.Application.csproj
#:project ../src/RaceReplay.Bundle/RaceReplay.Bundle.csproj
#:project ../src/RaceReplay.Synthetic/RaceReplay.Synthetic.csproj

// Spike S2 (RR-021, docs/08 §3): test bundles, compression variants and packages for the packaging and hosting
// decisions. Throwaway prototype code: the delta encodings here are candidates, not the format (docs/04 §7.3).
//
// Everything is synthetic seed 1, built through the real builder and writer, with a stand-in progress channel until
// M2 derives the real one. Output goes to artifacts/s2/ (gitignored, never committed). The build time is fixed, so
// two runs write byte-identical files and print identical numbers.
//
// Usage (from any directory; output always goes to the repository's artifacts/s2/):
//   dotnet run scripts/s2-variants.cs            build everything and print the size tables

using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

using RaceReplay.Analysis;
using RaceReplay.Application;
using RaceReplay.Bundle;
using RaceReplay.Domain;
using RaceReplay.Synthetic;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

const int Seed = 1;
const long SixMegabytes = 6_000_000;
const long TenMegabytes = 10_000_000;
int twoHourSamples = (int)(2 * 3600 * Built.TimelineHz);

// Anchored to the repository root, wherever the script is run from: it deletes this folder first.
string outRoot = Path.Combine(Path.GetDirectoryName(ScriptDirectory())!, "artifacts", "s2");
if (Directory.Exists(outRoot))
{
    Directory.Delete(outRoot, recursive: true);
}

Directory.CreateDirectory(outRoot);

// Lap counts for a target length: two short runs give samples per lap and the fixed overhead (grid, cool-down,
// finishing spread), then the target is solved for. Past the generator's 200-lap limit, a longer cool-down fills in:
// cars keep circulating, so the data stays realistic rather than a parked car that compresses to nothing.
LengthModel model = LengthModel.Fit(Seed);
Console.WriteLine($"Seed {Seed}: {model.SamplesPerLap:F1} samples per lap, {model.OverheadSamples:F0} samples of overhead.");
Console.WriteLine();

Built twoHour = Built.Make(Seed, model.For(twoHourSamples), "2-hour");
Built sixMb = Built.Make(Seed, model.For(Built.SamplesForBytes(SixMegabytes)), "6 MB");
Built tenMb = Built.Make(Seed, model.For(Built.SamplesForBytes(TenMegabytes)), "10 MB");

// The 2-hour bundle in the directory layout, as the site would serve it.
string bundleDir = Path.Combine(outRoot, "bundle-2h");
Directory.CreateDirectory(bundleDir);
File.WriteAllBytes(Path.Combine(bundleDir, BundleFiles.Manifest), twoHour.Bundle.ManifestJson.ToArray());
File.WriteAllBytes(Path.Combine(bundleDir, BundleFiles.Positions), twoHour.Bundle.Positions.ToArray());

Console.WriteLine("| Build | Laps | Cool-down s | Samples | Duration | manifest.json | positions.rrb |");
Console.WriteLine("|---|---:|---:|---:|---|---:|---:|");
foreach (Built b in new[] { twoHour, sixMb, tenMb })
{
    Console.WriteLine(
        $"| {b.Name} | {b.Length.Laps} | {b.Length.CoolDownSeconds:F0} | {b.Samples:N0} | {TimeSpan.FromSeconds((b.Samples - 1) / Built.TimelineHz):h\\:mm\\:ss} " +
        $"| {b.Bundle.ManifestJson.Length:N0} | {b.Bundle.Positions.Length:N0} |");
}

Console.WriteLine();

// Variants of the 2-hour positions file, each checked against the raw file it came from.
byte[] raw = twoHour.Bundle.Positions.ToArray();
(string Name, byte[] Bytes)[] variants =
[
    ("raw", raw),
    ("delta", Rrb.Encode(raw, deltaXY: true, quantiseProgress: false)),
    ("delta-qprogress", Rrb.Encode(raw, deltaXY: true, quantiseProgress: true)),
];

string variantDir = Path.Combine(outRoot, "variants");
Directory.CreateDirectory(variantDir);
foreach ((string name, byte[] bytes) in variants)
{
    Rrb.CheckRoundTrip(raw, bytes, name);
    File.WriteAllBytes(Path.Combine(variantDir, $"positions.{name}.rrb"), bytes);
    File.WriteAllBytes(Path.Combine(variantDir, $"positions.{name}.rrb.gz"), Compress.Gzip(bytes, 9));
}

Console.WriteLine("All variants decode to the raw file (X and Y exactly, valid progress within half a track unit).");
Console.WriteLine();
Console.WriteLine("Whole file, bytes (gzip and deflate by .NET's zlib-ng; Brotli quality 11, window 22):");
Console.WriteLine();
Console.WriteLine("| Variant | Raw | gzip 6 | gzip 9 | zip deflate (Optimal) | Brotli 11 |");
Console.WriteLine("|---|---:|---:|---:|---:|---:|");
foreach ((string name, byte[] bytes) in variants)
{
    Console.WriteLine(
        $"| {name} | {bytes.Length:N0} | {Compress.Gzip(bytes, 6).Length:N0} | {Compress.Gzip(bytes, 9).Length:N0} " +
        $"| {Compress.ZipDeflated(bytes).Length:N0} | {Compress.Brotli(bytes).Length:N0} |");
}

Console.WriteLine();
Console.WriteLine("Per channel, all cars' blocks of that channel concatenated in file order, bytes:");
Console.WriteLine();
Console.WriteLine("| Variant | Channel | Raw | gzip 9 | Brotli 11 |");
Console.WriteLine("|---|---|---:|---:|---:|");
foreach ((string name, byte[] bytes) in variants)
{
    foreach ((string channel, byte[] blocks) in Rrb.ByChannel(bytes))
    {
        Console.WriteLine($"| {name} | {channel} | {blocks.Length:N0} | {Compress.Gzip(blocks, 9).Length:N0} | {Compress.Brotli(blocks).Length:N0} |");
    }
}

Console.WriteLine();

// Packages (docs/04 §7.1), stored and deflated. The CLI doesn't write .rrz until RR-051 (#29).
string packageDir = Path.Combine(outRoot, "packages");
Directory.CreateDirectory(packageDir);
Console.WriteLine("| Package | Stored | Deflated (Optimal) | Saving |");
Console.WriteLine("|---|---:|---:|---:|");
foreach ((Built b, string file) in new[] { (sixMb, "s2-6mb"), (tenMb, "s2-10mb") })
{
    byte[] stored = Package.Write(b.Bundle, CompressionLevel.NoCompression);
    byte[] deflated = Package.Write(b.Bundle, CompressionLevel.Optimal);
    Package.Check(stored, b.Bundle);
    Package.Check(deflated, b.Bundle);
    File.WriteAllBytes(Path.Combine(packageDir, $"{file}.stored.rrz"), stored);
    File.WriteAllBytes(Path.Combine(packageDir, $"{file}.deflated.rrz"), deflated);
    Console.WriteLine($"| {b.Name} | {stored.Length:N0} | {deflated.Length:N0} | {1 - (deflated.Length / (double)stored.Length):P1} |");
}

Console.WriteLine();
Console.WriteLine("SHA-256 of every file written:");
foreach (string path in Directory.EnumerateFiles(outRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    Console.WriteLine($"{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))}  {Path.GetRelativePath(outRoot, path).Replace('\\', '/')}");
}

// The compiler fills in this source file's path, so output lands in the repository whatever the working directory.
static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

/// <summary>Laps and cool-down for one build.</summary>
internal sealed record RaceLength(int Laps, double CoolDownSeconds);

/// <summary>Timeline samples as a linear function of laps, fitted on two short runs of one seed.</summary>
internal sealed record LengthModel(double SamplesPerLap, double OverheadSamples)
{
    private const int MaxLaps = 200;
    private const double DefaultCoolDownSeconds = 60;

    public static LengthModel Fit(int seed)
    {
        int a = Built.Replay(seed, new RaceLength(10, DefaultCoolDownSeconds)).Timeline.SampleCount;
        int b = Built.Replay(seed, new RaceLength(30, DefaultCoolDownSeconds)).Timeline.SampleCount;
        double perLap = (b - a) / 20.0;
        return new LengthModel(perLap, a - (10 * perLap));
    }

    public RaceLength For(int targetSamples)
    {
        int laps = (int)Math.Round((targetSamples - OverheadSamples) / SamplesPerLap);
        if (laps <= MaxLaps)
        {
            return new RaceLength(laps, DefaultCoolDownSeconds);
        }

        double missing = targetSamples - (OverheadSamples + (MaxLaps * SamplesPerLap));
        return new RaceLength(MaxLaps, Math.Min(3_600, DefaultCoolDownSeconds + Math.Round(missing / Built.TimelineHz)));
    }
}

/// <summary>One synthetic race, built and written as a bundle with a stand-in progress channel.</summary>
internal sealed record Built(string Name, RaceLength Length, int Samples, ReplayBundle Bundle)
{
    // Every build records the same time, so its bytes are a pure function of seed and length.
    private static readonly TimeProvider s_fixedTime = new FixedTime(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero));

    /// <summary>The timeline rate the builder resamples onto.</summary>
    public static readonly double TimelineHz = new ResamplingOptions().TimelineHz;

    /// <summary>Positions bytes per sample for the whole field: X and Y (2 B each), progress (4 B), validity (1 bit).</summary>
    public static int SamplesForBytes(long bytes) => (int)(bytes / (SyntheticRaceOptions.Default.DriverCount * (2 + 2 + 4 + 0.125)));

    public static Built Make(int seed, RaceLength length, string name)
    {
        ReplaySession replay = WithStandInProgress(Replay(seed, length));
        return new Built(name, length, replay.Timeline.SampleCount, ReplayBundleWriter.Write(replay));
    }

    /// <summary>The built replay, without progress and unwritten: all the lap-count fit needs.</summary>
    public static ReplaySession Replay(int seed, RaceLength length)
    {
        SyntheticRaceOptions options = SyntheticRaceOptions.Default with { Laps = length.Laps, CoolDownSeconds = length.CoolDownSeconds };
        RawSessionData raw = new SyntheticRaceGenerator(options).Generate(seed).Raw;
        ReplayBuilder builder = new(
            new CentrelineBuilder(new CentrelineOptions()), new TimelineResampler(new ResamplingOptions()), s_fixedTime);
        return builder.Build(raw, new BuildOptions("s2-variants")).Replay;
    }

    // Stand-in for M2's progress: each car's cumulative distance travelled along its own X/Y, in track units.
    // Monotonic and smooth like the real channel, so it compresses like it. Invalid samples hold the last value.
    private static ReplaySession WithStandInProgress(ReplaySession r)
    {
        CarChannels[] cars = [.. r.Cars.Select(car =>
        {
            ReadOnlySpan<float> x = car.X.Span, y = car.Y.Span;
            float[] progress = new float[car.SampleCount];
            double distance = 0;
            int last = -1;
            for (int k = 0; k < progress.Length; k++)
            {
                if (car.Valid[k])
                {
                    if (last >= 0)
                    {
                        distance += Math.Sqrt(Math.Pow(x[k] - x[last], 2) + Math.Pow(y[k] - y[last], 2));
                    }

                    last = k;
                }

                progress[k] = (float)distance;
            }

            return new CarChannels(car.Driver, x, y, car.Valid, progress);
        })];

        return new ReplaySession(r.Session, r.Drivers, r.Track, r.Timeline, cars, r.Gaps, r.Events, r.Provenance);
    }
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Candidate encodings on the published <c>.rrb</c> layout (docs/04 §7.3). The layout is restated here because the
/// Bundle project keeps it internal, so every parse first checks the magic, version and kind it was written for.
/// </summary>
internal static class Rrb
{
    private const int HeaderSize = 64;
    private const int MajorOffset = 4;
    private const int KindOffset = 8;
    private const int FlagsOffset = 36;
    private const int DirectoryOffsetOffset = 40;
    private const int EntryCountOffset = 44;
    private const int EntrySize = 16;
    private const int EntryEncodingOffset = 4;

    private const ushort SupportedMajor = 1;
    private const ushort KindPositions = 1;
    private const int FlagDeltaInt16 = 1;          // flag bit 0, reserved for delta encoding (docs/04 §7.3)
    private const ushort ChannelProgress = 3;
    private const ushort ChannelValidity = 4;
    private const ushort EncodingInt16 = 1;
    private const ushort EncodingFloat32 = 2;
    private const ushort EncodingInt32UnitsDelta = 4; // candidate: whole track units, delta-encoded

    private static readonly Dictionary<ushort, string> s_channelNames = new() { [1] = "X", [2] = "Y", [3] = "progress", [4] = "validity" };

    private readonly record struct Entry(int Index, ushort Driver, ushort Channel, ushort Encoding, int Offset, int Length);

    /// <summary>
    /// X and Y as differences from the previous sample (the first sample as is), wrapping in 16 bits; progress
    /// rounded to whole track units and stored as 32-bit differences. Invalid or non-finite progress samples hold the
    /// last valid value, as the writer already does for X and Y, so they cost nothing and never reach the cast.
    /// </summary>
    public static byte[] Encode(byte[] raw, bool deltaXY, bool quantiseProgress)
    {
        byte[] file = (byte[])raw.Clone();
        if (deltaXY)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(FlagsOffset), FlagDeltaInt16);
        }

        Entry[] entries = Entries(raw);
        foreach (Entry e in entries)
        {
            Span<byte> block = file.AsSpan(e.Offset, e.Length);
            if (deltaXY && e.Encoding == EncodingInt16)
            {
                short previous = 0;
                for (int k = 0; k < e.Length / 2; k++)
                {
                    short value = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(e.Offset + (k * 2)));
                    BinaryPrimitives.WriteInt16LittleEndian(block[(k * 2)..], unchecked((short)(value - previous)));
                    previous = value;
                }
            }
            else if (quantiseProgress && e.Encoding == EncodingFloat32 && e.Channel == ChannelProgress)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(EncodingAt(raw, e)), EncodingInt32UnitsDelta);
                ReadOnlySpan<byte> valid = Validity(raw, entries, e.Driver);
                int previous = 0;
                for (int k = 0; k < e.Length / 4; k++)
                {
                    float value = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(e.Offset + (k * 4)));
                    int units = IsValid(valid, k) && float.IsFinite(value)
                        ? checked((int)Math.Round(value, MidpointRounding.ToEven))
                        : previous;
                    BinaryPrimitives.WriteInt32LittleEndian(block[(k * 4)..], units - previous);
                    previous = units;
                }
            }
        }

        return file;
    }

    /// <summary>Decodes <paramref name="variant"/> and checks it against <paramref name="raw"/>; throws on any difference.</summary>
    public static void CheckRoundTrip(byte[] raw, byte[] variant, string name)
    {
        if (variant.Length != raw.Length)
        {
            throw new InvalidOperationException($"{name}: length {variant.Length} differs from raw {raw.Length}.");
        }

        bool delta = (BinaryPrimitives.ReadUInt32LittleEndian(variant.AsSpan(FlagsOffset)) & FlagDeltaInt16) != 0;
        Entry[] variantEntries = Entries(variant);
        bool[] inBlock = new bool[raw.Length];
        foreach (Entry e in variantEntries)
        {
            Array.Fill(inBlock, true, e.Offset, e.Length);
            ReadOnlySpan<byte> got = variant.AsSpan(e.Offset, e.Length);
            ReadOnlySpan<byte> want = raw.AsSpan(e.Offset, e.Length);
            if (delta && e.Encoding == EncodingInt16)
            {
                short value = 0;
                for (int k = 0; k < e.Length / 2; k++)
                {
                    value = unchecked((short)(value + BinaryPrimitives.ReadInt16LittleEndian(got[(k * 2)..])));
                    if (value != BinaryPrimitives.ReadInt16LittleEndian(want[(k * 2)..]))
                    {
                        throw new InvalidOperationException($"{name}: channel {e.Channel} sample {k} doesn't round-trip.");
                    }
                }
            }
            else if (e.Encoding == EncodingInt32UnitsDelta)
            {
                // Invalid samples carry no meaning (docs/03 §7.2), so only valid ones must round-trip.
                ReadOnlySpan<byte> valid = Validity(variant, variantEntries, e.Driver);
                int units = 0;
                for (int k = 0; k < e.Length / 4; k++)
                {
                    units += BinaryPrimitives.ReadInt32LittleEndian(got[(k * 4)..]);
                    float expected = BinaryPrimitives.ReadSingleLittleEndian(want[(k * 4)..]);
                    if (IsValid(valid, k) && Math.Abs(units - expected) > 0.5)
                    {
                        throw new InvalidOperationException($"{name}: progress sample {k} is {units}, expected {expected}.");
                    }
                }
            }
            else if (!got.SequenceEqual(want))
            {
                throw new InvalidOperationException($"{name}: channel {e.Channel} at {e.Offset} differs from raw.");
            }
        }

        // Outside the blocks (header, directory and padding), only the flags and the changed encodings may differ.
        HashSet<int> allowed = [.. Enumerable.Range(FlagsOffset, 4)];
        foreach (Entry e in variantEntries)
        {
            allowed.Add(EncodingAt(raw, e));
            allowed.Add(EncodingAt(raw, e) + 1);
        }

        for (int i = 0; i < raw.Length; i++)
        {
            if (!inBlock[i] && !allowed.Contains(i) && variant[i] != raw[i])
            {
                throw new InvalidOperationException($"{name}: byte {i}, outside the channel blocks, differs from raw.");
            }
        }
    }

    /// <summary>All cars' blocks of each channel, concatenated in file order.</summary>
    public static IEnumerable<(string Channel, byte[] Blocks)> ByChannel(byte[] file) =>
        Entries(file)
            .GroupBy(e => e.Channel)
            .OrderBy(g => g.Key)
            .Select(g => (s_channelNames[g.Key], g.SelectMany(e => file.AsSpan(e.Offset, e.Length).ToArray()).ToArray()));

    private static int DirectoryOffset(byte[] file) => (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(DirectoryOffsetOffset));

    private static int EncodingAt(byte[] file, Entry e) => DirectoryOffset(file) + (e.Index * EntrySize) + EntryEncodingOffset;

    private static ReadOnlySpan<byte> Validity(byte[] file, Entry[] entries, ushort driver)
    {
        Entry v = entries.Single(x => x.Driver == driver && x.Channel == ChannelValidity);
        return file.AsSpan(v.Offset, v.Length);
    }

    private static bool IsValid(ReadOnlySpan<byte> bitmap, int k) => (bitmap[k >> 3] & (1 << (k & 7))) != 0;

    private static Entry[] Entries(byte[] file)
    {
        if (file.Length < HeaderSize || !file.AsSpan(0, 4).SequenceEqual("RRB1"u8)
            || BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(MajorOffset)) != SupportedMajor
            || BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(KindOffset)) != KindPositions)
        {
            throw new InvalidOperationException("Not a schema 1.x positions file: the prototype's layout no longer matches the format.");
        }

        int directory = DirectoryOffset(file);
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(EntryCountOffset));
        return [.. Enumerable.Range(0, count).Select(i =>
        {
            ReadOnlySpan<byte> d = file.AsSpan(directory + (i * EntrySize), EntrySize);
            return new Entry(
                i,
                BinaryPrimitives.ReadUInt16LittleEndian(d),
                BinaryPrimitives.ReadUInt16LittleEndian(d[2..]),
                BinaryPrimitives.ReadUInt16LittleEndian(d[EntryEncodingOffset..]),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(d[8..]),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(d[12..]));
        })];
    }
}

/// <summary>
/// Compressors, memoised by content: the tables ask for the same buffers more than once (a variant's gzip 9 for its
/// file and its row; channels that two variants share), and Brotli 11 is slow.
/// </summary>
internal static class Compress
{
    private static readonly Dictionary<string, byte[]> s_done = [];

    public static byte[] Gzip(byte[] data, int level) => Memo($"gzip{level}", data, () =>
    {
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, new ZLibCompressionOptions { CompressionLevel = level }))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    });

    public static byte[] ZipDeflated(byte[] data)
    {
        using MemoryStream output = new();
        using (DeflateStream deflate = new(output, CompressionLevel.Optimal))
        {
            deflate.Write(data);
        }

        return output.ToArray();
    }

    public static byte[] Brotli(byte[] data) => Memo("br11", data, () =>
    {
        byte[] output = new byte[BrotliEncoder.GetMaxCompressedLength(data.Length)];
        if (!BrotliEncoder.TryCompress(data, output, out int written, quality: 11, window: 22))
        {
            throw new InvalidOperationException("Brotli compression failed.");
        }

        return output[..written];
    });

    private static byte[] Memo(string method, byte[] data, Func<byte[]> compress)
    {
        string key = $"{method}:{Convert.ToHexStringLower(SHA256.HashData(data))}";
        if (!s_done.TryGetValue(key, out byte[]? result))
        {
            result = compress();
            s_done[key] = result;
        }

        return result;
    }
}

/// <summary>A <c>.rrz</c> as docs/04 §7.1 describes it, written deterministically: fixed entry order and timestamps.</summary>
internal static class Package
{
    private static readonly DateTimeOffset s_entryTime = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);

    private static readonly byte[] s_notice = Encoding.UTF8.GetBytes(
        "Synthetic demo: fictional data generated by Race Replay (seed 1). Not a real race.\n");

    public static byte[] Write(ReplayBundle bundle, CompressionLevel level)
    {
        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, BundleFiles.Manifest, bundle.ManifestJson.Span, level);
            Add(zip, BundleFiles.Positions, bundle.Positions.Span, level);
            Add(zip, "DATA-NOTICE.txt", s_notice, level);
        }

        return output.ToArray();
    }

    /// <summary>Reads the package back the way the browser will, and checks it gives the same replay.</summary>
    public static void Check(byte[] package, ReplayBundle bundle)
    {
        using ZipArchive zip = new(new MemoryStream(package), ZipArchiveMode.Read);
        byte[] manifest = ReadEntry(zip, BundleFiles.Manifest);
        byte[] positions = ReadEntry(zip, BundleFiles.Positions);
        if (!manifest.AsSpan().SequenceEqual(bundle.ManifestJson.Span) || !positions.AsSpan().SequenceEqual(bundle.Positions.Span))
        {
            throw new InvalidOperationException("A package entry doesn't match the bundle.");
        }

        _ = ReplayBundleReader.Read(manifest, positions);
    }

    private static void Add(ZipArchive zip, string name, ReadOnlySpan<byte> content, CompressionLevel level)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, level);
        entry.LastWriteTime = s_entryTime;
        using Stream stream = entry.Open();
        stream.Write(content);
    }

    private static byte[] ReadEntry(ZipArchive zip, string name)
    {
        ZipArchiveEntry entry = zip.GetEntry(name) ?? throw new InvalidOperationException($"No {name} in the package.");
        using Stream stream = entry.Open();
        using MemoryStream buffer = new((int)entry.Length);
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
