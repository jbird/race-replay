using System.Buffers.Binary;

using RaceReplay.Domain;

namespace RaceReplay.Bundle.Rrb;

/// <summary>One car's channels as read from a positions file.</summary>
internal sealed record PositionChannels(float[] X, float[] Y, ValidityMask Valid, float[] Progress);

/// <summary>
/// <c>positions.rrb</c>: per car, X and Y as Int16 quantised around a shared origin, the validity bitmap, and
/// progress as Float32 once it exists (docs/04 §7.3). The channel directory makes new channels additive.
/// </summary>
internal static class PositionsFile
{
    private const double QuantisationSteps = 65_000; // the larger of the X and Y ranges spans this many steps

    public static byte[] Write(ReplaySession replay)
    {
        int samples = replay.Timeline.SampleCount;
        (float originX, float originY, float scale) = Quantisation(replay);

        List<(RrbEntry Entry, int CarIndex)> blocks = [];
        int entryCount = replay.Cars.Sum(car => car.HasProgress ? 4 : 3);
        int offset = Align(RrbFormat.HeaderSize + (entryCount * RrbFormat.EntrySize));
        for (int c = 0; c < replay.Cars.Count; c++)
        {
            CarChannels car = replay.Cars[c];
            Add(RrbChannel.X, RrbEncoding.Int16Quantised);
            Add(RrbChannel.Y, RrbEncoding.Int16Quantised);
            Add(RrbChannel.Validity, RrbEncoding.Bitmap);
            if (car.HasProgress)
            {
                Add(RrbChannel.Progress, RrbEncoding.Float32);
            }

            void Add(RrbChannel channel, RrbEncoding encoding)
            {
                int length = RrbEntry.BlockLength(encoding, samples);
                blocks.Add((new RrbEntry(car.Driver.Value, channel, encoding, offset, length), c));
                offset = Align(offset + length);
            }
        }

        byte[] file = new byte[offset];
        new RrbHeader(RrbKind.Positions, replay.Cars.Count, samples, replay.Timeline.SampleRateHz, scale, originX, originY,
            RrbFormat.HeaderSize, blocks.Count).Write(file);

        for (int i = 0; i < blocks.Count; i++)
        {
            (RrbEntry entry, int c) = blocks[i];
            entry.Write(file.AsSpan(RrbFormat.HeaderSize + (i * RrbFormat.EntrySize)));
            Span<byte> block = file.AsSpan(entry.Offset, entry.Length);
            CarChannels car = replay.Cars[c];
            switch (entry.Channel)
            {
                case RrbChannel.X:
                    WriteQuantised(block, car.X.Span, car.Valid, originX, scale);
                    break;
                case RrbChannel.Y:
                    WriteQuantised(block, car.Y.Span, car.Valid, originY, scale);
                    break;
                case RrbChannel.Validity:
                    car.Valid.Bits.Span.CopyTo(block);
                    ClearPadding(block, samples);
                    break;
                default:
                    ReadOnlySpan<float> progress = car.Progress.Span;
                    for (int k = 0; k < samples; k++)
                    {
                        BinaryPrimitives.WriteSingleLittleEndian(block[(k * 4)..], progress[k]);
                    }

                    break;
            }
        }

        return file;
    }

    /// <exception cref="BundleFormatException">The file is malformed or doesn't match the manifest.</exception>
    public static IReadOnlyDictionary<DriverNumber, PositionChannels> Read(
        ReadOnlySpan<byte> file, IReadOnlyList<DriverNumber> drivers, int expectedSamples, double expectedRateHz)
    {
        RrbHeader header = RrbHeader.Parse(file, RrbKind.Positions);
        if (header.SampleCount != expectedSamples || header.SampleRateHz != expectedRateHz || header.CarCount != drivers.Count)
        {
            throw new BundleFormatException(
                $"The positions file ({header.CarCount} cars, {header.SampleCount} samples at {header.SampleRateHz} Hz) " +
                $"doesn't match the manifest ({drivers.Count} cars, {expectedSamples} samples at {expectedRateHz} Hz).");
        }

        HashSet<DriverNumber> known = [.. drivers];
        Dictionary<(DriverNumber, RrbChannel), RrbEntry> entries = [];
        for (int i = 0; i < header.EntryCount; i++)
        {
            RrbEntry entry = RrbEntry.Parse(file[(header.DirectoryOffset + (i * RrbFormat.EntrySize))..], file.Length);
            DriverNumber driver = ToDriverNumber(entry.Driver);
            if (!known.Contains(driver))
            {
                throw new BundleFormatException($"The positions file has channels for driver {entry.Driver}, who isn't in the manifest.");
            }

            RrbEncoding expectedEncoding = entry.Channel switch
            {
                RrbChannel.X or RrbChannel.Y => RrbEncoding.Int16Quantised,
                RrbChannel.Validity => RrbEncoding.Bitmap,
                RrbChannel.Progress => RrbEncoding.Float32,
                _ => throw new BundleFormatException($"Unknown positions channel {(ushort)entry.Channel}."),
            };
            if (entry.Encoding != expectedEncoding || entry.Length != RrbEntry.BlockLength(expectedEncoding, header.SampleCount))
            {
                throw new BundleFormatException($"Channel {entry.Channel} of driver {entry.Driver} has the wrong encoding or length.");
            }

            if (!entries.TryAdd((driver, entry.Channel), entry))
            {
                throw new BundleFormatException($"Channel {entry.Channel} of driver {entry.Driver} appears twice.");
            }
        }

        Dictionary<DriverNumber, PositionChannels> cars = new(drivers.Count);
        foreach (DriverNumber driver in drivers)
        {
            RrbEntry x = Required(entries, driver, RrbChannel.X);
            RrbEntry y = Required(entries, driver, RrbChannel.Y);
            RrbEntry valid = Required(entries, driver, RrbChannel.Validity);
            float[] progress = entries.TryGetValue((driver, RrbChannel.Progress), out RrbEntry p)
                ? ReadFloats(file.Slice(p.Offset, p.Length), header.SampleCount)
                : [];

            cars[driver] = new PositionChannels(
                Dequantise(file.Slice(x.Offset, x.Length), header.OriginX, header.Scale),
                Dequantise(file.Slice(y.Offset, y.Length), header.OriginY, header.Scale),
                new ValidityMask(file.Slice(valid.Offset, valid.Length), header.SampleCount),
                progress);
        }

        return cars;
    }

    // Origin at the centre of the observed extent; the larger range spans QuantisationSteps steps (docs/04 §7.3).
    private static (float OriginX, float OriginY, float Scale) Quantisation(ReplaySession replay)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (CarChannels car in replay.Cars)
        {
            ReadOnlySpan<float> xs = car.X.Span, ys = car.Y.Span;
            for (int k = 0; k < xs.Length; k++)
            {
                if (car.Valid[k] && float.IsFinite(xs[k]) && float.IsFinite(ys[k]))
                {
                    minX = Math.Min(minX, xs[k]);
                    maxX = Math.Max(maxX, xs[k]);
                    minY = Math.Min(minY, ys[k]);
                    maxY = Math.Max(maxY, ys[k]);
                }
            }
        }

        if (double.IsInfinity(minX))
        {
            return (0, 0, 1); // no valid sample at all
        }

        double range = Math.Max(maxX - minX, maxY - minY);
        float scale = range > 0 ? (float)(range / QuantisationSteps) : 1;
        return ((float)((minX + maxX) / 2), (float)((minY + maxY) / 2), scale);
    }

    // Invalid samples repeat the last valid value: they carry no meaning, and repeats compress well.
    private static void WriteQuantised(Span<byte> block, ReadOnlySpan<float> values, ValidityMask valid, float origin, float scale)
    {
        short last = 0;
        for (int k = 0; k < values.Length; k++)
        {
            if (valid[k] && float.IsFinite(values[k]))
            {
                double q = Math.Round((values[k] - (double)origin) / scale, MidpointRounding.ToEven);
                last = (short)Math.Clamp(q, -short.MaxValue, short.MaxValue);
            }

            BinaryPrimitives.WriteInt16LittleEndian(block[(k * 2)..], last);
        }
    }

    private static float[] Dequantise(ReadOnlySpan<byte> block, float origin, float scale)
    {
        float[] values = new float[block.Length / 2];
        for (int k = 0; k < values.Length; k++)
        {
            values[k] = (float)(origin + (BinaryPrimitives.ReadInt16LittleEndian(block[(k * 2)..]) * (double)scale));
        }

        return values;
    }

    private static float[] ReadFloats(ReadOnlySpan<byte> block, int samples)
    {
        float[] values = new float[samples];
        for (int k = 0; k < samples; k++)
        {
            values[k] = BinaryPrimitives.ReadSingleLittleEndian(block[(k * 4)..]);
        }

        return values;
    }

    // Padding bits carry no meaning, but zeroing them keeps the bytes a pure function of the samples.
    private static void ClearPadding(Span<byte> bitmap, int samples)
    {
        int remainder = samples & 7;
        if (remainder > 0)
        {
            bitmap[^1] &= (byte)((1 << remainder) - 1);
        }
    }

    private static RrbEntry Required(Dictionary<(DriverNumber, RrbChannel), RrbEntry> entries, DriverNumber driver, RrbChannel channel) =>
        entries.TryGetValue((driver, channel), out RrbEntry entry)
            ? entry
            : throw new BundleFormatException($"Driver {driver} has no {channel} channel.");

    private static DriverNumber ToDriverNumber(int value) =>
        value is >= DriverNumber.MinValue and <= DriverNumber.MaxValue
            ? new DriverNumber(value)
            : throw new BundleFormatException($"Driver number {value} is out of range.");

    private static int Align(int offset) => (offset + RrbFormat.BlockAlignment - 1) & ~(RrbFormat.BlockAlignment - 1);
}